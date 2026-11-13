using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingModel.Geometry
{
    /// <summary>
    /// A convex polygon in plan (ft), counterclockwise: the roof's faces are made of them (each a section's plane where it is
    /// the lowest of its section's planes, less where another section's roof is higher). Clipped by half-planes
    /// a x + b y + c ≥ 0 (Sutherland-Hodgman); empty when it has no area left.
    /// </summary>
    public sealed class Convex
    {
        public const double Eps = 1e-7;
        public List<Point2> P { get; } = new List<Point2>();

        public static Convex Of(Rect2 r)
        {
            var c = new Convex();
            c.P.AddRange(new[] { new Point2(r.X0, r.Y0), new Point2(r.X1, r.Y0), new Point2(r.X1, r.Y1), new Point2(r.X0, r.Y1) });
            return c;
        }

        public static Convex Of(IEnumerable<Point2> pts)
        {
            var c = new Convex();
            c.P.AddRange(pts);
            return c;
        }

        public double Area
        {
            get
            {
                double s = 0;
                for (int i = 0; i < P.Count; i++) { var a = P[i]; var b = P[(i + 1) % P.Count]; s += a.X * b.Y - b.X * a.Y; }
                return Math.Abs(s) / 2;
            }
        }

        /// <summary>Its centroid (the area's; a degenerate one: the vertices' mean).</summary>
        public Point2 Centroid
        {
            get
            {
                double s = 0, cx = 0, cy = 0;
                for (int i = 0; i < P.Count; i++)
                {
                    var a = P[i]; var b = P[(i + 1) % P.Count];
                    double k = a.X * b.Y - b.X * a.Y;
                    s += k; cx += (a.X + b.X) * k; cy += (a.Y + b.Y) * k;
                }
                if (Math.Abs(s) < 1e-12) return P.Count == 0 ? new Point2() : new Point2(P.Average(p => p.X), P.Average(p => p.Y));
                return new Point2(cx / (3 * s), cy / (3 * s));
            }
        }

        public bool IsEmpty => P.Count < 3 || Area < 1e-6;

        /// <summary>What is left with a x + b y + c ≥ 0.</summary>
        public Convex Clip(double a, double b, double c)
        {
            var o = new Convex();
            int n = P.Count;
            for (int i = 0; i < n; i++)
            {
                var p = P[i]; var q = P[(i + 1) % n];
                double fp = a * p.X + b * p.Y + c, fq = a * q.X + b * q.Y + c;
                if (fp >= -Eps) o.P.Add(p);
                if ((fp >= -Eps) != (fq >= -Eps) && Math.Abs(fp - fq) > 1e-15)
                {
                    double t = fp / (fp - fq);
                    o.P.Add(new Point2(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
                }
            }
            // drop repeated points
            for (int i = o.P.Count - 1; i >= 0 && o.P.Count > 1; i--)
            {
                var a1 = o.P[i]; var b1 = o.P[(i + 1) % o.P.Count];
                if (Math.Abs(a1.X - b1.X) < 1e-9 && Math.Abs(a1.Y - b1.Y) < 1e-9) o.P.RemoveAt(i);
            }
            return o;
        }

        /// <summary>Clipped to a rectangle.</summary>
        public Convex Clip(Rect2 r) => Clip(1, 0, -r.X0).Clip(-1, 0, r.X1).Clip(0, 1, -r.Y0).Clip(0, -1, r.Y1);

        /// <summary>The half-planes (a, b, c: a x + b y + c ≥ 0) bounding it, one an edge.</summary>
        public List<(double a, double b, double c)> Edges()
        {
            var list = new List<(double, double, double)>();
            int n = P.Count;
            // counterclockwise or not: the inside is the side of the centroid
            var m = Centroid;
            for (int i = 0; i < n; i++)
            {
                var p = P[i]; var q = P[(i + 1) % n];
                double a = -(q.Y - p.Y), b = q.X - p.X, len = Math.Sqrt(a * a + b * b);
                if (len < 1e-12) continue;
                a /= len; b /= len;
                double c = -(a * p.X + b * p.Y);
                if (a * m.X + b * m.Y + c < 0) { a = -a; b = -b; c = -c; }
                list.Add((a, b, c));
            }
            return list;
        }

        /// <summary>
        /// The part of the line p + t d inside it (grown by <paramref name="grow"/> ft each way): its t range, or null when the
        /// line misses it.
        /// </summary>
        public (double t0, double t1)? Along(Point2 p, Point2 d, double grow = 0)
        {
            double t0 = double.NegativeInfinity, t1 = double.PositiveInfinity;
            foreach (var (a, b, c) in Edges())
            {
                double f = a * p.X + b * p.Y + c + grow, g = a * d.X + b * d.Y;
                if (Math.Abs(g) < 1e-12) { if (f < 0) return null; continue; }
                double t = -f / g;
                if (g > 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t);
            }
            return t1 > t0 ? (t0, t1) : ((double, double)?)null;
        }

        /// <summary>
        /// Its width across a member's run at <paramref name="u"/> along it: a run along x (<paramref name="alongX"/>), its extent
        /// in y on the line x = u; along y, its extent in x on the line y = u.
        /// </summary>
        public double ChordAt(bool alongX, double u)
        {
            var r = alongX ? Along(new Point2(u, 0), new Point2(0, 1)) : Along(new Point2(0, u), new Point2(1, 0));
            return r == null ? 0 : r.Value.t1 - r.Value.t0;
        }

        /// <summary>
        /// This polygon less a convex region given by its half-planes (each a x + b y + c &gt; 0 inside it): the pieces left, each
        /// convex (outside the first, or inside it and outside the second, ...).
        /// </summary>
        public List<Convex> Minus(IList<(double a, double b, double c)> region)
        {
            var list = new List<Convex>();
            var rest = this;
            foreach (var (a, b, c) in region)
            {
                var outside = rest.Clip(-a, -b, -c);
                if (!outside.IsEmpty) list.Add(outside);
                rest = rest.Clip(a, b, c);
                if (rest.IsEmpty) return list;
            }
            return list; // the rest is inside the region: gone
        }

        public override string ToString() => string.Join(" ", P.Select(p => $"({p.X:0.##},{p.Y:0.##})"));
    }
}
