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
