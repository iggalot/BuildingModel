using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Statics
{
    /// <summary>A ridge, hip or valley where two faces meet; a header, where a dormer cuts the rafters of the roof below (one face).</summary>
    public enum CreaseKind { Ridge, Hip, Valley, Header }

    /// <summary>One section's roof plane: z = Z0 + Gx x + Gy y (ft above the roof level), sloping one way, its rafters that way.</summary>
    public sealed class RoofPlane
    {
        public int Section;
        public double Z0, Gx, Gy;
        /// <summary>Its rafters run W-E (it slopes in x); else S-N.</summary>
        public bool AlongX;
        /// <summary>+1: it rises toward +x (or +y); -1 toward -x.</summary>
        public int Up;
        public double SlopeDeg;
        /// <summary>"south", "north", "west", "east": the side it slopes down to.</summary>
        public string Side;
        public double Z(double x, double y) => Z0 + Gx * x + Gy * y;
        public double Z(Point2 p) => Z(p.X, p.Y);
    }

    /// <summary>A face of the roof: a plane where it is on top, in convex pieces (its eaves included).</summary>
    public sealed class RoofFace
    {
        public string Id;
        public string Name;
        public RoofPlane Plane;
        public List<Convex> Pieces = new List<Convex>();
        public double Area => Pieces.Sum(p => p.Area);
        public override string ToString() => $"{Name}: {Area:0.##} sq ft on plan";
    }

    /// <summary>Where two faces meet: a ridge or hip (one section's), or a valley (two sections'). A is its lower end.</summary>
    public sealed class RoofCrease
    {
        public string Id;
        public CreaseKind Kind;
        public Point2 A, B;
        public double ZA, ZB;
        public RoofFace F1, F2;
        public double Length => Dist(A, B);
        public bool Touches(RoofFace f) => F1 == f || F2 == f;
        public double DistanceTo(Point2 p) => ToSegment(p, A, B);
        public override string ToString() => $"{Kind} {Id} ({A.X:0.##}, {A.Y:0.##}) to ({B.X:0.##}, {B.Y:0.##})";
        internal static double Dist(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        internal static double ToSegment(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return Dist(p, a);
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            return Dist(p, new Point2(a.X + t * dx, a.Y + t * dy));
        }
    }

    /// <summary>What a rafter's end meets: nothing (an eave, a rake), a ridge, a hip or valley rafter, or a lower roof (a dormer's wall on it).</summary>
    public enum RafterEnd { Free, Ridge, Hip, Valley, OnRoof, Header }

    /// <summary>
    /// One rafter of a sectioned roof: square to its face's eave, on plan from its start (west or south) to its end, its
    /// tributary width along it (each piece linear: from, to, width at each, ft from its start), and what each end meets.
    /// </summary>
    public sealed class RoofRafter
    {
        public string Id;
        public RoofFace Face;
        public bool AlongX;
        /// <summary>Its line across the framing: the y of a W-E rafter, the x of a S-N one.</summary>
        public double Line;
        public Point2 Start, End;
        public double Length => AlongX ? End.X - Start.X : End.Y - Start.Y;
        public double SlopeDeg => Face.Plane.SlopeDeg;
        /// <summary>It rises toward its end.</summary>
        public bool RisesToEnd => Face.Plane.Up > 0;
        /// <summary>Its tributary width on plan, piece by piece (ft from its start): (from, to, width there, width at to).</summary>
        public List<(double a, double b, double wa, double wb)> Strip = new List<(double, double, double, double)>();
        /// <summary>Its strip's area past its start and past its end (sq ft on plan), each taken at that end; and where (on plan) each acts.</summary>
        public double BeforeArea, AfterArea;
        public Point2 BeforeAt, AfterAt;
        public RafterEnd AtStart, AtEnd;
        /// <summary>The crease (or the face below, <see cref="RafterEnd.OnRoof"/>) at each end.</summary>
        public RoofCrease StartCrease, EndCrease;
        public RoofFace StartOnFace, EndOnFace;
        public string Kind
        {
            get
            {
                bool lowFree = RisesToEnd ? AtStart == RafterEnd.Free : AtEnd == RafterEnd.Free;
                var top = RisesToEnd ? AtEnd : AtStart;
                var low = RisesToEnd ? AtStart : AtEnd;
                if (low == RafterEnd.Valley) return "valley jack";
                if (top == RafterEnd.Hip) return "hip jack";
                return lowFree || low == RafterEnd.OnRoof ? "common" : "rafter";
            }
        }
        /// <summary>The strip's whole area (sq ft on plan).</summary>
        public double Area => Strip.Sum(s => (s.b - s.a) * (s.wa + s.wb) / 2) + BeforeArea + AfterArea;
        public Point2 At(double u) => AlongX ? new Point2(Start.X + u, Line) : new Point2(Line, Start.Y + u);
        public override string ToString() => $"{Face.Name} {Kind} {(AlongX ? "y" : "x")} = {Line:0.##}, {Length:0.##} ft";
    }

    /// <summary>A hip or valley rafter: along its crease on plan, from its low end A to its high end B.</summary>
    public sealed class RoofLine
    {
        public string Id;
        public RoofCrease Crease;
        public CreaseKind Kind => Crease.Kind;
        public Point2 A => Crease.A;
        public Point2 B => Crease.B;
        public double Length => Crease.Length;
        /// <summary>Its own slope (degrees): its rise over its run on plan.</summary>
        public double SlopeDeg => Length < 1e-9 ? 0 : Math.Atan((Crease.ZB - Crease.ZA) / Length) * 180 / Math.PI;
        /// <summary>What its high end meets: the ridge it reaches (null: none, an apex or a face).</summary>
        public RoofCrease TopRidge;
        /// <summary>The hips meeting it at its high end with no ridge there (a pyramid's apex).</summary>
        public List<RoofLine> AtApex = new List<RoofLine>();
        /// <summary>A valley's high end on a face of the other section, not on a crease (it bears on that face's rafters).</summary>
        public RoofFace TopOnFace;
        /// <summary>A header's ends: on its face's rafters (the trimmers each side of the dormer).</summary>
        public RoofFace EndsOnFace;
        public override string ToString() => $"{(Kind == CreaseKind.Hip ? "hip" : "valley")} rafter {Id}, {Length:0.##} ft on plan";
    }

    /// <summary>
    /// A sloped roof laid out from its sections (owner 2026-11-12: hips, valleys, wings and dormers): each section's planes,
    /// the roof the highest of them at every point (a section's own surface the lowest of its planes), its faces in convex
    /// pieces, the creases where they meet (ridges, hips, valleys), the rafters on each face square to its eave at the
    /// roof's spacing, and the hip and valley rafters on the creases. Geometry only.
    /// </summary>
    public sealed class RoofFrame
    {
        private const double E = 1e-6;
        public List<RoofSection> Sections = new List<RoofSection>();
        public List<RoofPlane> Planes = new List<RoofPlane>();
        public List<RoofFace> Faces = new List<RoofFace>();
        public List<RoofCrease> Creases = new List<RoofCrease>();
        public List<RoofRafter> Rafters = new List<RoofRafter>();
        public List<RoofLine> Lines = new List<RoofLine>();
        /// <summary>Why it cannot be laid out (null: it is).</summary>
        public string NotLaidOut;

        /// <summary>The roof's sections: its own, else the one over its plan (its shape and ridge); none for a flat roof.</summary>
        public static List<RoofSection> SectionsOf(Building b)
        {
            var roof = b?.Roof;
            if (roof == null || !roof.IsSloped) return new List<RoofSection>();
            if (roof.Sections != null && roof.Sections.Count > 0) return roof.Sections.Select(s => s.Clone()).ToList();
            var plan = b.RoofPlan();
            if (plan.IsEmpty) return new List<RoofSection>();
            return new List<RoofSection> { new RoofSection { Name = "the roof", Walls = plan, Shape = roof.Shape, Ridge = b.EffectiveRidge() } };
        }

        /// <summary>
        /// Laid out by these sections (a hip roof, or a roof of its own sections): the plain gable over the plan keeps the gable's
        /// own layout (<see cref="Joists"/>).
        /// </summary>
        public static bool IsSectioned(Building b) =>
            b?.Roof != null && b.Roof.IsSloped && (b.Roof.Shape == RoofShape.Hip || (b.Roof.Sections != null && b.Roof.Sections.Count > 0));

        public static RoofFrame Of(Building b) => Of(b, SectionsOf(b), b?.Roof?.Pitch ?? 0, Math.Max(0, b?.Roof?.Overhang ?? 0),
                                                       b == null ? 2 : Joists.SpacingOf(b, b.RoofLevel() ?? new Level()));

        public static double PitchOf(RoofSection s, double roofPitch) => s.Pitch > 0 && s.Pitch < 90 ? s.Pitch : roofPitch;
        public static double OverhangOf(RoofSection s, double roofOverhang) => Math.Max(0, s.Overhang ?? roofOverhang);
        public static Rect2 EavesOf(RoofSection s, double roofOverhang)
        {
            double o = OverhangOf(s, roofOverhang);
            var w = s.Walls;
            return new Rect2(w.X0 - o, w.Y0 - o, w.X1 + o, w.Y1 + o);
        }

        /// <summary>A gable's ridge W-E (Auto: along the longer side; a hip always).</summary>
        public static bool RidgeWestEast(RoofSection s) =>
            s.Shape == RoofShape.Gable && s.Ridge != RidgeAxis.Auto ? s.Ridge == RidgeAxis.WestEast : s.Walls.Width >= s.Walls.Height;

        /// <param name="spacing">The rafters' spacing (ft).</param>
        public static RoofFrame Of(Building b, List<RoofSection> sections, double roofPitch, double roofOverhang, double spacing)
        {
            var f = new RoofFrame { Sections = sections };
            for (int i = 0; i < sections.Count; i++)
            {
                var s = sections[i];
                double pitch = PitchOf(s, roofPitch);
                if (!(pitch > 0 && pitch < 90)) { f.NotLaidOut = $"{s.Name ?? "a section"} has no slope"; return f; }
                if (s.Walls.IsEmpty) { f.NotLaidOut = $"{s.Name ?? "a section"} has no area"; return f; }
                double t = Math.Tan(pitch * Math.PI / 180), e = s.EaveRise;
                var w = s.Walls;
                RoofPlane P(string side) => side == "south" ? new RoofPlane { Section = i, Z0 = e - t * w.Y0, Gy = t, AlongX = false, Up = 1, SlopeDeg = pitch, Side = side }
                    : side == "north" ? new RoofPlane { Section = i, Z0 = e + t * w.Y1, Gy = -t, AlongX = false, Up = -1, SlopeDeg = pitch, Side = side }
                    : side == "west" ? new RoofPlane { Section = i, Z0 = e - t * w.X0, Gx = t, AlongX = true, Up = 1, SlopeDeg = pitch, Side = side }
                    : new RoofPlane { Section = i, Z0 = e + t * w.X1, Gx = -t, AlongX = true, Up = -1, SlopeDeg = pitch, Side = side };
                IEnumerable<string> sides;
                switch (s.Shape)
                {
                    case RoofShape.Hip: sides = new[] { "south", "north", "west", "east" }; break;
                    case RoofShape.Shed: sides = new[] { s.Low == RoofSide.South ? "south" : s.Low == RoofSide.North ? "north" : s.Low == RoofSide.West ? "west" : "east" }; break;
                    case RoofShape.Gable: sides = RidgeWestEast(s) ? new[] { "south", "north" } : new[] { "west", "east" }; break;
                    default: f.NotLaidOut = $"{s.Name ?? "a section"} is flat: a section is a gable, hip or shed"; return f;
                }
                f.Planes.AddRange(sides.Select(P));
            }

            // the faces: each plane where it is its section's lowest, less where another section is higher
            foreach (var pl in f.Planes)
            {
                var s = sections[pl.Section];
                var piece = Convex.Of(EavesOf(s, roofOverhang));
                foreach (var o in f.Planes.Where(o => o.Section == pl.Section && o != pl))
                    piece = piece.Clip(o.Gx - pl.Gx, o.Gy - pl.Gy, o.Z0 - pl.Z0);
                var pieces = piece.IsEmpty ? new List<Convex>() : new List<Convex> { piece };
                for (int j = 0; j < sections.Count && pieces.Count > 0; j++)
                {
                    if (j == pl.Section) continue;
                    // another section covers this plane where its surface is above it, over its eaves (a dormer: over its walls
                    // only: its eaves overhang the roof below, which runs on under them)
                    var cover = sections[j].EaveRise > E ? sections[j].Walls : EavesOf(sections[j], roofOverhang);
                    var region = new List<(double, double, double)> { (1, 0, -cover.X0), (-1, 0, cover.X1), (0, 1, -cover.Y0), (0, -1, cover.Y1) };
                    bool none = false;
                    foreach (var o in f.Planes.Where(o => o.Section == j))
                    {
                        double a = o.Gx - pl.Gx, bb = o.Gy - pl.Gy, c = o.Z0 - pl.Z0;
                        if (Math.Abs(a) < 1e-12 && Math.Abs(bb) < 1e-12)
                        {
                            // the same plane: the earlier section has it
                            if (c > 1e-9 || (Math.Abs(c) <= 1e-9 && j < pl.Section)) continue;
                            none = true; break;
                        }
                        region.Add((a, bb, c));
                    }
                    if (none) continue;
                    pieces = pieces.SelectMany(p => p.Minus(region)).ToList();
                }
                pieces = pieces.Where(p => !p.IsEmpty).ToList();
                if (pieces.Sum(p => p.Area) < 1e-3) continue;
                f.Faces.Add(new RoofFace { Id = $"F{f.Faces.Count + 1}", Name = $"{s.Name ?? "the roof"}, {pl.Side} face", Plane = pl, Pieces = pieces });
            }

            // the creases: where two faces' planes meet along their pieces' common edge
            for (int x = 0; x < f.Faces.Count; x++)
                for (int y = x + 1; y < f.Faces.Count; y++)
                {
                    var p = f.Faces[x].Plane; var q = f.Faces[y].Plane;
                    double a = p.Gx - q.Gx, bb = p.Gy - q.Gy, c = p.Z0 - q.Z0, n2 = a * a + bb * bb;
                    if (n2 < 1e-12) continue;
                    var p0 = new Point2(-a * c / n2, -bb * c / n2);
                    double n = Math.Sqrt(n2);
                    var d = new Point2(-bb / n, a / n);
                    var spans = new List<(double, double)>();
                    foreach (var u in f.Faces[x].Pieces)
                        foreach (var v in f.Faces[y].Pieces)
                        {
                            var ru = u.Along(p0, d, 1e-8);
                            var rv = v.Along(p0, d, 1e-8);
                            if (ru == null || rv == null) continue;
                            double t0 = Math.Max(ru.Value.t0, rv.Value.t0), t1 = Math.Min(ru.Value.t1, rv.Value.t1);
                            if (t1 - t0 > 1e-3) spans.Add((t0, t1));
                        }
                    if (spans.Count == 0) continue;
                    // the spans joined where they touch
                    var merged = new List<(double t0, double t1)>();
                    foreach (var (t0, t1) in spans.OrderBy(z => z.Item1))
                        if (merged.Count > 0 && t0 <= merged[merged.Count - 1].t1 + 1e-3) merged[merged.Count - 1] = (merged[merged.Count - 1].t0, Math.Max(merged[merged.Count - 1].t1, t1));
                        else merged.Add((t0, t1));
                    foreach (var (t0, t1) in merged)
                    {
                        var A = new Point2(p0.X + d.X * t0, p0.Y + d.Y * t0);
                        var B = new Point2(p0.X + d.X * t1, p0.Y + d.Y * t1);
                        if (p.Z(A) > p.Z(B)) { var tmp = A; A = B; B = tmp; }
                        var kind = p.Section != q.Section ? CreaseKind.Valley
                            : Math.Abs(p.Gx + q.Gx) < 1e-9 && Math.Abs(p.Gy + q.Gy) < 1e-9 ? CreaseKind.Ridge : CreaseKind.Hip;
                        f.Creases.Add(new RoofCrease { Kind = kind, A = A, B = B, ZA = p.Z(A), ZB = p.Z(B), F1 = f.Faces[x], F2 = f.Faces[y] });
                    }
                }
            int nr = 0, nh = 0, nv = 0;
            foreach (var cr in f.Creases.OrderBy(z => z.Kind).ThenBy(z => z.A.Y).ThenBy(z => z.A.X))
                cr.Id = cr.Kind == CreaseKind.Ridge ? $"R{++nr}" : cr.Kind == CreaseKind.Hip ? $"H{++nh}" : $"V{++nv}";

            // the hip and valley rafters
            foreach (var cr in f.Creases.Where(z => z.Kind != CreaseKind.Ridge).OrderBy(z => z.Id))
                f.Lines.Add(new RoofLine { Id = cr.Id, Crease = cr });
            foreach (var l in f.Lines)
            {
                if (l.Kind == CreaseKind.Hip)
                {
                    // a hip's top: its section's ridge (at the ridge's end), else the apex where the hips meet
                    l.TopRidge = f.Creases.FirstOrDefault(z => z.Kind == CreaseKind.Ridge && z.DistanceTo(l.B) < 1e-3);
                    if (l.TopRidge == null)
                        l.AtApex.AddRange(f.Lines.Where(o => o != l && o.Kind == CreaseKind.Hip && RoofCrease.Dist(o.B, l.B) < 1e-3));
                    continue;
                }
                // a valley's top bears on the roof that runs on above it (the main roof a wing's ridge dies into, or the main
                // roof's ridge): the face just past its high end, its rafters there
                double len = l.Length;
                if (len < 1e-9) continue;
                var past = new Point2(l.B.X + (l.B.X - l.A.X) / len * 0.01, l.B.Y + (l.B.Y - l.A.Y) / len * 0.01);
                l.TopOnFace = f.Faces.Where(o => o.Pieces.Any(pc => Inside(pc, past, 1e-6))).OrderByDescending(o => o.Plane.Z(past)).FirstOrDefault();
            }

            f.LayRafters(sections, spacing);
            f.Headers(sections);
            return f;
        }

        private static bool Inside(Convex c, Point2 p, double grow) => c.Edges().All(h => h.a * p.X + h.b * p.Y + h.c >= -grow);

        /// <summary>
        /// Each face's rafters: lines across it at the spacing from its section's wall line (one on each of its walls, none
        /// nearer an end than a quarter spacing), each line's runs through the face its rafters; each one's tributary strip
        /// half the gap to the line each side (to the face's edge past the last), its width along it exactly.
        /// </summary>
        private void LayRafters(List<RoofSection> sections, double s)
        {
            int n = 0;
            foreach (var face in Faces)
            {
                var pl = face.Plane;
                var w = sections[pl.Section].Walls;
                bool x = pl.AlongX;
                // across: y for a W-E rafter
                double v0 = x ? w.Y0 : w.X0, v1 = x ? w.Y1 : w.X1;
                double vmin = face.Pieces.SelectMany(p => p.P).Min(p => x ? p.Y : p.X), vmax = face.Pieces.SelectMany(p => p.P).Max(p => x ? p.Y : p.X);
                var grid = new List<double> { v0 };
                for (int k = 1; v0 + k * s < v1 - E; k++) if (v1 - (v0 + k * s) >= s / 4) grid.Add(v0 + k * s);
                grid.Add(v1);
                // a dormer on this face: a trimmer rafter on each of its sides (its cheek walls stand on them, its header hangs
                // from them), the spacing kept between
                foreach (var d in sections.Where((d, j) => j != pl.Section && d.EaveRise > sections[pl.Section].EaveRise + E))
                    foreach (double side in x ? new[] { d.Walls.Y0, d.Walls.Y1 } : new[] { d.Walls.X0, d.Walls.X1 })
                        if (side > vmin + E && side < vmax - E)
                        {
                            grid.RemoveAll(g => Math.Abs(g - side) < s / 4 && Math.Abs(g - side) > E);
                            if (!grid.Any(g => Math.Abs(g - side) <= E)) grid.Add(side);
                        }
                grid = grid.OrderBy(g => g).ToList();
                // each line's runs through the face; a line with none (it only touches the face) is no rafter, its
                // neighbours' strips meet over it
                var dir = x ? new Point2(1, 0) : new Point2(0, 1);
                var withRuns = new List<(double v, List<(double a, double b)> runs)>();
                foreach (double v in grid.Where(v => v >= vmin - E && v <= vmax + E))
                {
                    var at = x ? new Point2(0, v) : new Point2(v, 0);
                    var runs = new List<(double a, double b)>();
                    foreach (var pc in face.Pieces)
                    {
                        var r = pc.Along(at, dir, 1e-9);
                        if (r != null && r.Value.t1 - r.Value.t0 > 1e-6) runs.Add((r.Value.t0, r.Value.t1));
                    }
                    var m0 = new List<(double a, double b)>();
                    foreach (var (a, b) in runs.OrderBy(z => z.a))
                        if (m0.Count > 0 && a <= m0[m0.Count - 1].b + 1e-4) m0[m0.Count - 1] = (m0[m0.Count - 1].a, Math.Max(m0[m0.Count - 1].b, b));
                        else m0.Add((a, b));
                    m0 = m0.Where(z => z.b - z.a > 0.1).ToList();
                    if (m0.Count > 0) withRuns.Add((v, m0));
                }
                for (int li = 0; li < withRuns.Count; li++)
                {
                    double v = withRuns[li].v;
                    var merged = withRuns[li].runs;
                    double lo = li == 0 ? vmin : (withRuns[li - 1].v + v) / 2, hi = li == withRuns.Count - 1 ? vmax : (v + withRuns[li + 1].v) / 2;
                    // the strip's pieces: the face between lo and hi
                    var strip = face.Pieces.Select(pc => x ? pc.Clip(0, 1, -lo).Clip(0, -1, hi) : pc.Clip(1, 0, -lo).Clip(-1, 0, hi)).Where(pc => !pc.IsEmpty).ToList();
                    for (int k = 0; k < merged.Count; k++)
                    {
                        var (ua, ub) = merged[k];
                        double cutLo = k == 0 ? double.NegativeInfinity : (merged[k - 1].b + ua) / 2, cutHi = k == merged.Count - 1 ? double.PositiveInfinity : (ub + merged[k + 1].a) / 2;
                        var r = new RoofRafter { Id = $"RF{++n}", Face = face, AlongX = x, Line = v, Start = x ? new Point2(ua, v) : new Point2(v, ua), End = x ? new Point2(ub, v) : new Point2(v, ub) };
                        double bx = 0, by = 0, ax = 0, ay = 0;
                        foreach (var pc in strip)
                        {
                            var part = pc;
                            if (!double.IsNegativeInfinity(cutLo)) part = x ? part.Clip(1, 0, -cutLo) : part.Clip(0, 1, -cutLo);
                            if (!double.IsPositiveInfinity(cutHi)) part = x ? part.Clip(-1, 0, cutHi) : part.Clip(0, -1, cutHi);
                            if (part.IsEmpty) continue;
                            var us = part.P.Select(p => x ? p.X : p.Y).Distinct().OrderBy(z => z).ToList();
                            for (int m = 0; m + 1 < us.Count; m++)
                            {
                                double a = us[m], b = us[m + 1];
                                if (b - a < 1e-9) continue;
                                double wa = part.ChordAt(x, a + 1e-9), wb = part.ChordAt(x, b - 1e-9);
                                // the part before the rafter's start, on it, and past its end
                                void Piece(double p0, double p1)
                                {
                                    if (p1 - p0 < 1e-9) return;
                                    double w0 = wa + (wb - wa) * (p0 - a) / (b - a), w1 = wa + (wb - wa) * (p1 - a) / (b - a);
                                    double area = (p1 - p0) * (w0 + w1) / 2;
                                    // where its resultant acts along the run (a trapezoid's centroid) and across (the strip's middle there)
                                    double cu = w0 + w1 < 1e-12 ? (p0 + p1) / 2 : p0 + (p1 - p0) * (w0 + 2 * w1) / (3 * (w0 + w1));
                                    if (p1 <= ua + 1e-9) { r.BeforeArea += area; bx += area * cu; by += area * v; }
                                    else if (p0 >= ub - 1e-9) { r.AfterArea += area; ax += area * cu; ay += area * v; }
                                    else r.Strip.Add((p0 - ua, p1 - ua, w0, w1));
                                }
                                Piece(a, Math.Min(b, Math.Max(a, ua)));
                                Piece(Math.Max(a, ua), Math.Min(b, ub));
                                Piece(Math.Max(a, Math.Min(b, ub)), b);
                            }
                        }
                        r.BeforeAt = r.BeforeArea > 0 ? (x ? new Point2(bx / r.BeforeArea, by / r.BeforeArea) : new Point2(by / r.BeforeArea, bx / r.BeforeArea)) : r.Start;
                        r.AfterAt = r.AfterArea > 0 ? (x ? new Point2(ax / r.AfterArea, ay / r.AfterArea) : new Point2(ay / r.AfterArea, ax / r.AfterArea)) : r.End;
                        (r.AtStart, r.StartCrease) = EndOf(face, r.Start);
                        (r.AtEnd, r.EndCrease) = EndOf(face, r.End);
                        Rafters.Add(r);
                    }
                }
            }
        }

        /// <summary>
        /// Where a dormer cuts the rafters of the roof below at its wall square to them (its front, or a shed's high wall): a
        /// header along that wall from trimmer to trimmer, each cut rafter's end on it.
        /// </summary>
        private void Headers(List<RoofSection> sections)
        {
            int n = 0;
            for (int j = 0; j < sections.Count; j++)
            {
                var d = sections[j];
                if (d.EaveRise <= E) continue;
                var w = d.Walls;
                foreach (var face in Faces.Where(fc => fc.Plane.Section != j && sections[fc.Plane.Section].EaveRise < d.EaveRise - E))
                {
                    bool x = face.Plane.AlongX;
                    // the walls square to this face's rafters: x = X0, X1 for W-E rafters
                    foreach (double edge in x ? new[] { w.X0, w.X1 } : new[] { w.Y0, w.Y1 })
                    {
                        bool OnEdge(Point2 p) => Math.Abs((x ? p.X : p.Y) - edge) < 1e-6 && (x ? p.Y > w.Y0 - E && p.Y < w.Y1 + E : p.X > w.X0 - E && p.X < w.X1 + E);
                        var cut = new List<(RoofRafter r, bool atEnd)>();
                        foreach (var r in Rafters.Where(r => r.Face == face))
                        {
                            if (r.AtStart == RafterEnd.Free && OnEdge(r.Start)) cut.Add((r, false));
                            if (r.AtEnd == RafterEnd.Free && OnEdge(r.End)) cut.Add((r, true));
                        }
                        if (cut.Count == 0) continue;
                        // from trimmer to trimmer: the dormer's sides
                        var A = x ? new Point2(edge, w.Y0) : new Point2(w.X0, edge);
                        var B = x ? new Point2(edge, w.Y1) : new Point2(w.X1, edge);
                        var cr = new RoofCrease { Id = $"HD{++n}", Kind = CreaseKind.Header, A = A, B = B, ZA = face.Plane.Z(A), ZB = face.Plane.Z(B), F1 = face };
                        Creases.Add(cr);
                        Lines.Add(new RoofLine { Id = cr.Id, Crease = cr, EndsOnFace = face });
                        foreach (var (r, atEnd) in cut)
                            if (atEnd) { r.AtEnd = RafterEnd.Header; r.EndCrease = cr; }
                            else { r.AtStart = RafterEnd.Header; r.StartCrease = cr; }
                    }
                }
            }
        }

        /// <summary>
        /// A dormer's rafter on its walls (its cheeks, its front): where it crosses its section's wall lines square to it, each
        /// over the roof below (that face, the highest of the others under it there). None for a section at the wall tops.
        /// </summary>
        public List<(double u, Point2 at, RoofFace below)> DormerWalls(RoofRafter r)
        {
            var list = new List<(double, Point2, RoofFace)>();
            var s = Sections[r.Face.Plane.Section];
            if (s.EaveRise <= E) return list;
            foreach (double edge in r.AlongX ? new[] { s.Walls.X0, s.Walls.X1 } : new[] { s.Walls.Y0, s.Walls.Y1 })
            {
                double u = edge - (r.AlongX ? r.Start.X : r.Start.Y);
                if (u < -1e-6 || u > r.Length + 1e-6) continue;
                var p = r.At(Math.Max(0, Math.Min(r.Length, u)));
                double z = r.Face.Plane.Z(p);
                var below = Faces.Where(fc => fc.Plane.Section != r.Face.Plane.Section && fc.Plane.Z(p) < z + 1e-9 && fc.Pieces.Any(pc => Inside(pc, p, 1e-6)))
                                 .OrderByDescending(fc => fc.Plane.Z(p)).FirstOrDefault();
                if (below != null) list.Add((Math.Max(0, Math.Min(r.Length, u)), p, below));
            }
            return list;
        }

        private (RafterEnd, RoofCrease) EndOf(RoofFace face, Point2 p)
        {
            var cr = Creases.Where(z => z.Kind != CreaseKind.Header && z.Touches(face) && z.DistanceTo(p) < 1e-3).OrderBy(z => z.Kind == CreaseKind.Ridge ? 0 : 1).FirstOrDefault();
            if (cr == null) return (RafterEnd.Free, null);
            return (cr.Kind == CreaseKind.Ridge ? RafterEnd.Ridge : cr.Kind == CreaseKind.Hip ? RafterEnd.Hip : RafterEnd.Valley, cr);
        }

        /// <summary>The roof's height above the roof level at a point (its highest face there; NaN: no roof).</summary>
        public double ZAt(Point2 p)
        {
            double z = double.NaN;
            foreach (var f in Faces)
                if (f.Pieces.Any(pc => Inside(pc, p, 1e-6))) { double h = f.Plane.Z(p); if (double.IsNaN(z) || h > z) z = h; }
            return z;
        }
    }
}
