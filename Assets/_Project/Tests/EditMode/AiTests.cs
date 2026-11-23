using BeachTennis.AI;
using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using BeachTennis.Simulation.Rules;
using NUnit.Framework;

namespace BeachTennis.Tests
{
    /// <summary>
    /// Phase 4 validation with real simulated points (AI vs AI, no rendering). Seeds are fixed, so every
    /// run plays exactly the same points: these are regression tests, not flaky statistics.
    /// </summary>
    public class AiTests
    {
        private const int Points = 120;

        private static float WinRate(Difficulty strong, Difficulty weak, int playersPerTeam, out HeadlessMatch last)
        {
            int won = 0, total = 0;
            last = null;
            for (uint seed = 1; seed <= 2; seed++)
            {
                last = new HeadlessMatch(playersPerTeam, AiProfile.For(strong), AiProfile.For(weak), seed);
                last.PlayPoints(Points / 2);
                won += last.PointsWon[0];
                total += last.PointsWon[0] + last.PointsWon[1];
            }
            return (float)won / total;
        }

        [Test]
        public void MediumBeatsEasy() => Assert.Greater(WinRate(Difficulty.Medium, Difficulty.Easy, 1, out _), 0.6f);

        [Test]
        public void HardBeatsMedium() => Assert.Greater(WinRate(Difficulty.Hard, Difficulty.Medium, 1, out _), 0.6f);

        [Test]
        public void ProBeatsHard() => Assert.Greater(WinRate(Difficulty.Pro, Difficulty.Hard, 1, out _), 0.6f);

        [Test]
        public void EqualLevelsAreBalanced()
        {
            float rate = WinRate(Difficulty.Medium, Difficulty.Medium, 1, out _);
            Assert.That(rate, Is.InRange(0.35f, 0.65f));
        }

        [Test]
        public void HardDoublesTeamBeatsMediumDoublesTeam() =>
            Assert.Greater(WinRate(Difficulty.Hard, Difficulty.Medium, 2, out _), 0.6f);

        [Test]
        public void DoublesPartnersNeverGoForTheSameBall()
        {
            var m = new HeadlessMatch(2, AiProfile.For(Difficulty.Medium), AiProfile.For(Difficulty.Hard), 5);
            m.PlayPoints(Points);
            Assert.AreEqual(0, m.ConflictTicks, "both partners chasing one ball");
            Assert.AreEqual(0, m.Reasons[(int)PointReason.DoubleTouch], "both partners touched the ball (R24j)");
        }

        [Test]
        public void RalliesHappenBetweenGoodPlayers()
        {
            var m = new HeadlessMatch(1, AiProfile.For(Difficulty.Hard), AiProfile.For(Difficulty.Hard), 9);
            m.PlayPoints(60);
            Assert.Greater((double)m.TotalHits / m.PointsPlayed, 4.0, "average shots per point");
            int netShare = m.Reasons[(int)PointReason.Net] + m.Reasons[(int)PointReason.ServeNet];
            Assert.Less((double)netShare / m.PointsPlayed, 0.35, "most points should not end in the net");
        }

        [Test]
        public void AiMatchesAreDeterministic()
        {
            var a = new HeadlessMatch(2, AiProfile.For(Difficulty.Hard), AiProfile.For(Difficulty.Medium), 42);
            var b = new HeadlessMatch(2, AiProfile.For(Difficulty.Hard), AiProfile.For(Difficulty.Medium), 42);
            a.PlayPoints(40);
            b.PlayPoints(40);
            Assert.AreEqual(a.PointsWon[0], b.PointsWon[0]);
            Assert.AreEqual(a.TotalHits, b.TotalHits);
            Assert.AreEqual(a.Score.Games[0], b.Score.Games[0]);
        }

