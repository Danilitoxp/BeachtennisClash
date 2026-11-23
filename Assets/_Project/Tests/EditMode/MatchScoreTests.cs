using BeachTennis.Simulation.Rules;
using NUnit.Framework;

namespace BeachTennis.Tests
{
    /// <summary>Scoring rules: ITF Rules of Beach Tennis 2026, R6–R11 and R15, plus Appendix VI options.</summary>
    public class MatchScoreTests
    {
        private static MatchScore Singles(RuleSet? rules = null) =>
            new MatchScore(rules ?? RuleSet.ItfOfficial(EventCategory.Women), 1, firstServingTeam: 0, southTeam: 0);

        private static void Points(MatchScore s, int team, int n)
        {
            for (int i = 0; i < n; i++) s.AwardPoint(team);
        }

        private static void Games(MatchScore s, int team, int n) => Points(s, team, 4 * n);

        [Test]
        public void LoveGameIsFourPoints()
        {
            var s = Singles();
            Points(s, 0, 3);
            Assert.AreEqual("40", s.PointText(0));
            Assert.AreEqual("0", s.PointText(1));
            PointOutcome o = s.AwardPoint(0);
            Assert.IsTrue(o.GameWon);
            Assert.AreEqual(1, s.Games[0]);
        }

        [Test]
        public void NoAdDeuceIsADecidingPoint()
        {
            var s = Singles();
            Points(s, 0, 3);
            Points(s, 1, 3);
            Assert.IsTrue(s.IsDecidingPoint);
            PointOutcome o = s.AwardPoint(1);
            Assert.IsTrue(o.GameWon);
            Assert.AreEqual(1, s.Games[1]);
        }

        [Test]
        public void AdvantageScoringNeedsTwoClearPoints()
        {
            var rules = RuleSet.ItfOfficial(EventCategory.Women);
            rules.NoAd = false;
            var s = Singles(rules);
            Points(s, 0, 3);
            Points(s, 1, 3);
            Assert.IsFalse(s.AwardPoint(0).GameWon);
            Assert.AreEqual("AD", s.PointText(0));
            Assert.IsFalse(s.AwardPoint(1).GameWon);
            Assert.IsFalse(s.AwardPoint(1).GameWon);
            Assert.IsTrue(s.AwardPoint(1).GameWon);
        }

        [Test]
        public void SetNeedsSixGamesAndTwoClear()
        {
            var s = Singles();
            Games(s, 0, 5);
            Games(s, 1, 5);
            Games(s, 0, 1);
            Assert.AreEqual(0, s.SetsWon[0], "6-5 is not a set");
            Games(s, 0, 1);
            Assert.AreEqual(1, s.SetsWon[0], "7-5 wins the set");
            Assert.AreEqual(7, s.SetGames(0, 0));
            Assert.AreEqual(5, s.SetGames(0, 1));
        }

        [Test]
        public void TieBreakAtSixAllToSevenByTwo()
        {
            var s = Singles();
            for (int i = 0; i < 6; i++) { Games(s, 0, 1); Games(s, 1, 1); }
            Assert.IsTrue(s.InTieBreak);

            Points(s, 0, 6);
            Points(s, 1, 6);
            Assert.IsFalse(s.AwardPoint(0).SetWon, "7-6 in the tie-break is not enough");
            Assert.IsTrue(s.AwardPoint(0).SetWon, "8-6 wins");
            Assert.AreEqual(7, s.SetGames(0, 0));
            Assert.AreEqual(6, s.SetGames(0, 1));
            Assert.IsFalse(s.InTieBreak);
        }

        [Test]
        public void MatchTieBreakToTenDecidesAtOneSetAll()
        {
            var s = Singles();
            Games(s, 0, 6);          // set 1: 6-0
            Games(s, 1, 6);          // set 2: 0-6
            Assert.IsTrue(s.InTieBreak);
            Assert.IsTrue(s.InMatchTieBreak);

            Points(s, 0, 9);
            Points(s, 1, 9);
            Assert.IsFalse(s.AwardPoint(1).MatchWon, "10-9 is not enough");
            PointOutcome o = s.AwardPoint(1);
            Assert.IsTrue(o.MatchWon);
            Assert.AreEqual(1, s.Winner);
            Assert.IsTrue(s.SetWasMatchTieBreak(2));
            Assert.AreEqual(11, s.SetGames(2, 1));
        }

        [Test]
        public void QuickShortSetIsFirstToFourWithTieBreakAtFourAll()
        {
            var s = Singles(RuleSet.QuickShortSet(EventCategory.Women));
            Games(s, 0, 3);
            Games(s, 1, 3);
            Games(s, 0, 1);
            Games(s, 1, 1);
            Assert.IsTrue(s.InTieBreak, "4-4 → tie-break");
            Points(s, 1, 7);
            Assert.IsTrue(s.IsOver);
            Assert.AreEqual(1, s.Winner);
        }

        [Test]
        public void ShortSetWonFourTwo()
        {
            var s = Singles(RuleSet.ShortSets(EventCategory.Women));
            Games(s, 1, 2);
            Games(s, 0, 4);
            Assert.AreEqual(1, s.SetsWon[0]);
        }

