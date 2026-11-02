using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Edits
{
    /// <summary>The object snaps (like AutoCAD OSNAP). Several can be on at once.</summary>
    [Flags]
    public enum SnapModes
    {
        None = 0,
        Endpoint = 1,
        Midpoint = 2,
        Intersection = 4,
        Perpendicular = 8,
        Nearest = 16,
        Grid = 32,
        Default = Endpoint | Midpoint | Intersection | Perpendicular | Grid,
    }

    /// <summary>The snap found under the cursor: one mode, or None (the point is then the cursor).</summary>
    public struct SnapResult
    {
        public SnapModes Kind;
        public Point2 Point;
        public bool Found => Kind != SnapModes.None;

        public static string Describe(SnapModes kind) => kind == SnapModes.None ? "" : kind.ToString();
    }

    /// <summary>
    /// Finds the snap nearest the cursor. Within the reach (ft), the kinds rank: endpoint, intersection, midpoint,
    /// perpendicular (needs the point the line starts from), nearest, grid. Among candidates of one kind the closest wins.
    /// </summary>
    public static class Snapper
    {
        private static readonly SnapModes[] Order =
            { SnapModes.Endpoint, SnapModes.Intersection, SnapModes.Midpoint, SnapModes.Perpendicular, SnapModes.Nearest, SnapModes.Grid };

        public static SnapResult Find(Point2 cursor, double reach, IList<Segment2> segments, SnapModes modes, Point2? from = null, double grid = 0)
        {
            if (modes == SnapModes.None || !(reach > 0)) return new SnapResult { Point = cursor };
            segments = segments ?? new List<Segment2>();
            foreach (var kind in Order)
            {
                if ((modes & kind) == 0) continue;
                Point2? best = null;
                double bestD = reach;
                foreach (var p in Candidates(kind, cursor, segments, from, grid))
                {
                    double d = Dist(p, cursor);
                    if (d <= bestD) { bestD = d; best = p; }
                }
                if (best.HasValue) return new SnapResult { Kind = kind, Point = best.Value };
            }
            return new SnapResult { Point = cursor };
        }

        /// <summary>The lines of a level that can be snapped to: its walls, the edges of its diaphragms and openings, and the walls above.</summary>
        public static List<Segment2> SegmentsOf(Building b, Level level)
        {
            var list = new List<Segment2>();
            foreach (var w in b.WallsUnder(level).Concat(b.WallsOver(level)).Distinct()) list.Add(new Segment2(w.Start, w.End));
            foreach (var r in b.DiaphragmsOn(level).SelectMany(d => d.Pieces).Concat(b.OpeningsCutting(level).SelectMany(o => o.Pieces)))
                list.AddRange(Edges(r));
            var below = b.Below(level);
            if (below != null) foreach (var w in b.WallsUnder(below)) list.Add(new Segment2(w.Start, w.End));
            return list;
        }

        public static IEnumerable<Segment2> Edges(Rect2 r)
        {
            yield return new Segment2(new Point2(r.X0, r.Y0), new Point2(r.X1, r.Y0));
            yield return new Segment2(new Point2(r.X1, r.Y0), new Point2(r.X1, r.Y1));
            yield return new Segment2(new Point2(r.X1, r.Y1), new Point2(r.X0, r.Y1));
            yield return new Segment2(new Point2(r.X0, r.Y1), new Point2(r.X0, r.Y0));
        }

        private static IEnumerable<Point2> Candidates(SnapModes kind, Point2 cursor, IList<Segment2> segments, Point2? from, double grid)
        {
            switch (kind)
            {
                case SnapModes.Endpoint:
                    foreach (var s in segments) { yield return s.A; yield return s.B; }
                    break;
                case SnapModes.Midpoint:
                    foreach (var s in segments) yield return new Point2((s.A.X + s.B.X) / 2, (s.A.Y + s.B.Y) / 2);
                    break;
                case SnapModes.Intersection:
                    for (int i = 0; i < segments.Count; i++)
                        for (int j = i + 1; j < segments.Count; j++)
                            if (Intersect(segments[i], segments[j], out var x)) yield return x;
                    break;
                case SnapModes.Perpendicular:
                    if (from.HasValue)
                        foreach (var s in segments)
                            if (FootOnSegment(from.Value, s, out var f)) yield return f;
                    break;
                case SnapModes.Nearest:
                    foreach (var s in segments) yield return ClosestOnSegment(cursor, s);
                    break;
                case SnapModes.Grid:
                    if (grid > 0) yield return new Point2(Math.Round(cursor.X / grid) * grid, Math.Round(cursor.Y / grid) * grid);
                    break;
            }
        }

        public static double Dist(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>The crossing point of two segments (ends included). Parallel segments have none.</summary>
        public static bool Intersect(Segment2 s, Segment2 t, out Point2 p)
        {
            p = default(Point2);
            double rx = s.B.X - s.A.X, ry = s.B.Y - s.A.Y, qx = t.B.X - t.A.X, qy = t.B.Y - t.A.Y;
            double den = rx * qy - ry * qx;
            if (Math.Abs(den) < 1e-12) return false;
            double wx = t.A.X - s.A.X, wy = t.A.Y - s.A.Y;
            double u = (wx * qy - wy * qx) / den, v = (wx * ry - wy * rx) / den;
            const double e = 1e-9;
            if (u < -e || u > 1 + e || v < -e || v > 1 + e) return false;
            p = new Point2(s.A.X + u * rx, s.A.Y + u * ry);
            return true;
        }

        /// <summary>The foot of the perpendicular from a point to the segment, when it falls on the segment.</summary>
        public static bool FootOnSegment(Point2 p, Segment2 s, out Point2 foot)
        {
            double dx = s.B.X - s.A.X, dy = s.B.Y - s.A.Y, len2 = dx * dx + dy * dy;
            foot = s.A;
            if (len2 < 1e-18) return false;
            double u = ((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy) / len2;
            if (u < 0 || u > 1) return false;
            foot = new Point2(s.A.X + u * dx, s.A.Y + u * dy);
            return true;
        }

        public static Point2 ClosestOnSegment(Point2 p, Segment2 s)
        {
            double dx = s.B.X - s.A.X, dy = s.B.Y - s.A.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-18) return s.A;
            double u = Math.Max(0, Math.Min(1, ((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy) / len2));
            return new Point2(s.A.X + u * dx, s.A.Y + u * dy);
        }

        public static double DistanceToSegment(Point2 p, Segment2 s) => Dist(p, ClosestOnSegment(p, s));

        /// <summary>The point squared to <paramref name="from"/>: straight across or straight up, whichever is closer.</summary>
        public static Point2 Ortho(Point2 p, Point2 from) =>
            Math.Abs(p.X - from.X) >= Math.Abs(p.Y - from.Y) ? new Point2(p.X, from.Y) : new Point2(from.X, p.Y);

        /// <summary>
        /// What a click on a level's plan picks, in this order: a note, the closest wall within the reach, an opening
        /// containing the point, the smallest diaphragm containing it. The ID, or null.
        /// </summary>
        public static string Pick(Building b, Level level, Point2 p, double reach)
        {
            var note = b.NotesOn(level).Select(n => (n.Id, d: Dist(n.At, p))).Where(x => x.d <= reach * 1.5).OrderBy(x => x.d).Select(x => x.Id).FirstOrDefault();
            if (note != null) return note;
            var wall = b.WallsUnder(level).Select(w => (w.Id, d: DistanceToSegment(p, new Segment2(w.Start, w.End))))
                        .Where(x => x.d <= reach).OrderBy(x => x.d).Select(x => x.Id).FirstOrDefault();
            if (wall != null) return wall;
            var opening = b.OpeningsCutting(level).Where(o => o.Pieces.Any(r => r.Contains(p.X, p.Y)))
                           .OrderBy(o => o.Pieces.Sum(r => r.Area)).Select(o => o.Id).FirstOrDefault();
            if (opening != null) return opening;
            return b.DiaphragmsOn(level).Where(d => d.Pieces.Any(r => r.Contains(p.X, p.Y)))
                    .OrderBy(d => d.Pieces.Sum(r => r.Area)).Select(d => d.Id).FirstOrDefault();
        }

        /// <summary>The objects of a level wholly inside a window (a selection box).</summary>
        public static List<string> InWindow(Building b, Level level, Rect2 box)
        {
            bool In(Point2 p) => box.Contains(p.X, p.Y);
            var ids = new List<string>();
            ids.AddRange(b.WallsUnder(level).Where(w => In(w.Start) && In(w.End)).Select(w => w.Id));
            ids.AddRange(b.DiaphragmsOn(level).Where(d => d.Pieces.All(r => In(new Point2(r.X0, r.Y0)) && In(new Point2(r.X1, r.Y1)))).Select(d => d.Id));
            ids.AddRange(b.OpeningsCutting(level).Where(o => o.Pieces.All(r => In(new Point2(r.X0, r.Y0)) && In(new Point2(r.X1, r.Y1)))).Select(o => o.Id));
            ids.AddRange(b.NotesOn(level).Where(n => In(n.At)).Select(n => n.Id));
            return ids;
        }
    }
}
