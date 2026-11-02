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
    /// The building in 3D from what was drawn: each level's net diaphragm as a slab (openings show as holes), the walls
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
        }

        private Building building;

        /// <summary>Builds the 3D model again (after every change).</summary>
        public void Show(Building b)
        {
            bool first = building == null;
            building = b;
            var group = new Model3DGroup();
            if (b != null)
            {
                foreach (var l in b.LevelsBottomUp.Where(l => l.Elevation > 0))
                {
                    bool hi = l.Id == HighlightLevelId;
                    var mat = Material(hi ? Color.FromArgb(0xE0, 0xD9, 0x5B, 0x5B) : Color.FromArgb(0xB0, 0xD9, 0x8B, 0x8B));
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

        public void Fit()
        {
            if (building == null) return;
            var fp = building.Footprint();
            var bb = fp.IsEmpty ? new Rect2(0, 0, 60, 40) : fp.Bounds;
            double h = building.Levels.Count == 0 ? 10 : Math.Max(10, building.Levels.Max(l => l.Elevation));
            target = new Point3D((bb.X0 + bb.X1) / 2, h / 2, -(bb.Y0 + bb.Y1) / 2);
            double size = Math.Max(Math.Max(bb.Width, bb.Height), h);
            distance = size * 2.6;
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

        private static Material Material(Color c)
        {
            var m = new DiffuseMaterial(new SolidColorBrush(c));
            m.Freeze();
            return m;
        }

        /// <summary>A box from plan (x0, y0) to (x1, y1) and from z0 up to z1.</summary>
        private static GeometryModel3D Box(double x0, double y0, double z0, double x1, double y1, double z1, Material m)
        {
            var mesh = new MeshGeometry3D();
            Point3D P(double x, double y, double z) => new Point3D(x, z, -y);
            var c = new[] { P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0), P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1) };
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
