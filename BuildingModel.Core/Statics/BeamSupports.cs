using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Statics
{
    public enum BeamSupportKind { Wall, Column, Beam }

    /// <summary>One thing holding a beam up, where along it.</summary>
    public sealed class BeamBearing
    {
        /// <summary>"start", "end", or "at 12.5 ft" (from its start).</summary>
        public string Name { get; set; }
        /// <summary>ft from the beam's start.</summary>
        public double At { get; set; }
        public Point2 Where { get; set; }
        /// <summary>The wall, column or beam under it.</summary>
        public string On { get; set; }
        public BeamSupportKind Kind { get; set; }
    }

    /// <summary>
    /// What holds each beam of a level up, found from the geometry (one rule for every app; owner 2026-11-10): every bearing
    /// wall under it (crossing it, ending under it, under one of its ends; a wall along it, the ends of where they overlap),
    /// every column under it, and at an end with neither, a beam (one it lands on partway along, else one whose end is there).
    /// Two within <see cref="Bearing"/> of each other are one (a wall before a column); one within it of an end is that end.
    /// And whether the level's beams stand (<see cref="Unstable"/>): a beam needs two supports, or beams that rest on each
    /// other need something under them: else the model is not valid and nothing is calculated (<c>ModelChecks</c>).
    /// </summary>
    public static class BeamSupports
    {
        public const double Bearing = BuildingEdits.BearingTolerance;
        private const double Tol = 0.01;

        /// <summary>The walls whose top bears on a level (W-E or S-N).</summary>
        public static List<Wall> WallsUnder(Building b, Level lv) =>
            b.Walls.Where(w => w.Direction != WallDirection.NotOrthogonal && b.LevelsAlong(w).Any(x => x.Id == lv.Id)).ToList();

        /// <summary>The columns under a level: base below it, top at or above it.</summary>
        public static List<Column> ColumnsUnder(Building b, Level lv)
        {
            double E(string id) => b.Level(id)?.Elevation ?? 0;
            return b.Columns.Where(c => b.Level(c.BaseLevelId) != null && b.Level(c.TopLevelId) != null
                                        && E(c.BaseLevelId) < lv.Elevation - Tol && E(c.TopLevelId) >= lv.Elevation - Tol).ToList();
        }

        /// <summary>Every beam of a level (with length), and what holds each up.</summary>
        public static Dictionary<string, List<BeamBearing>> Of(Building b, Level lv)
        {
            var walls = WallsUnder(b, lv);
            var cols = ColumnsUnder(b, lv);
            var beams = b.Beams.Where(m => m.LevelId == lv.Id && m.Length > 0.1).ToList();
            return beams.ToDictionary(m => m.Id, m => Of(m, walls, cols, beams));
        }

        /// <summary>What holds one beam up, in order along it.</summary>
        public static List<BeamBearing> Of(Beam m, IList<Wall> walls, IList<Column> columns, IList<Beam> beams)
        {
            double L = Dist(m.Start, m.End);
            Point2 At(double t) => L < 1e-9 ? m.Start : new Point2(m.Start.X + (m.End.X - m.Start.X) * t / L, m.Start.Y + (m.End.Y - m.Start.Y) * t / L);
            double Along(Point2 p) => L < 1e-9 ? 0 : Math.Max(0, Math.Min(L, ((p.X - m.Start.X) * (m.End.X - m.Start.X) + (p.Y - m.Start.Y) * (m.End.Y - m.Start.Y)) / L));
            double Off(Point2 p) => ToSegment(p, m.Start, m.End);
            var found = new List<(double at, string on, BeamSupportKind kind)>();
            foreach (var w in walls)
            {
                if (ToSegment(m.Start, w.Start, w.End) <= Bearing) found.Add((0, w.Id, BeamSupportKind.Wall));
                if (ToSegment(m.End, w.Start, w.End) <= Bearing) found.Add((L, w.Id, BeamSupportKind.Wall));
                foreach (var p in new[] { w.Start, w.End }) if (Off(p) <= Bearing) found.Add((Along(p), w.Id, BeamSupportKind.Wall));
                if (Crossing(m.Start, m.End, w.Start, w.End, out var t)) found.Add((t * L, w.Id, BeamSupportKind.Wall));
            }
            foreach (var c in columns) if (Off(c.At) <= Bearing) found.Add((Along(c.At), c.Id, BeamSupportKind.Column));
            var list = new List<BeamBearing>();
            foreach (var f in found.Select(f => (at: f.at <= Bearing ? 0 : f.at >= L - Bearing ? L : f.at, f.on, f.kind)).OrderBy(f => f.at).ThenBy(f => f.kind))
                if (!list.Any(x => Math.Abs(x.At - f.at) <= Bearing)) list.Add(new BeamBearing { At = f.at, On = f.on, Kind = f.kind });
            // an end with no wall or column under it: a beam there
            string BeamAt(Point2 p) =>
                beams.FirstOrDefault(x => x.Id != m.Id && ToSegment(p, x.Start, x.End) <= Bearing && Dist(p, x.Start) > Bearing && Dist(p, x.End) > Bearing)?.Id
                ?? beams.FirstOrDefault(x => x.Id != m.Id && (Dist(p, x.Start) <= Bearing || Dist(p, x.End) <= Bearing))?.Id;
            if (!list.Any(x => x.At <= Bearing)) { var on = BeamAt(m.Start); if (on != null) list.Insert(0, new BeamBearing { At = 0, On = on, Kind = BeamSupportKind.Beam }); }
            if (!list.Any(x => x.At >= L - Bearing)) { var on = BeamAt(m.End); if (on != null) list.Add(new BeamBearing { At = L, On = on, Kind = BeamSupportKind.Beam }); }
            foreach (var x in list)
            {
                x.Name = x.At <= Bearing ? "start" : x.At >= L - Bearing ? "end" : $"at {x.At:0.##} ft";
                x.Where = x.Name == "start" ? m.Start : x.Name == "end" ? m.End : At(x.At);
            }
            return list;
        }

        /// <summary>
        /// The beams of a level that cannot stand: each taken as a rigid bar (its rise and its turn), held where a wall or a
        /// column is under it, and tied to the beam it rests on at that point. Where those ties leave a bar free to move, it
        /// is a mechanism: a beam with fewer than two supports, or beams propping each other up with nothing under them.
        /// </summary>
        public static List<string> Unstable(Building b, Level lv, Dictionary<string, List<BeamBearing>> supports = null)
        {
            supports = supports ?? Of(b, lv);
            var beams = b.Beams.Where(m => supports.ContainsKey(m.Id)).ToList();
            int n = beams.Count;
            if (n == 0) return new List<string>();
            var index = beams.Select((m, i) => (m.Id, i)).ToDictionary(t => t.Id, t => t.i);
            double AlongOf(Beam m, Point2 p)
            {
                double L = m.Length;
                return L < 1e-9 ? 0 : ((p.X - m.Start.X) * (m.End.X - m.Start.X) + (p.Y - m.Start.Y) * (m.End.Y - m.Start.Y)) / L;
            }
            // the ties: rows of [rise, turn] per beam, the turn per ft along it
            var rows = new List<double[]>();
            foreach (var m in beams)
            {
                int i = index[m.Id];
                foreach (var s in supports[m.Id])
                {
                    var row = new double[2 * n];
                    row[2 * i] = 1;
                    row[2 * i + 1] = s.At;
                    if (s.Kind == BeamSupportKind.Beam && index.TryGetValue(s.On, out int j))
                    {
                        var o = beams[j];
                        row[2 * j] -= 1;
                        row[2 * j + 1] -= AlongOf(o, s.Where);
                    }
                    rows.Add(row);
                }
            }
            // reduce, and read the free motions off the columns with no pivot
            int cols = 2 * n, r = 0;
            var pivotOf = new int[cols];
            for (int c = 0; c < cols; c++) pivotOf[c] = -1;
            var A = rows.ToArray();
            for (int c = 0; c < cols && r < A.Length; c++)
            {
                int best = -1;
                double big = 1e-9;
                for (int k = r; k < A.Length; k++) if (Math.Abs(A[k][c]) > big) { big = Math.Abs(A[k][c]); best = k; }
                if (best < 0) continue;
                var tmp = A[r]; A[r] = A[best]; A[best] = tmp;
                double pv = A[r][c];
                for (int k = 0; k < cols; k++) A[r][k] /= pv;
                for (int k = 0; k < A.Length; k++)
                {
                    if (k == r || A[k][c] == 0) continue;
                    double f = A[k][c];
                    for (int q = 0; q < cols; q++) A[k][q] -= f * A[r][q];
                }
                pivotOf[c] = r++;
            }
            var free = new HashSet<int>();
            for (int c = 0; c < cols; c++)
            {
                if (pivotOf[c] >= 0) continue;
                // the motion with this one free: it, and every pivot that moves with it
                free.Add(c / 2);
                for (int c2 = 0; c2 < cols; c2++)
                    if (pivotOf[c2] >= 0 && Math.Abs(A[pivotOf[c2]][c]) > 1e-9) free.Add(c2 / 2);
            }
            return free.OrderBy(i => i).Select(i => beams[i].Id).ToList();
        }

        private static double Dist(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double ToSegment(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return Dist(p, a);
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            return Dist(p, new Point2(a.X + t * dx, a.Y + t * dy));
        }

        /// <summary>Where two segments cross, as a share of the first (0 to 1); false when they do not, or run alongside.</summary>
        private static bool Crossing(Point2 a, Point2 b, Point2 c, Point2 d, out double t)
        {
            t = 0;
            double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-12) return false;
            double qx = c.X - a.X, qy = c.Y - a.Y;
            t = (qx * sy - qy * sx) / den;
            double u = (qx * ry - qy * rx) / den;
            return t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9;
        }
    }
}
