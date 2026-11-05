using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>
    /// The north arrow: the one reference every view of the building draws the same way (each plan, each 3D view, the wind
    /// calculator's and the shear wall app's). A flat arrow on the ground off the north-west corner, pointing north (plan +Y),
    /// labelled N. Its shape, size, place and colours are here and only here; the views only draw them.
    /// </summary>
    public sealed class NorthArrow
    {
        /// <summary>The arrow's colour (grey, as on the ground).</summary>
        public const string Color = "#898781";

        /// <summary>The N's colour; the N is semibold, 14 px, upright on the screen, centred on <see cref="LabelAt"/>.</summary>
        public const string LabelColor = "#52514E";

        public const string Label = "N";
        public const double LabelSize = 14;

        /// <summary>The arrow's length tail to tip (ft): 0.12 of the building's size (its largest of length, width and height).</summary>
        public double Length { get; private set; }

        /// <summary>The shaft, a strip from the tail (counter-clockwise, plan ft).</summary>
        public IReadOnlyList<Point2> Shaft { get; private set; }

        /// <summary>The head, a triangle (counter-clockwise, plan ft): the tip is its last point.</summary>
        public IReadOnlyList<Point2> Head { get; private set; }

        public Point2 Tail { get; private set; }
        public Point2 Tip => Head[2];

        /// <summary>Where the N goes (plan ft): a little past the tip.</summary>
        public Point2 LabelAt { get; private set; }

        /// <summary>
        /// The arrow off the north-west corner of <paramref name="bounds"/> (plan ft), sized by the largest of its length, width
        /// and <paramref name="height"/> (ft).
        /// </summary>
        public static NorthArrow For(Rect2 bounds, double height = 0)
        {
            double size = Math.Max(Math.Max(bounds.X1 - bounds.X0, bounds.Y1 - bounds.Y0), height);
            if (!(size > 0)) size = 10;
            double a = 0.12 * size;
            double cx = bounds.X0 - 0.3 * size, cy = bounds.Y1 + 0.12 * size;
            double hw = 0.06 * a;   // the shaft 0.12 a wide
            return new NorthArrow
            {
                Length = a,
                Tail = new Point2(cx, cy),
                Shaft = new[] { new Point2(cx - hw, cy), new Point2(cx + hw, cy), new Point2(cx + hw, cy + 0.6 * a), new Point2(cx - hw, cy + 0.6 * a) },
                Head = new[] { new Point2(cx - 0.3 * a, cy + 0.6 * a), new Point2(cx + 0.3 * a, cy + 0.6 * a), new Point2(cx, cy + a) },
                LabelAt = new Point2(cx, cy + 1.25 * a),
            };
        }

        /// <summary>The arrow for a building: off its footprint's north-west corner, sized by the footprint and the ridge.</summary>
        public static NorthArrow For(Building b)
        {
            var fp = b?.Footprint();
            if (fp == null || fp.IsEmpty) return null;
            return For(fp.Bounds, b.RidgeHeight());
        }

        /// <summary>
        /// The same arrow <paramref name="k"/> times as big, from the same tail (for a small view, where the usual size is a few
        /// pixels). The shape stays this one.
        /// </summary>
        public NorthArrow Scaled(double k)
        {
            if (!(k > 0) || k == 1) return this;
            Point2 S(Point2 p) => new Point2(Tail.X + k * (p.X - Tail.X), Tail.Y + k * (p.Y - Tail.Y));
            return new NorthArrow
            {
                Length = k * Length,
                Tail = Tail,
                Shaft = Shaft.Select(S).ToArray(),
                Head = Head.Select(S).ToArray(),
                LabelAt = S(LabelAt),
            };
        }

        /// <summary>Every point of the arrow and its N (plan ft), for a view to keep them in when it zooms to the drawing.</summary>
        public Rect2 Extent()
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            void Take(Point2 p) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            foreach (var p in Shaft) Take(p);
            foreach (var p in Head) Take(p);
            Take(new Point2(LabelAt.X, LabelAt.Y + 0.15 * Length));
            return new Rect2(x0, y0, x1, y1);
        }
    }
}
