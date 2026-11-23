using System;

namespace BeachTennis.Simulation.Players
{
    /// <summary>
    /// Movement, reach and timing parameters of a player. Plain data (Inspector-editable through a
    /// ScriptableObject wrapper); later per-character attributes scale these values.
    /// </summary>
    [Serializable]
    public struct PlayerConfig
    {
        // --- Movimento na areia
        public float MaxSpeed;            // m/s (sand is slower than a hard court)
        public float Acceleration;        // m/s²
        public float Braking;             // m/s² when stopping or reversing
        public float SplitStepBoost;      // acceleration multiplier right after the opponent's hit
        public float SplitStepDuration;   // s
        public float SplitStepMaxSpeed;   // must be almost still (m/s) to get the split-step

        // --- Alcance
        public float ReachRadius;         // horizontal distance body→ball that can be hit (m)
        public float IdealContactOffset;  // where the assist places the ball relative to the body (m, to the side)
        public float ReachHeightMin;      // m
        public float ReachHeightMax;      // m (with jump/stretch)
        public float SmashHeight;         // contact above this turns the main shot into a smash
        public float DiveExtraReach;      // m beyond ReachRadius
        public float DiveRecovery;        // s on the sand after a dive

        // --- Timing (segundos)
        public float SwingLead;           // ideal release this long before contact
        public float PerfectWindow;
        public float GoodWindow;
        public float WhiffEarlyLimit;     // releasing earlier than this = swing and miss
        public float HeldAtContactError;  // treated as this late if still holding at contact
        public float SwingCooldown;       // s after a whiff before a new swing

        public static PlayerConfig Default => new PlayerConfig
        {
            MaxSpeed = 5.0f,
            Acceleration = 9f,
            Braking = 14f,
            SplitStepBoost = 1.35f,
            SplitStepDuration = 0.3f,
            SplitStepMaxSpeed = 1.0f,

            ReachRadius = 1.0f,
            IdealContactOffset = 0.6f,
            ReachHeightMin = 0.25f,
            ReachHeightMax = 2.95f,
            SmashHeight = 2.25f,
            DiveExtraReach = 1.0f,
            DiveRecovery = 1.3f, // = dive (0.55 s) + get up (0.75 s) as animated

            SwingLead = 0.10f,
            PerfectWindow = 0.035f,
            GoodWindow = 0.085f,
            WhiffEarlyLimit = 0.45f,
            HeldAtContactError = 0.12f,
            SwingCooldown = 0.35f
        };

        /// <summary>Distance a player starting from rest can cover in <paramref name="time"/> seconds.</summary>
        public float TravelDistance(float time, float accelMultiplier = 1f)
        {
            if (time <= 0f) return 0f;
            float a = Acceleration * accelMultiplier;
            float tMax = MaxSpeed / a;
            return time < tMax ? 0.5f * a * time * time : MaxSpeed * time - MaxSpeed * MaxSpeed / (2f * a);
        }
    }
}
