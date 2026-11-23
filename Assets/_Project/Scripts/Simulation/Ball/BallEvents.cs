using BeachTennis.Core;

namespace BeachTennis.Simulation.Ball
{
    public enum BallEventType : byte
    {
        /// <summary>Ball touched the sand. In beach tennis the first one ends the rally.</summary>
        SandContact,
        /// <summary>Ball hit the net body and was stopped.</summary>
        NetBody,
        /// <summary>Ball clipped the net tape and continued (a "let" on serve).</summary>
        NetCord,
        /// <summary>Ball center crossed the net plane (z = 0).</summary>
        CrossedNet,
        /// <summary>Ball came to rest on the sand.</summary>
        Rest
    }

    public struct BallEvent
    {
        public BallEventType Type;
        public Vec3 Position;
        public Vec3 Velocity;
        /// <summary>Simulation tick when it happened (stamped by <see cref="BallSystem"/>; 0 in pure prediction).</summary>
        public long Tick;
    }

    /// <summary>
    /// Fixed-capacity, allocation-free event list. The simulation appends during ticks; consumers
    /// (rules, presentation) read and then <see cref="Clear"/> once per frame.
    /// </summary>
    public sealed class BallEventBuffer
    {
        private readonly BallEvent[] _events;

        public int Count { get; private set; }
        public int Capacity => _events.Length;
        /// <summary>Events ever added (never reset). Lets several readers consume new events independently of Clear.</summary>
        public long TotalAdded { get; private set; }
        /// <summary>Events lost because the buffer was full since the last Clear.</summary>
        public int Overflow { get; private set; }

        public BallEventBuffer(int capacity = 32)
        {
            _events = new BallEvent[capacity];
        }

        public ref readonly BallEvent this[int index] => ref _events[index];

        public void Add(in BallEvent e)
        {
            if (Count == _events.Length)
            {
                Overflow++;
                return;
            }
            _events[Count++] = e;
            TotalAdded++;
        }

        public void Clear()
        {
            Count = 0;
            Overflow = 0;
        }
    }
}
