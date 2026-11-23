using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using NUnit.Framework;

namespace BeachTennis.Tests
{
    public class PlayerSystemTests
    {
        private const float Dt = 1f / 120f;
        private static readonly CourtDimensions Court = CourtDimensions.For(CourtFormat.Doubles);

        /// <summary>Ball + one south player, ticked in the same order as the real scheduler.</summary>
        private sealed class World
        {
            public readonly BallSystem Ball = new BallSystem(BallPhysicsConfig.Default, Court);
            public readonly PlayerSystem Player;
            public long Tick;

            public World(Vec3 playerStart, PlayerConfig? config = null)
            {
                Player = new PlayerSystem(Ball, config ?? PlayerConfig.Default, Court, -1, playerStart, 7);
            }

            public void Step()
            {
                Player.Events.Clear();
                Ball.Events.Clear();
                Ball.Tick(Tick, Dt);
                Player.Tick(Tick, Dt);
                Tick++;
            }

            public double Now => PlayerSystem.TimeOfTick(Tick - 1, Dt);

            /// <summary>Machine shot from the north half to a target on the south half.</summary>
            public void FireAtSouth(Vec3 from, Vec3 target, ShotArc arc, float apexOrSpeed)
            {
                var req = new ShotRequest { From = from, Target = target, Arc = arc, ApexHeight = apexOrSpeed, Speed = apexOrSpeed, EnforceNetClearance = true, MinNetClearance = 0.1f };
                var s = ShotSolver.Solve(req, Ball.Config, Court, Dt);
                Ball.Launch(from, s.Velocity, s.Spin);
            }

            public bool StepUntil(System.Func<PlayerEvent, bool> predicate, int maxTicks, out PlayerEvent found)
            {
                for (int i = 0; i < maxTicks; i++)
                {
                    Step();
                    for (int e = 0; e < Player.Events.Count; e++)
                        if (predicate(Player.Events[e])) { found = Player.Events[e]; return true; }
                }
                found = default;
                return false;
            }

            public void Release(double time, ShotType type = ShotType.Main)
            {
                Player.Command.ShotHeld = false;
                Player.Command.ReleaseSerial++;
                Player.Command.ReleaseTime = time;
                Player.Command.ReleaseType = type;
            }

            /// <summary>Holds the button (assist on), then releases exactly at ideal time + offset.</summary>
            public bool PlayShot(float releaseOffset, out PlayerEvent hit, ShotType type = ShotType.Main)
            {
                Player.Command.ShotHeld = true;
                Player.Command.HeldType = type;
                Step();
                Assert.IsTrue(Player.State.Plan.Valid, "no contact plan");
                double ideal = Player.State.Plan.Time - Player.Config.SwingLead;
                double releaseAt = ideal + releaseOffset;
                while (Now + Dt < releaseAt) Step();
                Release(releaseAt, type);
                return StepUntil(e => e.Type == PlayerEventType.Hit, 400, out hit);
            }

            /// <summary>Flies the ball until it touches the sand and returns that point.</summary>
            public Vec3 LandingAfterHit()
            {
                for (int i = 0; i < 1500; i++)
                {
                    Step();
                    for (int e = 0; e < Ball.Events.Count; e++)
                        if (Ball.Events[e].Type == BallEventType.SandContact) return Ball.Events[e].Position;
                }
                Assert.Fail("ball never landed");
                return default;
            }
        }

        [Test]
        public void ReachesTopSpeedOnSandAndCoversExpectedDistance()
        {
            var w = new World(new Vec3(-4f, 0f, -6f));
            w.Player.Command.MoveX = 1f;
            for (int i = 0; i < 120; i++) w.Step();

            var cfg = PlayerConfig.Default;
            Assert.AreEqual(cfg.MaxSpeed, w.Player.State.Velocity.x, 0.01f);
            Assert.AreEqual(-4f + cfg.TravelDistance(1f), w.Player.State.Position.x, 0.08f);
        }

