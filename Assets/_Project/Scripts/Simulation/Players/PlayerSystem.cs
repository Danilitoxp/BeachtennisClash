using System;
using BeachTennis.Core;
using BeachTennis.Simulation.Ball;

namespace BeachTennis.Simulation.Players
{
    /// <summary>
    /// One player inside the fixed-tick simulation: sand locomotion, contact planning, positioning assist,
    /// timing-graded hits, dives and serves. Driven only by <see cref="Command"/>, so a human, the AI or a
    /// network peer control it the same way. Must be registered after the <see cref="BallSystem"/>.
    ///
    /// Control model (docs/02 Fase 2): hold a shot button to prepare (the player runs to the contact point
    /// by itself and Move becomes Aim), release to swing. Release time vs. ideal time grades the shot.
    /// Still holding when the ball arrives = automatic late swing (forgiving on touch screens).
    /// </summary>
    public sealed class PlayerSystem : ISimulationTickable
    {
        private const float ServeHandHeight = 1.6f;
        private const float ServeTossSpeed = 5.6f;      // apex ≈ 3.2 m
        private const float ServeIdealHeight = 2.75f;   // contact on the way down
        private const float ServeFailHeight = 0.8f;

        public PlayerConfig Config;
        public CourtDimensions Court;
        public PlayerState State;
        public PlayerState PreviousState;
        public PlayerCommand Command;
        public readonly PlayerEventBuffer Events = new PlayerEventBuffer();

        private readonly BallSystem _ball;
        private readonly BallState[] _prediction = new BallState[960];
        private DeterministicRandom _rng;
        private int _seenLaunchVersion;
        private int _plannedLaunchVersion = -1;
        private int _seenReleaseSerial;
        private bool _prevHeld;
        private bool _tossed;
        private float _dt = 1f / 120f;

        public PlayerSystem(BallSystem ball, in PlayerConfig config, in CourtDimensions court, int side, Vec3 start, uint seed)
        {
            _ball = ball;
            Config = config;
            Court = court;
            _rng = new DeterministicRandom(seed);
            _seenLaunchVersion = ball.LaunchVersion;
            State = new PlayerState
            {
                Position = ContactPlanner.ClampToBounds(start, court, side),
                Side = side,
                SwingTime = double.NaN
            };
            PreviousState = State;
        }

        /// <summary>Simulation time (s) at the end of tick index <paramref name="tick"/>.</summary>
        public static double TimeOfTick(long tick, float dt) => (tick + 1) * (double)dt;

        public bool IsServing => State.Phase == PlayerPhase.Serving;

        /// <summary>Ball launch this player has already evaluated (plan up to date when equal to the ball's).</summary>
        public int SeenLaunchVersion => _seenLaunchVersion;

        public BallSystem Ball => _ball;

        /// <summary>
        /// Closest the player may stand to the net. Raised to the prohibited service-return zone
        /// (3 m / 6 m, ITF App. I) until the serve is struck.
        /// </summary>
        public float MinNetDistance = ContactPlanner.DefaultMinNetDistance;

        public void Teleport(Vec3 position)
        {
            State.Position = ContactPlanner.ClampToBounds(position, Court, State.Side, MinNetDistance);
            State.Velocity = Vec3.Zero;
            PreviousState = State;
        }

        /// <summary>Moves the player to the other half (change of ends) and clears any pending action.</summary>
        public void SetSide(int side, Vec3 position)
        {
            State.Side = side;
            State.Phase = PlayerPhase.Free;
            State.Plan = default;
            State.SwingTime = double.NaN;
            State.HasHitCurrentBall = false;
            _tossed = false;
            Teleport(position);
        }

        /// <summary>Moves the player behind the baseline and waits for a toss (press) and hit (release).</summary>
        public void BeginServe(float x)
        {
            State.Phase = PlayerPhase.Serving;
            State.Position = new Vec3(x, 0f, State.Side * (Court.HalfLength + 0.3f));
            State.Velocity = Vec3.Zero;
            State.Plan = default;
            State.SwingTime = double.NaN;
            State.HasHitCurrentBall = false;
            PreviousState = State;
            _tossed = false;
            _ball.Deactivate();
            _seenLaunchVersion = _ball.LaunchVersion;
        }

        public void EndServe()
        {
            if (State.Phase == PlayerPhase.Serving) State.Phase = PlayerPhase.Free;
            _tossed = false;
        }

        public void Tick(long tick, float deltaTime)
        {
            _dt = deltaTime;
            PreviousState = State;
            double now = TimeOfTick(tick, deltaTime);

            State.SplitStepTimer = MathF.Max(0f, State.SplitStepTimer - deltaTime);
            State.SwingCooldownTimer = MathF.Max(0f, State.SwingCooldownTimer - deltaTime);
            if (State.Phase == PlayerPhase.Recovering)
            {
                State.PhaseTimer -= deltaTime;
                if (State.PhaseTimer <= 0f) State.Phase = PlayerPhase.Free;
            }

            DetectIncomingBall(tick);
            HandleServeToss(tick);
            ConsumeRelease(tick, now);
            Move(deltaTime);
            ResolveContact(tick);
            CheckServeFail();

            _prevHeld = Command.ShotHeld;
        }

