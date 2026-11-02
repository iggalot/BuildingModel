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
            Assert.AreEqual(3, ModelDocument.CurrentVersion);
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

        // ------------------------------------------------------------------ checks

        [TestMethod]
        public void Checks_LocatedOnTheirLevel()
        {
            var b = TwoStory(out var l2, out var roof);
            var free = b.AddWall(b.Ground, l2, 20, 50, 30, 50); // outside the diaphragm
            b.AddWall(b.Ground, l2, 10, 0, 30, 0);              // on top of the south wall
            var issues = ModelChecks.Run(b);
            var nr = issues.Single(i => i.Code == "ShearWallNotReached");
            Assert.AreEqual(l2.Id, nr.LevelId);
            Assert.AreEqual(new Point2(25, 50), nr.At.Value);
            var ov = issues.Single(i => i.Code == "WallOverlap");
            Assert.AreEqual(Severity.Error, ov.Severity);
            StringAssert.Contains(ov.Message, "20 ft");
            b.AddNote(new Level { Id = "L99" }, 0, 0, "lost");
            Assert.IsTrue(ModelChecks.Run(b).Any(i => i.Code == "BadReference" && i.Message.StartsWith("Note")));
        }
    }
}
