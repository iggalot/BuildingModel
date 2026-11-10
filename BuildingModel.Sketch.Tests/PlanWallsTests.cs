using System;
using System.IO;
using System.Linq;
using System.Text;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>
    /// Drawing walls on a CAD plan that shows their faces: the wall-centre snap and turning the plan's snaps off, a wall
    /// drawn along a face, and the walls proposed from a layer one by one.
    /// </summary>
    [TestClass]
    public class PlanWallsTests
    {
        /// <summary>
        /// A 40 x 30 ft box of 2x6 walls drawn in inches on A-WALL (the outside and inside faces as closed polylines), a 2x4
        /// wall across at 20 ft, and a table on FURN.
        /// </summary>
        private static (Building b, Level l2, SketchCanvas s) BoxOnAPlan()
        {
            var sb = new StringBuilder();
            void P(string text) { foreach (var t in text.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)) sb.Append(t.Trim()).Append("\r\n"); }
            P("0|SECTION|2|HEADER|9|$ACADVER|1|AC1027|9|$INSUNITS|70|1|0|ENDSEC|0|SECTION|2|BLOCKS|0|ENDSEC|0|SECTION|2|ENTITIES");
            P("0|LWPOLYLINE|8|A-WALL|90|4|70|1|10|0|20|0|10|480|20|0|10|480|20|360|10|0|20|360");
            P("0|LWPOLYLINE|8|A-WALL|90|4|70|1|10|6|20|6|10|474|20|6|10|474|20|354|10|6|20|354");
            P("0|LINE|8|A-WALL|10|238.25|20|6|11|238.25|21|354|0|LINE|8|A-WALL|10|241.75|20|6|11|241.75|21|354");
            P("0|LWPOLYLINE|8|FURN|90|4|70|1|10|60|20|60|10|120|20|60|10|120|20|90|10|60|20|90");
            P("0|ENDSEC|0|EOF");
            string dir = Path.Combine(Path.GetTempPath(), "plan-walls-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "plan.dxf");
            File.WriteAllText(path, sb.ToString(), Encoding.ASCII);

            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 9);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            var d = DxfReader.ReadFile(path);
            s.SetPlan(l2, PlanImage.FromDxf(path, d, null, DxfReader.FeetPerUnit(1), "DXF, inches"), null);
            Assert.IsNotNull(s.PlanOf(s.ActiveLevel)?.IsDxf == true ? s.PlanOf(s.ActiveLevel) : null, "the plan under Level 1");
            return (b, l2, s);
        }

        [STATestMethod]
        public void PlanSnap_TheWallCentre_AndOffItSnapsToTheModelOnly()
        {
            var (b, l2, s) = BoxOnAPlan();
            s.SetTool(SketchTool.Wall);
            var at = new Point2(10, 0.1); // over the south wall, near its outside face
            var on = s.SnapAt(at);
            Assert.AreEqual(SnapModes.WallCentre, on.Kind, on.Label);
            Assert.AreEqual(0.25, on.Point.Y, 1e-6, "midway: 3 in in");

            s.ToggleSnapToPlan();
            Assert.IsFalse(s.SnapToPlan);
            var off = s.SnapAt(at);
            Assert.AreNotEqual(SnapModes.WallCentre, off.Kind);
            Assert.IsFalse(Math.Abs(off.Point.Y) < 1e-6 && off.Kind == SnapModes.Nearest, "not onto the plan's face line either");
            // the model's own walls still snap
            b.AddWall(b.Ground, l2, 0, 10, 5, 10);
            s.Commit("a wall");
            s.SetTool(SketchTool.Wall); // a new command: the model's lines found again
            Assert.AreEqual(SnapModes.Endpoint, s.SnapAt(new Point2(5.05, 10.05)).Kind, "the model's wall end");
        }

        [STATestMethod]
        public void WallTool_AlongTheOutsideFace_TheCentrelinesIn_TheCornersMeet()
        {
            var (b, l2, s) = BoxOnAPlan();
            s.SnapOn = false; // the typed points as they are
            s.SetTool(SketchTool.Wall);
            s.WallDrawnBy = WallLine.LeftFace; // clockwise round the outside
            foreach (var p in new[] { "0,0", "0,30", "40,30", "40,0" }) Assert.IsTrue(s.TypePoint(p), p);
            var walls = b.WallsUnder(l2).ToList();
            Assert.AreEqual(3, walls.Count);
            double t = WallJoints.ThicknessOf(b, walls[0], l2.Id) / 2; // 2x6 first story: 2 3/4 in
            var west = walls[0];
            Assert.AreEqual(t, west.Start.X, 1e-9, "the west wall's centre half its depth in");
            Assert.AreEqual(30 - t, west.End.Y, 1e-9, "run to the corner");
            var north = walls[1];
            Assert.AreEqual(new Point2(t, 30 - t), north.Start, "the corner shared");
            Assert.AreEqual(30 - t, north.End.Y, 1e-9);
            Assert.AreEqual(40 - t, north.End.X, 1e-9);
            var east = walls[2];
            Assert.AreEqual(north.End, east.Start);
            Assert.AreEqual(40 - t, east.End.X, 1e-9);
            StringAssert.Contains(s.Prompt(), "left face");
        }

        [STATestMethod]
        public void WallsFromThePlan_ProposedOneByOne_AddOrSkip()
        {
            var (b, l2, s) = BoxOnAPlan();
            var plan = s.PlanOf(s.ActiveLevel);
            CollectionAssert.AreEquivalent(new[] { "A-WALL", "FURN" }, plan.LayersShown().Select(l => l.layer).ToArray());
            Assert.AreEqual(0, new PlanWallReview(s, "FURN").Proposed.Count, "a table is not a wall");

            var r = new PlanWallReview(s, "A-WALL");
            Assert.AreEqual(5, r.Proposed.Count, "four outside walls and the one across");
            Assert.AreEqual(5, s.ProposedWalls.Count, "drawn on the plan");
            Assert.AreEqual(0, s.ProposedWall);
            string id = r.Add();
            Assert.IsNotNull(id);
            Assert.AreEqual(1, b.WallsUnder(l2).Count());
            r.Skip();
            r.AddRest();
            Assert.IsTrue(r.Done);
            Assert.AreEqual(4, r.Added);
            Assert.AreEqual(1, r.Skipped);
            Assert.AreEqual(4, b.WallsUnder(l2).Count());
            Assert.AreEqual(-1, s.ProposedWall);
            s.Undo();
            Assert.AreEqual(3, b.WallsUnder(l2).Count(), "each wall its own undo step");
            r.Stop();
            Assert.IsNull(s.ProposedWalls);

            // found again: the walls already drawn are left out
            Assert.AreEqual(2, new PlanWallReview(s, "A-WALL").Proposed.Count);
        }

        /// <summary>The window: the wall layer picked for itself (A-WALL before FURN), the proposals on the plan, closed: gone.</summary>
        [STATestMethod]
        public void WallsFromThePlan_TheWindow_PicksTheWallLayer()
        {
            var (b, l2, s) = BoxOnAPlan();
            var w = new PlanWallsWindow(s);
            try
            {
                Assert.AreEqual(5, w.Review.Proposed.Count, "A-WALL picked: its name says walls");
                SketchTests.Render(s, "plan-walls-layout"); // laid out first: a size to fit the view to
                s.ZoomTo(new System.Windows.Rect(-5, -5, 50, 40));
                SketchTests.Render(s, "plan-walls-proposed"); // the one under review bold, the rest faint
                w.Review.Add();
                Assert.AreEqual(4, s.ProposedWalls.Count);
            }
            finally { w.Close(); }
            Assert.IsNull(s.ProposedWalls, "closed: off the plan");
        }
    }
}
