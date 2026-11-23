using System;

namespace BeachTennis.Presentation.Players
{
    /// <summary>Visual swing timing in simulation seconds; independent of render rate and pause time.</summary>
    public static class ShotContactTiming
    {
        public const float ContactPhase = 0.45f;
        public const double ApproachSeconds = 0.14;
        public const double FollowThroughSeconds = 0.22;

        public static float Phase(double now, double start, double contact)
        {
            if (now <= start && now < contact) return 0f;
            if (now < contact)
                return ContactPhase * Clamp((now - start) / Math.Max(0.001, contact - start));
            return ContactPhase + (1f - ContactPhase) * Clamp((now - contact) / FollowThroughSeconds);
        }

        // Smoothly converge before contact and release afterwards; no frame-based hold/snap.
        public static float ContactWeight(double now, double contact)
        {
            double distance = now < contact
                ? (contact - now) / ApproachSeconds
                : (now - contact) / FollowThroughSeconds;
            float t = 1f - Clamp(distance);
            return t * t * (3f - 2f * t);
        }

        static float Clamp(double value) => (float)Math.Max(0.0, Math.Min(1.0, value));
    }
}

