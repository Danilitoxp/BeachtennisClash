using System;

namespace BeachTennis.AI
{
    public enum Difficulty : byte
    {
        Easy,
        Medium,
        Hard,
        Pro
    }

    /// <summary>
    /// Human-like limits of an AI player. Every number models something a person does, so difficulty is
    /// tuned by making the bot more or less human, never by giving it information a player lacks.
    /// </summary>
    [Serializable]
    public struct AiProfile
    {
        public string Name;
        /// <summary>Seconds after the opponent's hit before the bot starts to move/prepare.</summary>
        public float ReactionTime;
        /// <summary>Standard deviation of the release timing error (s).</summary>
        public float TimingSigma;
        /// <summary>Chance of misjudging a ball and not swinging at all.</summary>
        public float MissChance;
        /// <summary>Random spread added to the chosen aim (aim units, -1..1).</summary>
        public float AimNoise;
        /// <summary>Chance of choosing the best target (open court) instead of a random one.</summary>
        public float TacticalSkill;
        /// <summary>Chance of lobbing when the opponents are at the net.</summary>
        public float LobSense;
        /// <summary>Chance of a drop shot when the opponents are deep.</summary>
        public float DropSense;
        /// <summary>Chance of aiming a smash at the nearest opponent (ball on the body wins the point, R24h).</summary>
        public float BodyAttack;
        /// <summary>Preferred distance from the net between shots (m).</summary>
        public float HomeDepth;
        /// <summary>Fraction of top speed used when repositioning between shots.</summary>
        public float PositioningSpeed;
        /// <summary>Base shot power 0..1 (drives, smashes, serves); raised further on attackable balls.</summary>
        public float Power;

        public static AiProfile For(Difficulty d) => d switch
        {
            Difficulty.Easy => new AiProfile
            {
                Name = "Fácil", ReactionTime = 0.30f, TimingSigma = 0.10f, MissChance = 0.10f, AimNoise = 0.45f,
                TacticalSkill = 0.2f, LobSense = 0.3f, DropSense = 0.15f, BodyAttack = 0f, HomeDepth = 6.0f, PositioningSpeed = 0.6f, Power = 0.35f
            },
            Difficulty.Medium => new AiProfile
            {
                Name = "Médio", ReactionTime = 0.22f, TimingSigma = 0.07f, MissChance = 0.05f, AimNoise = 0.30f,
                TacticalSkill = 0.5f, LobSense = 0.5f, DropSense = 0.25f, BodyAttack = 0.1f, HomeDepth = 5.5f, PositioningSpeed = 0.8f, Power = 0.5f
            },
            Difficulty.Hard => new AiProfile
            {
                Name = "Difícil", ReactionTime = 0.15f, TimingSigma = 0.045f, MissChance = 0.02f, AimNoise = 0.18f,
                TacticalSkill = 0.8f, LobSense = 0.7f, DropSense = 0.35f, BodyAttack = 0.25f, HomeDepth = 5.0f, PositioningSpeed = 0.95f, Power = 0.65f
            },
            _ => new AiProfile
            {
                Name = "Profissional", ReactionTime = 0.10f, TimingSigma = 0.028f, MissChance = 0.008f, AimNoise = 0.10f,
                TacticalSkill = 0.95f, LobSense = 0.85f, DropSense = 0.4f, BodyAttack = 0.35f, HomeDepth = 4.5f, PositioningSpeed = 1.0f, Power = 0.8f
            }
        };
    }
}
