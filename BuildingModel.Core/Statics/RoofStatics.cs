using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Statics
{
    /// <summary>What a roof member's support is: a wall, column or beam of the model (a landing), or another roof member.</summary>
    public enum RoofTargetKind { Wall, Column, Beam, Member, Apex, None }

    /// <summary>One support of a roof member: where along it (ft on plan from its start), what it bears on, and its reaction by load set.</summary>
    public sealed class RoofSupport
    {
        public double At;
        public Point2 Where;
        public RoofTargetKind Kind;
        /// <summary>The wall, column or beam's ID; a member's ID; the apex's key.</summary>
        public string On;
        /// <summary>A support shared between members by the lever rule (a dormer's wall on the rafters under it): each its share.</summary>
        public List<(RoofMember m, double share)> Shares;
        /// <summary>Its name for people: "W3", "hip H1", "the ridge board", "rafters of the main south face".</summary>
        public string Name;
        /// <summary>The reaction (+ up, holding the member) per load set.</summary>
        public double[] R;
    }

    /// <summary>
    /// A roof member as statics sees it: a rafter, a rafter pair across a ridge (the two one member from wall to wall, the
    /// ridge no support: their thrust tied), a hip, valley or header, or a ridge board (between the pairs, carrying the
    /// rafters and hips that meet it with no pair). On plan along its axis from <see cref="Start"/>.
    /// </summary>
    public sealed class RoofMember
    {
        public string Id;
        public string Kind;
        public Point2 Start, End;
        public double Length => RoofCrease.Dist(Start, End);
        public List<RoofRafter> Rafters = new List<RoofRafter>();
        public RoofLine Line;
        public RoofCrease Ridge;
        public List<RoofSupport> Supports = new List<RoofSupport>();
        /// <summary>Its own loads by set (its strip's), and what lands on it from the other members by set (ft from its start).</summary>
        public BeamLoads[] Own;
        public List<(double At, double[] P, string From)> Points = new List<(double, double[], string)>();
        public bool Stands => Supports.Count >= 2 || (Supports.Count == 1 && Supports[0].Kind != RoofTargetKind.None);
        public double Along(Point2 p)
        {
            double L = Length;
            return L < 1e-12 ? 0 : ((p.X - Start.X) * (End.X - Start.X) + (p.Y - Start.Y) * (End.Y - Start.Y)) / L;
        }
        public Point2 At(double u) { double L = Length; return L < 1e-12 ? Start : new Point2(Start.X + (End.X - Start.X) * u / L, Start.Y + (End.Y - Start.Y) * u / L); }
        public override string ToString() => $"{Kind} {Id}, {Length:0.##} ft, {Supports.Count} support(s)";
    }

    /// <summary>What a roof member puts on a wall, column or beam of the model (+ down) per load set, and over how wide along it.</summary>
    public sealed class RoofLanding
    {
        public RoofTargetKind Kind;
        public string On;
        public Point2 Where;
        public double[] P;
        public string From;
        /// <summary>A rafter's: its strip's width across (ft), centred on its line (0: a point, a hip's or valley's end).</summary>
        public double Width;
        public bool AlongX;
    }

    /// <summary>
    /// A sectioned roof's statics (owner 2026-11-12: hips, valleys, wings and dormers): every rafter, pair, hip, valley, header
    /// and ridge board a member on its own supports, each load set solved on its own, every member's reactions passed on to
    /// what holds it (another member: a point load on it; a wall, column or beam: a landing), round until nothing changes
    /// (linear: the members bearing on each other converge). A rafter on a dormer's wall or a valley's top on the roof below
    /// bear on that face's two rafters either side by the lever rule; the hips meeting at an apex with no ridge share it,
    /// each down to its own foot. Geometry and statics only: the loads are the caller's.
    /// </summary>
    public sealed class RoofStatics
    {
        public List<RoofMember> Members = new List<RoofMember>();
        public List<RoofLanding> Landings = new List<RoofLanding>();
        public List<string> Notes = new List<string>();
        public int Sets;
        private const double Bearing = BeamSupports.Bearing;

        /// <summary>The members, their supports found (no loads yet).</summary>
        public static RoofStatics Of(Building b, Level lv, RoofFrame f)
        {
            var beams = b.Beams.Where(m => m.LevelId == lv.Id && m.Length > 0.1).ToList();
            var ridgeBeam = b.RidgeBeam();
            if (ridgeBeam != null) beams.Add(ridgeBeam);
            return Of(f, BeamSupports.WallsUnder(b, lv), BeamSupports.ColumnsUnder(b, lv), beams);
        }

        /// <summary>The members on the walls, columns and beams under the roof.</summary>
        public static RoofStatics Of(RoofFrame f, IList<Wall> walls, IList<Column> cols, IList<Beam> beams)
        {
            var st = new RoofStatics();
            // the pairs: two rafters of one section meeting at a ridge on the same line
            var paired = new HashSet<RoofRafter>();
            var byRafter = new Dictionary<RoofRafter, RoofMember>();
            int n = 0;
            foreach (var r in f.Rafters.Where(r => r.RisesToEnd ? r.AtEnd == RafterEnd.Ridge : r.AtStart == RafterEnd.Ridge))
            {
                if (paired.Contains(r)) continue;
                var top = r.RisesToEnd ? r.End : r.Start;
                var other = f.Rafters.FirstOrDefault(o => o != r && !paired.Contains(o) && o.AlongX == r.AlongX && Math.Abs(o.Line - r.Line) < 1e-6 &&
                                                          o.Face.Plane.Section == r.Face.Plane.Section && RoofCrease.Dist(o.RisesToEnd ? o.End : o.Start, top) < 1e-6);
                if (other == null) continue;
                paired.Add(r); paired.Add(other);
                var lo = r.AlongX ? (r.Start.X < other.Start.X ? r : other) : (r.Start.Y < other.Start.Y ? r : other);
                var hi = lo == r ? other : r;
                var m = new RoofMember { Id = $"P{++n}", Kind = "rafter pair", Start = lo.Start, End = hi.End };
                m.Rafters.Add(lo); m.Rafters.Add(hi);
                st.Members.Add(m);
                byRafter[lo] = m; byRafter[hi] = m;
            }
            foreach (var r in f.Rafters.Where(r => !paired.Contains(r)))
            {
                var m = new RoofMember { Id = r.Id, Kind = r.Kind, Start = r.Start, End = r.End };
                m.Rafters.Add(r);
                st.Members.Add(m);
                byRafter[r] = m;
            }
            var byLine = new Dictionary<RoofLine, RoofMember>();
            foreach (var l in f.Lines)
            {
                var m = new RoofMember { Id = l.Id, Kind = l.Kind == CreaseKind.Hip ? "hip rafter" : l.Kind == CreaseKind.Valley ? "valley rafter" : "header", Start = l.A, End = l.B, Line = l };
                st.Members.Add(m);
                byLine[l] = m;
            }
            // a ridge board on each ridge carrying what meets it with no pair (a hip's top, a rafter opposite a dormer)
            var boards = new Dictionary<RoofCrease, RoofMember>();
            RoofMember Board(RoofCrease cr)
            {
                if (cr == null) return null;
                if (!boards.TryGetValue(cr, out var m))
                {
                    m = new RoofMember { Id = "ridge " + cr.Id, Kind = "ridge board", Start = cr.A, End = cr.B, Ridge = cr };
                    boards[cr] = m;
                    st.Members.Add(m);
                }
                return m;
            }

            // the lever rule onto a face's rafters at a point: the nearest each side whose run passes it
            List<(RoofMember, double)> Lever(RoofFace face, Point2 p, RoofMember except)
            {
                var cands = f.Rafters.Where(r => r.Face == face && byRafter.TryGetValue(r, out var mm) && mm != except).ToList();
                var near = new List<(RoofRafter r, double dv)>();
                foreach (var r in cands)
                {
                    double v = r.AlongX ? p.Y : p.X;
                    double u = r.AlongX ? p.X - r.Start.X : p.Y - r.Start.Y;
                    if (u <= 1e-6 || u >= r.Length - 1e-6) continue;
                    near.Add((r, r.Line - v));
                }
                var on = near.Where(t => Math.Abs(t.dv) < 1e-6).Select(t => t.r).FirstOrDefault();
                if (on != null) return new List<(RoofMember, double)> { (byRafter[on], 1) };
                var below = near.Where(t => t.dv < 0).OrderByDescending(t => t.dv).Select(t => ((RoofRafter, double)?)(t.r, -t.dv)).FirstOrDefault();
                var above = near.Where(t => t.dv > 0).OrderBy(t => t.dv).Select(t => ((RoofRafter, double)?)(t.r, t.dv)).FirstOrDefault();
                if (below == null && above == null) return new List<(RoofMember, double)>();
                if (below == null) return new List<(RoofMember, double)> { (byRafter[above.Value.Item1], 1) };
                if (above == null) return new List<(RoofMember, double)> { (byRafter[below.Value.Item1], 1) };
                double a = below.Value.Item2, c = above.Value.Item2;
                return new List<(RoofMember, double)> { (byRafter[below.Value.Item1], c / (a + c)), (byRafter[above.Value.Item1], a / (a + c)) };
            }

            void Add(RoofMember m, RoofSupport s)
            {
                s.At = Math.Max(0, Math.Min(m.Length, s.At));
                if (m.Supports.Any(x => Math.Abs(x.At - s.At) <= Bearing && x.Kind != RoofTargetKind.None)) return;
                m.Supports.Add(s);
            }
            void Model(RoofMember m)
            {
                foreach (var x in Joists.SupportsOf(m.Start, m.End, walls, cols, beams))
                    Add(m, new RoofSupport { At = x.At, Where = x.Where, On = x.On, Name = x.On, Kind = x.Kind == BeamSupportKind.Wall ? RoofTargetKind.Wall : x.Kind == BeamSupportKind.Column ? RoofTargetKind.Column : RoofTargetKind.Beam });
            }
            foreach (var m in st.Members.ToList())
            {
                if (m.Ridge != null) continue;
                Model(m);
                if (m.Line == null)
                {
                    double off = 0;
                    foreach (var r in m.Rafters)
                    {
                        void End(RafterEnd e, RoofCrease cr, Point2 p, double at)
                        {
                            if (e == RafterEnd.Hip || e == RafterEnd.Valley || e == RafterEnd.Header)
                            {
                                var l = f.Lines.FirstOrDefault(z => z.Crease == cr);
                                if (l != null) Add(m, new RoofSupport { At = at, Where = p, Kind = RoofTargetKind.Member, On = byLine[l].Id, Name = byLine[l].Kind + " " + l.Id });
                            }
                            else if (e == RafterEnd.Ridge && m.Rafters.Count == 1)
                            {
                                var bd = Board(cr);
                                Add(m, new RoofSupport { At = at, Where = p, Kind = RoofTargetKind.Member, On = bd.Id, Name = "the ridge board (" + cr.Id + ")" });
                            }
                        }
                        End(r.AtStart, r.StartCrease, r.Start, off);
                        End(r.AtEnd, r.EndCrease, r.End, off + r.Length);
                        // a dormer's rafter on its walls over the roof below
                        foreach (var (u, at, below) in f.DormerWalls(r))
                            if (!m.Supports.Any(x => Math.Abs(x.At - (off + u)) <= Bearing))
                                m.Supports.Add(new RoofSupport { At = off + u, Where = at, Kind = RoofTargetKind.Member, Name = "its wall on " + below.Name, Shares = Lever(below, at, m) });
                        off += r.Length;
                    }
                }
                else
                {
                    var l = m.Line;
                    // its high end: a hip's on its ridge's board or the apex; a valley's on the roof above; a header's both ends on its trimmers
                    if (l.Kind == CreaseKind.Hip)
                    {
                        if (l.TopRidge != null) { var bd = Board(l.TopRidge); Add(m, new RoofSupport { At = m.Length, Where = l.B, Kind = RoofTargetKind.Member, On = bd.Id, Name = "the ridge board (" + l.TopRidge.Id + ")" }); }
                        else if (l.AtApex.Count > 0) Add(m, new RoofSupport { At = m.Length, Where = l.B, Kind = RoofTargetKind.Apex, On = $"apex ({l.B.X:0.##}, {l.B.Y:0.##})", Name = "the apex" });
                    }
                    else if (l.Kind == CreaseKind.Valley && l.TopOnFace != null)
                        Add(m, new RoofSupport { At = m.Length, Where = l.B, Kind = RoofTargetKind.Member, Name = "the rafters of " + l.TopOnFace.Name, Shares = Lever(l.TopOnFace, l.B, m) });
                    else if (l.Kind == CreaseKind.Header)
                    {
                        Add(m, new RoofSupport { At = 0, Where = l.A, Kind = RoofTargetKind.Member, Name = "the trimmer", Shares = Lever(l.EndsOnFace, l.A, m) });
                        Add(m, new RoofSupport { At = m.Length, Where = l.B, Kind = RoofTargetKind.Member, Name = "the trimmer", Shares = Lever(l.EndsOnFace, l.B, m) });
                    }
                }
            }
            // the ridge boards: held at each pair crossing them
            foreach (var bd in boards.Values)
            {
                foreach (var pm in st.Members.Where(x => x.Kind == "rafter pair" && x.Rafters.Any(r => r.Face.Plane.Section == bd.Ridge.F1.Plane.Section)))
                {
                    var top = pm.Rafters[0].RisesToEnd ? pm.Rafters[0].End : pm.Rafters[0].Start;
                    if (bd.Ridge.DistanceTo(top) > 1e-6) continue;
                    Add(bd, new RoofSupport { At = bd.Along(top), Where = top, Kind = RoofTargetKind.Member, On = pm.Id, Name = "rafter pair " + pm.Id });
                }
                Model(bd);
            }
            foreach (var m in st.Members) m.Supports = m.Supports.OrderBy(x => x.At).ToList();
            // a support shared by the lever: its share's members
            foreach (var m in st.Members)
                foreach (var s in m.Supports.Where(x => x.Shares != null && x.Shares.Count == 0))
                {
                    s.Kind = RoofTargetKind.None;
                    st.Notes.Add($"{m.Kind} {m.Id}: nothing under its {s.Name} at ({s.Where.X:0.##}, {s.Where.Y:0.##}).");
                }
            return st;
        }

        /// <summary>
        /// Every member solved for its loads (<paramref name="own"/>: each member's own, by set, ft from its start; null: none),
        /// round until nothing changes; the landings on the model's walls, columns and beams.
        /// </summary>
        public void Solve(int sets, Func<RoofMember, BeamLoads[]> own)
        {
            Sets = sets;
            foreach (var m in Members) m.Own = own(m) ?? Enumerable.Range(0, sets).Select(_ => new BeamLoads()).ToArray();
            var byId = Members.ToDictionary(m => m.Id);
            var apexes = Members.Where(m => m.Supports.Any(s => s.Kind == RoofTargetKind.Apex)).GroupBy(m => m.Supports.First(s => s.Kind == RoofTargetKind.Apex).On).ToList();
            double change = double.MaxValue;
            int pass;
            for (pass = 0; pass < 500 && change > 1e-9; pass++)
            {
                // what each member gets from the others this pass, from last pass's reactions
                var incoming = Members.ToDictionary(m => m, m => new List<(double, double[], string)>());
                foreach (var m in Members)
                    foreach (var s in m.Supports.Where(x => x.R != null))
                    {
                        if (s.Kind == RoofTargetKind.Member && s.On != null && byId.TryGetValue(s.On, out var t))
                            incoming[t].Add((t.Along(s.Where), s.R.ToArray(), m.Id));
                        else if (s.Shares != null)
                            foreach (var (t2, k) in s.Shares)
                                incoming[t2].Add((t2.Along(s.Where), s.R.Select(x => x * k).ToArray(), m.Id));
                    }
                // the apex: the hips meeting share what they bring to it, each down at its own foot
                foreach (var g in apexes)
                {
                    var sum = new double[sets];
                    foreach (var m in g) foreach (var s in m.Supports.Where(x => x.Kind == RoofTargetKind.Apex && x.R != null)) for (int k = 0; k < sets; k++) sum[k] += s.R[k];
                    int count = g.Count();
                    foreach (var m in g)
                    {
                        var foot = m.Supports.Where(x => x.Kind != RoofTargetKind.Apex).OrderBy(x => x.At).FirstOrDefault();
                        if (foot != null) incoming[m].Add((foot.At, sum.Select(x => x / count).ToArray(), "the apex"));
                    }
                }
                change = 0;
                double scale = 0;
                foreach (var m in Members)
                {
                    m.Points = incoming[m];
                    var loads = Enumerable.Range(0, sets).Select(k =>
                    {
                        var q = new BeamLoads();
                        q.Points.AddRange(m.Own[k].Points); q.Spread.AddRange(m.Own[k].Spread); q.Linear.AddRange(m.Own[k].Linear);
                        foreach (var (at, p, _) in m.Points) if (p[k] != 0) q.Points.Add((Math.Max(0, Math.Min(m.Length, at)), p[k]));
                        return q;
                    }).ToArray();
                    var active = m.Supports.Where(x => x.Kind != RoofTargetKind.None).ToList();
                    double[][] R = null;
                    if (active.Count >= 2) R = BeamStatics.Reactions(m.Length, active.Select(x => x.At).ToList(), loads);
                    // one support (a stub at a hip's foot, a tail): all of it on that one
                    for (int i = 0; i < active.Count; i++)
                    {
                        var s = active[i];
                        var now = new double[sets];
                        for (int k = 0; k < sets; k++)
                            now[k] = active.Count == 1 ? TotalOf(loads[k]) : R == null ? 0 : R[k][i];
                        if (s.R != null) for (int k = 0; k < sets; k++) change = Math.Max(change, Math.Abs(now[k] - s.R[k]));
                        else change = Math.Max(change, now.Select(Math.Abs).DefaultIfEmpty(0).Max());
                        scale = Math.Max(scale, now.Select(Math.Abs).DefaultIfEmpty(0).Max());
                        s.R = now;
                    }
                }
                change /= Math.Max(1, scale);
            }
            if (change > 1e-6) Notes.Add($"The roof's members did not settle ({pass} passes): members propping each other up with nothing under them.");
            foreach (var m in Members.Where(x => x.Supports.Count(s => s.Kind != RoofTargetKind.None) == 0))
                Notes.Add($"{m.Kind} {m.Id}: nothing holds it up: its load is not traced down.");
            // the landings: what reaches the model
            foreach (var m in Members)
                foreach (var s in m.Supports.Where(x => x.R != null && (x.Kind == RoofTargetKind.Wall || x.Kind == RoofTargetKind.Column || x.Kind == RoofTargetKind.Beam)))
                {
                    RoofRafter r = null; double u = 0, off = 0;
                    foreach (var z in m.Rafters)
                    {
                        if (r == null && s.At <= off + z.Length + 1e-9) { r = z; u = s.At - off; }
                        off += z.Length;
                    }
                    Landings.Add(new RoofLanding
                    {
                        Kind = s.Kind, On = s.On, Where = s.Where, P = s.R.ToArray(), From = m.Id,
                        Width = r == null ? 0 : WidthAt(r, u), AlongX = r?.AlongX ?? false,
                    });
                }
        }

        /// <summary>A rafter's strip's width (ft on plan) at <paramref name="u"/> along it (an end: the piece ending there).</summary>
        public static double WidthAt(RoofRafter r, double u)
        {
            foreach (var (a, b, wa, wb) in r.Strip)
                if (u >= a - 1e-9 && u <= b + 1e-9) return b - a < 1e-12 ? wa : wa + (wb - wa) * Math.Max(0, Math.Min(1, (u - a) / (b - a)));
            return 0;
        }

        /// <summary>
        /// A member's own load from area loads (psf on plan, by set): each rafter's strip as a line load along it, its end
        /// pieces past the strip (<see cref="RoofRafter.BeforeArea"/>, <see cref="RoofRafter.AfterArea"/>) as points where they
        /// are. A hip, valley, header or ridge board: none (the rafters' strips cover the roof).
        /// </summary>
        public static BeamLoads[] AreaLoads(RoofMember m, IReadOnlyList<double> psf)
        {
            var q = psf.Select(_ => new BeamLoads()).ToArray();
            double off = 0;
            foreach (var r in m.Rafters)
            {
                for (int k = 0; k < psf.Count; k++)
                {
                    if (psf[k] == 0) continue;
                    foreach (var (a, b, wa, wb) in r.Strip) if (b - a > 1e-9) q[k].Linear.Add((off + a, off + b, wa * psf[k], wb * psf[k]));
                    if (r.BeforeArea > 0) q[k].Points.Add((Math.Max(0, Math.Min(m.Length, m.Along(r.BeforeAt))), r.BeforeArea * psf[k]));
                    if (r.AfterArea > 0) q[k].Points.Add((Math.Max(0, Math.Min(m.Length, m.Along(r.AfterAt))), r.AfterArea * psf[k]));
                }
                off += r.Length;
            }
            return q;
        }

        private static double TotalOf(BeamLoads q) =>
            q.Points.Sum(p => p.P) + q.Spread.Sum(s => (s.B - s.A) * s.W) + q.Linear.Sum(l => (l.B - l.A) * (l.WA + l.WB) / 2);
    }
}
