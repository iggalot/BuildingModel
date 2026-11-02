using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// Small, flat toolbar buttons (an icon from Segoe MDL2 Assets and an optional short word) and a few helpers. Error
    /// (dark vermilion) and Warning (dark amber) are text colours readable on white; the plan markers use the canvas palette.
    /// </summary>
    public static class Ui
    {
        public static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");
        public static readonly Brush Hover = Frozen(Color.FromRgb(0xEE, 0xF0, 0xF3));
        public static readonly Brush Checked = Frozen(Color.FromRgb(0xDB, 0xE8, 0xFB));
        public static readonly Brush CheckedEdge = Frozen(Color.FromRgb(0x7F, 0xA8, 0xE8));
        public static readonly Brush Ink = Frozen(Color.FromRgb(0x24, 0x29, 0x33));
        public static readonly Brush Muted = Frozen(Color.FromRgb(0x6B, 0x72, 0x80));
        public static readonly Brush Bar = Frozen(Color.FromRgb(0xF8, 0xF9, 0xFA));
        public static readonly Brush Line = Frozen(Color.FromRgb(0xE3, 0xE6, 0xEA));
        public static readonly Brush Error = Frozen(Color.FromRgb(0xB5, 0x47, 0x08));
        public static readonly Brush Warning = Frozen(Color.FromRgb(0x8A, 0x5A, 0x00));

        public static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static ControlTemplate template;

        /// <summary>A flat template: no chrome, a light fill on hover, a blue tint when checked.</summary>
        private static ControlTemplate Template()
        {
            if (template != null) return template;
            var border = new FrameworkElementFactory(typeof(Border), "bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var t = new ControlTemplate(typeof(ButtonBase)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "bd"));
            t.Triggers.Add(hover);
            var check = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            check.Setters.Add(new Setter(Border.BackgroundProperty, Checked, "bd"));
            check.Setters.Add(new Setter(Border.BorderBrushProperty, CheckedEdge, "bd"));
            t.Triggers.Add(check);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            t.Triggers.Add(off);
            template = t;
            return t;
        }

        /// <summary>A content of an icon glyph and a word (either can be empty).</summary>
        public static object Content(string glyph, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(glyph))
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink });
            if (!string.IsNullOrEmpty(text))
                sp.Children.Add(new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(string.IsNullOrEmpty(glyph) ? 0 : 5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Ink });
            return sp;
        }

        public static Button Button(string glyph, string text, string tip, RoutedEventHandler click)
        {
            var b = new Button();
            Style(b, glyph, text, tip);
            if (click != null) b.Click += click;
            return b;
        }

        public static ToggleButton Toggle(string glyph, string text, string tip, RoutedEventHandler click)
        {
            var b = new ToggleButton();
            Style(b, glyph, text, tip);
            if (click != null) b.Click += click;
            return b;
        }

        private static void Style(ButtonBase b, string glyph, string text, string tip)
        {
            b.Template = Template();
            b.Content = Content(glyph, text);
            b.ToolTip = tip;
            b.Padding = new Thickness(string.IsNullOrEmpty(text) ? 5 : 7, 3, string.IsNullOrEmpty(text) ? 5 : 8, 3);
            b.Margin = new Thickness(1, 0, 1, 0);
            b.MinHeight = 26;
            b.Focusable = false;
            b.Cursor = Cursors.Hand;
            ToolTipService.SetInitialShowDelay(b, 300);
        }

        /// <summary>The small caption that starts a group of buttons (DRAW, EDIT...), so the toolbar reads in the order of the work.</summary>
        public static FrameworkElement GroupLabel(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(), FontSize = 9.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 4, 0), IsHitTestVisible = false,
        };

        /// <summary>A thin vertical line between groups of buttons.</summary>
        public static FrameworkElement Separator() =>
            new Border { Width = 1, Margin = new Thickness(5, 4, 5, 4), Background = Line };
    }
}
