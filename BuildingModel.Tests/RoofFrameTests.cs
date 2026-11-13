using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Statics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// A sloped roof laid out from its sections (owner 2026-11-12: hips, valleys, wings that stick out, dormers): the faces,
    /// ridges, hips and valleys by hand, and the rafters' strips tiling every face exactly.
    /// </summary>
    [TestClass]
    public class RoofFrameTests
    {
        private const double T = 1e-6;
        private static readonly double Pitch612 = Math.Atan(0.5) * 180 / Math.PI;

        private static RoofFrame Frame(double overhang, params RoofSection[] s) => RoofFrame.Of(null, s.ToList(), Pitch612, overhang, 2);

        // ------------------------------------------------------------------ two roofs merged (owner 2026-11-13)

        private static RoofFrame LHouse(double wingPlate, bool dormer = false)
        {
            var list = new List<RoofSection>
            {
                new RoofSection { Name = "main", Walls = new Rect2(0, 0, 56, 28), Shape = RoofShape.Hip, Ridge = RidgeAxis.WestEast },
                new RoofSection { Name = "wing", Walls = new Rect2(32, 28, 56, 52), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth, EaveRise = wingPlate },
            };
            if (dormer) list.Add(new RoofSection { Name = "dormer", Walls = new Rect2(12, 1, 20, 8), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth, EaveRise = 3 });
            return RoofFrame.Of(null, list, Pitch612, 1.5, 2);
        }

        /// <summary>The roof's top over a plan point (the face over it), NaN where none is.</summary>
        private static double Top(RoofFrame f, double x, double y)
        {
            double top = double.NaN;
            foreach (var face in f.Faces)
                foreach (var p in face.Pieces)
                    if (p.Edges().All(e => e.a * x + e.b * y + e.c >= -1e-7)) { double z = face.Plane.Z(x, y); if (double.IsNaN(top) || z > top) top = z; }
            return top;
        }

        /// <summary>No step along a line on plan: the roof is one surface across it (no gable wall, no fascia standing on a roof).</summary>
        private static void Smooth(RoofFrame f, double x0, double y0, double x1, double y1, string what)
        {
            int n = 4000;
            double prev = double.NaN;
            for (int i = 0; i <= n; i++)
            {
                double x = x0 + (x1 - x0) * i / n, y = y0 + (y1 - y0) * i / n, z = Top(f, x, y);
                Assert.IsFalse(double.IsNaN(z), $"{what}: no roof over ({x:0.###}, {y:0.###})");
                if (!double.IsNaN(prev)) Assert.IsTrue(Math.Abs(z - prev) < 0.02, $"{what}: a step of {z - prev:0.###} ft at ({x:0.###}, {y:0.###})");
                prev = z;
            }
        }

        /// <summary>
        /// Every rafter end bears on something (owner 2026-11-13: "do they run continuous to the hip beam? or were you cutting
        /// them off"): an end not on a ridge, hip, valley or header is at the roof's edge, nothing of its own plane past it (a
        /// lower roof under an eave is fine; the seam between two faces in one plane is not an end).
        /// </summary>
        private static void Framed(RoofFrame f)
        {
            foreach (var r in f.Rafters)
                foreach (bool atEnd in new[] { false, true })
                {
                    if ((atEnd ? r.AtEnd : r.AtStart) != RafterEnd.Free) continue;
                    var p = atEnd ? r.End : r.Start;
                    double d = atEnd ? 0.05 : -0.05;
                    var past = r.AlongX ? new Point2(p.X + d, p.Y) : new Point2(p.X, p.Y + d);
                    double z = r.Face.Plane.Z(past);
                    bool more = f.Faces.Any(fc => fc.Pieces.Any(pc => pc.Edges().All(e => e.a * past.X + e.b * past.Y + e.c >= -1e-7)) && fc.Plane.Z(past) > z - 1e-6);
                    Assert.IsFalse(more, $"{r.Id} ({r.Face.Name}, line {r.Line}) stops at ({p.X}, {p.Y}) with its roof going on past it");
                }
        }

        /// <summary>
        /// The L-shaped house's roof as the owner asked (2026-11-13): the main block hipped 56 × 28, the wing's gable 24 × 24
        /// running north from its east end, 6:12, 1.5 ft eaves. The wing's ridge (6 ft up) runs back south over the main roof
        /// until the main roof's north slope rises to it, (28 − y) / 2 = 6 at y = 16: no gable wall at the junction. Its west
        /// slope meets the main's north slope in a valley, x − 32 = 28 − y, from the eaves' inside corner (30.5, 29.5) up to the
        /// ridge's end (44, 16); its east slope is the main roof's east hip face run on (the same plane, x = 56 flush). The roof
        /// covers the two eave rectangles exactly once (59 × 31 + 27 × 24 = 2477 sq ft), and is one surface across the junction.
        /// </summary>
        [TestMethod]
        public void LShapedHouse_TheWingsGableRunsIntoTheHip_TwoRoofsMerged()
        {
            var f = LHouse(0);
            Assert.IsNull(f.NotLaidOut);
            CollectionAssert.Contains(f.OpenEnds[1].ToList(), "south", "the wing's south end runs on into the main roof");
            Assert.AreEqual(0, f.OpenEnds[0].Count);
            Assert.AreEqual(16, f.Reach[1].Y0, 1e-6, "to where its ridge meets the main roof, no further");
            var ridge = f.Creases.Single(c => c.Kind == CreaseKind.Ridge && c.F1.Plane.Section == 1);
            Assert.AreEqual(44, ridge.A.X, 1e-6);
            Assert.AreEqual(16, Math.Min(ridge.A.Y, ridge.B.Y), 1e-6);
            Assert.AreEqual(6, ridge.ZA, 1e-6);
            var valley = f.Creases.Single(c => c.Kind == CreaseKind.Valley && Math.Abs(c.A.X - 30.5) < 1e-3);
            Assert.AreEqual(29.5, valley.A.Y, 1e-6);
            Assert.AreEqual(44, valley.B.X, 1e-6);
            Assert.AreEqual(16, valley.B.Y, 1e-6);
            Assert.AreEqual(59 * 31 + 27 * 24, f.Faces.Sum(x => x.Area), 1e-6, "every point of the roof once");
            Smooth(f, 40, 5, 40, 50, "x = 40, south to north");
            Smooth(f, 20, 27, 56, 27, "y = 27, along the junction");
            Smooth(f, 31, 20, 31, 29.4, "x = 31, the inside corner");
            Framed(f);
        }

        /// <summary>
        /// The L's rafters where the two roofs meet (owner 2026-11-13: "what did you do with the rafters from the right hip when
        /// they intersected with the gable overframe?"). The wing's east slope is the hip's east face run on, one plane: each
        /// line's rafter runs in one piece from the wing's ridge (x = 44) to the eave (57.5), none stopping on the old hip line
        /// (x − 28 = y) with nothing under it, and pairs at the ridge with the wing's west rafter. West of the ridge, over the
        /// main roof (y 16 .. 28), the wing's slope has valley jacks from the valley up to the ridge at the spacing. The hip's
        /// last jack is at y = 14; at y = 16 the hip and the wing's ridge meet.
        /// </summary>
        [TestMethod]
        public void LShapedHouse_TheHipFacesRaftersRunToTheWingsRidge()
        {
            var f = LHouse(0);
            for (double y = 18; y <= 52; y += 2)
            {
                var east = f.Rafters.Single(r => r.AlongX && Math.Abs(r.Line - y) < 1e-6 && r.Start.X > 43.9);
                Assert.AreEqual(44, east.Start.X, 1e-6, $"y = {y}: from the wing's ridge");
                Assert.AreEqual(57.5, east.End.X, 1e-6, $"y = {y}: to the eave");
                Assert.AreEqual(RafterEnd.Ridge, east.AtStart);
                Assert.AreEqual(1, east.Face.Plane.Section, "the wing's: paired at its ridge");
                var west = f.Rafters.Single(r => r.AlongX && Math.Abs(r.Line - y) < 1e-6 && r.End.X < 44.1 && r.End.X > 43.9);
                Assert.AreEqual(RafterEnd.Ridge, west.AtEnd);
                if (y < 29) Assert.AreEqual(RafterEnd.Valley, west.AtStart, $"y = {y}: a valley jack");
                Assert.AreEqual(y < 29 ? 32 + (28 - y) : 30.5, west.Start.X, 1e-6);
            }
            var st = RoofStatics.Of(f, new List<Wall>(), new List<Column>(), new List<Beam>());
            Assert.AreEqual(18, st.Members.Count(m => m.Kind == "rafter pair" && m.Rafters.All(r => r.Face.Plane.Section == 1)), "y = 18 .. 52, each pair across the wing's ridge");
        }

        /// <summary>
        /// The wing's plate 1 ft lower (owner 2026-11-13: the plate heights may differ): its ridge 5 ft up meets the main roof
        /// at (28 − y) / 2 = 5, y = 18. The main's north slope runs on past its eave (y = 29.5) over the wing until it is under
        /// the wing's eave edge, (1 + 0.75) / 0.5 = 3.5 ft past its wall: no eave and fascia of the hip across the gable, the
        /// valley x − 32 − 2 = 28 − y down to the wing's eave. One surface, every point once.
        /// </summary>
        [TestMethod]
        public void LShapedHouse_TheWingsPlateLower_TheHipsEavesStop()
        {
            var f = LHouse(-1);
            Assert.IsNull(f.NotLaidOut);
            Assert.AreEqual(18, f.Reach[1].Y0, 1e-6);
            Assert.IsTrue(f.Faces.Where(x => x.Plane.Section == 0).SelectMany(x => x.Pieces).SelectMany(p => p.P).Max(p => p.Y) > 29.5 + 0.1,
                          "the main roof's north slope runs on past its own eave, over the wing");
            // both slopes now meet the main roof in valleys up to the ridge's end (its east slope is no longer the hip's face)
            var valleys = f.Creases.Where(c => c.Kind == CreaseKind.Valley && Math.Abs(c.B.X - 44) < 1e-3 && Math.Abs(c.B.Y - 18) < 1e-3).ToList();
            Assert.AreEqual(2, valleys.Count);
            Assert.IsTrue(valleys.Any(c => c.A.X < 44) && valleys.Any(c => c.A.X > 44));
            Assert.AreEqual(59 * 31 + 27 * 24, f.Faces.Sum(x => x.Area), 1e-6);
            Smooth(f, 40, 5, 40, 50, "x = 40");
            Smooth(f, 20, 27, 56, 27, "y = 27");
            Smooth(f, 33, 28, 33, 33, "x = 33, across the hip's eave line");
            Framed(f);
            // the wing's slopes run on over the main roof down to its ridge's end: valley jacks there, y = 20 .. 30
            for (double y = 20; y <= 30; y += 2)
                Assert.AreEqual(2, f.Rafters.Count(r => r.AlongX && Math.Abs(r.Line - y) < 1e-6 && r.Face.Plane.Section == 1 &&
                                                        (r.AtStart == RafterEnd.Valley || r.AtEnd == RafterEnd.Valley)), $"y = {y}");
        }

        /// <summary>
        /// A wing (24 .. 48, under the main ridge) with its plate 0.5 ft higher: its ridge 6.5 ft up meets the main roof's north
        /// slope at (28 − y) / 2 = 6.5, y = 15. Its slopes run on past its eaves (x = 22.5, 49.5) over the main roof until under
        /// the main's eave edge, (0.5 + 0.75) / 0.5 = 2.5 ft past its walls: the valleys x − 24 + 1 = 28 − y and its mirror land
        /// on the main's eave at (21.5, 29.5) and (50.5, 29.5), no fascia of the wing standing on the hip. All within the two
        /// eave rectangles: every point once (59 × 31 + 27 × 24).
        /// </summary>
        [TestMethod]
        public void LShapedHouse_TheWingsPlateHigher_ItsSlopesComeDownToTheHip()
        {
            var f = RoofFrame.Of(null, new List<RoofSection>
            {
                new RoofSection { Name = "main", Walls = new Rect2(0, 0, 56, 28), Shape = RoofShape.Hip, Ridge = RidgeAxis.WestEast },
                new RoofSection { Name = "wing", Walls = new Rect2(24, 28, 48, 52), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth, EaveRise = 0.5 },
            }, Pitch612, 1.5, 2);
            Assert.IsNull(f.NotLaidOut);
            Assert.AreEqual(15, f.Reach[1].Y0, 1e-6);
            Assert.IsTrue(f.Faces.Where(x => x.Plane.Section == 1).SelectMany(x => x.Pieces).SelectMany(p => p.P).Min(p => p.X) < 22.5 - 0.1,
                          "the wing's west slope runs on past its own eave, over the main roof");
            Assert.IsTrue(f.Creases.Any(c => c.Kind == CreaseKind.Valley && Math.Abs(c.A.X - 21.5) < 1e-3 && Math.Abs(c.A.Y - 29.5) < 1e-3), "the west valley lands on the main's eave");
            Assert.IsTrue(f.Creases.Any(c => c.Kind == CreaseKind.Valley && Math.Abs(c.A.X - 50.5) < 1e-3 && Math.Abs(c.A.Y - 29.5) < 1e-3), "the east valley too");
            Assert.AreEqual(59 * 31 + 27 * 24, f.Faces.Sum(x => x.Area), 1e-6);
            Smooth(f, 36, 5, 36, 50, "x = 36, the wing's ridge line");
            Smooth(f, 10, 27, 56, 27, "y = 27");
            Smooth(f, 22, 20, 22, 29.4, "x = 22, under the wing's slope run on");
            Smooth(f, 15, 29, 30, 29, "y = 29, across the inside corner");
            Framed(f);
        }

        /// <summary>
        /// A dormer (8 wide, its eave 3 ft up) on the south slope: its ridge, 3 + 2 = 5 ft up, runs back north until the main roof
        /// rises to it, y / 2 = 5 at y = 10 (no back wall); its front stays a gable (the main roof falls away south of it) and its
        /// eaves stand over the main roof (its walls stand on that roof).
        /// </summary>
        [TestMethod]
        public void Dormer_ItsRidgeRunsBackIntoTheRoof_ItsFrontAGable()
        {
            var f = LHouse(0, dormer: true);
            Assert.IsNull(f.NotLaidOut);
            CollectionAssert.Contains(f.OpenEnds[2].ToList(), "north");
            Assert.IsFalse(f.OpenEnds[2].Contains("south"));
            Assert.AreEqual(10, f.Reach[2].Y1, 1e-6);
            Smooth(f, 16, 3, 16, 20, "the dormer's ridge line, back into the main roof");
            Framed(f);
        }

        /// <summary>
        /// An L (owner 2026-11-13, ASCE 7 26.2): a hip 56 × 28 at 6:12 rises 14 × 0.5 = 7 ft; the wing's gable 24 wide, 6 ft; a
        /// dormer's gable 8 wide with its eave 3 ft up, 3 + 2 = 5 ft. The highest point is the hip's ridge: rise 7, ridge 16, h
        /// 12.5, not a hip over the 56 × 52 rectangle around it (13, 22, 15.5).
        /// </summary>
        [TestMethod]
        public void LShapedHouse_TheMeanRoofHeight_ByTheHighestPoint()
        {
            var b = Building.New();
            var top = b.AddLevel("Level 2", 9);
            b.AddDiaphragm(top, new Rect2(0, 0, 56, 28));
            b.AddDiaphragm(top, new Rect2(32, 28, 56, 52));
            b.Roof = new Roof
            {
                Shape = RoofShape.Hip, Pitch = Pitch612, Ridge = RidgeAxis.WestEast, Overhang = 1.5,
                Sections = new List<RoofSection>
                {
                    new RoofSection { Name = "main", Walls = new Rect2(0, 0, 56, 28), Shape = RoofShape.Hip, Ridge = RidgeAxis.WestEast },
                    new RoofSection { Name = "wing", Walls = new Rect2(32, 28, 56, 52), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth },
                    new RoofSection { Name = "dormer", Walls = new Rect2(12, 1, 20, 8), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth, EaveRise = 3 },
                },
            };
            Assert.AreEqual(7, b.RoofRise(), 1e-9);
            Assert.AreEqual(16, b.RidgeHeight(), 1e-9);
            Assert.AreEqual(12.5, b.MeanRoofHeight(), 1e-9);
            StringAssert.Contains(b.DescribeRoof(), "3 sections (main hip W-E; wing gable S-N; dormer gable S-N, eave +3 ft), its highest point 7 ft above the eave");
            b.Roof.Sections = null;
            Assert.AreEqual(13, b.RoofRise(), 1e-9, "the plain hip over the rectangle around the plan");
        }

        /// <summary>
        /// A hip roof 30 × 20 at 6:12, no eaves: the ends' triangles 20 × 10 / 2 = 100 sq ft, the sides' trapezoids
        /// (30 + 10) / 2 × 10 = 200; the ridge (10, 10) to (20, 10) at 5 ft; four hips, each 10√2 on plan from a corner up to
        /// the ridge's end. A common on the south face at x = 14 runs to the ridge; at x = 4, a hip jack 4 ft long.
        /// </summary>
        [TestMethod]
        public void Hip_TheFacesCreasesAndRafters_ByHand()
        {
            var f = Frame(0, new RoofSection { Name = "main", Walls = new Rect2(0, 0, 30, 20), Shape = RoofShape.Hip });
            Assert.IsNull(f.NotLaidOut);
            Assert.AreEqual(4, f.Faces.Count);
            Assert.AreEqual(200, f.Faces.Single(x => x.Plane.Side == "south").Area, T);
            Assert.AreEqual(100, f.Faces.Single(x => x.Plane.Side == "west").Area, T);
            Assert.AreEqual(600, f.Faces.Sum(x => x.Area), T);
            var ridge = f.Creases.Single(c => c.Kind == CreaseKind.Ridge);
            Assert.AreEqual(10, Math.Min(ridge.A.X, ridge.B.X), 1e-6);
            Assert.AreEqual(20, Math.Max(ridge.A.X, ridge.B.X), 1e-6);
            Assert.AreEqual(5, ridge.ZA, 1e-9);
            var hips = f.Creases.Where(c => c.Kind == CreaseKind.Hip).ToList();
            Assert.AreEqual(4, hips.Count);
            foreach (var h in hips) { Assert.AreEqual(10 * Math.Sqrt(2), h.Length, 1e-6); Assert.AreEqual(0, h.ZA, 1e-6); Assert.AreEqual(5, h.ZB, 1e-6); }
            Assert.AreEqual(4, f.Lines.Count);
            Assert.IsTrue(f.Lines.All(l => l.TopRidge == ridge));
            Assert.AreEqual(Math.Atan(5 / (10 * Math.Sqrt(2))) * 180 / Math.PI, f.Lines[0].SlopeDeg, 1e-9);

            // the strips tile the roof
            Assert.AreEqual(600, f.Rafters.Sum(r => r.Area), 1e-6);
            var common = f.Rafters.Single(r => r.Face.Plane.Side == "south" && Math.Abs(r.Line - 14) < T);
            Assert.AreEqual(0, common.Start.Y, T);
            Assert.AreEqual(10, common.End.Y, T);
            Assert.AreEqual(RafterEnd.Ridge, common.AtEnd);
            Assert.AreEqual(RafterEnd.Free, common.AtStart);
            Assert.AreEqual(20, common.Area, 1e-9, "2 ft wide, 10 ft on plan");
            Assert.AreEqual("common", common.Kind);
            Assert.IsTrue(f.Rafters.Any(r => r.Face.Plane.Side == "north" && Math.Abs(r.Line - 14) < T && r.AtStart == RafterEnd.Ridge));
            // a hip jack: x = 4 up to the hip y = x; its strip 3 to 5 is ∫ x dx = 8 sq ft, 0.5 of it past its end (taken there)
            var jack = f.Rafters.Single(r => r.Face.Plane.Side == "south" && Math.Abs(r.Line - 4) < T);
            Assert.AreEqual(4, jack.Length, 1e-6);
            Assert.AreEqual(RafterEnd.Hip, jack.AtEnd);
            Assert.AreEqual("hip jack", jack.Kind);
            Assert.AreEqual(8, jack.Area, 1e-6);
            Assert.AreEqual(0.5, jack.AfterArea, 1e-6);
        }

        /// <summary>The same hip with 2 ft eaves: the roof 34 × 24 on plan, the commons' tails 2 ft past the walls.</summary>
        [TestMethod]
        public void Hip_WithEaves_TheTailsAndTheWholeRoof()
        {
            var f = Frame(2, new RoofSection { Walls = new Rect2(0, 0, 30, 20), Shape = RoofShape.Hip });
            Assert.AreEqual(34 * 24, f.Faces.Sum(x => x.Area), 1e-6);
            Assert.AreEqual(34 * 24, f.Rafters.Sum(r => r.Area), 1e-6);
            var common = f.Rafters.Single(r => r.Face.Plane.Side == "south" && Math.Abs(r.Line - 14) < T);
            Assert.AreEqual(-2, common.Start.Y, T);
            Assert.AreEqual(10, common.End.Y, T);
        }

        /// <summary>
        /// An L: the main gable 40 × 20 W-E, a wing 16 wide S-N from y = -16 up into it to y = 10, both 6:12, no eaves. The wing's
        /// ridge x = 18 at 4 ft dies into the main roof's south face at (18, 8); two valleys from the inside corners (10, 0) and
        /// (26, 0) up to it, 8√2 on plan. The main south face 400 - the wing's triangle 64 = 336; each wing face 8 × 16 + 32 =
        /// 160. Each valley's top bears on the main roof's south face.
        /// </summary>
        [TestMethod]
        public void Wing_TheValleys_ByHand()
        {
            var f = Frame(0,
                new RoofSection { Name = "main", Walls = new Rect2(0, 0, 40, 20), Shape = RoofShape.Gable, Ridge = RidgeAxis.WestEast },
                new RoofSection { Name = "wing", Walls = new Rect2(10, -16, 26, 10), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth });
            Assert.IsNull(f.NotLaidOut);
            Assert.AreEqual(336, f.Faces.Single(x => x.Name == "main, south face").Area, 1e-6);
            Assert.AreEqual(400, f.Faces.Single(x => x.Name == "main, north face").Area, 1e-6);
            Assert.AreEqual(160, f.Faces.Single(x => x.Name == "wing, west face").Area, 1e-6);
            Assert.AreEqual(160, f.Faces.Single(x => x.Name == "wing, east face").Area, 1e-6);
            var valleys = f.Creases.Where(c => c.Kind == CreaseKind.Valley).ToList();
            Assert.AreEqual(2, valleys.Count, string.Join("; ", f.Creases));
            foreach (var v in valleys)
            {
                Assert.AreEqual(8 * Math.Sqrt(2), v.Length, 1e-6);
                Assert.AreEqual(0, v.A.Y, 1e-6);
                Assert.AreEqual(18, v.B.X, 1e-6);
                Assert.AreEqual(8, v.B.Y, 1e-6);
            }
            var wingRidge = f.Creases.Single(c => c.Kind == CreaseKind.Ridge && c.F1.Plane.Section == 1);
            Assert.AreEqual(-16, Math.Min(wingRidge.A.Y, wingRidge.B.Y), 1e-6);
            Assert.AreEqual(8, Math.Max(wingRidge.A.Y, wingRidge.B.Y), 1e-6);
            Assert.IsTrue(f.Lines.All(l => l.TopOnFace?.Name == "main, south face"));
            Assert.AreEqual(1056, f.Rafters.Sum(r => r.Area), 1e-6);
            // a main rafter over the wing (x = 16) runs from the valley up to the ridge: a valley jack
            var vj = f.Rafters.Single(r => r.Face.Name == "main, south face" && Math.Abs(r.Line - 16) < T);
            Assert.AreEqual(6, vj.Start.Y, 1e-6);
            Assert.AreEqual(RafterEnd.Valley, vj.AtStart);
            Assert.AreEqual("valley jack", vj.Kind);
        }

        /// <summary>A wing of a lower pitch (4:12 into 6:12): its valleys are not at 45° on plan, the faces still tile.</summary>
        [TestMethod]
        public void Wing_OfItsOwnPitch_TheValleysOffDiagonal()
        {
            var f = Frame(1,
                new RoofSection { Name = "main", Walls = new Rect2(0, 0, 40, 20), Shape = RoofShape.Hip },
                new RoofSection { Name = "wing", Walls = new Rect2(10, -16, 26, 6), Shape = RoofShape.Hip, Pitch = Math.Atan(4.0 / 12) * 180 / Math.PI });
            Assert.IsNull(f.NotLaidOut);
            double total = 42 * 22 + 18 * 16; // the main's eaves; the wing past them: 18 wide from y = -17 to -1
            Assert.AreEqual(total, f.Faces.Sum(x => x.Area), 1e-6);
            Assert.AreEqual(total, f.Rafters.Sum(r => r.Area), 1e-6);
            var v = f.Creases.Where(c => c.Kind == CreaseKind.Valley).ToList();
            // the wing's hip end reaches into the main roof: a level valley across it too, and the wing's hips from there to its ridge
            Assert.AreEqual(3, v.Count, string.Join("; ", f.Creases));
            Assert.AreEqual(1, v.Count(c => Math.Abs(c.A.Y - c.B.Y) < 1e-6), "the level one");
            // along a valley the two planes are equal: (y) / 2 = (x - 10) / 3 from the inside corner (10, 0)
            foreach (var c in v) Assert.AreEqual(c.F1.Plane.Z(c.B), c.F2.Plane.Z(c.B), 1e-9);
        }

        /// <summary>
        /// A gable dormer on the main roof's south face (40 × 20 W-E, 6:12, no eaves): its walls 14 to 22 by 2 to 9, its eave
        /// 2 ft up, its ridge S-N at 4 ft dying into the main roof at (18, 8); its valleys from (14, 4) and (22, 4), where the
        /// main roof reaches its eave, up to there. Its faces: ∫ (x - 12) dx from 14 to 18 = 16 sq ft each; the main south face
        /// 400 - 32. The main rafters under it are cut at its front wall (y = 2): a header there from trimmer to trimmer (x = 14
        /// and 22, full length); its rafters stand on its cheek walls over the main roof.
        /// </summary>
        [TestMethod]
        public void Dormer_TheHeaderTrimmersAndCheeks_ByHand()
        {
            var f = Frame(0,
                new RoofSection { Name = "main", Walls = new Rect2(0, 0, 40, 20), Shape = RoofShape.Gable, Ridge = RidgeAxis.WestEast },
                new RoofSection { Name = "dormer", Walls = new Rect2(14, 2, 22, 9), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth, EaveRise = 2 });
            Assert.IsNull(f.NotLaidOut);
            Assert.AreEqual(16, f.Faces.Single(x => x.Name == "dormer, west face").Area, 1e-6);
            Assert.AreEqual(368, f.Faces.Single(x => x.Name == "main, south face").Area, 1e-6);
            Assert.AreEqual(800, f.Rafters.Sum(r => r.Area), 1e-6);
            var valleys = f.Creases.Where(c => c.Kind == CreaseKind.Valley).ToList();
            Assert.AreEqual(2, valleys.Count, string.Join("; ", f.Creases));
            Assert.IsTrue(valleys.All(v => Math.Abs(v.A.Y - 4) < 1e-6 && Math.Abs(v.B.X - 18) < 1e-6 && Math.Abs(v.B.Y - 8) < 1e-6));
            var header = f.Lines.Single(l => l.Kind == CreaseKind.Header);
            Assert.AreEqual(2, header.A.Y, 1e-9);
            Assert.AreEqual(8, header.Length, 1e-9);
            Assert.AreEqual("main, south face", header.EndsOnFace.Name);
            var cut = f.Rafters.Where(r => r.AtEnd == RafterEnd.Header).Select(r => r.Line).OrderBy(v => v).ToList();
            CollectionAssert.AreEqual(new[] { 16.0, 18.0, 20.0 }, cut);
            foreach (double trimmer in new[] { 14.0, 22.0 })
            {
                var t = f.Rafters.Single(r => r.Face.Name == "main, south face" && Math.Abs(r.Line - trimmer) < 1e-9);
                Assert.AreEqual(0, t.Start.Y, 1e-6);
                Assert.AreEqual(10, t.End.Y, 1e-6);
            }
            var cheek = f.Rafters.Single(r => r.Face.Name == "dormer, west face" && Math.Abs(r.Line - 2) < 1e-9);
            var walls = f.DormerWalls(cheek);
            Assert.AreEqual(1, walls.Count);
            Assert.AreEqual(0, walls[0].u, 1e-6);
            Assert.AreEqual("main, south face", walls[0].below.Name);
            Assert.AreEqual(RafterEnd.Ridge, cheek.AtEnd);
        }
    }
}
