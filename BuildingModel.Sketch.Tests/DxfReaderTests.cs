using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>DXF import: units, each entity type, blocks, mirrored entities, model space only, true-size placement.</summary>
    [TestClass]
    public class DxfReaderTests
    {
        private const double Tol = 1e-6;

        /// <summary>A DXF from group code / value pairs: "0 LINE 8 WALLS ..." written one item per line.</summary>
        private static DxfDrawing Read(int insUnits, string blocks, string entities)
        {
            var sb = new StringBuilder();
            void P(string s) { foreach (var t in s.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)) sb.Append(t.Trim()).Append("\r\n"); }
            P($"0|SECTION|2|HEADER|9|$ACADVER|1|AC1027|9|$INSUNITS|70|{insUnits}|0|ENDSEC");
            P("0|SECTION|2|BLOCKS|" + blocks + "|0|ENDSEC");
            P("0|SECTION|2|ENTITIES|" + entities + "|0|ENDSEC|0|EOF");
            return DxfReader.Read(new StringReader(sb.ToString()));
        }

        private static void Near(Point expected, Point actual, double tol = Tol) =>
            Assert.IsTrue((expected - actual).Length < tol, $"expected {expected}, got {actual}");

        [TestMethod]
        public void Units_FromTheHeader()
        {
            Assert.AreEqual(1.0 / 12, DxfReader.FeetPerUnit(1), Tol);   // inches
            Assert.AreEqual(1.0, DxfReader.FeetPerUnit(2), Tol);        // feet
            Assert.AreEqual(1 / 304.8, DxfReader.FeetPerUnit(4), Tol);  // mm
            Assert.AreEqual(3.280839895, DxfReader.FeetPerUnit(6), 1e-9); // m
            Assert.IsTrue(double.IsNaN(DxfReader.FeetPerUnit(0)));
            Assert.AreEqual(1, Read(1, "", "0|LINE|8|A|10|0|20|0|11|1|21|0").InsUnits);
        }

        [TestMethod]
        public void Line_LwPolyline_Arc_Circle()
        {
            var d = Read(1, "",
                "0|LINE|8|WALLS|10|0|20|0|11|480|21|0|" +
                "0|LWPOLYLINE|8|WALLS|90|4|70|1|10|0|20|0|10|100|20|0|10|100|20|50|10|0|20|50|" +
                "0|ARC|8|DOORS|10|10|20|10|40|5|50|0|51|90|" +
                "0|CIRCLE|8|COLS|10|0|20|0|40|2");
            Assert.AreEqual(4, d.Paths.Count);
            var line = d.Paths[0];
            Near(new Point(0, 0), line.Points[0]);
            Near(new Point(480, 0), line.Points[1]);
            Assert.IsTrue(d.Paths[1].Closed);
            Assert.AreEqual(4, d.Paths[1].Points.Count);
            var arc = d.Paths[2];
            Near(new Point(15, 10), arc.Points.First());
            Near(new Point(10, 15), arc.Points.Last());
            Assert.IsTrue(arc.Points.All(p => Math.Abs((p - new Point(10, 10)).Length - 5) < Tol), "every arc point on the radius");
            Assert.IsTrue(d.Paths[3].Closed);
            Assert.IsTrue(d.Paths[3].Points.All(p => Math.Abs(((Vector)p).Length - 2) < Tol));
            Assert.AreEqual("WALLS", d.Layers().First().layer);
            Assert.AreEqual(2, d.Layers().First().count);
        }

        [TestMethod]
        public void Bulge_SemicircleCounterClockwise()
        {
            // (0,0) to (10,0) with bulge 1: a half circle, centre (5,0), r 5, counter-clockwise = below the chord
            var d = Read(1, "", "0|LWPOLYLINE|8|A|90|2|70|0|10|0|20|0|42|1|10|10|20|0");
            var pts = d.Paths.Single().Points;
            Near(new Point(0, 0), pts.First());
            Near(new Point(10, 0), pts.Last());
            Assert.IsTrue(pts.All(p => Math.Abs((p - new Point(5, 0)).Length - 5) < Tol));
            Assert.IsTrue(pts.Skip(1).Take(pts.Count - 2).All(p => p.Y < 0), "CCW from (0,0) to (10,0) passes below");
            Assert.IsTrue(pts.Any(p => (p - new Point(5, -5)).Length < 0.5));
        }

        [TestMethod]
        public void OldStylePolyline_WithVertices()
        {
            var d = Read(1, "", "0|POLYLINE|8|A|66|1|70|1|0|VERTEX|8|A|10|0|20|0|0|VERTEX|8|A|10|5|20|0|0|VERTEX|8|A|10|5|20|5|0|SEQEND|0|LINE|8|B|10|0|20|0|11|1|21|1");
            Assert.AreEqual(2, d.Paths.Count);
            Assert.AreEqual(3, d.Paths[0].Points.Count);
            Assert.IsTrue(d.Paths[0].Closed);
            Assert.AreEqual("B", d.Paths[1].Layer);
        }

        [TestMethod]
        public void BlockInsert_ScaledRotatedMoved_Layer0TakesTheInsertLayer()
        {
            // block "B", base (1,0): a line on layer 0 from (1,0) to (3,0) and one on layer DOOR
            string blocks = "0|BLOCK|8|0|2|B|70|0|10|1|20|0|3|B|0|LINE|8|0|10|1|20|0|11|3|21|0|0|LINE|8|DOOR|10|1|20|0|11|1|21|1|0|ENDBLK|8|0";
            // insert at (100,50), x scale 2, rotated 90°: (3,0) - base = (2,0) → ×2 (4,0) → rot 90 (0,4) → (100,54)
            var d = Read(1, blocks, "0|INSERT|8|WALLS|2|B|10|100|20|50|41|2|42|2|50|90");
            Assert.AreEqual(2, d.Paths.Count);
            var first = d.Paths[0];
            Assert.AreEqual("WALLS", first.Layer);
            Near(new Point(100, 50), first.Points[0]);
            Near(new Point(100, 54), first.Points[1]);
            Assert.AreEqual("DOOR", d.Paths[1].Layer);
            Near(new Point(98, 50), d.Paths[1].Points[1]); // (0,1)×2 = (0,2) → rot 90 → (-2,0)
        }

        [TestMethod]
        public void MirroredExtrusion_FlipsX_ForOcsEntitiesOnly()
        {
            var d = Read(1, "", "0|ARC|8|A|10|10|20|0|40|1|50|0|51|90|210|0|220|0|230|-1|0|LINE|8|A|10|10|20|0|11|11|21|0|210|0|220|0|230|-1");
            Near(new Point(-11, 0), d.Paths[0].Points.First()); // OCS (11,0) → WCS (−11,0)
            Near(new Point(10, 0), d.Paths[1].Points[0]);       // a LINE is in world coordinates already
        }

        [TestMethod]
        public void PaperSpace_DimensionsAndHatches_Skipped_TextRead()
        {
            var d = Read(1, "", "0|LINE|8|A|67|1|10|0|20|0|11|1|21|0|0|TEXT|8|A|10|0|20|0|40|6|1|HELLO|0|DIMENSION|8|A|0|HATCH|8|A|0|LINE|8|A|10|0|20|0|11|2|21|0");
            Assert.AreEqual(1, d.Paths.Count);
            Assert.IsFalse(d.Skipped.ContainsKey("TEXT"));
            Assert.AreEqual(1, d.Skipped["DIMENSION"]);
            Assert.AreEqual(1, d.Skipped["HATCH"]);
            Assert.AreEqual("HELLO", d.Texts.Single().Text);
        }

        [TestMethod]
        public void Text_AlignedAndMText_PlacedTurnedAndPlain()
        {
            var d = Read(1, "",
                "0|TEXT|8|ROOMS|10|0|20|0|11|50|21|20|40|6|50|90|72|1|73|2|1|KITCHEN|" +
                "0|MTEXT|8|ROOMS|10|100|20|200|40|9|71|5|1|{\\fArial;BED}\\PROOM 2|");
            Assert.AreEqual(2, d.Texts.Count);
            var t = d.Texts[0];
            Near(new Point(50, 20), t.At);          // by its alignment point (72/73 not zero)
            Assert.AreEqual(90, t.Angle, Tol);
            Assert.AreEqual(6, t.Height, Tol);
            Assert.AreEqual(1, t.Across);
            Assert.AreEqual(2, t.Up);
            var m = d.Texts[1];
            Near(new Point(100, 200), m.At);
            Assert.AreEqual(9, m.Height, Tol);
            Assert.AreEqual("BED\nROOM 2", m.Text);
            Assert.AreEqual(1, m.Across);           // attach 5: middle center
            Assert.AreEqual(2, m.Up);
            Assert.AreEqual("1/2\" ⌀ 45°", DxfReader.PlainMText("\\S1/2;\" %%c 45%%d"));
        }

        [TestMethod]
        public void NotAsciiDxf_Rejected()
        {
            Assert.Throws<InvalidDataException>(() => DxfReader.Read(new StringReader("hello\r\nworld\r\n")));
            string dir = Path.Combine(Path.GetTempPath(), "swc-dxf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string dwg = Path.Combine(dir, "plan.dxf");
                File.WriteAllBytes(dwg, Encoding.ASCII.GetBytes("AC1032\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"));
                var ex = Assert.Throws<InvalidDataException>(() => DxfReader.ReadFile(dwg));
                StringAssert.Contains(ex.Message, "DWG");
            }
            finally { Directory.Delete(dir, true); }
        }

        [TestMethod]
        public void Placed_AtTrueSize_LowerLeftAtOrigin_Snaps()
        {
            // a 40' × 30' outline drawn in inches, starting at (1000, 2000)
            var d = Read(1, "", "0|LWPOLYLINE|8|WALLS|90|4|70|1|10|1000|20|2000|10|1480|20|2000|10|1480|20|2360|10|1000|20|2360|0|LINE|8|FURN|10|1100|20|2100|11|1110|21|2100");
            var img = PlanImage.FromDxf("plan.dxf", d, new[] { "WALLS" }, DxfReader.FeetPerUnit(1), "DXF, inches");
            Assert.IsTrue(img.Calibrated);
            var w = img.WorldBounds();
            Assert.AreEqual(0, w.Left, 0.01);
            Assert.AreEqual(0, w.Top, 0.01); // Rect.Top is the lowest y
            Assert.AreEqual(40, w.Width, 0.01);
            Assert.AreEqual(30, w.Height, 0.01);
            Assert.AreEqual(0, img.SegmentsNear(new Point2(8.4, 8.4), 0.5).Count, "the FURN layer was not ticked");

            // object snaps: the corner (40, 30) is an endpoint near the cursor
            var segs = img.SegmentsNear(new Point2(39.9, 29.9), 0.5);
            Assert.IsTrue(segs.Count >= 2);
            var snap = Snapper.Find(new Point2(39.9, 29.9), 0.5, segs, SnapModes.Endpoint);
            Assert.AreEqual(40, snap.Point.X, 1e-3);
            Assert.AreEqual(30, snap.Point.Y, 1e-3);
            Assert.AreEqual(0, img.SegmentsNear(new Point2(20, 15), 0.5).Count, "nothing near the middle of the room");

            // moved: the linework goes with it
            img.Move(new Point(0, 0), new Point(5, 5));
            var moved = Snapper.Find(new Point2(44.9, 34.9), 0.5, img.SegmentsNear(new Point2(44.9, 34.9), 0.5), SnapModes.Endpoint);
            Assert.AreEqual(45, moved.Point.X, 1e-3);
            Assert.AreEqual(35, moved.Point.Y, 1e-3);
        }

        [TestMethod]
        public void NoLineworkOnTheChosenLayers_Refused()
        {
            var d = Read(2, "", "0|LINE|8|A|10|0|20|0|11|1|21|0");
            Assert.Throws<ArgumentException>(() => PlanImage.FromDxf("x.dxf", d, new string[0], 1, "DXF"));
        }

        /// <summary>samplesDefault_60x40_plan.dxf: in feet, the default wind building's 60 x 40 ft outline, imports at true size.</summary>
        [TestMethod]
        public void SamplePlan_IsTheDefaultWindBuilding()
        {
            string path = Path.Combine(Path.GetDirectoryName(SourceFile()), "..", "..", "ShearWallCalculator", "samples", "Default_60x40_plan.dxf");
            var d = DxfReader.ReadFile(path);
            Assert.AreEqual(2, d.InsUnits, "feet");
            var b = d.Bounds();
            Assert.AreEqual(60, b.Width, 1e-9);
            Assert.AreEqual(40, b.Height, 1e-9);
            CollectionAssert.AreEquivalent(new[] { "EXT_WALLS", "INT_WALLS" }, d.Layers().Select(l => l.layer).ToArray());
            var img = PlanImage.FromDxf("Default_60x40_plan.dxf", d, new[] { "EXT_WALLS", "INT_WALLS" }, DxfReader.FeetPerUnit(2), "DXF, feet");
            Assert.AreEqual(60, img.WorldBounds().Width, 0.01);
        }

        private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    }
}
