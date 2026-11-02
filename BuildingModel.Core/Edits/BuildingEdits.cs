using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;

namespace BuildingModel.Edits
{
    /// <summary>A side of a rectangle (the plan's axes: x east, y north).</summary>
    public enum RectSide { West, East, South, North }

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
                case Beam bm: return b.Beams.Remove(bm);
                case Column c: return b.Columns.Remove(c);
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
                case Beam bm: bm.Start = Shift(bm.Start, dx, dy); bm.End = Shift(bm.End, dx, dy); return true;
                case Column c: c.At = Shift(c.At, dx, dy); return true;
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
                case Beam bm: b.Beams.Add(bm); break;
                case Column c: b.Columns.Add(c); break;
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
                    case Beam bm: bm.Start = M(bm.Start); bm.End = M(bm.End); break;
                    case Column c: c.At = M(c.At); break;
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

        /// <summary>Moves one end of a wall along its own line (grip edit): the length changes, the direction and the other end stay.</summary>
        public static bool MoveWallEnd(Building b, string id, bool start, Point2 to)
        {
            if (!(b.Find(id) is Wall w)) return false;
            var fixedEnd = start ? w.End : w.Start;
            var p = AlongWall(w, to);
            if (Snapper.Dist(p, fixedEnd) < MinimumWall) return false;
            if (start) w.Start = p; else w.End = p;
            return true;
        }

        /// <summary>The point on the wall's own line (extended) closest to <paramref name="p"/>.</summary>
        public static Point2 AlongWall(Wall w, Point2 p)
        {
            double ux = w.End.X - w.Start.X, uy = w.End.Y - w.Start.Y, len2 = ux * ux + uy * uy;
            if (len2 < 1e-12) return p;
            double t = ((p.X - w.Start.X) * ux + (p.Y - w.Start.Y) * uy) / len2;
            return new Point2(w.Start.X + t * ux, w.Start.Y + t * uy);
        }

