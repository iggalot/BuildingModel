using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// Cutting a rectangle out of a level's diaphragms: the rest stays (in few rectangles), an emptied diaphragm goes, a
    /// diaphragm cut in two becomes two (the larger keeps its ID and data), other levels and openings are untouched.
    /// </summary>
    [TestClass]
    public class CutDiaphragmTests
    {
        private const double Tol = 1e-9;

        private static Building Floor(out Level l2, out Diaphragm d)
        {
            var b = Building.New();
            l2 = b.AddLevel("Level 2", 10);
            d = b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            return b;
        }

        private static double Area(Diaphragm d) => Region.Of(d.Pieces).Area;

        [TestMethod]
        public void CornerNotch_LeavesAnL_TwoRectangles()
        {
            var b = Floor(out var l2, out var d);
            var r = BuildingEdits.CutDiaphragms(b, l2, new Rect2(40, 20, 60, 40));
            CollectionAssert.AreEqual(new[] { d.Id }, r.Changed);
            Assert.AreEqual(2400 - 400, Area(d), Tol);
            Assert.AreEqual(2, d.Pieces.Count, "an L in two rectangles");
            Assert.IsFalse(Region.Of(d.Pieces).Contains(50, 30), "the notch is gone");
            Assert.IsTrue(b.IsExterior(b.AddWall(b.Ground, l2, 40, 20, 40, 40)), "the notch's walls are outside walls");
        }

        [TestMethod]
        public void Courtyard_LeavesAHole_ItsEdgesAreExterior()
        {
            var b = Floor(out var l2, out var d);
            BuildingEdits.CutDiaphragms(b, l2, new Rect2(20, 10, 40, 30));
            Assert.AreEqual(2400 - 400, Area(d), Tol);
            Assert.AreEqual(1, b.DiaphragmsOn(l2).Count(), "one piece: still one diaphragm");
            Assert.IsTrue(b.IsExterior(b.AddWall(b.Ground, l2, 20, 10, 40, 10)));
        }

        [TestMethod]
        public void CutAcross_SplitsInTwo_TheLargerKeepsTheIdAndData()
        {
            var b = Floor(out var l2, out var d);
            d.SetData("test", "flexible");
            var r = BuildingEdits.CutDiaphragms(b, l2, new Rect2(35, -5, 40, 45)); // a 5 ft strip right across
            Assert.AreEqual(1, r.Split.Count);
            var parts = b.DiaphragmsOn(l2).ToList();
            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(35 * 40, Area(d), Tol, "the larger (west) part keeps the ID");
            var other = parts.Single(p => p.Id != d.Id);
            CollectionAssert.AreEqual(r.Split[0].parts, new[] { other.Id });
            Assert.AreEqual(20 * 40, Area(other), Tol);
            Assert.AreEqual("flexible", other.GetData("test"), "the new part gets a copy of the data");
        }

        [TestMethod]
        public void CutCoveringAll_RemovesTheDiaphragm()
        {
            var b = Floor(out var l2, out var d);
            var r = BuildingEdits.CutDiaphragms(b, l2, new Rect2(-1, -1, 61, 41));
            CollectionAssert.AreEqual(new[] { d.Id }, r.Removed);
            Assert.AreEqual(0, b.DiaphragmsOn(l2).Count());
        }

        [TestMethod]
        public void CutOutsideOrOnAnotherLevel_ChangesNothing()
        {
            var b = Floor(out var l2, out var d);
            var roof = b.AddLevel("Roof", 20);
            var top = b.AddDiaphragm(roof, new Rect2(0, 0, 60, 40));
            Assert.IsFalse(BuildingEdits.CutDiaphragms(b, l2, new Rect2(70, 0, 80, 10)).Any, "outside");
            Assert.IsFalse(BuildingEdits.CutDiaphragms(b, l2, new Rect2(60, 0, 70, 10)).Any, "touching the edge only");
            BuildingEdits.CutDiaphragms(b, l2, new Rect2(0, 0, 10, 10));
            Assert.AreEqual(2400, Area(top), Tol, "the roof is another level");
        }

        /// <summary>A diaphragm drawn as two rectangles that overlap: the cut works on their union.</summary>
        [TestMethod]
        public void OverlappingPieces_CutOnTheUnion()
        {
            var b = Building.New();
            var l2 = b.AddLevel("Level 2", 10);
            var d = b.AddDiaphragm(l2, new Rect2(0, 0, 40, 40), new Rect2(30, 0, 60, 40));
            BuildingEdits.CutDiaphragms(b, l2, new Rect2(0, 0, 10, 40));
            Assert.AreEqual(50 * 40, Area(d), Tol);
            Assert.AreEqual(1, d.Pieces.Count, "merged into one rectangle");
        }

        [TestMethod]
        public void Merge_JoinsGridCells_AreaKept()
        {
            var cells = new[] { new Rect2(0, 0, 10, 10), new Rect2(10, 0, 20, 10), new Rect2(0, 10, 10, 20), new Rect2(10, 10, 20, 20), new Rect2(20, 0, 30, 10) };
            var m = BuildingEdits.Merge(cells);
            Assert.AreEqual(500, m.Sum(r => r.Area), Tol);
            Assert.IsTrue(m.Count <= 2);
        }

        /// <summary>Cells that touch only at a corner are separate parts.</summary>
        [TestMethod]
        public void Parts_CornerTouchDoesNotJoin()
        {
            var region = Region.Of(new[] { new Rect2(0, 0, 10, 10), new Rect2(10, 10, 20, 20) });
            Assert.AreEqual(2, BuildingEdits.Parts(region).Count);
        }
    }
}
