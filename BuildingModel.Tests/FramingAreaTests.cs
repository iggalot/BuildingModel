using System;
using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Statics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// Framing areas (owner 2026-11-10): each diaphragm is covered by rectangles, each with its joists' way; a part turned the
    /// other way cuts the areas round it back, so a joist that ran through is two shorter ones ending at it. Every joist needs
    /// two supports (walls, columns, beams, girders), as a beam does. A beam carrying a beam is a girder.
    /// </summary>
    [TestClass]
    public class FramingAreaTests
    {
        /// <summary>A floor 40 W-E by 20 S-N on Level 2, its joists W-E, the four outside walls under it.</summary>
        private static Building Floor(out Level l2, out Diaphragm d)
        {
            var b = Building.New("Framing");
            l2 = b.AddLevel("Level 2", 10);
            d = b.AddDiaphragm(l2, new Rect2(0, 0, 40, 20));
            BuildingEdits.SetDiaphragmSpan(b, d.Id, SpanDirection.WestEast);
            b.AddWall(b.Ground, l2, 0, 0, 40, 0);
            b.AddWall(b.Ground, l2, 0, 20, 40, 20);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 40, 0, 40, 20);
            return b;
        }

        private static ModelIssue[] Errors(Building b, string code) => ModelChecks.Run(b).Where(i => i.Code == code).ToArray();

        private static void CoversExactly(Diaphragm d)
        {
            double sum = d.Framing.Sum(a => a.Rect.Area);
            Assert.AreEqual(Region.Of(d.Pieces).Area, sum, 1e-9, "the areas cover it");
            Assert.AreEqual(sum, Region.Of(d.Framing.Select(a => a.Rect)).Area, 1e-9, "no overlaps");
            Assert.AreEqual(d.Framing.Count, d.Framing.Select(a => a.Id).Distinct().Count(), "each its own ID");
        }

        [TestMethod]
        public void OneWay_EveryJoistOnTheEndWalls_40Ft()
        {
            var b = Floor(out var l2, out var d);
            var joists = Joists.Of(b, l2);
            Assert.AreEqual(11, joists.Count, "20 ft at 24 in. (Level 2 is the roof: its rafter spacing), the last on the far edge");
            Assert.IsTrue(joists.All(j => j.AlongX && Math.Abs(j.Length - 40) < 1e-9 && j.Stands));
            Assert.AreEqual(20, joists.Max(j => j.Line), 1e-9, "the last on the far edge");
            Assert.AreEqual(0, Errors(b, "JoistNotHeldUp").Length);
        }

        /// <summary>
        /// The owner's case: an 8 ft strip S-N across the middle (x 16 to 24) of the W-E floor. The floor's area is cut in
        /// two either side of it: each joist that ran 40 ft is now two of 16 ft, ending at the strip's edges, with nothing
        /// under them there: the model's error until the bearing walls are drawn.
        /// </summary>
        [TestMethod]
        public void StripTurnedAcrossTheMiddle_TheLongJoistsBecomeTwo_TheirInnerEndsNeedWalls()
        {
            var b = Floor(out var l2, out var d);
            var added = FramingAreas.Place(b, d, new Rect2(16, 0, 24, 20), SpanDirection.SouthNorth);
            Assert.AreEqual(1, added.Count);
            CoversExactly(d);
            Assert.AreEqual(3, d.Framing.Count);
            CollectionAssert.AreEquivalent(new[] { new Rect2(0, 0, 16, 20), new Rect2(16, 0, 24, 20), new Rect2(24, 0, 40, 20) }, d.Framing.Select(a => a.Rect).ToArray());
            var we = Joists.Of(b, l2).Where(j => j.AlongX && j.Line > 0 && j.Line < 20).ToList(); // the edge ones lie on the N and S walls
            Assert.IsTrue(we.All(j => Math.Abs(j.Length - 16) < 1e-9), "two of 16 ft for each 40");
            Assert.IsTrue(we.All(j => !j.Stands && j.Supports.Count == 1), "on the end wall only");
            var sn = Joists.Of(b, l2).Where(j => !j.AlongX).ToList();
            Assert.IsTrue(sn.All(j => j.Stands && Math.Abs(j.Length - 20) < 1e-9), "the strip's joists bear on the N and S walls");
            var errors = Errors(b, "JoistNotHeldUp");
            Assert.AreEqual(2, errors.Length, string.Join(" | ", errors.Select(e => e.Message)));   // one per side, each run held the same way
            Assert.IsTrue(errors.All(e => e.Severity == Severity.Error && e.LevelId == l2.Id));
            StringAssert.Contains(errors[0].Message, "one support");

            // the bearing walls under the strip's edges: every joist stands
            b.AddWall(b.Ground, l2, 16, 0, 16, 20);
            b.AddWall(b.Ground, l2, 24, 0, 24, 20);
            Assert.AreEqual(0, Errors(b, "JoistNotHeldUp").Length);
            Assert.IsTrue(Joists.Of(b, l2).All(j => j.Stands));
        }

        [TestMethod]
        public void TurnedFrom_ADrawnAreaRunsAcrossTheOneItLiesOn() =>
            Assert.AreEqual(SpanDirection.SouthNorth, FramingAreas.TurnedFrom(Floor(out _, out var d).Diaphragms[0], new Rect2(16, 0, 24, 20)));

        /// <summary>Joists W-E on walls at x = 0 and 16 of a 20 ft area: the last 4 ft a cantilever. They stand.</summary>
        [TestMethod]
        public void Cantilever_TwoSupports_Stands()
        {
            var b = Building.New("C");
            var l2 = b.AddLevel("Level 2", 10);
            var d = b.AddDiaphragm(l2, new Rect2(0, 0, 20, 30));
            Assert.AreEqual(SpanDirection.WestEast, d.Framing.Single().Span);
            b.AddWall(b.Ground, l2, 0, 0, 0, 30);
            b.AddWall(b.Ground, l2, 16, 0, 16, 30);
            var j = Joists.Of(b, l2).First();
            Assert.IsTrue(j.Stands);
            CollectionAssert.AreEqual(new[] { 0.0, 16.0 }, j.Supports.Select(s => s.At).ToArray());
            Assert.AreEqual(0, Errors(b, "JoistNotHeldUp").Length);
        }

        /// <summary>Joists W-E 0 to 20 on the west wall and a beam across them at x = 12 (on posts): the beam holds them, anywhere along.</summary>
        [TestMethod]
        public void OnABeamPartwayAlong_Stands_TheBeamCarriesThem()
        {
            var b = Building.New("B");
            var l2 = b.AddLevel("Level 2", 10);
            var d = b.AddDiaphragm(l2, new Rect2(0, 0, 20, 30));
            b.AddWall(b.Ground, l2, 0, 0, 0, 30);
            b.AddColumn(b.Ground, l2, 12, 0);
            b.AddColumn(b.Ground, l2, 12, 30);
            var bm = b.AddBeam(l2, 12, 0, 12, 30);
            var joists = Joists.Of(b, l2).Where(j => j.Line > 0 && j.Line < 30).ToList(); // the edge ones: on the posts
            Assert.IsTrue(joists.All(j => j.Stands && j.Supports.Any(s => s.On == bm.Id && Math.Abs(s.At - 12) < 1e-9)));
            var roles = FramingRoles.Of(b, l2);
            Assert.AreEqual(FramingRole.Beam, roles[bm.Id].Role);
            Assert.AreEqual(joists.Count, roles[bm.Id].CarriesJoists[d.Framing[0].Id]);
        }

        /// <summary>A beam landing on another partway along: the one carrying it is a girder, found from the drawing.</summary>
        [TestMethod]
        public void ABeamOnABeam_TheCarrierIsAGirder()
        {
            var b = Floor(out var l2, out _);
            var g = b.AddBeam(l2, 20, 0, 20, 20);   // on the N and S walls
            var bm = b.AddBeam(l2, 20, 10, 40, 10); // from the girder's middle to the east wall
            var roles = FramingRoles.Of(b, l2);
            Assert.AreEqual(FramingRole.Girder, roles[g.Id].Role);
            CollectionAssert.AreEqual(new[] { bm.Id }, roles[g.Id].CarriesBeams);
            Assert.AreEqual(FramingRole.Beam, roles[bm.Id].Role);
            StringAssert.StartsWith(roles[g.Id].Describe(), "girder: carries " + bm.Id);
        }

        [TestMethod]
        public void Delete_ItsPlaceGoesToTheAreaBeside_ThenJoinMakesOne()
        {
            var b = Floor(out _, out var d);
            var mid = FramingAreas.Place(b, d, new Rect2(16, 0, 24, 20), SpanDirection.SouthNorth).Single();
            Assert.IsTrue(BuildingEdits.Delete(b, mid));
            CoversExactly(d);
            Assert.AreEqual(2, d.Framing.Count);
            Assert.IsTrue(d.Framing.All(a => a.Span == SpanDirection.WestEast));
            Assert.AreEqual(1, FramingAreas.Join(b, d));
            Assert.AreEqual(new Rect2(0, 0, 40, 20), d.Framing.Single().Rect);
            Assert.IsFalse(BuildingEdits.Delete(b, d.Framing.Single().Id), "a diaphragm is always framed");
        }

        /// <summary>
        /// An area's name (owner 2026-11-10: a hallway, a balcony; the Load Manager gives it the loads of the use so named): the
        /// pieces an area is cut into keep it, a strip placed across it starts with none, and Join keeps areas of different
        /// names apart.
        /// </summary>
        [TestMethod]
        public void Name_KeptByThePieces_JoinKeepsDifferentNamesApart()
        {
            var b = Floor(out _, out var d);
            Assert.IsTrue(FramingAreas.SetName(b, new[] { d.Framing.Single().Id }, "  Bedroom "));
            Assert.AreEqual("Bedroom", d.Framing.Single().Name, "trimmed");
            Assert.IsFalse(FramingAreas.SetName(b, new[] { d.Framing.Single().Id }, "Bedroom"), "no change");
            var mid = FramingAreas.Place(b, d, new Rect2(16, 0, 24, 20), SpanDirection.SouthNorth).Single();
            CoversExactly(d);
            Assert.IsNull(FramingAreas.Find(b, mid).a.Name, "the strip has a name of its own (none yet)");
            Assert.IsTrue(d.Framing.Where(a => a.Id != mid).All(a => a.Name == "Bedroom"), "both pieces keep it");
            Assert.IsTrue(FramingAreas.SetName(b, new[] { mid }, "Hallway"));

            // turned back the same way: Join keeps the hallway apart from the bedroom, until it is named as the bedroom is
            Assert.IsTrue(FramingAreas.SetSpan(b, mid, SpanDirection.WestEast));
            FramingAreas.Join(b, d);
            Assert.AreEqual(3, d.Framing.Count, "Bedroom | Hallway | Bedroom");
            Assert.AreEqual("Hallway", FramingAreas.Find(b, mid).a?.Name ?? d.Framing.Single(a => a.Rect.Equals(new Rect2(16, 0, 24, 20))).Name);
            Assert.IsTrue(FramingAreas.SetName(b, d.Framing.Select(a => a.Id), "Bedroom"));
            FramingAreas.Join(b, d);
            Assert.AreEqual(new Rect2(0, 0, 40, 20), d.Framing.Single().Rect);
            Assert.AreEqual("Bedroom", d.Framing.Single().Name);
            Assert.IsTrue(FramingAreas.SetName(b, new[] { d.Framing.Single().Id }, " "), "blank: no name");
            Assert.IsNull(d.Framing.Single().Name);
        }

        [TestMethod]
        public void Reshape_GrownItTakes_ShrunkItGivesBack()
        {
            var b = Floor(out _, out var d);
            var mid = FramingAreas.Place(b, d, new Rect2(16, 0, 24, 20), SpanDirection.SouthNorth).Single();
            Assert.IsTrue(BuildingEdits.MoveSide(b, mid, 0, RectSide.West, 14));
            CoversExactly(d);
            Assert.IsTrue(d.Framing.Any(a => a.Rect.Equals(new Rect2(0, 0, 14, 20))), "the west area cut back");
            Assert.IsTrue(BuildingEdits.MoveSide(b, mid, 0, RectSide.East, 22));
            CoversExactly(d);
            Assert.IsTrue(d.Framing.Any(a => a.Rect.Equals(new Rect2(22, 0, 40, 20)) && a.Span == SpanDirection.WestEast), "what it gave up joins the area beside it");
        }

        /// <summary>A notch cut from the floor: the areas follow; a move takes them along; a copy gets IDs of its own.</summary>
        [TestMethod]
        public void TheAreasFollowTheirDiaphragm()
        {
            var b = Floor(out var l2, out var d);
            FramingAreas.Place(b, d, new Rect2(16, 0, 24, 20), SpanDirection.SouthNorth);
            BuildingEdits.CutDiaphragms(b, l2, new Rect2(30, 10, 40, 20));
            CoversExactly(d);
            Assert.IsTrue(BuildingEdits.Move(b, d.Id, 5, 0));
            Assert.AreEqual(5, d.Framing.Min(a => a.Rect.X0), 1e-9);
            CoversExactly(d);
            var copy = (Diaphragm)b.Find(BuildingEdits.Copy(b, d.Id, 0, 50));
            Assert.AreEqual(d.Framing.Count, copy.Framing.Count);
            Assert.IsFalse(copy.Framing.Select(a => a.Id).Intersect(d.Framing.Select(a => a.Id)).Any(), "IDs of its own");
            Assert.AreEqual(b.AllIds().Count(), b.AllIds().Distinct().Count());
            string before = string.Join(" | ", b.Diaphragms.Select(x => x.Id + ":" + string.Join(",", x.Framing.Select(a => a.Id + a.Rect + a.Span))));
            bool changed = FramingAreas.Tidy(b);
            string after = string.Join(" | ", b.Diaphragms.Select(x => x.Id + ":" + string.Join(",", x.Framing.Select(a => a.Id + a.Rect + a.Span))));
            Assert.IsFalse(changed, before + " ==> " + after);
        }

        /// <summary>An opening through the floor cuts its joists (the header there is a beam).</summary>
        [TestMethod]
        public void AnOpeningCutsTheJoists()
        {
            var b = Floor(out var l2, out _);
            var l3 = b.AddLevel("Level 3", 20);
            b.AddOpening(b.Ground, l3, new Rect2(10, 5, 14, 9));
            var cut = Joists.Of(b, l2).Where(j => j.Line > 5 && j.Line < 9).ToList();
            Assert.IsTrue(cut.Count > 0 && cut.All(j => j.Length < 40));
            Assert.IsTrue(cut.Any(j => Math.Abs(j.End.X - 10) < 1e-9 && !j.Stands), "the tail into the opening hangs on nothing until its header is drawn");
        }
    }
}
