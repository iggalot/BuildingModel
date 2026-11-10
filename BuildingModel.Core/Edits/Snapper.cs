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
        /// <summary>A single point: a column, a beam end, a hold-down (the host's points), ranked first.</summary>
        Point = 64,
        /// <summary>
        /// Midway between a wall's two lines on a CAD plan (two parallel lines a stud wall apart, <see cref="Snapper.WallLinesMinIn"/>
        /// to <see cref="Snapper.WallLinesMaxIn"/>): plans show a wall's faces, the model its centreline.
        /// </summary>
        WallCentre = 128,
        Default = Point | WallCentre | Endpoint | Midpoint | Intersection | Perpendicular | Grid,
    }

    /// <summary>A point to snap to, with what it is ("C1", "Hold-down W3").</summary>
    public struct SnapPoint
    {
        public Point2 At;
        public string Label;
        public SnapPoint(Point2 at, string label) { At = at; Label = label; }
    }

    /// <summary>The snap found under the cursor: one mode, or None (the point is then the cursor).</summary>
    public struct SnapResult
    {
        public SnapModes Kind;
        public Point2 Point;
        /// <summary>What a point snap is on ("C1"); null for the others.</summary>
        public string Label;
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
            { SnapModes.Point, SnapModes.WallCentre, SnapModes.Endpoint, SnapModes.Intersection, SnapModes.Midpoint, SnapModes.Perpendicular, SnapModes.Nearest, SnapModes.Grid };

        /// <summary>How far apart a CAD plan's two lines of one wall are (in): a 2x4's 3 1/2 to a 2x6 wall sheathed, 7 1/4.</summary>
        public const double WallLinesMinIn = 3.5, WallLinesMaxIn = 7.25, WallLinesToleranceIn = 0.25;

        /// <summary>
        /// The snap nearest <paramref name="cursor"/> within <paramref name="reach"/> (ft). <paramref name="planLines"/> are a CAD
        /// plan's own lines near the cursor: the wall centres are found among them only (<see cref="WallCentres"/>), never
        /// among the model's lines.
        /// </summary>
        public static SnapResult Find(Point2 cursor, double reach, IList<Segment2> segments, SnapModes modes, Point2? from = null, double grid = 0, IList<SnapPoint> points = null,
                                      IList<Segment2> planLines = null)
        {
            if (modes == SnapModes.None || !(reach > 0)) return new SnapResult { Point = cursor };
            segments = segments ?? new List<Segment2>();
            foreach (var kind in Order)
            {
                if ((modes & kind) == 0) continue;
                if (kind == SnapModes.Point)
                {
                    if (points == null || points.Count == 0) continue;
                    var near = points.Select(p => (p, d: Dist(p.At, cursor))).Where(x => x.d <= reach).OrderBy(x => x.d).ToList();
                    if (near.Count > 0) return new SnapResult { Kind = kind, Point = near[0].p.At, Label = near[0].p.Label };
                    continue;
                }
                if (kind == SnapModes.WallCentre)
                {
                    if (planLines == null || planLines.Count < 2) continue;
                    var centre = WallCentreNear(cursor, reach, planLines);
                    if (centre != null) return centre.Value;
                    continue;
                }
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
        public static List<Segment2> SegmentsOf(Building b, Level level, ICollection<string> except = null)
        {
            bool Keep(ModelObject o) => except == null || !except.Contains(o.Id);
            var list = new List<Segment2>();
            foreach (var w in b.WallsUnder(level).Concat(b.WallsOver(level)).Distinct().Where(Keep)) list.Add(new Segment2(w.Start, w.End));
            foreach (var r in b.DiaphragmsOn(level).Where(Keep).SelectMany(d => d.Pieces).Concat(b.OpeningsCutting(level).Where(Keep).SelectMany(o => o.Pieces)))
                list.AddRange(Edges(r));
            var below = b.Below(level);
            if (below != null) foreach (var w in b.WallsUnder(below).Where(Keep)) list.Add(new Segment2(w.Start, w.End));
            foreach (var bm in b.BeamsOn(level).Where(Keep).Where(x => x.Length > 0)) list.Add(new Segment2(bm.Start, bm.End));
            return list;
        }

        /// <summary>
        /// The single points of a level to snap to: the columns that reach it from below, stand on it or pass through it, and the
        /// ends of its beams.
        /// </summary>
        public static List<SnapPoint> PointsOf(Building b, Level level, ICollection<string> except = null)
        {
            var list = new List<SnapPoint>();
            if (level == null) return list;
            // the columns at this level, and those of the story below (to stack a column on one, or put a beam end on it)
            double from = b.Below(level)?.Elevation ?? level.Elevation;
            foreach (var c in b.Columns)
            {
                if (except != null && except.Contains(c.Id)) continue;
                var lo = b.Level(c.BaseLevelId);
                var hi = b.Level(c.TopLevelId);
                if (lo == null || hi == null || lo.Elevation > level.Elevation || hi.Elevation < from) continue;
                list.Add(new SnapPoint(c.At, hi.Elevation < level.Elevation ? c.Id + " (below)" : c.Id));
            }
            foreach (var bm in b.BeamsOn(level))
            {
                if (except != null && except.Contains(bm.Id)) continue;
                list.Add(new SnapPoint(bm.Start, bm.Id + " end"));
                list.Add(new SnapPoint(bm.End, bm.Id + " end"));
            }
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

        /// <summary>
        /// The centrelines of the walls a CAD plan draws as two lines (<paramref name="lines"/>): each pair of parallel lines
        /// <see cref="WallLinesMinIn"/> to <see cref="WallLinesMaxIn"/> apart (± <see cref="WallLinesToleranceIn"/>) gives the
        /// line midway between them, along the stretch where both run.
        /// </summary>
        public static List<Segment2> WallCentres(IList<Segment2> lines)
        {
            var list = new List<Segment2>();
            if (lines == null) return list;
            double min = (WallLinesMinIn - WallLinesToleranceIn) / 12, max = (WallLinesMaxIn + WallLinesToleranceIn) / 12;
            for (int i = 0; i < lines.Count; i++)
                for (int j = i + 1; j < lines.Count; j++)
                {
                    var s = lines[i];
                    var t = lines[j];
                    double len = Dist(s.A, s.B);
                    if (len < 1e-9 || Dist(t.A, t.B) < 1e-9) continue;
                    double ux = (s.B.X - s.A.X) / len, uy = (s.B.Y - s.A.Y) / len, nx = -uy, ny = ux;
                    double tx = t.B.X - t.A.X, ty = t.B.Y - t.A.Y, tl = Math.Sqrt(tx * tx + ty * ty);
                    if (Math.Abs(ux * ty - uy * tx) / tl > 0.0175) continue; // not parallel (1°)
                    double offA = (t.A.X - s.A.X) * nx + (t.A.Y - s.A.Y) * ny, offB = (t.B.X - s.A.X) * nx + (t.B.Y - s.A.Y) * ny;
                    double off = (offA + offB) / 2;
                    if (Math.Abs(off) < min || Math.Abs(off) > max) continue;
                    double a = (t.A.X - s.A.X) * ux + (t.A.Y - s.A.Y) * uy, b = (t.B.X - s.A.X) * ux + (t.B.Y - s.A.Y) * uy;
                    double lo = Math.Max(0, Math.Min(a, b)), hi = Math.Min(len, Math.Max(a, b));
                    if (hi - lo < 0.1) continue; // they do not run side by side
                    Point2 At(double along) => new Point2(s.A.X + ux * along + nx * off / 2, s.A.Y + uy * along + ny * off / 2);
                    list.Add(new Segment2(At(lo), At(hi)));
                }
            return list;
        }

        /// <summary>
        /// The wall-centre snap near <paramref name="cursor"/>: where two centrelines meet (a corner or a tee, the lines run on
        /// to it: at a corner each wall's pair stops at the other's face), else a centreline's end, else the nearest point on one.
        /// </summary>
        private static SnapResult? WallCentreNear(Point2 cursor, double reach, IList<Segment2> planLines)
        {
            var centres = WallCentres(planLines);
            if (centres.Count == 0) return null;
            SnapResult? Best(IEnumerable<Point2> candidates, string label)
            {
                var near = candidates.Select(p => (p, d: Dist(p, cursor))).Where(x => x.d <= reach).OrderBy(x => x.d).ToList();
                return near.Count == 0 ? (SnapResult?)null : new SnapResult { Kind = SnapModes.WallCentre, Point = near[0].p, Label = label };
            }
            double run = (WallLinesMaxIn + WallLinesToleranceIn) / 12; // how far a centreline runs on past its pair's end to meet another
            var meets = new List<Point2>();
            for (int i = 0; i < centres.Count; i++)
                for (int j = i + 1; j < centres.Count; j++)
                    if (LinesCross(centres[i], centres[j], out var x) && DistanceToSegment(x, centres[i]) <= run && DistanceToSegment(x, centres[j]) <= run)
                        meets.Add(x);
            return Best(meets, "Wall centres meet")
                ?? Best(centres.SelectMany(c => new[] { c.A, c.B }), "Wall centre end")
                ?? Best(centres.Select(c => ClosestOnSegment(cursor, c)), "Wall centre");
        }

        /// <summary>Where the lines through two segments cross (not parallel), wherever that is.</summary>
        private static bool LinesCross(Segment2 s, Segment2 t, out Point2 p)
        {
            p = default(Point2);
            double rx = s.B.X - s.A.X, ry = s.B.Y - s.A.Y, qx = t.B.X - t.A.X, qy = t.B.Y - t.A.Y;
            double den = rx * qy - ry * qx;
            if (Math.Abs(den) < 1e-9 * Math.Sqrt((rx * rx + ry * ry) * (qx * qx + qy * qy))) return false;
            double u = ((t.A.X - s.A.X) * qy - (t.A.Y - s.A.Y) * qx) / den;
            p = new Point2(s.A.X + u * rx, s.A.Y + u * ry);
            return true;
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
            var column = b.ColumnsUnder(level).Concat(b.ColumnsStandingOn(level)).Select(c => (c.Id, d: Dist(c.At, p))).Where(x => x.d <= reach * 1.5).OrderBy(x => x.d).Select(x => x.Id).FirstOrDefault();
            if (column != null) return column;
            var beam = b.BeamsOn(level).Select(x => (x.Id, d: DistanceToSegment(p, new Segment2(x.Start, x.End))))
                        .Where(x => x.d <= reach).OrderBy(x => x.d).Select(x => x.Id).FirstOrDefault();
            if (beam != null) return beam;
            var wall = b.WallsShown(level).Select(w => (w.Id, d: DistanceToSegment(p, new Segment2(w.Start, w.End))))
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
            ids.AddRange(b.WallsShown(level).Where(w => In(w.Start) && In(w.End)).Select(w => w.Id));
            ids.AddRange(b.DiaphragmsOn(level).Where(d => d.Pieces.All(r => In(new Point2(r.X0, r.Y0)) && In(new Point2(r.X1, r.Y1)))).Select(d => d.Id));
            ids.AddRange(b.OpeningsCutting(level).Where(o => o.Pieces.All(r => In(new Point2(r.X0, r.Y0)) && In(new Point2(r.X1, r.Y1)))).Select(o => o.Id));
            ids.AddRange(b.NotesOn(level).Where(n => In(n.At)).Select(n => n.Id));
            ids.AddRange(b.BeamsOn(level).Where(x => In(x.Start) && In(x.End)).Select(x => x.Id));
            ids.AddRange(b.ColumnsUnder(level).Concat(b.ColumnsStandingOn(level)).Where(c => In(c.At)).Select(c => c.Id));
            return ids;
        }
    }
}
