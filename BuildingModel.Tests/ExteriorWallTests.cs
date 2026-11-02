using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// Exterior walls: any part of the wall's length on the outline of a diaphragm it reaches (openings not counted). The
    /// shear-wall designer gives an exterior wall Structural I and an interior wall gypsum wallboard.
    /// </summary>
    [TestClass]
    public class ExteriorWallTests
    {
        private const double Tol = 1e-9;

        /// <summary>A 60 × 40 ft floor at 10 ft.</summary>
        private static Building Floor(out Level l2)
        {
            var b = Building.New();
            l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            return b;
        }

        [TestMethod]
        public void PerimeterWall_IsExterior_ItsWholeLength()
        {
            var b = Floor(out var l2);
            var w = b.AddWall(b.Ground, l2, 0, 0, 60, 0);
            Assert.AreEqual(60, b.ExteriorLength(w), Tol);
            Assert.IsTrue(b.IsExterior(w));
        }

        [TestMethod]
        public void WallAcrossTheBuilding_TouchingTheEdgeOnlyAtItsEnds_IsInterior()
        {
            var b = Floor(out var l2);
            var w = b.AddWall(b.Ground, l2, 30, 0, 30, 40);
            Assert.AreEqual(0, b.ExteriorLength(w), Tol);
            Assert.IsFalse(b.IsExterior(w));
        }

        /// <summary>Any part on the edge makes it exterior, however short: 2 ft of a 30 ft wall.</summary>
        [TestMethod]
        public void WallPartlyOnTheEdge_IsExterior()
        {
            var b = Floor(out var l2);
            var w = b.AddWall(b.Ground, l2, 58, 40, 88, 40); // runs on past the building's corner
            Assert.AreEqual(2, b.ExteriorLength(w), Tol);
            Assert.IsTrue(b.IsExterior(w));
        }

        /// <summary>Drawn a little off the edge (within the 0.25 ft tolerance): still exterior; 1 ft in: interior.</summary>
        [TestMethod]
        public void Tolerance_QuarterFoot()
        {
            var b = Floor(out var l2);
            Assert.IsTrue(b.IsExterior(b.AddWall(b.Ground, l2, 60.2, 0, 60.2, 40)));
            Assert.IsFalse(b.IsExterior(b.AddWall(b.Ground, l2, 59, 0, 59, 40)));
        }

        /// <summary>An L-shaped plan: the walls of the inside corner are exterior.</summary>
        [TestMethod]
        public void InsideCornerOfAnL_IsExterior()
        {
            var b = Building.New();
            var l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 60, 20));
            b.AddDiaphragm(l2, new Rect2(0, 20, 20, 40));
            Assert.IsTrue(b.IsExterior(b.AddWall(b.Ground, l2, 20, 20, 60, 20)), "the step along y = 20, east of x = 20");
            Assert.IsTrue(b.IsExterior(b.AddWall(b.Ground, l2, 20, 20, 20, 40)), "the step along x = 20, north of y = 20");
            Assert.IsFalse(b.IsExterior(b.AddWall(b.Ground, l2, 0, 10, 20, 10)), "inside the L");
        }

        /// <summary>A stair opening is a hole in the floor, not the outside: the walls around it stay interior.</summary>
        [TestMethod]
        public void WallAlongAnOpening_IsInterior()
        {
            var b = Building.New();
            var l2 = b.AddLevel("Level 2", 10);
            var roof = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            b.AddDiaphragm(roof, new Rect2(0, 0, 60, 40));
            b.Openings.Add(new Opening { Id = b.NewId("O"), BottomLevelId = b.Ground.Id, TopLevelId = roof.Id, Pieces = { new Rect2(20, 10, 30, 20) } });
            Assert.IsFalse(b.IsExterior(b.AddWall(b.Ground, l2, 20, 10, 30, 10)));
        }

        /// <summary>A wall two stories tall is exterior when it is on the outline of either level it reaches.</summary>
        [TestMethod]
        public void TallWall_ExteriorAtTheUpperLevel()
        {
            var b = Building.New();
            var l2 = b.AddLevel("Level 2", 10);
            var roof = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            b.AddDiaphragm(roof, new Rect2(0, 0, 30, 40));   // the roof covers the west half only
            var w = b.AddWall(b.Ground, roof, 30, 0, 30, 40); // under the roof's east edge, across level 2
            Assert.AreEqual(40, b.ExteriorLength(w), Tol);
        }

        [TestMethod]
        public void NoDiaphragm_NotExterior()
        {
            var b = Building.New();
            var l2 = b.AddLevel("Level 2", 10);
            Assert.IsFalse(b.IsExterior(b.AddWall(b.Ground, l2, 0, 0, 10, 0)));
        }
    }
}
