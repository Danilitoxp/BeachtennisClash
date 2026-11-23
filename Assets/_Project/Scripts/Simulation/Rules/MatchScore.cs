using System;

namespace BeachTennis.Simulation.Rules
{
    /// <summary>Identifies a player: team 0/1, index 0/1 inside the team.</summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public readonly int Team;
        public readonly int Index;

        public PlayerId(int team, int index)
        {
            Team = team;
            Index = index;
        }

        public bool Equals(PlayerId other) => Team == other.Team && Index == other.Index;
        public override bool Equals(object obj) => obj is PlayerId p && Equals(p);
        public override int GetHashCode() => Team * 2 + Index;
        public override string ToString() => $"T{Team}P{Index}";
    }

    /// <summary>What a point changed, so the match flow and UI can react.</summary>
    public struct PointOutcome
    {
        public int PointWinner;
        public bool GameWon;
        public bool SetWon;
        public bool MatchWon;
        public bool TieBreakStarted;
        public bool ChangeEnds;
    }

    /// <summary>
    /// Complete scoring state of a beach tennis match (ITF Rules 2026, R6–R11, R15): points, No-Ad games,
    /// tie-break sets, match tie-break, service order in singles and doubles, and change of ends.
    /// Pure logic: no physics, no time; feed it the winner of each point.
    /// </summary>
    public sealed class MatchScore
    {
        public const int MaxSets = 5;

        public RuleSet Rules { get; }
        public int PlayersPerTeam { get; }

        public int[] SetsWon { get; } = new int[2];
        /// <summary>Games in the current set.</summary>
        public int[] Games { get; } = new int[2];
        /// <summary>Points in the current game, or in the current tie-break.</summary>
        public int[] Points { get; } = new int[2];

        public bool InTieBreak { get; private set; }
        public bool InMatchTieBreak { get; private set; }
        public int Winner { get; private set; } = -1;
        public bool IsOver => Winner >= 0;

        /// <summary>Team currently playing on the south half (z &lt; 0).</summary>
        public int SouthTeam { get; private set; }

        /// <summary>Finished sets: games (or match tie-break points) per team.</summary>
        public int CompletedSets { get; private set; }
        private readonly int[,] _setHistory = new int[MaxSets, 2];
        private readonly bool[] _setWasMatchTieBreak = new bool[MaxSets];

        // Service rotation: [A0, B0, A1, B1] in doubles, [A0, B0] in singles (R15).
        private readonly PlayerId[] _order;
        private int _serveIndex;
        private int _tieBreakStartIndex;
        private int _tieBreakPointsPlayed;

        public MatchScore(in RuleSet rules, int playersPerTeam, int firstServingTeam, int southTeam)
        {
            if (playersPerTeam < 1 || playersPerTeam > 2) throw new ArgumentOutOfRangeException(nameof(playersPerTeam));
            Rules = rules;
            PlayersPerTeam = playersPerTeam;
            SouthTeam = southTeam;

            int other = 1 - firstServingTeam;
            _order = playersPerTeam == 1
                ? new[] { new PlayerId(firstServingTeam, 0), new PlayerId(other, 0) }
                : new[] { new PlayerId(firstServingTeam, 0), new PlayerId(other, 0), new PlayerId(firstServingTeam, 1), new PlayerId(other, 1) };
        }

        /// <summary>Who serves the next point.</summary>
        public PlayerId Server
        {
            get
            {
                if (!InTieBreak) return _order[_serveIndex];
                // R8b: first point by the player due; then two points each, alternating.
                int k = _tieBreakPointsPlayed;
                int offset = k == 0 ? 0 : (k + 1) / 2;
                return _order[(_tieBreakStartIndex + offset) % _order.Length];
            }
        }

        public int ServingTeam => Server.Team;

        /// <summary>No-Ad deuce: the next point decides the game (R8a).</summary>
        public bool IsDecidingPoint => !InTieBreak && Rules.NoAd && Points[0] == 3 && Points[1] == 3;

        public int SetGames(int set, int team) => _setHistory[set, team];
        public bool SetWasMatchTieBreak(int set) => _setWasMatchTieBreak[set];

        public PointOutcome AwardPoint(int team)
        {
            var o = new PointOutcome { PointWinner = team };
            if (IsOver) return o;
            int other = 1 - team;

            if (InTieBreak)
            {
                Points[team]++;
                _tieBreakPointsPlayed++;
                int target = InMatchTieBreak ? Rules.MatchTieBreakPoints : Rules.TieBreakPoints;
                if (Points[team] >= target && Points[team] - Points[other] >= 2)
                {
                    o.GameWon = true;
                    if (!InMatchTieBreak) Games[team]++;
                    WinSet(team, ref o);
                }
                else if (Rules.TieBreakChangeEvery > 1 && _tieBreakPointsPlayed % Rules.TieBreakChangeEvery == 1)
                {
                    ChangeEnds(ref o); // R11: after the first point, then every N points
                }
                return o;
            }

            Points[team]++;
            bool gameWon = Rules.NoAd
                ? Points[team] >= 4                                   // 40-40 → deciding point
                : Points[team] >= 4 && Points[team] - Points[other] >= 2;
            if (!gameWon) return o;

            o.GameWon = true;
            Games[team]++;
            Points[0] = Points[1] = 0;
            _serveIndex = (_serveIndex + 1) % _order.Length;

            int g = Games[team], og = Games[other];
            if (g >= Rules.GamesPerSet && g - og >= 2)
            {
                WinSet(team, ref o);
            }
            else if (Rules.TieBreakSets && g == Rules.GamesPerSet && og == Rules.GamesPerSet)
            {
                StartTieBreak(matchTieBreak: false);
                o.TieBreakStarted = true;
            }
            else if ((Games[0] + Games[1]) % 2 == 1)
            {
                ChangeEnds(ref o); // R11: after odd games
            }
            return o;
        }

        private void WinSet(int team, ref PointOutcome o)
        {
            o.SetWon = true;
            if (CompletedSets < MaxSets)
            {
                if (InMatchTieBreak)
                {
                    _setHistory[CompletedSets, 0] = Points[0];
                    _setHistory[CompletedSets, 1] = Points[1];
                    _setWasMatchTieBreak[CompletedSets] = true;
                }
                else
                {
                    _setHistory[CompletedSets, 0] = Games[0];
                    _setHistory[CompletedSets, 1] = Games[1];
                }
                CompletedSets++;
            }

            SetsWon[team]++;
            bool endedByTieBreak = InTieBreak;
            int totalGames = Games[0] + Games[1];

            if (SetsWon[team] >= Rules.SetsToWin)
            {
                Winner = team;
                o.MatchWon = true;
                return;
            }

            // R11: change ends at the end of a set with an odd number of games
            // (otherwise the change happens after the first game of the next set).
            if (totalGames % 2 == 1) ChangeEnds(ref o);

            // R8b: whoever served first in the tie-break receives first in the next set.
            if (endedByTieBreak) _serveIndex = (_tieBreakStartIndex + 1) % _order.Length;

            Games[0] = Games[1] = 0;
            Points[0] = Points[1] = 0;
            InTieBreak = false;
            InMatchTieBreak = false;

            if (Rules.MatchTieBreak && SetsWon[0] == Rules.SetsToWin - 1 && SetsWon[1] == Rules.SetsToWin - 1)
            {
                StartTieBreak(matchTieBreak: true);
                o.TieBreakStarted = true;
            }
        }

        private void StartTieBreak(bool matchTieBreak)
        {
            InTieBreak = true;
            InMatchTieBreak = matchTieBreak;
            Points[0] = Points[1] = 0;
            _tieBreakStartIndex = _serveIndex; // the player whose turn it is serves first
            _tieBreakPointsPlayed = 0;
        }

        private void ChangeEnds(ref PointOutcome o)
        {
            SouthTeam = 1 - SouthTeam;
            o.ChangeEnds = true;
        }

        /// <summary>Score of the current game in tennis terms for <paramref name="team"/> ("0", "15", "30", "40").</summary>
        public string PointText(int team)
        {
            if (InTieBreak) return Points[team].ToString();
            int p = Points[team];
            int o = Points[1 - team];
            if (!Rules.NoAd && p >= 3 && o >= 3)
                return p == o ? "40" : (p > o ? "AD" : "40");
            return p switch { 0 => "0", 1 => "15", 2 => "30", _ => "40" };
        }
    }
}
