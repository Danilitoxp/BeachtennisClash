using System;

namespace BeachTennis.Simulation.Rules
{
    public enum EventCategory : byte
    {
        Men,
        Women,
        Mixed
    }

    /// <summary>
    /// Match rules as data. Defaults follow the ITF Rules of Beach Tennis 2026; alternatives come from
    /// its Appendix VI. Rule numbers (R#) refer to that document.
    /// </summary>
    [Serializable]
    public struct RuleSet
    {
        public string Name;
        public EventCategory Category;

        /// <summary>Sets needed to win the match (R6: best of 3 → 2).</summary>
        public int SetsToWin;
        /// <summary>Games to win a set (R7: 6; App. VI short sets: 4).</summary>
        public int GamesPerSet;
        /// <summary>A tie-break is played at GamesPerSet all (R7). False = advantage set (App. VI).</summary>
        public bool TieBreakSets;
        /// <summary>Points to win a set tie-break (R8b: 7, margin 2).</summary>
        public int TieBreakPoints;
        /// <summary>At one set all, a match tie-break replaces the deciding set (R7).</summary>
        public bool MatchTieBreak;
        /// <summary>Points to win the match tie-break (R7: 10; App. VI: 7).</summary>
        public int MatchTieBreakPoints;
        /// <summary>No-Ad: at deuce a single deciding point wins the game (R8a).</summary>
        public bool NoAd;
        /// <summary>Change ends after the first point of a tie-break and then every N points (R11: 4; App. VI: 6).</summary>
        public int TieBreakChangeEvery;

        /// <summary>Prohibited service return zone depth from the net (App. I): 3 m women/mixed, 6 m men.</summary>
        public float ProhibitedZone => Category == EventCategory.Men ? 6f : 3f;

        /// <summary>Net height (R1): 1.80 m men, 1.70 m women and mixed.</summary>
        public float NetHeight => Category == EventCategory.Men ? 1.80f : 1.70f;

        /// <summary>Official ITF format: best of 3 tie-break sets, 10-point match tie-break at one set all.</summary>
        public static RuleSet ItfOfficial(EventCategory category) => new RuleSet
        {
            Name = "Oficial ITF",
            Category = category,
            SetsToWin = 2,
            GamesPerSet = 6,
            TieBreakSets = true,
            TieBreakPoints = 7,
            MatchTieBreak = true,
            MatchTieBreakPoints = 10,
            NoAd = true,
            TieBreakChangeEvery = 4
        };

        /// <summary>
        /// Quick mobile match: one short set (App. VI: first to 4 games, tie-break at 4-4).
        /// A one-set match is not an official ITF format; labelled as such in the UI.
        /// </summary>
        public static RuleSet QuickShortSet(EventCategory category) => new RuleSet
        {
            Name = "Rápida (1 set curto)",
            Category = category,
            SetsToWin = 1,
            GamesPerSet = 4,
            TieBreakSets = true,
            TieBreakPoints = 7,
            MatchTieBreak = false,
            MatchTieBreakPoints = 10,
            NoAd = true,
            TieBreakChangeEvery = 4
        };

        /// <summary>Best of 3 short sets with a 7-point match tie-break (all App. VI options).</summary>
        public static RuleSet ShortSets(EventCategory category) => new RuleSet
        {
            Name = "Sets curtos",
            Category = category,
            SetsToWin = 2,
            GamesPerSet = 4,
            TieBreakSets = true,
            TieBreakPoints = 7,
            MatchTieBreak = true,
            MatchTieBreakPoints = 7,
            NoAd = true,
            TieBreakChangeEvery = 4
        };
    }
}
