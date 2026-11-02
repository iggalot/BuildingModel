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
    /// The sketcher as one piece: a toolbar of small buttons over the plan, and a status line with the typed-point box,
    /// the message and the cursor coordinates. The level buttons can be hidden when the host shows its own.
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
        private readonly WrapPanel toolbar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

        /// <summary>Extra buttons of the host, at the right end of the toolbar.</summary>
        public Panel HostTools { get; } = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        public SketchView()
        {
            LastChildFill = true;
            Background = Brushes.White;

            // the toolbar, in the order of the work: pick and undo; draw; edit; drafting aids; view
            Tool(SketchTool.Select, "", "", "Select (Esc)  —  click, Shift+click to add, drag a window; drag a grip to stretch");
            undo = Ui.Button("", "", "Undo (Ctrl+Z)", (s, e) => Canvas.Undo());
            redo = Ui.Button("", "", "Redo (Ctrl+Y)", (s, e) => Canvas.Redo());
            toolbar.Children.Add(undo);
            toolbar.Children.Add(redo);
            toolbar.Children.Add(Ui.Separator());
            toolbar.Children.Add(Ui.GroupLabel("Draw"));
            Tool(SketchTool.Wall, "", "Wall", "Wall (W)  —  the story under this level; walls are square. Right-click ends a run.");
            Tool(SketchTool.Diaphragm, "", "Diaphragm", "Diaphragm (D)  —  a rectangle of floor or roof on this level; rectangles merge");
            Tool(SketchTool.Cut, "", "Cut", "Cut (K)  —  take a rectangle out of this level's diaphragms: a notch, an L, a courtyard. A part left separate becomes its own diaphragm.");
            Tool(SketchTool.Opening, "", "Opening", "Opening (O)  —  a hole through this level (stairs, a lobby); set taller ones in the properties");
            Tool(SketchTool.Note, "", "Note", "Note (N)  —  a note on this level's plan");
            toolbar.Children.Add(Ui.Separator());
            toolbar.Children.Add(Ui.GroupLabel("Edit"));
            Tool(SketchTool.Move, "", "Move", "Move (M)  —  base point, then second point");
            Tool(SketchTool.Copy, "", "", "Copy (C)  —  base point, then as many copies as you click");
            Tool(SketchTool.Mirror, "", "Mirror", "Mirror (I)  —  copies about a square line through two points");
            Tool(SketchTool.Split, "", "Split", "Split a wall (X)  —  click where it splits");
            toolbar.Children.Add(Ui.Button("", "Join", "Join (J)  —  the two selected walls, end to end on one line", (s, e) => Canvas.JoinSelected()));
            toolbar.Children.Add(Ui.Button("", "", "Delete (Del)", (s, e) => Canvas.DeleteSelected()));
            toolbar.Children.Add(Ui.Separator());
            toolbar.Children.Add(Ui.GroupLabel("Aids"));
            snap = Ui.Toggle("", "Snap", "Object snaps (F3): ends, middles, crossings, square to, grid", (s, e) => Canvas.ToggleSnap());
            ortho = Ui.Toggle("", "Ortho", "Ortho (F8): move, copy and measure straight across or up", (s, e) => Canvas.ToggleOrtho());
            grid = Ui.Toggle("", "Grid", "Grid (1 ft; snaps when Snap is on)", (s, e) => { Canvas.ShowGrid = grid.IsChecked == true; Canvas.Refresh(); });
            toolbar.Children.Add(snap);
            toolbar.Children.Add(ortho);
            toolbar.Children.Add(grid);
            Tool(SketchTool.Measure, "", "", "Measure (T)");
            toolbar.Children.Add(Ui.Separator());
            toolbar.Children.Add(Ui.GroupLabel("Building"));
            toolbar.Children.Add(Ui.Button("", "Roof", "The roof over the top story: flat, gable or hip, its slope and ridge, the parapet and the eaves", (s, e) => EditRoof()));
            toolbar.Children.Add(Ui.Separator());
            toolbar.Children.Add(Ui.GroupLabel("View"));
            toolbar.Children.Add(Ui.Button("", "", "Zoom to the drawing (Z, or double-click the middle button)", (s, e) => Canvas.ZoomExtents()));
            issuesButton = Ui.Toggle("", "Issues", "Show the issues on the plan (vermilion circle: error, amber triangle: warning). Click a marker to select what it is about.",
                                     (s, e) => { Canvas.ShowIssues = issuesButton.IsChecked == true; Canvas.Refresh(); });
            toolbar.Children.Add(issuesButton);
            planButton = Ui.Toggle("", "Plan ▾", "A plan under this level to trace: import a DXF or an image, calibrate, hide, remove", (s, e) => PlanMenu());
            toolbar.Children.Add(planButton);
            toolbar.Children.Add(HostTools);

            var top = new DockPanel { Background = Ui.Bar, LastChildFill = true };
            var topBorder = new Border { Child = top, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 3, 4, 3) };
            DockPanel.SetDock(levelBar, System.Windows.Controls.Dock.Right);
            top.Children.Add(levelBar);
            top.Children.Add(toolbar);
            DockPanel.SetDock(topBorder, System.Windows.Controls.Dock.Top);
            Children.Add(topBorder);

            // the status line
            var bottom = new DockPanel { Background = Ui.Bar, LastChildFill = true };
            var bottomBorder = new Border { Child = bottom, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(4, 3, 8, 3) };
            DockPanel.SetDock(command, System.Windows.Controls.Dock.Left);
            DockPanel.SetDock(coords, System.Windows.Controls.Dock.Right);
            command.ToolTip = "Type a point and Enter:  x,y   @dx,dy   @length<angle   or a length toward the cursor (12'6\" works too)";
            bottom.Children.Add(command);
            bottom.Children.Add(coords);
            bottom.Children.Add(status);
            DockPanel.SetDock(bottomBorder, System.Windows.Controls.Dock.Bottom);
            Children.Add(bottomBorder);

            Children.Add(Canvas);

            command.KeyDown += Command_KeyDown;
            Canvas.TypingStarted += (s, text) => { command.Text = text; command.Focus(); command.CaretIndex = command.Text.Length; };
            Canvas.Status += (s, e) => { status.Text = e.Text; status.Foreground = e.IsWarning ? Ui.Warning : Ui.Ink; };
            Canvas.CursorMoved += (s, e) => coords.Text = $"x {Canvas.CursorPoint.X:0.00}   y {Canvas.CursorPoint.Y:0.00} ft";
            Canvas.ToolChanged += (s, e) => UpdateButtons();
            Canvas.ModelChanged += (s, e) => { UpdateButtons(); UpdateLevels(); };
            Canvas.ActiveLevelChanged += (s, e) => UpdateLevels();
            Canvas.SelectionChanged += (s, e) => UpdateButtons();
            UpdateButtons();
            UpdateLevels();
        }

        private void Tool(SketchTool tool, string glyph, string text, string tip)
        {
            var b = Ui.Toggle(glyph, text, tip, (s, e) => Canvas.SetTool(tool));
            tools[tool] = b;
            toolbar.Children.Add(b);
        }

        /// <summary>Shows or hides the sketch's own level buttons (a host with its own hides them).</summary>
        public bool ShowLevelBar
        {
            get => levelBar.Visibility == Visibility.Visible;
            set => levelBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Shows or hides the Plan ▾ button (a host with its own plan import hides it).</summary>
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
            planButton.IsChecked = Canvas.UnderlayOf(Canvas.ActiveLevel) != null;
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

        /// <summary>ROOF: the roof window; OK changes the model's roof (one undo step).</summary>
        private void EditRoof()
        {
            var b = Canvas.Building;
            if (b == null) return;
            var r = RoofDialog.Ask(b, Window.GetWindow(this));
            if (r == null) return;
            b.Roof = r;
            Canvas.Commit("Roof: " + b.DescribeRoof() + ".");
            Canvas.Refresh();
        }

        private void PlanMenu()
        {
            UpdateButtons();
            var u = Canvas.UnderlayOf(Canvas.ActiveLevel);
            var menu = new ContextMenu { PlacementTarget = planButton, Placement = PlacementMode.Bottom };
            MenuItem Item(string header, bool enabled, Action a)
            {
                var m = new MenuItem { Header = header, IsEnabled = enabled };
                m.Click += (s, e) => a();
                menu.Items.Add(m);
                return m;
            }
            Item("Import a plan (DXF or image)...", true, ImportPlan);
            Item("Calibrate the scale (two points)", u != null && !u.IsDxf, () => Canvas.SetTool(SketchTool.Calibrate));
            Item(u?.Visible == false ? "Show the plan" : "Hide the plan", u != null, () => { u.Visible = !u.Visible; Canvas.SaveUnderlay(); Canvas.Commit(u.Visible ? "Plan shown." : "Plan hidden."); UpdateButtons(); });
            Item("Remove the plan", u != null, () => Canvas.SetUnderlay(null));
            menu.IsOpen = true;
        }

        private void ImportPlan()
        {
            var open = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Plan under " + Canvas.ActiveLevel?.Name,
                Filter = "Plans (*.dxf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.dxf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*",
            };
            if (open.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var u = PlanUnderlay.Import(open.FileName);
                Canvas.SetUnderlay(u);
                if (!u.IsDxf) Canvas.SetTool(SketchTool.Calibrate);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "This plan could not be read.\n\n" + ex.Message, "Plan", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateButtons();
        }
    }
}
