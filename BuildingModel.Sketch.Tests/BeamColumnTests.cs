using System.Linq;
using System.Windows.Input;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>
    /// Beams (in a level's framing) and columns (under a level): drawn with B and P, saved in the model file (version 4),
    /// moved, copied to another level, and removed with their story.
    /// </summary>
    [TestClass]
    public class BeamColumnTests
    {
        private static (Building b, Level l2, Level l3, SketchCanvas s) TwoStories()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var l3 = BuildingEdits.AddStory(b, 10);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            return (b, l2, l3, s);
        }

        [STATestMethod]
        public void B_DrawsABeamOnTheLevel_P_PlacesAColumnUnderIt()
        {
            var (b, l2, _, s) = TwoStories();
            Assert.IsTrue(s.HandleKey(Key.B, ModifierKeys.None));
            Assert.AreEqual(SketchTool.Beam, s.Tool);
            Assert.IsTrue(s.TypePoint("0,0"));
            Assert.IsTrue(s.TypePoint("12,0"));
            var bm = b.Beams.Single();
            Assert.AreEqual(l2.Id, bm.LevelId);
            Assert.AreEqual(12, bm.Length, 1e-9);
            Assert.IsNull(s.CommandPoint, "one beam per pair of clicks");

            Assert.IsTrue(s.HandleKey(Key.P, ModifierKeys.None));
            Assert.AreEqual(SketchTool.Column, s.Tool);
            Assert.IsTrue(s.TypePoint("12,0"));
            var c = b.Columns.Single();
            Assert.AreEqual(b.Ground.Id, c.BaseLevelId);
            Assert.AreEqual(l2.Id, c.TopLevelId);
            Assert.AreEqual(new Point2(12, 0), c.At);

            s.Undo();
            Assert.AreEqual(0, b.Columns.Count, "undo takes the column away");
        }

        [STATestMethod]
        public void OnTheGround_NoBeamAndNoColumn()
        {
            var (b, _, _, s) = TwoStories();
            s.ShowLevel(b.Ground.Id);
            s.HandleKey(Key.P, ModifierKeys.None);
            s.TypePoint("5,5");
            s.HandleKey(Key.B, ModifierKeys.None);
            s.TypePoint("0,0");
            s.TypePoint("10,0");
            Assert.AreEqual(0, b.Columns.Count);
            Assert.AreEqual(0, b.Beams.Count);
        }

        [STATestMethod]
        public void AColumnUpstairs_RunsDownUntilSomethingCarriesIt()
        {
            var (b, l2, l3, s) = TwoStories();
            s.ShowLevel(l3.Id);
            s.HandleKey(Key.P, ModifierKeys.None);
            Assert.IsTrue(s.TypePoint("5,5"));
            var c = b.Columns.Single();
            Assert.AreEqual(b.Ground.Id, c.BaseLevelId, "nothing under it on Level 2: it runs to the foundation");
            Assert.IsTrue(b.ColumnsUnder(l2).Contains(c), "and shows on Level 2's plan too");

            b.AddWall(b.Ground, l2, 0, 10, 20, 10);
            Assert.AreEqual(l2.Id, BuildingEdits.ColumnBase(b, l3, new Point2(8, 10)).Id, "on a wall below: it stands on Level 2");
            b.AddBeam(l2, 0, 20, 20, 20);
            Assert.AreEqual(l2.Id, BuildingEdits.ColumnBase(b, l3, new Point2(8, 20.1)).Id, "on a beam: it stands on the beam");
            Assert.AreEqual(l2.Id, BuildingEdits.ColumnBase(b, l3, new Point2(5, 5)).Id, "on the column below");
            Assert.AreEqual(b.Ground.Id, BuildingEdits.ColumnBase(b, l2, new Point2(5, 5)).Id);
        }

        [STATestMethod]
        public void ABeamDrawnLater_TrimsTheColumn_DeletingItRunsTheColumnDownAgain()
        {
            var (b, l2, l3, s) = TwoStories();
            s.ShowLevel(l3.Id);
            s.HandleKey(Key.P, ModifierKeys.None);
            s.TypePoint("10,5");
            var c = b.Columns.Single();
            Assert.AreEqual(b.Ground.Id, c.BaseLevelId);
            var said = new System.Collections.Generic.List<string>();
            s.Status += (o, e) => said.Add(e.Text);

            s.ShowLevel(l2.Id);
            s.HandleKey(Key.B, ModifierKeys.None);
            s.TypePoint("0,5");
            s.TypePoint("20,5");
            Assert.AreEqual(l2.Id, c.BaseLevelId, "the beam drawn across its path: the column now stands on it");
            Assert.IsTrue(said.Any(t => t.Contains($"{c.Id} now stands on")), "the status says so");

            var bm = b.Beams.Single();
            s.Edit("Beam deleted.", x => BuildingEdits.Delete(x, bm.Id));
            Assert.AreEqual(b.Ground.Id, b.Columns.Single().BaseLevelId, "its support gone: down to the foundation again");
            s.Undo();
            Assert.AreEqual(l2.Id, b.Columns.Single().BaseLevelId, "undo brings the beam and the trim back together");
        }

        [STATestMethod]
        public void AColumnSetToRunThrough_StaysThrough_AndTheCrossingIsAWarning()
        {
            var (b, l2, l3, s) = TwoStories();
            var c = b.AddColumn(b.Ground, l3, 10, 5);
            c.BaseFixed = true;
            s.Commit("A post through two stories.");
            s.Edit("A beam.", x => x.AddBeam(l2, 0, 5, 20, 5));
            Assert.AreEqual(b.Ground.Id, c.BaseLevelId, "set by hand: it is not trimmed");
            var issue = ModelChecks.Run(b).Single(i => i.Code == "BeamCrossesColumn");
            Assert.AreEqual(Checks.Severity.Warning, issue.Severity);
            CollectionAssert.AreEquivalent(new[] { b.Beams[0].Id, c.Id }, issue.Ids, "selecting the warning shows both members");
            Assert.AreEqual(l2.Id, issue.LevelId);
            Assert.AreEqual(new Point2(10, 5), issue.At);

            c.BaseFixed = false;
            s.Commit("Automatic again.");
            Assert.AreEqual(l2.Id, c.BaseLevelId);
            Assert.IsFalse(ModelChecks.Run(b).Any(i => i.Code == "BeamCrossesColumn"));
        }

        [STATestMethod]
        public void ShowIssue_GoesToItsLevel_AndSelectsBothMembers()
        {
            var (b, l2, l3, s) = TwoStories();
            var c = b.AddColumn(b.Ground, l2, 10, 5.6);
            var bm = b.AddBeam(l2, 0, 5, 20, 5);
            s.ShowLevel(l3.Id);
            var issue = ModelChecks.Run(b).Single(i => i.Code == "BeamMissesColumn");
            s.ShowIssue(issue);
            Assert.AreEqual(l2.Id, s.ActiveLevel.Id, "the level the issue is on");
            CollectionAssert.AreEquivalent(new[] { bm.Id, c.Id }, s.Selection.ToList(), "both members selected");
        }

        [TestMethod]
        public void ABeamThatJustMissesAColumn_IsAWarning()
        {
            var (b, l2, _, _) = TwoStories();
            var c = b.AddColumn(b.Ground, l2, 10, 5.6);
            var bm = b.AddBeam(l2, 0, 5, 20, 5);
            var issue = ModelChecks.Run(b).Single(i => i.Code == "BeamMissesColumn");
            CollectionAssert.AreEquivalent(new[] { bm.Id, c.Id }, issue.Ids);
            StringAssert.Contains(issue.Message, "0.6 ft");
            c.At = new Point2(10, 5.1);
            Assert.IsFalse(ModelChecks.Run(b).Any(i => i.Code == "BeamMissesColumn"), "within 0.25 ft they bear: connected");
        }

        [TestMethod]
        public void PointSnaps_AColumnCentre_BeforeALineEnd()
        {
            var (b, l2, l3, _) = TwoStories();
            b.AddColumn(b.Ground, l2, 10, 5);
            b.AddBeam(l2, 0, 0, 10, 4.8);
            var points = Snapper.PointsOf(b, l2);
            Assert.IsTrue(points.Any(p => p.Label.StartsWith("C") && p.At.Equals(new Point2(10, 5))));
            Assert.IsTrue(points.Count(p => p.Label.EndsWith(" end")) == 2);
            var r = Snapper.Find(new Point2(10.1, 5.05), 0.5, Snapper.SegmentsOf(b, l2), SnapModes.Default, null, 0, points);
            Assert.AreEqual(SnapModes.Point, r.Kind);
            Assert.AreEqual(new Point2(10, 5), r.Point);
            Assert.AreEqual(b.Columns[0].Id, r.Label);
            Assert.IsTrue(Snapper.PointsOf(b, l3).Any(p => p.At.Equals(new Point2(10, 5))), "the column's top is on Level 2: drawing on Level 3 you see it from above too");
        }

        [TestMethod]
        public void BeamsAndColumns_SurviveTheFile()
        {
            var (b, l2, l3, _) = TwoStories();
            b.AddBeam(l3, 0, 0, 0, 14);
            b.AddColumn(b.Ground, l2, 0, 14);
            var back = ModelFile.BuildingFromJson(ModelFile.BuildingToJson(b));
            Assert.AreEqual(1, back.Beams.Count);
            Assert.AreEqual(l3.Id, back.Beams[0].LevelId);
            Assert.AreEqual(new Point2(0, 14), back.Beams[0].End);
            Assert.AreEqual(1, back.Columns.Count);
            Assert.AreEqual(l2.Id, back.Columns[0].TopLevelId);
            Assert.IsTrue(ModelDocument.CurrentVersion >= 4); // beams and columns came in version 4
        }

        [TestMethod]
        public void MoveCopyToLevel_AndDeleteStory()
        {
            var (b, l2, l3, _) = TwoStories();
            var bm = b.AddBeam(l2, 0, 0, 10, 0);
            var c = b.AddColumn(b.Ground, l2, 10, 0);
            Assert.IsTrue(BuildingEdits.Move(b, bm.Id, 0, 5));
            Assert.AreEqual(new Point2(0, 5), bm.Start);
            Assert.IsTrue(BuildingEdits.Move(b, c.Id, 0, 5));
            Assert.AreEqual(new Point2(10, 5), c.At);

            var ids = BuildingEdits.CopyToLevel(b, new[] { bm.Id, c.Id }, l3);
            Assert.AreEqual(2, ids.Count);
            Assert.AreEqual(l3.Id, b.Beams.Single(x => x.Id != bm.Id).LevelId);
            var c2 = b.Columns.Single(x => x.Id != c.Id);
            Assert.AreEqual(l2.Id, c2.BaseLevelId, "the copy stands on the level below its new top");
            Assert.AreEqual(l3.Id, c2.TopLevelId);

            BuildingEdits.DeleteStory(b, l3);
            Assert.AreEqual(1, b.Beams.Count, "the beam on the deleted level goes");
            Assert.AreEqual(1, b.Columns.Count, "the column of the deleted story goes");
            Assert.AreEqual(0, ModelChecks.Run(b).Count(i => i.Code == "BadReference"));
        }
    }
}
