using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>
    /// Diaphragms at their own heights (owner 2026-11-13: "update the building model to handle variable diaphragm heights"): a
    /// diaphragm sits at its level's elevation plus its <see cref="Diaphragm.Offset"/> (a wing's ceiling 1 ft lower: −1). The
    /// walls follow: a wall's top is the diaphragm it holds up, its base the floor it stands on; under two diaphragms (the
    /// bearing wall where a wing's ceiling meets the main block's) the higher. A roof section sits on the diaphragm under it
    /// (<see cref="Statics.RoofFrame.SectionsOf"/>); beams and columns on it move with it. Every app reads heights from here:
    /// the 3D view, the wall window and stud lengths, SWC, the Load Manager.
    /// </summary>
    public static class WallTops
    {
        private const double E = 1e-6;

        /// <summary>The offset (ft, − lower) of the diaphragm of <paramref name="level"/> over a point; the highest of those there, 0 for none.</summary>
        public static double DiaphragmOffsetAt(this Building b, Level level, Point2 p)
        {
            if (b == null || level == null) return 0;
            double? best = null;
            foreach (var d in b.Diaphragms.Where(d => d.LevelId == level.Id))
                if (d.Pieces.Any(r => Inside(r, p)))
                    best = best == null ? d.Offset : Math.Max(best.Value, d.Offset);
            return best ?? 0;
        }

        /// <summary>
        /// The offset (ft) of the diaphragm of <paramref name="level"/> along a line on plan (a wall's, a beam's): the highest of
        /// the diaphragms over some length of it (not only touching it at a point); 0 for none.
        /// </summary>
        public static double DiaphragmOffsetAlong(this Building b, Level level, Point2 a, Point2 c)
        {
            if (b == null || level == null) return 0;
            double? best = null;
            foreach (var d in b.Diaphragms.Where(d => d.LevelId == level.Id))
                if (d.Pieces.Any(r => Overlap(r, a, c) > 1e-3))
                    best = best == null ? d.Offset : Math.Max(best.Value, d.Offset);
            return best ?? 0;
        }

        /// <summary>Where a wall meets <paramref name="level"/> (ft): its elevation plus the offset of the diaphragm along the wall.</summary>
        public static double WallTopAt(this Building b, Wall w, Level level)
        {
            if (level == null) return 0;
            return level.Elevation + (w == null ? 0 : b.DiaphragmOffsetAlong(level, w.Start, w.End));
        }

        /// <summary>The same as <see cref="WallTopAt"/>: a wall's base on <paramref name="level"/> (the floor it stands on).</summary>
        public static double WallBaseAt(this Building b, Wall w, Level level) => b.WallTopAt(w, level);

        /// <summary>The wall's top (ft): its top level's diaphragm along it.</summary>
        public static double WallTopElevation(this Building b, Wall w) => b.WallTopAt(w, b?.Level(w?.TopLevelId));

        /// <summary>The wall's base (ft): its base level's diaphragm along it (the ground: 0).</summary>
        public static double WallBaseElevation(this Building b, Wall w) => b.WallBaseAt(w, b?.Level(w?.BaseLevelId));

        /// <summary>The wall's height (ft): base to top.</summary>
        public static double WallHeight(this Building b, Wall w) => b.WallTopElevation(w) - b.WallBaseElevation(w);

        /// <summary>
        /// Where two diaphragms of one level at different heights meet (a step): each pair, the line they share on plan, its
        /// length. The shear across it goes through what is under it (the wall's top plates, blocking): not designed yet.
        /// </summary>
        public static List<(Diaphragm a, Diaphragm c, Point2 from, Point2 to)> Steps(this Building b, Level level)
        {
            var list = new List<(Diaphragm, Diaphragm, Point2, Point2)>();
            if (b == null || level == null) return list;
            var ds = b.Diaphragms.Where(d => d.LevelId == level.Id).ToList();
            for (int i = 0; i < ds.Count; i++)
                for (int j = i + 1; j < ds.Count; j++)
                {
                    if (Math.Abs(ds[i].Offset - ds[j].Offset) < E) continue;
                    foreach (var p in ds[i].Pieces)
                        foreach (var q in ds[j].Pieces)
                        {
                            double x0 = Math.Max(p.X0, q.X0), x1 = Math.Min(p.X1, q.X1), y0 = Math.Max(p.Y0, q.Y0), y1 = Math.Min(p.Y1, q.Y1);
                            if (x1 < x0 - E || y1 < y0 - E) continue;
                            if (x1 - x0 < E && y1 - y0 > E) list.Add((ds[i], ds[j], new Point2(x0, y0), new Point2(x0, y1)));
                            else if (y1 - y0 < E && x1 - x0 > E) list.Add((ds[i], ds[j], new Point2(x0, y0), new Point2(x1, y0)));
                            else if (x1 - x0 > E && y1 - y0 > E) list.Add((ds[i], ds[j], new Point2(x0, y0), new Point2(x1, y1)));   // overlapping: an area
                        }
                }
            return list;
        }

        private static bool Inside(Rect2 r, Point2 p) => p.X > r.X0 - E && p.X < r.X1 + E && p.Y > r.Y0 - E && p.Y < r.Y1 + E;

        /// <summary>The length of segment a–c inside (or on the edge of) a rectangle.</summary>
        private static double Overlap(Rect2 r, Point2 a, Point2 c)
        {
            double t0 = 0, t1 = 1, dx = c.X - a.X, dy = c.Y - a.Y;
            bool Clip(double p, double q)
            {
                if (Math.Abs(p) < 1e-12) return q >= -E;
                double t = q / p;
                if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
                return true;
            }
            if (!Clip(-dx, a.X - (r.X0 - E)) || !Clip(dx, (r.X1 + E) - a.X) || !Clip(-dy, a.Y - (r.Y0 - E)) || !Clip(dy, (r.Y1 + E) - a.Y)) return 0;
            return t1 > t0 ? (t1 - t0) * Math.Sqrt(dx * dx + dy * dy) : 0;
        }
    }
}
