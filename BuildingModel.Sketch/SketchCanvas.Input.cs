using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>The tools at work: points clicked or typed, picks, window selection, grips, pan and zoom, keys.</summary>
    public partial class SketchCanvas
    {
        private Point2? first;                 // the first point of the command under way
        private Point2 cursor;                 // the cursor in ft, snapped
        private Point2 rawCursor;              // without snaps
        private SnapResult snap;
        private (Point2 a, Point2 b)? lastMeasure;
        private Point? panFrom, windowFrom;
        private Grip? grip;                    // a grip being dragged
        private string hovered;                // what the status says is under the cursor
        private List<Segment2> segments;       // snap lines of the level (cleared on every change)

        private Point gripDown;                // where (on screen) the grip drag started: a click without a drag changes nothing

        /// <summary>
        /// A handle on the selection, dragged to edit it: the end of a wall (its length, along its own line), the middle of
        /// a wall (moves everything selected, straight across or straight up), or the corner of a rectangle.
        /// </summary>
        private struct Grip
        {
            public string Id;
            public bool IsWall;
            public bool Middle;     // a wall's midpoint: moves the selection
            public bool Start;      // a wall's start (else its end)
            public int Piece;       // a rectangle's piece
            public bool IsSide;     // the middle of a rectangle's side: moves that side
            public RectSide Side;
            public Point2 At;       // where the grip is
        }

        /// <summary>Walls with grips at most (a window over a large plan selects hundreds).</summary>
        private const int MaxGripWalls = 200;

        /// <summary>The command under way (the first point), so the host can show it; null when waiting for a first point.</summary>
        public Point2? CommandPoint => first;

        /// <summary>The cursor in ft, snapped.</summary>
        public Point2 CursorPoint => cursor;

        /// <summary>Ends the command under way (Esc); the tool stays.</summary>
        /// <summary>Ctrl+C: the copy follows the cursor from the selection's centre (straight across or up) and one click places it.</summary>
        private bool copyOnce;

        /// <summary>
        /// Ctrl+C (or Ctrl+D): copies the selection and puts the copy on the cursor, held straight across or straight up from
        /// the original; a click (or a typed distance) places it and selects it. Esc cancels. Nothing selected: says so.
        /// </summary>
        public void CopyAndPlace()
        {
            if (selection.Count == 0 || Building == null) { Say("Copy: select what to copy first (click it, or drag a window), then Ctrl+C.", true); return; }
            var pts = new List<Point2>();
            foreach (var id in selection)
            {
                var o = Building.Find(id);
                if (o is Wall w) { pts.Add(w.Start); pts.Add(w.End); }
                else if (o is Note n) pts.Add(n.At);
                else foreach (var r in LevelView.PiecesOf(o)) { pts.Add(new Point2(r.X0, r.Y0)); pts.Add(new Point2(r.X1, r.Y1)); }
            }
            if (pts.Count == 0) return;
            SetTool(SketchTool.Copy);
            first = new Point2((pts.Min(p => p.X) + pts.Max(p => p.X)) / 2, (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2);
            copyOnce = true;
            Say($"Copy of {Names(selection.ToList())}: move the cursor across or up and click to place it (or type the distance). Esc cancels.");
            InvalidateVisual();
        }

        /// <summary>Splits the wall of this level nearest <paramref name="at"/> there (one undo step). False with a message when it cannot.</summary>
        public bool SplitWallAt(Point2 at)
        {
            var b = Building;
            var level = ActiveLevel;
            if (b == null || level == null) return false;
            var wall = b.WallsUnder(level).OrderBy(x => Snapper.DistanceToSegment(at, new Segment2(x.Start, x.End))).FirstOrDefault();
            if (wall == null || Snapper.DistanceToSegment(at, new Segment2(wall.Start, wall.End)) > PixelsToFeet(8)) { Say("Click on a wall.", true); return false; }
            var p = BuildingEdits.AlongWall(wall, at);
            string rest = BuildingEdits.SplitWall(b, wall.Id, p);
            if (rest == null) { Say("That is the end of the wall: split it between its ends.", true); return false; }
            Commit($"Split {wall.Id}: {wall.Length:0.##} ft and {rest} {((Wall)b.Find(rest)).Length:0.##} ft.");
            return true;
        }

        /// <summary>Right-click in Select: the host shows a menu for what is under the cursor (already added to the selection).</summary>
        public event EventHandler<SketchMenuEventArgs> ContextMenuRequested;

        public void ResetCommand()
        {
            first = null;
            copyOnce = false;
            bool dragging = grip != null || windowFrom != null;
            grip = null;
            windowFrom = null;
            segments = null;
            if (dragging && panFrom == null && IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
        }

        /// <summary>What to do next, for the status line.</summary>
        public string Prompt()
        {
            string lvl = ActiveLevel?.Name ?? "the level";
            switch (Tool)
            {
                case SketchTool.Wall: return first == null ? $"Wall under {lvl}: click or type the start (x,y). Esc ends." : "Wall: click or type the end (@dx,dy or a length). Walls are square. Right-click ends the run.";
                case SketchTool.Diaphragm: return first == null ? $"Diaphragm on {lvl}: first corner." : "Diaphragm: opposite corner (or @width,depth).";
                case SketchTool.Opening: return first == null ? $"Opening in {lvl}: first corner." : "Opening: opposite corner (or @width,depth).";
                case SketchTool.Cut: return first == null ? $"Cut from the diaphragms of {lvl}: first corner of the area to take away." : "Cut: opposite corner (or @width,depth).";
                case SketchTool.Note: return $"Note on {lvl}: click where it goes.";
                case SketchTool.Beam: return first == null ? $"Beam in the framing of {lvl}: click or type its first end (over the wall or column it bears on)." : "Beam: click or type its other end. It carries what stands on it to its ends.";
                case SketchTool.Column: return $"Column under {lvl}: click where it stands (from the level below up to {lvl}).";
                case SketchTool.Move: return selection.Count == 0 ? "Move: pick what to move." : first == null ? "Move: base point." : "Move: second point.";
                case SketchTool.Copy: return selection.Count == 0 ? "Copy: pick what to copy." : first == null ? "Copy: base point." : "Copy: second point (again for more copies; Esc ends).";
                case SketchTool.Mirror: return selection.Count == 0 ? "Mirror: pick what to mirror." : first == null ? "Mirror: first point of the mirror line." : "Mirror: second point (the line is made square). The originals stay.";
                case SketchTool.Measure: return first == null ? "Measure: first point." : "Measure: second point.";
                case SketchTool.Split: return "Split: click on a wall where it splits in two.";
                case SketchTool.Calibrate: return first == null ? "Plan scale: click the first end of a known dimension on the plan  (Esc cancels)." : "Plan scale: click the other end; then type the real distance.";
                case SketchTool.PlanStraighten: return first == null ? "Straighten the plan: click a point on a wall line that should run E-W or N-S  (Esc cancels)." : "Straighten the plan: a second point on the same line.";
                case SketchTool.PlanMove: return first == null ? "Move the plan: click a base point on the plan  (Esc cancels)." : "Move the plan: click where the base point goes (snaps to the walls).";
                default: return "Select: click, Shift+click to add, drag a window. Drag a wall's middle grip to move the selection, an end grip to change the length. Del deletes.";
            }
        }

        // ------------------------------------------------------------------ points

        /// <summary>Text typed in the command box that the host takes first (true: used), e.g. a distance its own command asked for.</summary>
        public Func<string, bool> InterceptTyped { get; set; }

        /// <summary>A typed point (x,y  @dx,dy  @len&lt;angle  or a length toward the cursor). False with a message when it cannot be used.</summary>
        public bool TypePoint(string text)
        {
            if (InterceptTyped != null && InterceptTyped(text)) return true;
            if (Tool == SketchTool.Select) { Say("Pick a drawing tool first (W wall, D diaphragm...).", true); return false; }
            if (!CoordinateInput.TryParse(text, first, cursor, out var p, out var error))
            {
                Say(error, true);
                return false;
            }
            ApplyPoint(p, null);
            return true;
        }

        private void ApplyPoint(Point2 p, Point2? raw)
        {
            var b = Building;
            var level = ActiveLevel;
            var below = b.Below(level);
            switch (Tool)
            {
                case SketchTool.Wall:
                    if (below == null) { Say("Walls are drawn on a level above the ground: they run from the level below up to it.", true); return; }
                    if (first == null) { first = p; break; }
                    var end = Snapper.Ortho(p, first.Value);
                    if (Snapper.Dist(end, first.Value) < BuildingEdits.MinimumWall) return;
                    var w = b.AddWall(below, level, first.Value.X, first.Value.Y, end.X, end.Y);
                    Commit($"Wall {w.Id} added, {w.Length:0.##} ft.");
                    first = end; // the next wall starts here
                    break;

                case SketchTool.Diaphragm:
                case SketchTool.Opening:
                    bool opening = Tool == SketchTool.Opening;
                    if (level.Elevation == 0) { Say("The ground has no diaphragm: show a level above it.", true); return; }
                    var above = b.Above(level);
                    if (opening && above == null) { Say($"An opening is open from the level below to the level above; {level.Name} is the top level. Add a level above first, or draw the roof smaller.", true); return; }
                    if (first == null) { first = p; break; }
                    var r = new Rect2(first.Value.X, first.Value.Y, p.X, p.Y);
                    if (r.IsEmpty) { Say("The rectangle has no area: pick the opposite corner.", true); return; }
                    first = null;
                    if (opening)
                    {
                        var o = b.AddOpening(below, above, r);
                        Commit($"Opening {o.Id} added, {r.Width:0.##} × {r.Height:0.##} ft (it cuts {level.Name}; widen its levels in its properties for a taller one).");
                    }
                    else
                    {
                        var d = b.AddDiaphragm(level, r);
                        Commit($"Diaphragm {d.Id} added, {r.Width:0.##} × {r.Height:0.##} ft.");
                    }
                    break;

                case SketchTool.Cut:
                    if (level.Elevation == 0) { Say("The ground has no diaphragm: show a level above it.", true); return; }
                    if (first == null) { first = p; break; }
                    var cutRect = new Rect2(first.Value.X, first.Value.Y, p.X, p.Y);
                    if (cutRect.IsEmpty) { Say("The rectangle has no area: pick the opposite corner.", true); return; }
                    first = null;
                    var cut = BuildingEdits.CutDiaphragms(b, level, cutRect);
                    if (!cut.Any) { Say($"Nothing to cut there: the rectangle does not overlap a diaphragm of {level.Name}.", true); return; }
                    string said = $"Cut {cutRect.Width:0.##} × {cutRect.Height:0.##} ft from {string.Join(", ", cut.Changed.Concat(cut.Removed))}" +
                                  (cut.Removed.Count > 0 ? $"; {string.Join(", ", cut.Removed)} removed (nothing left)" : "") + ".";
                    foreach (var s in cut.Split)
                        said += $" {s.from} was cut in two: the separate part{(s.parts.Count == 1 ? " is" : "s are")} now {string.Join(", ", s.parts)}.";
                    Commit(said);
                    if (cut.Split.Count > 0 || cut.Removed.Count > 0) Say(said, true); // a split or a removal: said as a warning, so it is seen
                    break;

                case SketchTool.Beam:
                    if (level.Elevation == 0) { Say("A beam is in the framing of a level above the ground (a grade beam is the foundation's).", true); return; }
                    if (first == null) { first = p; break; }
                    var bEnd = Ortho ? Snapper.Ortho(p, first.Value) : p;
                    if (Snapper.Dist(bEnd, first.Value) < BuildingEdits.MinimumWall) return;
                    var bm = b.AddBeam(level, first.Value.X, first.Value.Y, bEnd.X, bEnd.Y);
                    first = null;
                    Commit($"Beam {bm.Id} added on {level.Name}, {bm.Length:0.##} ft.");
                    break;

                case SketchTool.Column:
                    if (below == null) { Say("Columns are drawn on a level above the ground: they stand on the level below.", true); return; }
                    var colBase = BuildingEdits.ColumnBase(b, level, p);
                    var col = b.AddColumn(colBase, level, p.X, p.Y);
                    string standsOn = colBase.Elevation == 0 ? "the foundation" : colBase.Id == below.Id ? $"{below.Name} (on what is under it there)" : colBase.Name;
                    Commit($"Column {col.Id} added under {level.Name} at ({p.X:0.##}, {p.Y:0.##}), down to {standsOn}.");
                    break;

                case SketchTool.Note:
                    string text = (PromptText ?? TextPrompt.Ask)("Note", "");
                    if (string.IsNullOrWhiteSpace(text)) return;
                    var n = b.AddNote(level, p.X, p.Y, text.Trim());
                    Commit($"Note {n.Id} added.");
                    break;

                case SketchTool.Move:
                case SketchTool.Copy:
                case SketchTool.Mirror:
                    if (selection.Count == 0)
                    {
                        string id = raw == null ? null : Snapper.Pick(b, level, raw.Value, PixelsToFeet(6));
                        if (id == null) { Say("Nothing there: click on a wall, diaphragm, opening or note.", true); return; }
                        Select(new[] { id });
                        break;
                    }
                    if (first == null) { first = p; break; }
                    var to = Ortho || copyOnce || Tool == SketchTool.Mirror ? Snapper.Ortho(p, first.Value) : p;
                    double dx = to.X - first.Value.X, dy = to.Y - first.Value.Y;
                    var ids = selection.ToList();
                    if (Tool == SketchTool.Move)
                    {
                        foreach (var id in ids) BuildingEdits.Move(b, id, dx, dy);
                        first = null;
                        Commit($"Moved {Names(ids)} by ({dx:0.##}, {dy:0.##}) ft.");
                    }
                    else if (Tool == SketchTool.Copy)
                    {
                        var copies = ids.Select(id => BuildingEdits.Copy(b, id, dx, dy)).Where(c => c != null).ToList();
                        if (copyOnce)
                        {
                            // Ctrl+C: one copy, placed; it is then the selection, ready to be moved or copied again
                            SetTool(SketchTool.Select);
                            Select(copies);
                            Commit($"Copied {Names(ids)} {Offset(dx, dy)}: {Names(copies)} (selected).");
                            break;
                        }
                        Commit($"Copied {Names(ids)} to {Names(copies)}. Click again for another copy; Esc ends.");
                    }
                    else
                    {
                        if (Snapper.Dist(to, first.Value) < 1e-6) return;
                        bool vertical = Math.Abs(dx) < Math.Abs(dy); // a vertical mirror line: x = first.X
                        var m = BuildingEdits.Mirror(b, ids, vertical, vertical ? first.Value.X : first.Value.Y, keepOriginal: true);
                        first = null;
                        Select(m);
                        Commit($"Mirrored {Names(ids)} about {(vertical ? "x" : "y")} = {(vertical ? to.X : to.Y):0.##} ft: {Names(m)}.");
                    }
                    break;

                case SketchTool.Measure:
                    if (first == null) { first = p; lastMeasure = null; break; }
                    var q = Ortho ? Snapper.Ortho(p, first.Value) : p;
                    lastMeasure = (first.Value, q);
                    Say($"Distance {Snapper.Dist(first.Value, q):0.00} ft   (Δx {q.X - first.Value.X:0.00}, Δy {q.Y - first.Value.Y:0.00})");
                    first = null;
                    break;

                case SketchTool.Split:
                    var wall = b.WallsUnder(level).OrderBy(x => Snapper.DistanceToSegment(raw ?? p, new Segment2(x.Start, x.End))).FirstOrDefault();
                    if (wall == null || Snapper.DistanceToSegment(raw ?? p, new Segment2(wall.Start, wall.End)) > PixelsToFeet(8)) { Say("Click on a wall.", true); return; }
                    string rest = BuildingEdits.SplitWall(b, wall.Id, p);
                    if (rest == null) { Say("That is the end of the wall: click between its ends.", true); return; }
                    Commit($"Split {wall.Id}: {wall.Length:0.##} ft and {rest} {((Wall)b.Find(rest)).Length:0.##} ft.");
                    break;

                case SketchTool.Calibrate:
                case SketchTool.PlanStraighten:
                case SketchTool.PlanMove:
                    var plan = PlanOf(level);
                    if (plan == null) { Say("There is no plan under this level.", true); SetTool(SketchTool.Select); return; }
                    if (plan.Locked) { Say("The plan is locked: Underlay ▾ > Unlock first.", true); SetTool(SketchTool.Select); return; }
                    if (first == null) { first = p; break; }
                    var a = first.Value;
                    if (Snapper.Dist(a, p) < 1e-9) { Say("Pick a different point.", true); return; }
                    first = null;
                    Point pa = new Point(a.X, a.Y), pb = new Point(p.X, p.Y);
                    if (Tool == SketchTool.Calibrate)
                    {
                        string len = (PromptText ?? TextPrompt.Ask)($"Real distance between the two points (now {Snapper.Dist(a, p):0.##} ft), e.g. 24'-6\" or 24.5", "");
                        if (len == null) return;
                        if (!CoordinateInput.TryLength(len, out double feet) || !(feet > 0)) { Say($"\"{len}\" is not a length: type feet, or feet and inches (24.5, 24'6\" or 24'-6\").", true); return; }
                        EditPlan(level, $"Plan scale set: the picked {Snapper.Dist(a, p):0.##} ft is now {feet:0.##} ft.", x => x.Calibrate(pa, pb, feet));
                    }
                    else if (Tool == SketchTool.PlanStraighten)
                    {
                        double turn = 0;
                        EditPlan(level, null, x => turn = x.Straighten(pa, pb));
                        Say(Math.Abs(turn) < 0.005 ? "The line already runs square: the plan was not turned." : $"Plan turned {turn:+0.##;-0.##}°.");
                    }
                    else EditPlan(level, $"Plan moved {p.X - a.X:0.##} ft, {p.Y - a.Y:0.##} ft.", x => x.Move(pa, pb));
                    SetTool(SketchTool.Select);
                    return;
            }
            segments = null;
            Say(Prompt());
            InvalidateVisual();
        }

        private static string Names(List<string> ids) => ids.Count <= 3 ? string.Join(", ", ids) : $"{ids.Count} objects";

        // ------------------------------------------------------------------ mouse

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var pos = e.GetPosition(this);
            UpdateCursor(pos);
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (e.ClickCount == 2) { ZoomExtents(); return; }
                panFrom = pos;
                CaptureMouse();
                Cursor = Cursors.SizeAll;
                return;
            }
            if (e.ChangedButton == MouseButton.Right)
            {
                if (Tool == SketchTool.Select && first == null && grip == null && windowFrom == null && ContextMenuRequested != null)
                {
                    // the menu of what is under the cursor (it joins the selection first, as in most programs)
                    string hit = Snapper.Pick(Building, ActiveLevel, rawCursor, PixelsToFeet(6));
                    if (hit != null && !selection.Contains(hit)) Select(new[] { hit });
                    ContextMenuRequested(this, new SketchMenuEventArgs(rawCursor, hit, pos));
                    e.Handled = true;
                    return;
                }
                if (first != null) ResetCommand();
                else if (Tool != SketchTool.Select) SetTool(SketchTool.Select);
                else ClearSelection();
                Say(Prompt());
                e.Handled = true;
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;
            if (InterceptClick != null && InterceptClick(cursor)) return; // a host command (the designer's plan-image tools)
            if (Tool != SketchTool.Select)
            {
                ApplyPoint(cursor, rawCursor);
                return;
            }
            // select: a grip, an issue marker, an object, or a window
            bool add = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
            var g = add ? null : GripAt(pos); // Shift+click on a selected wall still takes it out of the selection
            if (g != null)
            {
                BeginGrip(g.Value);
                gripDown = pos;
                CaptureMouse();
                UpdateCursor(pos);
                return;
            }
            var issue = IssueAt(pos);
            if (issue != null)
            {
                Select(issue.Ids.Where(id => Building.Find(id) != null && !(Building.Find(id) is Level)));
                Say((issue.Severity == Severity.Error ? "Error: " : "Warning: ") + issue.Message, true);
                return;
            }
            string id = Snapper.Pick(Building, ActiveLevel, rawCursor, PixelsToFeet(6));
            if (id != null)
            {
                if (add && selection.Contains(id)) { selection.Remove(id); SelectionChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual(); }
                else Select(new[] { id }, add);
                if (e.ClickCount == 2) ObjectDoubleClicked?.Invoke(this, id);
                return;
            }
            windowFrom = pos;
            CaptureMouse();
        }

        /// <summary>An object double-clicked with Select (the host opens its properties or design).</summary>
        public event EventHandler<string> ObjectDoubleClicked;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pos = e.GetPosition(this);
            if (panFrom != null)
            {
                if (e.MiddleButton != MouseButtonState.Pressed) { EndPan(); return; }
                OriginX += pos.X - panFrom.Value.X;
                OriginY += pos.Y - panFrom.Value.Y;
                panFrom = pos;
                InvalidateVisual();
                return;
            }
            UpdateCursor(pos);
            if (Tool == SketchTool.Select && grip == null && windowFrom == null)
            {
                Hover(pos);
                Cursor = GripCursor(GripAt(pos));
            }
            if (grip != null) Say(DragText(grip.Value, cursor));
            InvalidateVisual();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.ChangedButton == MouseButton.Middle) { EndPan(); return; }
            if (e.ChangedButton != MouseButton.Left) return;
            var pos = e.GetPosition(this);
            if (grip != null)
            {
                var g = grip.Value;
                grip = null;
                ReleaseMouseCapture();
                if ((pos - gripDown).Length >= 3) ApplyGrip(g, cursor); // a click on a grip is not a drag
                segments = null;
                InvalidateVisual();
                return;
            }
            if (windowFrom != null)
            {
                var from = windowFrom.Value;
                windowFrom = null;
                ReleaseMouseCapture();
                bool add = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
                if ((pos - from).Length < 4) { if (!add) ClearSelection(); return; }
                var a = ToWorld(from);
                var b = ToWorld(pos);
                Select(Snapper.InWindow(Building, ActiveLevel, new Rect2(a.X, a.Y, b.X, b.Y)), add);
                Say($"{selection.Count} selected.");
            }
        }

        private void EndPan()
        {
            if (panFrom == null) return;
            panFrom = null;
            ReleaseMouseCapture();
            Cursor = Cursors.Cross;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            var pos = e.GetPosition(this);
            var w = ToWorld(pos);
            double k = e.Delta > 0 ? 1.2 : 1 / 1.2;
            Zoom = Math.Max(0.05, Math.Min(2000, Zoom * k));
            OriginX = pos.X - w.X * Zoom;
            OriginY = pos.Y + w.Y * Zoom;
            viewSet = true;
            UpdateCursor(pos);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            mouseInside = false;
            InvalidateVisual();
        }

        private bool mouseInside;

        private void UpdateCursor(Point pos)
        {
            mouseInside = true;
            rawCursor = ToWorld(pos);
            // picking on the plan itself (its scale, its line, its base point): snap only to its own CAD lines, never to the
            // walls or the grid, which would pull the pick off the feature on the plan
            bool onPlan = Tool == SketchTool.Calibrate || Tool == SketchTool.PlanStraighten || (Tool == SketchTool.PlanMove && first == null);
            if (onPlan)
            {
                var own = PlanLinesNear(rawCursor, PixelsToFeet(10));
                snap = SnapOn && own.Count > 0 ? Snapper.Find(rawCursor, PixelsToFeet(10), own, SnapModes & ~SnapModes.Grid, first, GridSpacing)
                                               : new SnapResult { Point = rawCursor };
            }
            else if (SnapOn && (Tool != SketchTool.Select || HostWantsSnaps?.Invoke() == true) || grip != null)
            {
                if (segments == null) segments = Snapper.SegmentsOf(Building, ActiveLevel);
                var lines = segments;
                var extra = ExtraSnapLines?.Invoke(rawCursor, PixelsToFeet(10));
                if (extra != null) lines = segments.Concat(extra).ToList();
                var own = PlanLinesNear(rawCursor, PixelsToFeet(10));
                if (own.Count > 0) lines = lines.Concat(own).ToList();
                var modes = SnapModes;
                if (!ShowGrid) modes &= ~SnapModes.Grid;
                if (!SnapOn) modes = SnapModes.None;
                var points = Snapper.PointsOf(Building, ActiveLevel, grip != null ? (ICollection<string>)(grip.Value.Middle ? selection.ToList() : new List<string> { grip.Value.Id }) : null);
                var hostPoints = ExtraSnapPoints?.Invoke();
                if (hostPoints != null) points.AddRange(hostPoints);
                snap = Snapper.Find(rawCursor, PixelsToFeet(10), lines, modes, first ?? HostFrom?.Invoke(), GridSpacing, points);
            }
            else snap = new SnapResult { Point = rawCursor };
            cursor = snap.Point;
            CursorMoved?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A left click the host takes first (true: used, the tool does not see it).</summary>
        public Func<Point2, bool> InterceptClick { get; set; }

        /// <summary>True while a host command wants snapped points in the Select tool.</summary>
        public Func<bool> HostWantsSnaps { get; set; }

        /// <summary>The host command's last point (for the perpendicular snap), or null.</summary>
        public Func<Point2?> HostFrom { get; set; }

        /// <summary>More lines to snap to near the cursor (the designer's CAD plan), given the cursor and the reach in ft.</summary>
        public Func<Point2, double, IEnumerable<Segment2>> ExtraSnapLines { get; set; }

        /// <summary>The host's single points to snap to on the level shown (Shear Walls: the hold-downs, beam ends and column bases).</summary>
        public Func<IEnumerable<SnapPoint>> ExtraSnapPoints { get; set; }

        /// <summary>Esc was pressed in the sketch (the host ends its own command too).</summary>
        public event EventHandler Escaped;

        /// <summary>The cursor moved (the host shows the coordinates).</summary>
        public event EventHandler CursorMoved;

        /// <summary>The status says what is under the cursor: an issue, a note, a wall, a diaphragm.</summary>
        private void Hover(Point pos)
        {
            var issue = IssueAt(pos);
            string key = issue != null ? "issue:" + issue.Message : Snapper.Pick(Building, ActiveLevel, rawCursor, PixelsToFeet(6));
            if (key == hovered) return;
            hovered = key;
            if (issue != null) { Say((issue.Severity == Severity.Error ? "Error: " : "Warning: ") + issue.Message, true); return; }
            if (key != null) Say(Describe(key));
        }

        /// <summary>One line about an object: "W3 · shear wall · 20 ft W-E · Ground → Level 2".</summary>
        public string Describe(string id)
        {
            var b = Building;
            switch (b.Find(id))
            {
                case Wall w:
                    return $"{w.Id} · wall · {w.Length:0.##} ft {(w.Direction == WallDirection.WestEast ? "W-E" : w.Direction == WallDirection.SouthNorth ? "S-N" : "angled")} · " +
                           $"{b.Level(w.BaseLevelId)?.Name} → {b.Level(w.TopLevelId)?.Name}";
                case Diaphragm d:
                    var r = Region.Of(d.Pieces);
                    return $"{d.Id} · diaphragm on {b.Level(d.LevelId)?.Name} · {r.Bounds.Width:0.##} × {r.Bounds.Height:0.##} ft · {r.Area:0} sq ft";
                case Opening o:
                    var ro = Region.Of(o.Pieces);
                    return $"{o.Id} · opening {ro.Bounds.Width:0.##} × {ro.Bounds.Height:0.##} ft · open from {b.Level(o.BottomLevelId)?.Name} to {b.Level(o.TopLevelId)?.Name}";
                case Note n:
                    return $"{n.Id} · note: {n.Text}";
                default:
                    return id;
            }
        }

        // ------------------------------------------------------------------ grips and markers

        /// <summary>The grip nearest the point, within 6 px.</summary>
        private Grip? GripAt(Point pos)
        {
            Grip? best = null;
            double bestD = 6;
            foreach (var g in Grips())
            {
                double d = (ToScreen(g.At) - pos).Length;
                if (d <= bestD) { bestD = d; best = g; }
            }
            return best;
        }

        /// <summary>
        /// The grips of the selection: on every selected wall its two ends and its middle; on a single selected rectangle
        /// (a diaphragm or an opening) its corners and the middle of each side.
        /// </summary>
        private IEnumerable<Grip> Grips()
        {
            if (selection.Count == 0 || Building == null) yield break;
            int walls = 0;
            foreach (var id in selection)
            {
                if (!(Building.Find(id) is Wall w)) continue;
                if (++walls > MaxGripWalls) break;
                yield return new Grip { Id = w.Id, IsWall = true, Start = true, At = w.Start };
                yield return new Grip { Id = w.Id, IsWall = true, Start = false, At = w.End };
                yield return new Grip { Id = w.Id, IsWall = true, Middle = true, At = new Point2((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2) };
            }
            if (selection.Count != 1) yield break;
            var o = Building.Find(SelectedId);
            if (!(o is Wall))
            {
                int i = 0;
                foreach (var r in LevelView.PiecesOf(o))
                {
                    foreach (var c in new[] { new Point2(r.X0, r.Y0), new Point2(r.X1, r.Y0), new Point2(r.X1, r.Y1), new Point2(r.X0, r.Y1) })
                        yield return new Grip { Id = o.Id, Piece = i, At = c };
                    double mx = (r.X0 + r.X1) / 2, my = (r.Y0 + r.Y1) / 2;
                    yield return new Grip { Id = o.Id, Piece = i, IsSide = true, Side = RectSide.West, At = new Point2(r.X0, my) };
                    yield return new Grip { Id = o.Id, Piece = i, IsSide = true, Side = RectSide.East, At = new Point2(r.X1, my) };
                    yield return new Grip { Id = o.Id, Piece = i, IsSide = true, Side = RectSide.South, At = new Point2(mx, r.Y0) };
                    yield return new Grip { Id = o.Id, Piece = i, IsSide = true, Side = RectSide.North, At = new Point2(mx, r.Y1) };
                    i++;
                }
            }
        }

        /// <summary>A grip drag starts: the snaps leave out what is being dragged (it would only snap to itself).</summary>
        private void BeginGrip(Grip g)
        {
            grip = g;
            segments = Snapper.SegmentsOf(Building, ActiveLevel, g.Middle ? (ICollection<string>)selection.ToList() : new[] { g.Id });
        }

        /// <summary>What the drag would do: "Move 3 walls 4.5 ft east", "W3: 18 ft".</summary>
        private string DragText(Grip g, Point2 to)
        {
            if (g.Middle)
            {
                var (dx, dy) = BuildingEdits.OrthoOffset(g.At, to);
                return $"Move {Names(selection.ToList())} {Offset(dx, dy)}. Release to place; Esc cancels.";
            }
            if (g.IsWall && Building.Find(g.Id) is Wall w)
            {
                var fixedEnd = g.Start ? w.End : w.Start;
                return $"{w.Id}: {Snapper.Dist(fixedEnd, BuildingEdits.AlongWall(w, to)):0.##} ft (was {w.Length:0.##} ft). Release to set; Esc cancels.";
            }
            if (g.IsSide)
            {
                var r = LevelView.PiecesOf(Building.Find(g.Id)).ElementAtOrDefault(g.Piece);
                bool ns = g.Side == RectSide.West || g.Side == RectSide.East;
                double moved = ns ? to.X - g.At.X : to.Y - g.At.Y;
                double size = ns ? r.X1 - r.X0 + (g.Side == RectSide.East ? moved : -moved) : r.Y1 - r.Y0 + (g.Side == RectSide.North ? moved : -moved);
                return $"{g.Id}: {g.Side.ToString().ToLowerInvariant()} side {Math.Abs(moved):0.##} ft {(moved >= 0 ? (ns ? "east" : "north") : (ns ? "west" : "south"))}, " +
                       $"{(ns ? "width" : "depth")} {Math.Abs(size):0.##} ft. Release to set; Esc cancels.";
            }
            return $"Stretch {g.Id}. Release to set; Esc cancels.";
        }

        /// <summary>"4.5 ft east", "2 ft south".</summary>
        private static string Offset(double dx, double dy) =>
            Math.Abs(dx) >= Math.Abs(dy) ? $"{Math.Abs(dx):0.##} ft {(dx >= 0 ? "east" : "west")}" : $"{Math.Abs(dy):0.##} ft {(dy >= 0 ? "north" : "south")}";

        private static Cursor GripCursor(Grip? g) =>
            g == null ? Cursors.Cross : g.Value.Middle ? Cursors.SizeAll
            : g.Value.IsSide ? (g.Value.Side == RectSide.West || g.Value.Side == RectSide.East ? Cursors.SizeWE : Cursors.SizeNS)
            : Cursors.Hand;

        /// <summary>Applies a grip dragged to a point (one undo step). False with a message when it cannot be done.</summary>
        private bool ApplyGrip(Grip g, Point2 to)
        {
            var b = Building;
            if (g.Middle)
            {
                var (dx, dy) = BuildingEdits.OrthoOffset(g.At, to);
                if (Math.Abs(dx) + Math.Abs(dy) < 1e-9) return false;
                var ids = selection.ToList();
                BuildingEdits.MoveAll(b, ids, dx, dy);
                Commit($"Moved {Names(ids)} {Offset(dx, dy)}.");
                return true;
            }
            if (g.IsWall)
            {
                if (!BuildingEdits.MoveWallEnd(b, g.Id, g.Start, to)) { Say("That would leave the wall with no length.", true); return false; }
                Commit($"{g.Id} is now {((Wall)b.Find(g.Id)).Length:0.##} ft long.");
                return true;
            }
            if (g.IsSide)
            {
                double at = g.Side == RectSide.West || g.Side == RectSide.East ? to.X : to.Y;
                if (!BuildingEdits.MoveSide(b, g.Id, g.Piece, g.Side, at)) { Say("That would leave it with no area.", true); return false; }
                var r = LevelView.PiecesOf(b.Find(g.Id)).ElementAtOrDefault(g.Piece);
                Commit($"{g.Id} is now {r.X1 - r.X0:0.##} × {r.Y1 - r.Y0:0.##} ft.");
                return true;
            }
            if (!BuildingEdits.MoveCorner(b, g.Id, g.Piece, g.At, to)) { Say("That would leave it with no area.", true); return false; }
            Commit($"Stretched {g.Id}.");
            return true;
        }

        /// <summary>
        /// Drags the selection's grip at <paramref name="from"/> (ft) to <paramref name="to"/>, as the mouse does: a wall's
        /// middle moves everything selected (straight across or up), an end changes that wall's length, a corner stretches
        /// a rectangle. False when there is no grip there or the edit cannot be done.
        /// </summary>
        public bool DragGrip(Point2 from, Point2 to)
        {
            var g = GripAt(ToScreen(from));
            if (g == null) return false;
            bool ok = ApplyGrip(g.Value, to);
            segments = null;
            InvalidateVisual();
            return ok;
        }

        /// <summary>The issues shown on this level's plan.</summary>
        public IEnumerable<ModelIssue> IssuesHere() =>
            ShowIssues ? issues.Where(i => i.At != null && (i.LevelId == null || i.LevelId == ActiveLevel?.Id)) : Enumerable.Empty<ModelIssue>();

        private ModelIssue IssueAt(Point pos) =>
            IssuesHere().FirstOrDefault(i => (ToScreen(i.At.Value) - pos).Length <= 9);

        // ------------------------------------------------------------------ keys

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled) e.Handled = HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        }

        /// <summary>The sketch's keys (the host can pass on keys from the window). True when used.</summary>
        public bool HandleKey(Key key, ModifierKeys mods)
        {
            bool ctrl = (mods & ModifierKeys.Control) != 0;
            if (ctrl)
            {
                switch (key)
                {
                    case Key.Z: if ((mods & ModifierKeys.Shift) != 0) Redo(); else Undo(); return true;
                    case Key.Y: Redo(); return true;
                    case Key.A: Select(Building.VisibleIds(ActiveLevel)); return true;
                    case Key.C: CopyAndPlace(); return true;
                    case Key.D: CopyAndPlace(); return true;
                }
                return false;
            }
            if ((mods & ModifierKeys.Alt) != 0) return false;
            switch (key)
            {
                case Key.Escape:
                    // always back to Select: a command under way is cancelled and its tool put down; in Select, the selection goes
                    Escaped?.Invoke(this, EventArgs.Empty);
                    bool busy = first != null || grip != null || windowFrom != null;
                    if (busy) ResetCommand();
                    if (Tool != SketchTool.Select) SetTool(SketchTool.Select);
                    else if (!busy) ClearSelection();
                    Say(Prompt());
                    return true;
                case Key.Delete: DeleteSelected(); return true;
                case Key.F3: ToggleSnap(); return true;
                case Key.F8: ToggleOrtho(); return true;
                case Key.W: SetTool(SketchTool.Wall); return true;
                case Key.D: SetTool(SketchTool.Diaphragm); return true;
                case Key.O: SetTool(SketchTool.Opening); return true;
                case Key.K: SetTool(SketchTool.Cut); return true;
                case Key.N: SetTool(SketchTool.Note); return true;
                case Key.B: SetTool(SketchTool.Beam); return true;
                case Key.P: SetTool(SketchTool.Column); return true;
                case Key.M: SetTool(SketchTool.Move); return true;
                case Key.C: SetTool(SketchTool.Copy); return true;
                case Key.I: SetTool(SketchTool.Mirror); return true;
                case Key.T: SetTool(SketchTool.Measure); return true;
                case Key.X: SetTool(SketchTool.Split); return true;
                case Key.J: JoinSelected(); return true;
                case Key.Z: ZoomExtents(); return true;
                case Key.Space:
                case Key.Enter:
                    if (Tool != SketchTool.Select || first != null) { ResetCommand(); Say(Prompt()); return true; }
                    SetTool(lastTool); // repeat the last command
                    return true;
            }
            return false;
        }

        /// <summary>Typing a number or @ starts a typed point: the host's command box takes it from here.</summary>
        public event EventHandler<string> TypingStarted;

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            base.OnTextInput(e);
            if (string.IsNullOrEmpty(e.Text) || Tool == SketchTool.Select) return;
            char c = e.Text[0];
            if (char.IsDigit(c) || c == '@' || c == '-' || c == '.' || c == '\'')
            {
                TypingStarted?.Invoke(this, e.Text);
                e.Handled = true;
            }
        }

        internal static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
