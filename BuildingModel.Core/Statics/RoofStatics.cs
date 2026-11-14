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
        /// <summary>
        /// A wall the member runs along, holding it all the way (owner 2026-11-14: a rafter does not lie along a wall; a wall
        /// parallel to the rafters is balloon framed up to the roof): one of its supports at most a foot apart along that
        /// stretch, each this long (ft) along the wall, where its share lands. 0: a support at a point.
        /// </summary>
        public double Spread;
        /// <summary>A purlin's brace (owner 2026-11-12): its rise from the wall's top to the purlin and its run on plan (ft); 0, 0: not a brace.</summary>
        public double BraceRise, BraceRun;
        public bool IsBrace => BraceRise > 0 || BraceRun > 0;
        /// <summary>A brace's length (ft) and its slope from level (degrees; 90: plumb).</summary>
        public double BraceLength => Math.Sqrt(BraceRise * BraceRise + BraceRun * BraceRun);
        public double BraceSlopeDeg => BraceRun < 1e-9 ? 90 : Math.Atan(BraceRise / BraceRun) * 180 / Math.PI;
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
        /// <summary>A purlin's model (<see cref="Roof.Purlins"/>); null: not a purlin.</summary>
        public RoofPurlin Purlin;
        /// <summary>The dormer beam it is (a header's; a beam over a gap in the wall below a dormer's front); null: none.</summary>
        public DormerBeam Dormer;
        public List<RoofSupport> Supports = new List<RoofSupport>();
        /// <summary>Its own loads by set (its strip's), and what lands on it from the other members by set (ft from its start).</summary>
        public BeamLoads[] Own;
        public List<(double At, double[] P, string From)> Points = new List<(double, double[], string)>();
        public bool Stands => Supports.Count >= 2 || (Supports.Count == 1 && Supports[0].Kind != RoofTargetKind.None);
        /// <summary>
        /// On a wall all its length (owner 2026-11-14: a wall parallel to the rafters is balloon framed up to the roof; no rafter
        /// sits on it): every support of it the wall's, a foot apart; the wall carries its strip. No rafter of its own to size.
        /// </summary>
        public bool OnWall => Supports.Count > 0 && Supports.Where(s => s.Kind != RoofTargetKind.None).All(s => s.Spread > 0);
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
            return Of(f, BeamSupports.WallsUnder(b, lv), BeamSupports.ColumnsUnder(b, lv), beams, b.Roof?.Purlins);
        }

        /// <summary>The members on the walls, columns and beams under the roof (and its purlins, each braced down to a bearing wall).</summary>
        public static RoofStatics Of(RoofFrame f, IList<Wall> walls, IList<Column> cols, IList<Beam> beams, IList<RoofPurlin> purlins = null)
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
                if (l.Kind == CreaseKind.Header) m.Dormer = f.DormerBeams.FirstOrDefault(d => d.Header == l.Crease);
                st.Members.Add(m);
                byLine[l] = m;
            }
            // a dormer's front over a gap in the wall below it, no roof under it (owner 2026-11-14): its beam, wall end to wall end
            foreach (var d in f.DormerBeams.Where(d => d.Kind == "header" && d.Header == null))
            {
                var mid = new Point2((d.A.X + d.B.X) / 2, (d.A.Y + d.B.Y) / 2);
                if (f.Creases.Any(c => c.Kind == CreaseKind.Header && c.DistanceTo(mid) < 1e-3)) continue;   // a stretch of a header: that one
                st.Members.Add(new RoofMember { Id = d.Id, Kind = "header", Start = d.A, End = d.B, Dormer = d });
            }
            st.frame = f;
            st.wallsUnder = walls;
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
                // a wall it runs along holds it all the way along (balloon framed up to the roof, owner 2026-11-14): first, its
                // supports a foot apart at most along the stretch
                double L = m.Length;
                if (L > 1e-6)
                {
                    double ux = (m.End.X - m.Start.X) / L, uy = (m.End.Y - m.Start.Y) / L;
                    foreach (var w in walls)
                    {
                        double wl = RoofCrease.Dist(w.Start, w.End);
                        if (wl < 1e-6) continue;
                        double wx = (w.End.X - w.Start.X) / wl, wy = (w.End.Y - w.Start.Y) / wl;
                        if (Math.Abs(ux * wy - uy * wx) > 1e-6) continue;
                        // on its line: both its ends within the bearing of the wall's line
                        double Off(Point2 p) => Math.Abs((p.X - w.Start.X) * wy - (p.Y - w.Start.Y) * wx);
                        if (Off(m.Start) > Bearing || Off(m.End) > Bearing) continue;
                        double a = Math.Max(0, Math.Min(m.Along(w.Start), m.Along(w.End))), c = Math.Min(L, Math.Max(m.Along(w.Start), m.Along(w.End)));
                        if (c - a <= Bearing) continue;
                        int n = Math.Max(1, (int)Math.Ceiling(c - a - 1e-9));
                        double step = (c - a) / n;
                        for (int i = 0; i <= n; i++)
                        {
                            double u = a + step * i;
                            Add(m, new RoofSupport { At = u, Where = m.At(u), On = w.Id, Name = w.Id + " (along it)", Kind = RoofTargetKind.Wall, Spread = i == 0 || i == n ? step / 2 : step });
                        }
                    }
                }
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
                    {
                        // overframed (owner 2026-11-14): laid on the roof framed through under the wing, on each of its rafters
                        // it crosses (their runs going on past it); cut in: its top on that roof's rafters either side
                        // (a dormer's valley too, over the trimmer and the rafters running on past it): its ends not held so on
                        // that roof's rafters either side there
                        int main = l.TopOnFace.Plane.Section;
                        foreach (var r in f.Rafters.Where(r => r.Face.Plane.Section == main && byRafter.ContainsKey(r)))
                        {
                            if (!Cross(l.A, l.B, r.Start, r.End, out double t)) continue;
                            var at = new Point2(l.A.X + (l.B.X - l.A.X) * t, l.A.Y + (l.B.Y - l.A.Y) * t);
                            if (RoofCrease.Dist(at, r.Start) < 1e-3 || RoofCrease.Dist(at, r.End) < 1e-3) continue;   // a rafter ending on it: not under it
                            var under = byRafter[r];
                            Add(m, new RoofSupport { At = t * m.Length, Where = at, Kind = RoofTargetKind.Member, On = under.Id, Name = $"{under.Kind} {under.Id} under it" });
                        }
                        if (!m.Supports.Any(s => s.At >= m.Length - Bearing))
                            Add(m, new RoofSupport { At = m.Length, Where = l.B, Kind = RoofTargetKind.Member, Name = "the rafters of " + l.TopOnFace.Name, Shares = Lever(l.TopOnFace, l.B, m) });
                        var mainFace = l.Crease.F1.Plane.Section == main ? l.Crease.F1 : l.Crease.F2.Plane.Section == main ? l.Crease.F2 : null;
                        if (mainFace != null && !m.Supports.Any(s => s.At <= Bearing))
                        {
                            var shares = Lever(mainFace, l.A, m);
                            if (shares.Count > 0) Add(m, new RoofSupport { At = 0, Where = l.A, Kind = RoofTargetKind.Member, Name = "the rafters of " + mainFace.Name, Shares = shares });
                        }
                    }
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
            // the purlins (owner 2026-11-12): each rafter crossing one held there; each braced down to a bearing wall under it
            int np = 0;
            foreach (var p in purlins ?? new List<RoofPurlin>())
            {
                if (p == null || p.Length < 0.1) continue;
                var pm = new RoofMember { Id = string.IsNullOrWhiteSpace(p.Id) ? $"PL{++np}" : p.Id, Kind = "purlin", Start = p.Start, End = p.End, Purlin = p };
                int crossed = 0;
                foreach (var m in st.Members.Where(x => x.Rafters.Count > 0).ToList())
                {
                    double off = 0;
                    foreach (var r in m.Rafters)
                    {
                        if (Cross(r.Start, r.End, p.Start, p.End, out double t) && t > 1e-6 && t < 1 - 1e-6)
                        {
                            var at = new Point2(r.Start.X + (r.End.X - r.Start.X) * t, r.Start.Y + (r.End.Y - r.Start.Y) * t);
                            Add(m, new RoofSupport { At = off + t * r.Length, Where = at, Kind = RoofTargetKind.Member, On = pm.Id, Name = "purlin " + pm.Id });
                            crossed++;
                        }
                        off += r.Length;
                    }
                }
                st.Members.Add(pm);
                if (crossed == 0) st.Notes.Add($"purlin {pm.Id}: no rafter crosses it (it runs along them, or off the roof): it holds nothing.");
                // its braces: no more than its spacing apart, one at each end; each to the nearest bearing wall parallel to it under
                // it within 45° of plumb (its run on plan no more than its rise)
                double L = pm.Length;
                int nb = Math.Max(1, (int)Math.Ceiling(L / p.Spacing - 1e-9));
                double dx = (p.End.X - p.Start.X) / L, dy = (p.End.Y - p.Start.Y) / L;
                for (int i = 0; i <= nb; i++)
                {
                    double a = L * i / nb;
                    var top = pm.At(a);
                    double h = f.ZAt(top);
                    Wall best = null; double run = 0; Point2 foot = top;
                    foreach (var w in walls.Where(w => p.BraceTo == null || w.Id == p.BraceTo))
                    {
                        double wl = RoofCrease.Dist(w.Start, w.End);
                        if (wl < 1e-6) continue;
                        double ex = (w.End.X - w.Start.X) / wl, ey = (w.End.Y - w.Start.Y) / wl;
                        if (Math.Abs(dx * ey - dy * ex) > 1e-6) continue;
                        double s = (top.X - w.Start.X) * ex + (top.Y - w.Start.Y) * ey;
                        if (s < -1e-6 || s > wl + 1e-6) continue;
                        var q = new Point2(w.Start.X + ex * s, w.Start.Y + ey * s);
                        double d = RoofCrease.Dist(top, q);
                        if (double.IsNaN(h) || d > h + 1e-6) continue;
                        if (best == null || d < run) { best = w; run = d; foot = q; }
                    }
                    if (best == null)
                    {
                        pm.Supports.Add(new RoofSupport { At = a, Where = top, Kind = RoofTargetKind.None, Name = $"its brace at {a:0.##} ft" });
                        st.Notes.Add($"purlin {pm.Id}: no bearing wall{(p.BraceTo == null ? "" : " " + p.BraceTo)} under its brace at ({top.X:0.##}, {top.Y:0.##}) within 45° of plumb (IRC R802.4.5): nothing holds it there.");
                        continue;
                    }
                    Add(pm, new RoofSupport { At = a, Where = foot, Kind = RoofTargetKind.Wall, On = best.Id, Name = $"brace to {best.Id}", BraceRise = h, BraceRun = run });
                }
            }
            foreach (var m in st.Members) m.Supports = m.Supports.OrderBy(x => x.At).ToList();
            // a member on a wall for part of its length and spanning off it to other supports: the wall holds it at the
            // stretch's end next to each span (a rafter bearing on a wall's top, no fixed end there), the rest of the stretch a
            // tail on it; a foot apart all along would make it a continuous beam over short spans, levering on the wall
            foreach (var m in st.Members.Where(x => x.Supports.Any(s => s.Spread > 0)))
            {
                var along = m.Supports.Where(s => s.Spread > 0).ToList();
                double a = along.Min(s => s.At), c = along.Max(s => s.At);
                var off = m.Supports.Where(s => s.Spread == 0 && s.Kind != RoofTargetKind.None && (s.At < a - Bearing || s.At > c + Bearing)).ToList();
                if (off.Count == 0) continue;   // all along its wall
                bool before = off.Any(s => s.At < a), after = off.Any(s => s.At > c);
                foreach (var s in along)
                    if ((before && Math.Abs(s.At - a) < 1e-9) || (after && Math.Abs(s.At - c) < 1e-9)) { s.Spread = 0; s.Name = s.On; }
                    else m.Supports.Remove(s);
            }
            // a rafter's tail past its wall, its tip resting on a valley, hip or header of the roof beside it (a main rafter run
            // on past its eave to a wing's valley): only bearing there, never holding it down, so the wall holds it, the tail a
            // cantilever off it (held at its tip too, a long span over the wall would lever the tip down onto that member)
            foreach (var m in st.Members.Where(x => x.Rafters.Count > 0))
            {
                var model = m.Supports.Where(s => s.Kind == RoofTargetKind.Wall || s.Kind == RoofTargetKind.Column || s.Kind == RoofTargetKind.Beam).ToList();
                if (model.Count == 0) continue;
                foreach (var s in m.Supports.Where(s => s.Kind == RoofTargetKind.Member && s.Shares == null && (s.At <= Bearing || s.At >= m.Length - Bearing)).ToList())
                {
                    var r = s.At <= Bearing ? m.Rafters[0] : m.Rafters[m.Rafters.Count - 1];
                    var w = f.Sections[r.Face.Plane.Section].Walls;
                    var p = s.At <= Bearing ? m.Start : m.End;
                    bool past = p.X < w.X0 - 1e-6 || p.X > w.X1 + 1e-6 || p.Y < w.Y0 - 1e-6 || p.Y > w.Y1 + 1e-6;
                    double oh = RoofFrame.OverhangOf(f.Sections[r.Face.Plane.Section], f.RoofOverhang);
                    if (past && model.Any(x => Math.Abs(x.At - s.At) <= oh + 1e-6)) m.Supports.Remove(s);
                }
            }
            // a support shared by the lever: its share's members
            foreach (var m in st.Members)
                foreach (var s in m.Supports.Where(x => x.Shares != null && x.Shares.Count == 0))
                {
                    s.Kind = RoofTargetKind.None;
                    st.Notes.Add($"{m.Kind} {m.Id}: nothing under its {s.Name} at ({s.Where.X:0.##}, {s.Where.Y:0.##}).");
                }
            return st;
        }

        private RoofFrame frame;
        private IList<Wall> wallsUnder;
        /// <summary>The dormers' walls' own weight (lb, dead), piece by piece: the member under it (null: the wall below, or nothing).</summary>
        private readonly List<(DormerWall wall, Point2 at, double lb, double width, RoofMember on, Wall below, RoofFace face)> dormerWeights =
            new List<(DormerWall, Point2, double, double, RoofMember, Wall, RoofFace)>();

        /// <summary>
        /// The dormers' walls' own weight (owner 2026-11-14, sizing the dormer beams): <paramref name="psf"/> (the walls' dead
        /// load per sq ft of wall) times each wall's height along it, on what holds it up there: a cheek on the rafter under it
        /// (its beam, the trimmer's line), a front on its header (or the beam over a gap in the wall below), else straight onto
        /// the wall below it. Call before <see cref="Solve"/>: its points go into the members' dead load, its landings onto the
        /// walls.
        /// </summary>
        public void DormerWallWeights(double psf)
        {
            dormerWeights.Clear();
            if (!(psf > 0) || frame == null) return;
            double lost = 0;
            foreach (var dw in frame.DormerPanels)
                for (int k = 1; k < dw.Samples.Count; k++)
                {
                    var s0 = dw.Samples[k - 1]; var s1 = dw.Samples[k];
                    double ds = RoofCrease.Dist(s0.p, s1.p), h = ((s0.top - s0.bottom) + (s1.top - s1.bottom)) / 2;
                    if (ds < 1e-9 || h <= 1e-9) continue;
                    var p = new Point2((s0.p.X + s1.p.X) / 2, (s0.p.Y + s1.p.Y) / 2);
                    double lb = psf * h * ds;
                    RoofMember OnRafter() => Members.FirstOrDefault(m => m.Rafters.Any(r => r.Face.Plane.Section != dw.Section && OnRun(r, p)));
                    RoofMember OnHeader() => Members.FirstOrDefault(m => m.Kind == "header" && RoofCrease.ToSegment(p, m.Start, m.End) < 0.01);
                    var on = dw.Kind == "cheek" ? OnRafter() ?? OnHeader() : OnHeader() ?? OnRafter();
                    var below = on != null ? null : wallsUnder?.FirstOrDefault(w => RoofCrease.ToSegment(p, w.Start, w.End) <= 0.26);
                    var face = frame.Faces.Where(fc => fc.Plane.Section != dw.Section && fc.Pieces.Any(pc => pc.Edges().All(e => e.a * p.X + e.b * p.Y + e.c >= -1e-6)))
                                          .OrderByDescending(fc => fc.Plane.Z(p)).FirstOrDefault() ?? frame.Faces.FirstOrDefault(fc => fc.Plane.Section == dw.Section);
                    if (on == null && below == null) { lost += lb; continue; }
                    dormerWeights.Add((dw, p, lb, ds, on, below, face));
                }
            if (lost > 0.5) Notes.Add($"The dormers' walls: {lost:N0} lb of their weight stands on nothing (no rafter, header or wall under it): not traced down.");
        }

        private static bool OnRun(RoofRafter r, Point2 p) =>
            r.AlongX ? Math.Abs(p.Y - r.Line) < 1e-6 && p.X >= Math.Min(r.Start.X, r.End.X) - 1e-6 && p.X <= Math.Max(r.Start.X, r.End.X) + 1e-6
                     : Math.Abs(p.X - r.Line) < 1e-6 && p.Y >= Math.Min(r.Start.Y, r.End.Y) - 1e-6 && p.Y <= Math.Max(r.Start.Y, r.End.Y) + 1e-6;

        /// <summary>
        /// Every member solved for its loads (<paramref name="own"/>: each member's own, by set, ft from its start; null: none),
        /// round until nothing changes; the landings on the model's walls, columns and beams.
        /// </summary>
        public void Solve(int sets, Func<RoofMember, BeamLoads[]> own)
        {
            Sets = sets;
            foreach (var m in Members) m.Own = own(m) ?? Enumerable.Range(0, sets).Select(_ => new BeamLoads()).ToArray();
            // the dormers' walls' weight: on the member under it, else straight onto the wall below
            foreach (var x in dormerWeights.Where(z => z.on == null))
            {
                var p = new double[sets]; p[0] = x.lb;
                Landings.Add(new RoofLanding { Kind = RoofTargetKind.Wall, On = x.below.Id, Where = x.at, P = p, From = x.wall.Id, Width = x.width,
                                               AlongX = Math.Abs(x.below.End.Y - x.below.Start.Y) < 1e-6 });
            }
            var byId = Members.ToDictionary(m => m.Id);
            var apexes = Members.Where(m => m.Supports.Any(s => s.Kind == RoofTargetKind.Apex)).GroupBy(m => m.Supports.First(s => s.Kind == RoofTargetKind.Apex).On).ToList();
            double change = double.MaxValue;
            int pass;
            for (pass = 0; pass < 500 && change > 1e-9; pass++)
            {
                // what each member gets from the others this pass, from last pass's reactions
                var incoming = Members.ToDictionary(m => m, m => new List<(double, double[], string)>());
                // the dormers' walls standing on it (their own weight, dead)
                foreach (var x in dormerWeights.Where(z => z.on != null))
                {
                    var w = new double[sets]; w[0] = x.lb;
                    incoming[x.on].Add((Math.Max(0, Math.Min(x.on.Length, x.on.Along(x.at))), w, x.wall.Id));
                }
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
                    // all along its wall: each support its stretch's load (the wall carries it where it is, no beam over it)
                    if (m.OnWall && active.Count >= 2) R = Tributary(m.Length, active.Select(x => x.At).ToList(), loads);
                    else if (m.Rafters.Count == 2) R = PairReactions(m, active, loads, sets);
                    else if (active.Count >= 2) R = BeamStatics.Reactions(m.Length, active.Select(x => x.At).ToList(), loads);
                    // one support (a stub at a hip's foot, a tail): all of it on that one
                    for (int i = 0; i < active.Count; i++)
                    {
                        var s = active[i];
                        var now = new double[sets];
                        for (int k = 0; k < sets; k++)
                            now[k] = active.Count == 1 && m.Rafters.Count != 2 ? TotalOf(loads[k]) : R == null ? 0 : R[k][i];
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
                        // along a wall: its stretch of the wall; else its strip's width across, centred on its line
                        Width = s.Spread > 0 ? s.Spread : r == null ? 0 : WidthAt(r, u), AlongX = r?.AlongX ?? false,
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
        public static BeamLoads[] AreaLoads(RoofMember m, IReadOnlyList<double> psf) => AreaLoads(m, psf.Count, (r, p) => psf, double.PositiveInfinity);

        /// <summary>
        /// The same with the loads varying over the roof (<paramref name="psfAt"/>: psf on plan by set at a point of a rafter's
        /// strip, its line): each strip cut into pieces no longer than <paramref name="step"/> ft, each piece's load at its middle.
        /// </summary>
        public static BeamLoads[] AreaLoads(RoofMember m, int sets, Func<RoofRafter, Point2, IReadOnlyList<double>> psfAt, double step = 1)
        {
            var q = Enumerable.Range(0, sets).Select(_ => new BeamLoads()).ToArray();
            double off = 0;
            void Add(double a, double b, double wa, double wb, IReadOnlyList<double> psf)
            {
                for (int k = 0; k < sets; k++) if (psf[k] != 0) q[k].Linear.Add((a, b, wa * psf[k], wb * psf[k]));
            }
            void Point(double u, double area, IReadOnlyList<double> psf)
            {
                for (int k = 0; k < sets; k++) if (psf[k] != 0) q[k].Points.Add((Math.Max(0, Math.Min(m.Length, u)), area * psf[k]));
            }
            foreach (var r in m.Rafters)
            {
                foreach (var (a, b, wa, wb) in r.Strip)
                {
                    if (b - a <= 1e-9) continue;
                    int n = double.IsInfinity(step) ? 1 : Math.Max(1, (int)Math.Ceiling((b - a) / step - 1e-9));
                    for (int i = 0; i < n; i++)
                    {
                        double a1 = a + (b - a) * i / n, b1 = a + (b - a) * (i + 1) / n;
                        double w1 = wa + (wb - wa) * i / n, w2 = wa + (wb - wa) * (i + 1) / n;
                        Add(off + a1, off + b1, w1, w2, psfAt(r, r.At((a1 + b1) / 2)));
                    }
                }
                if (r.BeforeArea > 0) Point(m.Along(r.BeforeAt), r.BeforeArea, psfAt(r, r.BeforeAt));
                if (r.AfterArea > 0) Point(m.Along(r.AfterAt), r.AfterArea, psfAt(r, r.AfterAt));
                off += r.Length;
            }
            return q;
        }

        /// <summary>
        /// The roof's own loads as applied (by face, set by set): every member's, each piece at its resultant on plan; what the
        /// roof's free body takes as its external loads, the same as statics was given.
        /// </summary>
        public IEnumerable<(RoofFace face, Point2 at, double[] p)> Applied()
        {
            // the dormers' walls' weight, on the roof face under each piece (applied once: the members' own points skip it)
            foreach (var x in dormerWeights.Where(z => z.face != null))
            {
                var v = new double[Sets]; v[0] = x.lb;
                yield return (x.face, x.at, v);
            }
            foreach (var m in Members.Where(x => x.Own != null && x.Rafters.Count > 0))
            {
                RoofFace FaceAt(double u)
                {
                    double off = 0;
                    foreach (var r in m.Rafters) { if (u <= off + r.Length + 1e-9) return r.Face; off += r.Length; }
                    return m.Rafters.Last().Face;
                }
                for (int k = 0; k < Sets; k++)
                {
                    foreach (var (at, p) in m.Own[k].Points)
                    {
                        var v = new double[Sets]; v[k] = p;
                        yield return (FaceAt(at), m.At(at), v);
                    }
                    foreach (var (a, b, wa, wb) in m.Own[k].Lines())
                    {
                        double f = (b - a) * (wa + wb) / 2;
                        if (Math.Abs(f) < 1e-12) continue;
                        double c = Math.Abs(wa + wb) < 1e-12 ? (a + b) / 2 : a + (b - a) * (wa + 2 * wb) / (3 * (wa + wb));
                        var v = new double[Sets]; v[k] = f;
                        yield return (FaceAt((a + b) / 2), m.At(c), v);
                    }
                }
            }
        }

        /// <summary>
        /// A rafter pair's reactions (+ up) by set on its supports (<paramref name="active"/>, in order): a three-hinged frame,
        /// each half a beam from its own supports to the ridge (the ridge one of them: a pin), what the two put on the ridge
        /// carried down the rafters to the two heels (the supports farthest from it each side) as an arch takes a load at its
        /// crown, P b / (a + b) and P a / (a + b) on plan: with only the heels under it the same as one beam heel to heel. A
        /// wall, column or beam under the ridge takes it there instead (a ridge beam). Null: a half with nothing under it.
        /// </summary>
        private static double[][] PairReactions(RoofMember m, List<RoofSupport> active, BeamLoads[] loads, int sets)
        {
            double ridge = m.Rafters[0].Length, L = m.Length;
            var R = Enumerable.Range(0, sets).Select(_ => new double[active.Count]).ToArray();
            int real = active.FindIndex(x => Math.Abs(x.At - ridge) <= Bearing);
            var left = Enumerable.Range(0, active.Count).Where(i => active[i].At < ridge - Bearing).ToList();
            var right = Enumerable.Range(0, active.Count).Where(i => active[i].At > ridge + Bearing).ToList();
            if (left.Count == 0 && right.Count == 0 && real < 0) return null;
            var ridgeLoad = new double[sets];
            bool Half(List<int> idx, double u0, double u1, bool low)
            {
                // its loads, moved to start at u0 (a point at the ridge: on the low half)
                var q = loads.Select(z =>
                {
                    var h = new BeamLoads();
                    foreach (var (a, b, wa, wb) in z.Lines())
                        if (b > u0 + 1e-9 && a < u1 - 1e-9)
                        {
                            double a1 = Math.Max(a, u0), b1 = Math.Min(b, u1), k0 = (a1 - a) / (b - a), k1 = (b1 - a) / (b - a);
                            h.Linear.Add((a1 - u0, b1 - u0, wa + (wb - wa) * k0, wa + (wb - wa) * k1));
                        }
                    foreach (var (at, p) in z.Points)
                        if (low ? at <= ridge + 1e-9 : at > ridge + 1e-9) h.Points.Add((Math.Max(0, Math.Min(u1 - u0, at - u0)), p));
                    return h;
                }).ToArray();
                var at2 = idx.Select(i => active[i].At - u0).ToList();
                double ru = (low ? ridge : ridge) - u0;
                at2.Add(ru);
                var order = Enumerable.Range(0, at2.Count).OrderBy(i => at2[i]).ToList();
                var rr = BeamStatics.Reactions(u1 - u0, order.Select(i => at2[i]).ToList(), q);
                if (rr == null) return false;
                for (int k = 0; k < sets; k++)
                    for (int j = 0; j < order.Count; j++)
                    {
                        int o = order[j];
                        if (o < idx.Count) R[k][idx[o]] += rr[k][j];
                        else ridgeLoad[k] += rr[k][j];
                    }
                return true;
            }
            if (!Half(left, 0, ridge, true)) return null;
            if (!Half(right, ridge, L, false)) return null;
            if (real >= 0) { for (int k = 0; k < sets; k++) R[k][real] += ridgeLoad[k]; return R; }
            // the crown's load to the heels
            int ha = left.Count > 0 ? left.First() : -1, hb = right.Count > 0 ? right.Last() : -1;
            if (ha < 0 && hb < 0) return null;
            double da = ha < 0 ? 0 : ridge - active[ha].At, db = hb < 0 ? 0 : active[hb].At - ridge;
            for (int k = 0; k < sets; k++)
            {
                if (ha < 0) { R[k][hb] += ridgeLoad[k]; continue; }
                if (hb < 0) { R[k][ha] += ridgeLoad[k]; continue; }
                R[k][ha] += ridgeLoad[k] * db / (da + db);
                R[k][hb] += ridgeLoad[k] * da / (da + db);
            }
            return R;
        }

        /// <summary>Where segment a–b crosses segment c–d (t along a–b; false: they do not, or run along each other).</summary>
        private static bool Cross(Point2 a, Point2 b, Point2 c, Point2 d, out double t)
        {
            t = 0;
            double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y, den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-12) return false;
            double qx = c.X - a.X, qy = c.Y - a.Y;
            t = (qx * sy - qy * sx) / den;
            double u = (qx * ry - qy * rx) / den;
            return t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9;
        }

        /// <summary>Each support's share by set: the load from halfway to the one before it to halfway to the next (the ends: to the member's ends).</summary>
        private static double[][] Tributary(double L, List<double> at, BeamLoads[] loads)
        {
            var R = new double[loads.Length][];
            for (int k = 0; k < loads.Length; k++)
            {
                R[k] = new double[at.Count];
                for (int i = 0; i < at.Count; i++)
                {
                    double x0 = i == 0 ? double.NegativeInfinity : (at[i - 1] + at[i]) / 2, x1 = i == at.Count - 1 ? double.PositiveInfinity : (at[i] + at[i + 1]) / 2;
                    double sum = loads[k].Points.Where(p => p.At >= x0 && (p.At < x1 || double.IsPositiveInfinity(x1))).Sum(p => p.P);
                    foreach (var (a, b, wa, wb) in loads[k].Lines())
                    {
                        double c0 = Math.Max(a, x0), c1 = Math.Min(b, x1);
                        if (c1 - c0 <= 1e-12 || b - a <= 1e-12) continue;
                        double w0 = wa + (wb - wa) * (c0 - a) / (b - a), w1 = wa + (wb - wa) * (c1 - a) / (b - a);
                        sum += (c1 - c0) * (w0 + w1) / 2;
                    }
                    R[k][i] = sum;
                }
            }
            return R;
        }

        private static double TotalOf(BeamLoads q) =>
            q.Points.Sum(p => p.P) + q.Spread.Sum(s => (s.B - s.A) * s.W) + q.Linear.Sum(l => (l.B - l.A) * (l.WA + l.WB) / 2);
    }
}
