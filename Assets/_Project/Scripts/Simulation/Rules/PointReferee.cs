using System;
using BeachTennis.Core;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;

namespace BeachTennis.Simulation.Rules
{
    public enum PointReason : byte
    {
        None,
        /// <summary>R24b: the ball touched the sand inside the court on that team's side.</summary>
        BallLandedInOwnCourt,
        /// <summary>R24c: the ball was hit out.</summary>
        Out,
        /// <summary>The ball hit the net body and could not cross.</summary>
        Net,
        /// <summary>R20b: the server swung and missed (there is no second serve, R18).</summary>
        ServeMissed,
        ServeOut,
        ServeNet,
        /// <summary>R24e/j: a team touched the ball twice.</summary>
        DoubleTouch,
        /// <summary>R24h: the ball touched a player's body.</summary>
        BodyHit,
        /// <summary>R23: replay the point (stalled rally).</summary>
        Let
    }

    public enum RallyPhase : byte
    {
        Idle,
        AwaitingServe,
        InPlay,
        Decided
    }

    /// <summary>
    /// Judges each point (ITF Rules of Beach Tennis 2026, R12–R25) from the ball and player events,
    /// tick by tick, and updates the <see cref="MatchScore"/>. Registered after the ball and players.
    ///
    /// Rules enforced structurally elsewhere (cannot happen in the simulation): touching the net or the
    /// opponent's court (movement bounds), hitting before the ball crosses (contact planner),
    /// the prohibited service-return zone (movement constraint applied here until the serve is struck).
    /// Not modelled yet: net posts/permanent fixtures (R14), underarm serve in mixed doubles (R18).
    /// </summary>
    public sealed class PointReferee : ISimulationTickable
    {
        private const float BodyRadius = 0.28f;
        private const float BodyHeight = 1.85f;
        /// <summary>Seconds with the ball out of flight and no decision before the point is replayed.</summary>
        private const float StalledBallSeconds = 3f;

        public MatchScore Score { get; }
        public RallyPhase Phase { get; private set; }

        /// <summary>Decision of the last finished point (valid while Phase == Decided).</summary>
        public int LastWinner { get; private set; } = -1;
        public PointReason LastReason { get; private set; }
        public Vec3 LastPosition { get; private set; }
        public PointOutcome LastOutcome { get; private set; }
        /// <summary>Incremented on every decided point so the game layer can react exactly once.</summary>
        public int DecisionCount { get; private set; }

        private readonly BallSystem _ball;
        private readonly PlayerSystem[] _players;
        private readonly PlayerId[] _ids;
        private readonly CourtDimensions _court;

        private long _seenBallEvents;
        private readonly long[] _seenPlayerEvents;

        private PlayerId _server;
        private int _lastHitTeam = -1;
        private int _lastHitter = -1;
        private bool _serveInFlight;
        private float _rallyTime;

        public PointReferee(MatchScore score, BallSystem ball, PlayerSystem[] players, PlayerId[] ids, in CourtDimensions court)
        {
            if (players.Length != ids.Length) throw new ArgumentException("players and ids differ");
            Score = score;
            _ball = ball;
            _players = players;
            _ids = ids;
            _court = court;
            _seenPlayerEvents = new long[players.Length];
            _seenBallEvents = ball.Events.TotalAdded;
            for (int i = 0; i < players.Length; i++) _seenPlayerEvents[i] = players[i].Events.TotalAdded;
        }

        public int IndexOf(PlayerId id)
        {
            for (int i = 0; i < _ids.Length; i++) if (_ids[i].Equals(id)) return i;
            return -1;
        }

        /// <summary>Starts a point: only the server may act until the serve is struck.</summary>
        public void BeginPoint()
        {
            _server = Score.Server;
            Phase = RallyPhase.AwaitingServe;
            _lastHitTeam = -1;
            _lastHitter = -1;
            _serveInFlight = false;
            _rallyTime = 0f;
            LastReason = PointReason.None;

            // App. I: nobody may stand inside the prohibited zone until the ball is struck.
            for (int i = 0; i < _players.Length; i++) _players[i].MinNetDistance = Score.Rules.ProhibitedZone;
            SkipPendingEvents();
        }

        public void Tick(long tick, float deltaTime)
        {
            if (Phase == RallyPhase.Idle || Phase == RallyPhase.Decided)
            {
                SkipPendingEvents();
                return;
            }
            _rallyTime += deltaTime;

            // Same order as the simulation within a tick: the ball moved first, then players acted.
            ReadBallEvents();
            if (Phase == RallyPhase.Decided) { SkipPendingEvents(); return; }
            ReadPlayerEvents();
            if (Phase == RallyPhase.Decided) return;

            if (Phase == RallyPhase.InPlay)
            {
                CheckBodyHits();
                // Long rallies are fine; a ball that stopped flying without a decision is not.
                if (_ball.State.Mode == BallMode.Flight) _rallyTime = 0f;
                if (Phase == RallyPhase.InPlay && _rallyTime > StalledBallSeconds) Decide(-1, PointReason.Let, _ball.State.Position);
            }
        }

