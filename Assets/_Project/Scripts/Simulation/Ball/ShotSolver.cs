using System;
using BeachTennis.Core;

namespace BeachTennis.Simulation.Ball
{
    public enum ShotArc : byte
    {
        /// <summary>Defined by the height of the trajectory peak: lobs, serves, soft volleys, drops.</summary>
        Apex,
        /// <summary>Defined by horizontal speed: smashes, fast volleys, drives.</summary>
        Drive
    }

    /// <summary>What the player wants: where the ball should land and how it should travel.</summary>
    public struct ShotRequest
    {
        public Vec3 From;
        /// <summary>Desired landing point on the sand (Y ignored).</summary>
        public Vec3 Target;
        public ShotArc Arc;
        /// <summary><see cref="ShotArc.Apex"/>: absolute height of the trajectory peak, meters.</summary>
        public float ApexHeight;
        /// <summary><see cref="ShotArc.Drive"/>: horizontal speed at contact, m/s.</summary>
        public float Speed;
        public float TopspinRps;
        public float SidespinRps;

        /// <summary>
        /// When set, a shot that would not pass at least <see cref="MinNetClearance"/> over the tape is
        /// re-solved as an arc, raising the apex until it clears (what a player instinctively does with
        /// a ball below net height).
        /// </summary>
        public bool EnforceNetClearance;
        public float MinNetClearance;
    }

    public struct ShotSolution
    {
        public Vec3 Velocity;
        public Vec3 Spin;
        /// <summary>True when the arc had to be raised to clear the net (see <see cref="ShotRequest.EnforceNetClearance"/>).</summary>
        public bool ArcRaised;
        /// <summary>Where the solved shot touches the sand (ignoring the net).</summary>
        public Vec3 Landing;
        public float FlightTime;
        public float LandingError;
        public bool Converged;
        public int Iterations;

        public bool CrossesNetPlane;
        /// <summary>
        /// Ball center height at the net minus (net height + radius): ≥ 0 clears cleanly,
        /// (-radius, 0) clips the tape, ≤ -radius hits the net body. +∞ when it passes outside the posts.
        /// </summary>
        public float NetClearance;

        public bool ClearsNet => !CrossesNetPlane || NetClearance >= 0f;
    }

    /// <summary>
    /// Intent-driven shot model (docs/01 §2.3): instead of colliding a racket with the ball, the hit
    /// solves for the launch velocity that makes the ball land on the requested target, under the same
    /// drag and Magnus physics as the live ball (shooting method, bounded iterations, no allocations).
    /// Execution error from bad timing is applied afterwards with <see cref="ApplyExecutionError"/>.
    ///
    /// Cost control: iterations first run with a 4× coarser time step, then refine at the real tick,
    /// so a hit costs a fraction of a millisecond even on mid-range phones.
    /// </summary>
    public static class ShotSolver
    {
        public const int DefaultMaxIterations = 12;
        public const float DefaultTolerance = 0.02f;
        private const float MaxFlightSeconds = 8f;
        private const int CoarseFactor = 4;
        private const int FineIterations = 4;
        private const float ArcRaiseStep = 0.25f;
        private const int MaxArcRaises = 24;

        public static ShotSolution Solve(in ShotRequest request, in BallPhysicsConfig c, in CourtDimensions court,
            float dt, int maxIterations = DefaultMaxIterations, float tolerance = DefaultTolerance)
        {
            ShotSolution solution = request.Arc == ShotArc.Apex
                ? SolveApex(request, request.ApexHeight, c, court, dt, maxIterations, tolerance)
                : SolveDrive(request, c, court, dt, maxIterations, tolerance);

            if (!request.EnforceNetClearance || !solution.CrossesNetPlane || solution.NetClearance >= request.MinNetClearance)
                return solution;

            // Raise the arc until the ball clears the tape by the requested margin.
            float apex = MathF.Max(request.From.y, court.NetHeight) + request.MinNetClearance + c.Radius + 0.1f;
            if (request.Arc == ShotArc.Apex) apex = MathF.Max(apex, request.ApexHeight + ArcRaiseStep);

            for (int i = 0; i < MaxArcRaises; i++, apex += ArcRaiseStep)
            {
                solution = SolveApex(request, apex, c, court, dt, maxIterations, tolerance);
                solution.ArcRaised = true;
                if (!solution.CrossesNetPlane || solution.NetClearance >= request.MinNetClearance) break;
            }
            return solution;
        }

        private static ShotSolution SolveApex(in ShotRequest request, float apexHeight, in BallPhysicsConfig c,
            in CourtDimensions court, float dt, int maxIterations, float tolerance)
        {
            Prepare(request, c, out Vec3 dir, out float distance, out Vec3 spin);
            float g = c.Gravity;
            float apex = MathF.Max(apexHeight, request.From.y + 0.05f);
            float vy = MathF.Sqrt(2f * g * (apex - request.From.y));
            float time = vy / g + MathF.Sqrt(2f * MathF.Max(apex - c.Radius, 0.01f) / g);
            return Iterate(request, dir * (distance / time), vy, spin, c, court, dt, maxIterations, tolerance);
        }

