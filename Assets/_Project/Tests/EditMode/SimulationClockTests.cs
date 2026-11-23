using System.Collections.Generic;
using BeachTennis.Core;
using NUnit.Framework;

namespace BeachTennis.Tests
{
    public class SimulationClockTests
    {
        private sealed class Recorder : ISimulationTickable
        {
            public readonly List<long> Ticks = new List<long>();
            public float LastDelta;

            public void Tick(long tick, float deltaTime)
            {
                Ticks.Add(tick);
                LastDelta = deltaTime;
            }
        }

        [Test]
        public void RunsOneTickPerTickDuration()
        {
            var clock = new SimulationClock(120);
            var rec = new Recorder();

            int ticks = clock.Advance(1.0 / 120.0, rec);

            Assert.AreEqual(1, ticks);
            Assert.AreEqual(1, clock.TickCount);
            Assert.AreEqual(1f / 120f, rec.LastDelta, 1e-7f);
        }

        [Test]
        public void SimulatesSameTickCountRegardlessOfFrameRate()
        {
            // One simulated second rendered at 30, 60 and 120 FPS must produce exactly 120 ticks each.
            foreach (int fps in new[] { 30, 60, 120 })
            {
                var clock = new SimulationClock(120);
                var rec = new Recorder();
                for (int f = 0; f < fps; f++) clock.Advance(1.0 / fps, rec);
                Assert.AreEqual(120, clock.TickCount, $"at {fps} FPS");
            }
        }

        [Test]
        public void AccumulatesPartialFrames()
        {
            var clock = new SimulationClock(120);
            var rec = new Recorder();

            Assert.AreEqual(0, clock.Advance(0.004, rec)); // 4 ms < 8.33 ms
            Assert.AreEqual(1, clock.Advance(0.005, rec)); // 9 ms total
            Assert.That(clock.Alpha, Is.InRange(0f, 1f));
        }

        [Test]
        public void TickIndicesAreSequential()
        {
            var clock = new SimulationClock(120);
            var rec = new Recorder();
            for (int i = 0; i < 50; i++) clock.Advance(0.0173, rec);

            for (int i = 0; i < rec.Ticks.Count; i++)
                Assert.AreEqual(i, rec.Ticks[i]);
        }

        [Test]
        public void LimitsTicksPerFrameAndDropsBacklog()
        {
            var clock = new SimulationClock(120, maxTicksPerFrame: 8, maxFrameTime: 1.0);
            var rec = new Recorder();

            int ticks = clock.Advance(0.5, rec); // would be 60 ticks

            Assert.AreEqual(8, ticks);
            Assert.Greater(clock.DroppedTime, 0.0);
            Assert.AreEqual(0f, clock.Alpha, 1e-6f);
        }

        [Test]
        public void ClampsVeryLongFrames()
        {
            var clock = new SimulationClock(120, maxTicksPerFrame: 1000, maxFrameTime: 0.25);
            var rec = new Recorder();

            clock.Advance(5.0, rec);

            Assert.AreEqual(30, clock.TickCount); // 0.25 s * 120 Hz
            Assert.AreEqual(4.75, clock.DroppedTime, 1e-6);
        }

        [Test]
        public void NegativeDeltaDoesNothing()
        {
            var clock = new SimulationClock(120);
            Assert.AreEqual(0, clock.Advance(-1, new Recorder()));
        }
    }

    public class SimulationSchedulerTests
    {
        private sealed class Ordered : ISimulationTickable
        {
            private readonly List<string> _log;
            private readonly string _name;
            public Ordered(List<string> log, string name) { _log = log; _name = name; }
            public void Tick(long tick, float deltaTime) => _log.Add(_name);
        }

        [Test]
        public void RunsSystemsInRegistrationOrder()
        {
            var log = new List<string>();
            var scheduler = new SimulationScheduler();
            for (int i = 0; i < 12; i++) scheduler.Register(new Ordered(log, i.ToString())); // forces a resize

            scheduler.Tick(0, 0.01f);

            Assert.AreEqual(12, log.Count);
            for (int i = 0; i < 12; i++) Assert.AreEqual(i.ToString(), log[i]);
        }

        [Test]
        public void UnregisterKeepsOrder()
        {
            var log = new List<string>();
            var scheduler = new SimulationScheduler();
            var a = new Ordered(log, "a");
            var b = new Ordered(log, "b");
            var c = new Ordered(log, "c");
            scheduler.Register(a);
            scheduler.Register(b);
            scheduler.Register(c);

            Assert.IsTrue(scheduler.Unregister(b));
            scheduler.Tick(0, 0.01f);

            CollectionAssert.AreEqual(new[] { "a", "c" }, log);
        }
    }
}