        private void DetectIncomingBall(long tick)
        {
            if (_ball.LaunchVersion == _seenLaunchVersion) return;
            _seenLaunchVersion = _ball.LaunchVersion;
            if (State.Phase == PlayerPhase.Serving) return;

            ref readonly BallState ball = ref _ball.State;
            bool incoming = ball.Mode == BallMode.Flight && ball.Velocity.z * State.Side > 0f;
            if (!incoming)
            {
                State.Plan.Valid = false;
                return;
            }

            State.HasHitCurrentBall = false;
            State.SwingTime = double.NaN;
            if (State.Velocity.Magnitude <= Config.SplitStepMaxSpeed)
                State.SplitStepTimer = Config.SplitStepDuration;

            int n = BallPhysics.Predict(ball, _ball.Config, _ball.Court, _dt, _prediction, stopAtSand: true);
            float accel = State.SplitStepTimer > 0f ? Config.SplitStepBoost : 1f;
            ContactPlan plan = ContactPlanner.Plan(State, Config, Court, _prediction, n, tick + 1, _dt, accel);
            plan.Time = TimeOfTick(plan.Tick, _dt);
            State.Plan = plan;
            _plannedLaunchVersion = _ball.LaunchVersion;
        }

        private void HandleServeToss(long tick)
        {
            if (State.Phase != PlayerPhase.Serving || _tossed) return;
            if (!Command.ShotHeld || _prevHeld) return; // rising edge only

            int side = State.Side;
            var hand = new Vec3(State.Position.x - side * 0.25f, ServeHandHeight, State.Position.z - side * 0.3f);
            _ball.Launch(hand, new Vec3(0f, ServeTossSpeed, 0f), Vec3.Zero);
            _seenLaunchVersion = _ball.LaunchVersion;
            _plannedLaunchVersion = _ball.LaunchVersion;
            _tossed = true;

            int n = BallPhysics.Predict(_ball.State, _ball.Config, _ball.Court, _dt, _prediction, stopAtSand: true);
            State.Plan = default;
            for (int i = 0; i < n; i++)
            {
                if (_prediction[i].Velocity.y < 0f && _prediction[i].Position.y <= ServeIdealHeight)
                {
                    State.Plan = new ContactPlan
                    {
                        Valid = true,
                        Reachable = true,
                        Tick = tick + 1 + i,
                        Time = TimeOfTick(tick + 1 + i, _dt),
                        BallPosition = _prediction[i].Position,
                        StandPosition = State.Position
                    };
                    break;
                }
            }
            State.HasHitCurrentBall = false;
            State.SwingTime = double.NaN;
            Events.Add(new PlayerEvent { Type = PlayerEventType.ServeToss, Tick = tick });
        }

        private void ConsumeRelease(long tick, double now)
        {
            if (Command.ReleaseSerial == _seenReleaseSerial) return;
            _seenReleaseSerial = Command.ReleaseSerial;

            if (State.SwingCooldownTimer > 0f || State.Phase == PlayerPhase.Recovering) return;
            double release = Math.Min(Command.ReleaseTime, now);

            if (State.Plan.Valid && !State.HasHitCurrentBall)
            {
                double early = (State.Plan.Time - Config.SwingLead) - release;
                if (early > Config.WhiffEarlyLimit)
                {
                    State.SwingCooldownTimer = Config.SwingCooldown;
                    Events.Add(new PlayerEvent { Type = PlayerEventType.Whiff, Tick = tick, TimingError = (float)-early, Shot = Command.ReleaseType });
                    return;
                }
                State.SwingTime = release;
                State.SwingType = Command.ReleaseType;
            }
            else
            {
                State.SwingCooldownTimer = Config.SwingCooldown; // swing at nothing
            }

            Events.Add(new PlayerEvent { Type = PlayerEventType.SwingStarted, Tick = tick, Shot = Command.ReleaseType, ReleaseSerial = _seenReleaseSerial });
        }

