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

        /// <summary>The end of a wall or the corner of a rectangle, dragged to edit it.</summary>
        private struct Grip
        {
            public string Id;
            public bool IsWall;
            public bool Start;      // a wall's start (else its end)
            public int Piece;       // a rectangle's piece
            public Point2 At;       // where the grip is
        }

        /// <summary>The command under way (the first point), so the host can show it; null when waiting for a first point.</summary>
        public Point2? CommandPoint => first;

        /// <summary>The cursor in ft, snapped.</summary>
        public Point2 CursorPoint => cursor;

        /// <summary>Ends the command under way (Esc); the tool stays.</summary>
        public void ResetCommand()
        {
            first = null;
            grip = null;
            windowFrom = null;
            segments = null;
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
                case SketchTool.Note: return $"Note on {lvl}: click where it goes.";
                case SketchTool.Move: return selection.Count == 0 ? "Move: pick what to move." : first == null ? "Move: base point." : "Move: second point.";
                case SketchTool.Copy: return selection.Count == 0 ? "Copy: pick what to copy." : first == null ? "Copy: base point." : "Copy: second point (again for more copies; Esc ends).";
                case SketchTool.Mirror: return selection.Count == 0 ? "Mirror: pick what to mirror." : first == null ? "Mirror: first point of the mirror line." : "Mirror: second point (the line is made square). The originals stay.";
                case SketchTool.Measure: return first == null ? "Measure: first point." : "Measure: second point.";
                case SketchTool.Split: return "Split: click on a wall where it splits in two.";
                case SketchTool.Calibrate: return first == null ? "Calibrate the plan: click the first of two points a known distance apart." : "Calibrate: the second point.";
                default: return "Select: click, Shift+click to add, drag a window. Drag a grip to stretch. Del deletes.";
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
                    var to = Ortho || Tool == SketchTool.Mirror ? Snapper.Ortho(p, first.Value) : p;
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
                    var u = UnderlayOf(level);
                    if (u == null) { Say("There is no plan under this level to calibrate.", true); return; }
                    if (first == null) { first = p; break; }
                    var a = first.Value;
                    first = null;
                    string len = (PromptText ?? TextPrompt.Ask)($"Distance between the two points (now {Snapper.Dist(a, p):0.##} ft)", "");
                    if (len == null) return;
                    if (!CoordinateInput.TryLength(len, out double feet) || !(feet > 0)) { Say($"\"{len}\" is not a length.", true); return; }
                    u.Calibrate(new Point(a.X, a.Y), new Point(p.X, p.Y), feet);
                    SaveUnderlay();
                    Commit($"Plan scaled: those points are now {feet:0.##} ft apart.");
                    SetTool(SketchTool.Select);
                    break;
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
            var g = GripAt(pos);
            if (g != null)
            {
                grip = g;
                CaptureMouse();
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
            bool add = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
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
            if (Tool == SketchTool.Select && grip == null && windowFrom == null) Hover(pos);
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
                bool ok = g.IsWall ? BuildingEdits.MoveWallEnd(Building, g.Id, g.Start, cursor) : BuildingEdits.MoveCorner(Building, g.Id, g.Piece, g.At, cursor);
                if (ok) Commit($"Stretched {g.Id}.");
                else Say("That would leave it with no length or area.", true);
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
            if (SnapOn && (Tool != SketchTool.Select || HostWantsSnaps?.Invoke() == true) || grip != null)
            {
                if (segments == null) segments = Snapper.SegmentsOf(Building, ActiveLevel);
                var lines = segments;
                var extra = ExtraSnapLines?.Invoke(rawCursor, PixelsToFeet(10));
                if (extra != null) lines = segments.Concat(extra).ToList();
                var modes = SnapModes;
                if (!ShowGrid) modes &= ~SnapModes.Grid;
                if (!SnapOn) modes = SnapModes.None;
                snap = Snapper.Find(rawCursor, PixelsToFeet(10), lines, modes, first ?? HostFrom?.Invoke(), GridSpacing);
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
                    return $"{w.Id} · {(w.IsShearWall ? "shear wall" : "wall")} · {w.Length:0.##} ft {(w.Direction == WallDirection.WestEast ? "W-E" : w.Direction == WallDirection.SouthNorth ? "S-N" : "angled")} · " +
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

        private Grip? GripAt(Point pos)
        {
            foreach (var g in Grips())
                if ((ToScreen(g.At) - pos).Length <= 6) return g;
            return null;
        }

        /// <summary>The grips of the one selected wall (its ends) or rectangle (its corners).</summary>
        private IEnumerable<Grip> Grips()
        {
            if (selection.Count != 1) yield break;
            var o = Building.Find(SelectedId);
            if (o is Wall w)
            {
                yield return new Grip { Id = w.Id, IsWall = true, Start = true, At = w.Start };
                yield return new Grip { Id = w.Id, IsWall = true, Start = false, At = w.End };
            }
            else
            {
                int i = 0;
                foreach (var r in LevelView.PiecesOf(o))
                {
                    foreach (var c in new[] { new Point2(r.X0, r.Y0), new Point2(r.X1, r.Y0), new Point2(r.X1, r.Y1), new Point2(r.X0, r.Y1) })
                        yield return new Grip { Id = o.Id, Piece = i, At = c };
                    i++;
                }
            }
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
                }
                return false;
            }
            if ((mods & ModifierKeys.Alt) != 0) return false;
            switch (key)
            {
                case Key.Escape:
                    Escaped?.Invoke(this, EventArgs.Empty);
                    if (first != null || grip != null || windowFrom != null) ResetCommand();
                    else if (Tool != SketchTool.Select) SetTool(SketchTool.Select);
                    else ClearSelection();
                    Say(Prompt());
                    return true;
                case Key.Delete: DeleteSelected(); return true;
                case Key.F3: ToggleSnap(); return true;
                case Key.F8: ToggleOrtho(); return true;
                case Key.W: SetTool(SketchTool.Wall); return true;
                case Key.D: SetTool(SketchTool.Diaphragm); return true;
                case Key.O: SetTool(SketchTool.Opening); return true;
                case Key.N: SetTool(SketchTool.Note); return true;
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
