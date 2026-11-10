using System.Collections.Generic;
using System.Linq;
using System;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Edits;
using BuildingModel.Statics;

namespace BuildingModel.Checks
{
    public enum Severity { Warning, Error }

    /// <summary>One thing to fix or check, with the objects it is about.</summary>
    public sealed class ModelIssue
    {
        public Severity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public List<string> Ids { get; set; } = new List<string>();

        /// <summary>The level whose plan shows the issue (null: the whole building).</summary>
        public string LevelId { get; set; }

        /// <summary>Where to mark it on that plan (null: no one place).</summary>
        public Point2? At { get; set; }

        public override string ToString() => $"{Severity} {Code}: {Message}";
    }

    /// <summary>The checks of docs/DESIGN.md: what is wrong with the model, and what an engineer should look at.</summary>
    public static class ModelChecks
    {
        /// <summary>Openings over this share of a level's gross diaphragm: ASCE 7-22 Table 12.3-1, horizontal irregularity 3.</summary>
        public const double DiscontinuityRatio = 0.5;

        public static List<ModelIssue> Run(Building b)
        {
            var issues = new List<ModelIssue>();
            void Add(Severity s, string code, string message, params string[] ids) =>
                issues.Add(new ModelIssue { Severity = s, Code = code, Message = message, Ids = ids.ToList() });

            // levels
            if (b.Ground == null) Add(Severity.Error, "NoGround", "There is no ground level (elevation 0).");
            foreach (var g in b.Levels.GroupBy(l => l.Elevation).Where(g => g.Count() > 1))
                Add(Severity.Error, "LevelElevation", $"{string.Join(" and ", g.Select(l => l.Name))} are at the same elevation ({g.Key:0.##} ft).", g.Select(l => l.Id).ToArray());

            // references
            foreach (var d in b.Diaphragms.Where(d => b.Level(d.LevelId) == null))
                Add(Severity.Error, "BadReference", $"Diaphragm {d.Id} is on a level that does not exist ({d.LevelId}).", d.Id);
            foreach (var o in b.Openings.Where(o => b.Level(o.BottomLevelId) == null || b.Level(o.TopLevelId) == null))
                Add(Severity.Error, "BadReference", $"Opening {o.Id} names a level that does not exist.", o.Id);
            foreach (var w in b.Walls.Where(w => b.Level(w.BaseLevelId) == null || b.Level(w.TopLevelId) == null))
                Add(Severity.Error, "BadReference", $"Wall {w.Id} names a level that does not exist.", w.Id);

            // diaphragms per level
            foreach (var l in b.LevelsBottomUp.Where(l => l.Elevation > 0))
            {
                var gross = b.GrossDiaphragm(l);
                if (gross.IsEmpty)
                {
                    Add(Severity.Warning, "NoDiaphragm", $"{l.Name} has no diaphragm.", l.Id);
                    continue;
                }
                var net = b.NetDiaphragm(l);
                double open = gross.Area - net.Area;
                if (open > DiscontinuityRatio * gross.Area)
                    Add(Severity.Warning, "DiaphragmDiscontinuity",
                        $"{l.Name}: openings take {open:0} of {gross.Area:0} sq ft ({open / gross.Area:P0}), over 50 % of the diaphragm " +
                        "(ASCE 7-22 Table 12.3-1, horizontal irregularity type 3).", l.Id);
            }

            // openings
            foreach (var o in b.Openings)
            {
                var bottom = b.Level(o.BottomLevelId);
                var top = b.Level(o.TopLevelId);
                if (bottom == null || top == null) continue;
                if (!(top.Elevation > bottom.Elevation))
                {
                    Add(Severity.Error, "OpeningLevels", $"Opening {o.Id}: its top ({top.Name}) is not above its bottom ({bottom.Name}).", o.Id);
                    continue;
                }
                var cut = b.LevelsBottomUp.Where(l => l.Elevation > bottom.Elevation && l.Elevation < top.Elevation).ToList();
                bool cutsAny = cut.Any(l => o.Pieces.Any(p => b.GrossDiaphragm(l).AreaWithin(p) > 0));
                if (!cutsAny)
                    Add(Severity.Warning, "OpeningCutsNothing", $"Opening {o.Id} is not inside any diaphragm between {bottom.Name} and {top.Name}.", o.Id);
                foreach (var l in cut)
                {
                    var outline = b.GrossDiaphragm(l).Outline();
                    if (o.Pieces.Any(p => b.GrossDiaphragm(l).AreaWithin(p) > 0 && outline.Any(s => s.Box.DistanceTo(p) <= 1e-9)))
                        Add(Severity.Warning, "OpeningAtEdge", $"Opening {o.Id} reaches the edge of the {l.Name} diaphragm: the chord is interrupted there.", o.Id, l.Id);
                }
            }

            // walls
            foreach (var w in b.Walls)
            {
                var bottom = b.Level(w.BaseLevelId);
                var top = b.Level(w.TopLevelId);
                if (bottom == null || top == null) continue;
                if (w.Direction == WallDirection.NotOrthogonal)
                    Add(Severity.Error, "WallNotOrthogonal", $"Wall {w.Id} is not W-E or S-N (or has no length).", w.Id);
                if (!(top.Elevation > bottom.Elevation))
                {
                    Add(Severity.Error, "WallLevels", $"Wall {w.Id}: its top ({top.Name}) is not above its base ({bottom.Name}).", w.Id);
                    continue;
                }
                var braced = new HashSet<string>(b.BracedAt(w).Select(x => x.Level.Id));
                var passed = b.LevelsAlong(w).Where(l => l.Elevation < top.Elevation && !braced.Contains(l.Id)).ToList();
                if (passed.Count > 0)
                    Add(Severity.Warning, "TallWall",
                        $"Wall {w.Id} passes {string.Join(", ", passed.Select(l => l.Name))} with no diaphragm at the wall: unbraced height {b.UnbracedHeight(w):0.##} ft " +
                        "(check its out-of-plane bending and anchorage).", w.Id);
            }
            // notes
            foreach (var n in b.Notes.Where(n => b.Level(n.LevelId) == null))
                Add(Severity.Error, "BadReference", $"Note {n.Id} is on a level that does not exist.", n.Id);
            // beams and columns
            foreach (var bm in b.Beams)
            {
                if (b.Level(bm.LevelId) == null) Add(Severity.Error, "BadReference", $"Beam {bm.Id} is on a level that does not exist.", bm.Id);
                else if (!(bm.Length > 0.1)) Add(Severity.Error, "BeamLength", $"Beam {bm.Id} has no length.", bm.Id);
                else if (b.Level(bm.LevelId).Elevation == 0) Add(Severity.Warning, "BeamOnGround", $"Beam {bm.Id} is on the ground level: a grade beam is the foundation's (its ends anchor to the foundation).", bm.Id);
            }
            foreach (var c in b.Columns)
            {
                var lo = b.Level(c.BaseLevelId);
                var hi = b.Level(c.TopLevelId);
                if (lo == null || hi == null) Add(Severity.Error, "BadReference", $"Column {c.Id} refers to a level that does not exist.", c.Id);
                else if (!(hi.Elevation > lo.Elevation)) Add(Severity.Error, "ColumnLevels", $"Column {c.Id}: its top ({hi.Name}) is not above its base ({lo.Name}).", c.Id);
            }

            BeamColumnChecks(b, issues);
            BeamStabilityChecks(b, issues);
            RoofChecks(b, Add);
            OverlapChecks(b, Add);
            foreach (var i in issues) Locate(b, i);
            return issues;
        }

