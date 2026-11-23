using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using BeachTennis.Simulation.Rules;

namespace BeachTennis.AI
{
    /// <summary>
    /// Plays AI-vs-AI points with the real simulation, no rendering, as fast as the CPU allows.
    /// Used to calibrate and verify difficulty levels and doubles coordination objectively.
    /// </summary>
    public sealed class HeadlessMatch
    {
        public const float Dt = 1f / 120f;
        private const int MaxTicksPerPoint = 120 * 40;

        public readonly BallSystem Ball;
        public readonly PlayerSystem[] Players;
        public readonly PlayerId[] Ids;
        public readonly AiPlayer[] Ais;
        public readonly MatchScore Score;
        public readonly PointReferee Referee;

        public int PointsPlayed { get; private set; }
        public readonly int[] PointsWon = new int[2];
        public readonly int[] Reasons = new int[16];
        public long TotalHits { get; private set; }
        public long TotalTicks { get; private set; }

        // Diagnostics: the last hit of every point, bucketed by how the point ended.
        public readonly int[] HitsByGrade = new int[4];
        public readonly int[] NetByGrade = new int[4];
        public readonly int[] NetByShot = new int[3];
        public int NetSmashes, NetServes, NetFromBelowTape, NetDives, Dives;
        private PlayerEvent _lastHit;
        private bool _lastHitWasServe;
        private int _hitsInPoint;

        private long _tick;

        public HeadlessMatch(int playersPerTeam, in AiProfile team0, in AiProfile team1, uint seed, RuleSet? rules = null)
        {
            RuleSet r = rules ?? RuleSet.ItfOfficial(EventCategory.Women);
            CourtDimensions court = CourtDimensions.For(playersPerTeam == 1 ? CourtFormat.Singles : CourtFormat.Doubles);
            court.NetHeight = r.NetHeight;

            Ball = new BallSystem(BallPhysicsConfig.Default, court);
            int n = playersPerTeam * 2;
            Players = new PlayerSystem[n];
            Ids = new PlayerId[n];
            Ais = new AiPlayer[n];
            for (int k = 0; k < n; k++)
            {
                int team = k / playersPerTeam;
                Ids[k] = new PlayerId(team, k % playersPerTeam);
                Players[k] = new PlayerSystem(Ball, PlayerConfig.Default, court, team == 0 ? -1 : 1, Vec3.Zero, seed * 31u + (uint)k);
            }

            for (int team = 0; team < 2; team++)
            {
                var members = new PlayerSystem[playersPerTeam];
                var opponents = new PlayerSystem[playersPerTeam];
                var isAi = new bool[playersPerTeam];
                for (int i = 0; i < playersPerTeam; i++)
                {
                    members[i] = Players[team * playersPerTeam + i];
                    opponents[i] = Players[(1 - team) * playersPerTeam + i];
                    isAi[i] = true;
                }
                TeamBrain brain = playersPerTeam > 1 ? new TeamBrain(Ball, members, isAi) : null;
                for (int i = 0; i < playersPerTeam; i++)
                {
                    int k = team * playersPerTeam + i;
                    PlayerSystem partner = playersPerTeam > 1 ? members[1 - i] : null;
                    Ais[k] = new AiPlayer(Players[k], opponents, partner, brain, team == 0 ? team0 : team1, seed * 97u + (uint)k)
                    {
                        Role = playersPerTeam > 1 ? (i == 0 ? 1 : -1) : 0
                    };
                }
            }

            Score = new MatchScore(r, playersPerTeam, firstServingTeam: (int)(seed % 2), southTeam: 0);
            Referee = new PointReferee(Score, Ball, Players, Ids, court);
        }

        /// <summary>Plays one point; returns the winning team (-1 for a let or a stalled point).</summary>
        public int PlayPoint()
        {
            if (Score.IsOver) return -1;
            PointSetup.Prepare(Score, Players, Ids, Referee);
            int decisions = Referee.DecisionCount;
            _hitsInPoint = 0;

            for (int i = 0; i < MaxTicksPerPoint && Referee.DecisionCount == decisions; i++) Step();

            PointsPlayed++;
            if (Referee.DecisionCount == decisions)
            {
                Referee.Stop();
                Reasons[(int)PointReason.Let]++;
                return -1;
            }
            Reasons[(int)Referee.LastReason]++;
            if (Referee.LastReason == PointReason.Net || Referee.LastReason == PointReason.ServeNet)
            {
                NetByGrade[(int)_lastHit.Grade]++;
                NetByShot[(int)_lastHit.Shot]++;
                if (_lastHit.Smash) NetSmashes++;
                if (_lastHit.Dive) NetDives++;
                if (_lastHitWasServe) NetServes++;
                if (_lastHit.ContactPoint.y < Ball.Court.NetHeight) NetFromBelowTape++;
            }
            if (Referee.LastWinner >= 0) PointsWon[Referee.LastWinner]++;
            return Referee.LastWinner;
        }

        public void PlayPoints(int count)
        {
            for (int i = 0; i < count && !Score.IsOver; i++) PlayPoint();
        }

        /// <summary>Plays until the match ends (or a point budget runs out).</summary>
        public int PlayMatch(int maxPoints = 1000)
        {
            for (int i = 0; i < maxPoints && !Score.IsOver; i++) PlayPoint();
            return Score.Winner;
        }

        /// <summary>Ticks in which both members of a team were going for the same ball (doubles).</summary>
        public long ConflictTicks { get; private set; }
        /// <summary>CPU time spent inside the AI only (all bots), for the per-frame budget.</summary>
        public readonly System.Diagnostics.Stopwatch AiTime = new System.Diagnostics.Stopwatch();

        private void Step()
        {
            AiTime.Start();
            for (int k = 0; k < Ais.Length; k++) Ais[k].Tick(_tick, Dt);
            AiTime.Stop();
            CountConflicts();
            Ball.Tick(_tick, Dt);
            for (int k = 0; k < Players.Length; k++) Players[k].Tick(_tick, Dt);
            Referee.Tick(_tick, Dt);

            for (int k = 0; k < Players.Length; k++)
            {
                PlayerEventBuffer e = Players[k].Events;
                for (int i = 0; i < e.Count; i++)
                {
                    if (e[i].Type != PlayerEventType.Hit) continue;
                    TotalHits++;
                    HitsByGrade[(int)e[i].Grade]++;
                    if (e[i].Dive) Dives++;
                    _lastHitWasServe = _hitsInPoint == 0; // the serve is the first hit of the point
                    _lastHit = e[i];
                    _hitsInPoint++;
                }
                e.Clear();
            }
            Ball.Events.Clear();
            _tick++;
            TotalTicks++;
        }

        private void CountConflicts()
        {
            int perTeam = Score.PlayersPerTeam;
            if (perTeam < 2) return;
            for (int team = 0; team < 2; team++)
            {
                PlayerSystem a = Players[team * perTeam], b = Players[team * perTeam + 1];
                bool aGoing = a.Command.ShotHeld && a.State.Plan.Valid && !a.State.HasHitCurrentBall;
                bool bGoing = b.Command.ShotHeld && b.State.Plan.Valid && !b.State.HasHitCurrentBall;
                if (aGoing && bGoing) ConflictTicks++;
            }
        }
    }
}
