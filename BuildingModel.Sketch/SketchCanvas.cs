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
    public enum SketchTool { Select, Wall, Diaphragm, Opening, Note, Move, Copy, Measure, Split, Mirror, Calibrate, Cut }

    /// <summary>What a host draws under or over the model, with the sketch's transform.</summary>
    public sealed class SketchDrawEventArgs : EventArgs
    {
        public DrawingContext Context { get; }
        public SketchCanvas Sketch { get; }
        public SketchDrawEventArgs(DrawingContext ctx, SketchCanvas s) { Context = ctx; Sketch = s; }
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
            if (Building.Ground == null) Building.Levels.Insert(0, new Level { Id = Building.NewId("L"), Name = "Ground", Elevation = 0 });
            History.Reset(Building);
            selection.Clear();
            ResetCommand();
            ActiveLevel = Building.Level(activeLevelId) ?? Building.LevelsBottomUp.FirstOrDefault(l => l.Elevation > 0) ?? Building.Ground;
            underlays.Clear();
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
        public bool Commit(string what)
        {
            bool changed = History.Record(Building);
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
            foreach (var id in ids ?? Enumerable.Empty<string>()) if (Building.Find(id) != null) selection.Add(id);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public void ClearSelection() => Select(null);

        /// <summary>Selects an object from a table: shows its level and brings it into view.</summary>
        public void SelectAndShow(string id)
        {
            var o = Building.Find(id);
            if (o == null) return;
            string level = o is Diaphragm d ? d.LevelId : o is Wall w ? w.TopLevelId : o is Note n ? n.LevelId
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
            selection.RemoveWhere(id => Building.Find(id) == null || !visible.Contains(id));
            if (selection.Count != n) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void DeleteSelected()
        {
            if (selection.Count == 0) { Say("Select something first, then Delete.", true); return; }
            var ids = selection.ToList();
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
        public bool ShowLabels { get; set; } = true;
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
            if (tool != SketchTool.Select && tool != SketchTool.Measure && tool != SketchTool.Calibrate) lastTool = tool;
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
            var u = UnderlayOf(ActiveLevel);
            Rect box = r == null ? Rect.Empty : new Rect(r.Value.X0, r.Value.Y0, r.Value.Width, r.Value.Height);
            if (u != null && u.Visible && ShowUnderlay) { var ub = u.WorldBounds(); if (!ub.IsEmpty) box.Union(ub); }
            if (box.IsEmpty) box = new Rect(0, 0, 60, 40);
            ZoomTo(box);
        }

        public void ZoomTo(Rect world)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            viewSet = true;
            Zoom = Math.Max(0.05, Math.Min(w / (Math.Max(world.Width, 1) * 1.2), h / (Math.Max(world.Height, 1) * 1.2)));
            OriginX = w / 2 - (world.X + world.Width / 2) * Zoom;
            OriginY = h / 2 + (world.Y + world.Height / 2) * Zoom;
            InvalidateVisual();
        }

        // ------------------------------------------------------------------ underlays (one per level, in the level's data bag)

        private readonly Dictionary<string, PlanUnderlay> underlays = new Dictionary<string, PlanUnderlay>();

        public PlanUnderlay UnderlayOf(Level l)
        {
            if (l == null) return null;
            if (!underlays.TryGetValue(l.Id, out var u))
            {
                u = PlanUnderlay.FromJson(l.GetData(PlanUnderlay.DataKey));
                underlays[l.Id] = u;
                if (u != null && u.WorldBounds().IsEmpty && u.LoadError != null) Say(u.LoadError, true);
            }
            return u;
        }

        /// <summary>Puts a plan under the active level (null removes it); saved in the level's data bag.</summary>
        public void SetUnderlay(PlanUnderlay u)
        {
            if (ActiveLevel == null) return;
            underlays[ActiveLevel.Id] = u;
            ActiveLevel.SetData(PlanUnderlay.DataKey, u?.ToJson());
            Commit(u == null ? "Plan removed." : "Plan placed under " + ActiveLevel.Name + ".");
            if (u != null) ZoomExtents();
        }

        internal void SaveUnderlay()
        {
            var u = UnderlayOf(ActiveLevel);
            if (u != null) ActiveLevel.SetData(PlanUnderlay.DataKey, u.ToJson());
        }
    }
}
