using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildingModel.Checks;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The issues as a list (errors first): a red or amber mark, the message, and the level. A click selects what the
    /// issue is about and shows its level in the sketch.
    /// </summary>
    public sealed class IssueList : DockPanel
    {
        private readonly SketchCanvas sketch;
        private readonly TextBlock header = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        private readonly StackPanel rows = new StackPanel();

        public IssueList(SketchCanvas sketch)
        {
            this.sketch = sketch;
            DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
            Children.Add(header);
            Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        public void Show(IList<ModelIssue> issues)
        {
            rows.Children.Clear();
            issues = issues ?? new List<ModelIssue>();
            int e = issues.Count(i => i.Severity == Severity.Error), w = issues.Count - e;
            header.Text = issues.Count == 0 ? "No issues" : $"{e} error{(e == 1 ? "" : "s")}, {w} warning{(w == 1 ? "" : "s")}";
            header.Foreground = e > 0 ? Ui.Error : w > 0 ? Ui.Warning : Ui.Muted;
            foreach (var i in issues.OrderBy(i => i.Severity == Severity.Error ? 0 : 1))
            {
                var issue = i;
                var mark = new TextBlock
                {
                    Text = i.Severity == Severity.Error ? "●" : "▲",
                    Foreground = i.Severity == Severity.Error ? Ui.Error : Ui.Warning,
                    Margin = new Thickness(0, 1, 6, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var text = new TextBlock { Text = i.Message, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
                var level = sketch.Building.Level(i.LevelId);
                if (level != null) text.Inlines.Add(new System.Windows.Documents.Run("  · " + level.Name) { Foreground = Ui.Muted });
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = i.Code };
                DockPanel.SetDock(mark, System.Windows.Controls.Dock.Left);
                row.Children.Add(mark);
                row.Children.Add(text);
                row.MouseLeftButtonUp += (s, a) =>
                {
                    if (issue.LevelId != null) sketch.ShowLevel(issue.LevelId);
                    sketch.Select(issue.Ids.Where(id => !(sketch.Building.Find(id) is Model.Level)));
                    sketch.Focus();
                };
                row.MouseEnter += (s, a) => row.Background = Ui.Hover;
                row.MouseLeave += (s, a) => row.Background = Brushes.Transparent;
                rows.Children.Add(row);
            }
        }
    }
}