        [Test]
        public void SinglesServiceAlternatesEachGame()
        {
            var s = Singles();
            Assert.AreEqual(new PlayerId(0, 0), s.Server);
            Games(s, 0, 1);
            Assert.AreEqual(new PlayerId(1, 0), s.Server);
            Games(s, 0, 1);
            Assert.AreEqual(new PlayerId(0, 0), s.Server);
        }

        [Test]
        public void DoublesServiceRotatesThroughAllFourPlayers()
        {
            var s = new MatchScore(RuleSet.ItfOfficial(EventCategory.Mixed), 2, firstServingTeam: 1, southTeam: 0);
            var expected = new[] { new PlayerId(1, 0), new PlayerId(0, 0), new PlayerId(1, 1), new PlayerId(0, 1), new PlayerId(1, 0) };
            foreach (PlayerId id in expected)
            {
                Assert.AreEqual(id, s.Server);
                Games(s, s.Games[0] <= s.Games[1] ? 0 : 1, 1); // keep the set alive
            }
        }

        [Test]
        public void TieBreakServeSequenceIsOneThenTwoEach()
        {
            var s = Singles();
            for (int i = 0; i < 6; i++) { Games(s, 0, 1); Games(s, 1, 1); }
            // 12 games played → server index back at team 0.
            int[] expectedTeams = { 0, 1, 1, 0, 0, 1, 1, 0 };
            for (int i = 0; i < expectedTeams.Length; i++)
            {
                Assert.AreEqual(expectedTeams[i], s.ServingTeam, $"tie-break point {i + 1}");
                s.AwardPoint(i % 2); // keep it level
            }
        }

        [Test]
        public void FirstTieBreakServerReceivesFirstGameOfNextSet()
        {
            var s = Singles();
            for (int i = 0; i < 6; i++) { Games(s, 0, 1); Games(s, 1, 1); }
            int firstTieBreakServer = s.ServingTeam;
            Points(s, 0, 7);
            Assert.AreEqual(1 - firstTieBreakServer, s.ServingTeam);
        }

        [Test]
        public void EndsChangeAfterOddGames()
        {
            var s = Singles();
            int south = s.SouthTeam;
            Games(s, 0, 1);
            Assert.AreNotEqual(south, s.SouthTeam, "after game 1");
            south = s.SouthTeam;
            Games(s, 1, 1);
            Assert.AreEqual(south, s.SouthTeam, "no change after game 2");
            Games(s, 0, 1);
            Assert.AreNotEqual(south, s.SouthTeam, "after game 3");
        }

        [Test]
        public void EvenSetEndsChangeAfterFirstGameOfNextSet()
        {
            var s = Singles();
            Games(s, 1, 4);
            Games(s, 0, 6);                      // 6-4: 10 games (even)
            int south = s.SouthTeam;
            PointOutcome last = default;
            for (int i = 0; i < 4; i++) last = s.AwardPoint(0);
            Assert.IsTrue(last.ChangeEnds, "change after the first game of the next set");
            Assert.AreNotEqual(south, s.SouthTeam);
        }

        [Test]
        public void OddSetEndsChangeAtTheEndOfTheSet()
        {
            var s = Singles();
            Games(s, 1, 3);
            Games(s, 0, 5);
            PointOutcome last = default;
            for (int i = 0; i < 4; i++) last = s.AwardPoint(0); // 6-3: 9 games (odd)
            Assert.IsTrue(last.SetWon);
            Assert.IsTrue(last.ChangeEnds);
        }

        [Test]
        public void TieBreakEndsChangeAfterPointOneThenEveryFour()
        {
            var s = Singles();
            for (int i = 0; i < 6; i++) { Games(s, 0, 1); Games(s, 1, 1); }
            var changes = new System.Collections.Generic.List<int>();
            for (int p = 1; p <= 10; p++)
                if (s.AwardPoint(p % 2).ChangeEnds) changes.Add(p);
            CollectionAssert.AreEqual(new[] { 1, 5, 9 }, changes);
        }

        [Test]
        public void NoPointsAfterTheMatchIsOver()
        {
            var s = Singles(RuleSet.QuickShortSet(EventCategory.Women));
            Games(s, 0, 4);
            Assert.IsTrue(s.IsOver);
            s.AwardPoint(1);
            Assert.AreEqual(0, s.Points[1]);
        }

        [Test]
        public void NetHeightAndProhibitedZoneFollowCategory()
        {
            Assert.AreEqual(1.80f, RuleSet.ItfOfficial(EventCategory.Men).NetHeight);
            Assert.AreEqual(1.70f, RuleSet.ItfOfficial(EventCategory.Women).NetHeight);
            Assert.AreEqual(1.70f, RuleSet.ItfOfficial(EventCategory.Mixed).NetHeight);
            Assert.AreEqual(6f, RuleSet.ItfOfficial(EventCategory.Men).ProhibitedZone);
            Assert.AreEqual(3f, RuleSet.ItfOfficial(EventCategory.Women).ProhibitedZone);
        }
    }
}
