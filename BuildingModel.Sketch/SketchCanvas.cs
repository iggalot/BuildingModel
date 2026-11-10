using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>The drawing tools.</summary>
    public enum SketchTool { Select, Wall, Diaphragm, Opening, Note, Move, Copy, Measure, Split, Mirror, Calibrate, Cut, PlanStraighten, PlanMove, Beam, Column }

    /// <summary>What a host draws under or over the model, with the sketch's transform.</summary>
    public sealed class SketchDrawEventArgs : EventArgs
    {
        public DrawingContext Context { get; }
        public SketchCanvas Sketch { get; }
        public SketchDrawEventArgs(DrawingContext ctx, SketchCanvas s) { Context = ctx; Sketch = s; }
    }

    /// <summary>A right-click in Select: where (ft and on screen) and the object under the cursor (null: none).</summary>
    public sealed class SketchMenuEventArgs : EventArgs
    {
        public Geometry.Point2 At { get; }
        public string HitId { get; }
        public Point ScreenPoint { get; }
        public SketchMenuEventArgs(Geometry.Point2 at, string hitId, Point screen) { At = at; HitId = hitId; ScreenPoint = screen; }
    }

    /// <summary>A message for the status line.</summary>
    public sealed class SketchStatusEventArgs : EventArgs
    {
        public string Text { get; }
        public bool IsWarning { get; }
        public SketchStatusEventArgs(string text, bool warning) { Text = text; IsWarning = warning; }
    }

    /// <summary>
    /// The shared plan sketcher: draws one level of a <see cref="Building"/> (the story under it: its diaphragms,
    /// openings, walls and notes) and edits it with the drafting tools, snaps, ortho, typed coordinates, grips and
    /// undo. The building is the source of truth: every edit goes through <see cref="Commit"/> (an undo step and
    /// <see cref="ModelChanged"/>). Hosts add their own drawing with <see cref="DrawUnder"/> / <see cref="DrawOver"/>
    /// and show their checks with <see cref="Issues"/>.
    /// </summary>
    public partial class SketchCanvas : FrameworkElement
    {
        public SketchCanvas()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
            Cursor = Cursors.Cross;
            SetBuilding(Building.New(), null);
            SizeChanged += (s, e) =>
            {
                if (!viewSet && ActualWidth > 0) ZoomExtents();
                else if (e.PreviousSize.Height > 0) { OriginY += e.NewSize.Height - e.PreviousSize.Height; InvalidateVisual(); }
            };
        }

        // ------------------------------------------------------------------ the model

        public Building Building { get; private set; }
        public ModelHistory History { get; } = new ModelHistory();

        /// <summary>The level being drawn (the plan of the story under it).</summary>
        public Level ActiveLevel { get; private set; }

        /// <summary>After every edit, undo and redo (the model has changed; the host updates what it shows).</summary>
        public event EventHandler ModelChanged;
        public event EventHandler SelectionChanged;
        public event EventHandler ActiveLevelChanged;
        public event EventHandler ToolChanged;
        public event EventHandler<SketchStatusEventArgs> Status;
        public event EventHandler<SketchDrawEventArgs> DrawUnder;
        public event EventHandler<SketchDrawEventArgs> DrawOver;

        /// <summary>Asks the host for a text (a note); null cancels. The default asks with a small dialog.</summary>
        public Func<string, string, string> PromptText { get; set; }

        /// <summary>A new or opened model: the history starts afresh. The active level is kept by ID when it is still there.</summary>
        public void SetBuilding(Building b, string activeLevelId)
        {
            Building = b ?? Building.New();
            modelVersion++;
            if (Building.Ground == null) Building.Levels.Insert(0, new Level { Id = Building.NewId("L"), Name = "Ground", Elevation = 0 });
            var converted = PlanImage.Migrate(Building); // plans in older formats; part of opening, not an undo step
            History.Reset(Building);
            selection.Clear();
            ResetCommand();
            ActiveLevel = Building.Level(activeLevelId) ?? Building.LevelsBottomUp.FirstOrDefault(l => l.Elevation > 0) ?? Building.Ground;
            plans.Clear();
            plansWarned.Clear();
            if (converted.Count > 0) Say($"Plans converted from an older format: {converted.Count} level{(converted.Count == 1 ? "" : "s")}. Save to keep it.");
            ActiveLevelChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public void ShowLevel(string levelId)
        {
            var l = Building.Level(levelId);
            if (l == null || l == ActiveLevel) return;
            ResetCommand();
            ActiveLevel = l;
            var visible = Building.VisibleIds(l);
            selection.RemoveWhere(id => !visible.Contains(id));
            ActiveLevelChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        /// <summary>
        /// Records an edit made to <see cref="Building"/> (by a tool or by the host): an undo step when something changed,
        /// then <see cref="ModelChanged"/>. Returns true when something changed.
        /// </summary>
        /// <summary>An edit's message as the name of its undo step: without the "Ctrl+Z undoes it" hints.</summary>
        private static string StepName(string what) =>
            string.IsNullOrWhiteSpace(what) ? null : System.Text.RegularExpressions.Regex.Replace(what, @"\s*\(?Ctrl\+Z[^.)]*[.)]*", "").Trim();

        public bool Commit(string what)
        {
            // a host that only shows the building (the Shear Wall Calculator): its geometry is edited in the Building Model
            if (GeometryLocked && History.GeometryChanged(Building))
            {
                History.Revert(Building);
                DropMissingSelection();
                Say(GeometryLockedMessage);
                BuildingModel.Diagnostics.Log.Info("Edit", "Refused (the geometry is edited in the Building Model): " + (what ?? ""));
                GeometryEditRefused?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                return false;
            }
            // columns follow what carries them (a beam drawn across one trims it; its support deleted runs it down again)
            var followed = BuildingEdits.FollowSupports(Building);
            if (followed.Count > 0) what = (string.IsNullOrEmpty(what) ? "" : what + " ") + string.Join(" ", followed) + " (Ctrl+Z undoes it.)";
            bool changed = History.Record(Building, StepName(what));
            if (changed) modelVersion++; // what the views worked out from the model (LateralOutOfDate) is found again
            if (ActiveLevel == null || Building.Level(ActiveLevel.Id) == null)
            {
                ActiveLevel = Building.Level(ActiveLevel?.Id) ?? Building.LevelsBottomUp.LastOrDefault();
                ActiveLevelChanged?.Invoke(this, EventArgs.Empty);
            }
            else ActiveLevel = Building.Level(ActiveLevel.Id);
            DropMissingSelection();
            if (changed)
            {
                if (!string.IsNullOrEmpty(what)) Say(what);
                BuildingModel.Diagnostics.Log.Info("Edit", string.IsNullOrEmpty(what) ? "Model changed." : what);
                ModelChanged?.Invoke(this, EventArgs.Empty);
            }
            InvalidateVisual();
            return changed;
        }

        /// <summary>
        /// The geometry is shown, not edited (a host such as the Shear Wall Calculator, where Edit building opens the Building
        /// Model): an edit that would move, add or delete anything is put back with <see cref="GeometryLockedMessage"/>.
        /// The apps' data on the objects still changes (a wall's design).
        /// </summary>
        public bool GeometryLocked { get; set; }

        public string GeometryLockedMessage { get; set; } = "The building is edited in the Building Model (Edit building).";

        /// <summary>Raised when an edit is put back because <see cref="GeometryLocked"/>.</summary>
        public event EventHandler GeometryEditRefused;

        /// <summary>Runs an edit on the building and commits it.</summary>
        public bool Edit(string what, Action<Building> edit)
        {
            edit(Building);
            return Commit(what);
        }

        public void Undo() => UndoRedo(true);
        public void Redo() => UndoRedo(false);

        private void UndoRedo(bool undo)
        {
            ResetCommand();
            string what = undo ? History.Undo(Building) : History.Redo(Building);
            modelVersion++;
            if (what == null) { Say(undo ? "Nothing to undo." : "Nothing to redo."); return; }
            var keep = ActiveLevel?.Id;
            ActiveLevel = Building.Level(keep) ?? Building.LevelsBottomUp.LastOrDefault();
            if (ActiveLevel?.Id != keep) ActiveLevelChanged?.Invoke(this, EventArgs.Empty);
            DropMissingSelection();
            Say((undo ? "Undone: " : "Redone: ") + what + $"   ({History.UndoSteps} to undo, {History.RedoSteps} to redo)");
            BuildingModel.Diagnostics.Log.Info("Edit", (undo ? "Undone: " : "Redone: ") + what);
            ModelChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        // ------------------------------------------------------------------ selection

        private readonly HashSet<string> selection = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Selection => selection;

        /// <summary>The one selected object, or null (none or several).</summary>
        public string SelectedId => selection.Count == 1 ? selection.First() : null;

        public void Select(IEnumerable<string> ids, bool add = false)
        {
            if (!add) selection.Clear();
            foreach (var id in ids ?? Enumerable.Empty<string>()) if (Selectable(id)) selection.Add(id);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public void ClearSelection() => Select(null);

        // ------------------------------------------------------------------ the host's own objects in the selection

        /// <summary>
        /// The host's own objects (a load zone, a foundation point...) take part in the selection like the model's: a click,
        /// Shift+click or Ctrl+click, a window, Ctrl+A, Delete. Their IDs must not be model IDs (e.g. "zone:2"). True for one the
        /// host has now; the host draws their highlight. Null: none.
        /// </summary>
        public Func<string, bool> HostOwns { get; set; }

        /// <summary>The host's object at a point (ft; the pick tolerance in ft), when no model object is there; null: none.</summary>
        public Func<Point2, double, string> HostPick { get; set; }

        /// <summary>The host's objects wholly inside a box (ft): a window selection, and Ctrl+A (the whole plan).</summary>
        public Func<Rect2, IEnumerable<string>> HostInWindow { get; set; }

        /// <summary>The model objects the host lets be picked (null: all), e.g. only the walls.</summary>
        public Func<ModelObject, bool> PickFilter { get; set; }

        /// <summary>Delete with host objects selected: the host deletes these (the model's selected go as usual).</summary>
        public event EventHandler<IReadOnlyList<string>> HostDeleteRequested;

        /// <summary>A click picks the host's object before the model's (e.g. a foundation point standing on a wall).</summary>
        public bool HostPickFirst { get; set; }

        /// <summary>The selected model objects of one kind (e.g. the walls), in no set order.</summary>
        public List<T> SelectedOf<T>() where T : ModelObject => selection.Select(id => Building?.Find(id)).OfType<T>().ToList();

        /// <summary>The selected objects of the host (<see cref="HostOwns"/>).</summary>
        public List<string> SelectedHostIds => selection.Where(IsHostObject).ToList();

        public bool IsHostObject(string id) => id != null && Building?.Find(id) == null && HostOwns?.Invoke(id) == true;

        private bool Pickable(string id)
        {
            var o = id == null ? null : Building?.Find(id);
            return o != null && (PickFilter == null || PickFilter(o));
        }

        private bool Selectable(string id) => Pickable(id) || IsHostObject(id);

        /// <summary>What a window (ft) takes: the model's objects wholly inside it that may be picked, and the host's.</summary>
        public List<string> InWindow(Rect2 box)
        {
            var ids = Snapper.InWindow(Building, ActiveLevel, box).Where(Pickable).ToList();
            if (HostInWindow != null) ids.AddRange(HostInWindow(box) ?? Enumerable.Empty<string>());
            return ids;
        }

        /// <summary>
        /// An issue picked in a list: shows its level, selects what it is about (every member it names) and brings its place
        /// into view.
        /// </summary>
        public void ShowIssue(BuildingModel.Checks.ModelIssue issue) => ShowIssue(issue, true);

        private void ShowIssue(BuildingModel.Checks.ModelIssue issue, bool retry)
        {
            if (issue == null) return;
            if (issue.LevelId != null && Building.Level(issue.LevelId) != null && issue.LevelId != ActiveLevel?.Id) ShowLevel(issue.LevelId);
            Select(issue.Ids.Where(id => !(Building.Find(id) is Level)));
            Focus();
            // the canvas was just brought on screen (another workspace, a closed panel): zoom once it has its size
            if ((ActualWidth <= 0 || ActualHeight <= 0) && retry)
            {
                Dispatcher.BeginInvoke(new Action(() => ShowIssue(issue, false)), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            var box = IssueBounds(issue);
            if (!box.IsEmpty) ZoomTo(box);
        }

        /// <summary>Smallest span (ft) an issue is zoomed to: a column or a short wall is seen with what is around it.</summary>
        public const double IssueMinSpan = 25;

        /// <summary>
        /// The plan area (ft) to show for an issue: what it names (wall and beam ends, columns, diaphragm and opening pieces),
        /// else its place, at least <see cref="IssueMinSpan"/> across and a little more than the members.
        /// </summary>
        public Rect IssueBounds(BuildingModel.Checks.ModelIssue issue)
        {
            var box = Rect.Empty;
            void Add(Point2 p) => box.Union(new Point(p.X, p.Y));
            foreach (var id in issue.Ids)
            {
                switch (Building.Find(id))
                {
                    case Wall w: Add(w.Start); Add(w.End); break;
                    case Beam bm: Add(bm.Start); Add(bm.End); break;
                    case Column c: Add(c.At); break;
                    case Level _: break;
                    case ModelObject o:
                        var pieces = LevelView.PiecesOf(o).ToList();
                        foreach (var r in pieces) { Add(new Point2(r.X0, r.Y0)); Add(new Point2(r.X1, r.Y1)); }
                        if (pieces.Count == 0) { var a = BuildingEdits.Anchor(Building, id); if (a != null) Add(a.Value); }
                        break;
                }
            }
            if (box.IsEmpty && issue.At != null) Add(issue.At.Value);
            if (box.IsEmpty) return box;
            double cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
            double spanX = Math.Max(box.Width * 1.3, IssueMinSpan), spanY = Math.Max(box.Height * 1.3, IssueMinSpan);
            return new Rect(cx - spanX / 2, cy - spanY / 2, spanX, spanY);
        }

        /// <summary>Brings a point into the middle of the view when it is off screen (or near the edge).</summary>
        public void CenterOn(Point2 p)
        {
            var s = ToScreen(p);
            double m = 40;
            if (s.X >= m && s.Y >= m && s.X <= ActualWidth - m && s.Y <= ActualHeight - m) { InvalidateVisual(); return; }
            OriginX = ActualWidth / 2 - p.X * Zoom;
            OriginY = ActualHeight / 2 + p.Y * Zoom;
            viewSet = true;
            InvalidateVisual();
        }

        /// <summary>Selects an object from a table: shows its level and brings it into view.</summary>
        public void SelectAndShow(string id)
        {
            var o = Building.Find(id);
            if (o == null) return;
            string level = o is Diaphragm d ? d.LevelId : o is Wall w ? w.TopLevelId : o is Note n ? n.LevelId
                         : o is Beam bm ? bm.LevelId : o is Column col ? col.TopLevelId
                         : o is Opening op ? Building.Levels.Where(l => Building.OpeningsCutting(l).Contains(op)).OrderBy(l => l.Elevation).Select(l => l.Id).FirstOrDefault()
                         : null;
            if (level != null && level != ActiveLevel?.Id) ShowLevel(level);
            Select(new[] { id });
            var a = BuildingEdits.Anchor(Building, id);
            if (a != null)
            {
                var s = ToScreen(a.Value);
                if (s.X < 0 || s.Y < 0 || s.X > ActualWidth || s.Y > ActualHeight)
                {
                    OriginX = ActualWidth / 2 - a.Value.X * Zoom;
                    OriginY = ActualHeight / 2 + a.Value.Y * Zoom;
                    InvalidateVisual();
                }
            }
        }

        private void DropMissingSelection()
        {
            int n = selection.Count;
            var visible = ActiveLevel == null ? new HashSet<string>() : Building.VisibleIds(ActiveLevel);
            selection.RemoveWhere(id => !(IsHostObject(id) || (Building.Find(id) != null && visible.Contains(id))));
            if (selection.Count != n) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void DeleteSelected()
        {
            if (selection.Count == 0) { Say("Select something first, then Delete.", true); return; }
            var ids = selection.ToList();
            var host = ids.Where(IsHostObject).ToList();
            if (host.Count > 0)
            {
                // the host's own objects: the host deletes them (one step of its own); the model's go as below
                foreach (var h in host) selection.Remove(h);
                ids = ids.Except(host).ToList();
                HostDeleteRequested?.Invoke(this, host);
                if (ids.Count == 0) { SelectionChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual(); return; }
            }
            int n = ids.Count(id => BuildingEdits.Delete(Building, id));
            selection.Clear();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Commit(n == 1 ? $"Deleted {ids[0]}.  Ctrl+Z brings it back." : $"Deleted {n} objects.  Ctrl+Z brings them back.");
        }

        /// <summary>Joins the two selected walls (end to end on one line).</summary>
        public void JoinSelected()
        {
            var walls = selection.Where(id => Building.Find(id) is Wall).ToList();
            if (walls.Count != 2) { Say("Select two walls that meet end to end on one line, then Join.", true); return; }
            if (!BuildingEdits.JoinWalls(Building, walls[0], walls[1])) { Say("These walls are not end to end on one line with the same levels.", true); return; }
            Select(new[] { walls[0] });
            Commit($"Joined {walls[1]} into {walls[0]}.");
        }

        // ------------------------------------------------------------------ settings

        public SketchTool Tool { get; private set; } = SketchTool.Select;
        public bool SnapOn { get; set; } = true;
        public SnapModes SnapModes { get; set; } = SnapModes.Default;
        public bool Ortho { get; set; }
        public double GridSpacing { get; set; } = 1;
        public bool ShowGrid { get; set; } = true;

        /// <summary>The walls' name and length along each wall ("W9  39'"); a host whose walls open a card when clicked may leave them off.</summary>
        public bool ShowWallLabels { get; set; } = true;
        public bool ShowLabels { get; set; } = true;

        /// <summary>A span marker in every framing zone (each floor or roof diaphragm): the way its joists or rafters run, as on a framing plan.</summary>
        public bool ShowSpans { get; set; } = true;

        /// <summary>
        /// The size of the text on the plan (IDs, lengths, notes) against the base sizes; the apps' own overlays on the plan read it
        /// too, so one number sets them all. 1.2: the original was too small to read, 1.5 crowded the plan.
        /// </summary>
        public double LabelScale { get; set; } = 1.2;
        public bool ShowLevelBelow { get; set; } = true;
        public bool ShowIssues { get; set; } = true;
        public bool ShowUnderlay { get; set; } = true;

        /// <summary>The tools the host offers (the others cannot be started).</summary>
        public HashSet<SketchTool> EnabledTools { get; } = new HashSet<SketchTool>((SketchTool[])Enum.GetValues(typeof(SketchTool)));

        /// <summary>The host's checks, drawn as markers on the plan of their level.</summary>
        public IList<ModelIssue> Issues
        {
            get => issues;
            set { issues = value ?? new List<ModelIssue>(); InvalidateVisual(); }
        }
        private IList<ModelIssue> issues = new List<ModelIssue>();

        public void SetTool(SketchTool tool)
        {
            if (!EnabledTools.Contains(tool)) return;
            ResetCommand();
            Tool = tool;
            if (tool != SketchTool.Select && tool != SketchTool.Measure && !IsPlanTool(tool)) lastTool = tool;
            ToolChanged?.Invoke(this, EventArgs.Empty);
            Say(Prompt());
            InvalidateVisual();
            Focus();
        }

        private SketchTool lastTool = SketchTool.Wall;

        public void ToggleOrtho()
        {
            Ortho = !Ortho;
            Say($"Ortho {(Ortho ? "on" : "off")} (F8): move, copy, mirror and measure go straight across or straight up. Walls are always square.");
            ToolChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public void ToggleSnap()
        {
            SnapOn = !SnapOn;
            Say($"Snaps {(SnapOn ? "on" : "off")} (F3).");
            ToolChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public void Refresh() => InvalidateVisual();

        internal void Say(string text, bool warning = false) => Status?.Invoke(this, new SketchStatusEventArgs(text, warning));

        // ------------------------------------------------------------------ view (screen = (OriginX + x·Zoom, OriginY − y·Zoom))

        public double Zoom { get; private set; } = 10;
        public double OriginX { get; private set; } = 40;
        public double OriginY { get; private set; } = 400;
        private bool viewSet;

        public Point ToScreen(Point2 p) => new Point(OriginX + p.X * Zoom, OriginY - p.Y * Zoom);
        public Point2 ToWorld(Point s) => new Point2((s.X - OriginX) / Zoom, (OriginY - s.Y) / Zoom);
        public double PixelsToFeet(double px) => px / Zoom;

        /// <summary>Fits the level (or the whole building, or the underlay) in view; a 60 × 40 ft area when there is nothing.</summary>
        public void ZoomExtents()
        {
            var r = BuildingEdits.Extents(Building, ActiveLevel) ?? BuildingEdits.Extents(Building);
            var u = PlanOf(ActiveLevel);
            Rect box = r == null ? Rect.Empty : new Rect(r.Value.X0, r.Value.Y0, r.Value.Width, r.Value.Height);
            if (u != null && u.Visible && ShowUnderlay) { var ub = u.WorldBounds(); if (!ub.IsEmpty) box.Union(ub); }
            // the north arrow kept in view
            var n = ShowNorth && !box.IsEmpty ? NorthArrow.For(Building) : null;
            if (n != null) { var e = n.Extent(); box.Union(new Rect(e.X0, e.Y0, e.Width, e.Height)); }
            if (box.IsEmpty) box = new Rect(0, 0, 60, 40);
            ZoomTo(box, FitPadding?.Invoke() ?? new Thickness());
        }

        /// <summary>Draw the building model's north arrow (off the north-west corner, as in every view). On by default.</summary>
        public bool ShowNorth { get; set; } = true;

        /// <summary>
        /// Room (pixels) the host's own marks need around the drawing when it is fitted (Zoom to the drawing): labels are a
        /// fixed size on screen, so a margin in feet cannot hold them at every zoom.
        /// </summary>
        public Func<Thickness> FitPadding { get; set; }

        public void ZoomTo(Rect world) => ZoomTo(world, new Thickness());

        /// <summary>Fits <paramref name="world"/> in view, inside <paramref name="pad"/> pixels on each side.</summary>
        public void ZoomTo(Rect world, Thickness pad)
        {
            double w = ActualWidth - pad.Left - pad.Right, h = ActualHeight - pad.Top - pad.Bottom;
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            if (w < ActualWidth * 0.4) w = ActualWidth * 0.4;    // never squeeze the drawing to nothing for its labels
            if (h < ActualHeight * 0.4) h = ActualHeight * 0.4;
            viewSet = true;
            double m = pad.Left + pad.Right + pad.Top + pad.Bottom > 0 ? 1.05 : 1.2; // the padding is the margin when given
            Zoom = Math.Max(0.05, Math.Min(w / (Math.Max(world.Width, 1) * m), h / (Math.Max(world.Height, 1) * m)));
            double cx = pad.Left + w / 2, cy = pad.Top + h / 2;
            if (w + pad.Left + pad.Right > ActualWidth) cx = ActualWidth / 2;
            if (h + pad.Top + pad.Bottom > ActualHeight) cy = ActualHeight / 2;
            OriginX = cx - (world.X + world.Width / 2) * Zoom;
            OriginY = cy + (world.Y + world.Height / 2) * Zoom;
            InvalidateVisual();
        }

        // ------------------------------------------------------------------ underlays (one per level, in the level's data bag)

        // The plan of each level as last read: its stored record (JSON) and the object, which keeps the loaded file. A changed
        // record (an edit, undo, redo) is read again; the picture or linework is kept when the file is the same.
        private readonly Dictionary<string, (string json, PlanImage plan)> plans = new Dictionary<string, (string, PlanImage)>();
        private readonly HashSet<string> plansWarned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The plan under level <paramref name="l"/> (see <see cref="PlanImage"/>), or null.</summary>
        public PlanImage PlanOf(Level l)
        {
            if (l == null) return null;
            string json = l.GetData(PlanImage.DataKey);
            if (json == null) { plans.Remove(l.Id); return null; }
            if (plans.TryGetValue(l.Id, out var c) && c.json == json) return c.plan;
            var p = PlanImage.FromJson(json);
            if (p != null && c.plan != null) p.AdoptContent(c.plan);
            plans[l.Id] = (json, p);
            if (p != null)
            {
                p.EnsureLoaded();
                if (p.LoadError != null && plansWarned.Add(p.LoadError)) { Say(p.LoadError, true); BuildingModel.Diagnostics.Log.Warning("Plan", p.LoadError, null); }
            }
            return p;
        }

        /// <summary>
        /// Puts plan <paramref name="p"/> under level <paramref name="l"/> (null removes it), as one edit (Ctrl+Z undoes it).
        /// Only the Building Model editor does this: a host that only shows plans sets <see cref="SketchView.PlanEditing"/> off.
        /// </summary>
        public void SetPlan(Level l, PlanImage p, string what)
        {
            if (l == null) return;
            Edit(what ?? (p == null ? $"Plan removed from {l.Name}." : $"Plan placed under {l.Name}."), b => PlanImage.Put(b.Level(l.Id), p));
        }

        /// <summary>Changes the plan of level <paramref name="l"/> (a copy is changed and stored, as one edit).</summary>
        public void EditPlan(Level l, string what, Action<PlanImage> change)
        {
            var p = PlanOf(l)?.Clone();
            if (p == null) return;
            change(p);
            SetPlan(l, p, what);
        }

        /// <summary>The plan's CAD lines near a world point (for snaps), when it is shown.</summary>
        internal List<Segment2> PlanLinesNear(Point2 world, double reach)
        {
            var p = ShowUnderlay ? PlanOf(ActiveLevel) : null;
            return p == null || !p.Visible || !p.IsDxf ? new List<Segment2>() : p.SegmentsNear(world, reach);
        }

        internal static bool IsPlanTool(SketchTool t) => t == SketchTool.Calibrate || t == SketchTool.PlanStraighten || t == SketchTool.PlanMove;
    }
}
