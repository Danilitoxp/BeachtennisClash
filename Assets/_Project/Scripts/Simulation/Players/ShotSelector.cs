using System;
using BeachTennis.Core;
using BeachTennis.Simulation.Ball;

namespace BeachTennis.Simulation.Players
{
    /// <summary>
    /// Turns "which button + aim + where the ball was met" into a <see cref="ShotRequest"/>, and timing
    /// quality into execution error. All shot tuning for player and AI lives here.
    /// </summary>
    public static class ShotSelector
    {
        /// <summary>
        /// Margin over the tape that a well-timed shot aims for. Real players clear by 30–50 cm; with less,
        /// ordinary timing errors (a few hundredths of a radian) put most balls into the net.
        /// </summary>
        public const float MinNetClearance = 0.35f;
        /// <summary>A smash is struck from above and meant to pass close over the tape; a big margin would turn it into a lob.</summary>
        public const float SmashNetClearance = 0.20f;
        public const float ServeNetClearance = 0.25f;

        public static ShotRequest Build(ShotType type, bool serve, Vec3 contact, float aimX, float aimY,
            int side, in PlayerConfig cfg, in CourtDimensions court, out bool smash, float power = 0.5f)
        {
            power = Clamp(power, 0f, 1f);
            smash = false;
            float aimDepth = (Clamp(aimY, -1f, 1f) + 1f) * 0.5f;           // 0 = short, 1 = deep
            int opponent = -side;
            float rightX = -side;                                            // hitter's right in world X
            float x = Clamp(aimX, -1f, 1f) * (court.HalfWidth - 0.6f) * rightX;

            var r = new ShotRequest
            {
                From = contact,
                EnforceNetClearance = true,
                MinNetClearance = MinNetClearance
            };
            float depth;

            if (serve)
            {
                r.Arc = ShotArc.Drive;
                r.MinNetClearance = ServeNetClearance;
                r.Speed = Lerp(14f, 19f, power);
                r.TopspinRps = 22f;
                r.SidespinRps = 4f;
                depth = Lerp(4.0f, 7.4f, aimDepth);
            }
            else switch (type)
            {
                case ShotType.Lob:
                    r.Arc = ShotArc.Apex;
                    r.ApexHeight = MathF.Max(5.5f, contact.y + 2.5f) + Clamp(aimY, -1f, 1f) * 0.7f;
                    r.TopspinRps = 4f;
                    depth = Lerp(5.8f, 7.6f, aimDepth);
                    break;

                case ShotType.Drop:
                    r.Arc = ShotArc.Apex;
                    r.ApexHeight = MathF.Max(2.0f, contact.y + 0.4f);
                    r.TopspinRps = -10f;
                    depth = Lerp(0.8f, 2.4f, aimDepth);
                    break;

                default: // Main: volley, or smash when met high
                    if (contact.y >= cfg.SmashHeight)
                    {
                        smash = true;
                        r.Arc = ShotArc.Drive;
                        r.MinNetClearance = SmashNetClearance;
                        r.Speed = Lerp(20f, 28f, power);
                        r.TopspinRps = 10f;
                        depth = Lerp(3.0f, 6.5f, aimDepth);
                    }
                    else
                    {
                        r.Arc = ShotArc.Drive;
                        r.Speed = Lerp(11f, 20f, power);
                        r.TopspinRps = 8f;
                        depth = Lerp(3.5f, 7.0f, aimDepth);
                    }
                    break;
            }

            r.Target = new Vec3(x, 0f, opponent * depth);
            return r;
        }

        public static TimingGrade Grade(float timingError, in PlayerConfig cfg)
        {
            float a = MathF.Abs(timingError);
            if (a <= cfg.PerfectWindow) return TimingGrade.Perfect;
            if (a <= cfg.GoodWindow) return TimingGrade.Good;
            return timingError < 0f ? TimingGrade.Early : TimingGrade.Late;
        }

        /// <summary>
        /// Execution error from timing. Early swings pull to the hitter's left and fly long; late swings
        /// push right, lower and slower. Error grows with how far outside the good window the swing was.
        /// Yaw is in the hitter's frame: positive = to the hitter's right (see <see cref="Vec3.RotateAroundY"/>).
        /// </summary>
        public static Vec3 ApplyTimingError(Vec3 velocity, TimingGrade grade, float timingError, bool dive,
            in PlayerConfig cfg, ref DeterministicRandom rng)
        {
            float yawBias = 0f, yawSigma, pitchBias = 0f, pitchSigma, speed;
            float k = Clamp((MathF.Abs(timingError) - cfg.GoodWindow) / 0.12f, 0.3f, 1.5f);

            switch (grade)
            {
                case TimingGrade.Perfect: yawSigma = 0.008f; pitchSigma = 0.004f; speed = 1.04f; break;
                case TimingGrade.Good: yawSigma = 0.025f; pitchSigma = 0.012f; speed = 1.0f; break;
                case TimingGrade.Early:
                    yawBias = -0.07f * k; pitchBias = 0.02f * k; yawSigma = 0.04f; pitchSigma = 0.015f; speed = 0.93f;
                    break;
                default: // Late
                    yawBias = 0.07f * k; pitchBias = -0.012f * k; yawSigma = 0.04f; pitchSigma = 0.015f; speed = 0.9f;
                    break;
            }

            if (dive)
            {
                // A diving player can only push the ball up: a slower, higher defensive ball.
                yawSigma *= 2f;
                pitchSigma *= 2f;
                pitchBias += 0.07f;
                speed *= 0.85f;
            }

            float yaw = yawBias + Triangular(ref rng) * yawSigma;
            float pitch = pitchBias + Triangular(ref rng) * pitchSigma;
            return ShotSolver.ApplyExecutionError(velocity, yaw, pitch, speed);
        }

        /// <summary>Cheap bell-shaped noise in [-1, 1].</summary>
        private static float Triangular(ref DeterministicRandom rng) => rng.NextFloat() - rng.NextFloat();

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
