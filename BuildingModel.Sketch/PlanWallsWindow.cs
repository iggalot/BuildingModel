using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// Walls from the plan: the walls a CAD layer shows as pairs of lines (<see cref="WallsFromPlan.Propose"/>), offered on
    /// the plan one by one, to add or skip. Each wall added is its own undo step. The logic, without the window.
    /// </summary>
    public sealed class PlanWallReview
    {
        private readonly SketchCanvas canvas;
        private readonly Level level;

        /// <summary>The walls proposed, in order; <see cref="Index"/> the one under review (Count: done).</summary>
        public List<Segment2> Proposed { get; }
        public int Index { get; private set; }
        public int Added { get; private set; }
        public int Skipped { get; private set; }
        public bool Done => Index >= Proposed.Count;
        public Segment2? Current => Done ? (Segment2?)null : Proposed[Index];

        /// <summary>The walls on <paramref name="layer"/> of the plan under the canvas's level, those already drawn left out.</summary>
        public PlanWallReview(SketchCanvas canvas, string layer)
        {
            this.canvas = canvas;
            level = canvas.ActiveLevel;
            var plan = canvas.PlanOf(level);
            var existing = canvas.Building.WallsUnder(level).Select(w => new Segment2(w.Start, w.End));
            Proposed = plan == null || !plan.IsDxf ? new List<Segment2>() : WallsFromPlan.Propose(plan.SegmentsOn(layer), existing);
            Show();
        }

        /// <summary>Adds the wall under review (one undo step) and moves on.</summary>
        public string Add()
        {
            if (Done) return null;
            var s = Proposed[Index];
            var b = canvas.Building;
            var below = b.Below(level);
            string id = null;
            if (below != null)
                canvas.Edit($"Wall added from the plan, {Snapper.Dist(s.A, s.B):0.##} ft.", m => id = m.AddWall(below, level, s.A.X, s.A.Y, s.B.X, s.B.Y).Id);
            Added++;
            Index++;
            Show();
            return id;
        }

        /// <summary>Leaves the wall under review out and moves on.</summary>
        public void Skip()
        {
            if (Done) return;
            Skipped++;
            Index++;
            Show();
        }

        /// <summary>Adds every wall still to review (each its own undo step).</summary>
        public void AddRest()
        {
            while (!Done) Add();
        }

        /// <summary>Takes the proposals off the plan.</summary>
        public void Stop()
        {
            canvas.ProposedWalls = null;
            canvas.ProposedWall = -1;
        }

        /// <summary>What is left drawn on the plan: the one under review bold, the rest faint, in view.</summary>
        private void Show()
        {
            canvas.ProposedWalls = Proposed.Skip(Index).ToList();
            canvas.ProposedWall = Done ? -1 : 0;
            if (Current is Segment2 c) canvas.CenterOn(new Point2((c.A.X + c.B.X) / 2, (c.A.Y + c.B.Y) / 2));
        }
    }

    /// <summary>
    /// The window of <see cref="PlanWallReview"/>: pick the layer the walls are drawn on, then add or skip each wall it finds
    /// (A adds, S skips). Modeless: the plan stays in view to zoom and pan.
    /// </summary>
    public sealed class PlanWallsWindow : Window
    {
        private readonly SketchCanvas canvas;
        private readonly ComboBox layers = new ComboBox { Width = 260, Margin = new Thickness(8, 0, 0, 0) };
        private readonly TextBlock found = DialogUi.Note("");
        private readonly TextBlock now = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6), TextWrapping = TextWrapping.Wrap };
        private readonly Button add = new Button { Content = "_Add it", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button skip = new Button { Content = "_Skip", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button rest = new Button { Content = "Add all the rest", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        private PlanWallReview review;

        public PlanWallReview Review => review;

        public PlanWallsWindow(SketchCanvas canvas)
        {
            this.canvas = canvas;
            Title = "Walls from the plan";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            Background = DialogUi.Surface;

            var plan = canvas.PlanOf(canvas.ActiveLevel);
            var shown = plan?.LayersShown() ?? new List<(string layer, int count)>();
            foreach (var l in shown) layers.Items.Add(new ComboBoxItem { Content = $"{l.layer}  ({l.count})", Tag = l.layer });
            // a layer named for walls first, else the busiest
            int pick = shown.FindIndex(l => l.layer.IndexOf("WALL", StringComparison.OrdinalIgnoreCase) >= 0);
            layers.SelectedIndex = shown.Count == 0 ? -1 : Math.Max(0, pick);
            layers.SelectionChanged += (s, e) => Find();

            var root = new StackPanel { Margin = new Thickness(16), Width = 470 };
            root.Children.Add(DialogUi.Heading("Walls from the plan"));
            root.Children.Add(DialogUi.Note($"Each pair of parallel lines {Snapper.WallLinesMinIn:0.##} to {Snapper.WallLinesMaxIn:0.##} in apart on the layer is a wall: " +
                                            $"its centreline is proposed under {canvas.ActiveLevel?.Name}, one at a time, square walls only. Those already drawn are left out."));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(new TextBlock { Text = "The walls' layer", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(layers);
            root.Children.Add(row);
            root.Children.Add(found);
            root.Children.Add(now);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var close = new Button { Content = "Close", Padding = new Thickness(14, 3, 14, 3), IsCancel = true };
            foreach (var b in new[] { add, skip, rest, close }) buttons.Children.Add(b);
            root.Children.Add(buttons);
            Content = root;

            add.Click += (s, e) => { review?.Add(); Update(); };
            skip.Click += (s, e) => { review?.Skip(); Update(); };
            rest.Click += (s, e) => { review?.AddRest(); Update(); };
            close.Click += (s, e) => Close();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.A) { add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
                else if (e.Key == Key.S) { skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            };
            Closed += (s, e) => review?.Stop();
            if (shown.Count == 0) { found.Text = "This level's underlay has no CAD lines: import a DXF plan first (Underlay ▾)."; Update(); }
            else Find();
        }

        private void Find()
        {
            review?.Stop();
            string layer = (layers.SelectedItem as ComboBoxItem)?.Tag as string;
            review = new PlanWallReview(canvas, layer);
            found.Text = review.Proposed.Count == 0
                ? $"No walls found on {layer}: no pairs of parallel lines a stud wall apart (or they are all drawn already). Try another layer."
                : $"{review.Proposed.Count} wall{(review.Proposed.Count == 1 ? "" : "s")} found on {layer}.";
            Update();
        }

        private void Update()
        {
            var c = review?.Current;
            add.IsEnabled = skip.IsEnabled = rest.IsEnabled = c != null;
            if (review == null) { now.Text = ""; return; }
            if (c is Segment2 s)
            {
                bool ew = Math.Abs(s.B.X - s.A.X) >= Math.Abs(s.B.Y - s.A.Y);
                now.Text = $"Wall {review.Index + 1} of {review.Proposed.Count} (bold on the plan): {Snapper.Dist(s.A, s.B):0.##} ft " +
                           (ew ? $"east-west at y = {s.A.Y:0.##} ft" : $"north-south at x = {s.A.X:0.##} ft") + ". Add it (A) or skip it (S)?";
            }
            else if (review.Proposed.Count > 0)
                now.Text = $"Done: {review.Added} added, {review.Skipped} skipped. Ctrl+Z takes the added walls back one at a time.";
            else now.Text = "";
        }
    }
}
