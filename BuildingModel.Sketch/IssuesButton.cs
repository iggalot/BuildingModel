using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildingModel.Checks;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The suite's issues button, at the right of every app's top bar (owner 2026-11-11: one look and one behaviour in every
    /// app): "✓ No issues" in green, "ⓘ 2 to review" in blue, "⚠ 2 warnings" in amber, "✗ 1 error · ⊘ 1 not designed ·
    /// ⚠ 2 warnings" in red (it flashes when the errors grow). A click drops the list down (<see cref="IssueList"/>); a row
    /// clicked goes to its level, selects its members and zooms to them on the plan (<see cref="SketchCanvas.ShowIssue"/>),
    /// and the list closes. F8 / Shift+F8 step through the problems worst first, a bar under the button saying which
    /// (owner 2026-11-14, UI audit A6); the engineer's review list is apart, its own tier (A7).
    /// </summary>
    public sealed class IssuesButton : Button
    {
        public static readonly Brush ErrorFill = Ui.Frozen(Color.FromRgb(0xC6, 0x28, 0x28));
        public static readonly Brush WarnFill = Ui.Frozen(Color.FromRgb(0xF2, 0xB2, 0x33));
        public static readonly Brush WarnInk = Ui.Frozen(Color.FromRgb(0x3D, 0x2A, 0x00));
        public static readonly Brush OkFill = Ui.Frozen(Color.FromRgb(0xE6, 0xF4, 0xEA));
        public static readonly Brush OkInk = Ui.Frozen(Color.FromRgb(0x1E, 0x7B, 0x34));

        private IList<ModelIssue> issues = new List<ModelIssue>();
        private int shownErrors;

        public IssuesButton()
        {
            Template = PillTemplate();
            Cursor = System.Windows.Input.Cursors.Hand;
            FontSize = 14;
            FontWeight = FontWeights.SemiBold;
            VerticalAlignment = VerticalAlignment.Center;
            Margin = new Thickness(12, 0, 4, 0);
            System.Windows.Automation.AutomationProperties.SetName(this, "Issues");
            Show(null);
            Click += (s, e) => Open();
            Loaded += (s, e) => HookKeys();
        }

        /// <summary>The plan the list's rows go to.</summary>
        public SketchCanvas Sketch { get; set; }

        /// <summary>What the issues are in, for the tooltip ("the model", "the model and the design").</summary>
        public string Scope { get; set; } = "the model";

        /// <summary>The app showing it (<see cref="SuiteApps"/>): an issue another app fixes gets a "Fix in …" link.</summary>
        public string Here { get; set; }

        /// <summary>Opens the app that fixes an issue at its member (the host's); null: no links.</summary>
        public Action<ModelIssue> FixElsewhere { get; set; }

        /// <summary>Raised as a row is clicked, before the plan goes to it: the host shows its plan first (another page open).</summary>
        public event EventHandler<ModelIssue> Picking;

        /// <summary>A row clicked, after the plan has gone to it.</summary>
        public event EventHandler<ModelIssue> Picked;

        /// <summary>A review item ticked or cleared: the host saves it (<see cref="ReviewSection"/>, one undo step) and calls <see cref="ShowReview"/>.</summary>
        public event EventHandler<(ReviewItem item, bool reviewed)> ReviewChanged;

        private IList<ReviewItem> review = new List<ReviewItem>();
        private ReviewSection ticks = new ReviewSection();
        private IssueList openList;

        /// <summary>The engineer's review list and its ticks (UI audit A7): counted on the button (ⓘ), listed apart from the issues.</summary>
        public void ShowReview(IList<ReviewItem> items, ReviewSection reviewed)
        {
            review = items ?? new List<ReviewItem>();
            ticks = reviewed ?? new ReviewSection();
            Show(issues);
            openList?.ShowReview(review, ticks);
        }

        public int ToReview => review.Count(r => !ticks.IsReviewed(r));

        /// <summary>The issues now: the button's words and colour, each tier counted (✗ errors, ⊘ not designed, ⚠ warnings, ⓘ to review).</summary>
        public void Show(IList<ModelIssue> list)
        {
            issues = list ?? new List<ModelIssue>();
            int errors = issues.Count(i => IssueList.Tier(i) == 0), nd = issues.Count(i => IssueList.Tier(i) == 1), warnings = issues.Count(i => IssueList.Tier(i) == 2);
            int open = ToReview;
            Content = Words(errors, nd, warnings, open);
            bool red = errors + nd > 0;
            Background = red ? ErrorFill : warnings > 0 ? WarnFill : open > 0 ? Ui.InfoFill : OkFill;
            BorderBrush = red ? ErrorFill : warnings > 0 ? WarnFill : open > 0 ? Ui.Info : OkInk;
            Foreground = red ? Brushes.White : warnings > 0 ? WarnInk : open > 0 ? Ui.Info : OkInk;
            ToolTip = errors + nd + warnings + open == 0 ? $"No errors or warnings in {Scope}."
                    : $"{Words(errors, nd, warnings, open)} in {Scope}: click for the list. F8 goes to the next problem on the plan, Shift+F8 the one before.";
            if (errors + nd > shownErrors)
            {
                // a few flashes to catch the eye, then it stays solid red
                var flash = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(350)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) };
                BeginAnimation(OpacityProperty, flash);
            }
            shownErrors = errors + nd;
            if (step >= issues.Count) step = -1;
            openList?.Show(issues);
        }

        /// <summary>"✗ 2 errors · ⊘ 1 not designed · ⚠ 3 warnings · ⓘ 2 to review", or "✓ No issues".</summary>
        public static string Words(int errors, int notDesigned, int warnings, int toReview)
        {
            var parts = new List<string>();
            if (errors > 0) parts.Add("✗ " + E(errors));
            if (notDesigned > 0) parts.Add($"⊘ {notDesigned} not designed");
            if (warnings > 0) parts.Add("⚠ " + W(warnings));
            if (toReview > 0) parts.Add($"ⓘ {toReview} to review");
            return parts.Count == 0 ? "✓ No issues" : string.Join("  ·  ", parts);
        }

        public int Errors => issues.Count(i => i.Severity == Severity.Error);
        public int Warnings => issues.Count(i => i.Severity != Severity.Error);

        private static string E(int n) => n == 1 ? "1 error" : $"{n} errors";
        private static string W(int n) => n == 1 ? "1 warning" : $"{n} warnings";

        /// <summary>Drops the list down under the button.</summary>
        public void Open()
        {
            CloseNavigator();
            var list = new IssueList(Sketch) { Width = 440, MaxHeight = 420, Margin = new Thickness(8), Here = Here, FixElsewhere = FixElsewhere };
            // its own ink: a popup inherits from its placement target, and the button's text is white on red
            System.Windows.Documents.TextElement.SetForeground(list, Ui.Ink);
            System.Windows.Documents.TextElement.SetFontWeight(list, FontWeights.Normal);
            System.Windows.Documents.TextElement.SetFontSize(list, 12);
            list.Show(issues);
            list.ShowReview(review, ticks);
            var popup = Drop(list);
            list.Picking += (s, i) => Picking?.Invoke(this, i);
            list.Picked += (s, i) => { popup.IsOpen = false; Picked?.Invoke(this, i); };
            list.ReviewChanged += (s, r) => ReviewChanged?.Invoke(this, r);
            popup.Closed += (s, e) => { if (ReferenceEquals(openList, list)) openList = null; };
            openList = list;
            popup.IsOpen = true;
        }

        private Popup Drop(FrameworkElement child, bool staysOpen = false) => new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = staysOpen,
            AllowsTransparency = true,
            Child = new Border { Background = Brushes.White, BorderBrush = Frozen(0xC3, 0xC2, 0xB7), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = child },
        };

        // ------------------------------------------------------------------ the problem navigator (UI audit A6)

        private int step = -1;
        private Popup navigator;

        /// <summary>The problem stepped to last (F8), or -1.</summary>
        public int Step => step;

        /// <summary>
        /// The next (+1) or the one before (−1) problem, worst first: the plan goes to it (its level, its members selected and
        /// in view, the host's card open), and a bar under the button says which it is, "2 of 5", with ◀ ▶, its "Fix in …"
        /// link and the whole list. False when there is none.
        /// </summary>
        public bool StepTo(int by)
        {
            var ordered = IssueList.Ordered(issues);
            if (ordered.Count == 0) { CloseNavigator(); return false; }
            step = step < 0 ? (by >= 0 ? 0 : ordered.Count - 1) : ((step + by) % ordered.Count + ordered.Count) % ordered.Count;
            var i = ordered[step];
            Picking?.Invoke(this, i);
            Sketch?.ShowIssue(i);
            Picked?.Invoke(this, i);
            if (IsLoaded) ShowNavigator(i, step, ordered.Count);
            return true;
        }

        private void ShowNavigator(ModelIssue i, int at, int count)
        {
            CloseNavigator();
            var list = new IssueList(Sketch) { Here = Here, FixElsewhere = FixElsewhere };
            Button Arrow(string text, string tip, int by)
            {
                var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Top };
                b.Click += (s, e) => StepTo(by);
                return b;
            }
            var bar = new DockPanel { Width = 440, Margin = new Thickness(8) };
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            left.Children.Add(Arrow("◀", "The problem before (Shift+F8)", -1));
            left.Children.Add(Arrow("▶", "The next problem (F8)", 1));
            left.Children.Add(new TextBlock { Text = $"{at + 1} of {count}", Foreground = Ui.Muted, Margin = new Thickness(2, 2, 8, 0), FontWeight = FontWeights.Normal });
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);
            var body = new StackPanel();
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, FontWeight = FontWeights.Normal, Foreground = Ui.Ink };
            text.Inlines.Add(new System.Windows.Documents.Run(IssueList.Glyph(i) + " ") { Foreground = IssueList.Ink(i), FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new System.Windows.Documents.Run(i.Message));
            body.Children.Add(text);
            var links = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            if (list.LinksOut(i))
            {
                var fix = list.FixLink(i);
                fix.Margin = new Thickness(0, 0, 12, 0);
                fix.MouseLeftButtonUp += (s, e) => CloseNavigator();
                links.Children.Add(fix);
            }
            var all = new TextBlock { Text = "The whole list", Foreground = Ui.Info, Cursor = System.Windows.Input.Cursors.Hand, FontSize = 11, TextDecorations = TextDecorations.Underline, FontWeight = FontWeights.Normal };
            all.MouseLeftButtonUp += (s, e) => Open();
            links.Children.Add(all);
            body.Children.Add(links);
            bar.Children.Add(body);
            navigator = Drop(bar, staysOpen: true); // stays while the plan is used; Esc, the list or leaving the window closes it
            navigator.IsOpen = true;
        }

        private void CloseNavigator()
        {
            if (navigator != null) navigator.IsOpen = false;
            navigator = null;
        }

        private bool keysHooked;

        /// <summary>F8 / Shift+F8 anywhere in the window step through the problems on the plan; Esc closes the bar. Only with a plan.</summary>
        private void HookKeys()
        {
            if (keysHooked) return;
            var w = Window.GetWindow(this);
            if (w == null) return;
            keysHooked = true;
            w.PreviewKeyDown += (s, e) =>
            {
                if (Sketch == null || !IsVisible) return;
                if (e.Key == System.Windows.Input.Key.F8)
                {
                    bool back = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
                    if (StepTo(back ? -1 : 1)) e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Escape && navigator != null) CloseNavigator(); // and the plan takes its Esc too
            };
            w.Deactivated += (s, e) => CloseNavigator(); // a popup that stays open would float over another app
        }

        private static Brush Frozen(byte r, byte g, byte b) => Ui.Frozen(Color.FromRgb(r, g, b));

        private static ControlTemplate pill;

        /// <summary>A rounded pill: its fill and edge the button's, its edge heavier under the mouse.</summary>
        private static ControlTemplate PillTemplate()
        {
            if (pill != null) return pill;
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
            bd.SetValue(Border.PaddingProperty, new Thickness(14, 6, 14, 6));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            var t = new ControlTemplate(typeof(Button)) { VisualTree = bd };
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2.5), "bd"));
            t.Triggers.Add(hover);
            t.Seal();
            return pill = t;
        }
    }
}
