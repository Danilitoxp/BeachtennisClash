using System;
using BeachTennis.Core;

namespace BeachTennis.Simulation.Ball
{
    /// <summary>
    /// Pure, stateless ball physics. One function (<see cref="Step"/>) moves the ball for one fixed tick;
    /// the live simulation, trajectory prediction, the shot solver and tests all call it, so a prediction
    /// is exactly what will happen (docs/01 §2.3).
    ///
    /// Model: gravity + quadratic drag + Magnus lift (Cl = 1 / (2 + v/(r·ω))), integrated with RK2
    /// (midpoint). Collisions are analytic and swept along the tick's segment, so fast balls never tunnel:
    /// - Net: plane z = 0 between the posts. Center crossing below the tape = net body (ball stopped),
    ///   within one radius above the tape = net cord (ball continues, slowed and kicked up).
    /// - Sand: plane y = radius. Low restitution, Coulomb friction, then rolling until rest.
    /// </summary>
    public static class BallPhysics
    {
        /// <summary>Acceleration acting on the ball in flight.</summary>
        public static Vec3 Acceleration(Vec3 velocity, Vec3 spin, in BallPhysicsConfig c)
        {
            var gravity = new Vec3(0f, -c.Gravity, 0f);
            float speed = velocity.Magnitude;
            if (speed < 1e-6f || c.AirDensity <= 0f) return gravity;

            float k = c.AeroFactor;
            Vec3 drag = velocity * (-k * c.DragCoefficient * speed);

            float spinRate = spin.Magnitude;
            if (spinRate < 1e-6f || c.MagnusScale <= 0f) return gravity + drag;

            // Lift coefficient from the spin ratio S = r·ω / v: Cl = S / (2S + 1) = 1 / (2 + v/(r·ω)).
            float s = c.Radius * spinRate / speed;
            float cl = s / (2f * s + 1f);
            // Direction ω̂ × v̂ (its length is sin of the angle between spin axis and velocity).
            Vec3 dir = Vec3.Cross(spin, velocity) / (spinRate * speed);
            Vec3 magnus = dir * (k * cl * speed * speed * c.MagnusScale);

            return gravity + drag + magnus;
        }

        /// <summary>
        /// Advances the ball by one fixed tick. <paramref name="events"/> may be null (prediction).
        /// </summary>
        public static void Step(ref BallState s, in BallPhysicsConfig c, in CourtDimensions court, float dt,
            BallEventBuffer events = null, long tick = 0, bool collideNet = true)
        {
            switch (s.Mode)
            {
                case BallMode.Flight:
                    StepFlight(ref s, c, court, dt, events, tick, collideNet);
                    break;
                case BallMode.Rolling:
                    StepRolling(ref s, c, dt, events, tick);
                    break;
            }
        }

        private static void StepFlight(ref BallState s, in BallPhysicsConfig c, in CourtDimensions court, float dt,
            BallEventBuffer events, long tick, bool collideNet)
        {
            Vec3 p0 = s.Position;
            Vec3 v0 = s.Velocity;
            Vec3 w = s.Spin;

            // RK2 midpoint.
            Vec3 a0 = Acceleration(v0, w, c);
            Vec3 vMid = v0 + a0 * (0.5f * dt);
            Vec3 aMid = Acceleration(vMid, w, c);
            Vec3 v1 = v0 + aMid * dt;
            Vec3 p1 = p0 + vMid * dt;

            float decay = 1f - c.SpinDecayPerSecond * dt;
            w = w * (decay > 0f ? decay : 0f);
            s.FlightTime += dt;

            if (collideNet)
                ResolveNet(ref p1, ref v1, ref w, ref s, p0, v0, c, court, events, tick);

            float r = c.Radius;
            if (p1.y < r && v1.y < 0f)
            {
                // Swept contact with the sand plane y = r.
                float denom = p0.y - p1.y;
                float f = denom > 1e-9f ? Clamp01((p0.y - r) / denom) : 0f;
                Vec3 pc = Vec3.Lerp(p0, p1, f);
                Vec3 vc = Vec3.Lerp(v0, v1, f);

                events?.Add(new BallEvent
                {
                    Type = BallEventType.SandContact,
                    Position = new Vec3(pc.x, 0f, pc.z),
                    Velocity = vc,
                    Tick = tick
                });
                s.BounceCount++;

                float vn = -vc.y;                        // impact speed into the sand (positive)
                float vyOut = c.SandRestitution * vn;
                Vec3 vt = vc.Horizontal;
                float tMag = vt.Magnitude;
                if (tMag > 1e-6f)
                {
                    float dv = MathF.Min(tMag, c.SandFriction * (1f + c.SandRestitution) * vn);
                    vt = vt * ((tMag - dv) / tMag);
                }
                w = w * c.SandSpinRetention;

                p1 = new Vec3(pc.x, r, pc.z);
                if (vyOut < c.BounceToRollSpeed)
                {
                    v1 = vt;
                    s.Mode = BallMode.Rolling;
                }
                else
                {
                    v1 = new Vec3(vt.x, vyOut, vt.z);
                }
            }

            s.Position = p1;
            s.Velocity = v1;
            s.Spin = w;
        }

