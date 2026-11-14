using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>
    /// The roof as drawn (owner 2026-11-13, tutorial 8's L-shaped house): the gable ends closed, each roof plane its own
    /// shade. The L: a hip over the main block 56 × 28, a gable wing 24 × 24 north of its east end, 6:12, 1.5 ft eaves.
    /// </summary>
    [TestClass]
    public class RoofLookTests
    {
        internal static Building L(double wingOffset = 0)
        {
            var b = Building.New();
            var top = b.AddLevel("Level 2", 9);
            b.AddDiaphragm(top, new Rect2(0, 0, 56, 28));
            b.AddDiaphragm(top, new Rect2(32, 28, 56, 52)).Offset = wingOffset;
            foreach (var (x0, y0, x1, y1) in new[] { (0.0, 0.0, 56.0, 0.0), (0.0, 0.0, 0.0, 28.0), (0.0, 28.0, 32.0, 28.0), (32.0, 28.0, 56.0, 28.0),
                                                     (56.0, 0.0, 56.0, 28.0), (32.0, 28.0, 32.0, 52.0), (32.0, 52.0, 56.0, 52.0), (56.0, 28.0, 56.0, 52.0) })
                b.AddWall(b.Ground, top, x0, y0, x1, y1);
            b.Roof = new Roof
            {
                Shape = RoofShape.Hip, Pitch = Math.Atan(0.5) * 180 / Math.PI, Ridge = RidgeAxis.WestEast, Overhang = 1.5,
                Sections = new List<RoofSection>
                {
                    new RoofSection { Name = "main", Walls = new Rect2(0, 0, 56, 28), Shape = RoofShape.Hip, Ridge = RidgeAxis.WestEast },
                    new RoofSection { Name = "wing", Walls = new Rect2(32, 28, 56, 52), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth },
                },
            };
            return b;
        }

        private static Wall WallAt(Building b, double x0, double y0, double x1, double y1) =>
            b.Walls.Single(w => Math.Abs(w.Start.X - x0) < 1e-9 && Math.Abs(w.Start.Y - y0) < 1e-9 && Math.Abs(w.End.X - x1) < 1e-9 && Math.Abs(w.End.Y - y1) < 1e-9);

        /// <summary>
        /// The wing's north wall is its gable end: closed from its top to the roof, a triangle 6 ft high (12 ft half-span at 6:12)
        /// peaking at its middle; the eave walls, the hip ends and the wall between the main block and the wing (inside) get none.
        /// </summary>
        [STATestMethod]
        public void TheGableEnd_ClosedUpToTheRoof()
        {
            var b = L();
            var gable = Building3DView.GableOver(b, WallAt(b, 32, 52, 56, 52));
            Assert.IsNotNull(gable, "the wing's gable end is filled");
            double bottom = gable.Positions.Min(p => p.Y), peak = gable.Positions.Max(p => p.Y);
            Assert.AreEqual(6.0 + Building3DView.SlabThickness, peak - bottom, 0.2, "up to the ridge: 12 ft × 6/12 over the plate (the wall drawn to under the slab)");
            var top = gable.Positions.OrderByDescending(p => p.Y).First();
            Assert.AreEqual(44.0, top.X, 0.3, "the peak at the wall's middle");

            Assert.IsNull(Building3DView.GableOver(b, WallAt(b, 0, 0, 56, 0)), "the main block's south wall: an eave");
            Assert.IsNull(Building3DView.GableOver(b, WallAt(b, 0, 0, 0, 28)), "the hip's west end: the hip slopes up from it");
            Assert.IsNull(Building3DView.GableOver(b, WallAt(b, 32, 28, 56, 28)), "between the two: inside");
            Assert.IsNull(Building3DView.GableOver(b, WallAt(b, 32, 28, 32, 52)), "the wing's west wall: an eave");
        }

        /// <summary>
        /// Every two roof planes that meet (a ridge, hip or valley between them) are drawn in different shades, so each section
        /// and each slope reads on its own.
        /// </summary>
        [TestMethod]
        public void PlanesThatMeet_DifferentShades()
        {
            var frame = BuildingModel.Statics.RoofFrame.Of(L());
            Assert.IsNull(frame.NotLaidOut, frame.NotLaidOut);
            int pairs = 0;
            foreach (var c in frame.Creases.Where(c => c.Kind != BuildingModel.Statics.CreaseKind.Header))
            {
                var mid = new Point2((c.A.X + c.B.X) / 2, (c.A.Y + c.B.Y) / 2);
                var touching = frame.Faces.Where(f => f.Pieces.Any(pc => pc.P.Any(p => Math.Abs(p.X - mid.X) + Math.Abs(p.Y - mid.Y) < 1e-6) ||
                                                     Near(pc.P, mid))).ToList();
                for (int i = 0; i < touching.Count; i++)
                    for (int j = i + 1; j < touching.Count; j++)
                    {
                        if (touching[i].Plane.Side == touching[j].Plane.Side && touching[i].Plane.Section == touching[j].Plane.Section) continue; // one plane cut in two
                        pairs++;
                        Assert.AreNotEqual(Building3DView.RoofShade(touching[i].Plane.Side, touching[i].Plane.Section),
                                           Building3DView.RoofShade(touching[j].Plane.Side, touching[j].Plane.Section), $"{touching[i].Name} / {touching[j].Name}");
                    }
            }
            Assert.IsTrue(pairs >= 4, $"{pairs} pairs checked");
        }

        /// <summary>The L in 3D renders, the roof in several shades (SNAPSHOT_DIR saves it to look at).</summary>
        [STATestMethod]
        public void TheL_In3D_Renders()
        {
            var v = new Building3DView();
            v.Show(L(-1));
            var bmp = SketchTests.Render(v, "l-house-3d");
            int shades = new[] { "south", "west", "east", "north" }.Count(s => SketchTests.Count(bmp, Building3DView.RoofShade(s, 0), 40) > 50);
            Assert.IsTrue(shades >= 2, $"{shades} roof shades seen");
        }

        /// <summary>
        /// The roof's own plan, R (owner 2026-11-13: "R should be a level automatically, like G"): its button after the top
        /// level's; on it the ridges, hips and valleys; on Level 2 none of them (its ceiling joists and diaphragm only). Walls and
        /// floors are not drawn on R; a reload keeps R shown.
        /// </summary>
        [STATestMethod]
        public void TheRoofPlan_ALevelOfItsOwn()
        {
            var b = L(-1);
            var top = b.RoofLevel();
            var v = new SketchView();
            v.Canvas.SetBuilding(b, top.Id);
            SketchTests.Render(v, "rplan");
            v.Canvas.ZoomExtents();
            var ridge = Color.FromRgb(0xB2, 0x22, 0x22);
            var hip = Color.FromRgb(0xE0, 0x7B, 0x00);
            int onLevel2 = SketchTests.Count(SketchTests.Render(v, "rplan-level2"), ridge, 12) + SketchTests.Count(SketchTests.Render(v, "rplan-level2"), hip, 12);
            v.Canvas.ShowLevel(RoofQueries.RoofPlanId);
            Assert.IsTrue(v.Canvas.RoofPlan);
            Assert.AreEqual(top.Id, v.Canvas.ActiveLevel.Id, "R stands on the top level's plates");
            var onR = SketchTests.Render(v, "rplan-roof");
            int onRoof = SketchTests.Count(onR, ridge, 12) + SketchTests.Count(onR, hip, 12);
            Assert.IsTrue(onRoof > onLevel2 + 200, $"the ridges and hips on R ({onRoof} pixels), not on Level 2 ({onLevel2})");

            v.Canvas.SetTool(SketchTool.Wall);
            Assert.AreEqual(SketchTool.Select, v.Canvas.Tool, "no walls drawn on R");
            v.Canvas.SetBuilding(b, top.Id);
            Assert.IsTrue(v.Canvas.RoofPlan, "a reload keeps R");
            v.Canvas.ShowLevel(top.Id);
            Assert.IsFalse(v.Canvas.RoofPlan);
        }

        private static bool Near(List<Point2> poly, Point2 p)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2)) : 0;
                double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
                if (ex * ex + ey * ey < 1e-6) return true;
            }
            return false;
        }
    }
}
