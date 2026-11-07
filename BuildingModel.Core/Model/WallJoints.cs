using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>How two walls meet: at a corner (an L, both ending at one point) or a tee (one ending part way along the other).</summary>
    public enum JointKind { Corner, Tee }

    /// <summary>
    /// Where two perpendicular walls of one story meet. The walls are their centrelines; built, one runs past (<see cref="Through"/>)
    /// and the other stops at its face (<see cref="Butting"/>), so the two never take the same space.
    /// </summary>
    public sealed class WallJoint
    {
        public JointKind Kind { get; set; }
        /// <summary>Where the centrelines meet (ft).</summary>
        public Point2 At { get; set; }
        /// <summary>The wall that runs past.</summary>
        public Wall Through { get; set; }
        /// <summary>The wall that stops at the other's face.</summary>
        public Wall Butting { get; set; }
        /// <summary>A corner turned round from the rule by the engineer (<see cref="Building.FlippedJoints"/>).</summary>
        public bool Flipped { get; set; }
        /// <summary>Only a corner can be turned round: at a tee the wall that ends part way along the other butts into it.</summary>
        public bool CanFlip => Kind == JointKind.Corner;
        /// <summary>The joint's key in <see cref="Building.FlippedJoints"/>.</summary>
        public string Key => WallJoints.Key(Through, Butting);

        public override string ToString() =>
            $"{(Kind == JointKind.Corner ? "corner" : "tee")} at ({At.X:0.##}, {At.Y:0.##}): {Through.Id} runs past, {Butting.Id} stops at its face{(Flipped ? " (turned round)" : "")}";
    }

    /// <summary>
    /// The wall joints and each wall's real ends, for every app (one rule, here). At a corner the longer wall runs past the
    /// shorter, an E-W wall past an N-S one of the same length, unless the engineer turned that corner round
    /// (<see cref="Flip"/>); at a tee the wall that ends part way along the other butts into it. A wall that butts stops at
    /// the other's face: half the other's thickness (its stud depth, <see cref="StudSizes"/>) short of the centrelines' point.
    /// The plans show the walls as drawn; the real ends set where a shear wall's hold-downs go.
    /// </summary>
    public static class WallJoints
    {
        /// <summary>Within this distance (ft) a wall's end is on the other wall (the drawing's snap).</summary>
        public const double Tolerance = 0.01;

        /// <summary>A joint's key: the two walls' IDs, in order.</summary>
        public static string Key(Wall a, Wall c) =>
            string.CompareOrdinal(a?.Id, c?.Id) <= 0 ? $"{a?.Id}|{c?.Id}" : $"{c?.Id}|{a?.Id}";

        /// <summary>The rule at a corner: the longer wall runs past; the same length, the E-W one.</summary>
        public static Wall RunsPastByRule(Wall a, Wall c)
        {
            if (Math.Abs(a.Length - c.Length) > Tolerance) return a.Length > c.Length ? a : c;
            return a.Direction == WallDirection.WestEast ? a : c;
        }

        /// <summary>The wall's thickness (ft) on the story of <paramref name="storyTopId"/>: its stud depth there (a 2x6 if not known).</summary>
        public static double ThicknessOf(Building b, Wall w, string storyTopId) =>
            StudSizes.DepthOr(b.StudSizeOf(w, storyTopId ?? w.TopLevelId), 5.5) / 12.0;

        /// <summary>Every joint of the building, or only those of <paramref name="only"/>.</summary>
        public static List<WallJoint> Of(Building b, Wall only = null)
        {
            var list = new List<WallJoint>();
            if (b == null) return list;
            var walls = b.Walls.Where(w => w.Direction != WallDirection.NotOrthogonal && b.Level(w.BaseLevelId) != null && b.Level(w.TopLevelId) != null).ToList();
            var flipped = new HashSet<string>(b.FlippedJoints ?? new List<string>());
            foreach (var a in walls)
                foreach (var c in walls)
                {
                    if (string.CompareOrdinal(a.Id, c.Id) >= 0 || a.Direction == c.Direction) continue;
                    if (only != null && !ReferenceEquals(only, a) && !ReferenceEquals(only, c)) continue;
                    if (!SameStory(b, a, c)) continue;
                    var j = JointOf(a, c);
                    if (j == null) continue;
                    if (j.Kind == JointKind.Corner && flipped.Contains(Key(a, c)))
                    {
                        var t = j.Through; j.Through = j.Butting; j.Butting = t;
                        j.Flipped = true;
                    }
                    list.Add(j);
                }
            return list;
        }

        /// <summary>
        /// The wall's ends as built (ft): an end that butts into another wall moved in to that wall's face, by half its
        /// thickness; an end that runs past, or meets nothing, where it is drawn.
        /// </summary>
        public static (Point2 start, Point2 end) RealEnds(Building b, Wall w)
        {
            var s = w.Start; var e = w.End;
            double L = w.Length;
            if (b == null || !(L > 0)) return (s, e);
            double ux = (e.X - s.X) / L, uy = (e.Y - s.Y) / L;
            double inS = 0, inE = 0;
            foreach (var j in Of(b, w).Where(x => ReferenceEquals(x.Butting, w)))
            {
                double half = ThicknessOf(b, j.Through, w.TopLevelId) / 2;
                if (Near(j.At, s)) inS = Math.Max(inS, half);
                else if (Near(j.At, e)) inE = Math.Max(inE, half);
            }
            if (inS + inE >= L) return (s, e);
            return (new Point2(s.X + ux * inS, s.Y + uy * inS), new Point2(e.X - ux * inE, e.Y - uy * inE));
        }

        /// <summary>Turns a corner round (the other wall runs past), or back to the rule. A tee cannot be turned.</summary>
        public static void Flip(Building b, WallJoint j)
        {
            if (b == null || j == null || !j.CanFlip) return;
            b.FlippedJoints = b.FlippedJoints ?? new List<string>();
            string k = j.Key;
            if (!b.FlippedJoints.Remove(k)) b.FlippedJoints.Add(k);
        }

        /// <summary>The joint of two perpendicular walls, or null: a corner where an end of each meets, a tee where an end of one lands part way along the other.</summary>
        private static WallJoint JointOf(Wall a, Wall c)
        {
            foreach (var p in new[] { a.Start, a.End })
                foreach (var q in new[] { c.Start, c.End })
                    if (Near(p, q))
                        return new WallJoint { Kind = JointKind.Corner, At = new Point2((p.X + q.X) / 2, (p.Y + q.Y) / 2), Through = RunsPastByRule(a, c), Butting = ReferenceEquals(RunsPastByRule(a, c), a) ? c : a };
            foreach (var p in new[] { a.Start, a.End })
                if (Inside(c, p)) return new WallJoint { Kind = JointKind.Tee, At = p, Through = c, Butting = a };
            foreach (var q in new[] { c.Start, c.End })
                if (Inside(a, q)) return new WallJoint { Kind = JointKind.Tee, At = q, Through = a, Butting = c };
            return null;
        }

        private static bool Near(Point2 p, Point2 q) => Math.Abs(p.X - q.X) <= Tolerance && Math.Abs(p.Y - q.Y) <= Tolerance;

        /// <summary>On the wall's line, part way along it (not at its ends).</summary>
        private static bool Inside(Wall w, Point2 p)
        {
            bool we = w.Direction == WallDirection.WestEast;
            if (Math.Abs(we ? p.Y - w.Start.Y : p.X - w.Start.X) > Tolerance) return false;
            double t = we ? p.X : p.Y;
            double lo = we ? Math.Min(w.Start.X, w.End.X) : Math.Min(w.Start.Y, w.End.Y);
            double hi = we ? Math.Max(w.Start.X, w.End.X) : Math.Max(w.Start.Y, w.End.Y);
            return t > lo + Tolerance && t < hi - Tolerance;
        }

        /// <summary>The two walls stand over some of the same height.</summary>
        private static bool SameStory(Building b, Wall a, Wall c)
        {
            double a0 = b.Level(a.BaseLevelId).Elevation, a1 = b.Level(a.TopLevelId).Elevation;
            double c0 = b.Level(c.BaseLevelId).Elevation, c1 = b.Level(c.TopLevelId).Elevation;
            return Math.Min(a1, c1) - Math.Max(a0, c0) > 1e-6;
        }
    }
}
