using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>
    /// Keeps each diaphragm's framing areas covering it exactly (owner 2026-11-10: there is no span for a diaphragm, and no
    /// default under an override): a part turned the other way is its own area, and what it cuts across is cut back to it,
    /// so a long joist the floor had becomes two shorter ones ending at the turned part. Every edit ends here
    /// (<see cref="Tidy"/>): an area is clipped to its diaphragm, an overlap goes to the area listed first, and a part of the
    /// diaphragm with no area gets one, spanning its shorter way. An area that is no longer a rectangle is cut into
    /// rectangles along its joists (the cuts run with the joists, so no joist crosses one).
    /// </summary>
    public static class FramingAreas
    {
        private const double E = 1e-9;

        /// <summary>The shorter way across a rectangle (W-E when square): where joists are put when nothing says otherwise.</summary>
        public static SpanDirection ShortWay(Rect2 r) => r.Width <= r.Height ? SpanDirection.WestEast : SpanDirection.SouthNorth;

        /// <summary>The diaphragm and the framing area with this ID, or nulls.</summary>
        public static (Diaphragm d, FramingArea a) Find(Building b, string id)
        {
            foreach (var d in b?.Diaphragms ?? new List<Diaphragm>())
            {
                var a = d.Framing?.FirstOrDefault(x => x.Id == id);
                if (a != null) return (d, a);
            }
            return (null, null);
        }

        /// <summary>The framing areas of a level.</summary>
        public static IEnumerable<FramingArea> On(Building b, Level lv) =>
            lv == null ? Enumerable.Empty<FramingArea>() : b.Diaphragms.Where(d => d.LevelId == lv.Id).SelectMany(d => d.Framing ?? new List<FramingArea>());

        /// <summary>Every diaphragm's areas made to cover it exactly. True when anything changed.</summary>
        public static bool Tidy(Building b)
        {
            bool changed = false;
            foreach (var d in b?.Diaphragms ?? new List<Diaphragm>()) changed |= Fill(b, d);
            return changed;
        }

        /// <summary>
        /// One diaphragm's areas made to cover it exactly. A diaphragm with none (a new one, or one from an older file) gets
        /// its old span, the shorter way for Auto, over all of it. True when anything changed.
        /// </summary>
        public static bool Fill(Building b, Diaphragm d)
        {
            d.Framing = d.Framing ?? new List<FramingArea>();
            var whole = Region.Of(d.Pieces ?? new List<Rect2>());
            var ids = new HashSet<string>(b.AllIds(), StringComparer.Ordinal);
            string NewId() { for (int n = 1; ; n++) if (ids.Add("FA" + n)) return "FA" + n; }
            bool changed = false;
            if (d.Framing.Count == 0 && !whole.IsEmpty)
            {
                var span = d.Span != SpanDirection.Auto ? d.Span : ShortWay(whole.Bounds);
                foreach (var r in Slice(whole.Cells(), span)) d.Framing.Add(new FramingArea { Id = NewId(), Rect = r, Span = span });
                changed = true;
            }
            if (d.Span != SpanDirection.Auto) { d.Span = SpanDirection.Auto; changed = true; }

            var kept = new List<FramingArea>();
            var covered = new List<Rect2>();
            foreach (var a in d.Framing)
            {
                if (a.Span == SpanDirection.Auto) { a.Span = ShortWay(a.Rect); changed = true; }
                var part = Within(whole, a.Rect).Minus(covered);
                if (part.IsEmpty) { changed = true; continue; }
                var rects = Slice(part.Cells(), a.Span).OrderByDescending(r => r.Area).ToList();
                if (!(rects.Count == 1 && rects[0].Equals(a.Rect)))
                {
                    changed = true;
                    a.Rect = rects[0];
                }
                kept.Add(a);
                foreach (var r in rects.Skip(1))
                    kept.Add(new FramingArea { Id = NewId(), Rect = r, Span = a.Span });
                covered.AddRange(rects);
            }
            // a part with no framing: its own area, its shorter way
            var gap = whole.Minus(covered);
            if (!gap.IsEmpty)
                foreach (var p in Edits.BuildingEdits.Parts(gap))
                {
                    var span = ShortWay(Region.Of(p).Bounds);
                    foreach (var r in Slice(p, span)) kept.Add(new FramingArea { Id = NewId(), Rect = r, Span = span });
                    changed = true;
                }
            d.Framing = kept;
            return changed;
        }

        /// <summary>
        /// Puts a framing area on a diaphragm: the rectangle (what of it is on the diaphragm) with its joists running
        /// <paramref name="span"/>; the areas it covers are cut back round it. Returns the new area IDs (empty when the
        /// rectangle is off the diaphragm).
        /// </summary>
        public static List<string> Place(Building b, Diaphragm d, Rect2 r, SpanDirection span)
        {
            var added = new List<string>();
            if (b == null || d == null || r.IsEmpty || span == SpanDirection.Auto) return added;
            Fill(b, d);
            var part = Within(Region.Of(d.Pieces), r);
            if (part.IsEmpty) return added;
            var before = new HashSet<string>(d.Framing.Select(a => a.Id));
            var fresh = Slice(part.Cells(), span).Select(x => new FramingArea { Id = "", Rect = x, Span = span }).ToList();
            var ids = new HashSet<string>(b.AllIds(), StringComparer.Ordinal);
            foreach (var a in fresh) for (int n = 1; ; n++) if (ids.Add("FA" + n)) { a.Id = "FA" + n; break; }
            d.Framing.InsertRange(0, fresh); // first: what it covers goes to it
            Fill(b, d);
            added.AddRange(d.Framing.Where(a => !before.Contains(a.Id) && fresh.Any(f => f.Id == a.Id)).Select(a => a.Id));
            return added;
        }

        /// <summary>The diaphragm on a level that a rectangle lies most on, or null.</summary>
        public static Diaphragm DiaphragmUnder(Building b, Level lv, Rect2 r) =>
            b.Diaphragms.Where(d => d.LevelId == lv?.Id).Select(d => (d, a: Region.Of(d.Pieces).AreaWithin(r))).Where(t => t.a > E)
             .OrderByDescending(t => t.a).Select(t => t.d).FirstOrDefault();

        /// <summary>
        /// The way a new area drawn here is put: across the area it lies most on (it is drawn to turn that part), else the
        /// rectangle's shorter way.
        /// </summary>
        public static SpanDirection TurnedFrom(Diaphragm d, Rect2 r)
        {
            var most = d?.Framing?.Select(a => (a, x: Region.Of(new[] { a.Rect }).AreaWithin(r))).Where(t => t.x > E).OrderByDescending(t => t.x).Select(t => t.a).FirstOrDefault();
            return most == null ? ShortWay(r) : most.Span == SpanDirection.SouthNorth ? SpanDirection.WestEast : SpanDirection.SouthNorth;
        }

        /// <summary>Turns an area's joists. False when there is no such area or it already runs that way.</summary>
        public static bool SetSpan(Building b, string areaId, SpanDirection span)
        {
            var (_, a) = Find(b, areaId);
            if (a == null || span == SpanDirection.Auto || a.Span == span) return false;
            a.Span = span;
            return true;
        }

        /// <summary>
        /// Takes an area away: its place goes to the neighbour that shares the longest edge with it (its joists run on into
        /// it), joined to it where the two make one rectangle. False when there is no such area or it is the diaphragm's only
        /// one (a diaphragm is always framed).
        /// </summary>
        public static bool Delete(Building b, string areaId)
        {
            var (d, a) = Find(b, areaId);
            if (a == null || d.Framing.Count < 2) return false;
            d.Framing.Remove(a);
            var next = d.Framing.OrderByDescending(x => SharedEdge(x.Rect, a.Rect)).First();
            if (SharedEdge(next.Rect, a.Rect) > E && Joined(next.Rect, a.Rect) is Rect2 j) next.Rect = j;
            else d.Framing.Add(new FramingArea { Id = a.Id, Rect = a.Rect, Span = SharedEdge(next.Rect, a.Rect) > E ? next.Span : ShortWay(a.Rect) });
            Fill(b, d);
            return true;
        }

        /// <summary>
        /// An area made this rectangle (a corner or a side dragged): grown, it takes what it now covers from the areas round it;
        /// shrunk, the part it gave up goes to the area beside that part (its joists run on into it), else its shorter way.
        /// False when there is no such area or the rectangle is off its diaphragm.
        /// </summary>
        public static bool Reshape(Building b, string areaId, Rect2 to)
        {
            var (d, a) = Find(b, areaId);
            if (a == null || to.IsEmpty || Region.Of(d.Pieces).AreaWithin(to) <= E) return false;
            var old = a.Rect;
            a.Rect = to;
            d.Framing.Remove(a);
            d.Framing.Insert(0, a); // first: what it covers now is its
            foreach (var part in Edits.BuildingEdits.Parts(Region.Of(new[] { old }).Minus(new[] { to })))
            {
                var box = Region.Of(part).Bounds;
                var next = d.Framing.Where(x => x != a).OrderByDescending(x => SharedEdge(x.Rect, box)).FirstOrDefault();
                bool beside = next != null && SharedEdge(next.Rect, box) > E;
                if (beside && Joined(next.Rect, box) is Rect2 j) { next.Rect = j; continue; }
                var span = beside ? next.Span : ShortWay(box);
                d.Framing.AddRange(Slice(part, span).Select(r => new FramingArea { Id = null, Rect = r, Span = span }));
            }
            var ids = new HashSet<string>(b.AllIds().Where(x => x != null), StringComparer.Ordinal);
            foreach (var x in d.Framing.Where(x => x.Id == null)) for (int n = 1; ; n++) if (ids.Add("FA" + n)) { x.Id = "FA" + n; break; }
            Fill(b, d);
            return true;
        }

        /// <summary>
        /// Joins the areas of a diaphragm that run the same way and touch: their joists run on through (cut again along the
        /// joists where the joined shape is not a rectangle). Returns how many areas fewer.
        /// </summary>
        public static int Join(Building b, Diaphragm d)
        {
            if (d?.Framing == null) return 0;
            int before = d.Framing.Count;
            var next = new List<FramingArea>();
            foreach (var g in d.Framing.GroupBy(a => a.Span))
            {
                var areas = g.ToList();
                foreach (var part in Edits.BuildingEdits.Parts(Region.Of(areas.Select(a => a.Rect))))
                {
                    var rects = Slice(part, g.Key).OrderByDescending(r => r.Area).ToList();
                    // the IDs kept: the areas each rectangle covers most of, largest first
                    var pool = areas.Where(a => part.Any(c => Overlap(c, a.Rect) > E)).OrderByDescending(a => a.Rect.Area).ToList();
                    foreach (var r in rects)
                    {
                        var keep = pool.OrderByDescending(a => Overlap(a.Rect, r)).FirstOrDefault();
                        if (keep != null) { pool.Remove(keep); keep.Rect = r; next.Add(keep); }
                        else next.Add(new FramingArea { Id = null, Rect = r, Span = g.Key });
                    }
                }
            }
            var ids = new HashSet<string>(b.AllIds(), StringComparer.Ordinal);
            foreach (var a in next.Where(a => a.Id == null)) for (int n = 1; ; n++) if (ids.Add("FA" + n)) { a.Id = "FA" + n; break; }
            d.Framing = next;
            Fill(b, d);
            return before - d.Framing.Count;
        }

        /// <summary>A copied diaphragm's areas get IDs of their own.</summary>
        public static void Renumber(Building b, Diaphragm d)
        {
            if (d?.Framing == null) return;
            var ids = new HashSet<string>(b.Objects().Where(o => !d.Framing.Contains(o)).Select(o => o.Id), StringComparer.Ordinal);
            foreach (var a in d.Framing) for (int n = 1; ; n++) if (ids.Add("FA" + n)) { a.Id = "FA" + n; break; }
        }

        /// <summary>
        /// Cells joined into rectangles so that no joist crosses a cut: for joists running W-E, the cells in a row joined
        /// across first (each cut runs W-E, with the joists), then strips of the same width stacked; S-N the other way.
        /// </summary>
        public static List<Rect2> Slice(IEnumerable<Rect2> cells, SpanDirection span)
        {
            if (span != SpanDirection.SouthNorth) return Edits.BuildingEdits.Merge(cells);
            Rect2 T(Rect2 r) => new Rect2(r.Y0, r.X0, r.Y1, r.X1);
            return Edits.BuildingEdits.Merge(cells.Select(T)).Select(T).OrderBy(r => r.X0).ThenBy(r => r.Y0).ToList();
        }

        /// <summary>The part of a region inside a rectangle.</summary>
        private static Region Within(Region whole, Rect2 r)
        {
            // checked before the rectangle is made: Rect2 puts its corners in order, so a cell outside would turn into a sliver
            var parts = new List<Rect2>();
            foreach (var c in whole.Cells())
            {
                double x0 = Math.Max(c.X0, r.X0), y0 = Math.Max(c.Y0, r.Y0), x1 = Math.Min(c.X1, r.X1), y1 = Math.Min(c.Y1, r.Y1);
                if (x1 > x0 + E && y1 > y0 + E) parts.Add(new Rect2(x0, y0, x1, y1));
            }
            return Region.Of(parts);
        }

        private static double Overlap(Rect2 a, Rect2 c)
        {
            double w = Math.Min(a.X1, c.X1) - Math.Max(a.X0, c.X0), h = Math.Min(a.Y1, c.Y1) - Math.Max(a.Y0, c.Y0);
            return w > 0 && h > 0 ? w * h : 0;
        }

        /// <summary>The length of edge two rectangles share (side by side or stacked).</summary>
        private static double SharedEdge(Rect2 a, Rect2 c)
        {
            const double e = 1e-6;
            double ox = Math.Min(a.X1, c.X1) - Math.Max(a.X0, c.X0), oy = Math.Min(a.Y1, c.Y1) - Math.Max(a.Y0, c.Y0);
            if ((Math.Abs(a.X1 - c.X0) < e || Math.Abs(c.X1 - a.X0) < e) && oy > 0) return oy;
            if ((Math.Abs(a.Y1 - c.Y0) < e || Math.Abs(c.Y1 - a.Y0) < e) && ox > 0) return ox;
            return 0;
        }

        /// <summary>Two rectangles that make one rectangle side by side, or null.</summary>
        private static Rect2? Joined(Rect2 a, Rect2 c)
        {
            const double e = 1e-6;
            if (Math.Abs(a.Y0 - c.Y0) < e && Math.Abs(a.Y1 - c.Y1) < e && (Math.Abs(a.X1 - c.X0) < e || Math.Abs(c.X1 - a.X0) < e))
                return new Rect2(Math.Min(a.X0, c.X0), a.Y0, Math.Max(a.X1, c.X1), a.Y1);
            if (Math.Abs(a.X0 - c.X0) < e && Math.Abs(a.X1 - c.X1) < e && (Math.Abs(a.Y1 - c.Y0) < e || Math.Abs(c.Y1 - a.Y0) < e))
                return new Rect2(a.X0, Math.Min(a.Y0, c.Y0), a.X1, Math.Max(a.Y1, c.Y1));
            return null;
        }
    }
}
