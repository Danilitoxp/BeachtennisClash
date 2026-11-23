using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using NUnit.Framework;

namespace BeachTennis.Tests
{
    public class BallPhysicsTests
    {
        private const float Dt = 1f / 120f;
        private static readonly CourtDimensions Court = CourtDimensions.For(CourtFormat.Doubles);

        private static BallState Fly(BallState s, BallPhysicsConfig c, int ticks, BallEventBuffer events = null)
        {
            for (int i = 0; i < ticks; i++) BallPhysics.Step(ref s, c, Court, Dt, events, i);
            return s;
        }

        private static Vec3 LandingPoint(BallState s, BallPhysicsConfig c)
        {
            var events = new BallEventBuffer();
            for (int i = 0; i < 2000 && s.BounceCount == 0; i++)
                BallPhysics.Step(ref s, c, Court, Dt, events, i, collideNet: false);
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == BallEventType.SandContact) return events[i].Position;
            Assert.Fail("Ball never landed");
            return default;
        }

        [Test]
        public void VacuumMatchesAnalyticBallistics()
        {
            var c = BallPhysicsConfig.Vacuum;
            var s = BallState.Launch(new Vec3(0f, 1f, -6f), new Vec3(0f, 5f, 10f), Vec3.Zero);

            s = Fly(s, c, 120); // exactly 1 s

            Assert.AreEqual(4f, s.Position.z, 1e-3f);                  // -6 + 10
            Assert.AreEqual(1f + 5f - 0.5f * 9.81f, s.Position.y, 1e-3f);
            Assert.AreEqual(5f - 9.81f, s.Velocity.y, 1e-3f);
        }

        [Test]
        public void DragShortensTheShot()
        {
            var launch = BallState.Launch(new Vec3(0f, 1f, -7f), new Vec3(0f, 8f, 15f), Vec3.Zero);
            float vacuum = LandingPoint(launch, BallPhysicsConfig.Vacuum).z;
            float air = LandingPoint(launch, BallPhysicsConfig.Default).z;

            Assert.Less(air, vacuum - 1f);
        }

        [Test]
        public void TopspinDipsAndBackspinFloats()
        {
            var c = BallPhysicsConfig.Default;
            var v = new Vec3(0f, 6f, 14f);
            var p = new Vec3(0f, 1f, -7f);

            float flat = LandingPoint(BallState.Launch(p, v, Vec3.Zero), c).z;
            float top = LandingPoint(BallState.Launch(p, v, BallSpin.FromComponents(v, 30f, 0f)), c).z;
            float back = LandingPoint(BallState.Launch(p, v, BallSpin.FromComponents(v, -20f, 0f)), c).z;

            Assert.Less(top, flat - 0.5f);
            Assert.Greater(back, flat + 0.5f);
        }

        [Test]
        public void PositiveSidespinCurvesRight()
        {
            var c = BallPhysicsConfig.Default;
            var v = new Vec3(0f, 6f, 14f); // travelling +Z, so "right" is +X
            Vec3 landing = LandingPoint(BallState.Launch(new Vec3(0f, 1f, -7f), v, BallSpin.FromComponents(v, 0f, 20f)), c);

            Assert.Greater(landing.x, 0.3f);
        }

        [Test]
        public void SimulationIsDeterministic()
        {
            var c = BallPhysicsConfig.Default;
            var v = new Vec3(1.3f, 7.1f, 13.7f);
            var a = BallState.Launch(new Vec3(-1f, 1.1f, -7f), v, BallSpin.FromComponents(v, 25f, -8f));
            var b = a;

            for (int i = 0; i < 900; i++)
            {
                BallPhysics.Step(ref a, c, Court, Dt);
                BallPhysics.Step(ref b, c, Court, Dt);
                Assert.AreEqual(a.Position, b.Position, $"tick {i}");
                Assert.AreEqual(a.Velocity, b.Velocity, $"tick {i}");
            }
        }

