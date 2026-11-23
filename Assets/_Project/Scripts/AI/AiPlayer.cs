using System;
using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;

namespace BeachTennis.AI
{
    /// <summary>
    /// AI player. Sees what a person sees — the ball's flight (the same contact plan the human's stand
    /// ring shows) and where everyone stands — and acts only through <see cref="PlayerCommand"/>.
    ///
    /// Per incoming ball: waits its reaction time, then (if this teammate owns the ball) holds the
    /// chosen shot so the assist takes it to the ball, and releases at the ideal moment plus a timing
    /// error. Shot and target come from the situation: smash high balls, lob opponents at the net,
    /// drop opponents who stay deep, otherwise play into the open court. Between shots it returns to a
    /// tactical base position (its half of the court in doubles).
    /// Register before the player it controls so its command applies in the same tick.
    /// </summary>
    public sealed class AiPlayer : ISimulationTickable
    {
        private static readonly float[] AimXs = { -0.9f, -0.45f, 0f, 0.45f, 0.9f };
        private static readonly float[] AimYs = { -0.8f, 0f, 0.8f };

        public AiProfile Profile;

        private readonly PlayerSystem _player;
        private readonly PlayerSystem[] _opponents;
        private readonly PlayerSystem _partner;
        private readonly TeamBrain _brain;
        private DeterministicRandom _rng;

        private long _plannedForTick = -1;
        private double _reactAt;
        private bool _willSwing;
        private double _releaseAt = double.NaN;
        private ShotType _shot;
        private float _aimX, _aimY;
        private float _serveTimer;
        private bool _lastShotWasLob;
        private float _power;

        /// <summary>-1 = covers the left half, +1 = the right half (as seen by this team), 0 = whole court.</summary>
        public int Role { get; set; }

        public AiPlayer(PlayerSystem player, PlayerSystem[] opponents, PlayerSystem partner, TeamBrain brain,
            in AiProfile profile, uint seed)
        {
            _player = player;
            _opponents = opponents;
            _partner = partner;
            _brain = brain;
            Profile = profile;
            _rng = new DeterministicRandom(seed);
        }

        public void Tick(long tick, float deltaTime)
        {
            ref PlayerCommand cmd = ref _player.Command;
            ref readonly PlayerState s = ref _player.State;
            double now = PlayerSystem.TimeOfTick(tick, deltaTime);
            cmd.MoveX = 0f;
            cmd.MoveZ = 0f;

            if (_player.IsServing)
            {
                Serve(ref cmd, s, now, deltaTime);
                return;
            }
            _serveTimer = 0f;

            bool incoming = s.Plan.Valid && !s.HasHitCurrentBall;
            if (incoming && s.Plan.Tick != _plannedForTick) OnNewBall(s, now);

            if (incoming)
            {
                int owner = _brain?.Owner ?? 0;
                bool mine = _brain == null || owner == _brain.IndexOf(_player);
                bool undecided = _brain != null && owner < 0;

                if (now < _reactAt || undecided)
                {
                    // Still reading the shot: keep drifting to the base position.
                    cmd.ShotHeld = false;
                    MoveTo(ref cmd, s, BasePosition(s), Profile.PositioningSpeed);
                    return;
                }
                if (!mine)
                {
                    cmd.ShotHeld = false;
                    MoveTo(ref cmd, s, CoverPosition(s), 1f); // partner's ball: cover the open side
                    return;
                }
                if (!_willSwing)
                {
                    cmd.ShotHeld = false;
                    return;
                }

                cmd.ShotHeld = true;
                cmd.HeldType = _shot;
                cmd.AimX = _aimX;
                cmd.AimY = _aimY;
                cmd.Power = _power;
                if (!double.IsNaN(_releaseAt) && now >= _releaseAt) Release(ref cmd, _releaseAt);
                return;
            }

            cmd.ShotHeld = false;
            _releaseAt = double.NaN;
            MoveTo(ref cmd, s, BasePosition(s), Profile.PositioningSpeed);
        }

        // ------------------------------------------------------------------ decisions

