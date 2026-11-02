using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using BuildingModel.ShearWalls;
using calculator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShearWallCalculator;

namespace BuildingModel.Tests
{
    /// <summary>Building model → the Shear Wall Designer's stories, design data kept by wall and story, and old projects → model.</summary>
    [TestClass]
    public class ShearWallAdapterTests
    {
        private const double Tol = 1e-9;

        /// <summary>60 × 40 ft, level 2 at 12 ft and the roof at 22 ft, four shear walls a story.</summary>
        private static Building Box(out Level l2, out Level roof)
        {
            var b = Building.New();
            l2 = b.AddLevel("Level 2", 12);
            roof = b.AddLevel("Roof", 22);
            var lv = new[] { b.Ground, l2, roof };
            for (int i = 1; i < lv.Length; i++)
            {
                b.AddDiaphragm(lv[i], new Rect2(0, 0, 60, 40));
                b.AddWall(lv[i - 1], lv[i], 0, 0, 60, 0);
                b.AddWall(lv[i - 1], lv[i], 0, 40, 60, 40);
                b.AddWall(lv[i - 1], lv[i], 0, 0, 0, 40);
                b.AddWall(lv[i - 1], lv[i], 60, 0, 60, 40);
            }
            return b;
        }

        [TestMethod]
        public void Stories_KeysAreTheIdNumbers_HeightsAreThePlates()
        {
            var b = Box(out var l2, out var roof);
            var s = ShearWallAdapter.ToCalculators(b);
            Assert.AreEqual(2, s.Count);
            Assert.AreEqual(l2, s[0].Level);
            Assert.AreEqual(12, s[0].PlateHeight, Tol);
            Assert.AreEqual(10, s[1].PlateHeight, Tol);
            var w1 = s[0].Calculator._wall_system._walls[ShearWallAdapter.Number(b.Walls[0].Id).Value];
            Assert.AreEqual(12, w1.WallHeight, Tol);
            Assert.AreEqual(60, w1.WallLength, Tol);
            Assert.AreEqual(4, s[1].Calculator._wall_system._walls.Count);
            Assert.IsTrue(s[1].Calculator._wall_system._walls.Values.All(w => w.WallHeight == 10));
            Assert.AreEqual(2400, s[0].Calculator._diaphragm_system._diaphragms.Values.Sum(d => d.Area), 1e-3);
            Assert.AreEqual(b.Walls[0].Id, s[0].ModelId("Wall", w1.ID));
            Assert.AreEqual(w1.ID, s[0].Key(b.Walls[0].Id));
        }

        /// <summary>A lobby open from the ground to the roof cuts level 2: its rectangles are the net diaphragm, mapped to D-ids.</summary>
        [TestMethod]
        public void Openings_CutTheDesignerDiaphragm()
        {
            var b = Box(out var l2, out var roof);
            b.AddOpening(b.Ground, roof, new Rect2(10, 10, 20, 20));
            var s = ShearWallAdapter.ToCalculators(b)[0];
            var dias = s.Calculator._diaphragm_system._diaphragms;
            Assert.IsTrue(dias.Count > 1);
            Assert.AreEqual(2400 - 100, dias.Values.Sum(d => d.Area), 1e-3);
            string did = b.DiaphragmsOn(l2).Single().Id;
            Assert.IsTrue(s.Diaphragms.Values.All(id => id == did));
            Assert.IsTrue(dias.Keys.All(k => k / 100 == ShearWallAdapter.Number(did)));
        }

        /// <summary>A two-story wall in a lobby that level 2 does not reach: only the roof story has it, 22 ft tall.</summary>
        [TestMethod]
        public void TallWall_OnlyWhereADiaphragmReachesIt()
        {
            var b = Box(out var l2, out var roof);
            b.AddOpening(b.Ground, roof, new Rect2(25, 0, 35, 15));
            var tall = b.AddWall(b.Ground, roof, 30, 0, 30, 10);
            var other = b.AddWall(b.Ground, l2, 45, 10, 45, 20, shearWall: false);
            var s = ShearWallAdapter.ToCalculators(b);
            int key = ShearWallAdapter.Number(tall.Id).Value;
            Assert.IsFalse(s[0].Calculator._wall_system._walls.ContainsKey(key), "level 2 has a hole there");
            Assert.AreEqual(22, s[1].Calculator._wall_system._walls[key].WallHeight, Tol);
            Assert.IsFalse(s[0].Walls.Values.Contains(other.Id), "not a shear wall");
        }

