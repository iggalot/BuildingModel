using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Statics
{
    /// <summary>One joist (or flat-roof rafter), laid out from its framing area: where it runs and what holds it up.</summary>
    public sealed class Joist
    {
        public string AreaId { get; set; }
        public string DiaphragmId { get; set; }
        public Point2 Start { get; set; }
        public Point2 End { get; set; }
        /// <summary>True when it runs W-E.</summary>
        public bool AlongX { get; set; }
        /// <summary>Its line across the framing: the y of a W-E joist, the x of a S-N one (ft).</summary>
        public double Line { get; set; }
        public double Length => AlongX ? End.X - Start.X : End.Y - Start.Y;
        /// <summary>What holds it up, in order from its start (west or south): the walls, columns, beams and girders under it.</summary>
        public List<BeamBearing> Supports { get; set; } = new List<BeamBearing>();
        /// <summary>Two supports or more: it stands (an end past one is a cantilever).</summary>
        public bool Stands => Supports.Count >= 2;

        public override string ToString() => $"{AreaId} joist {(AlongX ? "y" : "x")} = {Line:0.##} ft, {Length:0.##} ft, {Supports.Count} support(s)";
    }

    /// <summary>
    /// The joists of a level, laid out from its framing areas (<see cref="FramingArea"/>), and what holds each up: the one rule
    /// for every app (owner 2026-11-10). Each area's joists run its way at the project's spacing (the floors'
    /// <see cref="ModelDefaults.FloorJoistSpacing"/>, the roof's <see cref="ModelDefaults.RafterSpacing"/>), one on each of its
    /// edges; they run on through the next area framed the same way (owner 2026-11-11: a hallway named apart for its loads is
    /// not a break in the floor) and end where the framing turns, at the floor's edge, or at an opening, which cuts them (the
    /// header there is a beam). A joist is held by every wall, column
    /// and beam under it anywhere along it, and needs two to stand, as a beam does; it never bears on another joist. A sloped
    /// roof's rafters run across the ridge: the roof's, not laid out here.
    /// </summary>
    public static class Joists
    {
        public const double Bearing = BeamSupports.Bearing;
        private const double E = 1e-6;

        /// <summary>The level's framing: rafters at the roof, else joists.</summary>
        public static bool IsRoof(Building b, Level lv) => lv != null && b.RoofLevel()?.Id == lv.Id;

        /// <summary>The joists' spacing on a level (ft).</summary>
        public static double SpacingOf(Building b, Level lv) =>
            Math.Max(1.0, IsRoof(b, lv) ? b.Defaults?.RafterSpacing ?? 24 : b.Defaults?.FloorJoistSpacing ?? 16) / 12;

        /// <summary>Every joist of a level, with what holds it up (none on the ground, nor under a sloped roof).</summary>
        public static List<Joist> Of(Building b, Level lv)
        {
            var list = new List<Joist>();
            if (b == null || lv == null || lv.Elevation <= 0) return list;
            if (IsRoof(b, lv) && b.Roof != null && b.Roof.IsSloped) return list;
            double s = SpacingOf(b, lv);
            var holes = b.OpeningsCutting(lv).SelectMany(o => o.Pieces).ToList();
            var walls = BeamSupports.WallsUnder(b, lv);
            var cols = BeamSupports.ColumnsUnder(b, lv);
            var beams = b.Beams.Where(m => m.LevelId == lv.Id && m.Length > 0.1).ToList();
            foreach (var d in b.Diaphragms.Where(d => d.LevelId == lv.Id))
                // the areas running the same way together: a joist runs on through the next area framed the same way (a hallway
                // named apart for its loads), ending only where the framing turns, at an opening or at the floor's edge (owner 2026-11-11)
                foreach (var run in (d.Framing ?? new List<FramingArea>()).GroupBy(a => a.AlongX))
                {
                    bool x = run.Key;
                    var areas = run.Select(a => (a, p0: x ? a.Rect.Y0 : a.Rect.X0, p1: x ? a.Rect.Y1 : a.Rect.X1, q0: x ? a.Rect.X0 : a.Rect.Y0, q1: x ? a.Rect.X1 : a.Rect.Y1)).ToList();
                    // a joist on every area's edges, and between at the spacing from the first edge (none nearer an edge than a quarter of it)
                    var edges = areas.SelectMany(t => new[] { t.p0, t.p1 }).OrderBy(p => p).ToList();
                    double first = edges[0], last = edges[edges.Count - 1];
                    var lines = new List<double>(edges);
                    for (int k = 1; first + k * s < last - E; k++)
                    {
                        double p = first + k * s;
                        if (edges.All(e => Math.Abs(e - p) >= s / 4)) lines.Add(p);
                    }
                    var at = new List<double>();
                    foreach (double p in lines.OrderBy(p => p)) if (at.Count == 0 || p - at[at.Count - 1] > E) at.Add(p);
                    foreach (double p in at)
                    {
                        // the line's extent: through every area of the run it crosses, end to end
                        var spans = areas.Where(t => t.p0 - E <= p && p <= t.p1 + E).Select(t => (t.q0, t.q1)).OrderBy(t => t.q0).ToList();
                        var merged = new List<(double a, double b)>();
                        foreach (var (u, v) in spans)
                            if (merged.Count > 0 && u <= merged[merged.Count - 1].b + E) merged[merged.Count - 1] = (merged[merged.Count - 1].a, Math.Max(merged[merged.Count - 1].b, v));
                            else merged.Add((u, v));
                        foreach (var (m0, m1) in merged)
                            foreach (var (u0, u1) in Cut(m0, m1, p, x, holes))
                            {
                                // the area it is in at its middle (a joist running through several: the one it is named by)
                                double mid = (u0 + u1) / 2;
                                var own = areas.Where(t => t.p0 - E <= p && p <= t.p1 + E && t.q0 - E <= mid && mid <= t.q1 + E).Select(t => t.a).FirstOrDefault()
                                          ?? areas.First(t => t.p0 - E <= p && p <= t.p1 + E).a;
                                var j = new Joist
                                {
                                    AreaId = own.Id, DiaphragmId = d.Id, AlongX = x, Line = p,
                                    Start = x ? new Point2(u0, p) : new Point2(p, u0), End = x ? new Point2(u1, p) : new Point2(p, u1),
                                };
                                j.Supports = SupportsOf(j.Start, j.End, walls, cols, beams);
                                list.Add(j);
                            }
                    }
                }
            return list;
        }

        /// <summary>A joist line from q0 to q1 with the openings it passes through taken out.</summary>
        private static List<(double, double)> Cut(double q0, double q1, double p, bool alongX, List<Rect2> holes)
        {
            var parts = new List<(double a, double b)> { (q0, q1) };
            foreach (var h in holes)
            {
                double hp0 = alongX ? h.Y0 : h.X0, hp1 = alongX ? h.Y1 : h.X1, hq0 = alongX ? h.X0 : h.Y0, hq1 = alongX ? h.X1 : h.Y1;
                if (!(p > hp0 + E && p < hp1 - E)) continue;
                var next = new List<(double, double)>();
                foreach (var (a, c) in parts)
                {
                    if (hq1 <= a + E || hq0 >= c - E) { next.Add((a, c)); continue; }
                    if (hq0 > a + E) next.Add((a, hq0));
                    if (hq1 < c - E) next.Add((hq1, c));
                }
                parts = next;
            }
            return parts.Where(t => t.b - t.a > 0.1).ToList();
        }

        /// <summary>
        /// What holds a joist from <paramref name="a"/> to <paramref name="c"/> up: every wall and column under it, and every
        /// beam on the level under it, anywhere along it (crossing it, ending under it, under one of its ends; one along it,
        /// the ends of where they overlap). Two within <see cref="Bearing"/> are one (a wall before a column before a beam).
        /// </summary>
        public static List<BeamBearing> SupportsOf(Point2 a, Point2 c, IList<Wall> walls, IList<Column> columns, IList<Beam> beams)
        {
            double L = Dist(a, c);
            double Along(Point2 p) => L < 1e-9 ? 0 : Math.Max(0, Math.Min(L, ((p.X - a.X) * (c.X - a.X) + (p.Y - a.Y) * (c.Y - a.Y)) / L));
            double Off(Point2 p) => ToSegment(p, a, c);
            var found = new List<(double at, string on, BeamSupportKind kind)>();
            void Line(Point2 s, Point2 t, string id, BeamSupportKind kind)
            {
                if (ToSegment(a, s, t) <= Bearing) found.Add((0, id, kind));
                if (ToSegment(c, s, t) <= Bearing) found.Add((L, id, kind));
                foreach (var p in new[] { s, t }) if (Off(p) <= Bearing) found.Add((Along(p), id, kind));
                if (Crossing(a, c, s, t, out double u)) found.Add((u * L, id, kind));
            }
            foreach (var w in walls) Line(w.Start, w.End, w.Id, BeamSupportKind.Wall);
            foreach (var col in columns) if (Off(col.At) <= Bearing) found.Add((Along(col.At), col.Id, BeamSupportKind.Column));
            foreach (var m in beams) Line(m.Start, m.End, m.Id, BeamSupportKind.Beam);
            var list = new List<BeamBearing>();
            foreach (var f in found.Select(f => (at: f.at <= Bearing ? 0 : f.at >= L - Bearing ? L : f.at, f.on, f.kind)).OrderBy(f => f.at).ThenBy(f => f.kind))
                if (!list.Any(y => Math.Abs(y.At - f.at) <= Bearing)) list.Add(new BeamBearing { At = f.at, On = f.on, Kind = f.kind });
            foreach (var y in list)
            {
                y.Name = y.At <= Bearing ? "start" : y.At >= L - Bearing ? "end" : $"at {y.At:0.##} ft";
                y.Where = L < 1e-9 ? a : new Point2(a.X + (c.X - a.X) * y.At / L, a.Y + (c.Y - a.Y) * y.At / L);
            }
            return list;
        }

        private static double Dist(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double ToSegment(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return Dist(p, a);
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            return Dist(p, new Point2(a.X + t * dx, a.Y + t * dy));
        }

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

    /// <summary>A beam's place in the framing, from what lands on it: a girder carries a beam; a beam carries joists or nothing.</summary>
    public enum FramingRole { Beam, Girder }

    /// <summary>What a member of a level's framing carries and what carries it.</summary>
    public sealed class FramingMember
    {
        public string Id { get; set; }
        public FramingRole Role { get; set; }
        /// <summary>The beams that land on it (at an end or partway along).</summary>
        public List<string> CarriesBeams { get; } = new List<string>();
        /// <summary>The framing areas whose joists land on it, and how many joists.</summary>
        public Dictionary<string, int> CarriesJoists { get; } = new Dictionary<string, int>();
        /// <summary>What holds it up: walls, columns, beams.</summary>
        public List<BeamBearing> Supports { get; set; } = new List<BeamBearing>();

        /// <summary>"girder: carries BM2, BM3; joists of FA1 (12)".</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (CarriesBeams.Count > 0) parts.Add("carries " + string.Join(", ", CarriesBeams));
            if (CarriesJoists.Count > 0) parts.Add("joists of " + string.Join(", ", CarriesJoists.Select(kv => $"{kv.Key} ({kv.Value})")));
            return (Role == FramingRole.Girder ? "girder" : "beam") + (parts.Count == 0 ? ": carries nothing" : ": " + string.Join("; ", parts));
        }
    }

    /// <summary>
    /// The framing's order on a level, found from the geometry, never declared (a declared girder could disagree with what is
    /// drawn): joists on beams, beams on girders, girders on columns and walls. A beam that carries another beam is a girder.
    /// </summary>
    public static class FramingRoles
    {
        public static Dictionary<string, FramingMember> Of(Building b, Level lv, List<Joist> joists = null)
        {
            var supports = BeamSupports.Of(b, lv);
            var result = supports.ToDictionary(kv => kv.Key, kv => new FramingMember { Id = kv.Key, Supports = kv.Value });
            foreach (var kv in supports)
                foreach (var s in kv.Value.Where(s => s.Kind == BeamSupportKind.Beam && result.ContainsKey(s.On)))
                {
                    var g = result[s.On];
                    if (!g.CarriesBeams.Contains(kv.Key)) g.CarriesBeams.Add(kv.Key);
                    g.Role = FramingRole.Girder;
                }
            foreach (var j in joists ?? Joists.Of(b, lv))
                foreach (var s in j.Supports.Where(s => s.Kind == BeamSupportKind.Beam && result.ContainsKey(s.On)))
                {
                    var m = result[s.On].CarriesJoists;
                    m[j.AreaId] = m.TryGetValue(j.AreaId, out int n) ? n + 1 : 1;
                }
            return result;
        }
    }
}
