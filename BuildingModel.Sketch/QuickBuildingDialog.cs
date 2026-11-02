using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingModel.Edits;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// "New from size": L, B, the number of stories and their heights (one height for all, or one per story, bottom up),
    /// and whether the outside walls are shear walls. It starts from the building as it is (or the model's defaults) and
    /// says what it will build as you type. Built in code so it can be tested without a window.
    /// </summary>
    public sealed class QuickBuildingPanel : StackPanel
    {
        private readonly TextBox length, width, stories, heights;
        private readonly CheckBox walls;
        private readonly TextBlock summary, problem;
        private readonly int? maxStories;

        public event EventHandler Changed;

        /// <summary>What will be built, or null when the inputs cannot be built (<see cref="Problem"/> says why).</summary>
        public BuildingEdits.QuickBuilding Result { get; private set; }

        public string Problem { get; private set; }

        public QuickBuildingPanel(Building b, int? maxStories = null)
        {
            this.maxStories = maxStories;
            Margin = new Thickness(14);
            Width = 400;
            var start = BuildingEdits.QuickBuilding.Of(b ?? Building.New());
            if (maxStories != null && start.StoryHeights.Count > maxStories) start.StoryHeights = start.StoryHeights.Take(maxStories.Value).ToList();

            length = Box(Num(start.Length));
            width = Box(Num(start.Width));
            stories = Box(start.StoryHeights.Count.ToString(CultureInfo.CurrentCulture));
            heights = Box(start.StoryHeights.Distinct().Count() == 1 ? Num(start.StoryHeights[0]) : string.Join(", ", start.StoryHeights.Select(Num)));
            heights.Width = 160;
            walls = new CheckBox { Content = "Shear walls on the four outside walls", IsChecked = start.OutsideShearWalls, Margin = new Thickness(0, 4, 0, 0) };
            walls.Click += (s, e) => Update();

            Children.Add(Row("L (W-E)", length, "ft"));
            Children.Add(Row("B (S-N)", width, "ft"));
            Children.Add(Row("Stories", stories, maxStories == null ? "" : $"(up to {maxStories})"));
            Children.Add(Row("Heights", heights, "ft: one for every story, or one per story bottom up (15, 12, 12)"));
            Children.Add(walls);
            summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = Ui.Ink };
            problem = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Ui.Error };
            Children.Add(summary);
            Children.Add(problem);
            Update();
        }

        /// <summary>Types into a box as the user would: "length", "width", "stories" or "heights".</summary>
        public void Type(string field, string text) =>
            (field == "length" ? length : field == "width" ? width : field == "stories" ? stories : heights).Text = text;

        public void SetOutsideShearWalls(bool on) { walls.IsChecked = on; Update(); }

        private void Update()
        {
            Result = null;
            Problem = Read(out var q);
            if (Problem == null) Problem = q.Problem();
            if (Problem == null) Result = q;
            if (Result != null)
            {
                var h = Result.StoryHeights;
                summary.Text = $"{Result.Length:0.##} × {Result.Width:0.##} ft, {h.Count} stor{(h.Count == 1 ? "y" : "ies")} " +
                               $"({string.Join(", ", h.Select(x => x.ToString("0.##", CultureInfo.CurrentCulture)))} ft; eave {h.Sum():0.##} ft), " +
                               $"a diaphragm on each level{(Result.OutsideShearWalls ? $" and {4 * h.Count} outside shear walls" : "")}. " +
                               "It replaces what is drawn on every floor; the roof and the project defaults stay. Undo puts it back.";
            }
            else summary.Text = "";
            problem.Text = Problem ?? "";
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private string Read(out BuildingEdits.QuickBuilding q)
        {
            q = new BuildingEdits.QuickBuilding { OutsideShearWalls = walls.IsChecked == true };
            if (!TryNum(length.Text, out double l) || !TryNum(width.Text, out double w)) return "Enter L and B in ft.";
            q.Length = l;
            q.Width = w;
            if (!int.TryParse(stories.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int n) || n < 1) return "Enter the number of stories (1 or more).";
            if (maxStories != null && n > maxStories) return $"This app handles up to {maxStories} stories.";
            var parts = heights.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<double>();
            foreach (var p in parts)
            {
                if (!TryNum(p, out double h)) return $"\"{p}\" is not a height in ft.";
                list.Add(h);
            }
            if (list.Count == 1) list = Enumerable.Repeat(list[0], n).ToList();
            else if (list.Count != n) return $"Give one height for every story, or {n} heights (one per story, bottom up); {list.Count} given.";
            q.StoryHeights = list;
            return null;
        }

        private TextBox Box(string text)
        {
            var t = new TextBox { Text = text, Width = 64, Padding = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
            t.TextChanged += (s, e) => { if (summary != null) Update(); };
            return t;
        }

        private static FrameworkElement Row(string label, FrameworkElement input, string after)
        {
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var caption = new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Ink };
            DockPanel.SetDock(caption, Dock.Left);
            DockPanel.SetDock(input, Dock.Left);
            d.Children.Add(caption);
            d.Children.Add(input);
            d.Children.Add(new TextBlock { Text = after, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
            return d;
        }

        private static string Num(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

        private static bool TryNum(string s, out double v) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);
    }

    /// <summary>The "New from size" window: the panel with Build and Cancel. What to build, or null when cancelled.</summary>
    public static class QuickBuildingDialog
    {
        public static BuildingEdits.QuickBuilding Ask(Building b, int? maxStories = null, Window owner = null)
        {
            var panel = new QuickBuildingPanel(b, maxStories);
            var ok = new Button { Content = "Build", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 14) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var root = new StackPanel();
            root.Children.Add(panel);
            root.Children.Add(buttons);
            ok.IsEnabled = panel.Result != null;
            panel.Changed += (s, e) => ok.IsEnabled = panel.Result != null;
            var w = new Window
            {
                Title = "New from size",
                Content = root,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = owner ?? Application.Current?.MainWindow,
                Background = Brushes.White,
            };
            ok.Click += (s, e) => w.DialogResult = true;
            return w.ShowDialog() == true ? panel.Result : null;
        }
    }
}
