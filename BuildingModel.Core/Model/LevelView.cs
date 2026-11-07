using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>
    /// What the plan of one level shows. A level is drawn as the story under it: its diaphragm, the openings that cut it,
    /// the walls that hold it up (from below, reaching it) and its notes. The ground has no story under it: its plan (the
    /// foundation plan) shows the walls and columns standing on it, which are drawn on the levels they carry.
    /// </summary>
    public static class LevelView
    {
        public static IEnumerable<Diaphragm> DiaphragmsOn(this Building b, Level level) =>
            level == null ? Enumerable.Empty<Diaphragm>() : b.Diaphragms.Where(d => d.LevelId == level.Id);

        /// <summary>The walls that reach this level from below (base under it, top at or above it).</summary>
        public static IEnumerable<Wall> WallsUnder(this Building b, Level level)
        {
            if (level == null) return Enumerable.Empty<Wall>();
            return b.Walls.Where(w =>
            {
                var lo = b.Level(w.BaseLevelId);
                var hi = b.Level(w.TopLevelId);
                return lo != null && hi != null && lo.Elevation < level.Elevation && hi.Elevation >= level.Elevation;
            });
        }

        /// <summary>The foundation plan: the lowest level (no story under it). It shows what stands on it.</summary>
        public static bool IsFoundationPlan(this Building b, Level level) => level != null && b.Below(level) == null;

        /// <summary>The walls standing on this level (their base is here).</summary>
        public static IEnumerable<Wall> WallsStandingOn(this Building b, Level level) =>
            level == null ? Enumerable.Empty<Wall>() : b.Walls.Where(w => w.BaseLevelId == level.Id);

        /// <summary>
        /// The walls a level's plan shows, picks and selects: the walls under it (the story it carries). The foundation plan has
        /// no story under it: it shows the walls standing on it, the same walls the level above shows, drawn cut at their base
        /// (a view only: they are selected and edited there, and drawn on the level they carry).
        /// </summary>
        public static IEnumerable<Wall> WallsShown(this Building b, Level level) =>
            b.IsFoundationPlan(level) ? b.WallsStandingOn(level) : b.WallsUnder(level);

        /// <summary>
        /// The columns standing on this level (their base is here: the foundation for the ground), drawn at their base on its
        /// plan so a column run down to the foundation shows on the foundation plan.
        /// </summary>
        public static IEnumerable<Column> ColumnsStandingOn(this Building b, Level level) =>
            level == null ? Enumerable.Empty<Column>() : b.Columns.Where(c => c.BaseLevelId == level.Id);

        /// <summary>The walls of the story above this level (drawn faint, to line walls up).</summary>
        public static IEnumerable<Wall> WallsOver(this Building b, Level level)
        {
            if (level == null) return Enumerable.Empty<Wall>();
            return b.Walls.Where(w =>
            {
                var lo = b.Level(w.BaseLevelId);
                var hi = b.Level(w.TopLevelId);
                return lo != null && hi != null && lo.Elevation >= level.Elevation && lo.Elevation < (b.Above(level)?.Elevation ?? double.MaxValue)
                       && hi.Elevation > level.Elevation;
            });
        }

        public static IEnumerable<Note> NotesOn(this Building b, Level level) =>
            level == null ? Enumerable.Empty<Note>() : b.Notes.Where(n => n.LevelId == level.Id);

        /// <summary>The beams in the framing of this level.</summary>
        public static IEnumerable<Beam> BeamsOn(this Building b, Level level) =>
            level == null ? Enumerable.Empty<Beam>() : b.Beams.Where(x => x.LevelId == level.Id);

        /// <summary>The columns that reach this level from below (base under it, top at or above it), like <see cref="WallsUnder"/>.</summary>
        public static IEnumerable<Column> ColumnsUnder(this Building b, Level level)
        {
            if (level == null) return Enumerable.Empty<Column>();
            return b.Columns.Where(c =>
            {
                var lo = b.Level(c.BaseLevelId);
                var hi = b.Level(c.TopLevelId);
                return lo != null && hi != null && lo.Elevation < level.Elevation && hi.Elevation >= level.Elevation;
            });
        }

        /// <summary>The rectangles of an object on the plan (a diaphragm's or an opening's pieces); none for others.</summary>
        public static IEnumerable<Rect2> PiecesOf(ModelObject o) =>
            o is Diaphragm d ? d.Pieces : o is Opening op ? op.Pieces : Enumerable.Empty<Rect2>();

        /// <summary>The story height under a level (ft): its elevation less the one below; 0 for the ground.</summary>
        public static double StoryHeight(this Building b, Level level)
        {
            var below = b.Below(level);
            return below == null ? 0 : level.Elevation - below.Elevation;
        }

        /// <summary>Everything that can be seen on the plan of a level, as IDs (to keep a selection valid).</summary>
        public static HashSet<string> VisibleIds(this Building b, Level level)
        {
            var ids = new HashSet<string>();
            foreach (var d in b.DiaphragmsOn(level)) ids.Add(d.Id);
            foreach (var o in b.OpeningsCutting(level)) ids.Add(o.Id);
            foreach (var w in b.WallsShown(level)) ids.Add(w.Id);
            foreach (var n in b.NotesOn(level)) ids.Add(n.Id);
            foreach (var x in b.BeamsOn(level)) ids.Add(x.Id);
            foreach (var c in b.ColumnsUnder(level)) ids.Add(c.Id);
            foreach (var c in b.ColumnsStandingOn(level)) ids.Add(c.Id);
            return ids;
        }
    }
}
