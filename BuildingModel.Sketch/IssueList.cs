using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using BuildingModel.Checks;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The issues as a list, worst first (owner 2026-11-14, UI audit A6, A7): ✗ the errors, ⊘ what is NOT DESIGNED, ⚠ the
    /// warnings, each with its message and level; a click selects what it is about and shows its level in the sketch. An issue
    /// another app fixes has a "Fix in …" link that opens that app at its member. Under them, apart, ⓘ the assumptions for the
    /// engineer's review, each with its tick.
    /// </summary>
    public sealed class IssueList : DockPanel
    {
        private readonly SketchCanvas sketch;
        private readonly TextBlock header = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        private readonly StackPanel rows = new StackPanel();
        private IList<ModelIssue> issues = new List<ModelIssue>();
        private IList<ReviewItem> review = new List<ReviewItem>();
        private ReviewSection ticks = new ReviewSection();

        public IssueList(SketchCanvas sketch)
        {
            this.sketch = sketch;
            DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
            Children.Add(header);
            Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        /// <summary>An issue clicked (after the sketch has gone to it): e.g. the host closes the popup holding the list.</summary>
        public event EventHandler<ModelIssue> Picked;

        /// <summary>An issue clicked, before the sketch goes to it: the host shows its plan first (another page open).</summary>
        public event EventHandler<ModelIssue> Picking;

        /// <summary>The app showing the list (<see cref="SuiteApps"/>): an issue fixed elsewhere gets its link.</summary>
        public string Here { get; set; }

        /// <summary>Opens the app that fixes an issue at its member (the host's: it saves and waits, as Edit building does); null: no links.</summary>
        public Action<ModelIssue> FixElsewhere { get; set; }

        /// <summary>A review item ticked (true) or cleared: the host saves the tick (one undo step).</summary>
        public event EventHandler<(ReviewItem item, bool reviewed)> ReviewChanged;

        /// <summary>The issues worst first: errors (✗ before ⊘), then warnings.</summary>
        public static List<ModelIssue> Ordered(IEnumerable<ModelIssue> list) =>
            (list ?? Enumerable.Empty<ModelIssue>()).OrderBy(Tier).ToList();

        /// <summary>0 an error, 1 not designed, 2 a warning.</summary>
        public static int Tier(ModelIssue i) => i.Severity != Severity.Error ? 2 : i.NotDesigned ? 1 : 0;

        public static string Glyph(ModelIssue i) => Tier(i) == 0 ? "✗" : Tier(i) == 1 ? "⊘" : "⚠";

        public static Brush Ink(ModelIssue i) => i.Severity == Severity.Error ? Ui.Error : Ui.Warning;

        /// <summary>Whether a row of <paramref name="i"/> links to another app (it is fixed there, and the host can open it).</summary>
        public bool LinksOut(ModelIssue i) => FixElsewhere != null && i?.FixIn != null && i.FixIn != Here;

        public void Show(IList<ModelIssue> list)
        {
            issues = list ?? new List<ModelIssue>();
            Rebuild();
        }

        /// <summary>The engineer's review list and its ticks (none: no review section).</summary>
        public void ShowReview(IList<ReviewItem> items, ReviewSection reviewed)
        {
            review = items ?? new List<ReviewItem>();
            ticks = reviewed ?? new ReviewSection();
            Rebuild();
        }

        private void Rebuild()
        {
            rows.Children.Clear();
            int e = issues.Count(i => Tier(i) == 0), nd = issues.Count(i => Tier(i) == 1), w = issues.Count(i => Tier(i) == 2);
            int open = review.Count(r => !ticks.IsReviewed(r));
            header.Text = issues.Count == 0 && open == 0 ? "No issues"
                        : string.Join(", ", new[]
                          {
                              e > 0 ? $"{e} error{(e == 1 ? "" : "s")}" : null,
                              nd > 0 ? $"{nd} not designed" : null,
                              w > 0 ? $"{w} warning{(w == 1 ? "" : "s")}" : null,
                              open > 0 ? $"{open} to review" : null,
                          }.Where(x => x != null));
            header.Foreground = e + nd > 0 ? Ui.Error : w > 0 ? Ui.Warning : open > 0 ? Ui.Info : Ui.Muted;
            foreach (var i in Ordered(issues)) rows.Children.Add(IssueRow(i));
            if (review.Count > 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = "For the engineer's review", FontWeight = FontWeights.SemiBold, Foreground = Ui.Info,
                    Margin = new Thickness(0, issues.Count > 0 ? 10 : 0, 0, 2),
                    ToolTip = "Assumptions the program made that the engineer of record should agree with. Not errors: tick each one once reviewed. " +
                              "The tick is saved with the project and printed in the report; it clears itself when the assumption's values change.",
                });
                foreach (var r in review.OrderBy(r => ticks.IsReviewed(r) ? 1 : 0)) rows.Children.Add(ReviewRow(r));
            }
        }

        private FrameworkElement IssueRow(ModelIssue i)
        {
            var issue = i;
            var mark = new TextBlock { Text = Glyph(i), Foreground = Ink(i), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Top };
            var text = new TextBlock { Text = i.Message, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
            var level = sketch?.Building?.Level(i.LevelId);
            if (level != null) text.Inlines.Add(new Run("  · " + level.Name) { Foreground = Ui.Muted });
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = i.Code };
            DockPanel.SetDock(mark, System.Windows.Controls.Dock.Left);
            row.Children.Add(mark);
            if (LinksOut(i))
            {
                var fix = FixLink(i);
                DockPanel.SetDock(fix, System.Windows.Controls.Dock.Bottom);
                row.Children.Add(fix);
            }
            row.Children.Add(text);
            row.MouseLeftButtonUp += (s, a) => { if (a.Handled) return; Picking?.Invoke(this, issue); sketch?.ShowIssue(issue); Picked?.Invoke(this, issue); };
            row.MouseEnter += (s, a) => row.Background = Ui.Hover;
            row.MouseLeave += (s, a) => row.Background = Brushes.Transparent;
            return row;
        }

        /// <summary>"Fix in the Building Model ↗": opens that app at the issue's member.</summary>
        public TextBlock FixLink(ModelIssue i)
        {
            var link = new TextBlock
            {
                Text = $"Fix in {SuiteApps.Name(i.FixIn)} ↗", Foreground = Ui.Info, Cursor = Cursors.Hand, FontSize = 11, Margin = new Thickness(0, 1, 0, 0),
                TextDecorations = TextDecorations.Underline, HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = $"Saves this project and opens it in {SuiteApps.Name(i.FixIn)}" + (i.Ids.Count > 0 ? $" at {i.Ids[0]}" : "") + ": it is fixed there. This window updates when it saves.",
            };
            link.MouseLeftButtonUp += (s, a) => { a.Handled = true; Picked?.Invoke(this, i); FixElsewhere?.Invoke(i); };
            return link;
        }

        private FrameworkElement ReviewRow(ReviewItem r)
        {
            var tick = ticks.TickOf(r);
            var box = new CheckBox { IsChecked = tick != null, Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Reviewed: I agree with this assumption" };
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = tick != null ? Ui.Muted : Ui.Ink };
            text.Inlines.Add(new Run("ⓘ ") { Foreground = Ui.Info });
            text.Inlines.Add(new Run(r.Title + ": ") { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Run(r.Text));
            if (tick != null) text.Inlines.Add(new Run($"  · reviewed {tick.On}") { Foreground = Ui.Muted });
            box.Click += (s, a) => ReviewChanged?.Invoke(this, (r, box.IsChecked == true));
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            DockPanel.SetDock(box, System.Windows.Controls.Dock.Left);
            row.Children.Add(box);
            row.Children.Add(text);
            if (r.Ids.Count > 0 || r.At != null)
            {
                // its members on the plan
                text.Cursor = Cursors.Hand;
                var asIssue = new ModelIssue { Code = "Review", Message = r.Text, Ids = r.Ids.ToList(), LevelId = r.LevelId, At = r.At, Severity = Severity.Warning };
                text.MouseLeftButtonUp += (s, a) => { Picking?.Invoke(this, asIssue); sketch?.ShowIssue(asIssue); Picked?.Invoke(this, asIssue); };
                row.MouseEnter += (s, a) => row.Background = Ui.Hover;
                row.MouseLeave += (s, a) => row.Background = Brushes.Transparent;
            }
            return row;
        }
    }
}
