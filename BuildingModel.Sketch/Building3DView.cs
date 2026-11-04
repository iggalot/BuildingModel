using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The building in 3D from what was drawn: each level's net diaphragm as a slab (openings show as holes), the roof, the walls
    /// from their base to their top (shear walls blue). Drag to turn, wheel to zoom, double-click to fit. Plan x east,
    /// y north and z up become 3D X, -Z and Y.
    /// </summary>
    public sealed class Building3DView : Border
    {
        private readonly Viewport3D viewport = new Viewport3D();
        private readonly PerspectiveCamera camera = new PerspectiveCamera { FieldOfView = 40 };
        private readonly ModelVisual3D content = new ModelVisual3D();
        private Point3D target = new Point3D();
        private double yaw = -35, pitch = 28, distance = 120;
        private Point? dragFrom;

        public const double SlabThickness = 0.75;
        public const double WallThickness = 0.5;

        /// <summary>The level shown in the plan, drawn stronger.</summary>
        public string HighlightLevelId { get; set; }

        /// <summary>The building drawn see-through (the host's own models in <see cref="Extra"/> are the subject).</summary>
        public bool Ghost { get; set; }

        /// <summary>The host's own models, drawn with the building's on every <see cref="Show"/> (3D X east, Y up, -Z north).</summary>
        public Func<Building, Model3D> Extra { get; set; }

        public Building3DView()
        {
            Background = new LinearGradientBrush(Color.FromRgb(0xF7, 0xF9, 0xFC), Color.FromRgb(0xE4, 0xE9, 0xF0), 90);
            viewport.Camera = camera;
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(0x70, 0x70, 0x70)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(0xA0, 0xA0, 0xA0), new Vector3D(-0.5, -1, -0.7)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(0x50, 0x50, 0x50), new Vector3D(0.6, -0.3, 0.8)));
            viewport.Children.Add(new ModelVisual3D { Content = lights });
            viewport.Children.Add(content);
            Child = new Grid { Children = { viewport, new TextBlock { Text = "Drag to turn · wheel to zoom · double-click to fit", Foreground = Ui.Muted, FontSize = 11, Margin = new Thickness(10, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } } };
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { Fit(); return; }
                dragFrom = e.GetPosition(this);
                CaptureMouse();
            };
            MouseMove += (s, e) =>
            {
                if (dragFrom == null) return;
                var p = e.GetPosition(this);
                yaw -= (p.X - dragFrom.Value.X) * 0.4;
                pitch = Math.Max(-5, Math.Min(85, pitch + (p.Y - dragFrom.Value.Y) * 0.3));
                dragFrom = p;
                PlaceCamera();
            };
            MouseLeftButtonUp += (s, e) => { dragFrom = null; ReleaseMouseCapture(); };
            MouseWheel += (s, e) => { distance *= e.Delta > 0 ? 0.88 : 1 / 0.88; PlaceCamera(); };
            // fitted again once it has a size (shown in a page or window after Show), until the user turns or zooms
            SizeChanged += (s, e) => { if (!moved) Fit(); };
            PreviewMouseDown += (s, e) => moved = true;
            PreviewMouseWheel += (s, e) => moved = true;
        }

        private Building building;
        private bool moved; // the user has turned or zoomed: keep their view

        /// <summary>Builds the 3D model again (after every change).</summary>
        public void Show(Building b)
        {
            bool first = building == null;
            building = b;
            var group = new Model3DGroup();
            ghost = Ghost;
            // the host's first: see-through surfaces drawn after them
            if (b != null && Extra != null) { var x = Extra(b); if (x != null) group.Children.Add(x); }
            if (b != null)
            {
                foreach (var l in b.LevelsBottomUp.Where(l => l.Elevation > 0))
                {
                    bool hi = l.Id == HighlightLevelId;
                    var mat = Material(hi ? Color.FromArgb(0xE8, 0xC9, 0xA8, 0x62) : Color.FromArgb(0xB0, 0xE2, 0xD2, 0xAE));
                    foreach (var c in b.NetDiaphragm(l).Cells())
                        group.Children.Add(Box(c.X0, c.Y0, l.Elevation - SlabThickness, c.X1, c.Y1, l.Elevation, mat));
                }
                var shear = Material(Color.FromArgb(0xF0, 0x1D, 0x4E, 0xD8));
                var other = Material(Color.FromArgb(0xD0, 0xB8, 0xBD, 0xC6));
                foreach (var w in b.Walls)
                {
                    var lo = b.Level(w.BaseLevelId);
                    var top = b.Level(w.TopLevelId);
                    if (lo == null || top == null || !(top.Elevation > lo.Elevation)) continue;
                    double t = WallThickness / 2;
                    var r = w.Direction == WallDirection.SouthNorth
                        ? new Rect2(w.Start.X - t, w.Start.Y, w.End.X + t, w.End.Y)
                        : new Rect2(w.Start.X, w.Start.Y - t, w.End.X, w.End.Y + t);
                    group.Children.Add(Box(r.X0, r.Y0, lo.Elevation, r.X1, r.Y1, top.Elevation - SlabThickness, w.IsShearWall ? shear : other));
                }
                // beams: a box under the floor they frame; columns: a square post from base to top (rectangles for now)
                var timber = Material(Color.FromArgb(0xF0, 0x8B, 0x4A, 0x14));
                foreach (var bm in b.Beams)
                {
                    var l = b.Level(bm.LevelId);
                    if (l == null || bm.Length <= 0) continue;
                    double top = l.Elevation - SlabThickness;
                    group.Children.Add(Along(bm.Start, bm.End, BeamWidth, top - BeamDepth, top, timber));
                }
                foreach (var c in b.Columns)
                {
                    var lo = b.Level(c.BaseLevelId);
                    var hi = b.Level(c.TopLevelId);
                    if (lo == null || hi == null || !(hi.Elevation > lo.Elevation)) continue;
                    double h = ColumnSize / 2;
                    group.Children.Add(Box(c.At.X - h, c.At.Y - h, lo.Elevation, c.At.X + h, c.At.Y + h, hi.Elevation - SlabThickness - BeamDepthUnder(b, hi, c.At), timber));
                }
                AddRoof(group, b);
                var ext = b.Footprint();
                if (!ext.IsEmpty)
                {
                    var bb = ext.Bounds;
                    var ground = Material(Color.FromArgb(0x60, 0x9C, 0xA3, 0xAF));
                    group.Children.Add(Box(bb.X0 - 5, bb.Y0 - 5, -0.2, bb.X1 + 5, bb.Y1 + 5, 0, ground));
                }
            }
            content.Content = group;
            if (first) Fit(); else PlaceCamera();
        }

        /// <summary>The 3D model's extent (3D X east, Y up, -Z north), for checks.</summary>
        public Rect3D ModelBounds => content.Content?.Bounds ?? Rect3D.Empty;

        public void Fit()
        {
            if (building == null) return;
            var fp = building.Footprint();
            var bb = fp.IsEmpty ? new Rect2(0, 0, 60, 40) : fp.Bounds;
            double h = building.Levels.Count == 0 ? 10 : Math.Max(10, Math.Max(building.Levels.Max(l => l.Elevation), building.RidgeHeight()));
            target = new Point3D((bb.X0 + bb.X1) / 2, h / 2, -(bb.Y0 + bb.Y1) / 2);
            // far enough that the building's bounding sphere fits both across and up (FieldOfView is the horizontal angle)
            double radius = 0.5 * Math.Sqrt(bb.Width * bb.Width + bb.Height * bb.Height + h * h);
            double aspect = ActualWidth > 0 && ActualHeight > 0 ? ActualHeight / ActualWidth : 0.7;
            double half = camera.FieldOfView / 2 * Math.PI / 180;
            double halfV = Math.Atan(Math.Tan(half) * aspect);
            distance = 1.1 * radius / Math.Sin(Math.Min(half, halfV));
            PlaceCamera();
        }

        private void PlaceCamera()
        {
            double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
            var dir = new Vector3D(Math.Cos(p) * Math.Sin(y), Math.Sin(p), Math.Cos(p) * Math.Cos(y));
            camera.Position = target + dir * distance;
            camera.LookDirection = -dir * distance;
            camera.UpDirection = new Vector3D(0, 1, 0);
        }

        /// <summary>The model's roof (Building.Roof) over the top level: a gable or hip from the roof plan widened by the eaves,
        /// rising to the ridge height; a flat roof's parapet as a low wall around the plan.</summary>
        public const double ParapetThickness = 0.5;

        private static void AddRoof(Model3DGroup group, Building b)
        {
            var roof = b.Roof;
            var plan = b.RoofPlan();
            if (roof == null || plan.Width <= 0 || plan.Height <= 0) return;
            double eave = b.EaveHeight();
            var roofMat = Material(Color.FromArgb(0xE8, 0x7A, 0x6A, 0x5A));
            if (!roof.IsSloped)
            {
                if (roof.Parapet <= 0) return;
                double t = ParapetThickness, z1 = eave + roof.Parapet;
                group.Children.Add(Box(plan.X0, plan.Y0, eave, plan.X1, plan.Y0 + t, z1, roofMat));
                group.Children.Add(Box(plan.X0, plan.Y1 - t, eave, plan.X1, plan.Y1, z1, roofMat));
                group.Children.Add(Box(plan.X0, plan.Y0, eave, plan.X0 + t, plan.Y1, z1, roofMat));
                group.Children.Add(Box(plan.X1 - t, plan.Y0, eave, plan.X1, plan.Y1, z1, roofMat));
                return;
            }
            double rise = b.RoofRise();
            if (rise <= 0) return;
            double o = Math.Max(0, roof.Overhang), slope = Math.Tan(roof.Pitch * Math.PI / 180);
            double x0 = plan.X0 - o, x1 = plan.X1 + o, y0 = plan.Y0 - o, y1 = plan.Y1 + o;
            double ze = eave - o * slope, zr = eave + rise; // the eaves' edge drops below the wall top by the overhang's slope
            bool alongX = b.EffectiveRidge() != RidgeAxis.SouthNorth;
            // work in a frame where the ridge runs along u (x when W-E); v across it
            double u0 = alongX ? x0 : y0, u1 = alongX ? x1 : y1, v0 = alongX ? y0 : x0, v1 = alongX ? y1 : x1, vc = (v0 + v1) / 2;
            Point3D P(double u, double v, double z) => alongX ? new Point3D(u, z, -v) : new Point3D(v, z, -u);
            double ua = u0, ub = u1;
            if (roof.Shape == RoofShape.Hip)
            {
                double half = (v1 - v0) / 2;
                ua = Math.Min(u0 + half, (u0 + u1) / 2); ub = Math.Max(u1 - half, (u0 + u1) / 2);
            }
            var mesh = new MeshGeometry3D();
            void Face(params Point3D[] pts)
            {
                int i = mesh.Positions.Count;
                foreach (var p in pts) mesh.Positions.Add(p);
                for (int k = 1; k + 1 < pts.Length; k++) { mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + k); mesh.TriangleIndices.Add(i + k + 1); }
            }
            Face(P(u0, v0, ze), P(u1, v0, ze), P(ub, vc, zr), P(ua, vc, zr));   // one slope
            Face(P(u1, v1, ze), P(u0, v1, ze), P(ua, vc, zr), P(ub, vc, zr));   // the other
            Face(P(u0, v1, ze), P(u0, v0, ze), P(ua, vc, zr));                  // the ends: a hip's slopes, a gable's triangles
            Face(P(u1, v0, ze), P(u1, v1, ze), P(ub, vc, zr));
            mesh.Freeze();
            group.Children.Add(new GeometryModel3D(mesh, roofMat) { BackMaterial = roofMat });
        }

        /// <summary>While <see cref="Show"/> builds: <see cref="Ghost"/> (one view builds at a time, on the UI thread).</summary>
        [ThreadStatic] private static bool ghost;

        private static Material Material(Color c)
        {
            if (ghost)
            {
                // see-through and grey: the host's own colours are the subject
                byte g = (byte)Math.Min(255, 0.3 * c.R + 0.59 * c.G + 0.11 * c.B + 40);
                c = Color.FromArgb((byte)(c.A * 0.16), g, g, g);
            }
            var m = new DiffuseMaterial(new SolidColorBrush(c));
            m.Freeze();
            return m;
        }

        /// <summary>A box from plan (x0, y0) to (x1, y1) and from z0 up to z1.</summary>
        /// <summary>Shown sizes (ft) of beams and columns: rectangles until they are sized.</summary>
        public const double BeamWidth = 0.45, BeamDepth = 1.0, ColumnSize = 0.45;

        /// <summary>A column under a beam stops at the beam's underside.</summary>
        private static double BeamDepthUnder(Building b, Level top, BuildingModel.Geometry.Point2 at) =>
            b.BeamsOn(top).Any(x => BuildingModel.Edits.Snapper.DistanceToSegment(at, new BuildingModel.Geometry.Segment2(x.Start, x.End)) <= 0.5) ? BeamDepth : 0;

        /// <summary>A box of width <paramref name="w"/> along a segment in plan (any direction), from z0 to z1.</summary>
        private static GeometryModel3D Along(BuildingModel.Geometry.Point2 a, BuildingModel.Geometry.Point2 e, double w, double z0, double z1, Material m)
        {
            double len = Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Y - a.Y) * (e.Y - a.Y));
            double nx = -(e.Y - a.Y) / len * w / 2, ny = (e.X - a.X) / len * w / 2;
            var mesh = new MeshGeometry3D();
            Point3D P(double x, double y, double z) => new Point3D(x, z, -y);
            var c = new[] { P(a.X - nx, a.Y - ny, z0), P(e.X - nx, e.Y - ny, z0), P(e.X + nx, e.Y + ny, z0), P(a.X + nx, a.Y + ny, z0),
                            P(a.X - nx, a.Y - ny, z1), P(e.X - nx, e.Y - ny, z1), P(e.X + nx, e.Y + ny, z1), P(a.X + nx, a.Y + ny, z1) };
            return Mesh(c, m);
        }

        private static GeometryModel3D Box(double x0, double y0, double z0, double x1, double y1, double z1, Material m)
        {
            Point3D P(double x, double y, double z) => new Point3D(x, z, -y);
            return Mesh(new[] { P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0), P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1) }, m);
        }

        private static GeometryModel3D Mesh(Point3D[] c, Material m)
        {
            var mesh = new MeshGeometry3D();
            int[][] faces = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            foreach (var f in faces)
            {
                int i = mesh.Positions.Count;
                foreach (var k in f) mesh.Positions.Add(c[k]);
                mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
                mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
            }
            mesh.Freeze();
            return new GeometryModel3D(mesh, m) { BackMaterial = m };
        }
    }
}
