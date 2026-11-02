using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using BuildingModel.ShearWalls;
using calculator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShearWallCalculator;
using ShearWallCalculator.Design;
using SheathingMaterials = ShearWallCalculator.NDSProvisions.SheathingMaterials;

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

        /// <summary>Typed coordinates in the designer's table go back to the model's wall and single-rectangle diaphragm.</summary>
        [TestMethod]
        public void StoreGeometry_WritesTypedCoordinatesBack()
        {
            var b = Box(out var l2, out _);
            var s = ShearWallAdapter.ToCalculators(b)[0];
            int key = ShearWallAdapter.Number(b.Walls[0].Id).Value;
            Assert.IsFalse(ShearWallAdapter.StoreGeometry(b, s), "nothing typed yet");
            var w = s.Calculator._wall_system._walls[key];
            w.Start = new System.Windows.Point(0, 0);
            w.End = new System.Windows.Point(50, 0);
            var d = s.Calculator._diaphragm_system._diaphragms.Values.Single();
            d.P1 = new System.Windows.Point(0, 0);
            d.P3 = new System.Windows.Point(70, 40);
            Assert.IsTrue(ShearWallAdapter.StoreGeometry(b, s));
            Assert.AreEqual(50, b.Walls[0].End.X, Tol);
            Assert.AreEqual(70, b.DiaphragmsOn(l2).Single().Pieces[0].X1, Tol);
            Assert.IsFalse(ShearWallAdapter.StoreGeometry(b, s), "already stored");
        }

        /// <summary>A wall drawn after the project's defaults were set starts with the one for where it is: exterior or interior.</summary>
        [TestMethod]
        public void NewWall_GetsTheProjectDefaultDesign_ExteriorOrInterior()
        {
            var b = Box(out var l2, out _);
            var s = ShearWallAdapter.ToCalculators(b);
            s[0].Calculator.DesignSettings.NewWallDesign = new WallDesignInputs { EdgeSpacing = 3 };
            s[0].Calculator.DesignSettings.NewInteriorWallDesign = new WallDesignInputs { EdgeSpacing = 2 };
            ShearWallAdapter.StoreDesign(b, s);
            var inside = b.AddWall(b.Ground, l2, 20, 0, 20, 40);      // across the building: only its ends touch the outline
            var edge = b.AddWall(b.Ground, l2, 50, 0, 70, 0);         // half of it on the south edge
            var again = ShearWallAdapter.ToCalculators(b);
            Assert.AreEqual(2, DesignOf(again[0], inside).EdgeSpacing, "interior: the project's interior default");
            Assert.AreEqual(3, DesignOf(again[0], edge).EdgeSpacing, "any part on the outside edge: exterior");
        }

        private static WallDesignInputs DesignOf(StoryMap s, Wall w) =>
            s.Calculator._wall_system._walls[ShearWallAdapter.Number(w.Id).Value].Design;

        /// <summary>The program defaults: Structural I on an exterior wall, 1/2" gypsum wallboard on both faces of an interior one.</summary>
        [TestMethod]
        public void NewWall_ProgramDefaults_StructuralIOutside_GypsumInside()
        {
            var b = Box(out var l2, out _);
            var inside = b.AddWall(b.Ground, l2, 30, 0, 30, 40);
            var s = ShearWallAdapter.ToCalculators(b);
            var outsideDesign = DesignOf(s[0], b.Walls[0]);
            Assert.AreEqual(SheathingTable.WoodPanels, outsideDesign.EffectiveFace1().Table);
            Assert.AreEqual(SheathingMaterials.SHEATH_MAT_STRUCTURAL_1, outsideDesign.EffectiveFace1().Material);
            Assert.IsNull(outsideDesign.EffectiveFace2(), "one face");
            var insideDesign = DesignOf(s[0], inside);
            Assert.AreEqual(SheathingTable.GypsumAndPlaster, insideDesign.EffectiveFace1().Table);
            Assert.AreEqual(WallDesignInputs.InteriorGypsumRow, insideDesign.EffectiveFace1().Row);
            Assert.IsTrue(insideDesign.EffectiveFace1().SameConstructionAs(insideDesign.EffectiveFace2()), "both faces");
        }

        /// <summary>An untouched default follows the wall when it moves onto the outside edge; an edited design never changes.</summary>
        [TestMethod]
        public void UntouchedDefault_FollowsTheWall_EditedDesignStays()
        {
            var b = Box(out var l2, out _);
            var a = b.AddWall(b.Ground, l2, 20, 5, 20, 35);
            var c = b.AddWall(b.Ground, l2, 40, 5, 40, 35);
            var s = ShearWallAdapter.ToCalculators(b);
            ShearWallAdapter.StoreDesign(b, s);                                     // stored as given: the interior default
            DesignOf(s[0], c).Species = WoodTypes.WOODTYPE_SPF_HF;                 // the user edits wall c
            ShearWallAdapter.StoreDesign(b, s);

            a.Start = new Point2(0, 5); a.End = new Point2(0, 35);                // both move onto the west and east edges
            c.Start = new Point2(60, 5); c.End = new Point2(60, 35);
            var again = ShearWallAdapter.ToCalculators(b);
            Assert.AreEqual(SheathingTable.WoodPanels, DesignOf(again[0], a).EffectiveFace1().Table, "untouched: now the exterior default");
            Assert.AreEqual(WoodTypes.WOODTYPE_SPF_HF, DesignOf(again[0], c).Species, "edited: kept");
            Assert.AreEqual(SheathingTable.GypsumAndPlaster, DesignOf(again[0], c).EffectiveFace1().Table, "edited: kept");

            ShearWallAdapter.StoreDesign(b, again);                                 // and back inside: a follows again, c stays
            a.Start = new Point2(20, 5); a.End = new Point2(20, 35);
            var back = ShearWallAdapter.ToCalculators(b);
            Assert.AreEqual(SheathingTable.GypsumAndPlaster, DesignOf(back[0], a).EffectiveFace1().Table);
        }

        /// <summary>A wall of an older project keeps its design (an interior wall is not given gypsum).</summary>
        [TestMethod]
        public void OldProject_InteriorWall_KeepsItsDesign()
        {
            var c1 = new ShearWallCalculator_RigidDiaphragm();
            c1._diaphragm_system.AddDiaphragm(new DiaphragmData_Rectangular(new System.Windows.Point(0, 0), new System.Windows.Point(60, 40)));
            c1._wall_system.AddWall(new WallData(9, 30, 0, 30, 40));
            var w = c1._wall_system._walls.Values.Single();
            w.Design = null;                                                        // saved before designs: the program default then
            var b = ShearWallAdapter.FromCalculators(new ShearWallCalculatorBase[] { c1 }, new[] { 9.0 });
            var s = ShearWallAdapter.ToCalculators(b);
            Assert.AreEqual(SheathingTable.WoodPanels, s[0].Calculator._wall_system._walls.Values.Single().Design.EffectiveFace1().Table);
        }

        [TestMethod]
        public void NewBuilding_OneStoryPerPlateHeight_NamedLevelsAndRoof()
        {
            var b = ShearWallAdapter.NewBuilding(new[] { 10.0, 9.0, 0.0 });
            var up = ShearWallAdapter.Stories(b);
            CollectionAssert.AreEqual(new[] { 10.0, 19.0, 28.0 }, up.Select(l => l.Elevation).ToArray(), "a height of 0 becomes 9 ft");
            CollectionAssert.AreEqual(new[] { "Level 2", "Level 3", "Roof" }, up.Select(l => l.Name).ToArray());
            Assert.AreEqual("Level 2", ShearWallAdapter.Stories(ShearWallAdapter.NewBuilding(null)).Single().Name, "one story");
        }

        [TestMethod]
        public void NameStories_FollowsAddAndDelete_KeepsTheUsersNames()
        {
            var b = ShearWallAdapter.NewBuilding(new[] { 10.0, 9.0 });
            var mezz = BuildingEdits.AddStory(b, 8);
            ShearWallAdapter.NameStories(b);
            CollectionAssert.AreEqual(new[] { "Level 2", "Level 3", "Roof" }, ShearWallAdapter.Stories(b).Select(l => l.Name).ToArray(), "the old roof is a floor now");
            ShearWallAdapter.Stories(b)[0].Name = "Podium";
            BuildingEdits.DeleteStory(b, mezz);
            ShearWallAdapter.NameStories(b);
            CollectionAssert.AreEqual(new[] { "Podium", "Roof" }, ShearWallAdapter.Stories(b).Select(l => l.Name).ToArray());
        }

        /// <summary>Copy ↑ in the designer: the copied walls keep the design they had on the story below.</summary>
        [TestMethod]
        public void CopyDesignUp_TheCopiedWallsKeepTheirDesign()
        {
            var b = Box(out var l2, out var roof);
            b.Walls.RemoveAll(w => w.TopLevelId == roof.Id);
            b.Diaphragms.RemoveAll(d => d.LevelId == roof.Id);
            var s = ShearWallAdapter.ToCalculators(b);
            foreach (var w in s[0].Calculator._wall_system._walls.Values) w.Design.EdgeSpacing = 3;
            ShearWallAdapter.StoreDesign(b, s);
            Assert.AreEqual(5, BuildingEdits.CopyStory(b, l2, roof));
            Assert.AreEqual(4, ShearWallAdapter.CopyDesignUp(b, l2, roof));
            var again = ShearWallAdapter.ToCalculators(b);
            Assert.AreEqual(4, again[1].Calculator._wall_system._walls.Count);
            Assert.IsTrue(again[1].Calculator._wall_system._walls.Values.All(w => w.Design.EdgeSpacing == 3));
            Assert.AreEqual(0, ShearWallAdapter.CopyDesignUp(b, l2, roof), "already there");
        }

        /// <summary>Data an app fills in after an undo amends that step: the redo is still there.</summary>
        [TestMethod]
        public void HistoryAmend_KeepsTheRedo()
        {
            var b = Box(out var l2, out _);
            var h = new ModelHistory();
            h.Reset(b);
            b.AddWall(b.Ground, l2, 20, 0, 20, 40);
            Assert.IsTrue(h.Record(b));
            Assert.IsNotNull(h.Undo(b));
            ShearWallAdapter.StoreDesign(b, ShearWallAdapter.ToCalculators(b)); // the designer fills in the walls' design
            Assert.IsTrue(h.Amend(b));
            Assert.IsFalse(h.Amend(b), "nothing new");
            Assert.IsTrue(h.CanRedo);
            Assert.IsNotNull(h.Redo(b));
            Assert.AreEqual(9, b.Walls.Count);
            Assert.IsNotNull(h.Undo(b));
            Assert.IsNotNull(b.Walls[0].GetData(ShearWallAdapter.WallKey), "the amended step has the design");
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
