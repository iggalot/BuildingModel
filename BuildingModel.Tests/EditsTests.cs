using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>Format version 2 (data bags, notes), the drafting edits, stories, undo, change sets, snaps and the located issues.</summary>
    [TestClass]
    public class EditsTests
    {
        private const double Tol = 1e-9;

        /// <summary>Two stories, 40 × 30 ft: level 2 at 10 ft, roof at 19 ft, four shear walls a story.</summary>
        private static Building TwoStory(out Level l2, out Level roof)
        {
            var b = Building.New("Two");
            l2 = b.AddLevel("Level 2", 10);
            roof = b.AddLevel("Roof", 19);
            var lv = new[] { b.Ground, l2, roof };
            for (int i = 1; i < lv.Length; i++)
            {
                b.AddDiaphragm(lv[i], new Rect2(0, 0, 40, 30));
                b.AddWall(lv[i - 1], lv[i], 0, 0, 40, 0);
                b.AddWall(lv[i - 1], lv[i], 0, 30, 40, 30);
                b.AddWall(lv[i - 1], lv[i], 0, 0, 0, 30);
                b.AddWall(lv[i - 1], lv[i], 40, 0, 40, 30);
            }
            return b;
        }

        // ------------------------------------------------------------------ format version 2

        [TestMethod]
        public void Version2_KeepsDataBagsAndNotes()
        {
            var b = TwoStory(out var l2, out _);
            b.Walls[0].SetData("ShearWall", "{\"Sheathing\":\"7/16 OSB\"}");
            b.Data["Wind"] = "{\"Exposure\":\"C\"}";
            b.AddNote(l2, 5, 6, "Check the stair opening");
            var back = ModelFile.FromJson(ModelFile.ToJson(new ModelDocument { Building = b })).Building;
            Assert.AreEqual("{\"Sheathing\":\"7/16 OSB\"}", back.Walls[0].GetData("ShearWall"));
            Assert.IsNull(back.Walls[1].GetData("ShearWall"));
            Assert.AreEqual("{\"Exposure\":\"C\"}", back.GetData("Wind"));
            Assert.AreEqual("Check the stair opening", back.Notes.Single().Text);
            Assert.AreEqual(5, back.Notes.Single().At.X, Tol);
            Assert.AreEqual(7, ModelDocument.CurrentVersion); // 7: the wall joints turned round
        }

        [TestMethod]
        public void Version1File_ReadsWithEmptyNotesAndBags()
        {
            const string v1 = "{\"Format\":\"BuildingModel\",\"Version\":1,\"Revision\":3,\"Building\":{\"Name\":\"Old\"," +
                              "\"Levels\":[{\"Id\":\"L1\",\"Name\":\"Ground\",\"Elevation\":0},{\"Id\":\"L2\",\"Name\":\"Roof\",\"Elevation\":10}]," +
                              "\"Diaphragms\":[{\"Id\":\"D1\",\"LevelId\":\"L2\",\"Pieces\":[{\"X0\":0,\"Y0\":0,\"X1\":20,\"Y1\":10}]}]," +
                              "\"Openings\":[],\"Walls\":[{\"Id\":\"W1\",\"Start\":{\"X\":0,\"Y\":0},\"End\":{\"X\":20,\"Y\":0},\"BaseLevelId\":\"L1\",\"TopLevelId\":\"L2\",\"IsShearWall\":true}]}}";
            var doc = ModelFile.FromJson(v1);
            Assert.AreEqual(1, doc.Version);
            Assert.AreEqual(0, doc.Building.Notes.Count);
            Assert.IsNull(doc.Building.Walls[0].GetData("ShearWall"));
            Assert.AreEqual("W1", doc.Building.Walls[0].Id);
            Assert.AreEqual(20, doc.Building.NetDiaphragm(doc.Building.Level("L2")).Bounds.Width, Tol);
            Assert.AreEqual("W2", doc.Building.NewId("W"));
        }

        [TestMethod]
        public void EmptyBag_IsNotWritten()
        {
            var b = Building.New();
            b.Levels[0].SetData("X", "1");
            b.Levels[0].SetData("X", null);
            StringAssert.DoesNotMatch(ModelFile.BuildingToJson(b), new System.Text.RegularExpressions.Regex("\"Data\""));
        }

        // ------------------------------------------------------------------ edits

        [TestMethod]
        public void MoveCopyDelete_KeepIdsAndCopyTheData()
        {
            var b = TwoStory(out var l2, out _);
            var w = b.Walls[0];
            w.SetData("ShearWall", "design");
            Assert.IsTrue(BuildingEdits.Move(b, w.Id, 1, 2));
            Assert.AreEqual(new Point2(1, 2), w.Start);
            string c = BuildingEdits.Copy(b, w.Id, 0, 5);
            var copy = (Wall)b.Find(c);
            Assert.AreNotEqual(w.Id, c);
            Assert.AreEqual(7, copy.Start.Y, Tol);
            Assert.AreEqual("design", copy.GetData("ShearWall"));
            copy.SetData("ShearWall", "other");
            Assert.AreEqual("design", w.GetData("ShearWall"), "the copy's bag is its own");
            Assert.IsTrue(BuildingEdits.Delete(b, c));
            Assert.IsNull(b.Find(c));
            Assert.IsFalse(BuildingEdits.Delete(b, l2.Id), "levels go with DeleteStory");
            Assert.IsFalse(BuildingEdits.Move(b, "nope", 1, 1));
        }

        [TestMethod]
        public void SplitAndJoinWalls()
        {
            var b = TwoStory(out _, out _);
            var w = b.Walls[0]; // (0,0)-(40,0)
            string rest = BuildingEdits.SplitWall(b, w.Id, new Point2(15, 0.3));
            Assert.AreEqual(15, w.Length, Tol);
            Assert.AreEqual(25, ((Wall)b.Find(rest)).Length, Tol);
            Assert.IsNull(BuildingEdits.SplitWall(b, w.Id, new Point2(0.01, 0)), "at an end: nothing");
            Assert.IsTrue(BuildingEdits.JoinWalls(b, w.Id, rest));
            Assert.AreEqual(40, w.Length, Tol);
            Assert.IsNull(b.Find(rest));
            Assert.IsFalse(BuildingEdits.JoinWalls(b, b.Walls[0].Id, b.Walls[2].Id), "not on one line");
        }

        [TestMethod]
        public void MirrorAndArray()
        {
            var b = Building.New();
            var l = b.AddLevel("Roof", 10);
            var w = b.AddWall(b.Ground, l, 2, 0, 2, 10);
            var m = BuildingEdits.Mirror(b, new[] { w.Id }, aboutVertical: true, axis: 10, keepOriginal: true);
            Assert.AreEqual(18, ((Wall)b.Find(m[0])).Start.X, Tol);
            Assert.AreEqual(2, w.Start.X, Tol);
            var a = BuildingEdits.ArrayCopy(b, new[] { w.Id }, 3, 4, 0);
            CollectionAssert.AreEqual(new[] { 6.0, 10.0, 14.0 }, a.Select(id => ((Wall)b.Find(id)).Start.X).ToArray());
        }

        [TestMethod]
        public void Grips_WallStaysSquare_RectangleKeepsTheOppositeCorner()
        {
            var b = TwoStory(out var l2, out _);
            var w = b.Walls[0];
            Assert.IsTrue(BuildingEdits.MoveWallEnd(b, w.Id, start: false, to: new Point2(50, 1)));
            Assert.AreEqual(new Point2(50, 0), w.End);
            var d = b.DiaphragmsOn(l2).Single();
            Assert.IsTrue(BuildingEdits.MoveCorner(b, d.Id, 0, new Point2(40, 30), new Point2(45, 35)));
            Assert.AreEqual(new Rect2(0, 0, 45, 35), d.Pieces[0]);
        }

        /// <summary>An end grip changes the length only: dragged off to the side, the wall does not turn.</summary>
        [TestMethod]
        public void WallEnd_StaysOnItsLine_LengthOnly()
        {
            var b = TwoStory(out _, out _);
            var w = b.Walls[0];                                   // (0,0) → (40,0)
            Assert.IsTrue(BuildingEdits.MoveWallEnd(b, w.Id, start: false, to: new Point2(5, 20)));
            Assert.AreEqual(new Point2(5, 0), w.End, "pulled up and back: shorter, still W-E (it used to turn N-S)");
            Assert.IsTrue(BuildingEdits.MoveWallEnd(b, w.Id, start: true, to: new Point2(-3, -2)));
            Assert.AreEqual(new Point2(-3, 0), w.Start, "the start pulled out: longer");
            Assert.IsFalse(BuildingEdits.MoveWallEnd(b, w.Id, start: false, to: new Point2(-3, 9)), "onto the other end: no length");
            var angled = b.AddWall(b.Ground, b.Levels.First(l => l.Elevation > 0), 0, 0, 10, 10);
            Assert.IsTrue(BuildingEdits.MoveWallEnd(b, angled.Id, start: false, to: new Point2(20, 0)));
            Assert.AreEqual(10, angled.End.X, 1e-9);
            Assert.AreEqual(10, angled.End.Y, 1e-9, "the closest point on its own (45°) line");
        }

        [TestMethod]
        public void OrthoOffset_TheLargerDirectionOnly_MoveAllMovesEach()
        {
            Assert.AreEqual((5.0, 0.0), BuildingEdits.OrthoOffset(new Point2(1, 1), new Point2(6, 3)));
            Assert.AreEqual((0.0, -4.0), BuildingEdits.OrthoOffset(new Point2(1, 1), new Point2(2, -3)));
            var b = TwoStory(out var l2, out _);
            var ids = new[] { b.Walls[0].Id, b.Walls[1].Id, b.DiaphragmsOn(l2).Single().Id, b.Walls[0].Id };
            Assert.AreEqual(3, BuildingEdits.MoveAll(b, ids, 0, 2), "the repeated ID moves once");
            Assert.AreEqual(new Point2(0, 2), b.Walls[0].Start);
            Assert.AreEqual(new Point2(40, 32), b.Walls[1].End);
            Assert.AreEqual(new Rect2(0, 2, 40, 32), b.DiaphragmsOn(l2).Single().Pieces[0]);
        }

        /// <summary>While something is dragged the snaps leave it out: it would only snap back to where it was.</summary>
        [TestMethod]
        public void SnapLines_LeaveOutWhatIsDragged()
        {
            var b = TwoStory(out var l2, out _);
            int all = Snapper.SegmentsOf(b, l2).Count;
            Assert.AreEqual(all - 1, Snapper.SegmentsOf(b, l2, new[] { b.WallsUnder(l2).First().Id }).Count);
            Assert.AreEqual(all - 4, Snapper.SegmentsOf(b, l2, new[] { b.DiaphragmsOn(l2).Single().Id }).Count);
        }

        // ------------------------------------------------------------------ stories

        [TestMethod]
        public void AddAndCopyStory()
        {
            var b = TwoStory(out var l2, out var roof);
            var top = BuildingEdits.AddStory(b, 9);
            Assert.AreEqual(28, top.Elevation, Tol);
            Assert.AreEqual("Level 4", top.Name);
            int n = BuildingEdits.CopyStory(b, roof, top);
            Assert.AreEqual(5, n, "1 diaphragm and 4 walls");
            Assert.IsTrue(b.WallsUnder(top).All(w => w.BaseLevelId == roof.Id && w.TopLevelId == top.Id));
            Assert.AreEqual(4, b.WallsUnder(top).Count());
        }

        /// <summary>Deleting level 2 of three stories: its story goes, the roof comes down 10 ft, the roof's story keeps 9 ft.</summary>
        [TestMethod]
        public void DeleteStory_LevelsAboveComeDown()
        {
            var b = TwoStory(out var l2, out var roof);
            var tall = b.AddWall(b.Ground, roof, 20, 0, 20, 30); // spans both stories
            var said = BuildingEdits.DeleteStoryConsequences(b, l2);
            Assert.IsTrue(said.Any(s => s.Contains("4 walls")), string.Join("; ", said));
            Assert.IsTrue(said.Any(s => s.Contains("Roof comes down 10")), string.Join("; ", said));
            BuildingEdits.DeleteStory(b, l2);
            Assert.IsNull(b.Level(l2.Id));
            Assert.AreEqual(9, roof.Elevation, Tol);
            Assert.AreEqual(4 + 1, b.Walls.Count);
            Assert.IsTrue(b.Walls.Where(w => w != tall).All(w => w.BaseLevelId == b.Ground.Id && w.TopLevelId == roof.Id));
            Assert.AreEqual(1, b.Diaphragms.Count);
            Assert.ThrowsExactly<System.InvalidOperationException>(() => BuildingEdits.DeleteStory(b, b.Ground));
        }

        [TestMethod]
        public void SetStoryHeight_MovesTheLevelsAbove()
        {
            var b = TwoStory(out var l2, out var roof);
            Assert.IsTrue(BuildingEdits.SetStoryHeight(b, l2, 12));
            Assert.AreEqual(12, l2.Elevation, Tol);
            Assert.AreEqual(21, roof.Elevation, Tol);
            Assert.IsFalse(BuildingEdits.SetStoryHeight(b, l2, 0));
        }

        // ------------------------------------------------------------------ undo and change sets

        /// <summary>The suite's one undo: named steps, the saved step (an undo back to it is clean), redo dropped by a new step.</summary>
        [TestMethod]
        public void EditHistory_StepsNamesAndTheSavedStep()
        {
            var h = new EditHistory();
            int changed = 0;
            h.Changed += (s, e) => changed++;
            h.Reset("a");
            Assert.IsFalse(h.IsDirty, "as opened");
            Assert.IsFalse(h.Record("a", "nothing"), "the same: no step");
            Assert.IsTrue(h.Record("b", "B set"));
            Assert.IsTrue(h.Record("c", "C set"));
            Assert.IsTrue(h.IsDirty);
            h.MarkSaved();
            Assert.IsFalse(h.IsDirty);
            Assert.AreEqual("C set", h.UndoLabel);
            Assert.AreEqual("b", h.Undo());
            Assert.IsTrue(h.IsDirty, "undone past the save");
            Assert.AreEqual("C set", h.RedoLabel);
            Assert.AreEqual("c", h.Redo());
            Assert.IsFalse(h.IsDirty, "back at the saved step");
            h.Undo();
            Assert.IsTrue(h.Record("d", "D set"), "a new step after an undo");
            Assert.IsFalse(h.CanRedo, "the undone step is dropped");
            Assert.IsTrue(h.IsDirty);
            h.Undo(); h.Undo();
            Assert.IsTrue(h.IsDirty, "the saved step was dropped: never clean again by undo");
            Assert.IsTrue(h.Amend("a2"));
            Assert.AreEqual(2, h.RedoSteps, "amend keeps the redo steps");
            Assert.AreEqual("a2", h.Current);
            Assert.IsTrue(changed > 6);
            h.Reset("x", isSaved: false);
            Assert.IsTrue(h.IsDirty, "a new project not saved yet");
        }

        [TestMethod]
        public void ModelHistory_UndoNamesTheStepItWasRecordedWith()
        {
            var b = TwoStory(out var l2, out _);
            var h = new ModelHistory();
            h.Reset(b);
            b.AddWall(b.Ground, l2, 5, 5, 15, 5);
            Assert.IsTrue(h.Record(b, "a wall drawn"));
            Assert.IsTrue(h.IsDirty);
            Assert.AreEqual("a wall drawn", h.Undo(b));
            Assert.IsFalse(h.IsDirty);
            b.AddWall(b.Ground, l2, 5, 8, 15, 8);
            h.Record(b);
            StringAssert.Contains(h.Undo(b), "added", "unnamed: from what changed");
        }

        [TestMethod]
        public void History_UndoRedo_SameBuildingObject()
        {
            var b = TwoStory(out var l2, out _);
            var h = new ModelHistory();
            h.Reset(b);
            var w = b.AddWall(b.Ground, l2, 10, 0, 10, 30);
            Assert.IsTrue(h.Record(b));
            Assert.IsFalse(h.Record(b), "no change, no step");
            BuildingEdits.Move(b, w.Id, 5, 0);
            h.Record(b);
            Assert.AreEqual(w.Id + " changed", h.Undo(b));
            Assert.AreEqual(10, ((Wall)b.Find(w.Id)).Start.X, Tol);
            Assert.AreEqual(w.Id + " added", h.Undo(b));
            Assert.IsNull(b.Find(w.Id));
            Assert.IsNull(h.Undo(b));
            Assert.AreEqual(w.Id + " added", h.Redo(b));
            Assert.AreEqual(1, h.RedoSteps);
        }

        [TestMethod]
        public void ChangeSet_RoundTripAndConflicts()
        {
            var model = TwoStory(out var l2, out _);
            var designer = ModelFile.Clone(model);
            var added = designer.AddWall(designer.Ground, designer.Level(l2.Id), 20, 0, 20, 30);
            BuildingEdits.Move(designer, designer.Walls[0].Id, 0, 1);
            designer.Walls[1].SetData("ShearWall", "results");
            BuildingEdits.Delete(designer, designer.Walls[2].Id);
            var cs = ChangeSet.FromJson(ChangeSet.Between(model, designer, baseRevision: 4).ToJson());
            Assert.AreEqual(4, cs.BaseRevision);
            Assert.AreEqual(4, cs.Changes.Count, cs.Summary());

            // meanwhile the model moved wall 1 too: that change is a conflict, the rest apply
            var w1 = model.Walls[0];
            BuildingEdits.Move(model, w1.Id, 0, 2);
            var conflicts = cs.ApplyTo(model);
            Assert.AreEqual(1, conflicts.Count);
            Assert.AreEqual(w1.Id, conflicts[0].Change.Id);
            Assert.IsNotNull(model.Find(added.Id));
            Assert.AreEqual("results", model.Walls.Single(w => w.Id == designer.Walls[1].Id).GetData("ShearWall"));
            Assert.AreEqual(2, ((Wall)model.Find(w1.Id)).Start.Y, Tol, "kept the model's edit");
            Assert.AreEqual(0, cs.ApplyTo(model).Count(c => c.Change.Id != w1.Id), "applying again: the rest are already so");
            cs.ApplyTo(model, force: true);
            Assert.AreEqual(1, ((Wall)model.Find(w1.Id)).Start.Y, Tol);
        }

        [TestMethod]
        public void ChangeSet_RejectsOtherFiles()
        {
            Assert.ThrowsExactly<InvalidDataException>(() => ChangeSet.FromJson("{\"Format\":\"Other\"}"));
            Assert.AreEqual("no change", ChangeSet.Between(Building.New(), Building.New()).Summary());
        }

        // ------------------------------------------------------------------ snaps and picks

        [TestMethod]
        public void Snaps_RankAndGrid()
        {
            var segs = new[] { new Segment2(new Point2(0, 0), new Point2(10, 0)), new Segment2(new Point2(5, -5), new Point2(5, 5)) };
            Assert.AreEqual(SnapModes.Endpoint, Snapper.Find(new Point2(0.2, 0.1), 0.5, segs, SnapModes.Default).Kind);
            var x = Snapper.Find(new Point2(5.2, 0.2), 0.5, segs, SnapModes.Default);
            Assert.AreEqual(SnapModes.Intersection, x.Kind);
            Assert.AreEqual(new Point2(5, 0), x.Point);
            var g = Snapper.Find(new Point2(21.2, 33.9), 1, segs, SnapModes.Grid, grid: 2);
            Assert.AreEqual(new Point2(22, 34), g.Point);
            Assert.AreEqual(new Point2(7, 1), Snapper.Ortho(new Point2(7, 1.5), new Point2(1, 1)));
            var perp = Snapper.Find(new Point2(3, 0.3), 0.5, segs, SnapModes.Perpendicular, from: new Point2(3, 8));
            Assert.AreEqual(new Point2(3, 0), perp.Point);
        }

        /// <summary>
        /// A CAD plan draws a wall as its two faces; the model wants its centreline. An exterior corner of 2x6 walls (6 in
        /// apart): the outside faces from the corner, the inside ones from 6 in in. The snap goes to the centres, never to a
        /// face, and only among the plan's own lines.
        /// </summary>
        [TestMethod]
        public void Snaps_WallCentre_MidwayBetweenAPlansTwoLines()
        {
            double t = 0.5; // 6 in
            var plan = new[]
            {
                new Segment2(new Point2(0, 0), new Point2(20, 0)), new Segment2(new Point2(t, t), new Point2(20, t)),   // the south wall's faces
                new Segment2(new Point2(0, 0), new Point2(0, 20)), new Segment2(new Point2(t, t), new Point2(t, 20)),   // the west wall's
                new Segment2(new Point2(30, 0), new Point2(40, 0)), new Segment2(new Point2(30, 1), new Point2(40, 1)), // 12 in apart: not a wall
            };
            var centres = Snapper.WallCentres(plan);
            Assert.AreEqual(2, centres.Count, "two walls; the pair 12 in apart is not one");
            Assert.IsTrue(centres.Any(c => System.Math.Abs(c.A.Y - 0.25) < 1e-9 && System.Math.Abs(c.B.Y - 0.25) < 1e-9), "the south wall's centre 3 in in");

            // along the wall: midway, wherever the cursor is across it
            var along = Snapper.Find(new Point2(10, 0.05), 0.6, new Segment2[0], SnapModes.Default, planLines: plan);
            Assert.AreEqual(SnapModes.WallCentre, along.Kind);
            Assert.AreEqual(10, along.Point.X, 1e-9);
            Assert.AreEqual(0.25, along.Point.Y, 1e-9);
            Assert.AreEqual("Wall centre", along.Label);

            // at the corner: where the centres meet (3 in in both ways), not the inside face's corner (6 in) nor the outside's
            var corner = Snapper.Find(new Point2(0.45, 0.4), 0.6, plan, SnapModes.Default, planLines: plan);
            Assert.AreEqual(SnapModes.WallCentre, corner.Kind);
            Assert.AreEqual(0.25, corner.Point.X, 1e-9);
            Assert.AreEqual(0.25, corner.Point.Y, 1e-9);
            Assert.AreEqual("Wall centres meet", corner.Label);

            // a free end (an opening in the wall): the centre's end
            var end = Snapper.Find(new Point2(19.8, 0.4), 0.6, new Segment2[0], SnapModes.Default, planLines: plan);
            Assert.AreEqual(20, end.Point.X, 1e-9);
            Assert.AreEqual(0.25, end.Point.Y, 1e-9);
            Assert.AreEqual("Wall centre end", end.Label);

            // off: the faces as before; no plan lines given: nothing to pair (the model's own lines are never paired)
            Assert.AreEqual(SnapModes.Endpoint, Snapper.Find(new Point2(0.45, 0.4), 0.6, plan, SnapModes.Default & ~SnapModes.WallCentre, planLines: plan).Kind);
            Assert.AreNotEqual(SnapModes.WallCentre, Snapper.Find(new Point2(10, 0.05), 0.6, plan, SnapModes.Default).Kind);
        }

        /// <summary>
        /// A wall drawn along a face: its centreline half the thickness to the side the wall lies on. Clockwise round the
        /// outside of a building (the left face), the walls go inside; the run's corner is found again.
        /// </summary>
        [TestMethod]
        public void WallByAFace_TheCentrelineHalfTheThicknessIn_TheCornerFoundAgain()
        {
            double t = 0.5;
            // clockwise round the outside, south-west corner first: up the west face, then east along the north face
            var west = WallsFromPlan.CentreOf(new Point2(0, 0), new Point2(0, 30), WallLine.LeftFace, t);
            Assert.AreEqual(new Point2(0.25, 0), west.A, "the wall lies to the right going north: east of the line");
            var north = WallsFromPlan.CentreOf(new Point2(0, 30), new Point2(40, 30), WallLine.LeftFace, t);
            Assert.AreEqual(new Point2(0, 29.75), north.A, "going east, to the right is south");
            Assert.AreEqual(new Point2(0.25, 29.75), WallsFromPlan.RunJoint(west, north), "the corner: both centres 3 in in");
            Assert.AreEqual(new Point2(-0.25, 0), WallsFromPlan.CentreOf(new Point2(0, 0), new Point2(0, 30), WallLine.RightFace, t).A);
            Assert.AreEqual(new Point2(5, 5), WallsFromPlan.CentreOf(new Point2(5, 5), new Point2(9, 5), WallLine.Centre, t).A);
            // straight on: no corner, the previous end
            Assert.AreEqual(north.B, WallsFromPlan.RunJoint(north, new Segment2(north.B, new Point2(60, 29.75))));
        }

        /// <summary>
        /// Walls from a CAD layer: a 40 x 30 ft box of 2x6 walls (6 in) with a 2x4 wall (3 1/2 in) across it, the outside
        /// wall's south line drawn in two pieces. Four outside walls and the tee; their centres meet; a wall already drawn and
        /// a pair 12 in apart are left out.
        /// </summary>
        [TestMethod]
        public void WallsFromPlan_ThePairsOfLines_TheirCentres_JoinedAndRunOnToMeet()
        {
            double o = 0.5, i2 = 3.5 / 12;
            var lines = new List<Segment2>
            {
                new Segment2(new Point2(0, 0), new Point2(15, 0)), new Segment2(new Point2(15, 0), new Point2(40, 0)), // south, outside, two pieces
                new Segment2(new Point2(40, 0), new Point2(40, 30)), new Segment2(new Point2(40, 30), new Point2(0, 30)), new Segment2(new Point2(0, 30), new Point2(0, 0)),
                new Segment2(new Point2(o, o), new Point2(40 - o, o)), new Segment2(new Point2(40 - o, o), new Point2(40 - o, 30 - o)),
                new Segment2(new Point2(40 - o, 30 - o), new Point2(o, 30 - o)), new Segment2(new Point2(o, 30 - o), new Point2(o, o)),
                new Segment2(new Point2(20 - i2 / 2, o), new Point2(20 - i2 / 2, 30 - o)), new Segment2(new Point2(20 + i2 / 2, o), new Point2(20 + i2 / 2, 30 - o)), // the 2x4 across
                new Segment2(new Point2(50, 0), new Point2(60, 0)), new Segment2(new Point2(50, 1), new Point2(60, 1)), // 12 in apart: not a wall
            };
            var walls = WallsFromPlan.Propose(lines);
            Assert.AreEqual(5, walls.Count, string.Join("; ", walls.Select(w => $"{w.A}-{w.B}")));
            var south = walls.Single(w => Math.Abs(w.A.Y - 0.25) < 1e-9 && Math.Abs(w.B.Y - 0.25) < 1e-9);
            Assert.AreEqual(0.25, south.A.X, 1e-9, "run on to the west wall's centre");
            Assert.AreEqual(39.75, south.B.X, 1e-9, "one wall, not two pieces: to the east wall's centre");
            var tee = walls.Single(w => Math.Abs(w.A.X - 20) < 1e-9 && Math.Abs(w.B.X - 20) < 1e-9);
            Assert.AreEqual(0.25, tee.A.Y, 1e-9, "the tee runs on to the south wall's centre");
            Assert.AreEqual(29.75, tee.B.Y, 1e-9);
            var west = walls.Single(w => Math.Abs(w.A.X - 0.25) < 1e-9 && Math.Abs(w.B.X - 0.25) < 1e-9);
            Assert.AreEqual(0.25, west.A.Y, 1e-9);
            Assert.AreEqual(29.75, west.B.Y, 1e-9);

            // the south wall drawn already: not proposed again
            var again = WallsFromPlan.Propose(lines, new[] { new Segment2(new Point2(0.25, 0.25), new Point2(39.75, 0.25)) });
            Assert.AreEqual(4, again.Count);
            Assert.IsFalse(again.Any(w => Math.Abs(w.A.Y - 0.25) < 1e-9 && Math.Abs(w.B.Y - 0.25) < 1e-9));
        }

        [TestMethod]
        public void Pick_NoteThenWallThenOpeningThenDiaphragm()
        {
            var b = TwoStory(out var l2, out _);
            var o = b.AddOpening(b.Ground, b.Above(l2), new Rect2(10, 10, 15, 15));
            var n = b.AddNote(l2, 30, 20, "x");
            Assert.AreEqual(n.Id, Snapper.Pick(b, l2, new Point2(30.2, 20), 0.5));
            Assert.AreEqual(b.Walls[0].Id, Snapper.Pick(b, l2, new Point2(20, 0.3), 0.5));
            Assert.AreEqual(o.Id, Snapper.Pick(b, l2, new Point2(12, 12), 0.5));
            Assert.AreEqual(b.DiaphragmsOn(l2).Single().Id, Snapper.Pick(b, l2, new Point2(25, 12), 0.5));
            Assert.AreEqual(5, Snapper.InWindow(b, l2, new Rect2(-1, -1, 41, 31)).Count(id => id.StartsWith("W") || id.StartsWith("D")));
        }

        [TestMethod]
        public void Column_ToTheFoundation_ShowsOnTheGroundPlan_AndIsPicked()
        {
            var b = TwoStory(out var l2, out var roof);
            var c = b.AddColumn(b.Ground, l2, 20, 15);    // under the Level 2 floor, down to the foundation
            var up = b.AddColumn(l2, roof, 10, 15);       // standing on the Level 2 floor
            CollectionAssert.AreEqual(new[] { c.Id }, b.ColumnsStandingOn(b.Ground).Select(x => x.Id).ToArray());
            CollectionAssert.AreEqual(new[] { up.Id }, b.ColumnsStandingOn(l2).Select(x => x.Id).ToArray());
            Assert.IsFalse(b.ColumnsUnder(b.Ground).Any()); // the ground has nothing under it
            Assert.AreEqual(c.Id, Snapper.Pick(b, b.Ground, new Point2(20.2, 15), 0.5));
            Assert.AreEqual(up.Id, Snapper.Pick(b, l2, new Point2(10.2, 15), 0.5));
        }

        // ------------------------------------------------------------------ checks

        [TestMethod]
        public void Checks_LocatedOnTheirLevel()
        {
            var b = TwoStory(out var l2, out var roof);
            b.AddWall(b.Ground, l2, 10, 0, 30, 0);              // on top of the south wall
            var issues = ModelChecks.Run(b);
            Assert.IsFalse(issues.Any(i => i.Code == "NoLateralSupport" || i.Code == "ShearWallNotReached"), "the shear wall checks are the Shear Wall Calculator's");
            var ov = issues.Single(i => i.Code == "WallsOverlap");
            Assert.AreEqual(Severity.Warning, ov.Severity, "a wall drawn twice: a warning here (two declared shear walls: the Shear Wall Calculator's error)");
            StringAssert.Contains(ov.Message, "20 ft");
            b.AddNote(new Level { Id = "L99" }, 0, 0, "lost");
            Assert.IsTrue(ModelChecks.Run(b).Any(i => i.Code == "BadReference" && i.Message.StartsWith("Note")));
        }

        [TestMethod]
        public void Checks_ObjectsDrawnTwice_AreFlagged()
        {
            var b = TwoStory(out var l2, out var roof);
            // two columns at one point over the same story; a third on top of them (end to end) is fine
            var c1 = b.AddColumn(b.Ground, l2, 20, 15);
            var c2 = b.AddColumn(b.Ground, l2, 20.1, 15);
            b.AddColumn(l2, roof, 20, 15);
            // two beams on one line overlapping 6 ft; a third end to end with the second is fine
            var m1 = b.AddBeam(l2, 0, 10, 20, 10);
            var m2 = b.AddBeam(l2, 14, 10, 30, 10);
            b.AddBeam(l2, 30, 10, 40, 10);
            // a partition drawn on the south shear wall
            var p = b.AddWall(b.Ground, l2, 5, 0, 15, 0, shearWall: false);
            var issues = ModelChecks.Run(b);

            var col = issues.Single(i => i.Code == "ColumnsOverlap");
            Assert.AreEqual(Severity.Warning, col.Severity);
            CollectionAssert.AreEquivalent(new[] { c1.Id, c2.Id }, col.Ids);
            var bm = issues.Single(i => i.Code == "BeamsOverlap");
            CollectionAssert.AreEquivalent(new[] { m1.Id, m2.Id }, bm.Ids);
            StringAssert.Contains(bm.Message, "6 ft");
            var w = issues.Single(i => i.Code == "WallsOverlap");
            Assert.IsTrue(w.Ids.Contains(p.Id));
            StringAssert.Contains(w.Message, "10 ft");
            Assert.AreEqual(Severity.Warning, w.Severity);
        }
    }
}