        /// <summary>
        /// The roof's geometry: a sloped roof needs a slope between 0 and 90 degrees and a rectangular top floor; parapet
        /// and eaves are not negative; a parapet counts on a flat roof only. (The code's limits on slope and height, such as
        /// the cladding figures' 45 degrees and 60 ft, are the wind calculator's checks.)
        /// </summary>
        private static void RoofChecks(Building b, Action<Severity, string, string, string[]> add)
        {
            var r = b.Roof;
            if (r == null) return;
            var top = b.RoofLevel();
            var at = top == null ? new string[0] : new[] { top.Id }.Concat(b.DiaphragmsOn(top).Select(d => d.Id).Take(1)).ToArray();
            if (r.IsSloped && !(r.Pitch > 0 && r.Pitch < 90))
                add(Severity.Error, "RoofPitch", r.Pitch == 0 ? $"The {r.Shape.ToString().ToLowerInvariant()} roof has a 0° slope: make it Flat, or give it a slope."
                                                             : $"The roof slope ({r.Pitch:0.##}°) must be between 0 and 90 degrees.", at);
            if (r.Parapet < 0) add(Severity.Error, "RoofParapet", $"The parapet height ({r.Parapet:0.##} ft) cannot be negative.", at);
            if (r.Overhang < 0) add(Severity.Error, "RoofEaves", $"The eaves ({r.Overhang:0.##} ft) cannot be negative.", at);
            if (r.IsSloped && r.Parapet > 0)
                add(Severity.Warning, "ParapetOnSlopedRoof", $"A parapet ({r.Parapet:0.##} ft) is counted on a flat roof only; the {r.Shape.ToString().ToLowerInvariant()} roof ignores it.", at);
            if (!r.IsSloped || top == null) return;
            var floor = b.GrossDiaphragm(top);
            if (floor.Area <= 0) return; // NoDiaphragm says so
            if (floor.Area < floor.Bounds.Area - 1e-6)
                add(Severity.Warning, "RoofNotRectangular",
                    $"{top.Name} is not a rectangle ({floor.Area:0} of {floor.Bounds.Area:0} sq ft): the {r.Shape.ToString().ToLowerInvariant()} roof is taken over the " +
                    $"{floor.Bounds.Width:0.##} × {floor.Bounds.Height:0.##} ft rectangle around it.", at);
            if (r.Shape == RoofShape.Hip && r.Ridge != RidgeAxis.Auto && r.Ridge != b.EffectiveRidge())
                add(Severity.Warning, "HipRidge", "A hip roof's ridge runs along the longer side; the ridge direction chosen is not used.", at);
        }

