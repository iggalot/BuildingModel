using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// Draws the building model's <see cref="NorthArrow"/> (its one definition) on a plan and in a 3D view, the same in every app:
    /// the grey arrow on the ground off the north-west corner, and an upright N past its tip.
    /// </summary>
    public static class NorthArrowDrawing
    {
        public static readonly Brush ArrowBrush = Ui.Frozen((Color)ColorConverter.ConvertFromString(NorthArrow.Color));
        public static readonly Brush LabelBrush = Ui.Frozen((Color)ColorConverter.ConvertFromString(NorthArrow.LabelColor));

        /// <summary>On a plan: <paramref name="toScreen"/> takes plan ft to the screen.</summary>
        public static void Draw(DrawingContext dc, NorthArrow n, Func<Point2, Point> toScreen, double pixelsPerDip)
        {
            if (n == null) return;
            dc.DrawGeometry(ArrowBrush, null, Polygon(n.Shaft, toScreen));
            dc.DrawGeometry(ArrowBrush, null, Polygon(n.Head, toScreen));
            var ft = new FormattedText(NorthArrow.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), NorthArrow.LabelSize, LabelBrush, pixelsPerDip);
            var at = toScreen(n.LabelAt);
            dc.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
        }

        private static System.Windows.Media.Geometry Polygon(IReadOnlyList<Point2> pts, Func<Point2, Point> toScreen)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(toScreen(pts[0]), true, true);
                for (int i = 1; i < pts.Count; i++) c.LineTo(toScreen(pts[i]), false, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// In 3D: the arrow as a flat model (seen from both sides); <paramref name="map"/> takes plan ft to the model's 3D
        /// (on the ground, just above it).
        /// </summary>
        public static Model3D Model(NorthArrow n, Func<Point2, Point3D> map)
        {
            if (n == null) return null;
            var mesh = new MeshGeometry3D();
            void Fan(IReadOnlyList<Point2> pts)
            {
                int i0 = mesh.Positions.Count;
                foreach (var p in pts) mesh.Positions.Add(map(p));
                for (int i = 1; i + 1 < pts.Count; i++)
                {
                    mesh.TriangleIndices.Add(i0); mesh.TriangleIndices.Add(i0 + i); mesh.TriangleIndices.Add(i0 + i + 1);
                }
            }
            Fan(n.Shaft);
            Fan(n.Head);
            var mat = new DiffuseMaterial(ArrowBrush);
            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }

        /// <summary>The N for a 3D view: put it on an overlay canvas and <see cref="Pin"/> it after every camera move.</summary>
        public static TextBlock NewLabel() => new TextBlock
        {
            Text = NorthArrow.Label, FontWeight = FontWeights.SemiBold, FontSize = NorthArrow.LabelSize, Foreground = LabelBrush,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };

        /// <summary>Centres the N on <paramref name="at"/> (overlay pixels), or hides it when the point is behind the camera.</summary>
        public static void Pin(TextBlock label, Point? at)
        {
            label.Visibility = at.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (!at.HasValue) return;
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, at.Value.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, at.Value.Y - label.DesiredSize.Height / 2);
        }

        /// <summary>Plan ft to the 3D views' axes (X east, Y up, −Z north), at <paramref name="elevation"/> ft.</summary>
        public static Point3D OnGround(Point2 p, double elevation = 0.01) => new Point3D(p.X, elevation, -p.Y);
    }
}
