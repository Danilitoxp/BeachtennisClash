using BeachTennis.Core;

namespace BeachTennis.Simulation.Players
{
    /// <summary>What the player asked for. The main shot is contextual (volley or smash by contact height).</summary>
    public enum ShotType : byte
    {
        Main,
        Lob,
        Drop
    }

    public enum TimingGrade : byte
    {
        Perfect,
        Good,
        Early,
        Late
    }

    public enum PlayerPhase : byte
    {
        Free,
        /// <summary>Serving: tossing and hitting from behind the baseline.</summary>
        Serving,
        /// <summary>On the sand after a dive; cannot move.</summary>
        Recovering
    }

    /// <summary>
    /// Input to a player for one tick — produced identically by touch controls, AI and (later) the network.
    /// </summary>
    public struct PlayerCommand
    {
        /// <summary>Desired movement in world X/Z, magnitude 0..1.</summary>
        public float MoveX, MoveZ;

        /// <summary>A shot button is held (preparing). While held, movement is assisted and Move becomes Aim.</summary>
        public bool ShotHeld;
        public ShotType HeldType;

        /// <summary>Incremented on every button release; the release time is consumed once per serial.</summary>
        public int ReleaseSerial;
        /// <summary>Simulation time (s) at which the button was physically released (latency-compensated).</summary>
        public double ReleaseTime;
        public ShotType ReleaseType;

        /// <summary>Aim in -1..1. X: left/right as seen by the hitter; Y: deeper (+) or shorter (-).</summary>
        public float AimX, AimY;

        /// <summary>
        /// How hard to hit drives, smashes and serves, 0..1 (0 = use the default 0.5). More power means a
        /// faster ball that is harder to reach, and a smaller margin for timing errors.
        /// </summary>
        public float Power;

        public float EffectivePower => Power <= 0f ? 0.5f : (Power > 1f ? 1f : Power);
    }

    /// <summary>Where and when the player will meet the incoming ball.</summary>
    public struct ContactPlan
    {
        public bool Valid;
        /// <summary>Reachable in time by running (false = only by diving, or not at all).</summary>
        public bool Reachable;
        public long Tick;
        public double Time;
        public Vec3 BallPosition;
        /// <summary>Where the body should be (on the sand) to hit comfortably.</summary>
        public Vec3 StandPosition;
    }

    public struct PlayerState
    {
        /// <summary>On the sand (y = 0).</summary>
        public Vec3 Position;
        public Vec3 Velocity;
        /// <summary>-1 = south half (z &lt; 0), +1 = north half.</summary>
        public int Side;
        public PlayerPhase Phase;
        public float PhaseTimer;
        public float SplitStepTimer;
        public float SwingCooldownTimer;

        public ContactPlan Plan;
        /// <summary>Pending swing (button released) waiting for the contact tick. NaN = none.</summary>
        public double SwingTime;
        public ShotType SwingType;
        public bool HasHitCurrentBall;
    }

    public enum PlayerEventType : byte
    {
        /// <summary>Button released: swing begins (visual starts here; used to measure input latency).</summary>
        SwingStarted,
        Hit,
        /// <summary>Swung way too early: missed the ball.</summary>
        Whiff,
        /// <summary>Ball arrived and the player did not swing.</summary>
        NoSwing,
        /// <summary>Ball arrived out of reach.</summary>
        OutOfReach,
        ServeToss
    }

    public struct PlayerEvent
    {
        public PlayerEventType Type;
        public long Tick;
        public TimingGrade Grade;
        /// <summary>Release time minus ideal release time (s). Negative = early.</summary>
        public float TimingError;
        public ShotType Shot;
        public bool Smash;
        public bool Dive;
        public bool ArcRaised;
        public Vec3 Target;
        public Vec3 ContactPoint;
        public float BallSpeed;
        /// <summary>For SwingStarted: the release serial that triggered it.</summary>
        public int ReleaseSerial;
    }

    public sealed class PlayerEventBuffer
    {
        private readonly PlayerEvent[] _events;
        public int Count { get; private set; }
        /// <summary>Events ever added (never reset). Lets several readers consume new events independently of Clear.</summary>
        public long TotalAdded { get; private set; }

        public PlayerEventBuffer(int capacity = 16) => _events = new PlayerEvent[capacity];

        public ref readonly PlayerEvent this[int i] => ref _events[i];

        public void Add(in PlayerEvent e)
        {
            if (Count >= _events.Length) return;
            _events[Count++] = e;
            TotalAdded++;
        }

        public void Clear() => Count = 0;
    }
}