        /// <summary>
        /// Objects drawn twice (a warning: one is probably left over from an edit). A wall on another wall (two declared shear walls
        /// overlapping are the Shear Wall Calculator's error); two beams on one level along the same line; two columns at one
        /// point (within the bearing tolerance) over the same height, where a load may land on the one that carries nothing.
        /// </summary>
        private static void OverlapChecks(Building b, Action<Severity, string, string, string[]> add)
        {
            const double tol = 1e-6;
            // walls
            var walls = b.Walls.Where(w => w.Direction != WallDirection.NotOrthogonal && b.Level(w.BaseLevelId) != null && b.Level(w.TopLevelId) != null).ToList();
            for (int i = 0; i < walls.Count; i++)
                for (int j = i + 1; j < walls.Count; j++)
                {
                    var a = walls[i]; var c = walls[j];
                    if (a.Direction != c.Direction) continue;
                    bool we = a.Direction == WallDirection.WestEast;
                    if (Math.Abs(we ? a.Start.Y - c.Start.Y : a.Start.X - c.Start.X) > tol) continue;
                    double lo = Math.Max(Lo(a, we), Lo(c, we)), hi = Math.Min(Hi(a, we), Hi(c, we));
                    if (hi - lo <= tol) continue;
                    if (!HeightsOverlap(b.Level(a.BaseLevelId).Elevation, b.Level(a.TopLevelId).Elevation, b.Level(c.BaseLevelId).Elevation, b.Level(c.TopLevelId).Elevation)) continue;
                    add(Severity.Warning, "WallsOverlap", $"Walls {a.Id} and {c.Id} overlap over {hi - lo:0.##} ft: one is drawn on top of the other. Delete one, or move it.", new[] { a.Id, c.Id });
                }

            WallJointChecks(walls, b, add);

            // beams on one level along one line
            var beams = b.Beams.Where(x => b.Level(x.LevelId) != null && x.Length > 0.1).ToList();
            for (int i = 0; i < beams.Count; i++)
                for (int j = i + 1; j < beams.Count; j++)
                {
                    var a = beams[i]; var c = beams[j];
                    if (a.LevelId != c.LevelId) continue;
                    double L = a.Length, ux = (a.End.X - a.Start.X) / L, uy = (a.End.Y - a.Start.Y) / L;
                    double Off(Point2 p) => Math.Abs(ux * (p.Y - a.Start.Y) - uy * (p.X - a.Start.X)); // distance off a's line
                    double Along(Point2 p) => ux * (p.X - a.Start.X) + uy * (p.Y - a.Start.Y);
                    if (Off(c.Start) > BuildingEdits.BearingTolerance || Off(c.End) > BuildingEdits.BearingTolerance) continue;
                    double t0 = Along(c.Start), t1 = Along(c.End);
                    double over = Math.Min(L, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1));
                    if (over <= BuildingEdits.BearingTolerance) continue; // end to end is fine
                    add(Severity.Warning, "BeamsOverlap", $"Beams {a.Id} and {c.Id} overlap over {over:0.##} ft on {b.Level(a.LevelId).Name}: one is drawn on top of the other. Delete one, or shorten it.", new[] { a.Id, c.Id });
                }

