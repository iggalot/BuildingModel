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
