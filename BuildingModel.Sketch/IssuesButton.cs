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
    /// app): "✓ No issues" in green, "⚠ 2 warnings" in amber, "✗ 1 error · ⚠ 2 warnings" in red (it flashes when the errors
    /// grow). A click drops the list down (<see cref="IssueList"/>); a row clicked goes to its level, selects its members and
    /// zooms to them on the plan (<see cref="SketchCanvas.ShowIssue"/>), and the list closes.
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
        }

        /// <summary>The plan the list's rows go to.</summary>
        public SketchCanvas Sketch { get; set; }

        /// <summary>What the issues are in, for the tooltip ("the model", "the model and the design").</summary>
        public string Scope { get; set; } = "the model";

        /// <summary>Raised as a row is clicked, before the plan goes to it: the host shows its plan first (another page open).</summary>
        public event EventHandler<ModelIssue> Picking;

        /// <summary>A row clicked, after the plan has gone to it.</summary>
        public event EventHandler<ModelIssue> Picked;

        /// <summary>The issues now: the button's words and colour.</summary>
        public void Show(IList<ModelIssue> list)
        {
            issues = list ?? new List<ModelIssue>();
            int errors = issues.Count(i => i.Severity == Severity.Error), warnings = issues.Count - errors;
            Content = errors > 0 ? "✗ " + E(errors) + (warnings > 0 ? "  ·  ⚠ " + W(warnings) : "")
                    : warnings > 0 ? "⚠ " + W(warnings) : "✓ No issues";
            Background = errors > 0 ? ErrorFill : warnings > 0 ? WarnFill : OkFill;
            BorderBrush = errors > 0 ? ErrorFill : warnings > 0 ? WarnFill : OkInk;
            Foreground = errors > 0 ? Brushes.White : warnings > 0 ? WarnInk : OkInk;
            ToolTip = errors + warnings == 0 ? $"No errors or warnings in {Scope}."
                    : $"{E(errors)}, {W(warnings)} in {Scope}: click for the list (click one to find it on the plan).";
            if (errors > shownErrors)
            {
                // a few flashes to catch the eye, then it stays solid red
                var flash = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(350)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) };
                BeginAnimation(OpacityProperty, flash);
            }
            shownErrors = errors;
        }

        public int Errors => issues.Count(i => i.Severity == Severity.Error);
        public int Warnings => issues.Count(i => i.Severity != Severity.Error);

        private static string E(int n) => n == 1 ? "1 error" : $"{n} errors";
        private static string W(int n) => n == 1 ? "1 warning" : $"{n} warnings";

        /// <summary>Drops the list down under the button.</summary>
        public void Open()
        {
            var list = new IssueList(Sketch) { Width = 420, MaxHeight = 360, Margin = new Thickness(8) };
            // its own ink: a popup inherits from its placement target, and the button's text is white on red
            System.Windows.Documents.TextElement.SetForeground(list, Ui.Ink);
            System.Windows.Documents.TextElement.SetFontWeight(list, FontWeights.Normal);
            System.Windows.Documents.TextElement.SetFontSize(list, 12);
            list.Show(issues);
            var popup = new Popup
            {
                PlacementTarget = this,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border { Background = Brushes.White, BorderBrush = Frozen(0xC3, 0xC2, 0xB7), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = list },
            };
            list.Picking += (s, i) => Picking?.Invoke(this, i);
            list.Picked += (s, i) => { popup.IsOpen = false; Picked?.Invoke(this, i); };
            popup.IsOpen = true;
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
