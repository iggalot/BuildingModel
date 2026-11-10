using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Statics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// What holds a beam up (<see cref="BeamSupports"/>), and the model's error when a beam cannot stand (owner 2026-11-10:
    /// the model is not valid, nothing is calculated; the error names the beam and its level, to be shown on the plan).
    /// </summary>
    [TestClass]
    public class BeamSupportsTests
    {
        /// <summary>A one-story box 30 × 20, Level 2 over it; walls only where a test draws them.</summary>
        private static Building Story(out Level l2)
        {
            var b = Building.New("Beams");
            l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            return b;
        }

        private static ModelIssue[] NotHeldUp(Building b) => ModelChecks.Run(b).Where(i => i.Code == "BeamNotHeldUp").ToArray();

        [TestMethod]
        public void OneSupport_AnError_OnItsLevel_NamingTheBeam()
        {
            var b = Story(out var l2);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            var bm = b.AddBeam(l2, 0, 10, 20, 10);
            var e = NotHeldUp(b).Single();
            Assert.AreEqual(Severity.Error, e.Severity);
            CollectionAssert.AreEqual(new[] { bm.Id }, e.Ids);
            Assert.AreEqual(l2.Id, e.LevelId, "shown on its level");
            Assert.IsNotNull(e.At);
            StringAssert.Contains(e.Message, "one support");
        }

        /// <summary>The hub's --select BM1: the error naming the beam is the one shown (its level, the beam selected).</summary>
        [TestMethod]
        public void IssueFor_TheErrorNamingTheBeam()
        {
            var b = Story(out var l2);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            var bm = b.AddBeam(l2, 0, 10, 20, 10);
            var all = ModelChecks.Run(b);
            var e = ModelChecks.IssueFor(all, bm.Id);
            Assert.AreEqual("BeamNotHeldUp", e.Code);
            Assert.AreEqual(l2.Id, e.LevelId);
            Assert.IsNull(ModelChecks.IssueFor(all, "nothing"));
        }

        [TestMethod]
        public void NoSupport_AnError()
        {
            var b = Story(out var l2);
            var bm = b.AddBeam(l2, 5, 10, 20, 10);
            StringAssert.Contains(NotHeldUp(b).Single(i => i.Ids.Contains(bm.Id)).Message, "no support");
        }

        /// <summary>Walls at 6 and 18 ft under a 24 ft beam (3 to 27): both ends cantilevered, 3 ft each. It stands.</summary>
        [TestMethod]
        public void DoubleCantilever_Stands()
        {
            var b = Story(out var l2);
            b.AddWall(b.Ground, l2, 6, 0, 6, 20);
            b.AddWall(b.Ground, l2, 18, 0, 18, 20);
            var bm = b.AddBeam(l2, 3, 10, 27, 10);
            var s = BeamSupports.Of(b, l2)[bm.Id];
            CollectionAssert.AreEqual(new[] { "at 3 ft", "at 15 ft" }, s.Select(x => x.Name).ToArray());
            Assert.AreEqual(0, NotHeldUp(b).Length);
        }

        /// <summary>Two beams end to end over nothing, each on a wall at its far end: a mechanism, both named in one error.</summary>
        [TestMethod]
        public void EndToEndOverNothing_AMechanism()
        {
            var b = Story(out var l2);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 30, 0, 30, 20);
            var a = b.AddBeam(l2, 0, 10, 15, 10);
            var c = b.AddBeam(l2, 15, 10, 30, 10);
            var e = NotHeldUp(b).Single();
            CollectionAssert.AreEquivalent(new[] { a.Id, c.Id }, e.Ids);
            StringAssert.Contains(e.Message, "mechanism");
            // a post under the joint: they stand
            b.AddColumn(b.Ground, l2, 15, 10);
            Assert.AreEqual(0, NotHeldUp(b).Length);
        }

        /// <summary>A beam whose end rests on another's cantilevered tip stands (the corner of an overhang).</summary>
        [TestMethod]
        public void OnACantileversTip_Stands()
        {
            var b = Story(out var l2);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 10, 0, 10, 20);
            b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddBeam(l2, 0, 10, 16, 10);           // on the walls at 0 and 10, 6 ft past
            b.AddBeam(l2, 16, 0, 16, 10);           // from the S wall onto that tip
            Assert.AreEqual(0, NotHeldUp(b).Length, string.Join(" | ", NotHeldUp(b).Select(i => i.Message)));
        }

        /// <summary>Four beams round an opening, each from a wall onto the next one partway along (a pinwheel): they stand.</summary>
        [TestMethod]
        public void Pinwheel_Stands()
        {
            var b = Story(out var l2);
            b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 30, 0, 30, 20);
            b.AddBeam(l2, 10, 0, 10, 12);   // S wall up onto the top beam
            b.AddBeam(l2, 0, 12, 20, 12);   // W wall across onto the right beam
            b.AddBeam(l2, 20, 20, 20, 8);   // N wall down onto the bottom beam
            b.AddBeam(l2, 30, 8, 10, 8);    // E wall across onto the left beam
            Assert.AreEqual(0, NotHeldUp(b).Length, string.Join(" | ", NotHeldUp(b).Select(i => i.Message)));
        }
    }
}