        [TestMethod]
        public void Design_IsKeptPerStoryInTheWallsBag()
        {
            var b = Box(out var l2, out var roof);
            var tall = b.AddWall(b.Ground, roof, 20, 0, 20, 40); // reached at both levels
            var s = ShearWallAdapter.ToCalculators(b);
            int key = ShearWallAdapter.Number(tall.Id).Value;
            s[0].Calculator._wall_system._walls[key].Design.EdgeSpacing = 2;
            s[1].Calculator._wall_system._walls[key].Design.EdgeSpacing = 4;
            s[0].Calculator._diaphragm_system._diaphragms.Values.First().DiaphragmType = DiaphragmTypes.DIAPHRAGM_FLEXIBLE;
            s[0].Calculator.DesignSettings.HoldownsFromStandardList = true;
            Assert.IsTrue(ShearWallAdapter.StoreDesign(b, s));
            Assert.IsFalse(ShearWallAdapter.StoreDesign(b, s), "nothing new");

            var again = ShearWallAdapter.ToCalculators(ModelFile.Clone(b));
            Assert.AreEqual(2, again[0].Calculator._wall_system._walls[key].Design.EdgeSpacing);
            Assert.AreEqual(4, again[1].Calculator._wall_system._walls[key].Design.EdgeSpacing);
            Assert.AreEqual(12, again[0].Calculator._wall_system._walls[key].WallHeight, Tol);
            Assert.AreEqual(10, again[1].Calculator._wall_system._walls[key].WallHeight, Tol);
            Assert.AreEqual(DiaphragmTypes.DIAPHRAGM_FLEXIBLE, again[0].Calculator._diaphragm_system._diaphragms.Values.First().DiaphragmType);
            Assert.IsTrue(again[0].Calculator.DesignSettings.HoldownsFromStandardList);
            Assert.AreSame(again[0].Calculator.DesignSettings, again[1].Calculator.DesignSettings, "one set for the project");
        }

        [TestMethod]
        public void ExistingCalculators_AreReused()
        {
            var b = Box(out _, out _);
            var mine = new ShearWallCalculatorBase[] { new ShearWallCalculator_RigidDiaphragm { V_x = 5 } };
            var s = ShearWallAdapter.ToCalculators(b, mine);
            Assert.AreSame(mine[0], s[0].Calculator);
            Assert.AreEqual(5, s[0].Calculator.V_x);
            Assert.AreNotSame(mine[0], s[1].Calculator);
        }

        /// <summary>The same plan analysed directly and through the model gives the same centre of rigidity.</summary>
        [TestMethod]
        public void Analysis_SameAsDrawnInTheDesigner()
        {
            var direct = new ShearWallCalculator_RigidDiaphragm();
            direct._diaphragm_system.AddDiaphragm(new DiaphragmData_Rectangular(new System.Windows.Point(0, 0), new System.Windows.Point(60, 40)));
            direct._wall_system.AddWall(new WallData(12, 0, 0, 60, 0));
            direct._wall_system.AddWall(new WallData(12, 0, 40, 30, 40));
            direct._wall_system.AddWall(new WallData(12, 0, 0, 0, 40));
            direct._wall_system.AddWall(new WallData(12, 60, 0, 60, 40));
            direct.PerformCalculations();

            var b = ShearWallAdapter.FromCalculators(new ShearWallCalculatorBase[] { direct }, new[] { 12.0 });
            var via = ShearWallAdapter.ToCalculators(b)[0].Calculator;
            via.PerformCalculations();
            Assert.AreEqual(direct._wall_system.CtrRigidity.X, via._wall_system.CtrRigidity.X, 1e-9);
            Assert.AreEqual(direct._wall_system.CtrRigidity.Y, via._wall_system.CtrRigidity.Y, 1e-9);
            Assert.AreEqual(direct._diaphragm_system.CtrMass.X, via._diaphragm_system.CtrMass.X, 1e-9);
        }

        [TestMethod]
        public void OldProject_BecomesAModel()
        {
            var c1 = new ShearWallCalculator_RigidDiaphragm();
            c1._diaphragm_system.AddDiaphragm(new DiaphragmData_Rectangular(new System.Windows.Point(0, 0), new System.Windows.Point(40, 30)));
            c1._wall_system.AddWall(new WallData(9, 0, 0, 40, 0));
            c1._wall_system._walls.Values.First().Design.Sides = 2;
            var c2 = new ShearWallCalculator_RigidDiaphragm();
            c2._diaphragm_system.AddDiaphragm(new DiaphragmData_Rectangular(new System.Windows.Point(0, 0), new System.Windows.Point(40, 30)));
            c2._wall_system.AddWall(new WallData(8, 0, 0, 40, 0)); // key collides with c1's: a new ID
            var b = ShearWallAdapter.FromCalculators(new ShearWallCalculatorBase[] { c1, c2 }, new[] { 10.0, 8.0 });
            CollectionAssert.AreEqual(new[] { 0.0, 10.0, 18.0 }, b.LevelsBottomUp.Select(l => l.Elevation).ToArray());
            Assert.AreEqual("Roof", b.LevelsBottomUp.Last().Name);
            Assert.AreEqual(2, b.Walls.Count);
            Assert.AreNotEqual(b.Walls[0].Id, b.Walls[1].Id);
            var s = ShearWallAdapter.ToCalculators(b);
            Assert.AreEqual(2, s[0].Calculator._wall_system._walls.Values.Single().Design.Sides);
            Assert.AreEqual(8, s[1].Calculator._wall_system._walls.Values.Single().WallHeight, Tol);
            Assert.AreEqual(0, Checks.ModelChecks.Run(b).Count(i => i.Code == "BadReference"));
        }

        [TestMethod]
        public void Numbers()
        {
            Assert.AreEqual(12, ShearWallAdapter.Number("W12"));
            Assert.IsNull(ShearWallAdapter.Number("W"));
            Assert.IsNull(ShearWallAdapter.Number(null));
        }
    }
}