            // columns at one point
            var cols = b.Columns.Where(x => b.Level(x.BaseLevelId) != null && b.Level(x.TopLevelId) != null).ToList();
            for (int i = 0; i < cols.Count; i++)
                for (int j = i + 1; j < cols.Count; j++)
                {
                    var a = cols[i]; var c = cols[j];
                    if (Snapper.Dist(a.At, c.At) > BuildingEdits.BearingTolerance) continue;
                    if (!HeightsOverlap(b.Level(a.BaseLevelId).Elevation, b.Level(a.TopLevelId).Elevation, b.Level(c.BaseLevelId).Elevation, b.Level(c.TopLevelId).Elevation)) continue;
                    add(Severity.Warning, "ColumnsOverlap", $"Columns {a.Id} and {c.Id} stand at the same point ({a.At.X:0.##}, {a.At.Y:0.##}) over the same height: a load may land on " +
                                                            "one and not the other. Delete one.", new[] { a.Id, c.Id });
                }
        }

        /// <summary>
        /// Perpendicular walls of one story that take the same space (a warning): WallsCross, each running past the other.
        /// Split one at the crossing (two walls butting into the other), or move it. Where two walls meet at a corner or a tee,
        /// one runs past and the other stops at its face, by the rule or as the engineer turned it (<see cref="WallJoints"/>):
        /// nothing to flag.
        /// </summary>
        private static void WallJointChecks(List<Wall> walls, Building b, Action<Severity, string, string, string[]> add)
        {
            const double tol = WallJoints.Tolerance;
            // strictly inside a wall's length (not at its ends), on its line
            bool Inside(Wall w, Point2 p)
            {
                bool we = w.Direction == WallDirection.WestEast;
                if (Math.Abs(we ? p.Y - w.Start.Y : p.X - w.Start.X) > tol) return false;
                double t = we ? p.X : p.Y;
                return t > Lo(w, we) + tol && t < Hi(w, we) - tol;
            }
            for (int i = 0; i < walls.Count; i++)
                for (int j = i + 1; j < walls.Count; j++)
                {
                    var a = walls[i]; var c = walls[j];
                    if (a.Direction == c.Direction) continue;
                    if (!HeightsOverlap(b.Level(a.BaseLevelId).Elevation, b.Level(a.TopLevelId).Elevation, b.Level(c.BaseLevelId).Elevation, b.Level(c.TopLevelId).Elevation)) continue;
                    var we = a.Direction == WallDirection.WestEast ? a : c;
                    var sn = ReferenceEquals(we, a) ? c : a;
                    var cross = new Point2(sn.Start.X, we.Start.Y);
                    if (Inside(we, cross) && Inside(sn, cross))
                        add(Severity.Warning, "WallsCross",
                            $"Walls {a.Id} and {c.Id} cross at ({cross.X:0.##}, {cross.Y:0.##}): each runs past the other, so they take the same space there. " +
                            "Split one at the crossing (two walls butting into the other), or move it.",
                            new[] { a.Id, c.Id });
                }
        }

