using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using NUnit.Framework;

namespace BeachTennis.Tests
{
    public class ShotSolverTests
    {
        private const float Dt = 1f / 120f;
        private static readonly CourtDimensions Court = CourtDimensions.For(CourtFormat.Doubles);
        private static readonly BallPhysicsConfig Config = BallPhysicsConfig.Default;

        private static ShotSolution SolveAndCheck(ShotRequest request)
        {
            var solution = ShotSolver.Solve(request, Config, Court, Dt);
            Assert.IsTrue(solution.Converged, $"not converged, error {solution.LandingError:0.000} m after {solution.Iterations} iterations");
            Assert.Less(solution.LandingError, ShotSolver.DefaultTolerance);
            return solution;
        }

        [Test]
        public void LobLandsOnTargetAndClearsNet()
        {
            var s = SolveAndCheck(new ShotRequest
            {
                From = new Vec3(0f, 1.2f, -6f), Target = new Vec3(2f, 0f, 6.5f),
                Arc = ShotArc.Apex, ApexHeight = 6.5f, TopspinRps = 5f
            });
            Assert.IsTrue(s.ClearsNet);
            Assert.Greater(s.NetClearance, 2f);
        }

        [Test]
        public void SmashFromAboveTheHeadLandsOnTarget()
        {
            var s = SolveAndCheck(new ShotRequest
            {
                From = new Vec3(0.5f, 2.8f, -2f), Target = new Vec3(-1.5f, 0f, 4.5f),
                Arc = ShotArc.Drive, Speed = 25f, TopspinRps = 10f
            });
            Assert.Less(s.Velocity.y, 0f, "a smash goes downwards");
            Assert.IsTrue(s.ClearsNet);
        }

        [Test]
        public void ServeFromBaselineLandsOnTarget()
        {
            SolveAndCheck(new ShotRequest
            {
                From = new Vec3(1.5f, 2.6f, -8.3f), Target = new Vec3(-2f, 0f, 5f),
                Arc = ShotArc.Drive, Speed = 17f, TopspinRps = 25f, SidespinRps = 5f
            });
        }

        [Test]
        public void DropShotWithBackspinLandsShort()
        {
            var s = SolveAndCheck(new ShotRequest
            {
                From = new Vec3(0f, 1.0f, -3f), Target = new Vec3(0.5f, 0f, 1.5f),
                Arc = ShotArc.Apex, ApexHeight = 2.4f, TopspinRps = -10f
            });
            Assert.IsTrue(s.ClearsNet);
        }

        [Test]
        public void SidespinShotStillLandsOnTarget()
        {
            SolveAndCheck(new ShotRequest
            {
                From = new Vec3(-2f, 1.5f, -4f), Target = new Vec3(3f, 0f, 5f),
                Arc = ShotArc.Drive, Speed = 14f, SidespinRps = 25f
            });
        }

        [Test]
        public void SolvedShotPlayedLiveLandsOnTarget()
        {
            var request = new ShotRequest
            {
                From = new Vec3(1f, 1.4f, -5f), Target = new Vec3(-2.5f, 0f, 6f),
                Arc = ShotArc.Apex, ApexHeight = 4f, TopspinRps = 15f
            };
            var solution = SolveAndCheck(request);

            var system = new BallSystem(Config, Court);
            system.Launch(request.From, solution.Velocity, solution.Spin);
            for (int i = 0; i < 1000 && system.State.BounceCount == 0; i++) system.Tick(i, Dt);

            BallEvent sand = default;
            for (int i = 0; i < system.Events.Count; i++)
                if (system.Events[i].Type == BallEventType.SandContact) sand = system.Events[i];

            Assert.Less(Vec3.HorizontalDistance(sand.Position, request.Target), ShotSolver.DefaultTolerance);
        }

        [Test]
        public void NetClearanceDetectsShotsIntoTheNet()
        {
            // Very flat drive from low contact height: must report it does not clear.
            var s = ShotSolver.Solve(new ShotRequest
            {
                From = new Vec3(0f, 0.6f, -4f), Target = new Vec3(0f, 0f, 2f),
                Arc = ShotArc.Drive, Speed = 22f
            }, Config, Court, Dt);
            Assert.IsFalse(s.ClearsNet);
        }

        [Test]
        public void LowFlatVolleyRaisesItsArcToClearTheNet()
        {
            var s = SolveAndCheck(new ShotRequest
            {
                From = new Vec3(0.5f, 1.3f, 3f), Target = new Vec3(0f, 0f, -4.5f),
                Arc = ShotArc.Drive, Speed = 14f, TopspinRps = 10f,
                EnforceNetClearance = true, MinNetClearance = 0.1f
            });
            Assert.IsTrue(s.ArcRaised);
            Assert.GreaterOrEqual(s.NetClearance, 0.1f);
        }

        [Test]
        public void EnforcementDoesNotTouchShotsThatAlreadyClear()
        {
            var s = SolveAndCheck(new ShotRequest
            {
                From = new Vec3(0f, 2.8f, 2f), Target = new Vec3(0f, 0f, -4.5f),
                Arc = ShotArc.Drive, Speed = 25f, TopspinRps = 10f,
                EnforceNetClearance = true, MinNetClearance = 0.1f
            });
            Assert.IsFalse(s.ArcRaised);
            Assert.Less(s.Velocity.y, 0f);
        }

        [Test]
        public void ExecutionErrorWithZeroErrorChangesNothing()
        {
            var v = new Vec3(2f, 5f, 12f);
            Vec3 r = ShotSolver.ApplyExecutionError(v, 0f, 0f, 1f);
            Assert.AreEqual(v.x, r.x, 1e-4f);
            Assert.AreEqual(v.y, r.y, 1e-4f);
            Assert.AreEqual(v.z, r.z, 1e-4f);
        }

        [Test]
        public void PositiveYawErrorSendsTheBallRight()
        {
            var v = new Vec3(0f, 5f, 12f);
            Vec3 r = ShotSolver.ApplyExecutionError(v, 0.1f, 0f, 1f);
            Assert.Greater(r.x, 0f);
            Assert.AreEqual(v.Magnitude, r.Magnitude, 1e-3f);
        }

        [Test]
        public void SolverIsDeterministic()
        {
            var request = new ShotRequest
            {
                From = new Vec3(0.3f, 1.3f, -6f), Target = new Vec3(1f, 0f, 5f),
                Arc = ShotArc.Apex, ApexHeight = 3.5f, TopspinRps = 12f, SidespinRps = -4f
            };
            var a = ShotSolver.Solve(request, Config, Court, Dt);
            var b = ShotSolver.Solve(request, Config, Court, Dt);
            Assert.AreEqual(a.Velocity, b.Velocity);
        }
    }
}
