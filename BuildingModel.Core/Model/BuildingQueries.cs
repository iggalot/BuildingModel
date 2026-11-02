using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>A level where a diaphragm reaches a wall, and how much of the wall's length it reaches (ft).</summary>
    public sealed class Bracing
    {
        public Level Level { get; set; }
        public double LengthReached { get; set; }
    }

    /// <summary>What the model works out from what was drawn: net diaphragms, the footprint, and how each wall is braced.</summary>
    public static class BuildingQueries
    {
        /// <summary>A diaphragm reaches a wall when its outline is this close to the wall line (ft).</summary>
        public const double WallTolerance = 0.25;

        /// <summary>The union of a level's diaphragms, before openings.</summary>
        public static Region GrossDiaphragm(this Building b, Level level) =>
            Region.Of(b.Diaphragms.Where(d => d.LevelId == level.Id).SelectMany(d => d.Pieces));

        /// <summary>The openings that cut a level: open from below it to above it.</summary>
        public static List<Opening> OpeningsCutting(this Building b, Level level)
        {
            var result = new List<Opening>();
            foreach (var o in b.Openings)
            {
                var bottom = b.Level(o.BottomLevelId);
                var top = b.Level(o.TopLevelId);
                if (bottom != null && top != null && bottom.Elevation < level.Elevation && level.Elevation < top.Elevation) result.Add(o);
            }
            return result;
        }

        /// <summary>A level's diaphragm with the openings that cut it taken out.</summary>
        public static Region NetDiaphragm(this Building b, Level level) =>
            b.GrossDiaphragm(level).Minus(b.OpeningsCutting(level).SelectMany(o => o.Pieces));

        /// <summary>The plan area the building covers at any level (all gross diaphragms): its extents are L and B.</summary>
        public static Region Footprint(this Building b) =>
            Region.Of(b.Diaphragms.Where(d => b.Level(d.LevelId) != null).SelectMany(d => d.Pieces));

        /// <summary>
        /// How much of a wall's length runs along the building's outside edge (ft): along the outline of the gross diaphragm
        /// (openings not counted, so a stair hole is not "outside"; a notch or courtyard is) of any level the wall reaches,
        /// within <paramref name="tolerance"/> of the wall line.
        /// </summary>
        public static double ExteriorLength(this Building b, Wall w, double tolerance = WallTolerance)
        {
            if (w.Direction == WallDirection.NotOrthogonal) return 0;
            bool ew = w.Direction == WallDirection.WestEast;
            double line = ew ? w.Start.Y : w.Start.X;
            double s0 = ew ? Math.Min(w.Start.X, w.End.X) : Math.Min(w.Start.Y, w.End.Y);
            double s1 = ew ? Math.Max(w.Start.X, w.End.X) : Math.Max(w.Start.Y, w.End.Y);
            var along = new List<(double from, double to)>();
            foreach (var l in b.LevelsAlong(w))
                foreach (var s in b.GrossDiaphragm(l).Outline())
                {
                    if (s.IsHorizontal != ew) continue;
                    double sl = ew ? s.A.Y : s.A.X;
                    if (Math.Abs(sl - line) > tolerance) continue;
                    double from = Math.Max(s0, ew ? Math.Min(s.A.X, s.B.X) : Math.Min(s.A.Y, s.B.Y));
                    double to = Math.Min(s1, ew ? Math.Max(s.A.X, s.B.X) : Math.Max(s.A.Y, s.B.Y));
                    if (to > from) along.Add((from, to));
                }
            // the union of the pieces (levels can share an edge: counted once)
            double total = 0, end = double.NegativeInfinity;
            foreach (var p in along.OrderBy(p => p.from))
            {
                double from = Math.Max(p.from, end);
                if (p.to > from) total += p.to - from;
                end = Math.Max(end, p.to);
            }
            return total;
        }

        /// <summary>
        /// An exterior wall: any part of its length runs along the building's outside edge (<see cref="ExteriorLength"/>).
        /// It is sheathed as an exterior wall even when most of it is inside.
        /// </summary>
        public static bool IsExterior(this Building b, Wall w) => b.ExteriorLength(w) > 1e-6;

        /// <summary>The levels a wall passes or reaches above its base (base excluded, top included), bottom up.</summary>
        public static List<Level> LevelsAlong(this Building b, Wall w)
        {
            var bottom = b.Level(w.BaseLevelId);
            var top = b.Level(w.TopLevelId);
            if (bottom == null || top == null) return new List<Level>();
            return b.LevelsBottomUp.Where(l => l.Elevation > bottom.Elevation && l.Elevation <= top.Elevation).ToList();
        }

        /// <summary>The levels above the wall's base whose (net) diaphragm reaches the wall, with the length reached.</summary>
        public static List<Bracing> BracedAt(this Building b, Wall w)
        {
            var list = new List<Bracing>();
            foreach (var l in b.LevelsAlong(w))
            {
                double len = b.NetDiaphragm(l).LengthAlong(w.Start, w.End, WallTolerance);
                if (len > 0) list.Add(new Bracing { Level = l, LengthReached = len });
            }
            return list;
        }

        /// <summary>
        /// The longest height of the wall between supports (ft): from its base to the first level whose diaphragm reaches
        /// it, between such levels, and from the last one to its top.
        /// </summary>
        public static double UnbracedHeight(this Building b, Wall w)
        {
            var bottom = b.Level(w.BaseLevelId);
            var top = b.Level(w.TopLevelId);
            if (bottom == null || top == null) return double.NaN;
            var supports = new List<double> { bottom.Elevation };
            supports.AddRange(b.BracedAt(w).Select(x => x.Level.Elevation));
            supports.Add(top.Elevation);
            supports = supports.Distinct().OrderBy(z => z).ToList();
            double worst = 0;
            for (int i = 0; i + 1 < supports.Count; i++) worst = Math.Max(worst, supports[i + 1] - supports[i]);
            return worst;
        }

        /// <summary>The shear walls that hold up a level's diaphragm in one direction: below it, reaching it, and reached by it.</summary>
        public static List<Wall> ShearWallsUnder(this Building b, Level level, WallDirection direction)
        {
            var net = b.NetDiaphragm(level);
            return b.Walls.Where(w =>
            {
                if (!w.IsShearWall || w.Direction != direction) return false;
                var bottom = b.Level(w.BaseLevelId);
                var top = b.Level(w.TopLevelId);
                return bottom != null && top != null && bottom.Elevation < level.Elevation && top.Elevation >= level.Elevation
                       && net.LengthAlong(w.Start, w.End, WallTolerance) > 0;
            }).ToList();
        }
    }
}