        /// <summary>Two elevation ranges share some height (end to end, one on the other, does not count).</summary>
        private static bool HeightsOverlap(double a0, double a1, double c0, double c1) => Math.Min(a1, c1) - Math.Max(a0, c0) > 1e-6;

        private static double Lo(Wall w, bool we) => we ? Math.Min(w.Start.X, w.End.X) : Math.Min(w.Start.Y, w.End.Y);
        private static double Hi(Wall w, bool we) => we ? Math.Max(w.Start.X, w.End.X) : Math.Max(w.Start.Y, w.End.Y);

        /// <summary>Where the issue shows: the level it names, else the level of its first object; at the object, else at the level's diaphragm.</summary>
        /// <summary>Within this distance (ft), but farther than the bearing tolerance, a beam "just misses" a column: probably a drawing slip.</summary>
        public const double NearMiss = 1.0;

        /// <summary>
        /// A beam and a column that cross in plan but are not connected (a warning to examine; selecting it shows both):
        /// <list type="bullet">
        /// <item>BeamCrossesColumn: the column runs past the beam's level through the beam (its base set by hand), so the beam does
        /// not bear on it and it does not carry the beam;</item>
        /// <item>BeamMissesColumn: the beam passes close to a column that reaches its level, but outside the bearing tolerance, so they
        /// do not connect.</item>
        /// </list>
        /// </summary>
        private static void BeamColumnChecks(Building b, List<ModelIssue> issues)
        {
            foreach (var bm in b.Beams)
            {
                var l = b.Level(bm.LevelId);
                if (l == null || !(bm.Length > 0.1)) continue;
                var seg = new Segment2(bm.Start, bm.End);
                foreach (var c in b.Columns)
                {
                    var lo = b.Level(c.BaseLevelId);
                    var hi = b.Level(c.TopLevelId);
                    if (lo == null || hi == null || lo.Elevation > l.Elevation || hi.Elevation < l.Elevation) continue;
                    var foot = Snapper.ClosestOnSegment(c.At, seg);
                    double d = Snapper.Dist(foot, c.At);
                    if (d <= BuildingEdits.BearingTolerance && lo.Elevation < l.Elevation && hi.Elevation > l.Elevation)
                        issues.Add(new ModelIssue
                        {
                            Severity = Severity.Warning, Code = "BeamCrossesColumn", Ids = new List<string> { bm.Id, c.Id }, At = foot,
                            Message = $"Beam {bm.Id} crosses column {c.Id} at {l.Name}, but {c.Id} runs on past it (from {lo.Name} to {hi.Name}{(c.BaseFixed ? ", its base set by hand" : "")}): " +
                                      $"they are not connected. Check whether the beam should bear on the column, or the column stop on the beam.",
                        });
                    else if (d > BuildingEdits.BearingTolerance && d <= NearMiss)
                        issues.Add(new ModelIssue
                        {
                            Severity = Severity.Warning, Code = "BeamMissesColumn", Ids = new List<string> { bm.Id, c.Id }, At = foot,
                            Message = $"Beam {bm.Id} passes {d:0.##} ft from column {c.Id} at {l.Name}: they are not connected (they connect within {BuildingEdits.BearingTolerance:0.##} ft). " +
                                      "If the column is meant to carry the beam, or stand on it, snap one onto the other.",
                        });
                }
            }
        }