        [Test]
        public void PredictionEqualsLiveSimulation()
        {
            var c = BallPhysicsConfig.Default;
            var v = new Vec3(0.5f, 6f, 15f);
            var system = new BallSystem(c, Court);
            system.Launch(new Vec3(0f, 1.2f, -7f), v, BallSpin.FromComponents(v, 20f, 5f));

            var predicted = new BallState[600];
            int n = BallPhysics.Predict(system.State, c, Court, Dt, predicted, stopAtSand: false);
            Assert.Greater(n, 100);

            for (int i = 0; i < n; i++)
            {
                system.Tick(i, Dt);
                Assert.AreEqual(predicted[i].Position, system.State.Position, $"tick {i}");
                Assert.AreEqual(predicted[i].Mode, system.State.Mode, $"tick {i}");
            }
        }

        [Test]
        public void SandKillsTheBounceAndBallComesToRest()
        {
            var c = BallPhysicsConfig.Default;
            var events = new BallEventBuffer();
            var s = BallState.Launch(new Vec3(2f, 2f, 3f), Vec3.Zero, Vec3.Zero);

            float apexAfterFirstBounce = 0f;
            for (int i = 0; i < 1200 && s.Mode != BallMode.Resting; i++)
            {
                BallPhysics.Step(ref s, c, Court, Dt, events, i);
                if (s.BounceCount == 1 && s.Position.y > apexAfterFirstBounce) apexAfterFirstBounce = s.Position.y;
            }

            Assert.AreEqual(BallMode.Resting, s.Mode);
            Assert.AreEqual(BallEventType.SandContact, events[0].Type);
            Assert.AreEqual(2f, events[0].Position.x, 1e-4f);
            Assert.AreEqual(0f, events[0].Position.y);
            // Restitution 0.3 → rebound ≈ 0.09 × drop height (2 m - radius).
            Assert.That(apexAfterFirstBounce - c.Radius, Is.InRange(0.10f, 0.22f));
        }

        [Test]
        public void LowBallHitsNetBodyAndStaysOnItsSide()
        {
            var c = BallPhysicsConfig.Default;
            var events = new BallEventBuffer();
            var s = BallState.Launch(new Vec3(0f, 1f, -3f), new Vec3(0f, 1f, 12f), Vec3.Zero);

            s = Fly(s, c, 240, events);

            Assert.IsTrue(HasEvent(events, BallEventType.NetBody));
            Assert.IsFalse(HasEvent(events, BallEventType.CrossedNet));
            Assert.Less(s.Position.z, 0f);
        }

        [Test]
        public void BallJustOverTheTapeClipsTheCordAndContinues()
        {
            var c = BallPhysicsConfig.Vacuum;
            var events = new BallEventBuffer();
            // Horizontal launch timed so the center crosses z = 0 at 1.715 m (tape at 1.70, radius 3.35 cm).
            const float t = 0.1f;
            float y0 = 1.715f + 0.5f * c.Gravity * t * t;
            var s = BallState.Launch(new Vec3(0f, y0, -2f), new Vec3(0f, 0f, 20f), Vec3.Zero);

            s = Fly(s, c, 24, events);

            Assert.IsTrue(HasEvent(events, BallEventType.NetCord));
            Assert.IsTrue(HasEvent(events, BallEventType.CrossedNet));
            Assert.Greater(s.Position.z, 0f);
            Assert.Less(s.Velocity.z, 20f);
        }

        [Test]
        public void HighBallCrossesCleanly()
        {
            var c = BallPhysicsConfig.Default;
            var events = new BallEventBuffer();
            var s = BallState.Launch(new Vec3(0f, 2.5f, -3f), new Vec3(0f, 0.5f, 15f), Vec3.Zero);

            Fly(s, c, 60, events);

            Assert.IsTrue(HasEvent(events, BallEventType.CrossedNet));
            Assert.IsFalse(HasEvent(events, BallEventType.NetCord));
            Assert.IsFalse(HasEvent(events, BallEventType.NetBody));
        }

        [Test]
        public void FastBallDoesNotTunnelThroughNet()
        {
            // 60 m/s moves 0.5 m per tick: much more than the ball diameter.
            var c = BallPhysicsConfig.Default;
            var events = new BallEventBuffer();
            var s = BallState.Launch(new Vec3(0f, 1.2f, -2f), new Vec3(0f, 0f, 60f), Vec3.Zero);

            Fly(s, c, 30, events);

            Assert.IsTrue(HasEvent(events, BallEventType.NetBody));
        }

        private static bool HasEvent(BallEventBuffer events, BallEventType type)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == type) return true;
            return false;
        }
    }
}