        [Test]
        public void BrakesToAStop()
        {
            var w = new World(new Vec3(-4f, 0f, -6f));
            w.Player.Command.MoveX = 1f;
            for (int i = 0; i < 120; i++) w.Step();
            w.Player.Command.MoveX = 0f;
            for (int i = 0; i < 60; i++) w.Step();
            Assert.Less(w.Player.State.Velocity.Magnitude, 0.01f);
        }

        [Test]
        public void CannotCrossTheNet()
        {
            var w = new World(new Vec3(0f, 0f, -2f));
            w.Player.Command.MoveZ = 1f;
            for (int i = 0; i < 240; i++) w.Step();
            Assert.Less(w.Player.State.Position.z, 0f);
        }

        [Test]
        public void PlansAReachableContactForAnIncomingBall()
        {
            var w = new World(new Vec3(0f, 0f, -5f));
            w.FireAtSouth(new Vec3(0f, 1.3f, 5f), new Vec3(1.5f, 0f, -6f), ShotArc.Apex, 3.5f);
            w.Step();

            ContactPlan plan = w.Player.State.Plan;
            Assert.IsTrue(plan.Valid);
            Assert.IsTrue(plan.Reachable);
            Assert.Less(plan.BallPosition.z, 0f);
            Assert.That(plan.BallPosition.y, Is.InRange(0.25f, 2.95f));
        }

        [Test]
        public void PerfectReleaseReturnsTheBallIntoTheOpponentCourt()
        {
            var w = new World(new Vec3(0f, 0f, -5f));
            w.FireAtSouth(new Vec3(0f, 1.3f, 5f), new Vec3(1.5f, 0f, -6f), ShotArc.Apex, 3.5f);

            Assert.IsTrue(w.PlayShot(0f, out PlayerEvent hit));
            Assert.AreEqual(TimingGrade.Perfect, hit.Grade);
            Assert.Greater(w.Ball.State.Velocity.z, 0f, "ball goes back north");

            Vec3 landing = w.LandingAfterHit();
            Assert.Greater(landing.z, 0f);
            Assert.IsTrue(Court.IsInside(landing.x, landing.z), $"landed out at {landing}");
        }

        [Test]
        public void ReleaseGradesFollowTiming()
        {
            var cfg = PlayerConfig.Default;
            Assert.AreEqual(TimingGrade.Perfect, ShotSelector.Grade(0.02f, cfg));
            Assert.AreEqual(TimingGrade.Good, ShotSelector.Grade(-0.06f, cfg));
            Assert.AreEqual(TimingGrade.Early, ShotSelector.Grade(-0.15f, cfg));
            Assert.AreEqual(TimingGrade.Late, ShotSelector.Grade(0.15f, cfg));
        }

        [Test]
        public void HoldingThroughContactSwingsLate()
        {
            var w = new World(new Vec3(0f, 0f, -5f));
            w.FireAtSouth(new Vec3(0f, 1.3f, 5f), new Vec3(-1f, 0f, -5.5f), ShotArc.Apex, 3.5f);
            w.Player.Command.ShotHeld = true;

            Assert.IsTrue(w.StepUntil(e => e.Type == PlayerEventType.Hit, 600, out PlayerEvent hit));
            Assert.AreEqual(TimingGrade.Late, hit.Grade);
        }

        [Test]
        public void NoInputMeansNoSwingAndTheBallLandsOnOurSide()
        {
            var w = new World(new Vec3(0f, 0f, -5f));
            w.FireAtSouth(new Vec3(0f, 1.3f, 5f), new Vec3(1.5f, 0f, -6f), ShotArc.Apex, 3.5f);

            Assert.IsTrue(w.StepUntil(e => e.Type == PlayerEventType.NoSwing, 600, out _));
            Assert.Less(w.LandingAfterHit().z, 0f);
        }

        [Test]
        public void ReleasingFarTooEarlyWhiffs()
        {
            var w = new World(new Vec3(0f, 0f, -5f));
            w.FireAtSouth(new Vec3(0f, 1.2f, 6f), new Vec3(0f, 0f, -6f), ShotArc.Apex, 6f); // slow lob
            w.Step();
            w.Release(w.Now);

            Assert.IsTrue(w.StepUntil(e => e.Type == PlayerEventType.Whiff, 5, out _));
        }