        private void Move(float dt)
        {
            if (State.Phase == PlayerPhase.Recovering)
            {
                State.Velocity = Vec3.Zero;
                return;
            }

            Vec3 desired;
            bool preparing = Command.ShotHeld || !double.IsNaN(State.SwingTime);
            bool assist = State.Plan.Valid && !State.HasHitCurrentBall && preparing && State.Phase != PlayerPhase.Serving;

            if (assist)
            {
                Vec3 to = (State.Plan.StandPosition - State.Position).Horizontal;
                float d = to.Magnitude;
                float speed = d < 0.02f ? 0f : MathF.Min(Config.MaxSpeed, MathF.Sqrt(2f * Config.Braking * d));
                desired = d < 0.02f ? Vec3.Zero : to * (speed / d);
            }
            else
            {
                var m = new Vec3(Command.MoveX, 0f, Command.MoveZ);
                float mag = m.Magnitude;
                if (mag > 1f) m = m / mag;
                desired = m * Config.MaxSpeed;
                if (State.Phase == PlayerPhase.Serving) desired.z = 0f;
            }

            Vec3 v = State.Velocity;
            Vec3 dv = desired - v;
            bool braking = Vec3.Dot(desired, v) <= 0f || desired.SqrMagnitude < v.SqrMagnitude;
            float rate = braking ? Config.Braking : Config.Acceleration * (State.SplitStepTimer > 0f ? Config.SplitStepBoost : 1f);
            float maxDv = rate * dt;
            float dvMag = dv.Magnitude;
            if (dvMag > maxDv) dv = dv * (maxDv / dvMag);
            v = v + dv;

            Vec3 p = State.Position + v * dt;
            Vec3 clamped = State.Phase == PlayerPhase.Serving
                ? new Vec3(MathF.Max(-Court.HalfWidth, MathF.Min(Court.HalfWidth, p.x)), 0f, State.Position.z)
                : ContactPlanner.ClampToBounds(p, Court, State.Side, MinNetDistance);
            if (clamped.x != p.x) v.x = 0f;
            if (clamped.z != p.z) v.z = 0f;

            State.Position = clamped;
            State.Velocity = v;
        }

        private void ResolveContact(long tick)
        {
            if (!State.Plan.Valid || State.HasHitCurrentBall || tick != State.Plan.Tick) return;

            ref readonly BallState ball = ref _ball.State;
            if (_ball.LaunchVersion != _plannedLaunchVersion || ball.Mode != BallMode.Flight || ball.BounceCount > 0)
            {
                State.Plan.Valid = false;
                return;
            }

            State.HasHitCurrentBall = true;
            float error;
            ShotType type;
            if (!double.IsNaN(State.SwingTime))
            {
                error = (float)(State.SwingTime - (State.Plan.Time - Config.SwingLead));
                type = State.SwingType;
            }
            else if (Command.ShotHeld)
            {
                error = Config.HeldAtContactError;
                type = Command.HeldType;
            }
            else
            {
                Events.Add(new PlayerEvent { Type = PlayerEventType.NoSwing, Tick = tick, ContactPoint = ball.Position });
                return;
            }
            State.SwingTime = double.NaN;

            bool serving = State.Phase == PlayerPhase.Serving;
            float distance = Vec3.HorizontalDistance(State.Position, ball.Position);
            bool dive = false;
            if (distance > Config.ReachRadius)
            {
                if (!serving && distance <= Config.ReachRadius + Config.DiveExtraReach) dive = true;
                else
                {
                    Events.Add(new PlayerEvent { Type = PlayerEventType.OutOfReach, Tick = tick, ContactPoint = ball.Position, TimingError = error });
                    return;
                }
            }

            TimingGrade grade = ShotSelector.Grade(error, Config);
            ShotRequest request = ShotSelector.Build(type, serving, ball.Position, Command.AimX, Command.AimY,
                State.Side, Config, Court, out bool smash, Command.EffectivePower);
            ShotSolution solution = ShotSolver.Solve(request, _ball.Config, Court, _dt);
            Vec3 velocity = ShotSelector.ApplyTimingError(solution.Velocity, grade, error, dive, Config, ref _rng);

            Vec3 contact = ball.Position;
            _ball.Launch(contact, velocity, solution.Spin);
            _seenLaunchVersion = _ball.LaunchVersion;
            State.Plan.Valid = false;

            Events.Add(new PlayerEvent
            {
                Type = PlayerEventType.Hit,
                Tick = tick,
                Grade = grade,
                TimingError = error,
                Shot = type,
                Smash = smash,
                Dive = dive,
                ArcRaised = solution.ArcRaised,
                Target = request.Target,
                ContactPoint = contact,
                BallSpeed = velocity.Magnitude
            });

            if (dive)
            {
                // Lunge towards the ball, then stay down.
                Vec3 dir = (contact - State.Position).Horizontal.Normalized;
                State.Position = ContactPlanner.ClampToBounds(State.Position + dir * (distance - Config.ReachRadius * 0.8f), Court, State.Side);
                State.Velocity = Vec3.Zero;
                State.Phase = PlayerPhase.Recovering;
                State.PhaseTimer = Config.DiveRecovery;
            }
            if (serving) EndServe();
        }

        private void CheckServeFail()
        {
            if (State.Phase != PlayerPhase.Serving || !_tossed) return;
            if (_ball.LaunchVersion != _plannedLaunchVersion) return;
            ref readonly BallState ball = ref _ball.State;
            if (ball.Velocity.y < 0f && ball.Position.y < ServeFailHeight)
            {
                // Toss not hit: catch it and toss again.
                _ball.Deactivate();
                _tossed = false;
                State.Plan.Valid = false;
                State.HasHitCurrentBall = false;
                State.SwingTime = double.NaN;
            }
        }
    }
}