        private void OnNewBall(in PlayerState s, double now)
        {
            _plannedForTick = s.Plan.Tick;
            _reactAt = now + Profile.ReactionTime * _rng.Range(0.8f, 1.2f);
            _willSwing = _rng.NextFloat() >= Profile.MissChance;
            ChooseShot(s.Plan);
            // Fast balls are harder to time: the error grows with the incoming speed (a 25 m/s smash
            // roughly doubles it compared to a slow ball).
            float incoming = _player.Ball.State.Velocity.Magnitude;
            float sigma = Profile.TimingSigma * (1f + incoming / 20f);
            _releaseAt = s.Plan.Time - _player.Config.SwingLead + Gaussian() * sigma;
        }

        private void ChooseShot(in ContactPlan plan)
        {
            Vec3 contact = plan.BallPosition;
            float oppDepth = MeanOpponentDepth();

            if (contact.y >= _player.Config.SmashHeight)
                _shot = ShotType.Main;                                    // smash
            else if (oppDepth < 3.8f && _rng.NextFloat() < Profile.LobSense)
                _shot = ShotType.Lob;                                     // they are at the net: go over them
            else if (oppDepth > 6.2f && _rng.NextFloat() < Profile.DropSense)
                _shot = ShotType.Drop;                                    // they are deep: play short
            else
                _shot = ShotType.Main;

            // Attack balls met high and close to the net; play safer from deep or low.
            float attack = (contact.y > _player.Court.NetHeight ? 0.15f : -0.1f) + (System.MathF.Abs(contact.z) < 3f ? 0.1f : 0f);
            _power = Clamp(Profile.Power + attack, 0.05f, 1f);

            bool smash = _shot == ShotType.Main && contact.y >= _player.Config.SmashHeight;
            bool aimed = smash && _rng.NextFloat() < Profile.BodyAttack && AimAtNearestOpponent();
            if (!aimed)
            {
                if (_rng.NextFloat() < Profile.TacticalSkill)
                {
                    AimAtOpenCourt(contact);
                }
                else
                {
                    _aimX = _rng.Range(-0.9f, 0.9f);
                    _aimY = _rng.Range(-0.8f, 0.8f);
                }
            }

            _aimX = Clamp(_aimX + Gaussian() * Profile.AimNoise, -1f, 1f);
            _aimY = Clamp(_aimY + Gaussian() * Profile.AimNoise, -1f, 1f);
            _lastShotWasLob = _shot == ShotType.Lob;
        }

        /// <summary>Picks the aim whose target is farthest from every opponent, minus a risk penalty near the lines.</summary>
        private void AimAtOpenCourt(Vec3 contact)
        {
            float best = float.MinValue;
            CourtDimensions court = _player.Court;
            foreach (float ax in AimXs)
            foreach (float ay in AimYs)
            {
                ShotRequest r = ShotSelector.Build(_shot, false, contact, ax, ay, _player.State.Side, _player.Config, court, out _, _power);
                float space = float.MaxValue;
                foreach (PlayerSystem o in _opponents)
                    space = MathF.Min(space, Vec3.HorizontalDistance(o.State.Position, r.Target));
                // Less precise players pay more for aiming near the lines.
                float edge = MathF.Min(court.HalfWidth - MathF.Abs(r.Target.x), court.HalfLength - MathF.Abs(r.Target.z));
                float risk = (1f - MathF.Min(1f, edge / 1.5f)) * Profile.AimNoise * 6f;
                float score = space - risk;
                if (score > best)
                {
                    best = score;
                    _aimX = ax;
                    _aimY = ay;
                }
            }
        }

        /// <summary>Aims a smash at the opponent closest to the net (hardest to react, body hit wins, R24h).</summary>
        private bool AimAtNearestOpponent()
        {
            PlayerSystem target = null;
            float d = float.MaxValue;
            foreach (PlayerSystem o in _opponents)
            {
                float od = MathF.Abs(o.State.Position.z);
                if (od < d) { d = od; target = o; }
            }
            if (target == null) return false;

            // Invert ShotSelector's aim mapping for the main shot: x = aimX·(halfWidth-0.6)·right.
            CourtDimensions court = _player.Court;
            float right = -_player.State.Side;
            _aimX = Clamp(target.State.Position.x / ((court.HalfWidth - 0.6f) * right), -1f, 1f);
            float depth = MathF.Abs(target.State.Position.z);
            _aimY = Clamp((depth - 3.0f) / 3.5f * 2f - 1f, -1f, 1f); // smash depth range 3.0–6.5 m
            return true;
        }

