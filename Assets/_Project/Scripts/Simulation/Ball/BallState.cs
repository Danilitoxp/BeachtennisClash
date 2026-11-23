using BeachTennis.Core;

namespace BeachTennis.Simulation.Ball
{
    public enum BallMode : byte
    {
        /// <summary>Not in play (held for serve, between points).</summary>
        Inactive,
        Flight,
        Rolling,
        Resting
    }

    /// <summary>Complete, copyable state of the ball. Copying it is how prediction and interpolation work.</summary>
    public struct BallState
    {
        public Vec3 Position;
        public Vec3 Velocity;
        /// <summary>Angular velocity, rad/s (axis × rate). See <see cref="BallSpin"/> for helpers.</summary>
        public Vec3 Spin;
        public BallMode Mode;
        /// <summary>Times the ball touched the sand since the last launch.</summary>
        public int BounceCount;
        /// <summary>Times the ball crossed the net plane since the last launch.</summary>
        public int NetCrossings;
        /// <summary>Simulated seconds since the last launch.</summary>
        public float FlightTime;

        public static BallState Launch(Vec3 position, Vec3 velocity, Vec3 spin) => new BallState
        {
            Position = position,
            Velocity = velocity,
            Spin = spin,
            Mode = BallMode.Flight
        };
    }

    /// <summary>
    /// Builds angular velocity from tennis terms relative to the shot direction.
    /// Topspin makes the ball dip, backspin (negative topspin) makes it float.
    /// Positive sidespin curves the ball to the right of its travel direction.
    /// </summary>
    public static class BallSpin
    {
        public const float RpsToRadPerSec = 6.2831853f;

        public static Vec3 FromComponents(Vec3 travelDirection, float topspinRps, float sidespinRps)
        {
            Vec3 forward = travelDirection.Horizontal.Normalized;
            if (forward.SqrMagnitude < 1e-6f) forward = Vec3.Forward;

            // Magnus force ∝ ω × v.
            // Topspin axis = Up × forward (the travel direction's right): top of the ball moves forward,
            // ω × v points down → the ball dips.
            Vec3 topAxis = Vec3.Cross(Vec3.Up, forward);
            // Sidespin axis = Up: Up × forward = right → the ball curves right.
            return topAxis * (topspinRps * RpsToRadPerSec) + Vec3.Up * (sidespinRps * RpsToRadPerSec);
        }
    }
}