        private static void ResolveNet(ref Vec3 p1, ref Vec3 v1, ref Vec3 w, ref BallState s,
            Vec3 p0, Vec3 v0, in BallPhysicsConfig c, in CourtDimensions court, BallEventBuffer events, long tick)
        {
            bool crossed = (p0.z > 0f && p1.z <= 0f) || (p0.z < 0f && p1.z >= 0f);
            if (!crossed) return;

            float f = Clamp01(p0.z / (p0.z - p1.z));
            Vec3 pc = Vec3.Lerp(p0, p1, f);
            Vec3 vc = Vec3.Lerp(v0, v1, f);
            float halfSpan = court.HalfWidth + court.NetPostOffset;
            bool withinPosts = MathF.Abs(pc.x) <= halfSpan;

            if (withinPosts && pc.y < court.NetHeight)
            {
                // Net body: the ball stays on its side, losing almost all energy.
                float side = p0.z >= 0f ? 1f : -1f;
                p1 = new Vec3(pc.x, pc.y, side * c.Radius);
                v1 = new Vec3(vc.x * c.NetDamping, vc.y * c.NetDamping, -vc.z * c.NetRestitution);
                w = w * 0.2f;
                events?.Add(new BallEvent { Type = BallEventType.NetBody, Position = pc, Velocity = vc, Tick = tick });
                return;
            }

            if (withinPosts && pc.y < court.NetHeight + c.Radius)
            {
                // Net cord: deeper contact (lower ball) = more speed lost and bigger kick up.
                float depth = 1f - (pc.y - court.NetHeight) / c.Radius; // 0 = grazing, 1 = center at tape
                float forward = MathF.Abs(vc.z);
                v1 = new Vec3(v1.x, v1.y + c.NetCordLift * depth * forward, v1.z * (1f - c.NetCordSpeedLoss * depth));
                events?.Add(new BallEvent { Type = BallEventType.NetCord, Position = pc, Velocity = vc, Tick = tick });
            }

            s.NetCrossings++;
            events?.Add(new BallEvent { Type = BallEventType.CrossedNet, Position = pc, Velocity = vc, Tick = tick });
        }

        private static void StepRolling(ref BallState s, in BallPhysicsConfig c, float dt, BallEventBuffer events, long tick)
        {
            Vec3 v = s.Velocity.Horizontal;
            float speed = v.Magnitude;
            float dec = c.RollingDeceleration * dt;

            if (speed <= dec || speed < c.RestSpeed)
            {
                s.Velocity = Vec3.Zero;
                s.Spin = Vec3.Zero;
                s.Mode = BallMode.Resting;
                events?.Add(new BallEvent { Type = BallEventType.Rest, Position = s.Position, Velocity = Vec3.Zero, Tick = tick });
                return;
            }

            v = v * ((speed - dec) / speed);
            s.Velocity = v;
            s.Position = new Vec3(s.Position.x + v.x * dt, c.Radius, s.Position.z + v.z * dt);
            s.Spin = s.Spin * 0.9f;
        }

        /// <summary>
        /// Writes the future states of the ball into <paramref name="output"/> (one per tick) and returns
        /// how many were written. Stops at the first sand contact when <paramref name="stopAtSand"/> is set,
        /// when the ball stops moving, or when the buffer is full. Never allocates.
        /// </summary>
        public static int Predict(BallState start, in BallPhysicsConfig c, in CourtDimensions court, float dt,
            BallState[] output, bool stopAtSand = true)
        {
            var s = start;
            int startBounces = s.BounceCount;
            int n = 0;
            while (n < output.Length && (s.Mode == BallMode.Flight || s.Mode == BallMode.Rolling))
            {
                Step(ref s, c, court, dt);
                output[n++] = s;
                if (stopAtSand && s.BounceCount != startBounces) break;
            }
            return n;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
