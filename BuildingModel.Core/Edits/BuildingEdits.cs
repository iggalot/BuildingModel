using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;

namespace BuildingModel.Edits
{
    /// <summary>
    /// The drafting edits on the model: delete, move, copy, split and stretch; levels and stories added, copied and
    /// deleted. Every edit keeps the IDs of what it does not delete, and an object's data bag goes with it (a copy
    /// gets a copy of the bag).
    /// </summary>
    public static class BuildingEdits
    {
        /// <summary>Walls shorter than this are not drawn (a double click).</summary>
        public const double MinimumWall = 0.1;

        // ------------------------------------------------------------------ objects

        /// <summary>Deletes a diaphragm, opening, wall or note. A level is deleted with <see cref="DeleteStory"/>. False when there is no such object.</summary>
        public static bool Delete(Building b, string id)
        {
            switch (b.Find(id))
            {
                case Diaphragm d: return b.Diaphragms.Remove(d);
                case Opening o: return b.Openings.Remove(o);
                case Wall w: return b.Walls.Remove(w);
                case Note n: return b.Notes.Remove(n);
                default: return false;
            }
        }

        /// <summary>Moves an object by (dx, dy) ft. False when it cannot move (a level, or no such object).</summary>
        public static bool Move(Building b, string id, double dx, double dy)
        {
            switch (b.Find(id))
            {
                case Diaphragm d: d.Pieces = d.Pieces.Select(r => Shift(r, dx, dy)).ToList(); return true;
                case Opening o: o.Pieces = o.Pieces.Select(r => Shift(r, dx, dy)).ToList(); return true;
                case Wall w: w.Start = Shift(w.Start, dx, dy); w.End = Shift(w.End, dx, dy); return true;
                case Note n: n.At = Shift(n.At, dx, dy); return true;
                default: return false;
            }
        }

        /// <summary>Adds a copy moved by (dx, dy) ft, with a copy of its data. Returns the new ID, or null.</summary>
        public static string Copy(Building b, string id, double dx, double dy)
        {
            var o = b.Find(id);
            if (o == null || o is Level || o is Building) return null;
            var copy = CloneObject(o);
            copy.Id = b.NewId(Prefix(o));
            switch (copy)
            {
                case Diaphragm d: b.Diaphragms.Add(d); break;
                case Opening op: b.Openings.Add(op); break;
                case Wall w: b.Walls.Add(w); break;
                case Note n: b.Notes.Add(n); break;
            }
            Move(b, copy.Id, dx, dy);
            return copy.Id;
        }

        /// <summary>
        /// Copies objects in a row: <paramref name="count"/> more copies, each (dx, dy) from the last (an array). Returns the new IDs.
        /// </summary>
        public static List<string> ArrayCopy(Building b, IEnumerable<string> ids, int count, double dx, double dy)
        {
            var added = new List<string>();
            foreach (var id in ids.ToList())
                for (int i = 1; i <= count; i++)
                {
                    var c = Copy(b, id, dx * i, dy * i);
                    if (c != null) added.Add(c);
                }
            return added;
        }

        /// <summary>
        /// Mirrors objects about a vertical line x = <paramref name="axis"/> (or a horizontal line y = axis when
        /// <paramref name="aboutVertical"/> is false), as copies or in place. Returns the IDs of the mirrored objects.
        /// </summary>
        public static List<string> Mirror(Building b, IEnumerable<string> ids, bool aboutVertical, double axis, bool keepOriginal)
        {
            Point2 M(Point2 p) => aboutVertical ? new Point2(2 * axis - p.X, p.Y) : new Point2(p.X, 2 * axis - p.Y);
            Rect2 R(Rect2 r) { var a = M(new Point2(r.X0, r.Y0)); var c = M(new Point2(r.X1, r.Y1)); return new Rect2(a.X, a.Y, c.X, c.Y); }
            var result = new List<string>();
            foreach (var id in ids.ToList())
            {
                string target = keepOriginal ? Copy(b, id, 0, 0) : id;
                switch (b.Find(target))
                {
                    case Diaphragm d: d.Pieces = d.Pieces.Select(R).ToList(); break;
                    case Opening o: o.Pieces = o.Pieces.Select(R).ToList(); break;
                    case Wall w: w.Start = M(w.Start); w.End = M(w.End); break;
                    case Note n: n.At = M(n.At); break;
                    default: continue;
                }
                result.Add(target);
            }
            return result;
        }