        /// <summary>The offset from <paramref name="from"/> to <paramref name="to"/>, straight across or straight up (whichever is larger).</summary>
        public static (double dx, double dy) OrthoOffset(Point2 from, Point2 to)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y;
            return Math.Abs(dx) >= Math.Abs(dy) ? (dx, 0.0) : (0.0, dy);
        }

        /// <summary>Moves every object by (dx, dy) ft; the number moved.</summary>
        public static int MoveAll(Building b, IEnumerable<string> ids, double dx, double dy) =>
            ids.Distinct().Count(id => Move(b, id, dx, dy));

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

        /// <summary>
        /// Moves one side of a rectangle piece of a diaphragm or an opening to <paramref name="to"/> (an x for the west and east
        /// sides, a y for the south and north): the side keeps its length, the two sides next to it get longer or shorter.
        /// False when there is no such piece or the rectangle would have no area.
        /// </summary>
        public static bool MoveSide(Building b, string id, int piece, RectSide side, double to)
        {
            var o = b.Find(id);
            var pieces = o is Diaphragm d ? d.Pieces : o is Opening op ? op.Pieces : null;
            if (pieces == null || piece < 0 || piece >= pieces.Count || double.IsNaN(to) || double.IsInfinity(to)) return false;
            var r = pieces[piece];
            double x0 = r.X0, y0 = r.Y0, x1 = r.X1, y1 = r.Y1;
            switch (side)
            {
                case RectSide.West: x0 = to; break;
                case RectSide.East: x1 = to; break;
                case RectSide.South: y0 = to; break;
                default: y1 = to; break;
            }
            if (Math.Abs(x1 - x0) < 1e-6 || Math.Abs(y1 - y0) < 1e-6) return false;
            pieces[piece] = new Rect2(x0, y0, x1, y1);
            return true;
        }

        /// <summary>
        /// Copies objects to another level at the same place in plan: diaphragms and notes onto <paramref name="to"/>, walls into
        /// the story under it (one story), openings through it (from the level below). Each copy keeps its data. Returns the new
        /// IDs (nothing for what cannot go there, e.g. a wall when <paramref name="to"/> is the ground).
        /// </summary>
        public static List<string> CopyToLevel(Building b, IEnumerable<string> ids, Level to)
        {
            var added = new List<string>();
            if (to == null || ids == null) return added;
            var below = b.Below(to);
            foreach (var id in ids.ToList())
            {
                var o = b.Find(id);
                if (o == null || o is Level || o is Building) continue;
                if ((o is Wall || o is Opening || o is Column) && below == null) continue;
                if (!(o is Diaphragm || o is Note || o is Wall || o is Opening || o is Beam || o is Column)) continue;
                var copy = CloneObject(o);
                copy.Id = b.NewId(Prefix(o));
                switch (copy)
                {
                    case Diaphragm d: d.LevelId = to.Id; b.Diaphragms.Add(d); break;
                    case Note n: n.LevelId = to.Id; b.Notes.Add(n); break;
                    case Wall w: w.BaseLevelId = below.Id; w.TopLevelId = to.Id; b.Walls.Add(w); break;
                    case Opening op: op.BottomLevelId = below.Id; op.TopLevelId = to.Id; b.Openings.Add(op); break;
                    case Beam bm: bm.LevelId = to.Id; b.Beams.Add(bm); break;
                    case Column c: c.BaseLevelId = below.Id; c.TopLevelId = to.Id; b.Columns.Add(c); break;
                }
                added.Add(copy.Id);
            }
            return added;
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
            foreach (var bm in level == null ? b.Beams : b.BeamsOn(level).ToList()) { pts.Add(bm.Start); pts.Add(bm.End); }
            foreach (var c in level == null ? b.Columns : b.ColumnsUnder(level).ToList()) pts.Add(c.At);
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
                case Beam bm: return bm.Middle;
                case Column c: return c.At;
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
        /// <summary>How close (ft) a column must be to a wall, beam or column below to bear on it.</summary>
        public const double BearingTolerance = 0.25;

        /// <summary>
        /// Where a column under <paramref name="top"/> at <paramref name="at"/> stands: a vertical load path goes down until something
        /// carries it. Going down from the level below, the first level with a wall under it through the point, a beam on it
        /// through the point, or a column under it at the point; otherwise the ground. So a post drawn on the third floor with
        /// nothing under it runs down through the second floor to the foundation.
        /// </summary>
        public static Level ColumnBase(Building b, Level top, Point2 at)
        {
            var l = b.Below(top);
            while (l != null && l.Elevation > 0)
            {
                if (b.WallsUnder(l).Any(w => Snapper.DistanceToSegment(at, new Segment2(w.Start, w.End)) <= BearingTolerance)) return l;
                if (b.BeamsOn(l).Any(x => Snapper.DistanceToSegment(at, new Segment2(x.Start, x.End)) <= BearingTolerance)) return l;
                if (b.ColumnsUnder(l).Any(c => Snapper.Dist(c.At, at) <= BearingTolerance)) return l;
                var next = b.Below(l);
                if (next == null) return l;
                l = next;
            }
            return l;
        }

        public static void DeleteStory(Building b, Level level)
        {
            if (level == null || !b.Levels.Contains(level)) throw new ArgumentException("No such level.");
            if (level.Elevation == 0) throw new InvalidOperationException("The ground level cannot be deleted.");
            var below = b.Below(level);
            double h = level.Elevation - below.Elevation;
            b.Diaphragms.RemoveAll(d => d.LevelId == level.Id);
            b.Notes.RemoveAll(n => n.LevelId == level.Id);
            b.Beams.RemoveAll(x => x.LevelId == level.Id);
            b.Columns.RemoveAll(c => c.TopLevelId == level.Id && c.BaseLevelId == below.Id);
            foreach (var c in b.Columns)
            {
                if (c.TopLevelId == level.Id) c.TopLevelId = below.Id;
                if (c.BaseLevelId == level.Id) c.BaseLevelId = below.Id;
            }
            b.Columns.RemoveAll(c => c.BaseLevelId == c.TopLevelId);
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

        /// <summary>
        /// The standard story names, bottom up: Level 2, Level 3, ..., and Roof for the top of two or more (one story: Level 2).
        /// A name the user gave (anything but "Level n" or "Roof") stays.
        /// </summary>
        public static void NameStories(Building b)
        {
            var up = b.LevelsBottomUp.Where(l => l.Elevation > 0).ToList();
            for (int i = 0; i < up.Count; i++)
            {
                string name = up[i].Name ?? "";
                bool standard = name.Length == 0 || name == "Roof" || (name.StartsWith("Level ", StringComparison.Ordinal) && int.TryParse(name.Substring(6), out _));
                if (standard) up[i].Name = i == up.Count - 1 && up.Count > 1 ? "Roof" : $"Level {i + 2}";
            }
        }

        // ------------------------------------------------------------------ a building from its size

        /// <summary>What "New from size" builds: a rectangle L × B (ft) at the origin, its stories, and outside walls.</summary>
        public sealed class QuickBuilding
        {
            /// <summary>L: W-E (ft).</summary>
            public double Length { get; set; }

            /// <summary>B: S-N (ft).</summary>
            public double Width { get; set; }

            /// <summary>The story heights, bottom up (ft); one per story.</summary>
            public List<double> StoryHeights { get; set; } = new List<double>();

            /// <summary>Put shear walls on the four outside walls of every story.</summary>
            public bool OutsideShearWalls { get; set; } = true;

            /// <summary>Why it cannot be built (null when it can).</summary>
            public string Problem()
            {
                if (!(Length > 0) || !(Width > 0)) return "L and B must be more than 0 ft.";
                if (StoryHeights == null || StoryHeights.Count == 0) return "Give at least one story.";
                if (StoryHeights.Any(h => !(h > 0))) return "Every story height must be more than 0 ft.";
                return null;
            }

            /// <summary>The model's defaults as a starting point: one story of the default height, the default plan.</summary>
            public static QuickBuilding FromDefaults(ModelDefaults d) => new QuickBuilding
            {
                Length = d.NewLength, Width = d.NewWidth,
                StoryHeights = Enumerable.Repeat(d.StoryHeight, Math.Max(1, d.NewStories)).ToList(),
            };

            /// <summary>The building as it is now, where it is a plain box (else the defaults): to start the window from.</summary>
            public static QuickBuilding Of(Building b)
            {
                var q = FromDefaults(b.Defaults);
                var up = b.LevelsBottomUp.Where(l => l.Elevation > 0).ToList();
                var fp = b.Footprint();
                if (up.Count > 0) q.StoryHeights = up.Select(l => b.StoryHeight(l)).ToList();
                if (!fp.IsEmpty) { q.Length = fp.Bounds.Width; q.Width = fp.Bounds.Height; }
                return q;
            }
        }

        /// <summary>
        /// Replaces the building's drawing with a plain box: the ground and one level per story, a diaphragm L × B on each,
        /// and (when asked) shear walls on the four outside walls of each story. The building's name, roof, defaults and
        /// its own data stay; walls, diaphragms, openings and notes on every level go. False (nothing changed) when the
        /// size cannot be built. One undo step for the caller.
        /// </summary>
        public static bool BuildFromSize(Building b, QuickBuilding q)
        {
            if (b == null || q == null || q.Problem() != null) return false;
            b.Walls.Clear();
            b.Diaphragms.Clear();
            b.Openings.Clear();
            b.Notes.Clear();
            var ground = b.Ground;
            b.Levels.RemoveAll(l => l != ground);
            if (ground == null) b.Levels.Add(ground = new Level { Id = b.NewId("L"), Name = "Ground", Elevation = 0 });
            var rect = new Rect2(0, 0, q.Length, q.Width);
            var below = ground;
            foreach (double h in q.StoryHeights)
            {
                var l = AddStory(b, h);
                b.AddDiaphragm(l, rect);
                if (q.OutsideShearWalls)
                {
                    b.AddWall(below, l, 0, 0, q.Length, 0);
                    b.AddWall(below, l, 0, q.Width, q.Length, q.Width);
                    b.AddWall(below, l, 0, 0, 0, q.Width);
                    b.AddWall(below, l, q.Length, 0, q.Length, q.Width);
                }
                below = l;
            }
            NameStories(b);
            return true;
        }

        // ------------------------------------------------------------------ cut

        /// <summary>What a cut did: diaphragms made smaller, taken away whole, and the new ones a cut split off.</summary>
        public sealed class CutResult
        {
            public List<string> Changed { get; } = new List<string>();
            public List<string> Removed { get; } = new List<string>();
            /// <summary>Each split: the diaphragm cut and the IDs of the separate parts made from it (it keeps the largest).</summary>
            public List<(string from, List<string> parts)> Split { get; } = new List<(string, List<string>)>();
            public bool Any => Changed.Count + Removed.Count > 0;
        }

        /// <summary>
        /// Takes a rectangle out of the diaphragms of a level (a notch, an L, a courtyard): each diaphragm it overlaps keeps
        /// the rest, as few rectangles as practical. A diaphragm left with nothing is deleted. One left in separate parts
        /// becomes separate diaphragms: it keeps the largest part (its ID and data), the others are new diaphragms with a
        /// copy of its data. Unlike an opening (a hole through floors the checks know about), a cut changes the outline.
        /// </summary>
        public static CutResult CutDiaphragms(Building b, Level level, Rect2 cut)
        {
            var result = new CutResult();
            if (b == null || level == null || cut.IsEmpty) return result;
            foreach (var d in b.Diaphragms.Where(d => d.LevelId == level.Id).ToList())
            {
                var before = Region.Of(d.Pieces);
                if (!(before.AreaWithin(cut) > 1e-9)) continue;
                var after = before.Minus(new[] { cut });
                if (after.IsEmpty)
                {
                    b.Diaphragms.Remove(d);
                    result.Removed.Add(d.Id);
                    continue;
                }
                var parts = Parts(after).OrderByDescending(p => p.Sum(r => r.Area)).ToList();
                d.Pieces = Merge(parts[0]);
                result.Changed.Add(d.Id);
                if (parts.Count == 1) continue;
                var ids = new List<string>();
                foreach (var p in parts.Skip(1))
                {
                    var extra = (Diaphragm)CloneObject(d);
                    extra.Id = b.NewId("D");
                    extra.Pieces = Merge(p);
                    b.Diaphragms.Add(extra);
                    ids.Add(extra.Id);
                }
                result.Split.Add((d.Id, ids));
            }
            return result;
        }

        /// <summary>The region's cells grouped into pieces that touch along an edge (a corner touch does not join).</summary>
        public static List<List<Rect2>> Parts(Region region)
        {
            var cells = region.Cells().ToList();
            var group = new int[cells.Count];
            for (int i = 0; i < group.Length; i++) group[i] = -1;
            int n = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (group[i] >= 0) continue;
                var stack = new Stack<int>();
                stack.Push(i);
                group[i] = n;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    for (int j = 0; j < cells.Count; j++)
                        if (group[j] < 0 && ShareEdge(cells[k], cells[j])) { group[j] = n; stack.Push(j); }
                }
                n++;
            }
            return Enumerable.Range(0, n).Select(g => cells.Where((c, i) => group[i] == g).ToList()).ToList();
        }

        private static bool ShareEdge(Rect2 a, Rect2 c)
        {
            const double e = 1e-9;
            double overlapX = Math.Min(a.X1, c.X1) - Math.Max(a.X0, c.X0);
            double overlapY = Math.Min(a.Y1, c.Y1) - Math.Max(a.Y0, c.Y0);
            bool sideBySide = (Math.Abs(a.X1 - c.X0) < e || Math.Abs(c.X1 - a.X0) < e) && overlapY > e;
            bool stacked = (Math.Abs(a.Y1 - c.Y0) < e || Math.Abs(c.Y1 - a.Y0) < e) && overlapX > e;
            return sideBySide || stacked;
        }

        /// <summary>
        /// Grid cells joined into fewer rectangles with the same area: cells in a row joined across, then rows of the same
        /// span joined up.
        /// </summary>
        public static List<Rect2> Merge(IEnumerable<Rect2> cells)
        {
            const double e = 1e-9;
            var strips = new List<Rect2>();
            foreach (var row in cells.GroupBy(c => (c.Y0, c.Y1)))
            {
                Rect2? run = null;
                foreach (var c in row.OrderBy(c => c.X0))
                {
                    if (run != null && Math.Abs(run.Value.X1 - c.X0) < e) run = new Rect2(run.Value.X0, run.Value.Y0, c.X1, run.Value.Y1);
                    else { if (run != null) strips.Add(run.Value); run = c; }
                }
                if (run != null) strips.Add(run.Value);
            }
            var merged = new List<Rect2>();
            foreach (var col in strips.GroupBy(s => (s.X0, s.X1)))
            {
                Rect2? run = null;
                foreach (var s in col.OrderBy(s => s.Y0))
                {
                    if (run != null && Math.Abs(run.Value.Y1 - s.Y0) < e) run = new Rect2(run.Value.X0, run.Value.Y0, run.Value.X1, s.Y1);
                    else { if (run != null) merged.Add(run.Value); run = s; }
                }
                if (run != null) merged.Add(run.Value);
            }
            return merged.OrderBy(r => r.Y0).ThenBy(r => r.X0).ToList();
        }

        // ------------------------------------------------------------------ helpers

        public static string Prefix(ModelObject o) => o is Level ? "L" : o is Diaphragm ? "D" : o is Opening ? "O" : o is Wall ? "W" : o is Note ? "N" : o is Beam ? "BM" : o is Column ? "C" : "X";

        public static string KindName(ModelObject o) => o is Level ? "level" : o is Diaphragm ? "diaphragm" : o is Opening ? "opening" : o is Wall ? "wall" : o is Note ? "note" : o is Beam ? "beam" : o is Column ? "column" : "object";

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
                case Beam bm: tmp.Beams.Add(bm); break;
                case Column c: tmp.Columns.Add(c); break;
                default: throw new ArgumentException("Not a model object.");
            }
            var copy = ModelFile.Clone(tmp);
            return copy.Objects().First();
        }

        private static Rect2 Shift(Rect2 r, double dx, double dy) => new Rect2(r.X0 + dx, r.Y0 + dy, r.X1 + dx, r.Y1 + dy);
        private static Point2 Shift(Point2 p, double dx, double dy) => new Point2(p.X + dx, p.Y + dy);
    }
}