        [Test]
        public void SlightlyOutOfReachTriggersADive()
        {
            var cfg = PlayerConfig.Default;
            var w = new World(new Vec3(0f, 0f, -5f), cfg);
            w.FireAtSouth(new Vec3(0f, 1.3f, 5f), new Vec3(1.5f, 0f, -6f), ShotArc.Apex, 3.5f);
            w.Step();
            ContactPlan plan = w.Player.State.Plan;

            // Stand still 1.5 m to the side of the contact point (reach 1.0 + dive 1.0).
            w.Player.Teleport(new Vec3(plan.BallPosition.x - 1.5f, 0f, plan.BallPosition.z));
            double ideal = plan.Time - cfg.SwingLead;
            while (w.Now + Dt < ideal) w.Step();
            w.Release(ideal);

            Assert.IsTrue(w.StepUntil(e => e.Type == PlayerEventType.Hit, 200, out PlayerEvent hit));
            Assert.IsTrue(hit.Dive);
            Assert.AreEqual(PlayerPhase.Recovering, w.Player.State.Phase);
        }

        [Test]
        public void FarOutOfReachIsAMiss()
        {
            var w = new World(new Vec3(0f, 0f, -5f));
            w.FireAtSouth(new Vec3(0f, 1.3f, 5f), new Vec3(3.5f, 0f, -6f), ShotArc.Apex, 3.5f);
            w.Step();
            ContactPlan plan = w.Player.State.Plan;
            w.Player.Teleport(new Vec3(plan.BallPosition.x - 4f, 0f, plan.BallPosition.z));
            double ideal = plan.Time - w.Player.Config.SwingLead;
            while (w.Now + Dt < ideal) w.Step();
            w.Release(ideal);

            Assert.IsTrue(w.StepUntil(e => e.Type == PlayerEventType.OutOfReach, 200, out _));
        }

        [Test]
        public void HighBallWithMainShotIsASmash()
        {
            var w = new World(new Vec3(0f, 0f, -4f));
            w.FireAtSouth(new Vec3(0f, 1.2f, 6f), new Vec3(0f, 0f, -4f), ShotArc.Apex, 6.5f); // lob
            Assert.IsTrue(w.PlayShot(0f, out PlayerEvent hit));
            Assert.IsTrue(hit.Smash);
            Assert.Greater(hit.BallSpeed, 20f);
        }

        [Test]
        public void ServeTossAndPerfectHitLandsInTheOpponentCourt()
        {
            var w = new World(new Vec3(1f, 0f, -6f));
            w.Player.BeginServe(1f);
            Assert.IsTrue(w.Player.IsServing);

            Assert.IsTrue(w.PlayShot(0f, out PlayerEvent hit), "serve not hit");
            Assert.IsFalse(w.Player.IsServing);
            Vec3 landing = w.LandingAfterHit();
            Assert.Greater(landing.z, 0f);
            Assert.IsTrue(Court.IsInside(landing.x, landing.z), $"serve out at {landing}");
        }

        [Test]
        public void UnhitTossIsCaughtForANewToss()
        {
            var w = new World(new Vec3(1f, 0f, -6f));
            w.Player.BeginServe(1f);
            w.Player.Command.ShotHeld = true;
            w.Step();                                // toss
            Assert.AreEqual(BallMode.Flight, w.Ball.State.Mode);
            w.Player.Command.ShotHeld = false;       // never release-swing: just let go without a release
            for (int i = 0; i < 240; i++) w.Step();
            Assert.AreEqual(BallMode.Inactive, w.Ball.State.Mode);
            Assert.IsTrue(w.Player.IsServing);
        }

        [Test]
        public void RallyOutcomeIsDeterministic()
        {
            Vec3 Run()
            {
                var w = new World(new Vec3(0.5f, 0f, -5f));
                w.FireAtSouth(new Vec3(0f, 1.3f, 5f), new Vec3(-2f, 0f, -6.5f), ShotArc.Drive, 13f);
                w.Player.Command.AimX = 0.7f;
                w.PlayShot(0.05f, out _);
                return w.LandingAfterHit();
            }
            Assert.AreEqual(Run(), Run());
        }
    }
}
