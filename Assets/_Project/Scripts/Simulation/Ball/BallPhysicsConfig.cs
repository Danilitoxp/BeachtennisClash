using System;

namespace BeachTennis.Simulation.Ball
{
    /// <summary>
    /// Physical parameters of the beach tennis ball and its interaction with air, sand and net.
    /// Plain data so tests, the simulation and the Inspector (via a ScriptableObject wrapper) share it.
    ///
    /// Defaults: ITF stage-2 (depressurized) ball, ~6.7 cm diameter, ~57.5 g. Drag and lift follow
    /// the usual tennis-ball models (Cd ≈ 0.55, Cl = 1 / (2 + v / (r·ω))). Sand values are tuned
    /// for feel: the ball should die quickly in soft sand.
    /// </summary>
    [Serializable]
    public struct BallPhysicsConfig
    {
        public float Radius;            // m
        public float Mass;              // kg
        public float Gravity;           // m/s² (positive, applied downwards)
        public float AirDensity;        // kg/m³
        public float DragCoefficient;   // Cd
        public float MagnusScale;       // multiplier on the lift model (1 = physical)
        public float SpinDecayPerSecond;// fraction of spin lost per second in flight

        public float SandRestitution;   // vertical bounce coefficient (0..1)
        public float SandFriction;      // Coulomb friction during a bounce
        public float SandSpinRetention; // fraction of spin kept after a bounce
        public float RollingDeceleration;// m/s² while rolling on sand
        public float RestSpeed;         // below this the ball stops (m/s)
        public float BounceToRollSpeed; // vertical speed under which a bounce becomes rolling

        public float NetRestitution;    // ball hitting the net body
        public float NetDamping;        // fraction of x/y velocity kept after hitting the net body
        public float NetCordSpeedLoss;  // fraction of forward speed lost when clipping the tape
        public float NetCordLift;       // vertical kick from clipping the tape, relative to forward speed

        public float CrossSectionArea => MathF.PI * Radius * Radius;

        /// <summary>0.5·ρ·A / m, shared factor of drag and lift accelerations.</summary>
        public float AeroFactor => 0.5f * AirDensity * CrossSectionArea / Mass;

        public static BallPhysicsConfig Default => new BallPhysicsConfig
        {
            Radius = 0.0335f,
            Mass = 0.0575f,
            Gravity = 9.81f,
            AirDensity = 1.2f,
            DragCoefficient = 0.55f,
            MagnusScale = 1f,
            SpinDecayPerSecond = 0.05f,

            SandRestitution = 0.30f,
            SandFriction = 0.60f,
            SandSpinRetention = 0.25f,
            RollingDeceleration = 4f,
            RestSpeed = 0.05f,
            BounceToRollSpeed = 0.35f,

            NetRestitution = 0.10f,
            NetDamping = 0.30f,
            NetCordSpeedLoss = 0.25f,
            NetCordLift = 0.30f
        };

        /// <summary>Vacuum, no spin effects: used by tests to compare against analytic ballistics.</summary>
        public static BallPhysicsConfig Vacuum
        {
            get
            {
                var c = Default;
                c.AirDensity = 0f;
                c.SpinDecayPerSecond = 0f;
                return c;
            }
        }
    }
}
