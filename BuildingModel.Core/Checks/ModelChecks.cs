using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;

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
            return issues;
        }
    }
}