        private void Serve(ref PlayerCommand cmd, in PlayerState s, double now, float dt)
        {
            if (!s.Plan.Valid && _plannedForTick != -1)
            {
                _plannedForTick = -1; // toss fell without a hit: let go and toss again
                _serveTimer = 0f;
            }
            _serveTimer += dt;
            if (!s.Plan.Valid)
            {
                cmd.ShotHeld = _serveTimer >= 0.8f; // rising edge tosses the ball
                cmd.HeldType = ShotType.Main;
                return;
            }
            if (s.Plan.Tick != _plannedForTick)
            {
                _plannedForTick = s.Plan.Tick;
                _shot = ShotType.Main;
                _power = Clamp(Profile.Power, 0.05f, 1f);
                // Serve away from the receivers when skilled; anywhere otherwise.
                if (_rng.NextFloat() < Profile.TacticalSkill) AimServe();
                else { _aimX = _rng.Range(-0.8f, 0.8f); _aimY = _rng.Range(-0.2f, 0.9f); }
                _aimX = Clamp(_aimX + Gaussian() * Profile.AimNoise * 0.5f, -1f, 1f);
                _aimY = Clamp(_aimY + Gaussian() * Profile.AimNoise * 0.5f, -1f, 1f);
                _releaseAt = s.Plan.Time - _player.Config.SwingLead + Gaussian() * Profile.TimingSigma * 0.6f;
            }
            cmd.ShotHeld = true;
            cmd.AimX = _aimX;
            cmd.AimY = _aimY;
            cmd.Power = _power;
            if (now >= _releaseAt) Release(ref cmd, _releaseAt);
        }

        private void AimServe()
        {
            // Deep, towards the side with fewer receivers.
            float meanX = 0f;
            foreach (PlayerSystem o in _opponents) meanX += o.State.Position.x;
            meanX /= _opponents.Length;
            float right = -_player.State.Side;
            _aimX = meanX * right > 0f ? -0.75f : 0.75f;
            _aimY = 0.6f;
        }

        private void Release(ref PlayerCommand cmd, double time)
        {
            cmd.ShotHeld = false;
            cmd.ReleaseSerial++;
            cmd.ReleaseTime = time;
            cmd.ReleaseType = _shot;
            _releaseAt = double.NaN;
        }

        // ------------------------------------------------------------------ positioning

        private Vec3 BasePosition(in PlayerState s)
        {
            CourtDimensions court = _player.Court;
            float depth = Profile.HomeDepth;
            if (_lastShotWasLob) depth -= 1.2f;               // after a lob, take the net
            if (MeanOpponentDepth() < 3.8f) depth += 0.6f;    // they are at the net: expect a hard shot

            ref readonly BallState ball = ref _player.Ball.State;
            float ballX = ball.Mode == BallMode.Flight ? ball.Position.x : 0f;
            float right = -s.Side;
            float x = Role == 0
                ? ballX * 0.5f
                : Role * right * court.HalfWidth * 0.45f + ballX * 0.25f;
            return new Vec3(x, 0f, s.Side * depth);
        }

        /// <summary>When the partner takes the ball: hold our half, slightly deeper.</summary>
        private Vec3 CoverPosition(in PlayerState s)
        {
            Vec3 b = BasePosition(s);
            if (_partner == null) return b;
            float partnerX = _partner.State.Plan.Valid ? _partner.State.Plan.StandPosition.x : _partner.State.Position.x;
            float half = _player.Court.HalfWidth * 0.5f;
            b.x = partnerX > 0f ? -half : half;
            return b;
        }

        private void MoveTo(ref PlayerCommand cmd, in PlayerState s, Vec3 target, float speed)
        {
            Vec3 to = (target - s.Position).Horizontal;
            float d = to.Magnitude;
            if (d < 0.25f) return;
            float k = MathF.Min(speed, d / 1.2f); // ease in when arriving
            cmd.MoveX = to.x / d * k;
            cmd.MoveZ = to.z / d * k;
        }

        private float MeanOpponentDepth()
        {
            float sum = 0f;
            foreach (PlayerSystem o in _opponents) sum += MathF.Abs(o.State.Position.z);
            return _opponents.Length > 0 ? sum / _opponents.Length : 6f;
        }

        private float Gaussian() => (_rng.NextFloat() + _rng.NextFloat() + _rng.NextFloat() - 1.5f) * 2f;
        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
