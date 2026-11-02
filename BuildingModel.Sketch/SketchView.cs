using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuildingModel.Edits;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The sketcher as one piece, laid out by purpose:
    /// <list type="bullet">
    /// <item>LEFT, a narrow strip of icon buttons for the work on the drawing: select, undo/redo, draw, edit, measure (each
    /// named with its key in its tooltip);</item>
    /// <item>BOTTOM, the view: zoom, the drafting aids (snap, ortho, grid), what is shown (issues, the plan underlay), then the
    /// typed-point box, the message and the cursor coordinates;</item>
    /// <item>TOP, the building commands (new from size, roof) and the level buttons, which a host can hide or replace by its menus;</item>
    /// <item>RIGHT-CLICK in Select, the less used edits of what is under the cursor (mirror, split here, join, copy to another level).</item>
    /// </list>
    /// Esc always returns to Select.
    /// </summary>
    public sealed class SketchView : DockPanel
    {
        public SketchCanvas Canvas { get; } = new SketchCanvas();

        private readonly Dictionary<SketchTool, ToggleButton> tools = new Dictionary<SketchTool, ToggleButton>();
        private readonly ToggleButton snap, ortho, grid, issuesButton, planButton;
        private readonly Button undo, redo;
        private readonly StackPanel levelBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox command = new TextBox { Width = 120, VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = Ui.Line, Padding = new Thickness(3, 1, 3, 1) };
        private readonly TextBlock status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 10, 0) };
        private readonly TextBlock coords = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted, MinWidth = 150, TextAlignment = TextAlignment.Right };
        private readonly StackPanel toolStrip = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        private readonly StackPanel buildingTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel viewBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border topBorder;

        /// <summary>Extra buttons of the host, in the top bar.</summary>
        public Panel HostTools { get; } = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        public SketchView()
        {
            LastChildFill = true;
            Background = Brushes.White;

            // LEFT: the work on the drawing, in the order of the work
            Tool(SketchTool.Select, Ui.Icons16.Select, "Select", "Esc", "Click to pick, Shift+click to add, drag a window. Drag a grip to stretch or move. Right-click for more edits.");
            toolStrip.Children.Add(Ui.HSeparator());
            undo = Ui.Square(new Button(), Ui.Icon(Ui.Icons16.Undo), Ui.Tip("Undo", "Ctrl+Z", null));
            undo.Click += (s, e) => Canvas.Undo();
            redo = Ui.Square(new Button(), Ui.Icon(Ui.Icons16.Redo), Ui.Tip("Redo", "Ctrl+Y", null));
            redo.Click += (s, e) => Canvas.Redo();
            toolStrip.Children.Add(undo);
            toolStrip.Children.Add(redo);
            toolStrip.Children.Add(Ui.HSeparator());
            Tool(SketchTool.Wall, Ui.Icons16.Wall, "Wall", "W", "A wall of the story under this level; walls are square. Right-click or Enter ends a run.", fill: true);
            Tool(SketchTool.Diaphragm, Ui.Icons16.Diaphragm, "Diaphragm", "D", "A rectangle of floor or roof on this level; touching rectangles merge. Select it to drag its corners or sides.", fill: true);
            Tool(SketchTool.Cut, Ui.Icons16.Cut, "Cut out", "K", "Take a rectangle out of this level's diaphragms: a notch, an L, a courtyard. A part left separate becomes its own diaphragm.");
            Tool(SketchTool.Opening, Ui.Icons16.Opening, "Opening", "O", "A hole through this level (stairs, a lobby); set taller ones in the properties.");
            Tool(SketchTool.Note, Ui.Icons16.Note, "Note", "N", "A note on this level's plan.");
            toolStrip.Children.Add(Ui.HSeparator());
            Tool(SketchTool.Move, Ui.Icons16.Move, "Move", "M", "Pick what to move (or select it first), then a base point and where it goes.");
            Tool(SketchTool.Copy, Ui.Icons16.Copy, "Copy", "C  ·  Ctrl+C", "C: a base point, then click for as many copies as you need.\nCtrl+C: one copy follows the cursor straight across or up; click to place it.");
            var delete = Ui.Square(new Button(), Ui.Icon(Ui.Icons16.Delete), Ui.Tip("Delete", "Del", "Deletes the selection (Ctrl+Z brings it back)."));
            delete.Click += (s, e) => Canvas.DeleteSelected();
            toolStrip.Children.Add(delete);
            toolStrip.Children.Add(Ui.HSeparator());
            Tool(SketchTool.Measure, Ui.Icons16.Measure, "Measure", "T", "The distance between two points.");
            var left = new Border { Child = toolStrip, Background = Ui.Bar, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0) };
            DockPanel.SetDock(left, System.Windows.Controls.Dock.Left);

            // TOP: the building and the levels
            buildingTools.Children.Add(Ui.Button("", "From size…", "New from size: a box L × B with its stories, a diaphragm on each level and the outside shear walls (replaces the drawing; Undo puts it back)", (s, e) => NewFromSize()));
            buildingTools.Children.Add(Ui.Button("", "Roof…", "The roof over the top story: flat, gable or hip, its slope (4:12, 6:12...), ridge, parapet and eaves", (s, e) => EditRoof()));
            var top = new DockPanel { Background = Ui.Bar, LastChildFill = false };
            DockPanel.SetDock(levelBar, System.Windows.Controls.Dock.Right);
            top.Children.Add(levelBar);
            top.Children.Add(buildingTools);
            top.Children.Add(HostTools);
            topBorder = new Border { Child = top, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 3, 4, 3) };
            DockPanel.SetDock(topBorder, System.Windows.Controls.Dock.Top);

            // BOTTOM: the view, then the typed point, the message and the coordinates
            viewBar.Children.Add(Ui.GroupLabel("View"));
            var zoom = Ui.Button("", "", "Zoom to the drawing (Z, or double-click the middle button). The wheel zooms, the middle button pans.", (s, e) => Canvas.ZoomExtents());
            zoom.Content = Ui.Icon(Ui.Icons16.ZoomAll);
            viewBar.Children.Add(zoom);
            viewBar.Children.Add(Ui.Separator());
            snap = Ui.Toggle("", "Snap", "Object snaps (F3): ends, middles, crossings, square to, grid", (s, e) => Canvas.ToggleSnap());
            ortho = Ui.Toggle("", "Ortho", "Ortho (F8): move, copy and measure straight across or up", (s, e) => Canvas.ToggleOrtho());
            grid = Ui.Toggle("", "Grid", "The grid: 1 ft lines, heavier every 10 ft, labelled in feet along the edges (coarser when zoomed out); snaps when Snap is on", (s, e) => { Canvas.ShowGrid = grid.IsChecked == true; Canvas.Refresh(); });
            viewBar.Children.Add(snap);
            viewBar.Children.Add(ortho);
            viewBar.Children.Add(grid);
            viewBar.Children.Add(Ui.Separator());
            issuesButton = Ui.Toggle("", "Issues", "Show the issues on the plan (circle: error, triangle: warning). Click a marker to select what it is about.",
                                     (s, e) => { Canvas.ShowIssues = issuesButton.IsChecked == true; Canvas.Refresh(); });
            viewBar.Children.Add(issuesButton);
            planButton = Ui.Toggle("", "Underlay ▾", "The drawing under this level, to trace: import a DXF, PDF or picture; set its scale, straighten, move; move or copy it to another floor", (s, e) => PlanMenu());
            viewBar.Children.Add(planButton);
            viewBar.Children.Add(Ui.Separator());

            var bottom = new DockPanel { Background = Ui.Bar, LastChildFill = true };
            var bottomBorder = new Border { Child = bottom, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(4, 3, 8, 3) };
            DockPanel.SetDock(viewBar, System.Windows.Controls.Dock.Left);
            DockPanel.SetDock(command, System.Windows.Controls.Dock.Left);
            DockPanel.SetDock(coords, System.Windows.Controls.Dock.Right);
            command.ToolTip = "Type a point and Enter:  x,y   @dx,dy   @length<angle   or a length toward the cursor (12'6\" works too)";
            bottom.Children.Add(viewBar);
            bottom.Children.Add(command);
            bottom.Children.Add(coords);
            bottom.Children.Add(status);
            DockPanel.SetDock(bottomBorder, System.Windows.Controls.Dock.Bottom);

            Children.Add(topBorder);
            Children.Add(bottomBorder);
            Children.Add(left);
            Children.Add(Canvas);

            command.KeyDown += Command_KeyDown;
            Canvas.TypingStarted += (s, text) => { command.Text = text; command.Focus(); command.CaretIndex = command.Text.Length; };
            Canvas.Status += (s, e) => { status.Text = e.Text; status.Foreground = e.IsWarning ? Ui.Warning : Ui.Ink; };
            Canvas.CursorMoved += (s, e) => coords.Text = $"x {Canvas.CursorPoint.X:0.00}   y {Canvas.CursorPoint.Y:0.00} ft";
            Canvas.ToolChanged += (s, e) => UpdateButtons();
            Canvas.ModelChanged += (s, e) => { UpdateButtons(); UpdateLevels(); };
            Canvas.ActiveLevelChanged += (s, e) => UpdateLevels();
            Canvas.SelectionChanged += (s, e) => UpdateButtons();
            Canvas.ContextMenuRequested += (s, e) => ShowContextMenu(e);
            UpdateButtons();
            UpdateLevels();
        }

        private void Tool(SketchTool tool, string icon, string name, string keys, string tip, bool fill = false)
        {
            var b = Ui.Square(new ToggleButton(), Ui.Icon(icon, fill), Ui.Tip(name, keys, tip));
            b.Click += (s, e) => Canvas.SetTool(tool);
            tools[tool] = b;
            toolStrip.Children.Add(b);
        }

        /// <summary>Shows or hides the sketch's own level buttons (a host with its own hides them).</summary>
        public bool ShowLevelBar
        {
            get => levelBar.Visibility == Visibility.Visible;
            set { levelBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed; UpdateTopBar(); }
        }

        /// <summary>Shows or hides From size… and Roof… (a host that has them in its menus hides them).</summary>
        public bool ShowBuildingTools
        {
            get => buildingTools.Visibility == Visibility.Visible;
            set { buildingTools.Visibility = value ? Visibility.Visible : Visibility.Collapsed; UpdateTopBar(); }
        }

        /// <summary>The top bar shows only when it has something in it.</summary>
        private void UpdateTopBar() =>
            topBorder.Visibility = ShowLevelBar || ShowBuildingTools || HostTools.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Shows or hides the Underlay ▾ button (a host with its own plan commands hides it).</summary>
        public bool ShowPlanButton
        {
            get => planButton.Visibility == Visibility.Visible;
            set => planButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Puts the cursor in the command box (with some text), for a host command that waits for a typed value.</summary>
        public void FocusCommand(string text = "")
        {
            command.Text = text ?? "";
            command.Focus();
            command.CaretIndex = command.Text.Length;
        }

        /// <summary>Hides a tool's button and stops it being used.</summary>
        public void HideTool(SketchTool tool)
        {
            Canvas.EnabledTools.Remove(tool);
            if (tools.TryGetValue(tool, out var b)) b.Visibility = Visibility.Collapsed;
        }

        public void UpdateButtons()
        {
            foreach (var kv in tools) kv.Value.IsChecked = Canvas.Tool == kv.Key;
            snap.IsChecked = Canvas.SnapOn;
            ortho.IsChecked = Canvas.Ortho;
            grid.IsChecked = Canvas.ShowGrid;
            issuesButton.IsChecked = Canvas.ShowIssues;
            planButton.IsChecked = Canvas.ShowUnderlay && Canvas.PlanOf(Canvas.ActiveLevel) != null;
            undo.IsEnabled = Canvas.History.CanUndo;
            redo.IsEnabled = Canvas.History.CanRedo;
        }

        /// <summary>One small button per level, top level first on the right; the active one checked.</summary>
        public void UpdateLevels()
        {
            levelBar.Children.Clear();
            var b = Canvas.Building;
            if (b == null) return;
            levelBar.Children.Add(new TextBlock { Text = "Level", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            foreach (var l in b.LevelsBottomUp)
            {
                var lv = l;
                var t = Ui.Toggle("", l.Name, $"{l.Name}, {l.Elevation:0.##} ft", (s, e) => Canvas.ShowLevel(lv.Id));
                t.IsChecked = Canvas.ActiveLevel == l;
                levelBar.Children.Add(t);
            }
            UpdateButtons();
        }

        // ------------------------------------------------------------------ right-click

        /// <summary>
        /// The right-click menu in Select: with a selection, its edits (move, copy, copy to another level, mirror, split this
        /// wall here, join, delete); on empty space, the view (zoom, select all, the aids).
        /// </summary>
        private void ShowContextMenu(SketchMenuEventArgs e)
        {
            var b = Canvas.Building;
            if (b == null) return;
            var menu = new ContextMenu { PlacementTarget = Canvas, Placement = PlacementMode.MousePoint };
            MenuItem Item(ItemsControl parent, string header, string keys, bool enabled, Action a, string tip = null)
            {
                var m = new MenuItem { Header = header, InputGestureText = keys ?? "", IsEnabled = enabled, ToolTip = tip };
                if (a != null) m.Click += (s, ev) => a();
                parent.Items.Add(m);
                return m;
            }
            var sel = Canvas.Selection.ToList();
            if (sel.Count > 0)
            {
                menu.Items.Add(new MenuItem { Header = sel.Count == 1 ? sel[0] : $"{sel.Count} selected", IsEnabled = false, FontWeight = FontWeights.SemiBold });
                Item(menu, "Move", "M", true, () => Canvas.SetTool(SketchTool.Move), "A base point, then where it goes.");
                Item(menu, "Copy and place", "Ctrl+C", true, Canvas.CopyAndPlace, "One copy follows the cursor straight across or up; click to place it.");
                Item(menu, "Copy several", "C", true, () => Canvas.SetTool(SketchTool.Copy), "A base point, then click for as many copies as you need.");
                var others = b.LevelsBottomUp.Where(l => l != Canvas.ActiveLevel).ToList();
                var toLevel = Item(menu, "Copy to level", null, others.Count > 0, null, "The same place in plan on another level (walls go in the story under it).");
                foreach (var l in others)
                {
                    var target = l;
                    Item(toLevel, $"{l.Name}  ({l.Elevation:0.##} ft)", null, true, () =>
                    {
                        List<string> added = null;
                        Canvas.Edit($"Copied {string.Join(", ", sel)} to {target.Name}.", m => added = BuildingEdits.CopyToLevel(m, sel, m.Level(target.Id)));
                        if (added == null || added.Count == 0) Canvas.Say($"Nothing could go to {target.Name} (walls and openings need a story under it).", true);
                    });
                }
                Item(menu, "Mirror", "I", true, () => Canvas.SetTool(SketchTool.Mirror), "Copies about a square line through two points; the originals stay.");
                menu.Items.Add(new Separator());
                bool onWall = e.HitId != null && b.Find(e.HitId) is Wall;
                Item(menu, "Split this wall here", "X", onWall, () => Canvas.SplitWallAt(e.At), "Splits the wall where you right-clicked.");
                int walls = sel.Count(id => b.Find(id) is Wall);
                Item(menu, "Join the two walls", "J", walls == 2 && sel.Count == 2, Canvas.JoinSelected, "Two walls end to end on one line become one.");
                menu.Items.Add(new Separator());
                Item(menu, "Delete", "Del", true, Canvas.DeleteSelected);
            }
            else
            {
                Item(menu, "Zoom to the drawing", "Z", true, Canvas.ZoomExtents);
                Item(menu, "Select all on this level", "Ctrl+A", true, () => Canvas.Select(b.VisibleIds(Canvas.ActiveLevel)));
                menu.Items.Add(new Separator());
                var s1 = Item(menu, "Snap", "F3", true, () => { Canvas.ToggleSnap(); UpdateButtons(); });
                s1.IsCheckable = true; s1.IsChecked = Canvas.SnapOn;
                var s2 = Item(menu, "Ortho", "F8", true, () => { Canvas.ToggleOrtho(); UpdateButtons(); });
                s2.IsCheckable = true; s2.IsChecked = Canvas.Ortho;
                var s3 = Item(menu, "Grid", null, true, () => { Canvas.ShowGrid = !Canvas.ShowGrid; Canvas.Refresh(); UpdateButtons(); });
                s3.IsCheckable = true; s3.IsChecked = Canvas.ShowGrid;
            }
            menu.IsOpen = true;
        }

        private void Command_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Canvas.TypePoint(command.Text)) command.Clear();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                command.Clear();
                Canvas.Focus();
                e.Handled = true;
            }
        }

        /// <summary>The most stories the host handles in "New from size" (null: no limit).</summary>
        public int? MaxStories { get; set; }

        /// <summary>NEW FROM SIZE: the window, then the box replaces the drawing (one undo step).</summary>
        public void NewFromSize()
        {
            var b = Canvas.Building;
            if (b == null) return;
            var q = QuickBuildingDialog.Ask(b, MaxStories, Window.GetWindow(this));
            if (q == null) return;
            BuildFromSize(q);
        }

        /// <summary>Builds the box (the window's result) in the model and shows its first floor. False when it cannot be built.</summary>
        public bool BuildFromSize(BuildingEdits.QuickBuilding q)
        {
            var b = Canvas.Building;
            if (b == null || !BuildingEdits.BuildFromSize(b, q)) return false;
            var first = b.LevelsBottomUp.FirstOrDefault(l => l.Elevation > 0);
            Canvas.ClearSelection();
            Canvas.Commit($"New from size: {q.Length:0.##} × {q.Width:0.##} ft, {q.StoryHeights.Count} stor{(q.StoryHeights.Count == 1 ? "y" : "ies")}.");
            if (first != null) Canvas.ShowLevel(first.Id);
            Canvas.ZoomExtents();
            return true;
        }

        /// <summary>ROOF: the roof window; OK changes the model's roof (one undo step).</summary>
        public void EditRoof()
        {
            var b = Canvas.Building;
            if (b == null) return;
            var r = RoofDialog.Ask(b, Window.GetWindow(this));
            if (r == null) return;
            b.Roof = r;
            Canvas.Commit("Roof: " + b.DescribeRoof() + ".");
            Canvas.Refresh();
        }

        /// <summary>
        /// True (the Building Model editor): Plan ▾ imports, places and removes the plans. False (an app that only uses the
        /// model, e.g. the Shear Wall Calculator): the plans are shown or hidden and zoomed to, nothing else, because the
        /// plans are the building's and only the editor changes the building.
        /// </summary>
        public bool PlanEditing { get; set; } = true;

        /// <summary>Raised by "Edit plans in the Building Model editor..." when <see cref="PlanEditing"/> is off (the item shows only when handled).</summary>
        public event EventHandler EditPlansRequested;

        private void PlanMenu()
        {
            UpdateButtons();
            var level = Canvas.ActiveLevel;
            var u = Canvas.PlanOf(level);
            var menu = new ContextMenu { PlacementTarget = planButton, Placement = PlacementMode.Bottom };
            MenuItem Item(ItemsControl parent, string header, bool enabled, Action a, string tip = null)
            {
                var m = new MenuItem { Header = header, IsEnabled = enabled, ToolTip = tip };
                if (a != null) m.Click += (s, e) => a();
                parent.Items.Add(m);
                return m;
            }
            void ZoomToPlan() { var r = u?.WorldBounds() ?? Rect.Empty; if (!r.IsEmpty) Canvas.ZoomTo(r); }

            if (!PlanEditing)
            {
                Item(menu, Canvas.ShowUnderlay ? "Hide the plans" : "Show the plans", true, () => { Canvas.ShowUnderlay = !Canvas.ShowUnderlay; Canvas.Refresh(); UpdateButtons(); },
                     "Only on this screen: the plans stay in the building model.");
                Item(menu, "Zoom to the plan", u != null, ZoomToPlan);
                if (EditPlansRequested != null)
                {
                    menu.Items.Add(new Separator());
                    Item(menu, "Import or place plans in the Building Model editor...", true, () => EditPlansRequested(this, EventArgs.Empty),
                         "The plans are part of the building, so they are imported, scaled, moved and removed in the Building Model editor.");
                }
                menu.IsOpen = true;
                return;
            }

            bool movable = u != null && !u.Locked;
            Item(menu, u == null ? "Import a plan (DXF, PDF, image)..." : "Replace the plan (DXF, PDF, image)...", level != null, ImportPlan);
            menu.Items.Add(new Separator());
            Item(menu, "Set scale: click two points, type the distance", movable, () => Canvas.SetTool(SketchTool.Calibrate),
                 "Pick the two ends of a long dimension on the plan; the plan is rescaled about the first point.");
            Item(menu, "Straighten: click two points on a wall line", movable, () => Canvas.SetTool(SketchTool.PlanStraighten),
                 "Turns the plan so that line runs exactly E-W or N-S.");
            Item(menu, "Move: base point, then destination", movable, () => Canvas.SetTool(SketchTool.PlanMove),
                 "The destination snaps to the walls, e.g. to line up a building corner with a wall end.");
            Item(menu, "Rotate 90°", movable, () =>
            {
                var r = u.WorldBounds();
                var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                Canvas.EditPlan(level, "Plan turned 90°.", p => p.RotateAbout(c, 90));
            });
            menu.Items.Add(new Separator());
            var opacity = Item(menu, "Opacity", u != null, null);
            foreach (double v in new[] { 0.25, 0.4, 0.6, 0.8, 1.0 })
            {
                var mi = Item(opacity, $"{v * 100:0} %", true, () => Canvas.EditPlan(level, $"Plan opacity {v * 100:0} %.", p => p.Opacity = v));
                mi.IsCheckable = true;
                mi.IsChecked = u != null && Math.Abs(u.Opacity - v) < 0.01;
            }
            Item(menu, u?.Locked == true ? "Unlock" : "Lock (no moving or rescaling)", u != null,
                 () => Canvas.EditPlan(level, u.Locked ? "Plan unlocked." : "Plan locked.", p => p.Locked = !p.Locked));
            Item(menu, u?.Visible == false ? "Show the plan" : "Hide the plan", u != null,
                 () => Canvas.EditPlan(level, u.Visible ? "Plan hidden." : "Plan shown.", p => p.Visible = !p.Visible), "Saved with the building.");
            Item(menu, "Zoom to the plan", u != null, ZoomToPlan);
            menu.Items.Add(new Separator());
            var others = Canvas.Building.LevelsBottomUp.Where(l => l != level).ToList();
            var moveTo = Item(menu, "Move to floor", u != null && others.Count > 0, null);
            var copyTo = Item(menu, "Copy to floor", u != null && others.Count > 0, null);
            foreach (var l in others)
            {
                var target = l;
                Item(moveTo, target.Name, true, () => PlanToLevel(level, target, move: true));
                Item(copyTo, target.Name, true, () => PlanToLevel(level, target, move: false));
            }
            Item(menu, "Remove the plan", u != null, () => Canvas.SetPlan(level, null, $"Plan removed from {level.Name} (the file stays in the project folder). Ctrl+Z undoes it."));
            menu.IsOpen = true;
        }

        /// <summary>Move or copy the plan of <paramref name="from"/> to <paramref name="to"/>, asking before replacing a plan there.</summary>
        private void PlanToLevel(Level from, Level to, bool move)
        {
            var p = Canvas.PlanOf(from)?.Clone();
            if (p == null) return;
            if (Canvas.PlanOf(to) != null &&
                MessageBox.Show(Window.GetWindow(this), $"{to.Name} already has a plan. Replace it with this one?\n\nCtrl+Z can undo it.", "Plan",
                                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Canvas.Edit(move ? $"Plan moved from {from.Name} to {to.Name}." : $"Plan copied from {from.Name} to {to.Name}.", b =>
            {
                PlanImage.Put(b.Level(to.Id), p);
                if (move) PlanImage.Put(b.Level(from.Id), null);
            });
            Canvas.ShowLevel(to.Id);
            UpdateButtons();
        }

        /// <summary>The building's width (ft) for a first guess at a picture's scale; 60 ft when there is nothing yet.</summary>
        private double DrawingWidthFeet()
        {
            var r = BuildingEdits.Extents(Canvas.Building);
            return r != null && r.Value.Width > 1 ? r.Value.Width : 60;
        }

        /// <summary>
        /// Import a plan under the level shown: a DXF (true size from its units, linework to snap to), a PDF sheet (the printed
        /// drawing scale; the paper size is in the file) or a picture (drawing scale and dpi, or set the scale on the plan).
        /// The file is copied into the project's plans folder; the level keeps the file and its placement (Ctrl+Z undoes it).
        /// </summary>
        private async void ImportPlan()
        {
            var level = Canvas.ActiveLevel;
            if (level == null) return;
            var owner = Window.GetWindow(this);
            var open = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Plan under " + level.Name,
                InitialDirectory = BuildingModel.Diagnostics.ProjectFolder.DialogFolder,
                Filter = "Plans (*.dxf;*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif)|*.dxf;*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|" +
                         "CAD drawing (*.dxf)|*.dxf|PDF sheet (*.pdf)|*.pdf|Images (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|" +
                         "All files (*.*)|*.*",
            };
            if (open.ShowDialog(owner) != true) return;
            string file = open.FileName;
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".dwg")
            {
                MessageBox.Show(owner, "DWG files are not read directly. In AutoCAD, save the drawing as DXF (SAVEAS > DXF, or DXFOUT) and import the DXF: " +
                                       "it comes in at true size.", "Plan", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PlanImage p = null;
            bool calibrateOnPlan = false;
            Cursor = Cursors.Wait;
            try
            {
                if (ext == ".dxf")
                {
                    var drawing = await PdfPages.InBackground(() => DxfReader.ReadFile(file));
                    if (drawing.Paths.Count == 0) throw new System.IO.InvalidDataException("No lines, polylines or arcs were found in model space.");
                    Cursor = null;
                    var dialog = new ImportDxfDialog(file, drawing) { Owner = owner };
                    if (dialog.ShowDialog() != true) return;
                    p = dialog.Result;
                    p.StoreInProject(file);
                }
                else if (ext == ".pdf")
                {
                    var pages = await PdfPages.InBackground(() => PdfPages.PaperSizes(file));
                    if (pages.Count == 0) throw new System.IO.InvalidDataException("The PDF has no pages.");
                    Cursor = null;
                    var dialog = new ImportPdfDialog(file, pages, DrawingWidthFeet()) { Owner = owner };
                    if (dialog.ShowDialog() != true) return;
                    p = dialog.Result;
                    calibrateOnPlan = dialog.CalibrateOnDrawing;
                    string name = System.IO.Path.GetFileNameWithoutExtension(file) + (pages.Count > 1 ? $" p{dialog.PageIndex + 1}" : "") + ".png";
                    p.StoreInProject(file, dialog.Png, name);
                }
                else
                {
                    Cursor = null;
                    var dialog = new ImportImageDialog(file, DrawingWidthFeet()) { Owner = owner };
                    if (dialog.ShowDialog() != true) return;
                    p = dialog.Result;
                    p.Source = "Image";
                    calibrateOnPlan = dialog.CalibrateOnDrawing;
                    p.StoreInProject(file);
                }
            }
            catch (Exception ex)
            {
                BuildingModel.Diagnostics.Log.Warning("Plan", "Could not import the plan " + file, ex);
                MessageBox.Show(owner, "This file could not be imported.\n\n" + ex.Message, "Plan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                Cursor = null;
            }

            Canvas.ShowUnderlay = true;
            Canvas.SetPlan(level, p, $"Plan {System.IO.Path.GetFileName(file)} ({p.Source}) placed under {level.Name}" + (p.Calibrated ? "." : "; set its scale next."));
            var bounds = Canvas.PlanOf(level)?.WorldBounds() ?? Rect.Empty;
            if (!bounds.IsEmpty) Canvas.ZoomTo(bounds);
            if (calibrateOnPlan || !p.Calibrated) Canvas.SetTool(SketchTool.Calibrate);
            UpdateButtons();
        }
    }
}
