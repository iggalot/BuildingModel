using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// One anatomy for every inspector card in the suite (owner 2026-11-14, UI audit B1), always in this order, whatever order
    /// the host fills it in:
    /// <list type="number">
    /// <item>the head: its mark, what it is, its status chip ("W2 · shear wall · ✗ 2.13");</item>
    /// <item>the key numbers, at most six, big;</item>
    /// <item>why, one line, in its state's colour;</item>
    /// <item>the fixes, when it fails: a button or two that do the usual fix, each one undo step (the host's);</item>
    /// <item>go deeper: links to its windows (Construction · Diagrams · Every case · Calculation · Sources);</item>
    /// <item>the sections, folded: the edits and the rest of the detail, each remembered open or shut.</item>
    /// </list>
    /// A section left empty takes no room.
    /// </summary>
    public sealed class MemberCard : StackPanel
    {
        /// <summary>At most this many key numbers: the rest go in a section.</summary>
        public const int MaxNumbers = 6;

        private readonly DockPanel head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        private readonly TextBlock subtitle = new TextBlock { Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), FontSize = 11.5 };
        private readonly UniformGrid numbers = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 4) };
        private readonly TextBlock why = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4), FontSize = 12 };
        private readonly StackPanel figure = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        private readonly WrapPanel fixes = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        private readonly WrapPanel deeper = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        private readonly StackPanel sections = new StackPanel();

        /// <summary>Which sections were left open, by their title (and the card's kind): a card rebuilt after an edit opens as it was.</summary>
        private static readonly Dictionary<string, bool> open = new Dictionary<string, bool>();

        private string kind = "";

        public MemberCard()
        {
            Children.Add(head);
            Children.Add(subtitle);
            Children.Add(numbers);
            Children.Add(why);
            Children.Add(figure);
            Children.Add(fixes);
            Children.Add(deeper);
            Children.Add(sections);
            Tidy();
        }

        /// <summary>The card's state (none until <see cref="Head"/> gives one).</summary>
        public MemberHealth Health { get; private set; }

        /// <summary>The key numbers shown, in order (for tests and the grid).</summary>
        public IReadOnlyList<string> NumberLabels => numbers.Children.OfType<FrameworkElement>().Select(f => f.Tag as string).ToList();

        /// <summary>The why line's words; null when there is none.</summary>
        public string WhyText => why.Visibility == Visibility.Visible ? new TextRange(why.ContentStart, why.ContentEnd).Text : null;

        /// <summary>The fix buttons' words.</summary>
        public IReadOnlyList<string> FixTexts => fixes.Children.OfType<Button>().Select(b => b.Content as string).ToList();

        /// <summary>The go-deeper links' words.</summary>
        public IReadOnlyList<string> DeeperTexts => deeper.Children.OfType<TextBlock>().Select(t => t.Text).ToList();

        /// <summary>The sections' titles.</summary>
        public IReadOnlyList<string> SectionTitles => sections.Children.OfType<Expander>().Select(e => e.Tag as string).ToList();

        /// <summary>
        /// The head: <paramref name="mark"/> big, "· <paramref name="what"/>", its status chip (none: no chip), a close button
        /// when <paramref name="close"/> is given, and a muted line under it (where it is, its size).
        /// </summary>
        public MemberCard Head(string mark, string what, MemberHealth health = null, string under = null, Action close = null)
        {
            kind = what ?? "";
            Health = health;
            head.Children.Clear();
            if (close != null)
            {
                var x = new Button
                {
                    Content = "×", Width = 22, Height = 22, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    FontSize = 15, Foreground = Ui.Muted, Cursor = Cursors.Hand, ToolTip = "Close (Esc)", Focusable = false, VerticalAlignment = VerticalAlignment.Top,
                };
                x.Click += (s, e) => close();
                DockPanel.SetDock(x, Dock.Right);
                head.Children.Add(x);
            }
            var chip = Chip(health);
            if (chip != null)
            {
                chip.Margin = new Thickness(6, 1, 0, 0);
                chip.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(chip, Dock.Right);
                head.Children.Add(chip);
            }
            var title = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            title.Inlines.Add(new Run(mark ?? "") { FontSize = 15, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(what)) title.Inlines.Add(new Run("  · " + what) { Foreground = Ui.Muted, FontSize = 12.5 });
            head.Children.Add(title);
            subtitle.Text = under ?? "";
            Tidy();
            return this;
        }

        /// <summary>The status chip: "✗ 2.13" on red, "▲ 0.94" on amber, "✓ 0.72" on green, "⊘ NOT DESIGNED" on red; null for none.</summary>
        public static Border Chip(MemberHealth h)
        {
            if (h == null || h.State == BuildingModel.Sketch.Health.None) return null;
            bool loud = h.State == BuildingModel.Sketch.Health.Fails || h.State == BuildingModel.Sketch.Health.NotDesigned;
            var c = PlanStatus.ColorOf(h.State);
            return new Border
            {
                Background = loud ? PlanStatus.BrushOf(h.State) : Ui.Frozen(Color.FromArgb(0x26, c.R, c.G, c.B)),
                BorderBrush = PlanStatus.BrushOf(h.State), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 0, 7, 1),
                ToolTip = Words(h),
                Child = new TextBlock
                {
                    Text = h.Tag, FontWeight = FontWeights.Bold, FontSize = 12,
                    Foreground = loud ? Brushes.White : h.State == BuildingModel.Sketch.Health.Close ? Ui.Warning : PlanStatus.BrushOf(h.State),
                },
            };
        }

        /// <summary>The chip's words: "Fails: hold-down T, ratio 2.13", "Close to the limit (≥ 0.90)", "Passes", "NOT DESIGNED".</summary>
        public static string Words(MemberHealth h)
        {
            if (h == null) return "";
            string ratio = double.IsNaN(h.Ratio) ? "" : $", ratio {h.Ratio:0.00}";
            string what = string.IsNullOrEmpty(h.What) ? "" : ": " + h.What;
            switch (h.State)
            {
                case BuildingModel.Sketch.Health.Fails: return "Fails" + what + ratio;
                case BuildingModel.Sketch.Health.NotDesigned: return "NOT DESIGNED" + what + ": it has not been checked.";
                case BuildingModel.Sketch.Health.Close: return $"Passes, close to the limit (from {PlanStatus.CloseRatio:0.00})" + what + ratio;
                case BuildingModel.Sketch.Health.Passes: return "Passes" + what + ratio;
                default: return "";
            }
        }

        /// <summary>
        /// A key number: its value big, its label small under it, and a second line (the limit, "of 380 plf"). Its state
        /// colours the value; past <see cref="MaxNumbers"/> it is not shown (put it in a section).
        /// </summary>
        public MemberCard Number(string label, string value, string sub = null, Health state = BuildingModel.Sketch.Health.None, string tip = null)
        {
            if (numbers.Children.Count >= MaxNumbers) return this;
            var ink = state == BuildingModel.Sketch.Health.None || state == BuildingModel.Sketch.Health.Passes ? Ui.Ink
                    : state == BuildingModel.Sketch.Health.Close ? Ui.Warning : PlanStatus.BrushOf(state);
            var tile = new StackPanel { Margin = new Thickness(0, 0, 8, 6), Tag = label, ToolTip = tip, Background = Brushes.Transparent };
            var v = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis };
            string glyph = PlanStatus.Attention(state) ? PlanStatus.Glyph(state) + " " : "";
            v.Text = glyph + (value ?? "—");
            tile.Children.Add(v);
            if (!string.IsNullOrEmpty(sub)) tile.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = ink == Ui.Ink ? Ui.Muted : ink, TextTrimming = TextTrimming.CharacterEllipsis });
            tile.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
            numbers.Children.Add(tile);
            Tidy();
            return this;
        }

        /// <summary>Why, in one line ("Shear 532 plf on gypsum (250 plf): face 1 must be a wood structural panel"), in its state's colour.</summary>
        public MemberCard Why(string text, Health state = BuildingModel.Sketch.Health.None)
        {
            why.Inlines.Clear();
            if (!string.IsNullOrEmpty(text))
            {
                var ink = state == BuildingModel.Sketch.Health.Close ? Ui.Warning : state == BuildingModel.Sketch.Health.None || state == BuildingModel.Sketch.Health.Passes ? Ui.Ink : PlanStatus.BrushOf(state);
                if (PlanStatus.Attention(state)) why.Inlines.Add(new Run(PlanStatus.Glyph(state) + " ") { Foreground = ink, FontWeight = FontWeights.Bold });
                why.Inlines.Add(new Run(text) { Foreground = ink, FontWeight = PlanStatus.Attention(state) ? FontWeights.SemiBold : FontWeights.Normal });
            }
            Tidy();
            return this;
        }

        /// <summary>A picture that reads in one glance (a load diagram), under the why line: the numbers behind it go in a section.</summary>
        public MemberCard Figure(UIElement e)
        {
            if (e == null) return this;
            figure.Children.Add(e);
            Tidy();
            return this;
        }

        /// <summary>For tests: the figures shown.</summary>
        public int FigureCount => figure.Children.Count;

        /// <summary>A fix: a button that does the usual fix (one undo step: the host's). Null <paramref name="click"/>: none.</summary>
        public MemberCard Fix(string text, string tip, Action click)
        {
            if (click == null || string.IsNullOrEmpty(text)) return this;
            var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 4), Focusable = false, FontWeight = FontWeights.SemiBold };
            b.Click += (s, e) => click();
            fixes.Children.Add(b);
            Tidy();
            return this;
        }

        /// <summary>A link deeper: one of its windows ("Construction", "Calculation"). Null <paramref name="click"/>: none.</summary>
        public MemberCard Deeper(string text, string tip, Action click)
        {
            if (click == null || string.IsNullOrEmpty(text)) return this;
            if (deeper.Children.Count > 0) deeper.Children.Add(new TextBlock { Text = "·", Foreground = Ui.Muted, Margin = new Thickness(0, 0, 6, 0) });
            var t = new TextBlock { Text = text, ToolTip = tip, Foreground = Ui.Info, Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline, Margin = new Thickness(0, 0, 6, 0) };
            t.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            deeper.Children.Add(t);
            Tidy();
            return this;
        }

        /// <summary>
        /// A folded section (the edits, the inputs, the notes): its panel, for the host's rows. Open the first time as
        /// <paramref name="openAtFirst"/>, then as the engineer left it (for this kind of card).
        /// </summary>
        public StackPanel Section(string title, bool openAtFirst = false)
        {
            string key = kind + "|" + title;
            var body = new StackPanel { Margin = new Thickness(2, 2, 0, 4) };
            var e = new Expander
            {
                Header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink },
                IsExpanded = open.TryGetValue(key, out bool was) ? was : openAtFirst, Content = body, Tag = title, Margin = new Thickness(0, 2, 0, 0),
            };
            e.Expanded += (s, a) => { if (ReferenceEquals(a.OriginalSource, e)) open[key] = true; };
            e.Collapsed += (s, a) => { if (ReferenceEquals(a.OriginalSource, e)) open[key] = false; };
            sections.Children.Add(e);
            Tidy();
            return body;
        }

        /// <summary>A plain "label  value" row for a section.</summary>
        public static FrameworkElement Row(string label, string value, string tip = null, Brush ink = null, double labelWidth = 100)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1), ToolTip = tip };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = label, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap });
            var v = new TextBlock { Text = value, Foreground = ink ?? Ui.Ink, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        /// <summary>A muted note for a section.</summary>
        public static TextBlock Note(string text) =>
            new TextBlock { Text = text, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 2), FontSize = 11 };

        /// <summary>The state of a ratio, for a key number: fails over 1, close from 0.90.</summary>
        public static Health StateOf(double ratio) => PlanStatus.Of(ratio);

        private void Tidy()
        {
            subtitle.Visibility = string.IsNullOrEmpty(subtitle.Text) ? Visibility.Collapsed : Visibility.Visible;
            numbers.Visibility = numbers.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            numbers.Columns = Math.Max(1, Math.Min(3, numbers.Children.Count));
            why.Visibility = why.Inlines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            figure.Visibility = figure.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            fixes.Visibility = fixes.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            deeper.Visibility = deeper.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            sections.Visibility = sections.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
