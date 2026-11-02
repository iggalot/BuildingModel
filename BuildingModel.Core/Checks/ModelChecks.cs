using System.Collections.Generic;
using System.Linq;
using System;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Edits;

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
                foreach (var dir in new[] { WallDirection.WestEast, WallDirection.SouthNorth })
                    if (b.ShearWallsUnder(l, dir).Count == 0)
                        Add(Severity.Error, "NoLateralSupport",
                            $"{l.Name}: no shear wall {(dir == WallDirection.WestEast ? "running W-E" : "running S-N")} below it reaches its diaphragm.", l.Id);
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
            RoofChecks(b, Add);
            ShearWallChecks(b, Add);
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

        /// <summary>Shear walls that carry nothing, and shear walls drawn twice.</summary>
        private static void ShearWallChecks(Building b, Action<Severity, string, string, string[]> add)
        {
            var shear = b.Walls.Where(w => w.IsShearWall && w.Direction != WallDirection.NotOrthogonal && b.Level(w.BaseLevelId) != null && b.Level(w.TopLevelId) != null).ToList();
            foreach (var w in shear)
            {
                var top = b.Level(w.TopLevelId);
                if (top.Elevation > b.Level(w.BaseLevelId).Elevation && b.NetDiaphragm(top).LengthAlong(w.Start, w.End, BuildingQueries.WallTolerance) <= 0)
                    add(Severity.Warning, "ShearWallNotReached", $"Shear wall {w.Id}: no diaphragm at its top ({top.Name}) reaches it, so it takes no load there.", new[] { w.Id });
            }
            for (int i = 0; i < shear.Count; i++)
                for (int j = i + 1; j < shear.Count; j++)
                {
                    var a = shear[i]; var c = shear[j];
                    if (a.Direction != c.Direction) continue;
                    bool we = a.Direction == WallDirection.WestEast;
                    if (Math.Abs(we ? a.Start.Y - c.Start.Y : a.Start.X - c.Start.X) > 1e-6) continue;
                    double lo = Math.Max(Lo(a, we), Lo(c, we)), hi = Math.Min(Hi(a, we), Hi(c, we));
                    if (hi - lo <= 1e-6) continue;
                    double a0 = b.Level(a.BaseLevelId).Elevation, a1 = b.Level(a.TopLevelId).Elevation;
                    double c0 = b.Level(c.BaseLevelId).Elevation, c1 = b.Level(c.TopLevelId).Elevation;
                    if (Math.Min(a1, c1) - Math.Max(a0, c0) <= 1e-6) continue;
                    add(Severity.Error, "WallOverlap", $"Shear walls {a.Id} and {c.Id} overlap over {hi - lo:0.##} ft: the same wall is counted twice.", new[] { a.Id, c.Id });
                }
        }

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