        [Test]
        public void FourBotsFitTheCpuBudget()
        {
            var m = new HeadlessMatch(2, AiProfile.For(Difficulty.Pro), AiProfile.For(Difficulty.Pro), 3);
            m.PlayPoints(60);
            // Two simulation ticks per 60 FPS frame. Budget (docs/02 Fase 4): < 0.5 ms per frame for 4 bots.
            double msPerFrame = m.AiTime.Elapsed.TotalMilliseconds / m.TotalTicks * 2.0;
            TestContext.WriteLine($"AI: {msPerFrame * 1000.0:0.0} µs per frame (4 bots)");
            Assert.Less(msPerFrame, 0.5);
        }

        [Test]
        public void AiWaitsItsReactionTimeBeforeGoingForTheBall()
        {
            var m = new HeadlessMatch(1, AiProfile.For(Difficulty.Easy), AiProfile.For(Difficulty.Easy), 1);
            // Receiver (team 1) faces a machine ball fired from the south half.
            PlayerSystem receiver = m.Players[1];
            receiver.SetSide(1, new Vec3(0f, 0f, 5f));
            m.Players[0].SetSide(-1, new Vec3(0f, 0f, -5f));
            var req = new ShotRequest { From = new Vec3(0f, 1.3f, -5f), Target = new Vec3(1.5f, 0f, 5.5f), Arc = ShotArc.Apex, ApexHeight = 3.5f, EnforceNetClearance = true, MinNetClearance = 0.3f };
            var s = ShotSolver.Solve(req, m.Ball.Config, m.Ball.Court, HeadlessMatch.Dt);
            m.Ball.Launch(req.From, s.Velocity, s.Spin);

            float minReaction = AiProfile.For(Difficulty.Easy).ReactionTime * 0.8f;
            int ticks = (int)(minReaction / HeadlessMatch.Dt) - 2;
            for (long t = 0; t < ticks; t++)
            {
                m.Ais[1].Tick(t, HeadlessMatch.Dt);
                m.Ball.Tick(t, HeadlessMatch.Dt);
                receiver.Tick(t, HeadlessMatch.Dt);
                Assert.IsFalse(receiver.Command.ShotHeld, $"reacted after {t * HeadlessMatch.Dt:0.000} s");
            }
        }

        [Test]
        public void TeamBrainGivesTheBallToTheHumanUnlessTheAiIsClearlyFirst()
        {
            var court = CourtDimensions.For(CourtFormat.Doubles);
            var ball = new BallSystem(BallPhysicsConfig.Default, court);
            var human = new PlayerSystem(ball, PlayerConfig.Default, court, -1, new Vec3(-1.2f, 0f, -5f), 1);
            var bot = new PlayerSystem(ball, PlayerConfig.Default, court, -1, new Vec3(1.2f, 0f, -5f), 2);
            var brain = new TeamBrain(ball, new[] { human, bot }, new[] { false, true });

            // A ball down the middle: both can reach it about equally → the human keeps it.
            var req = new ShotRequest { From = new Vec3(0f, 1.3f, 5f), Target = new Vec3(0f, 0f, -6f), Arc = ShotArc.Apex, ApexHeight = 3.5f, EnforceNetClearance = true, MinNetClearance = 0.3f };
            var s = ShotSolver.Solve(req, ball.Config, court, HeadlessMatch.Dt);
            ball.Launch(req.From, s.Velocity, s.Spin);
            ball.Tick(0, HeadlessMatch.Dt);
            human.Tick(0, HeadlessMatch.Dt);
            bot.Tick(0, HeadlessMatch.Dt);
            Assert.AreEqual(0, brain.Owner, "middle ball belongs to the human");

            // A ball wide on the bot's side, far from the human → the bot takes it and calls it.
            req.Target = new Vec3(3.2f, 0f, -4.5f);
            s = ShotSolver.Solve(req, ball.Config, court, HeadlessMatch.Dt);
            ball.Launch(req.From, s.Velocity, s.Spin);
            ball.Tick(1, HeadlessMatch.Dt);
            human.Tick(1, HeadlessMatch.Dt);
            bot.Tick(1, HeadlessMatch.Dt);
            Assert.AreEqual(1, brain.Owner, "wide ball on the bot's side");
            Assert.IsTrue(brain.CallMine);
        }
    }
}
