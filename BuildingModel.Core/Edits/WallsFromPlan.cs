using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Edits
{
    /// <summary>What the line a wall is drawn along is: its centreline (the model's), or one of its faces.</summary>
    public enum WallLine
    {
        Centre,
        /// <summary>The drawn line is the wall's left face, going from the first point to the second: the wall lies to its right.</summary>
        LeftFace,
        /// <summary>The drawn line is the wall's right face: the wall lies to its left.</summary>
        RightFace,
    }

    /// <summary>
    /// Walls from a plan that shows their faces, not their centrelines (CAD plans): a wall drawn along a face
    /// (<see cref="CentreOf"/>), and the walls proposed from a CAD layer's pairs of lines (<see cref="Propose"/>).
    /// </summary>
    public static class WallsFromPlan
    {
        /// <summary>
        /// The centreline of a wall <paramref name="thickness"/> (ft) thick drawn from <paramref name="a"/> to
        /// <paramref name="b"/> along <paramref name="by"/>: half the thickness to the side the wall lies on.
        /// </summary>
        public static Segment2 CentreOf(Point2 a, Point2 b, WallLine by, double thickness)
        {
            double len = Snapper.Dist(a, b);
            if (by == WallLine.Centre || len < 1e-12 || !(thickness > 0)) return new Segment2(a, b);
            double ux = (b.X - a.X) / len, uy = (b.Y - a.Y) / len;
            // the right-hand normal (uy, -ux): a left face has the wall to its right
            double s = (by == WallLine.LeftFace ? 1 : -1) * thickness / 2;
            return new Segment2(new Point2(a.X + uy * s, a.Y - ux * s), new Point2(b.X + uy * s, b.Y - ux * s));
        }

        /// <summary>
        /// Where the centreline of the next wall of a run (<paramref name="next"/>) meets the previous one's
        /// (<paramref name="previous"/>), both square: the crossing of their lines when they turn a corner, else the
        /// previous one's end. Drawn by a face, the two centrelines are each offset, so the corner is found again.
        /// </summary>
        public static Point2 RunJoint(Segment2 previous, Segment2 next)
        {
            bool prevEw = Math.Abs(previous.B.X - previous.A.X) >= Math.Abs(previous.B.Y - previous.A.Y);
            bool nextEw = Math.Abs(next.B.X - next.A.X) >= Math.Abs(next.B.Y - next.A.Y);
            if (prevEw == nextEw) return previous.B;
            return prevEw ? new Point2(next.A.X, previous.B.Y) : new Point2(previous.B.X, next.A.Y);
        }

        /// <summary>How close two proposed walls' ends must be to be taken as one wall (ft), and the shortest proposed (ft).</summary>
        public const double JoinGap = 0.1, ShortestWall = 1.0;

        /// <summary>
        /// The walls a CAD layer's lines show (<paramref name="lines"/>, world ft): each pair of parallel lines a stud wall
        /// apart (<see cref="Snapper.WallCentres"/>) gives its centreline. Only square walls (east-west or north-south): the
        /// pieces of one wall on one line are joined, each run on to where it meets another wall's centre (a corner or a
        /// tee: each pair stops at the other wall's face). Those already in the model (<paramref name="existing"/>, on the
        /// same line and overlapping) and those shorter than <see cref="ShortestWall"/> are left out. West to east, south
        /// to north.
        /// </summary>
        public static List<Segment2> Propose(IList<Segment2> lines, IEnumerable<Segment2> existing = null)
        {
            const double square = 0.0175; // 1°
            var centres = new List<(bool ew, double at, double lo, double hi)>();
            foreach (var c in Snapper.WallCentres(lines))
            {
                double dx = c.B.X - c.A.X, dy = c.B.Y - c.A.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                if (Math.Abs(dy) / len < square) centres.Add((true, (c.A.Y + c.B.Y) / 2, Math.Min(c.A.X, c.B.X), Math.Max(c.A.X, c.B.X)));
                else if (Math.Abs(dx) / len < square) centres.Add((false, (c.A.X + c.B.X) / 2, Math.Min(c.A.Y, c.B.Y), Math.Max(c.A.Y, c.B.Y)));
            }

            // one wall per line: the pieces on a line (within a hair) that touch or overlap joined
            var walls = new List<(bool ew, double at, double lo, double hi)>();
            foreach (var g in centres.OrderBy(c => c.ew ? 0 : 1).ThenBy(c => c.at).ThenBy(c => c.lo))
            {
                int k = walls.FindIndex(w => w.ew == g.ew && Math.Abs(w.at - g.at) < 0.02 && g.lo <= w.hi + JoinGap && g.hi >= w.lo - JoinGap);
                if (k < 0) walls.Add(g);
                else walls[k] = (g.ew, walls[k].at, Math.Min(walls[k].lo, g.lo), Math.Max(walls[k].hi, g.hi));
            }

            // run each end on to another wall's centre it nearly meets (a corner, a tee)
            double reach = (Snapper.WallLinesMaxIn + Snapper.WallLinesToleranceIn) / 12;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                foreach (var o in walls.Where(o => o.ew != w.ew))
                {
                    // the other wall crosses this one's line at o.at (along this wall), if this wall's line is within its span
                    if (w.at < o.lo - reach || w.at > o.hi + reach) continue;
                    if (o.at < w.lo && w.lo - o.at <= reach) w.lo = o.at;
                    if (o.at > w.hi && o.at - w.hi <= reach) w.hi = o.at;
                }
                walls[i] = w;
            }

            var have = (existing ?? Enumerable.Empty<Segment2>()).ToList();
            bool Exists((bool ew, double at, double lo, double hi) w) => have.Any(e =>
            {
                bool eEw = Math.Abs(e.B.X - e.A.X) >= Math.Abs(e.B.Y - e.A.Y);
                if (eEw != w.ew) return false;
                double at = w.ew ? (e.A.Y + e.B.Y) / 2 : (e.A.X + e.B.X) / 2;
                double lo = w.ew ? Math.Min(e.A.X, e.B.X) : Math.Min(e.A.Y, e.B.Y), hi = w.ew ? Math.Max(e.A.X, e.B.X) : Math.Max(e.A.Y, e.B.Y);
                return Math.Abs(at - w.at) < 0.25 && Math.Min(hi, w.hi) - Math.Max(lo, w.lo) > JoinGap;
            });

            return walls.Where(w => w.hi - w.lo >= ShortestWall && !Exists(w))
                        .OrderBy(w => w.ew ? 0 : 1).ThenBy(w => w.at).ThenBy(w => w.lo)
                        .Select(w => w.ew ? new Segment2(new Point2(w.lo, w.at), new Point2(w.hi, w.at)) : new Segment2(new Point2(w.at, w.lo), new Point2(w.at, w.hi)))
                        .ToList();
        }
    }
}
