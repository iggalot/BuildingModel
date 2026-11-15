using System.Windows;
using System.Windows.Controls;
using BuildingModel.Diagnostics;
using BuildingModel.Sketch;

namespace BuildingModel.App
{
    /// <summary>
    /// The side panel's footer (owner 2026-11-14, UI audit D1): the selection's card stays on top; the building's totals and
    /// what the analysis apps read from it sit folded at the bottom, one line when folded. "What the apps read" is a section
    /// here, not a message box: it stays open beside the plan and follows each edit.
    /// </summary>
    public partial class MainWindow
    {
        private Border issueBox;
        private Expander footerBox, readsBox;
        private TextBlock footerHead;
        private readonly TextBlock readsText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Ui.Ink };

        private Border BuildFooter()
        {
            footerHead = new TextBlock { Foreground = Ui.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = "BUILDING", FontSize = 10.5, Foreground = Ui.Muted, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(footerHead);
            readsBox = new Expander
            {
                Header = new TextBlock { Text = "What the analysis apps read", FontWeight = FontWeights.SemiBold, FontSize = 11.5 },
                Content = new ScrollViewer { Content = readsText, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 0) },
                ToolTip = "L, B, the stories, the roof and each level's share of each face, as the wind and shear wall apps read them",
                Margin = new Thickness(0, 8, 0, 0),
            };
            readsBox.Expanded += (s, e) => { readsText.Text = ReadsText(); Log.Info("View", "What the analysis apps read: shown"); };
            var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            body.Children.Add(summary);
            body.Children.Add(readsBox);
            footerBox = new Expander { Header = head, Content = body, IsExpanded = false };
            return new Border { Child = footerBox, Padding = new Thickness(10, 6, 8, 8), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        }

        /// <summary>Building > What the analysis apps read: the footer opened at that section.</summary>
        private void BuildingSummary()
        {
            footerBox.IsExpanded = true;
            readsBox.IsExpanded = true;
            readsText.Text = ReadsText();
            readsBox.BringIntoView();
        }
    }
}
