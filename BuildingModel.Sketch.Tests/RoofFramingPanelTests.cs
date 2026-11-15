using System;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Sketch;
using BuildingModel.Statics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>
    /// The roof framing window (owner 2026-11-12): sections, dormers and purlins entered as a person types them, the plan
    /// drawn from what they frame to, a roof that cannot be framed an error (OK off), and the building untouched.
    /// </summary>
    [TestClass]
    public class RoofFramingPanelTests
    {
        private static Building Box()
        {
            var b = Building.New("Framing");
            var l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 40, 20));
            b.AddWall(b.Ground, l2, 0, 0, 40, 0);
            b.AddWall(b.Ground, l2, 0, 20, 40, 20);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 40, 0, 40, 20);
            b.Roof.Shape = RoofShape.Gable;
            b.Roof.Ridge = RidgeAxis.WestEast;
            b.Roof.Pitch = Math.Atan(0.5) * 180 / Math.PI;
            return b;
        }

        /// <summary>
        /// Opened on a plain gable: its one section shown ("main"), nothing changed until edited (no sections written). A wing
        /// added and typed (x 10 to 26, y -16 to 10, S-N): two sections, valleys drawn; the building's own roof untouched.
        /// </summary>
        [STATestMethod]
        public void AWing_Typed_TwoSections_ValleysDrawn()
        {
            var b = Box();
            var p = new RoofFramingPanel(b);
            Assert.IsNull(p.Roof.Sections, "nothing edited: the roof keeps its own shape");
            Assert.AreEqual(2, p.Faces);
            p.ClickAddSection();
            p.Type("x0", "10"); p.Type("y0", "-16"); p.Type("x1", "26"); p.Type("y1", "10");
            p.SetRidge(RidgeAxis.SouthNorth);
            Assert.AreEqual(2, p.Roof.Sections.Count);
            Assert.AreEqual(new Rect2(10, -16, 26, 10).ToString(), p.Roof.Sections[1].Walls.ToString());
            Assert.IsFalse(p.HasErrors);
            Assert.AreEqual(4, p.Faces);
            var trial = Serialization.ModelFile.Clone(b);
            trial.Roof = p.Roof;
            Assert.AreEqual(2, RoofFrame.Of(trial).Creases.Count(c => c.Kind == CreaseKind.Valley));
            Assert.IsNull(b.Roof.Sections, "the building is not changed here");
        }

        /// <summary>A dormer added: its header and trimmers framed; a purlin added and its hip size picked carried in the roof.</summary>
        [STATestMethod]
        public void ADormer_AndAPurlin_AndASize()
        {
            var b = Box();
            var p = new RoofFramingPanel(b);
            p.ClickAddDormer();
            Assert.AreEqual(2, p.Roof.Sections.Count);
            Assert.IsTrue(p.Roof.Sections[1].EaveRise > 0);
            Assert.IsFalse(p.HasErrors);
            p.ClickAddPurlin();
            p.Type("p.y0", "5"); p.Type("p.y1", "5");
            Assert.AreEqual(1, p.Roof.Purlins.Count);
            Assert.AreEqual(5, p.Roof.Purlins[0].Start.Y, 1e-9);
            p.SetHipSize("2-2x10");
            Assert.AreEqual("2-2x10", p.Roof.HipSize);
        }

        /// <summary>
        /// Not sized (UI audit D1): a red ⊘ NOT DESIGNED chip by the box, the valleys drawn red dashed, and Use with the
        /// host's lightest size, which fills the box in one click; sized, the chip goes and the valleys are drawn as valleys.
        /// </summary>
        [STATestMethod]
        public void NotSized_ChipAndUse_OneClickFills()
        {
            var was = RoofFramingPanel.LightestSizes;
            try
            {
                RoofFramingPanel.LightestSizes = b => new System.Collections.Generic.Dictionary<string, string> { ["valley"] = "2x10" };
                var p = new RoofFramingPanel(Box());
                p.ClickAddSection();
                p.Type("x0", "10"); p.Type("y0", "-16"); p.Type("x1", "26"); p.Type("y1", "10");
                p.SetRidge(RidgeAxis.SouthNorth);
                Assert.AreEqual(("⊘ NOT DESIGNED", "2x10"), p.ChipOf("valley"));
                Assert.AreEqual(("⊘ NOT DESIGNED", (string)null), p.ChipOf("hip"), "no size from the host: the chip only");
                Assert.AreEqual(2, p.NotSizedDrawn, "both valleys red dashed");
                p.ClickUse("valley");
                Assert.AreEqual("2x10", p.Roof.ValleySize);
                Assert.AreEqual(((string)null, (string)null), p.ChipOf("valley"));
                Assert.AreEqual(0, p.NotSizedDrawn);
            }
            finally { RoofFramingPanel.LightestSizes = was; }
        }

        /// <summary>A section with no area cannot be framed: an error, OK off.</summary>
        [STATestMethod]
        public void ASectionWithNoArea_IsAnError()
        {
            var p = new RoofFramingPanel(Box());
            p.ClickAddSection();
            p.Type("x0", "5"); p.Type("x1", "5");
            Assert.IsTrue(p.HasErrors);
        }
    }
}
