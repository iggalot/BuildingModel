using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Edits;
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
        /// <summary>
        /// Its plane under another section's roof, inside its own walls (owner 2026-11-14: overframed where possible): its
        /// rafters run on through it whole to their walls, the other roof built on top of it. Not roof surface: no load of its own.
        /// </summary>
        public List<Convex> Hidden = new List<Convex>();
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

    /// <summary>
    /// One wall of a dormer (owner 2026-11-13): a cheek (along the rafters of the roof below: a triangle on its slope) or its
    /// front (across them: its gable over a strip), standing on the roof below. Not a shear wall. Its shape along its line,
    /// sample by sample: the roof below and its own roof over it (ft above the roof level's plates).
    /// </summary>
    public sealed class DormerWall
    {
        public string Id;
        public int Section;
        /// <summary>"south", "north", "west", "east": which of its section's wall lines.</summary>
        public string Side;
        /// <summary>"cheek", "front" (or "wall" with no roof under it).</summary>
        public string Kind;
        public List<(Point2 p, double bottom, double top)> Samples = new List<(Point2, double, double)>();
        /// <summary>The walls below it stands on ("wall W3"; "(part)" when only part way: a beam under each stretch with nothing under it), or null: all on the roof.</summary>
        public string OnWall;
        public Point2 A => Samples.Count > 0 ? Samples[0].p : default(Point2);
        public Point2 B => Samples.Count > 0 ? Samples[Samples.Count - 1].p : default(Point2);
        /// <summary>Its length on plan (ft).</summary>
        public double Length => RoofCrease.Dist(A, B);
        /// <summary>Its tallest (ft).</summary>
        public double Height => Samples.Count == 0 ? 0 : Samples.Max(s => s.top - s.bottom);
        /// <summary>Its area in elevation (sq ft): the height along its line, by the trapezoid rule.</summary>
        public double Area
        {
            get
            {
                double s = 0;
                for (int k = 1; k < Samples.Count; k++)
                    s += RoofCrease.Dist(Samples[k - 1].p, Samples[k].p) * ((Samples[k - 1].top - Samples[k - 1].bottom) + (Samples[k].top - Samples[k].bottom)) / 2;
                return s;
            }
        }
        public override string ToString() => $"dormer {Kind} {Id}: {Length:0.##} ft long, {Height:0.##} ft high, {Area:0.#} sq ft{(OnWall != null ? $", on {OnWall}" : "")}";
    }

    /// <summary>
    /// What holds a dormer wall up (owner 2026-11-13: "a beam object under every dormer wall ... parallel to the rafters next to
    /// it"): under a cheek a beam on the trimmer's line, sloping with the roof below; under the front the header. On plan A to
    /// B, its top at ZA and ZB (ft above the roof level's plates). Sized in the roof framing check (the Load Manager's joists window: <see cref="Roof.CheekBeamSize"/>, <see cref="Roof.HeaderSize"/>).
    /// </summary>
    public sealed class DormerBeam
    {
        public string Id;
        /// <summary>"cheek beam" or "header".</summary>
        public string Kind;
        public DormerWall Wall;
        /// <summary>The header it is (a front's).</summary>
        public RoofCrease Header;
        public Point2 A, B;
        public double ZA, ZB;
        /// <summary>On plan (ft).</summary>
        public double Length => RoofCrease.Dist(A, B);
        /// <summary>Along its slope (ft).</summary>
        public double SlopedLength => Math.Sqrt(Length * Length + (ZB - ZA) * (ZB - ZA));
        public override string ToString() => $"{Kind} {Id} under {Wall?.Id}: {SlopedLength:0.##} ft";
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
        /// <summary>The dormers' walls: each one's cheeks and front, standing on the roof below (<see cref="LayDormers"/>).</summary>
        public List<DormerWall> DormerPanels = new List<DormerWall>();
        /// <summary>What holds each dormer wall up: a beam under each cheek along the rafters, the header under its front.</summary>
        public List<DormerBeam> DormerBeams = new List<DormerBeam>();
        /// <summary>Why it cannot be laid out (null: it is).</summary>
        public string NotLaidOut;
        /// <summary>
        /// Overframed (owner 2026-11-14: "Overframed where possible. Only when the ceiling is vaulted is this not possible."): where a
        /// wing's ridge dies into a roof, that roof is framed through under the wing (<see cref="RoofFace.Hidden"/>), the wing's
        /// valleys laid on it, bearing on each of its rafters they cross. False (a vault): cut in, each valley's top on its rafters
        /// there. A dormer is cut in either way.
        /// </summary>
        public bool Overframed = true;
        /// <summary>The roof's eave overhang (ft on plan; a section's own may differ, <see cref="OverhangOf"/>).</summary>
        public double RoofOverhang;
        /// <summary>
        /// Each section's gable ends that run on into a higher section ("south", "north", "west", "east"): the gable's slopes go on
        /// over that roof until they die into it, the valleys where they meet (owner 2026-11-13: a cross gable or a dormer frames
        /// into the main roof, no gable wall standing on it). The section's plan reach there is <see cref="Reach"/>.
        /// </summary>
        public List<HashSet<string>> OpenEnds = new List<HashSet<string>>();
        /// <summary>Each section's reach on plan: its eaves, run on past its open ends.</summary>
        public List<Rect2> Reach = new List<Rect2>();

        /// <summary>The roof's sections: its own, else the one over its plan (its shape and ridge); none for a flat roof.</summary>
        public static List<RoofSection> SectionsOf(Building b)
        {
            var roof = b?.Roof;
            if (roof == null || !roof.IsSloped) return new List<RoofSection>();
            // each section on the roof level's diaphragm under it (its own height: WallTops), its eave rise over that
            var top = b.RoofLevel();
            RoofSection On(RoofSection s)
            {
                var c = s.Clone();
                c.EaveRise += b.DiaphragmOffsetAt(top, new Point2((s.Walls.X0 + s.Walls.X1) / 2, (s.Walls.Y0 + s.Walls.Y1) / 2));
                return c;
            }
            if (roof.Sections != null && roof.Sections.Count > 0) return roof.Sections.Select(On).ToList();
            var plan = b.RoofPlan();
            if (plan.IsEmpty) return new List<RoofSection>();
            return new List<RoofSection> { On(new RoofSection { Name = "the roof", Walls = plan, Shape = roof.Shape, Ridge = b.EffectiveRidge() }) };
        }

        /// <summary>
        /// Laid out by these sections (a hip roof, a roof of its own sections, or one with purlins): the plain gable over the plan
        /// keeps the gable's own layout (<see cref="Joists"/>).
        /// </summary>
        public static bool IsSectioned(Building b) =>
            b?.Roof != null && b.Roof.IsSloped && (b.Roof.Shape == RoofShape.Hip || (b.Roof.Sections != null && b.Roof.Sections.Count > 0) ||
                                                   (b.Roof.Purlins != null && b.Roof.Purlins.Count > 0));

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

        /// <summary>A dormer: its walls stand on another section's roof (inside that section's walls), its eave raised.</summary>
        public static bool IsDormer(IList<RoofSection> sections, int i)
        {
            var w = sections[i].Walls;
            if (!(sections[i].EaveRise > E)) return false;
            for (int j = 0; j < sections.Count; j++)
            {
                if (j == i) continue;
                var o = sections[j].Walls;
                if (w.X0 > o.X0 - E && w.X1 < o.X1 + E && w.Y0 > o.Y0 - E && w.Y1 < o.Y1 + E) return true;
            }
            return false;
        }

        /// <summary>A section's own surface over a plan point: the lowest of its planes (ft above the wall tops).</summary>
        private static double SurfaceOf(RoofFrame f, int section, double x, double y) =>
            f.Planes.Where(p => p.Section == section).Select(p => p.Z(x, y)).DefaultIfEmpty(double.NegativeInfinity).Min();

        /// <summary>A gable's ridge W-E (Auto: along the longer side; a hip always).</summary>
        public static bool RidgeWestEast(RoofSection s) =>
            s.Shape == RoofShape.Gable && s.Ridge != RidgeAxis.Auto ? s.Ridge == RidgeAxis.WestEast : s.Walls.Width >= s.Walls.Height;

        /// <param name="spacing">The rafters' spacing (ft).</param>
        public static RoofFrame Of(Building b, List<RoofSection> sections, double roofPitch, double roofOverhang, double spacing)
        {
            var f = new RoofFrame { Sections = sections, Overframed = b?.Roof?.NoCeilingJoists != true, RoofOverhang = roofOverhang };
            var roofLevel = b?.RoofLevel();
            if (roofLevel != null) f.wallsBelow = BeamSupports.WallsUnder(b, roofLevel);
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

            // Two roofs merged (owner 2026-11-13: a cross gable's ridge runs back into the hip roof, two valleys, a smooth
            // interface; the plate heights may differ; the hip's eaves stop, never running on across the gable under its roof):
            //  1. a gable's end butting into a higher section runs on: its ridge goes on along its line to where that section's
            //     roof rises to it, its slopes with it, so the two roofs meet in valleys. An end where the other roof never rises
            //     to the ridge (a dormer's front, a wing taller than the main roof) stays a gable end.
            //  2. a slope running down to a wall another section stands against runs on past its eaves over that section, until
            //     it is under that section's lowest eave: where that section's plate is lower, the slope comes down to meet its
            //     roof (a valley), not an eave and fascia standing over it.
            // The highest roof then wins at every point, as before.
            var cover = new List<Rect2>();
            var bands = new List<List<Rect2>>();
            for (int i = 0; i < sections.Count; i++)
            {
                var s = sections[i];
                f.OpenEnds.Add(new HashSet<string>());
                var reach = EavesOf(s, roofOverhang);
                var cov = IsDormer(sections, i) ? s.Walls : reach;   // a dormer's eaves overhang the roof below, which runs on under them
                if (IsDormer(sections, i))
                {
                    // a dormer wall flush with the wall below (owner 2026-11-14: pushed out to the outside wall line, that wall holds
                    // it up): the roof below stops at its sides there, its own roof overhangs the wall, no rafter tails under it
                    var w = s.Walls;
                    foreach (var o in sections.Where((o, j) => j != i && !IsDormer(sections, j)).Select(o => o.Walls))
                    {
                        if (!(w.X0 > o.X0 - E && w.X1 < o.X1 + E && w.Y0 > o.Y0 - E && w.Y1 < o.Y1 + E)) continue;
                        cov = new Rect2(Math.Abs(w.X0 - o.X0) < E ? reach.X0 : cov.X0, Math.Abs(w.Y0 - o.Y0) < E ? reach.Y0 : cov.Y0,
                                        Math.Abs(w.X1 - o.X1) < E ? reach.X1 : cov.X1, Math.Abs(w.Y1 - o.Y1) < E ? reach.Y1 : cov.Y1);
                    }
                }
                if (s.Shape == RoofShape.Gable)
                {
                    var w = s.Walls;
                    bool we = RidgeWestEast(s);
                    double top = SurfaceOf(f, i, (w.X0 + w.X1) / 2, (w.Y0 + w.Y1) / 2);   // its ridge's height
                    foreach (var (side, dir) in we ? new[] { ("west", -1), ("east", 1) } : new[] { ("south", -1), ("north", 1) })
                    {
                        double end = we ? (dir < 0 ? w.X0 : w.X1) : (dir < 0 ? w.Y0 : w.Y1);
                        double ridge = we ? (w.Y0 + w.Y1) / 2 : (w.X0 + w.X1) / 2;
                        for (int j = 0; j < sections.Count; j++)
                        {
                            if (j == i) continue;
                            var o = sections[j].Walls;
                            // the end's line on or inside the other's walls
                            bool on = we ? end >= o.X0 - E && end <= o.X1 + E && w.Y0 >= o.Y0 - E && w.Y1 <= o.Y1 + E
                                         : end >= o.Y0 - E && end <= o.Y1 + E && w.X0 >= o.X0 - E && w.X1 <= o.X1 + E;
                            if (!on) continue;
                            // walk out along the ridge line until the other roof reaches the ridge, within its eaves
                            var oe = EavesOf(sections[j], roofOverhang);
                            double limit = we ? (dir < 0 ? oe.X0 : oe.X1) : (dir < 0 ? oe.Y0 : oe.Y1);
                            double Gap(double u) => SurfaceOf(f, j, we ? u : ridge, we ? ridge : u) - top;
                            double meets = double.NaN;
                            for (double u = end; dir < 0 ? u >= limit - E : u <= limit + E; u += dir * 0.05)
                                if (Gap(u) >= -1e-9)
                                {
                                    // exactly where: halved between the last step under it and this one
                                    double lo = u - dir * 0.05, hi = u;
                                    if (dir < 0 ? lo > end : lo < end) lo = end;
                                    for (int it = 0; it < 60; it++) { double mid = (lo + hi) / 2; if (Gap(mid) >= -1e-12) hi = mid; else lo = mid; }
                                    meets = hi; break;
                                }
                            if (double.IsNaN(meets) || Math.Abs(meets - end) < E) continue;
                            f.OpenEnds[i].Add(side);
                            // run on to where the ridge meets the other roof, no further (past its ridge the other roof falls away);
                            // never short of its own eaves
                            if (we)
                            {
                                reach = dir < 0 ? new Rect2(Math.Min(reach.X0, meets), reach.Y0, reach.X1, reach.Y1) : new Rect2(reach.X0, reach.Y0, Math.Max(reach.X1, meets), reach.Y1);
                                cov = dir < 0 ? new Rect2(Math.Min(cov.X0, meets), cov.Y0, cov.X1, cov.Y1) : new Rect2(cov.X0, cov.Y0, Math.Max(cov.X1, meets), cov.Y1);
                            }
                            else
                            {
                                reach = dir < 0 ? new Rect2(reach.X0, Math.Min(reach.Y0, meets), reach.X1, reach.Y1) : new Rect2(reach.X0, reach.Y0, reach.X1, Math.Max(reach.Y1, meets));
                                cov = dir < 0 ? new Rect2(cov.X0, Math.Min(cov.Y0, meets), cov.X1, cov.Y1) : new Rect2(cov.X0, cov.Y0, cov.X1, Math.Max(cov.Y1, meets));
                            }
                            break;
                        }
                    }
                }
                f.Reach.Add(reach);
                cover.Add(cov);
            }
            for (int i = 0; i < sections.Count; i++)
            {
                var s = sections[i];
                var w = s.Walls;
                double oi = OverhangOf(s, roofOverhang), ti = Math.Tan(PitchOf(s, roofPitch) * Math.PI / 180);
                var mine = new List<Rect2>();
                for (int j = 0; j < sections.Count; j++)
                {
                    if (j == i) continue;
                    var o = sections[j].Walls;
                    // a dormer (its walls standing on the other's roof) keeps its eaves: its fascia stands over that roof
                    if (w.X0 > o.X0 - E && w.X1 < o.X1 + E && w.Y0 > o.Y0 - E && w.Y1 < o.Y1 + E) continue;
                    double oj = OverhangOf(sections[j], roofOverhang), tj = Math.Tan(PitchOf(sections[j], roofPitch) * Math.PI / 180);
                    // how far past its wall the slope must run to be under the other's lowest eave (its eave's edge)
                    double depth = (s.EaveRise - sections[j].EaveRise + tj * oj) / ti;
                    if (depth <= oi + E) continue;
                    var ej = f.Reach[j];
                    var ei = f.Reach[i];
                    foreach (var side in new[] { "south", "north", "west", "east" })
                    {
                        if (!f.Planes.Any(p => p.Section == i && p.Side == side)) continue;   // no slope of its own coming down there
                        // past its eave on that side, along its reach, over the other's roof (its reach) only
                        var band = side == "south" ? new Rect2(ei.X0, w.Y0 - depth, ei.X1, w.Y0 - oi)
                                 : side == "north" ? new Rect2(ei.X0, w.Y1 + oi, ei.X1, w.Y1 + depth)
                                 : side == "west" ? new Rect2(w.X0 - depth, ei.Y0, w.X0 - oi, ei.Y1)
                                 : new Rect2(w.X1 + oi, ei.Y0, w.X1 + depth, ei.Y1);
                        double x0 = Math.Max(band.X0, ej.X0), x1 = Math.Min(band.X1, ej.X1), y0 = Math.Max(band.Y0, ej.Y0), y1 = Math.Min(band.Y1, ej.Y1);
                        // along the other's walls only, not over its eaves (owner 2026-11-13: at an L's inside corner the lower
                        // eave runs straight to the first wall it meets; over the other's eave strip the two planes would cross
                        // on a diagonal, a mitre)
                        if (side == "south" || side == "north") { x0 = Math.Max(x0, o.X0); x1 = Math.Min(x1, o.X1); }
                        else { y0 = Math.Max(y0, o.Y0); y1 = Math.Min(y1, o.Y1); }
                        if (x1 - x0 <= E || y1 - y0 <= E) continue;
                        mine.Add(new Rect2(x0, y0, x1, y1));
                    }
                }
                bands.Add(mine);
            }

            // the faces: each plane where it is its section's lowest, less where another section is higher
            foreach (var pl in f.Planes)
            {
                var s = sections[pl.Section];
                var pieces = new List<Convex>();
                var done = new List<Rect2>();
                foreach (var rect in new[] { f.Reach[pl.Section] }.Concat(bands[pl.Section]))
                {
                    var piece = Convex.Of(rect);
                    foreach (var o in f.Planes.Where(o => o.Section == pl.Section && o != pl))
                        piece = piece.Clip(o.Gx - pl.Gx, o.Gy - pl.Gy, o.Z0 - pl.Z0);
                    // never twice: less what the rectangles before it have
                    var parts = piece.IsEmpty ? new List<Convex>() : new List<Convex> { piece };
                    foreach (var d0 in done)
                        parts = parts.SelectMany(p => p.Minus(new List<(double, double, double)> { (1, 0, -d0.X0), (-1, 0, d0.X1), (0, 1, -d0.Y0), (0, -1, d0.Y1) })).ToList();
                    pieces.AddRange(parts.Where(p => !p.IsEmpty));
                    done.Add(rect);
                }
                for (int j = 0; j < sections.Count && pieces.Count > 0; j++)
                {
                    if (j == pl.Section) continue;
                    // another section covers this plane where its surface is above it, over its eaves (a dormer: over its walls
                    // only: its eaves overhang the roof below, which runs on under them)
                    foreach (var cj in new[] { cover[j] }.Concat(bands[j]))
                    {
                        var region = new List<(double, double, double)> { (1, 0, -cj.X0), (-1, 0, cj.X1), (0, 1, -cj.Y0), (0, -1, cj.Y1) };
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
            if (f.Overframed) f.FrameThrough(sections, cover);

            f.LayRafters(sections, spacing, roofOverhang);
            f.Headers(sections);
            f.LayDormers(sections);
            return f;
        }

        /// <summary>
        /// Each dormer's walls (owner 2026-11-13: not shear walls, but wall sections in the model, for the take-off; "two
        /// sidewalls, and the window wall that connects them; all need beams or supports"): on each of its wall lines but a
        /// gable end that runs on into the roof (<see cref="OpenEnds"/>), from the roof below up to its own roof, where that is
        /// higher (a cheek: a triangle on the slope; the front: its gable over a strip). Under each wall along the rafters of the
        /// roof below, a beam on that line (the trimmer's), from that section's wall up the slope as far as the rafters there run;
        /// under a wall across them, the header from trimmer to trimmer (<see cref="Headers"/>). Laid out here; sized in the roof framing check.
        /// </summary>
        private void LayDormers(List<RoofSection> sections)
        {
            int nw = 0, nb = 0;
            for (int i = 0; i < sections.Count; i++)
            {
                if (!IsDormer(sections, i)) continue;
                var w = sections[i].Walls;
                foreach (var (side, a, b) in new[] { ("south", new Point2(w.X0, w.Y0), new Point2(w.X1, w.Y0)), ("north", new Point2(w.X0, w.Y1), new Point2(w.X1, w.Y1)),
                                                     ("west", new Point2(w.X0, w.Y0), new Point2(w.X0, w.Y1)), ("east", new Point2(w.X1, w.Y0), new Point2(w.X1, w.Y1)) })
                {
                    if (OpenEnds.Count > i && OpenEnds[i].Contains(side)) continue;
                    double len = RoofCrease.Dist(a, b);
                    int n = Math.Max(2, (int)Math.Ceiling(len / 0.25));
                    // along the line: the roof below and its own roof; the wall where its own is the higher, the ends trimmed to
                    // one point of no height each side
                    var all = new List<(Point2 p, double bottom, double top)>();
                    for (int k = 0; k <= n; k++)
                    {
                        var p = new Point2(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
                        double bottom = SurfaceBelow(sections, i, p), top = SurfaceOf(this, i, p.X, p.Y);
                        all.Add((p, bottom, Math.Max(bottom, top)));
                    }
                    int first = all.FindIndex(s => s.top > s.bottom + 1e-3), last = all.FindLastIndex(s => s.top > s.bottom + 1e-3);
                    if (first < 0) continue;
                    var samples = all.GetRange(Math.Max(0, first - 1), Math.Min(all.Count - 1, last + 1) - Math.Max(0, first - 1) + 1);
                    var wall = new DormerWall { Section = i, Side = side, Samples = samples };
                    if (wall.Area < 0.25) continue;
                    wall.Id = $"DW{++nw}";
                    var mid = wall.Samples[wall.Samples.Count / 2].p;
                    var below = Faces.Where(fc => fc.Plane.Section != i && fc.Pieces.Any(pc => Inside(pc, mid, 1e-6))).OrderByDescending(fc => fc.Plane.Z(mid)).FirstOrDefault();
                    // a cheek along the rafters of the roof below, the front across them; with no roof under it (flush with the wall
                    // below) by its own ridge: the gable end its front
                    bool weLine = Math.Abs(b.Y - a.Y) < E;
                    wall.Kind = below != null ? (below.Plane.AlongX == weLine ? "cheek" : "front")
                              : sections[i].Shape == RoofShape.Gable ? (RidgeWestEast(sections[i]) == weLine ? "cheek" : "front") : "wall";
                    DormerPanels.Add(wall);
                    // where it stands on a wall below (owner 2026-11-13: "if there's actually a wall already below them, you don't need
                    // to add a beam ... only the stretch with nothing under it gets the beam"): it bears on that wall there
                    bool lx = Math.Abs(b.Y - a.Y) < E;   // the wall's line W-E: along it is x
                    double U(Point2 p) => lx ? p.X : p.Y;
                    var tall = wall.Samples.Where(z => z.top > z.bottom + 1e-3).Select(z => (u: U(z.p), on: WallBelowAt(sections, i, z.p))).OrderBy(z => z.u).ToList();
                    var names = tall.Where(z => z.on != null).Select(z => z.on).Distinct().ToList();
                    // the stretches with nothing under it: (from, to, a wall ends it there before, after)
                    var bare = new List<(double s, double e, bool wallBefore, bool wallAfter)>();
                    for (int k = 0; k < tall.Count; k++)
                    {
                        if (tall[k].on != null) continue;
                        int m = k;
                        while (m + 1 < tall.Count && tall[m + 1].on == null) m++;
                        bool before = k > 0, after = m + 1 < tall.Count;
                        bare.Add((before ? tall[k - 1].u : tall[k].u, after ? tall[m + 1].u : tall[m].u, before, after));
                        k = m;
                    }
                    if (names.Count > 0) wall.OnWall = string.Join(", ", names) + (bare.Count > 0 ? " (part)" : "");
                    if (bare.Count == 0) continue;
                    // each stretch's beam, from the wall below that ends it (it bears there) or else on along the framing to its
                    // supports: a cheek's the trimmer's run, eave wall to ridge; a front's the header's, trimmer to trimmer
                    double line = lx ? a.Y : a.X;
                    Point2 At(double u) => lx ? new Point2(u, line) : new Point2(line, u);
                    double r0, r1; RoofCrease hd = null;
                    if (below == null)
                    {
                        // no roof under it (flush with the wall below, the roof below stopping at its sides): over a gap in that wall
                        // a beam from wall end to wall end
                        r0 = double.PositiveInfinity; r1 = double.NegativeInfinity;
                    }
                    else if (wall.Kind == "cheek")
                    {
                        bool x = below.Plane.AlongX;
                        var on = Rafters.Where(r => r.Face == below && r.AlongX == x && Math.Abs(r.Line - line) < 1e-6).ToList();
                        var bw = sections[below.Plane.Section].Walls;
                        double lo = x ? bw.X0 : bw.Y0, hi = x ? bw.X1 : bw.Y1;
                        r0 = Math.Max(lo, on.Count > 0 ? on.Min(r => x ? r.Start.X : r.Start.Y) : Math.Min(U(a), U(b)));
                        r1 = Math.Min(hi, on.Count > 0 ? on.Max(r => x ? r.End.X : r.End.Y) : Math.Max(U(a), U(b)));
                    }
                    else
                    {
                        hd = Creases.FirstOrDefault(c => c.Kind == CreaseKind.Header && c.DistanceTo(mid) < 1e-3);
                        if (hd == null) continue;
                        r0 = Math.Min(U(hd.A), U(hd.B)); r1 = Math.Max(U(hd.A), U(hd.B));
                    }
                    foreach (var (s, e, wb, wa) in bare)
                    {
                        double u0 = wb ? s : Math.Min(s, r0), u1 = wa ? e : Math.Max(e, r1);
                        if (u1 - u0 < 0.5) continue;
                        Point2 s0 = At(u0), s1 = At(u1);
                        double z0 = hd != null ? hd.ZA + (hd.ZB - hd.ZA) * Frac(hd, s0) : SurfaceBelow(sections, i, s0);
                        double z1 = hd != null ? hd.ZA + (hd.ZB - hd.ZA) * Frac(hd, s1) : SurfaceBelow(sections, i, s1);
                        bool whole = !wb && !wa;
                        DormerBeams.Add(new DormerBeam
                        {
                            Id = $"DB{++nb}", Kind = wall.Kind == "cheek" ? "cheek beam" : "header", Wall = wall,
                            Header = whole ? hd : null, A = s0, B = s1, ZA = z0, ZB = z1,
                        });
                    }
                }
            }
        }

        /// <summary>
        /// The wall a dormer wall stands on at a point, or null (it stands on the roof below there). It touches a wall below
        /// where it is on an outside wall line of the section under it (that section's eave wall, its bottom there at the
        /// plate, or its gable end wall, up to the rake), and, in a building, on one of the walls under the roof level (a wall
        /// inside stops at the plates, under the rafters: the dormer wall does not touch it).
        /// </summary>
        private string WallBelowAt(List<RoofSection> sections, int dormer, Point2 p)
        {
            const double near = 0.26;
            int j = -1; double best = double.NegativeInfinity;
            for (int k = 0; k < sections.Count; k++)
            {
                if (k == dormer || Reach.Count <= k) continue;
                var r = Reach[k];
                if (!(r.X0 - E <= p.X && p.X <= r.X1 + E && r.Y0 - E <= p.Y && p.Y <= r.Y1 + E)) continue;
                double z = SurfaceOf(this, k, p.X, p.Y);
                if (z > best) { best = z; j = k; }
            }
            if (j < 0) return null;
            var w = sections[j].Walls;
            bool onEdge = (Math.Abs(p.X - w.X0) <= near || Math.Abs(p.X - w.X1) <= near) && p.Y >= w.Y0 - near && p.Y <= w.Y1 + near
                       || (Math.Abs(p.Y - w.Y0) <= near || Math.Abs(p.Y - w.Y1) <= near) && p.X >= w.X0 - near && p.X <= w.X1 + near;
            if (!onEdge) return null;
            if (wallsBelow == null) return $"{sections[j].Name ?? "the roof below"}'s wall";
            var under = wallsBelow.FirstOrDefault(x => Snapper.DistanceToSegment(p, new Segment2(x.Start, x.End)) <= near);
            return under == null ? null : $"wall {under.Id}";
        }

        /// <summary>Where a point is along a crease, 0 at A to 1 at B (on plan).</summary>
        private static double Frac(RoofCrease c, Point2 p)
        {
            double l = RoofCrease.Dist(c.A, c.B);
            return l < 1e-9 ? 0 : RoofCrease.Dist(c.A, p) / l;
        }

        /// <summary>The walls under the roof level (a building's; null: sections only, their outside wall lines taken as walls).</summary>
        private List<Wall> wallsBelow;

        /// <summary>The roof under a dormer at a point: the highest of the other sections' surfaces whose reach is over it.</summary>
        private double SurfaceBelow(List<RoofSection> sections, int dormer, Point2 p)
        {
            double z = double.NegativeInfinity;
            for (int j = 0; j < sections.Count; j++)
                if (j != dormer && Reach[j].X0 - E <= p.X && p.X <= Reach[j].X1 + E && Reach[j].Y0 - E <= p.Y && p.Y <= Reach[j].Y1 + E)
                    z = Math.Max(z, SurfaceOf(this, j, p.X, p.Y));
            return double.IsInfinity(z) ? 0 : z;
        }

        private static bool Inside(Convex c, Point2 p, double grow) => c.Edges().All(h => h.a * p.X + h.b * p.Y + h.c >= -grow);

        /// <summary>
        /// Each face's rafters: lines across it at the spacing from its section's wall line (one on each of its walls, none
        /// nearer an end than a quarter spacing), each line's runs through the face its rafters; each one's tributary strip
        /// half the gap to the line each side (to the face's edge past the last), its width along it exactly.
        /// Faces in one plane (a wing's slope flush with the main roof's hip face: owner 2026-11-13, "do they run continuous
        /// to the hip beam?") are framed as one: each line's rafter runs through them all, never stopping at the seam
        /// between them with nothing under it; each rafter is the face's that holds its top (its section: its pair at the
        /// ridge). A face running on past its section's walls (a wing's ridge run back over the main roof, a slope run on
        /// past its eave) has rafters there too, at the spacing on from its wall line.
        /// </summary>
        private void LayRafters(List<RoofSection> sections, double s, double roofOverhang)
        {
            int n = 0;
            var groups = new List<List<RoofFace>>();
            foreach (var fc in Faces)
            {
                var g = groups.FirstOrDefault(z => SamePlane(z[0].Plane, fc.Plane));
                if (g != null) g.Add(fc); else groups.Add(new List<RoofFace> { fc });
            }
            foreach (var group in groups)
            {
                var face = group[0];
                var pl = face.Plane;
                var w = sections[pl.Section].Walls;
                bool x = pl.AlongX;
                var all = group.SelectMany(g => g.Pieces).ToList();
                // the rafters run through its hidden pieces too (overframed: under a wing, to their walls); the strips are its surface's
                var through = all.Concat(group.SelectMany(g => g.Hidden)).ToList();
                // across: y for a W-E rafter
                double vmin = through.SelectMany(p => p.P).Min(p => x ? p.Y : p.X), vmax = through.SelectMany(p => p.P).Max(p => x ? p.Y : p.X);
                // each section's lines over its own walls (an earlier section's kept where they are too close)
                var grid = new List<double>();
                var secs = group.Select(g => g.Plane.Section).Distinct().OrderBy(z => z).ToList();
                foreach (int si in secs)
                {
                    var ws = sections[si].Walls;
                    double a0 = x ? ws.Y0 : ws.X0, a1 = x ? ws.Y1 : ws.X1;
                    var own = new List<double> { a0 };
                    for (int k = 1; a0 + k * s < a1 - E; k++) if (a1 - (a0 + k * s) >= s / 4) own.Add(a0 + k * s);
                    own.Add(a1);
                    foreach (double v in own)
                        if (!grid.Any(g => Math.Abs(g - v) < s / 4)) grid.Add(v);
                }
                double v0 = secs.Min(si => x ? sections[si].Walls.Y0 : sections[si].Walls.X0), v1 = secs.Max(si => x ? sections[si].Walls.Y1 : sections[si].Walls.X1);
                // run on past the walls (more than the eave's overhang): on at the spacing, none nearer its edge than a quarter
                double oh = OverhangOf(sections[pl.Section], roofOverhang);
                if (vmin < v0 - oh - E)
                    for (int k = 1; v0 - k * s >= vmin + s / 4; k++) grid.Add(v0 - k * s);
                if (vmax > v1 + oh + E)
                    for (int k = 1; v1 + k * s <= vmax - s / 4; k++) grid.Add(v1 + k * s);
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
                    foreach (var pc in through)
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
                    var strip = all.Select(pc => x ? pc.Clip(0, 1, -lo).Clip(0, -1, hi) : pc.Clip(1, 0, -lo).Clip(-1, 0, hi)).Where(pc => !pc.IsEmpty).ToList();
                    for (int k = 0; k < merged.Count; k++)
                    {
                        var (ua, ub) = merged[k];
                        double cutLo = k == 0 ? double.NegativeInfinity : (merged[k - 1].b + ua) / 2, cutHi = k == merged.Count - 1 ? double.PositiveInfinity : (ub + merged[k + 1].a) / 2;
                        var r = new RoofRafter { Id = $"RF{++n}", Face = face, AlongX = x, Line = v, Start = x ? new Point2(ua, v) : new Point2(v, ua), End = x ? new Point2(ub, v) : new Point2(v, ub) };
                        if (group.Count > 1)
                        {
                            // the face that holds its top, just below it
                            var top = pl.Up > 0 ? r.End : r.Start;
                            double du = pl.Up > 0 ? -0.01 : 0.01;
                            var near = x ? new Point2(top.X + du, top.Y) : new Point2(top.X, top.Y + du);
                            r.Face = group.FirstOrDefault(g => g.Pieces.Any(pc => Inside(pc, near, 1e-9))) ?? face;
                        }
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
                        (r.AtStart, r.StartCrease) = EndOf(group, r.Start);
                        (r.AtEnd, r.EndCrease) = EndOf(group, r.End);
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

        /// <summary>
        /// Overframing (owner 2026-11-14): for each wing whose ridge dies into another section's roof (a valley's top on it), that
        /// roof's planes under the wing, inside its own walls, its hidden pieces: its rafters run on through them whole. A dormer
        /// is cut in, never framed over.
        /// </summary>
        private void FrameThrough(List<RoofSection> sections, List<Rect2> cover)
        {
            var done = new HashSet<(int, int)>();
            foreach (var l in Lines.Where(z => z.Kind == CreaseKind.Valley && z.TopOnFace != null))
            {
                int a = l.TopOnFace.Plane.Section;
                var wingFace = l.Crease.F1.Plane.Section == a ? l.Crease.F2 : l.Crease.F1;
                int w = wingFace.Plane.Section;
                if (w == a || IsDormer(sections, w) || IsDormer(sections, a) || !done.Add((a, w))) continue;
                var mine = sections[a].Walls; var over = cover[w];
                var box = new Rect2(Math.Max(mine.X0, over.X0), Math.Max(mine.Y0, over.Y0), Math.Min(mine.X1, over.X1), Math.Min(mine.Y1, over.Y1));
                if (box.X1 - box.X0 <= E || box.Y1 - box.Y0 <= E) continue;
                foreach (var pl in Planes.Where(p => p.Section == a))
                {
                    var face = Faces.FirstOrDefault(fc => fc.Plane == pl);
                    if (face == null) continue;
                    var piece = Convex.Of(box);
                    foreach (var o in Planes.Where(o => o.Section == a && o != pl))
                        piece = piece.Clip(o.Gx - pl.Gx, o.Gy - pl.Gy, o.Z0 - pl.Z0);
                    var parts = piece.IsEmpty ? new List<Convex>() : new List<Convex> { piece };
                    // less the roof surface it has in that plane, what is hidden already, and every dormer's walls (cut in)
                    foreach (var vis in Faces.Where(fc => SamePlane(fc.Plane, pl)).SelectMany(fc => fc.Pieces.Concat(fc.Hidden)).ToList())
                        parts = parts.SelectMany(p => p.Minus(vis.Edges())).ToList();
                    for (int j = 0; j < sections.Count; j++)
                        if (IsDormer(sections, j))
                        {
                            var d = sections[j].Walls;
                            parts = parts.SelectMany(p => p.Minus(new List<(double, double, double)> { (1, 0, -d.X0), (-1, 0, d.X1), (0, 1, -d.Y0), (0, -1, d.Y1) })).ToList();
                        }
                    face.Hidden.AddRange(parts.Where(p => !p.IsEmpty && p.Area > 1e-3));
                }
            }
        }

        private static bool SamePlane(RoofPlane a, RoofPlane b) =>
            a.AlongX == b.AlongX && Math.Abs(a.Gx - b.Gx) < 1e-9 && Math.Abs(a.Gy - b.Gy) < 1e-9 && Math.Abs(a.Z0 - b.Z0) < 1e-9;

        private (RafterEnd, RoofCrease) EndOf(List<RoofFace> group, Point2 p)
        {
            var cr = Creases.Where(z => z.Kind != CreaseKind.Header && group.Any(z.Touches) && z.DistanceTo(p) < 1e-3).OrderBy(z => z.Kind == CreaseKind.Ridge ? 0 : 1).FirstOrDefault();
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