        /// <summary>
        /// Splits a wall at the point on it nearest <paramref name="at"/>: the wall keeps the part from its start, a new
        /// wall (with a copy of its data) takes the rest. Returns the new wall's ID, or null when the point is at an end.
        /// </summary>
        public static string SplitWall(Building b, string id, Point2 at)
        {
            if (!(b.Find(id) is Wall w)) return null;
            var p = Snapper.ClosestOnSegment(at, new Segment2(w.Start, w.End));
            if (Snapper.Dist(p, w.Start) < MinimumWall || Snapper.Dist(p, w.End) < MinimumWall) return null;
            var rest = (Wall)CloneObject(w);
            rest.Id = b.NewId("W");
            rest.Start = p;
            w.End = p;
            b.Walls.Insert(b.Walls.IndexOf(w) + 1, rest);
            return rest.Id;
        }

        /// <summary>Joins two walls that are end to end on one line, with the same levels: the first keeps the joined length. False otherwise.</summary>
        public static bool JoinWalls(Building b, string first, string second)
        {
            if (!(b.Find(first) is Wall a) || !(b.Find(second) is Wall c) || a == c) return false;
            if (a.Direction == WallDirection.NotOrthogonal || a.Direction != c.Direction) return false;
            if (a.BaseLevelId != c.BaseLevelId || a.TopLevelId != c.TopLevelId) return false;
            bool we = a.Direction == WallDirection.WestEast;
            if (we ? a.Start.Y != c.Start.Y : a.Start.X != c.Start.X) return false;
            double Lo(Wall w) => we ? Math.Min(w.Start.X, w.End.X) : Math.Min(w.Start.Y, w.End.Y);
            double Hi(Wall w) => we ? Math.Max(w.Start.X, w.End.X) : Math.Max(w.Start.Y, w.End.Y);
            if (Lo(c) > Hi(a) + 1e-9 || Lo(a) > Hi(c) + 1e-9) return false; // a gap between them
            double lo = Math.Min(Lo(a), Lo(c)), hi = Math.Max(Hi(a), Hi(c));
            a.Start = we ? new Point2(lo, a.Start.Y) : new Point2(a.Start.X, lo);
            a.End = we ? new Point2(hi, a.Start.Y) : new Point2(a.Start.X, hi);
            b.Walls.Remove(c);
            return true;
        }

        /// <summary>Moves one end of a wall (grip edit); the wall stays square: the other coordinate follows the fixed end.</summary>
        public static bool MoveWallEnd(Building b, string id, bool start, Point2 to)
        {
            if (!(b.Find(id) is Wall w)) return false;
            var fixedEnd = start ? w.End : w.Start;
            var p = Snapper.Ortho(to, fixedEnd);
            if (Snapper.Dist(p, fixedEnd) < MinimumWall) return false;
            if (start) w.Start = p; else w.End = p;
            return true;
        }

        /// <summary>Moves one corner of a rectangle piece (grip edit); the opposite corner stays.</summary>
        public static bool MoveCorner(Building b, string id, int piece, Point2 corner, Point2 to)
        {
            var o = b.Find(id);
            var pieces = o is Diaphragm d ? d.Pieces : o is Opening op ? op.Pieces : null;
            if (pieces == null || piece < 0 || piece >= pieces.Count) return false;
            var r = pieces[piece];
            double ox = Math.Abs(corner.X - r.X0) < Math.Abs(corner.X - r.X1) ? r.X1 : r.X0;
            double oy = Math.Abs(corner.Y - r.Y0) < Math.Abs(corner.Y - r.Y1) ? r.Y1 : r.Y0;
            var n = new Rect2(ox, oy, to.X, to.Y);
            if (n.IsEmpty) return false;
            pieces[piece] = n;
            return true;
        }

