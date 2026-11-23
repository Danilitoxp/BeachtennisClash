using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using BeachTennis.Simulation.Rules;
using NUnit.Framework;

namespace BeachTennis.Tests
{
    /// <summary>Point decisions (ITF Rules of Beach Tennis 2026, R18–R24, App. I) in simulated singles points.</summary>
    public class PointRefereeTests
    {
        private const float Dt = 1f / 120f;
        private static readonly CourtDimensions Court = CourtDimensions.For(CourtFormat.Doubles);
        /// <summary>A receiver who can actually reach a short serve (aim -1 lands ~4 m from the net).</summary>
        private static readonly Vec3 ReceivingPosition = new Vec3(0.9f, 0f, 5f);

        private sealed class Match
        {
            public readonly BallSystem Ball = new BallSystem(BallPhysicsConfig.Default, Court);
            public readonly PlayerSystem Server;   // team 0, south
            public readonly PlayerSystem Receiver; // team 1, north
            public readonly MatchScore Score;
            public readonly PointReferee Referee;
            public long Tick;

            public Match()
            {
                Server = new PlayerSystem(Ball, PlayerConfig.Default, Court, -1, new Vec3(1f, 0f, -8f), 1);
                Receiver = new PlayerSystem(Ball, PlayerConfig.Default, Court, +1, new Vec3(3f, 0f, 7.5f), 2);
                Score = new MatchScore(RuleSet.ItfOfficial(EventCategory.Women), 1, firstServingTeam: 0, southTeam: 0);
                Referee = new PointReferee(Score, Ball, new[] { Server, Receiver }, new[] { new PlayerId(0, 0), new PlayerId(1, 0) }, Court);
                Server.BeginServe(1f);
                Referee.BeginPoint();
            }

            public double Now => PlayerSystem.TimeOfTick(Tick - 1, Dt);

            public void Step()
            {
                Ball.Events.Clear();
                Server.Events.Clear();
                Receiver.Events.Clear();
                Ball.Tick(Tick, Dt);
                Server.Tick(Tick, Dt);
                Receiver.Tick(Tick, Dt);
                Referee.Tick(Tick, Dt);
                Tick++;
            }

            public bool StepUntilDecided(int maxTicks = 2000)
            {
                for (int i = 0; i < maxTicks && Referee.Phase != RallyPhase.Decided; i++) Step();
                return Referee.Phase == RallyPhase.Decided;
            }

            public static void Release(PlayerSystem p, double time)
            {
                p.Command.ShotHeld = false;
                p.Command.ReleaseSerial++;
                p.Command.ReleaseTime = time;
                p.Command.ReleaseType = ShotType.Main;
            }

            /// <summary>Holds (toss / assist) and releases at the ideal moment.</summary>
            public void PlayPerfect(PlayerSystem p, int maxTicks = 600)
            {
                p.Command.ShotHeld = true;
                for (int i = 0; i < maxTicks && !p.State.Plan.Valid; i++) Step();
                Assert.IsTrue(p.State.Plan.Valid, "no contact plan");
                double ideal = p.State.Plan.Time - p.Config.SwingLead;
                while (Now + Dt < ideal) Step();
                Release(p, ideal);
                // The release is SwingLead before contact: keep going until the ball is actually struck.
                for (int i = 0; i < 120 && !p.State.HasHitCurrentBall && Referee.Phase != RallyPhase.Decided; i++) Step();
            }

            public void Serve()
            {
                PlayPerfect(Server);
                for (int i = 0; i < 200 && Referee.Phase == RallyPhase.AwaitingServe; i++) Step();
                Assert.AreEqual(RallyPhase.InPlay, Referee.Phase, "serve not struck");
            }
        }

        [Test]
        public void UnreturnedServeIsAPointForTheServer()
        {
            var m = new Match();
            m.Serve();
            Assert.IsTrue(m.StepUntilDecided());
            Assert.AreEqual(0, m.Referee.LastWinner);
            Assert.AreEqual(PointReason.BallLandedInOwnCourt, m.Referee.LastReason);
            Assert.AreEqual(1, m.Score.Points[0]);
        }

        [Test]
        public void MissingTheBallOnServeIsAFault()
        {
            var m = new Match();
            m.Server.Command.ShotHeld = true;
            m.Step();                              // toss
            Match.Release(m.Server, m.Now);        // swing immediately: far too early
            Assert.IsTrue(m.StepUntilDecided(10));
            Assert.AreEqual(1, m.Referee.LastWinner);
            Assert.AreEqual(PointReason.ServeMissed, m.Referee.LastReason);
        }