        private void ReadBallEvents()
        {
            BallEventBuffer events = _ball.Events;
            long fresh = events.TotalAdded - _seenBallEvents;
            _seenBallEvents = events.TotalAdded;
            int start = (int)Math.Max(0, events.Count - fresh);

            for (int i = start; i < events.Count && Phase == RallyPhase.InPlay; i++)
            {
                ref readonly BallEvent e = ref events[i];
                switch (e.Type)
                {
                    case BallEventType.SandContact:
                        if (_court.IsInside(e.Position.x, e.Position.z))
                        {
                            // R24b: the team whose half the ball landed on loses. On a serve that is
                            // the receivers (an ace) unless it fell on the server's own half.
                            int teamOnThatHalf = e.Position.z < 0f ? Score.SouthTeam : 1 - Score.SouthTeam;
                            PointReason reason = teamOnThatHalf == _lastHitTeam && _serveInFlight
                                ? PointReason.ServeNet
                                : PointReason.BallLandedInOwnCourt;
                            Decide(1 - teamOnThatHalf, reason, e.Position);
                        }
                        else
                        {
                            Decide(1 - _lastHitTeam, _serveInFlight ? PointReason.ServeOut : PointReason.Out, e.Position);
                        }
                        break;

                    case BallEventType.NetBody:
                        Decide(1 - _lastHitTeam, _serveInFlight ? PointReason.ServeNet : PointReason.Net, e.Position);
                        break;

                    case BallEventType.CrossedNet:
                        // R22: a serve that clips the tape and crosses stays in play (no let).
                        break;
                }
            }
        }

        private void ReadPlayerEvents()
        {
            for (int p = 0; p < _players.Length && Phase != RallyPhase.Decided; p++)
            {
                PlayerEventBuffer events = _players[p].Events;
                long fresh = events.TotalAdded - _seenPlayerEvents[p];
                _seenPlayerEvents[p] = events.TotalAdded;
                int start = (int)Math.Max(0, events.Count - fresh);

                for (int i = start; i < events.Count && Phase != RallyPhase.Decided; i++)
                {
                    ref readonly PlayerEvent e = ref events[i];
                    int team = _ids[p].Team;

                    if (Phase == RallyPhase.AwaitingServe)
                    {
                        if (!_ids[p].Equals(_server)) continue;
                        if (e.Type == PlayerEventType.Hit)
                        {
                            Phase = RallyPhase.InPlay;
                            _lastHitTeam = team;
                            _lastHitter = p;
                            _serveInFlight = true;
                            // The ball is struck: the prohibited zone no longer applies.
                            for (int k = 0; k < _players.Length; k++) _players[k].MinNetDistance = Players.ContactPlanner.DefaultMinNetDistance;
                        }
                        else if (e.Type == PlayerEventType.Whiff)
                        {
                            Decide(1 - team, PointReason.ServeMissed, _players[p].State.Position); // R20b
                        }
                        continue;
                    }

                    if (e.Type != PlayerEventType.Hit) continue;
                    if (team == _lastHitTeam)
                    {
                        Decide(1 - team, PointReason.DoubleTouch, e.ContactPoint); // R24e/j
                        continue;
                    }
                    _lastHitTeam = team;
                    _lastHitter = p;
                    _serveInFlight = false;
                }
            }
        }

        /// <summary>R24h: the ball in flight passing through a player's body (anyone but the last hitter).</summary>
        private void CheckBodyHits()
        {
            ref readonly BallState ball = ref _ball.State;
            if (ball.Mode != BallMode.Flight || ball.Position.y > BodyHeight) return;

            for (int p = 0; p < _players.Length; p++)
            {
                if (p == _lastHitter) continue;
                if (Vec3.HorizontalDistance(_players[p].State.Position, ball.Position) <= BodyRadius + 0.0335f)
                {
                    Decide(1 - _ids[p].Team, PointReason.BodyHit, ball.Position);
                    return;
                }
            }
        }

        private void Decide(int winner, PointReason reason, Vec3 position)
        {
            Phase = RallyPhase.Decided;
            LastWinner = winner;
            LastReason = reason;
            LastPosition = position;
            LastOutcome = winner >= 0 ? Score.AwardPoint(winner) : default;
            DecisionCount++;
            for (int i = 0; i < _players.Length; i++) _players[i].MinNetDistance = Players.ContactPlanner.DefaultMinNetDistance;
        }

        /// <summary>Ends the point without a decision (e.g. the game layer resets).</summary>
        public void Stop()
        {
            Phase = RallyPhase.Idle;
            SkipPendingEvents();
        }

        private void SkipPendingEvents()
        {
            _seenBallEvents = _ball.Events.TotalAdded;
            for (int i = 0; i < _players.Length; i++) _seenPlayerEvents[i] = _players[i].Events.TotalAdded;
        }
    }
}
