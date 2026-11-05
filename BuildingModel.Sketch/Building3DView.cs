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
        private const double StartYaw = -35, StartPitch = 28;
        private double yaw = StartYaw, pitch = StartPitch, distance = 120;

        /// <summary>Back to the first view: the starting angle, the whole building and its north arrow fitted (and fitted again on resize).</summary>
        public void ResetView()
        {
            yaw = StartYaw;
            pitch = StartPitch;
            moved = false;
            Fit();
        }
        private Point? dragFrom;

        public const double SlabThickness = 0.75;
        public const double WallThickness = 0.5;

        /// <summary>The level shown in the plan, drawn stronger.</summary>
        public string HighlightLevelId { get; set; }

        /// <summary>The building drawn see-through (the host's own models in <see cref="Extra"/> are the subject).</summary>
        public bool Ghost { get; set; }

        /// <summary>The host's own models, drawn with the building's on every <see cref="Show"/> (3D X east, Y up, -Z north).</summary>
        public Func<Building, Model3D> Extra { get; set; }

        /// <summary>
        /// Draw only this element (a wall, beam or column ID), whole and solid, with the host's <see cref="Extra"/>: no slabs, roof
        /// or ground (a free body). Null: the whole building.
        /// </summary>
        public string Only { get; set; }

        /// <summary>With <see cref="Only"/>: draw the element itself (see-through), or leave it to the host (its framing drawn in <see cref="Extra"/>).</summary>
        public bool DrawOnly { get; set; } = true;

        /// <summary>A wall, beam or column was clicked (not dragged): its ID; null when the click was on none of them.</summary>
        public event Action<string> Picked;

        /// <summary>
        /// The host's say on a click, before <see cref="Picked"/>: given the click (in this view's pixels), it answers true when it
        /// takes it (e.g. it is near one of its own <see cref="Extra"/> models, found with <see cref="Project"/>). Its models win
        /// over the see-through building in front of them; a click it takes raises no <see cref="Picked"/>.
        /// </summary>
        public Func<Point, bool> PickScreen { get; set; }

        /// <summary>Where a 3D point (X east, Y up, -Z north) is drawn in this view (pixels); null when behind the camera.</summary>
        public Point? Project(Point3D p)
        {
            var v = OrbitMath.Project(camera, p, new Size(viewport.ActualWidth, viewport.ActualHeight));
            return v.HasValue ? viewport.TranslatePoint(v.Value, this) : (Point?)null;
        }

        private Point? panFrom;
        private Grid stage; // the view under the bottom bar: the mouse turns, pans and zooms it

        private void EndPan()
        {
            panFrom = null;
            Cursor = null;
            if (dragFrom == null) stage.ReleaseMouseCapture();
        }

        // what each drawn element's model is, for picking
        private readonly Dictionary<Model3D, string> ids = new Dictionary<Model3D, string>();
        private Point? downAt;

        private readonly FrameworkElement viewBar;

        /// <summary>The bar under the view (VIEW, zoom to all, the mouse hint). Off for a small preview (the hub's, New project's): the mouse still turns, zooms and double-click fits.</summary>
        /// <summary>How much bigger than usual the north arrow is drawn (1: as in every view). A small preview sets about 2.5, or the arrow is a few pixels.</summary>
        public double NorthArrowScale { get; set; } = 1;

        public bool ShowViewBar
        {
            get => viewBar.Visibility == Visibility.Visible;
            set => viewBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

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
            // the view over the bottom bar (VIEW, zoom to all, the mouse hint), laid out as on every plan; the mouse works on the view only
            stage = new Grid { Background = Brushes.Transparent, Children = { viewport, new Canvas { IsHitTestVisible = false, Children = { northLabel } } } };
            viewBar = Ui.ViewBar(Fit, Ui.View3DHint, "Fit the model in the view (or double-click). The wheel zooms at the pointer, the middle button pans.");
            DockPanel.SetDock(viewBar, Dock.Bottom);
            Child = new DockPanel { Children = { viewBar, stage } };
            stage.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { Fit(); return; }
                dragFrom = e.GetPosition(this);
                downAt = dragFrom;
                stage.CaptureMouse();
            };
            // a click that did not turn the view picks what is under it
            stage.MouseLeftButtonUp += (s, e) =>
            {
                var p = e.GetPosition(this);
                if (downAt is Point d && Math.Abs(p.X - d.X) < 4 && Math.Abs(p.Y - d.Y) < 4)
                {
                    if (PickScreen != null && PickScreen(p)) { downAt = null; return; }
                    Picked?.Invoke(HitsAt(e.GetPosition(viewport)).element);
                }
                downAt = null;
            };
            stage.MouseMove += (s, e) =>
            {
                if (dragFrom == null) return;
                var p = e.GetPosition(this);
                yaw -= (p.X - dragFrom.Value.X) * 0.4;
                pitch = Math.Max(-5, Math.Min(85, pitch + (p.Y - dragFrom.Value.Y) * 0.3));
                dragFrom = p;
                PlaceCamera();
            };
            stage.MouseLeftButtonUp += (s, e) => { dragFrom = null; stage.ReleaseMouseCapture(); };
            // the middle button pans (double-click: fit), the wheel zooms toward the pointer: as on the plan
            stage.MouseDown += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                if (e.ClickCount == 2) { Fit(); return; }
                panFrom = e.GetPosition(this);
                stage.CaptureMouse();
                Cursor = Cursors.SizeAll;
                e.Handled = true;
            };
            stage.MouseMove += (s, e) =>
            {
                if (panFrom == null) return;
                if (e.MiddleButton != MouseButtonState.Pressed) { EndPan(); return; }
                var p = e.GetPosition(this);
                target = OrbitMath.Pan(camera, target, distance, p.X - panFrom.Value.X, p.Y - panFrom.Value.Y, viewport.ActualWidth);
                panFrom = p;
                PlaceCamera();
            };
            stage.MouseUp += (s, e) => { if (e.ChangedButton == MouseButton.Middle) EndPan(); };
            stage.MouseWheel += (s, e) =>
            {
                (target, distance) = OrbitMath.ZoomAt(camera, target, distance, e.Delta > 0 ? 0.88 : 1 / 0.88, e.GetPosition(viewport), new Size(viewport.ActualWidth, viewport.ActualHeight));
                PlaceCamera();
            };
            // fitted again once it has a size (shown in a page or window after Show), until the user turns or zooms
            SizeChanged += (s, e) => { if (!moved) Fit(); else PlaceCamera(); };
            stage.PreviewMouseDown += (s, e) => moved = true;
            stage.PreviewMouseWheel += (s, e) => moved = true;
        }

        private Building building;
        private bool moved; // the user has turned or zoomed: keep their view

        /// <summary>Builds the 3D model again (after every change).</summary>
        public void Show(Building b)
        {
            bool first = building == null;
            building = b;
            var group = new Model3DGroup();
            ids.Clear();
            string only = Only;
            // a free body: the element see-through grey too (a little stronger), the host's forces on it seen through it
            ghost = Ghost;
            ghostAlpha = only == null ? 0.16 : 0.32;
            bool Drawn(string id) => only == null || (id == only && DrawOnly);
            // the host's first: see-through surfaces drawn after them
            if (b != null && Extra != null) { var x = Extra(b); if (x != null) group.Children.Add(x); }
            if (b != null)
            {
                if (only == null)
                    foreach (var l in b.LevelsBottomUp.Where(l => l.Elevation > 0))
                    {
                        bool hi = l.Id == HighlightLevelId;
                        var mat = Material(hi ? Color.FromArgb(0xE8, 0xC9, 0xA8, 0x62) : Color.FromArgb(0xB0, 0xE2, 0xD2, 0xAE));
                        foreach (var c in b.NetDiaphragm(l).Cells())
                            group.Children.Add(Box(c.X0, c.Y0, l.Elevation - SlabThickness, c.X1, c.Y1, l.Elevation, mat));
                    }
                var shear = Material(Color.FromArgb(0xF0, 0x1D, 0x4E, 0xD8));
                var other = Material(Color.FromArgb(0xD0, 0xB8, 0xBD, 0xC6));
                foreach (var w in b.Walls.Where(w => Drawn(w.Id)))
                {
                    var lo = b.Level(w.BaseLevelId);
                    var top = b.Level(w.TopLevelId);
                    if (lo == null || top == null || !(top.Elevation > lo.Elevation)) continue;
                    double t = WallThickness / 2;
                    var r = w.Direction == WallDirection.SouthNorth
                        ? new Rect2(w.Start.X - t, w.Start.Y, w.End.X + t, w.End.Y)
                        : new Rect2(w.Start.X, w.Start.Y - t, w.End.X, w.End.Y + t);
                    Add(group, Box(r.X0, r.Y0, lo.Elevation, r.X1, r.Y1, top.Elevation - SlabThickness, w.IsShearWall ? shear : other), w.Id);
                }
                // beams: a box under the floor they frame; columns: a square post from base to top (rectangles for now)
                var timber = Material(Color.FromArgb(0xF0, 0x8B, 0x4A, 0x14));
                foreach (var bm in b.Beams.Where(x => Drawn(x.Id)))
                {
                    var l = b.Level(bm.LevelId);
                    if (l == null || bm.Length <= 0) continue;
                    double top = l.Elevation - SlabThickness;
                    Add(group, Along(bm.Start, bm.End, BeamWidth, top - BeamDepth, top, timber), bm.Id);
                }
                foreach (var c in b.Columns.Where(x => Drawn(x.Id)))
                {
                    var lo = b.Level(c.BaseLevelId);
                    var hi = b.Level(c.TopLevelId);
                    if (lo == null || hi == null || !(hi.Elevation > lo.Elevation)) continue;
                    double h = ColumnSize / 2;
                    Add(group, Box(c.At.X - h, c.At.Y - h, lo.Elevation, c.At.X + h, c.At.Y + h, ColumnTop(b, c), timber), c.Id);
                }
                if (only == null)
                {
                    AddRoof(group, b);
                    var ext = b.Footprint();
                    if (!ext.IsEmpty)
                    {
                        var bb = ext.Bounds;
                        var ground = Material(Color.FromArgb(0x60, 0x9C, 0xA3, 0xAF));
                        group.Children.Add(Box(bb.X0 - 5, bb.Y0 - 5, -0.2, bb.X1 + 5, bb.Y1 + 5, 0, ground));
                    }
                }
            }
            // the north arrow, the building model's one (as in every view): off the building's north-west corner, or a free body's
            var north = b == null ? null : only == null ? NorthArrow.For(b) : ElementBounds(b, only) is Rect2 eb ? NorthArrow.For(eb) : null;
            north = north?.Scaled(NorthArrowScale);
            if (north != null) group.Children.Add(NorthArrowDrawing.Model(north, p => NorthArrowDrawing.OnGround(p)));
            northAt = north == null ? (Point3D?)null : NorthArrowDrawing.OnGround(north.LabelAt);
            northTip = north == null ? (Point3D?)null : NorthArrowDrawing.OnGround(north.Tip);
            northTail = north == null ? (Point3D?)null : NorthArrowDrawing.OnGround(north.Tail);
            content.Content = group;
            if (first || only != shownOnly) Fit(); else PlaceCamera();
            shownOnly = only;
        }

        private readonly TextBlock northLabel = NorthArrowDrawing.NewLabel();
        private Point3D? northAt, northTip, northTail;

        /// <summary>
        /// Where the N goes on the screen: past the arrow's tip, along the arrow as drawn, by half the N's size and a little more,
        /// so the N (a fixed size on the screen) never covers the arrow however far the view is zoomed out. Null: not in view.
        /// </summary>
        private Point? NorthLabelOnScreen()
        {
            var size = new Size(viewport.ActualWidth, viewport.ActualHeight);
            Point? Of(Point3D? p) => p is Point3D q ? OrbitMath.Project(camera, q, size) : null;
            if (Of(northTip) is Point tip && Of(northTail) is Point tail)
            {
                var along = tip - tail;
                if (along.Length > 0.5)
                {
                    along.Normalize();
                    return tip + along * (NorthArrow.LabelSize * 0.75 + 3);
                }
            }
            return Of(northAt);
        }

        /// <summary>A wall's, beam's or column's plan extent (ft).</summary>
        private static Rect2? ElementBounds(Building b, string id)
        {
            Rect2 Of(Point2 a, Point2 c) => new Rect2(Math.Min(a.X, c.X), Math.Min(a.Y, c.Y), Math.Max(a.X, c.X), Math.Max(a.Y, c.Y));
            var w = b.Walls.FirstOrDefault(x => x.Id == id);
            if (w != null) return Of(w.Start, w.End);
            var bm = b.Beams.FirstOrDefault(x => x.Id == id);
            if (bm != null) return Of(bm.Start, bm.End);
            var c = b.Columns.FirstOrDefault(x => x.Id == id);
            if (c != null) return Of(new Point2(c.At.X - 0.5, c.At.Y - 0.5), new Point2(c.At.X + 0.5, c.At.Y + 0.5));
            return null;
        }

        private string shownOnly;

        /// <summary>The top of a column as drawn (ft): the level it reaches, less the slab and any beam it stops under.</summary>
        public static double ColumnTop(Building b, Column c)
        {
            var hi = b?.Level(c.TopLevelId);
            return hi == null ? 0 : hi.Elevation - SlabThickness - BeamDepthUnder(b, hi, c.At);
        }

        /// <summary>The top of a wall as drawn (ft): under the slab of the level it reaches.</summary>
        public static double WallTop(Building b, Wall w) => (b?.Level(w.TopLevelId)?.Elevation ?? 0) - SlabThickness;

        /// <summary>The top of a beam as drawn (ft): under its level's slab (it is <see cref="BeamDepth"/> deep).</summary>
        public static double BeamTop(Building b, Beam bm) => (b?.Level(bm.LevelId)?.Elevation ?? 0) - SlabThickness;

        private void Add(Model3DGroup g, GeometryModel3D m, string id)
        {
            g.Children.Add(m);
            if (id != null) ids[m] = id;
        }

        /// <summary>The wall, beam or column under a point of the viewport (the nearest drawn one), or null.</summary>
        /// <summary>Under a point: the nearest wall, beam or column (null: none) and every other model, nearest first (the host's).</summary>
        private (string element, List<Model3D> extras) HitsAt(Point p)
        {
            string hit = null;
            var extras = new List<Model3D>();
            VisualTreeHelper.HitTest(viewport, null, r =>
            {
                if (r is RayMeshGeometry3DHitTestResult m && m.ModelHit is Model3D model)
                {
                    if (ids.TryGetValue(model, out var id)) { if (hit == null) hit = id; }
                    else extras.Add(model);
                }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(p));
            return (hit, extras);
        }

        /// <summary>The 3D model's extent (3D X east, Y up, -Z north), for checks.</summary>
        public Rect3D ModelBounds => content.Content?.Bounds ?? Rect3D.Empty;

        public void Fit()
        {
            if (building == null) return;
            if (Only != null && content.Content != null && !content.Content.Bounds.IsEmpty)
            {
                // a free body: the element and the host's forces on it
                var r = content.Content.Bounds;
                target = new Point3D(r.X + r.SizeX / 2, r.Y + r.SizeY / 2, r.Z + r.SizeZ / 2);
                double rad = 0.5 * Math.Sqrt(r.SizeX * r.SizeX + r.SizeY * r.SizeY + r.SizeZ * r.SizeZ);
                double asp = viewport.ActualWidth > 0 && viewport.ActualHeight > 0 ? viewport.ActualHeight / viewport.ActualWidth : 0.7;
                double hf = camera.FieldOfView / 2 * Math.PI / 180;
                distance = 1.1 * Math.Max(1, rad) / Math.Sin(Math.Min(hf, Math.Atan(Math.Tan(hf) * asp)));
                PlaceCamera();
                return;
            }
            var fp = building.Footprint();
            var bb = fp.IsEmpty ? new Rect2(0, 0, 60, 40) : fp.Bounds;
            // the north arrow kept in view
            if (NorthArrow.For(building)?.Scaled(NorthArrowScale).Extent() is Rect2 ne)
                bb = new Rect2(Math.Min(bb.X0, ne.X0), Math.Min(bb.Y0, ne.Y0), Math.Max(bb.X1, ne.X1), Math.Max(bb.Y1, ne.Y1));
            double h = building.Levels.Count == 0 ? 10 : Math.Max(10, Math.Max(building.Levels.Max(l => l.Elevation), building.RidgeHeight()));
            target = new Point3D((bb.X0 + bb.X1) / 2, h / 2, -(bb.Y0 + bb.Y1) / 2);
            // far enough that the building's bounding sphere fits both across and up (FieldOfView is the horizontal angle)
            double radius = 0.5 * Math.Sqrt(bb.Width * bb.Width + bb.Height * bb.Height + h * h);
            double aspect = viewport.ActualWidth > 0 && viewport.ActualHeight > 0 ? viewport.ActualHeight / viewport.ActualWidth : 0.7;
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
            NorthArrowDrawing.Pin(northLabel, NorthLabelOnScreen());
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
        [ThreadStatic] private static double ghostAlpha;

        private static Material Material(Color c)
        {
            if (ghost)
            {
                // see-through and grey: the host's own colours are the subject
                byte g = (byte)Math.Min(255, 0.3 * c.R + 0.59 * c.G + 0.11 * c.B + 40);
                c = Color.FromArgb((byte)(c.A * ghostAlpha), g, g, g);
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
