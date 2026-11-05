using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>The north arrow every view draws: its one definition, checked by hand.</summary>
    [TestClass]
    public class NorthArrowTests
    {
        [TestMethod]
        public void OffTheNorthWestCorner_SizedByTheBuilding()
        {
            // 40 × 30 ft, 20 ft high: size = max(40, 30, 20) = 40; a = 0.12 × 40 = 4.8 ft
            // tail at x = 0 − 0.3 × 40 = −12, y = 30 + 0.12 × 40 = 34.8; head at 34.8 + 0.6 × 4.8 = 37.68; tip at 34.8 + 4.8 = 39.6
            // N at 34.8 + 1.25 × 4.8 = 40.8; shaft 0.12 × 4.8 = 0.576 wide; head 0.6 × 4.8 = 2.88 wide
            var n = NorthArrow.For(new Rect2(0, 0, 40, 30), 20);
            Assert.AreEqual(4.8, n.Length, 1e-9);
            Assert.AreEqual(-12, n.Tail.X, 1e-9);
            Assert.AreEqual(34.8, n.Tail.Y, 1e-9);
            Assert.AreEqual(-12, n.Tip.X, 1e-9);
            Assert.AreEqual(39.6, n.Tip.Y, 1e-9);
            Assert.AreEqual(40.8, n.LabelAt.Y, 1e-9);
            Assert.AreEqual(0.576, n.Shaft[1].X - n.Shaft[0].X, 1e-9);
            Assert.AreEqual(37.68, n.Shaft[2].Y, 1e-9);
            Assert.AreEqual(2.88, n.Head[1].X - n.Head[0].X, 1e-9);
            Assert.AreEqual(37.68, n.Head[0].Y, 1e-9);
        }

        [TestMethod]
        public void TallerThanWide_SizedByTheHeight()
        {
            // a 10 ft wall line 30 ft high: size 30, a = 3.6
            var n = NorthArrow.For(new Rect2(5, 2, 15, 2), 30);
            Assert.AreEqual(3.6, n.Length, 1e-9);
            Assert.AreEqual(5 - 9, n.Tail.X, 1e-9);
            Assert.AreEqual(2 + 3.6, n.Tail.Y, 1e-9);
        }
    }
}
