using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>
    /// One horizontal connection of a shear wall as a pair of equal and opposite forces (ROADMAP step 6, the owner's rule for
    /// every connection): the wind's shear along the wall's shear part, between the wall and the diaphragm at its top (the
    /// diaphragm's boundary nailing into the top plates), or between the wall's sill and what is under it: the floor's
    /// diaphragm (the sill nailing through the sheathing), or the foundation (the sill's anchor bolts). Made from the lateral
    /// design's walls (<see cref="ShearWallDesign.ShearOnTop"/>) by <see cref="ShearPairs.Of"/>.
    /// </summary>
    public sealed class ShearPair
    {
        public string WallId { get; set; }
        /// <summary>The level of the diaphragm on the other side (the foundation's: the lowest level).</summary>
        public string LevelId { get; set; }
        /// <summary>At the wall's top (its diaphragm), else at its sill.</summary>
        public bool AtTop { get; set; }
        /// <summary>The sill on the foundation: its force on the foundation is a reaction of the whole structure.</summary>
        public bool OnFoundation { get; set; }
        /// <summary>The shear part as built (ft): the line the force acts along.</summary>
        public Point2 Start { get; set; }
        public Point2 End { get; set; }
        /// <summary>The + direction (a unit plan vector): toward the wall's high end, east or north.</summary>
        public Point2 Dir { get; set; }
        /// <summary>"W3 top", "W3 sill".</summary>
        public string From { get; set; }
        /// <summary>What takes it: "the Level 2 diaphragm's boundary nailing", "(4) 5/8" anchor bolts", "sill nailing (not designed)".</summary>
        public string Connector { get; set; }
        /// <summary>Wind case → the force on the wall (lb, W at strength level, + along <see cref="Dir"/>).</summary>
        public Dictionary<string, double> OnWall { get; set; } = new Dictionary<string, double>();

        public double OnWallIn(string c) => c != null && OnWall != null && OnWall.TryGetValue(c, out var v) ? v : 0;
        /// <summary>The force on the diaphragm (or the foundation) in one case: the other half.</summary>
        public double OnOtherIn(string c) => -OnWallIn(c);
        public Point2 Middle => new Point2((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);
    }

    /// <summary>The horizontal pairs of the shear walls (<see cref="ShearPair"/>) and every diaphragm's free body from them.</summary>
    public static class ShearPairs
    {
        /// <summary>Every designed shear wall's two pairs, its top and its sill, per story.</summary>
        public static List<ShearPair> Of(Building b, LateralSection lateral)
        {
            var list = new List<ShearPair>();
            if (b == null || lateral?.Walls == null) return list;
            var lowest = b.LevelsBottomUp.FirstOrDefault();
            foreach (var d in lateral.Walls.Where(d => d.ShearOnTop != null && d.ShearOnTop.Count > 0))
            {
                var w = b.Find(d.WallId) as Wall;
                var top = b.Level(d.LevelId);
                if (w == null || top == null) continue;
                // the story's base: the level before its top along the wall (the wall's own base for its first story)
                var along = new List<Level>();
                if (b.Level(w.BaseLevelId) is Level wb) along.Add(wb);
                along.AddRange(b.LevelsAlong(w));
                int i = along.FindIndex(l => l.Id == top.Id);
                var bas = i > 0 ? along[i - 1] : along.FirstOrDefault();
                if (bas == null) continue;
                bool onFoundation = lowest != null && bas.Elevation <= lowest.Elevation + 1e-9;
                var a = new Point2(d.X0, d.Y0);
                var e = new Point2(d.X1, d.Y1);
                bool ew = Math.Abs(e.X - a.X) >= Math.Abs(e.Y - a.Y);
                var dir = ew ? new Point2(1, 0) : new Point2(0, 1);
                string name = w.Id;
                list.Add(new ShearPair
                {
                    WallId = w.Id, LevelId = top.Id, AtTop = true, Start = a, End = e, Dir = dir, From = $"{name} top",
                    Connector = $"the {top.Name} diaphragm's boundary nailing into the top plates",
                    OnWall = d.ShearOnTop.ToDictionary(kv => kv.Key, kv => kv.Value),
                });
                list.Add(new ShearPair
                {
                    WallId = w.Id, LevelId = bas.Id, OnFoundation = onFoundation, Start = a, End = e, Dir = dir, From = $"{name} sill",
                    Connector = onFoundation ? Anchors(d.Anchors) : Nails(d.SillNails, bas.Name),
                    OnWall = d.ShearOnTop.ToDictionary(kv => kv.Key, kv => -kv.Value),
                });
            }
            return list;
        }

        private static string Nails(SillNails n, string floor)
        {
            if (n == null || n.SpacingIn <= 0) return $"the sill nailing into the {floor} floor (not designed)";
            string s = $"{n.Nail} at {n.SpacingIn:0.#}\" o.c. through the {floor} floor's sheathing into the rim or blocking";
            return n.Fails == null ? s : $"{s}: {n.Fails}";
        }

        private static string Anchors(SillAnchors a)
        {
            if (a == null || a.BoltDiaIn <= 0) return "the sill's anchor bolts (not designed)";
            int n = a.PositionsIn?.Count ?? 0;
            string dia = a.BoltDiaIn.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return $"{(n > 0 ? $"({n}) " : "")}{dia}\" anchor bolts at {a.SpacingIn:0}\" o.c.";
        }

        /// <summary>
        /// One diaphragm's free body in one case (lb, lb·ft): its own wind (<see cref="DiaphragmWind"/>), each wall of the story
        /// under it pushing back on it at its top, each wall of the story above pushing on it at its sill. ΣFx, ΣFy, and ΣMz
        /// about the wind's point where the wind's moment is known (rigid), else null.
        /// </summary>
        public static (double fx, double fy, double? mz, double scale, double momentScale) Balance(DiaphragmWind wind, IEnumerable<ShearPair> pairs, string windCase)
        {
            if (wind == null) return (0, 0, null, 0, 0);
            double fx = wind.FxOf(windCase), fy = wind.FyOf(windCase);
            double? m = wind.MzOf(windCase);
            double mz = m ?? 0, scale = Math.Abs(fx) + Math.Abs(fy), mScale = Math.Abs(mz);
            foreach (var p in (pairs ?? Enumerable.Empty<ShearPair>()).Where(p => p.LevelId == wind.LevelId && !p.OnFoundation))
            {
                double f = p.OnOtherIn(windCase);
                double px = f * p.Dir.X, py = f * p.Dir.Y;
                fx += px; fy += py;
                // about the wind's point: r × F, r to any point of the line it acts along
                var r = new Point2(p.Middle.X - wind.X, p.Middle.Y - wind.Y);
                mz += r.X * py - r.Y * px;
                mScale += Math.Abs(r.X * py - r.Y * px);
                scale += Math.Abs(f);
            }
            return (fx, fy, m.HasValue ? mz : (double?)null, scale, mScale);
        }

        /// <summary>The cases of the diaphragms' winds.</summary>
        public static List<string> Cases(LateralSection lateral) =>
            (lateral?.Diaphragms ?? new List<DiaphragmWind>()).SelectMany(d => (d.Fx?.Keys ?? Enumerable.Empty<string>()).Concat(d.Fy?.Keys ?? Enumerable.Empty<string>())).Distinct().ToList();
    }
}