        /// <summary>
        /// BeamNotHeldUp, an error (owner 2026-11-10: the model is not valid, nothing is calculated anywhere; leaving the one
        /// beam out would be dangerous): a beam above the ground with fewer than two supports, or beams resting on each other
        /// with nothing under them (a mechanism), found by <see cref="BeamSupports.Unstable"/>. A cantilever past its last
        /// support stands (two supports behind it), at one end or both.
        /// </summary>
        private static void BeamStabilityChecks(Building b, List<ModelIssue> issues)
        {
            foreach (var lv in b.Levels.Where(l => l.Elevation > 0))
            {
                if (!b.Beams.Any(m => m.LevelId == lv.Id)) continue;
                var supports = BeamSupports.Of(b, lv);
                var unstable = BeamSupports.Unstable(b, lv, supports);
                if (unstable.Count == 0) continue;
                string Held(string id) => supports[id].Count == 0 ? "nothing under it"
                    : string.Join(", ", supports[id].Select(s => $"{s.On} at its {s.Name}"));
                // a beam on fewer than two: its own error; the rest resting on one another: one error together
                foreach (var id in unstable.Where(id => supports[id].Count < 2))
                    issues.Add(new ModelIssue
                    {
                        Severity = Severity.Error, Code = "BeamNotHeldUp", Ids = new List<string> { id }, LevelId = lv.Id,
                        Message = $"Beam {id} on {lv.Name} has {(supports[id].Count == 0 ? "no support" : "one support")} ({Held(id)}): a beam needs two to stand " +
                                  "(an end may run past one as a cantilever). Draw the wall, column or beam that holds it up. Nothing is calculated until it stands.",
                    });
                var rest = unstable.Where(id => supports[id].Count >= 2).ToList();
                if (rest.Count > 0)
                    issues.Add(new ModelIssue
                    {
                        Severity = Severity.Error, Code = "BeamNotHeldUp", Ids = rest, LevelId = lv.Id,
                        Message = $"{(rest.Count == 1 ? "Beam" : "Beams")} {string.Join(", ", rest)} on {lv.Name} rest on each other with nothing under where they meet " +
                                  $"({string.Join("; ", rest.Select(id => $"{id}: {Held(id)}"))}): a mechanism, they cannot stand. Draw a column, a post or a wall under that point. Nothing is calculated until they stand.",
                    });
            }
        }

        public static void Locate(Building b, ModelIssue issue)
        {
            var level = issue.Ids.Select(b.Level).FirstOrDefault(l => l != null);
            foreach (var id in issue.Ids)
            {
                switch (b.Find(id))
                {
                    case Wall w:
                        issue.At = issue.At ?? w.Middle;
                        level = level ?? b.Level(w.TopLevelId);
                        break;
                    case Opening o when o.Pieces.Count > 0:
                        var r = o.Pieces[0];
                        issue.At = issue.At ?? new Point2((r.X0 + r.X1) / 2, (r.Y0 + r.Y1) / 2);
                        level = level ?? b.Levels.Where(l => b.OpeningsCutting(l).Contains(o)).OrderBy(l => l.Elevation).FirstOrDefault() ?? b.Level(o.BottomLevelId);
                        break;
                    case Diaphragm d when d.Pieces.Count > 0:
                        var p = d.Pieces[0];
                        issue.At = issue.At ?? new Point2((p.X0 + p.X1) / 2, (p.Y0 + p.Y1) / 2);
                        level = level ?? b.Level(d.LevelId);
                        break;
                    case Note n:
                        issue.At = issue.At ?? n.At;
                        level = level ?? b.Level(n.LevelId);
                        break;
                    case Beam bm:
                        issue.At = issue.At ?? bm.Middle;
                        level = level ?? b.Level(bm.LevelId);
                        break;
                    case Column c:
                        issue.At = issue.At ?? c.At;
                        level = level ?? b.Level(c.TopLevelId);
                        break;
                }
            }
            if (level != null && issue.At == null && !b.GrossDiaphragm(level).IsEmpty) issue.At = b.GrossDiaphragm(level).Centroid;
            issue.LevelId = level?.Id;
        }
    }
}
