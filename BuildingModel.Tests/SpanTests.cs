using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>Which way a diaphragm's joists or rafters span: its framing areas (format version 8; version 6's span read once into them).</summary>
    [TestClass]
    public class SpanTests
    {
        [TestMethod]
        public void NewDiaphragm_OneArea_TheShorterWay()
        {
            var b = Building.New("S");
            var l2 = b.AddLevel("Level 2", 10);
            var wide = b.AddDiaphragm(l2, new Rect2(0, 0, 40, 24));          // 40 W-E by 24 S-N: joists span S-N
            var tall = b.AddDiaphragm(l2, new Rect2(0, 30, 20, 80));         // 20 by 50: joists span W-E
            Assert.AreEqual(SpanDirection.Auto, wide.Span, "no span of its own");
            Assert.AreEqual(SpanDirection.SouthNorth, wide.Framing.Single().Span);
            Assert.AreEqual(SpanDirection.WestEast, tall.Framing.Single().Span);
            Assert.AreEqual(new Rect2(0, 0, 40, 24), wide.Framing.Single().Rect);
        }

        [TestMethod]
        public void SetDiaphragmSpan_TurnsAllOfIt_RoundTrips()
        {
            var b = Building.New("S");
            var d = b.AddDiaphragm(b.AddLevel("Level 2", 10), new Rect2(0, 0, 40, 24));
            Assert.IsTrue(BuildingEdits.SetDiaphragmSpan(b, d.Id, SpanDirection.WestEast));
            Assert.IsFalse(BuildingEdits.SetDiaphragmSpan(b, d.Id, SpanDirection.WestEast));
            Assert.IsFalse(BuildingEdits.SetDiaphragmSpan(b, "nope", SpanDirection.WestEast));
            Assert.AreEqual(SpanDirection.WestEast, d.Framing.Single().Span);
            var back = ModelFile.FromJson(ModelFile.ToJson(new ModelDocument { Building = b })).Building;
            Assert.AreEqual(SpanDirection.WestEast, back.Diaphragms.Single().Framing.Single().Span);
            Assert.IsFalse(ModelFile.ToJson(new ModelDocument { Building = b }).Contains("\"Span\":0"), "the diaphragm's own span is not written");
        }

        /// <summary>A version 7 file: the diaphragm's span S-N becomes one framing area S-N when it is opened, and the old span goes.</summary>
        [TestMethod]
        public void OlderFile_ItsSpanBecomesItsFramingArea()
        {
            var b = Building.New("S");
            var d = b.AddDiaphragm(b.AddLevel("Level 2", 10), new Rect2(0, 0, 20, 50)); // W-E the short way
            d.Framing.Clear();
            d.Span = SpanDirection.SouthNorth;
            var doc = new ModelDocument { Building = b, Version = 7 };
            var back = ModelFile.FromJson(ModelFile.ToJson(doc)).Building.Diaphragms.Single();
            Assert.AreEqual(SpanDirection.SouthNorth, back.Framing.Single().Span);
            Assert.AreEqual(SpanDirection.Auto, back.Span);
            Assert.IsFalse(FramingAreas.Tidy(ModelFile.FromJson(ModelFile.ToJson(doc)).Building), "read once: tidy after that changes nothing");
        }
    }
}