        /// <summary>The box around everything on the plan (all levels); null when there is nothing.</summary>
        public static Rect2? Extents(Building b, Level level = null)
        {
            var pts = new List<Point2>();
            var walls = level == null ? b.Walls : b.WallsUnder(level).ToList();
            var diaphragms = level == null ? b.Diaphragms : b.DiaphragmsOn(level).ToList();
            foreach (var w in walls) { pts.Add(w.Start); pts.Add(w.End); }
            foreach (var r in diaphragms.SelectMany(d => d.Pieces).Concat(b.Openings.SelectMany(o => o.Pieces)))
            {
                pts.Add(new Point2(r.X0, r.Y0));
                pts.Add(new Point2(r.X1, r.Y1));
            }
            foreach (var n in level == null ? b.Notes : b.NotesOn(level).ToList()) pts.Add(n.At);
            if (pts.Count == 0) return null;
            return new Rect2(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        }

        /// <summary>A rough anchor for an object on the plan: a wall's middle, a rectangle's centre, a note's point.</summary>
        public static Point2? Anchor(Building b, string id)
        {
            switch (b.Find(id))
            {
                case Wall w: return w.Middle;
                case Note n: return n.At;
                case ModelObject o when LevelView.PiecesOf(o).Any():
                    var r = LevelView.PiecesOf(o).First();
                    return new Point2((r.X0 + r.X1) / 2, (r.Y0 + r.Y1) / 2);
                default: return null;
            }
        }

        // ------------------------------------------------------------------ levels and stories

        /// <summary>The next "Level n" name: one more than the number of levels above the ground.</summary>
        public static string NextLevelName(Building b) => "Level " + (b.Levels.Count(l => l.Elevation > 0) + 2);

        /// <summary>Adds a level <paramref name="storyHeight"/> ft over the top one. Returns it.</summary>
        public static Level AddStory(Building b, double storyHeight, string name = null)
        {
            if (!(storyHeight > 0)) throw new ArgumentOutOfRangeException(nameof(storyHeight), "The story height must be more than 0.");
            double top = b.Levels.Count == 0 ? 0 : b.Levels.Max(l => l.Elevation);
            if (b.Ground == null) b.Levels.Insert(0, new Level { Id = b.NewId("L"), Name = "Ground", Elevation = 0 });
            return b.AddLevel(name ?? NextLevelName(b), top + storyHeight);
        }

        /// <summary>
        /// Puts the story under <paramref name="to"/> as a copy of the story under <paramref name="from"/>: the diaphragms
        /// on the level, the walls of one story under it and the notes, with their data. Returns how many were copied.
        /// </summary>
        public static int CopyStory(Building b, Level from, Level to)
        {
            if (from == null || to == null || from == to) return 0;
            var toBelow = b.Below(to);
            var fromBelow = b.Below(from);
            int n = 0;
            foreach (var d in b.DiaphragmsOn(from).ToList())
            {
                var c = (Diaphragm)CloneObject(d);
                c.Id = b.NewId("D");
                c.LevelId = to.Id;
                b.Diaphragms.Add(c);
                n++;
            }
            if (toBelow != null)
                foreach (var w in b.Walls.Where(w => w.TopLevelId == from.Id && w.BaseLevelId == fromBelow?.Id).ToList())
                {
                    var c = (Wall)CloneObject(w);
                    c.Id = b.NewId("W");
                    c.BaseLevelId = toBelow.Id;
                    c.TopLevelId = to.Id;
                    b.Walls.Add(c);
                    n++;
                }
            foreach (var note in b.NotesOn(from).ToList())
            {
                var c = (Note)CloneObject(note);
                c.Id = b.NewId("N");
                c.LevelId = to.Id;
                b.Notes.Add(c);
                n++;
            }
            return n;
        }

        /// <summary>
        /// What <see cref="DeleteStory"/> would do, in words (to confirm first). Empty when the level does not exist or is the ground.
        /// </summary>
        public static List<string> DeleteStoryConsequences(Building b, Level level)
        {
            var list = new List<string>();
            if (level == null || level.Elevation == 0) return list;
            var below = b.Below(level);
            double h = b.StoryHeight(level);
            int dia = b.DiaphragmsOn(level).Count(), notes = b.NotesOn(level).Count();
            int walls = b.Walls.Count(w => w.TopLevelId == level.Id && w.BaseLevelId == below?.Id);
            int cut = b.Walls.Count(w => w.TopLevelId == level.Id && w.BaseLevelId != below?.Id);
            if (dia > 0) list.Add($"{dia} diaphragm{(dia == 1 ? "" : "s")} on {level.Name} deleted");
            if (walls > 0) list.Add($"{walls} wall{(walls == 1 ? "" : "s")} of the story under it deleted");
            if (cut > 0) list.Add($"{cut} taller wall{(cut == 1 ? "" : "s")} ending there will end at {below?.Name}");
            if (notes > 0) list.Add($"{notes} note{(notes == 1 ? "" : "s")} deleted");
            var above = b.Levels.Where(l => l.Elevation > level.Elevation).OrderBy(l => l.Elevation).ToList();
            if (above.Count > 0) list.Add($"{string.Join(", ", above.Select(l => l.Name))} come{(above.Count == 1 ? "s" : "")} down {h:0.##} ft");
            return list;
        }

        /// <summary>
        /// Deletes a level and the story under it: its diaphragms and notes, the walls of that one story, and openings
        /// left with no height. Taller walls and openings that ended there end at the level below; the levels above come
        /// down by the story height so the other stories keep their heights. The ground cannot be deleted.
        /// </summary>
        public static void DeleteStory(Building b, Level level)
        {
            if (level == null || !b.Levels.Contains(level)) throw new ArgumentException("No such level.");
            if (level.Elevation == 0) throw new InvalidOperationException("The ground level cannot be deleted.");
            var below = b.Below(level);
            double h = level.Elevation - below.Elevation;
            b.Diaphragms.RemoveAll(d => d.LevelId == level.Id);
            b.Notes.RemoveAll(n => n.LevelId == level.Id);
            b.Walls.RemoveAll(w => w.TopLevelId == level.Id && w.BaseLevelId == below.Id);
            foreach (var w in b.Walls)
            {
                if (w.TopLevelId == level.Id) w.TopLevelId = below.Id;
                if (w.BaseLevelId == level.Id) w.BaseLevelId = below.Id;
            }
            foreach (var o in b.Openings)
            {
                if (o.TopLevelId == level.Id) o.TopLevelId = below.Id;
                if (o.BottomLevelId == level.Id) o.BottomLevelId = below.Id;
            }
            b.Walls.RemoveAll(w => w.BaseLevelId == w.TopLevelId);
            b.Openings.RemoveAll(o => o.BottomLevelId == o.TopLevelId);
            foreach (var l in b.Levels.Where(l => l.Elevation > level.Elevation)) l.Elevation -= h;
            b.Levels.Remove(level);
        }

        /// <summary>
        /// Changes the story height under a level: it and every level above move by the difference. False when the new
        /// height is not more than 0 or the level is the ground.
        /// </summary>
        public static bool SetStoryHeight(Building b, Level level, double height)
        {
            if (level == null || level.Elevation == 0 || !(height > 0)) return false;
            double d = height - b.StoryHeight(level);
            double z = level.Elevation;
            foreach (var l in b.Levels.Where(l => l.Elevation >= z)) l.Elevation += d;
            return true;
        }

        // ------------------------------------------------------------------ helpers

        public static string Prefix(ModelObject o) => o is Level ? "L" : o is Diaphragm ? "D" : o is Opening ? "O" : o is Wall ? "W" : o is Note ? "N" : "X";

        public static string KindName(ModelObject o) => o is Level ? "level" : o is Diaphragm ? "diaphragm" : o is Opening ? "opening" : o is Wall ? "wall" : o is Note ? "note" : "object";

        /// <summary>A deep copy of one object (data included), through the building's JSON.</summary>
        public static ModelObject CloneObject(ModelObject o)
        {
            var tmp = new Building();
            switch (o)
            {
                case Level l: tmp.Levels.Add(l); break;
                case Diaphragm d: tmp.Diaphragms.Add(d); break;
                case Opening op: tmp.Openings.Add(op); break;
                case Wall w: tmp.Walls.Add(w); break;
                case Note n: tmp.Notes.Add(n); break;
                default: throw new ArgumentException("Not a model object.");
            }
            var copy = ModelFile.Clone(tmp);
            return copy.Objects().First();
        }

        private static Rect2 Shift(Rect2 r, double dx, double dy) => new Rect2(r.X0 + dx, r.Y0 + dy, r.X1 + dx, r.Y1 + dy);
        private static Point2 Shift(Point2 p, double dx, double dy) => new Point2(p.X + dx, p.Y + dy);
    }
}
