using System;

namespace BeachTennis.Core
{
    /// <summary>
    /// Something advanced by the fixed-rate simulation clock.
    /// </summary>
    public interface ISimulationTickable
    {
        /// <param name="tick">Index of the tick being simulated (0-based, monotonic).</param>
        /// <param name="deltaTime">Fixed tick duration in seconds.</param>
        void Tick(long tick, float deltaTime);
    }

    /// <summary>
    /// Fixed-timestep accumulator that decouples simulation rate from render rate.
    /// The simulation always advances in identical steps (default 120 Hz), so it behaves the same
    /// at 30, 60 or 120 FPS and stays reproducible for tests and future networking.
    /// Presentation interpolates between the last two simulated states using <see cref="Alpha"/>.
    /// Pure C#: no UnityEngine dependency, no allocations.
    /// </summary>
    public sealed class SimulationClock : ISimulationStats
    {
        public const int DefaultTickRate = 120;

        /// <summary>Longest real frame we will try to catch up on. Longer hitches are dropped.</summary>
        public const double DefaultMaxFrameTime = 0.25;

        public int TickRate { get; }
        public double TickDelta { get; }
        public float TickDeltaF { get; }
        public int MaxTicksPerFrame { get; }
        public double MaxFrameTime { get; }

        /// <summary>Number of ticks simulated so far.</summary>
        public long TickCount { get; private set; }

        /// <summary>Ticks executed during the last <see cref="Advance"/> call.</summary>
        public int LastFrameTicks { get; private set; }

        /// <summary>Total simulated seconds discarded because the frame was too long (hitch protection).</summary>
        public double DroppedTime { get; private set; }

        /// <summary>Interpolation factor in [0,1) between the previous and the current simulated state.</summary>
        public float Alpha => (float)(_accumulator / TickDelta);

        private double _accumulator;

        public SimulationClock(int tickRate = DefaultTickRate, int maxTicksPerFrame = 8, double maxFrameTime = DefaultMaxFrameTime)
        {
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (maxTicksPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame));
            if (maxFrameTime <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrameTime));

            TickRate = tickRate;
            TickDelta = 1.0 / tickRate;
            TickDeltaF = (float)TickDelta;
            MaxTicksPerFrame = maxTicksPerFrame;
            MaxFrameTime = maxFrameTime;
        }

        /// <summary>
        /// Accumulates real frame time and runs as many fixed ticks as fit.
        /// Returns the number of ticks executed this frame.
        /// </summary>
        public int Advance(double frameDeltaTime, ISimulationTickable target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (frameDeltaTime < 0) frameDeltaTime = 0;

            if (frameDeltaTime > MaxFrameTime)
            {
                DroppedTime += frameDeltaTime - MaxFrameTime;
                frameDeltaTime = MaxFrameTime;
            }

            _accumulator += frameDeltaTime;

            int ticks = 0;
            // Small epsilon so 1/120 + 1/120 accumulated in doubles still yields exactly 2 ticks.
            const double epsilon = 1e-9;
            while (_accumulator + epsilon >= TickDelta)
            {
                if (ticks == MaxTicksPerFrame)
                {
                    // Spiral-of-death protection: discard the backlog instead of falling further behind.
                    DroppedTime += _accumulator;
                    _accumulator = 0;
                    break;
                }

                target.Tick(TickCount, TickDeltaF);
                TickCount++;
                ticks++;
                _accumulator -= TickDelta;
            }

            if (_accumulator < 0) _accumulator = 0;
            LastFrameTicks = ticks;
            return ticks;
        }

        public void Reset()
        {
            _accumulator = 0;
            TickCount = 0;
            LastFrameTicks = 0;
            DroppedTime = 0;
        }
    }
}
