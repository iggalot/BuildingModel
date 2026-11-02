using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingModel.Geometry
{
    /// <summary>
    /// An area in plan made of right-angled pieces: the union of some rectangles minus others (a diaphragm with its
    /// openings). Worked on a grid through every rectangle edge: each grid cell is wholly in or wholly out, so the area,
    /// centroid and outline are exact.
    /// </summary>
    public sealed class Region
    {
        private readonly List<Rect2> added;
        private readonly List<Rect2> removed;
        private readonly double[] xs, ys;
        private readonly bool[,] cells; // [i, j]: the cell xs[i]..xs[i+1] by ys[j]..ys[j+1] is in the region

        private Region(IEnumerable<Rect2> add, IEnumerable<Rect2> remove)
        {
            added = (add ?? Enumerable.Empty<Rect2>()).Where(r => !r.IsEmpty).ToList();
            removed = (remove ?? Enumerable.Empty<Rect2>()).Where(r => !r.IsEmpty).ToList();
            var all = added.Concat(removed).ToList();
            xs = all.SelectMany(r => new[] { r.X0, r.X1 }).Distinct().OrderBy(v => v).ToArray();
            ys = all.SelectMany(r => new[] { r.Y0, r.Y1 }).Distinct().OrderBy(v => v).ToArray();
            int nx = Math.Max(0, xs.Length - 1), ny = Math.Max(0, ys.Length - 1);
            cells = new bool[nx, ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    double cx = (xs[i] + xs[i + 1]) / 2, cy = (ys[j] + ys[j + 1]) / 2;
                    cells[i, j] = added.Any(r => r.Contains(cx, cy)) && !removed.Any(r => r.Contains(cx, cy));
                }
        }

        /// <summary>The union of these rectangles.</summary>
        public static Region Of(IEnumerable<Rect2> rects) => new Region(rects, null);

        public static readonly Region Empty = new Region(null, null);

        /// <summary>This region with these rectangles taken out.</summary>
        public Region Minus(IEnumerable<Rect2> holes) => new Region(added, removed.Concat(holes ?? Enumerable.Empty<Rect2>()));

        /// <summary>The union of this region and another (both as they are, holes kept).</summary>
        public Region Union(Region other)
        {
            if (other == null || other.IsEmpty) return this;
            if (IsEmpty) return other;
            return Of(Cells().Concat(other.Cells()));
        }

        /// <summary>The region's in cells as rectangles (the pieces it is made of, no overlaps).</summary>
        public IEnumerable<Rect2> Cells()
        {
            for (int i = 0; i < cells.GetLength(0); i++)
                for (int j = 0; j < cells.GetLength(1); j++)
                    if (cells[i, j]) yield return new Rect2(xs[i], ys[j], xs[i + 1], ys[j + 1]);
        }

        public double Area => Cells().Sum(c => c.Area);

        public bool IsEmpty => !(Area > 0);

        /// <summary>Center of the area (NaN when empty).</summary>
        public Point2 Centroid
        {
            get
            {
                double a = 0, sx = 0, sy = 0;
                foreach (var c in Cells())
                {
                    a += c.Area;
                    sx += c.Area * (c.X0 + c.X1) / 2;
                    sy += c.Area * (c.Y0 + c.Y1) / 2;
                }
                return a > 0 ? new Point2(sx / a, sy / a) : new Point2(double.NaN, double.NaN);
            }
        }

        /// <summary>The smallest rectangle around the region (empty when the region is).</summary>
        public Rect2 Bounds
        {
            get
            {
                var c = Cells().ToList();
                return c.Count == 0 ? new Rect2() : new Rect2(c.Min(r => r.X0), c.Min(r => r.Y0), c.Max(r => r.X1), c.Max(r => r.Y1));
            }
        }

        private bool In(int i, int j) => i >= 0 && j >= 0 && i < cells.GetLength(0) && j < cells.GetLength(1) && cells[i, j];

        /// <summary>True when the point is in the region or on its outline (within <paramref name="tolerance"/>).</summary>
        public bool Contains(double x, double y, double tolerance = 1e-9) => Cells().Any(c => c.DistanceTo(x, y) <= tolerance);

        /// <summary>
        /// The outline (outer edges and the edges of holes), as the longest straight pieces: every grid line where a cell
        /// on one side is in and the one on the other side is not.
        /// </summary>
        public List<Segment2> Outline()
        {
            var list = new List<Segment2>();
            int nx = cells.GetLength(0), ny = cells.GetLength(1);
            for (int i = 0; i <= nx; i++) // vertical lines x = xs[i]
            {
                int start = -1;
                for (int j = 0; j <= ny; j++)
                {
                    bool edge = j < ny && In(i - 1, j) != In(i, j);
                    if (edge && start < 0) start = j;
                    if (!edge && start >= 0)
                    {
                        list.Add(new Segment2(new Point2(xs[i], ys[start]), new Point2(xs[i], ys[j])));
                        start = -1;
                    }
                }
            }
            for (int j = 0; j <= ny; j++) // horizontal lines y = ys[j]
            {
                int start = -1;
                for (int i = 0; i <= nx; i++)
                {
                    bool edge = i < nx && In(i, j - 1) != In(i, j);
                    if (edge && start < 0) start = i;
                    if (!edge && start >= 0)
                    {
                        list.Add(new Segment2(new Point2(xs[start], ys[j]), new Point2(xs[i], ys[j])));
                        start = -1;
                    }
                }
            }
            return list;
        }

        /// <summary>The area of this region inside a rectangle.</summary>
        public double AreaWithin(Rect2 r) => Cells().Sum(c =>
        {
            double w = Math.Min(c.X1, r.X1) - Math.Max(c.X0, r.X0), h = Math.Min(c.Y1, r.Y1) - Math.Max(c.Y0, r.Y0);
            return w > 0 && h > 0 ? w * h : 0;
        });

        /// <summary>
        /// The length of a W-E or S-N line from <paramref name="a"/> to <paramref name="b"/> that lies in the region or on
        /// its outline (within <paramref name="tolerance"/>): where a diaphragm reaches a wall.
        /// </summary>
        public double LengthAlong(Point2 a, Point2 b, double tolerance = 1e-6)
        {
            bool ew = Math.Abs(b.Y - a.Y) <= Math.Abs(b.X - a.X);
            double s0 = ew ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
            double s1 = ew ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
            double line = ew ? (a.Y + b.Y) / 2 : (a.X + b.X) / 2;
            var cuts = (ew ? xs : ys).Where(v => v > s0 && v < s1).Concat(new[] { s0, s1 }).Distinct().OrderBy(v => v).ToList();
            double total = 0;
            for (int k = 0; k + 1 < cuts.Count; k++)
            {
                double m = (cuts[k] + cuts[k + 1]) / 2;
                bool on = ew ? Contains(m, line, tolerance) : Contains(line, m, tolerance);
                if (on) total += cuts[k + 1] - cuts[k];
            }
            return total;
        }
    }
}
