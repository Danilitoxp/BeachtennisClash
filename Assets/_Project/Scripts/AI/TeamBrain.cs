using BeachTennis.Core;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;

namespace BeachTennis.AI
{
    /// <summary>
    /// Decides which teammate takes each incoming ball ("minha!" / "sua!"), so doubles partners never chase
    /// the same ball (R24j: both touching it loses the point) and never both leave it.
    ///
    /// Owner = the teammate who can meet the ball earliest; near-ties go to whoever is closer to it.
    /// A human teammate keeps priority unless the AI can clearly get there first, because the bot must
    /// not steal balls the player is going for.
    /// </summary>
    public sealed class TeamBrain
    {
        /// <summary>The AI only overrides a human teammate when it meets the ball this much earlier (s).</summary>
        private const float HumanPriorityMargin = 0.2f;
        private const float TieWindow = 0.08f;

        private readonly BallSystem _ball;
        private readonly PlayerSystem[] _members;
        private readonly bool[] _isAi;
        private int _resolvedVersion = -1;
        private int _owner = -1;

        /// <summary>Last call made by an AI member (for the "MINHA!/SUA!" bubble). Incremented per call.</summary>
        public int CallCount { get; private set; }
        public int CallerIndex { get; private set; } = -1;
        public bool CallMine { get; private set; }

        public TeamBrain(BallSystem ball, PlayerSystem[] members, bool[] isAi)
        {
            _ball = ball;
            _members = members;
            _isAi = isAi;
        }

        public int IndexOf(PlayerSystem p)
        {
            for (int i = 0; i < _members.Length; i++) if (ReferenceEquals(_members[i], p)) return i;
            return -1;
        }

        /// <summary>
        /// Owner of the current ball, or -1 while undecided (some teammate has not evaluated the latest
        /// launch yet) or when nobody can play it.
        /// </summary>
        public int Owner
        {
            get
            {
                int version = _ball.LaunchVersion;
                if (version == _resolvedVersion) return _owner;
                for (int i = 0; i < _members.Length; i++)
                    if (_members[i].SeenLaunchVersion != version) return -1; // wait until every plan is fresh
                Resolve(version);
                return _owner;
            }
        }

        private void Resolve(int version)
        {
            _resolvedVersion = version;
            _owner = -1;

            int bestAi = -1, bestHuman = -1;
            for (int i = 0; i < _members.Length; i++)
            {
                ref readonly PlayerState s = ref _members[i].State;
                if (!s.Plan.Valid || s.HasHitCurrentBall) continue;
                if (_isAi[i]) { if (Better(i, bestAi)) bestAi = i; }
                else if (Better(i, bestHuman)) bestHuman = i;
            }

            if (bestHuman >= 0 && bestAi >= 0)
            {
                bool aiClearlyFirst = Effective(bestAi) + HumanPriorityMargin < Effective(bestHuman);
                _owner = aiClearlyFirst ? bestAi : bestHuman;
            }
            else
            {
                _owner = bestHuman >= 0 ? bestHuman : bestAi;
            }

            if (_owner < 0) return;
            // An AI member announces the decision: "mine" if it takes it, "yours" to a teammate.
            int caller = _isAi[_owner] ? _owner : FirstAiOtherThan(_owner);
            if (caller < 0) return;
            CallerIndex = caller;
            CallMine = caller == _owner;
            CallCount++;
        }

        /// <summary>Contact time, penalised when only a dive reaches it.</summary>
        private float Effective(int i)
        {
            ref readonly ContactPlan p = ref _members[i].State.Plan;
            return (float)p.Time + (p.Reachable ? 0f : 0.5f);
        }

        private bool Better(int candidate, int current)
        {
            if (current < 0) return true;
            float a = Effective(candidate), b = Effective(current);
            if (a < b - TieWindow) return true;
            if (a > b + TieWindow) return false;
            return Distance(candidate) < Distance(current);
        }

        private float Distance(int i) =>
            Vec3.HorizontalDistance(_members[i].State.Position, _members[i].State.Plan.BallPosition);

        private int FirstAiOtherThan(int index)
        {
            for (int i = 0; i < _members.Length; i++) if (_isAi[i] && i != index) return i;
            return -1;
        }
    }
}