        [Test]
        public void LettingTheTossFallIsNotAFault()
        {
            var m = new Match();
            m.Server.Command.ShotHeld = true;
            m.Step();
            m.Server.Command.ShotHeld = false;     // let go without swinging
            for (int i = 0; i < 300; i++) m.Step();
            Assert.AreEqual(RallyPhase.AwaitingServe, m.Referee.Phase);
            Assert.IsTrue(m.Server.IsServing);
        }

        [Test]
        public void ServeIntoTheNetLosesThePoint()
        {
            var m = new Match();
            m.Serve();
            m.Ball.Launch(new Vec3(0f, 1.0f, -3f), new Vec3(0f, 0f, 14f), Vec3.Zero); // force it into the net body
            Assert.IsTrue(m.StepUntilDecided());
            Assert.AreEqual(1, m.Referee.LastWinner);
            Assert.AreEqual(PointReason.ServeNet, m.Referee.LastReason);
        }

        [Test]
        public void ReturnThatBeatsTheServerWinsForTheReceiver()
        {
            var m = new Match();
            m.Receiver.Teleport(ReceivingPosition);
            m.Server.Command.AimY = -1f; // short serve, easy to reach
            m.Serve();
            m.PlayPerfect(m.Receiver);
            Assert.IsTrue(m.StepUntilDecided());
            Assert.AreEqual(1, m.Referee.LastWinner);
            Assert.That(m.Referee.LastReason, Is.EqualTo(PointReason.BallLandedInOwnCourt).Or.EqualTo(PointReason.BodyHit));
        }

        [Test]
        public void ReturnHitOutLosesForTheHitter()
        {
            var m = new Match();
            m.Receiver.Teleport(ReceivingPosition);
            m.Server.Command.AimY = -1f;
            m.Serve();
            m.PlayPerfect(m.Receiver);
            Assert.AreEqual(RallyPhase.InPlay, m.Referee.Phase);
            // Replace the return with one that flies far past the south baseline (still the receiver's shot).
            m.Ball.Launch(m.Ball.State.Position, new Vec3(0f, 6f, -20f), Vec3.Zero);
            Assert.IsTrue(m.StepUntilDecided());
            Assert.AreEqual(0, m.Referee.LastWinner);
            Assert.AreEqual(PointReason.Out, m.Referee.LastReason);
        }

        [Test]
        public void BallTouchingABodyLosesForThatPlayersTeam()
        {
            var m = new Match();
            m.Serve();
            // Find where the serve is at chest height on the north half and stand the receiver there.
            var path = new BallState[600];
            int n = BallPhysics.Predict(m.Ball.State, m.Ball.Config, Court, Dt, path);
            Vec3 chest = default;
            for (int i = 0; i < n; i++)
                if (path[i].Position.z > 3f && path[i].Position.y < 1.3f) { chest = path[i].Position; break; }
            m.Receiver.Teleport(new Vec3(chest.x, 0f, chest.z));

            Assert.IsTrue(m.StepUntilDecided());
            Assert.AreEqual(0, m.Referee.LastWinner);
            Assert.AreEqual(PointReason.BodyHit, m.Referee.LastReason);
        }

        [Test]
        public void ProhibitedZoneHoldsUntilTheServeIsStruck()
        {
            var m = new Match();
            m.Receiver.Command.MoveZ = -1f; // run towards the net
            for (int i = 0; i < 240; i++) m.Step();
            Assert.GreaterOrEqual(m.Receiver.State.Position.z, 3f - 1e-4f, "kept out of the 3 m zone");

            m.Serve();
            m.Receiver.Command.ShotHeld = false;
            m.Receiver.Command.MoveZ = -1f;
            for (int i = 0; i < 240 && m.Referee.Phase == RallyPhase.InPlay; i++) m.Step();
            Assert.Less(m.Receiver.State.Position.z, 3f, "free to approach once the ball is struck");
        }

        [Test]
        public void SameTeamTouchingTwiceLosesThePoint()
        {
            var m = new Match();
            m.Receiver.Teleport(ReceivingPosition);
            m.Server.Command.AimY = -1f;
            m.Serve();
            m.PlayPerfect(m.Receiver);
            Assert.AreEqual(RallyPhase.InPlay, m.Referee.Phase);
            // Send the ball back up on the receiver's own half so the receiver can hit it again.
            m.Ball.Launch(new Vec3(m.Receiver.State.Position.x, 1.5f, 3f), new Vec3(0f, 4f, 3f), Vec3.Zero);
            m.PlayPerfect(m.Receiver);
            Assert.IsTrue(m.StepUntilDecided(50));
            Assert.AreEqual(0, m.Referee.LastWinner);
            Assert.AreEqual(PointReason.DoubleTouch, m.Referee.LastReason);
        }
    }
}