        private static ShotSolution SolveDrive(in ShotRequest request, in BallPhysicsConfig c,
            in CourtDimensions court, float dt, int maxIterations, float tolerance)
        {
            Prepare(request, c, out Vec3 dir, out float distance, out Vec3 spin);
            float g = c.Gravity;
            float vh = MathF.Max(request.Speed, 1f);
            float time = MathF.Max(distance / vh, 0.05f);
            float vy = (c.Radius - request.From.y + 0.5f * g * time * time) / time;
            return Iterate(request, dir * vh, vy, spin, c, court, dt, maxIterations, tolerance);
        }

        private static void Prepare(in ShotRequest request, in BallPhysicsConfig c, out Vec3 dir, out float distance, out Vec3 spin)
        {
            var target = new Vec3(request.Target.x, 0f, request.Target.z);
            Vec3 delta = target - request.From.Horizontal;
            distance = delta.Magnitude;
            dir = distance > 0.01f ? delta / distance : Vec3.Forward;
            spin = BallSpin.FromComponents(dir, request.TopspinRps, request.SidespinRps);
        }

        /// <summary>
        /// Shooting method on the horizontal velocity with fixed vertical velocity: coarse passes first,
        /// then refinement at the real tick. The returned solution always reflects a fine-step simulation.
        /// </summary>
        private static ShotSolution Iterate(in ShotRequest request, Vec3 vHorizontal, float vy, Vec3 spin,
            in BallPhysicsConfig c, in CourtDimensions court, float dt, int maxIterations, float tolerance)
        {
            Vec3 from = request.From;
            var target = new Vec3(request.Target.x, 0f, request.Target.z);
            var solution = new ShotSolution { Spin = spin };
            float coarseDt = dt * CoarseFactor;

            for (int i = 0; i < maxIterations; i++)
            {
                var velocity = new Vec3(vHorizontal.x, vy, vHorizontal.z);
                Simulate(BallState.Launch(from, velocity, spin), c, court, coarseDt, out Vec3 landing, out float time, out _, out _);
                solution.Iterations++;
                Vec3 error = target - landing.Horizontal;
                if (error.Magnitude <= tolerance) break;
                // Range is monotonic in horizontal speed for a fixed vertical speed: proportional correction.
                vHorizontal = vHorizontal + error / MathF.Max(time, 0.1f);
            }

            for (int i = 0; i < FineIterations; i++)
            {
                var velocity = new Vec3(vHorizontal.x, vy, vHorizontal.z);
                Simulate(BallState.Launch(from, velocity, spin), c, court, dt, out Vec3 landing, out float time,
                    out solution.CrossesNetPlane, out solution.NetClearance);
                solution.Iterations++;
                solution.Velocity = velocity;
                solution.Landing = landing;
                solution.FlightTime = time;

                Vec3 error = target - landing.Horizontal;
                solution.LandingError = error.Magnitude;
                if (solution.LandingError <= tolerance)
                {
                    solution.Converged = true;
                    break;
                }
                vHorizontal = vHorizontal + error / MathF.Max(time, 0.1f);
            }

            if (!solution.CrossesNetPlane) solution.NetClearance = float.PositiveInfinity;
            return solution;
        }

        /// <summary>
        /// Perturbs a solved launch velocity: yaw/pitch errors in radians and a speed multiplier.
        /// Used to turn timing quality into realistic misses.
        /// </summary>
        public static Vec3 ApplyExecutionError(Vec3 velocity, float yawRadians, float pitchRadians, float speedScale)
        {
            Vec3 horizontal = Vec3.RotateAroundY(velocity.Horizontal, yawRadians);
            float h = horizontal.Magnitude;
            float speed = velocity.Magnitude * speedScale;
            float pitch = MathF.Atan2(velocity.y, h) + pitchRadians;

            Vec3 dir = h > 1e-6f ? horizontal / h : Vec3.Forward;
            float newH = speed * MathF.Cos(pitch);
            return new Vec3(dir.x * newH, speed * MathF.Sin(pitch), dir.z * newH);
        }

        /// <summary>
        /// Flies the ball (net disabled) until it first touches the sand. Reports landing, time and the
        /// clearance over the net at the moment the center crosses z = 0.
        /// </summary>
        private static void Simulate(BallState s, in BallPhysicsConfig c, in CourtDimensions court, float dt,
            out Vec3 landing, out float time, out bool crossedNet, out float netClearance)
        {
            crossedNet = false;
            netClearance = float.PositiveInfinity;
            float halfSpan = court.HalfWidth + court.NetPostOffset;
            int maxSteps = (int)(MaxFlightSeconds / dt);

            for (int i = 0; i < maxSteps; i++)
            {
                Vec3 p0 = s.Position;
                BallPhysics.Step(ref s, c, court, dt, null, 0, collideNet: false);
                Vec3 p1 = s.Position;

                if (!crossedNet && ((p0.z > 0f && p1.z <= 0f) || (p0.z < 0f && p1.z >= 0f)))
                {
                    crossedNet = true;
                    float f = p0.z / (p0.z - p1.z);
                    Vec3 pc = Vec3.Lerp(p0, p1, f);
                    if (MathF.Abs(pc.x) <= halfSpan)
                        netClearance = pc.y - (court.NetHeight + c.Radius);
                }

                if (s.BounceCount > 0)
                {
                    // Position after the bounce is at the contact point (see BallPhysics.StepFlight).
                    landing = new Vec3(s.Position.x, 0f, s.Position.z);
                    time = s.FlightTime;
                    return;
                }
            }

            landing = new Vec3(s.Position.x, 0f, s.Position.z);
            time = s.FlightTime;
        }
    }
}
