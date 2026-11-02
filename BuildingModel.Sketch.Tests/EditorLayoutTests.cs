using System;
using System.Linq;
using System.Windows.Input;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>
    /// The editor pass of 2026-11-02: Esc returns to Select, Ctrl+C copies and places, diaphragm sides have grips, objects copy
    /// to another level, the roof slope is typed as rise in 12, and the grid's spacings.
    /// </summary>
    [TestClass]
    public class EditorLayoutTests
    {
        private static (Building b, Level l2, SketchCanvas s) Box()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            return (b, l2, s);
        }

        [STATestMethod]
        public void Escape_AlwaysBackToSelect_EvenInTheMiddleOfACommand()
        {
            var (b, _, s) = Box();
            s.HandleKey(Key.W, ModifierKeys.None);
            Assert.IsTrue(s.TypePoint("0,0"));
            Assert.IsNotNull(s.CommandPoint, "a wall under way");
            s.HandleKey(Key.Escape, ModifierKeys.None);
            Assert.AreEqual(SketchTool.Select, s.Tool, "one Esc: the command is cancelled AND the tool put down");
            Assert.IsNull(s.CommandPoint);
            Assert.AreEqual(0, b.Walls.Count);

            s.Select(new[] { b.AddWall(b.Ground, b.LevelsBottomUp.Last(), 0, 0, 10, 0).Id });
            s.HandleKey(Key.Escape, ModifierKeys.None);
            Assert.AreEqual(0, s.Selection.Count, "in Select, Esc clears the selection");
        }

        [STATestMethod]
        public void CtrlC_CopiesAndPlaces_StraightAcrossOrUp_ThenSelectsTheCopy()
        {
            var (b, l2, s) = Box();
            var w = b.AddWall(b.Ground, l2, 0, 0, 20, 0);
            s.Commit("A wall.");
            s.Select(new[] { w.Id });
            Assert.IsTrue(s.HandleKey(Key.C, ModifierKeys.Control));
            Assert.AreEqual(SketchTool.Copy, s.Tool);
            Assert.AreEqual(new Point2(10, 0), s.CommandPoint, "the copy is held at the selection's centre");
            Assert.IsTrue(s.TypePoint("@1,15"), "mostly up: the copy goes straight up");
            Assert.AreEqual(2, b.Walls.Count);
            var copy = b.Walls.Single(x => x.Id != w.Id);
            Assert.AreEqual(new Point2(0, 15), copy.Start);
            Assert.AreEqual(new Point2(20, 15), copy.End);
            Assert.AreEqual(SketchTool.Select, s.Tool, "one copy, then back to Select");
            CollectionAssert.AreEqual(new[] { copy.Id }, s.Selection.ToList(), "the copy is selected, ready to move or copy again");
            s.Undo();
            Assert.AreEqual(1, b.Walls.Count, "one undo step");

            s.ClearSelection();
            s.CopyAndPlace();
            Assert.AreEqual(SketchTool.Select, s.Tool, "nothing selected: nothing to copy");
        }

        [STATestMethod]
        public void DiaphragmSideGrips_MoveOneSide()
        {
            var (b, l2, s) = Box();
            var d = b.AddDiaphragm(l2, new Rect2(0, 0, 40, 30));
            s.Select(new[] { d.Id });
            Assert.IsTrue(s.DragGrip(new Point2(40, 15), new Point2(48, 21)), "the east side's grip, dragged east (and a little north)");
            Assert.AreEqual(new Rect2(0, 0, 48, 30), ((Diaphragm)s.Building.Find(d.Id)).Pieces[0], "only the east side moves, straight east");
            Assert.IsTrue(s.DragGrip(new Point2(24, 30), new Point2(10, 36)), "the north side's grip");
            Assert.AreEqual(new Rect2(0, 0, 48, 36), ((Diaphragm)s.Building.Find(d.Id)).Pieces[0]);
            Assert.IsFalse(s.DragGrip(new Point2(0, 18), new Point2(48, 18)), "the west side onto the east side: no area");
        }

        [TestMethod]
        public void MoveSide_AndCopyToLevel_InTheModel()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var roof = BuildingEdits.AddStory(b, 9);
            var d = b.AddDiaphragm(l2, new Rect2(0, 0, 40, 30));
            var w = b.AddWall(b.Ground, l2, 0, 0, 40, 0);
            d.SetData("x", "kept");
            Assert.IsTrue(BuildingEdits.MoveSide(b, d.Id, 0, RectSide.West, -5));
            Assert.AreEqual(new Rect2(-5, 0, 40, 30), d.Pieces[0]);
            Assert.IsFalse(BuildingEdits.MoveSide(b, d.Id, 0, RectSide.South, 30), "no depth left");
            Assert.IsFalse(BuildingEdits.MoveSide(b, d.Id, 3, RectSide.South, 1), "no such piece");

            var added = BuildingEdits.CopyToLevel(b, new[] { d.Id, w.Id }, roof);
            Assert.AreEqual(2, added.Count);
            var dc = (Diaphragm)b.Find(added[0]);
            var wc = (Wall)b.Find(added[1]);
            Assert.AreEqual(roof.Id, dc.LevelId);
            Assert.AreEqual(d.Pieces[0], dc.Pieces[0]);
            Assert.AreEqual("kept", dc.GetData("x"), "the data goes with the copy");
            Assert.AreEqual(l2.Id, wc.BaseLevelId, "a wall goes in the story under the level");
            Assert.AreEqual(roof.Id, wc.TopLevelId);
            Assert.AreEqual(0, BuildingEdits.CopyToLevel(b, new[] { w.Id }, b.Ground).Count, "no story under the ground");
        }

        [TestMethod]
        public void RoofSlope_TypedAsRiseIn12_OrDegrees()
        {
            double Deg(double rise) => Math.Atan(rise / 12) * 180 / Math.PI;
            Assert.AreEqual(Deg(4), RoofPanel.ParseSlope("4"), 1e-9);
            Assert.AreEqual(Deg(6), RoofPanel.ParseSlope("6:12"), 1e-9);
            Assert.AreEqual(Deg(8), RoofPanel.ParseSlope("8/12"), 1e-9);
            Assert.AreEqual(45, RoofPanel.ParseSlope("12:12"), 1e-9);
            Assert.AreEqual(Deg(3), RoofPanel.ParseSlope("6 in 24"), 1e-9);
            Assert.AreEqual(30, RoofPanel.ParseSlope("30°"), 1e-9);
            Assert.AreEqual(22.5, RoofPanel.ParseSlope("22.5 deg"), 1e-9);
            Assert.IsTrue(double.IsNaN(RoofPanel.ParseSlope("abc")));
            Assert.IsTrue(double.IsNaN(RoofPanel.ParseSlope("95°")));
            Assert.IsTrue(double.IsNaN(RoofPanel.ParseSlope("-2")));
        }

        [STATestMethod]
        public void RoofPanel_ShowsRiseIn12_KeepsTheExactAngleUntilRetyped()
        {
            var (b, l2, _) = Box();
            b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            b.Roof.Shape = RoofShape.Gable;
            b.Roof.Pitch = 18.43; // stored in degrees (about 4:12)
            var p = new RoofPanel(b);
            Assert.AreEqual(18.43, p.Roof.Pitch, 1e-12, "opened and OK'd: the model's angle, not one rebuilt from 4:12");
            p.Type("pitch", "12");
            Assert.AreEqual(45, p.Roof.Pitch, 1e-9);
            p.Type("pitch", "30°");
            Assert.AreEqual(30, p.Roof.Pitch, 1e-9);
        }

        [STATestMethod]
        public void Grid_OneFootAndTenFeet_CoarserWhenZoomedOut()
        {
            var (_, _, s) = Box();
            // the default zoom, 10 px per ft
            Assert.AreEqual((1.0, 10.0), s.GridSteps());
            s.ZoomTo(new System.Windows.Rect(0, 0, 100, 100)); // no window: the farthest zoom
            var far = s.GridSteps();
            Assert.IsTrue(far.Minor * s.Zoom >= 8, "minor lines never closer than 8 px");
            Assert.AreEqual(far.Minor * 10, far.Major, 1e-12);
        }
    }
}
