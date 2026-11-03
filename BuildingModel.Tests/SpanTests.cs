using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>Format version 6: which way a diaphragm's joists or rafters span.</summary>
    [TestClass]
    public class SpanTests
    {
        [TestMethod]
        public void Auto_SpansTheShorterWay()
        {
            var b = Building.New("S");
            var l2 = b.AddLevel("Level 2", 10);
            var wide = b.AddDiaphragm(l2, new Rect2(0, 0, 40, 24));          // 40 W-E by 24 S-N: joists span S-N
            var tall = b.AddDiaphragm(l2, new Rect2(0, 30, 20, 80));         // 20 by 50: joists span W-E
            Assert.AreEqual(SpanDirection.Auto, wide.Span);
            Assert.AreEqual(SpanDirection.SouthNorth, wide.EffectiveSpan);
            Assert.AreEqual(SpanDirection.WestEast, tall.EffectiveSpan);
        }

        [TestMethod]
        public void SetDiaphragmSpan_SetsRoundTripsAndOlderFilesAreAuto()
        {
            var b = Building.New("S");
            var d = b.AddDiaphragm(b.AddLevel("Level 2", 10), new Rect2(0, 0, 40, 24));
            Assert.IsTrue(BuildingEdits.SetDiaphragmSpan(b, d.Id, SpanDirection.WestEast));
            Assert.IsFalse(BuildingEdits.SetDiaphragmSpan(b, d.Id, SpanDirection.WestEast));
            Assert.IsFalse(BuildingEdits.SetDiaphragmSpan(b, "nope", SpanDirection.WestEast));
            Assert.AreEqual(SpanDirection.WestEast, d.EffectiveSpan);
            var back = ModelFile.FromJson(ModelFile.ToJson(new ModelDocument { Building = b })).Building;
            Assert.AreEqual(SpanDirection.WestEast, back.Diaphragms.Single().Span);
            Assert.IsTrue(BuildingEdits.SetDiaphragmSpan(b, d.Id, SpanDirection.Auto));
            Assert.IsFalse(ModelFile.ToJson(new ModelDocument { Building = b }).Contains("\"Span\""));   // Auto is not written
        }
    }
}
