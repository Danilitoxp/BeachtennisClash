using BeachTennis.Simulation;
using NUnit.Framework;

namespace BeachTennis.Tests
{
    public class CourtDimensionsTests
    {
        [Test]
        public void DoublesCourtIs16By8()
        {
            var court = CourtDimensions.For(CourtFormat.Doubles);
            Assert.AreEqual(16f, court.Length);
            Assert.AreEqual(8f, court.Width);
        }

        [Test]
        public void SinglesCourtIsNarrower()
        {
            var court = CourtDimensions.For(CourtFormat.Singles);
            Assert.AreEqual(16f, court.Length);
            Assert.AreEqual(4.5f, court.Width);
        }

        [Test]
        public void LinesCountAsInside()
        {
            var court = CourtDimensions.For(CourtFormat.Doubles);
            Assert.IsTrue(court.IsInside(4f, 8f));
            Assert.IsTrue(court.IsInside(0f, 0f));
            Assert.IsFalse(court.IsInside(4.01f, 0f));
            Assert.IsFalse(court.IsInside(0f, -8.01f));
        }
    }
}
