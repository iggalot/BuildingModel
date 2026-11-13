using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Statics;

namespace BuildingModel.Model
{
    /// <summary>
    /// Where a wall's top is. A level's walls reach its elevation, except under a roof of sections where a section's plate is
    /// its own (owner 2026-11-13: "give each section its own wall top height"): the walls up to the roof level standing under a
    /// section (inside its walls' rectangle, the whole wall) top out at that level plus the section's eave rise (a wing's plate
    /// 1 ft lower: −1). A wall under two sections (the bearing wall where a wing meets the main block) goes to the higher plate.
    /// A dormer's walls stand on the roof, not on the level: no wall of the model is under one.
    /// Every app reads a wall's height from here: the 3D view, the wall window and stud lengths, SWC, the Load Manager.
    /// </summary>
    public static class WallTops
    {
        private const double E = 1e-6;

        /// <summary>The rise (ft, − lower) of a wall's top over the level it reaches, from the roof section it stands under.</summary>
        public static double WallTopRise(this Building b, Wall w)
        {
            if (b?.Roof == null || w == null || !b.Roof.IsSloped) return 0;
            var sections = b.Roof.Sections;
            if (sections == null || sections.Count == 0) return 0;
            var roofLevel = b.RoofLevel();
            if (roofLevel == null || w.TopLevelId != roofLevel.Id) return 0;
            double? rise = null;
            for (int i = 0; i < sections.Count; i++)
            {
                if (RoofFrame.IsDormer(sections, i)) continue;
                var r = sections[i].Walls;
                if (!Inside(r, w.Start) || !Inside(r, w.End)) continue;
                rise = rise == null ? sections[i].EaveRise : Math.Max(rise.Value, sections[i].EaveRise);
            }
            return rise ?? 0;
        }

        /// <summary>
        /// The wall's top (ft) where it reaches <paramref name="level"/>: the level's elevation, its section's eave rise added
        /// when that is the wall's top level (the roof level).
        /// </summary>
        public static double WallTopAt(this Building b, Wall w, Level level)
        {
            if (level == null) return 0;
            return level.Elevation + (w != null && level.Id == w.TopLevelId ? b.WallTopRise(w) : 0);
        }

        /// <summary>The wall's top (ft): its top level's elevation and its section's eave rise.</summary>
        public static double WallTopElevation(this Building b, Wall w) => b.WallTopAt(w, b?.Level(w?.TopLevelId));

        /// <summary>
        /// The roof level's ceiling split where its sections' plates differ: each piece with its rise (ft, − lower), the
        /// highest section over it (0 where none is). Other levels: the rectangle itself, 0.
        /// </summary>
        public static List<(Rect2 r, double rise)> CeilingPieces(this Building b, Level level, Rect2 cell)
        {
            var list = new List<(Rect2, double)>();
            var sections = b?.Roof != null && b.Roof.IsSloped && level != null && level.Id == b.RoofLevel()?.Id ? b.Roof.Sections : null;
            var own = sections == null ? new List<RoofSection>() : sections.Where((s, i) => !RoofFrame.IsDormer(sections, i)).ToList();
            if (own.All(s => Math.Abs(s.EaveRise) < E)) { list.Add((cell, 0)); return list; }
            var xs = new List<double> { cell.X0, cell.X1 }; var ys = new List<double> { cell.Y0, cell.Y1 };
            foreach (var s in own)
            {
                foreach (double x in new[] { s.Walls.X0, s.Walls.X1 }) if (x > cell.X0 + E && x < cell.X1 - E) xs.Add(x);
                foreach (double y in new[] { s.Walls.Y0, s.Walls.Y1 }) if (y > cell.Y0 + E && y < cell.Y1 - E) ys.Add(y);
            }
            xs = xs.Distinct().OrderBy(z => z).ToList(); ys = ys.Distinct().OrderBy(z => z).ToList();
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < ys.Count; j++)
                {
                    var p = new Point2((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
                    var over = own.Where(s => Inside(s.Walls, p)).Select(s => s.EaveRise).ToList();
                    list.Add((new Rect2(xs[i], ys[j], xs[i + 1], ys[j + 1]), over.Count == 0 ? 0 : over.Max()));
                }
            return list;
        }

        private static bool Inside(Rect2 r, Point2 p) => p.X > r.X0 - E && p.X < r.X1 + E && p.Y > r.Y0 - E && p.Y < r.Y1 + E;
    }
}
