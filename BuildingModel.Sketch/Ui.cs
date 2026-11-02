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

        /// <summary>A thin horizontal line between groups of a vertical toolbar.</summary>
        public static FrameworkElement HSeparator() =>
            new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5), Background = Line };

        /// <summary>A 16 × 16 line icon from path data (stroked; <paramref name="fill"/> also fills its closed shapes lightly).</summary>
        public static FrameworkElement Icon(string data, bool fill = false)
        {
            var g = System.Windows.Media.Geometry.Parse(data);
            g.Freeze();
            return new System.Windows.Shapes.Path
            {
                Data = g, Width = 16, Height = 16, Stretch = Stretch.None, Stroke = Ink, StrokeThickness = 1.3,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Fill = fill ? Checked : null, SnapsToDevicePixels = true,
            };
        }

        /// <summary>A tooltip: the command's name and keys in bold, then what it does.</summary>
        public static ToolTip Tip(string title, string keys, string text)
        {
            var sp = new StackPanel { MaxWidth = 320 };
            var head = new TextBlock { FontWeight = FontWeights.SemiBold, Foreground = Ink };
            head.Inlines.Add(new System.Windows.Documents.Run(title));
            if (!string.IsNullOrEmpty(keys)) head.Inlines.Add(new System.Windows.Documents.Run("   " + keys) { Foreground = Muted, FontWeight = FontWeights.Normal });
            sp.Children.Add(head);
            if (!string.IsNullOrEmpty(text)) sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 3, 0, 0) });
            return new ToolTip { Content = sp, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };
        }

        /// <summary>A square icon button for a vertical toolbar (no text: the tooltip names it).</summary>
        public static T Square<T>(T b, FrameworkElement icon, ToolTip tip) where T : ButtonBase
        {
            b.Template = Template();
            b.Content = icon;
            b.ToolTip = tip;
            b.Width = 32;
            b.Height = 30;
            b.Margin = new Thickness(3, 1, 3, 1);
            b.Focusable = false;
            b.Cursor = Cursors.Hand;
            ToolTipService.SetInitialShowDelay(b, 250);
            return b;
        }

        /// <summary>The icons of the drawing tools (16 × 16 path data).</summary>
        public static class Icons16
        {
            public const string Select = "M4,1.5 L4,13.5 L7,10.6 L9.2,15 L11,14.1 L8.8,9.8 L12.8,9.8 Z";
            public const string Wall = "M1.5,6.5 L14.5,6.5 L14.5,9.5 L1.5,9.5 Z";
            public const string Diaphragm = "M2.5,3.5 L13.5,3.5 L13.5,12.5 L2.5,12.5 Z";
            public const string Cut = "M2.5,2.5 L13.5,2.5 L13.5,8 L8,8 L8,13.5 L2.5,13.5 Z M9.5,9.5 L13.5,13.5 M13.5,9.5 L9.5,13.5";
            public const string Opening = "M2.5,2.5 L13.5,2.5 L13.5,13.5 L2.5,13.5 Z M2.5,2.5 L13.5,13.5 M13.5,2.5 L2.5,13.5";
            public const string Note = "M2,2.5 L14,2.5 L14,10.5 L8,10.5 L5,13.5 L5,10.5 L2,10.5 Z";
            public const string Move = "M8,1 L8,15 M1,8 L15,8 M6,3 L8,1 L10,3 M6,13 L8,15 L10,13 M3,6 L1,8 L3,10 M13,6 L15,8 L13,10";
            public const string Copy = "M5.5,1.5 L14.5,1.5 L14.5,10.5 L5.5,10.5 Z M3.5,5.5 L1.5,5.5 L1.5,14.5 L10.5,14.5 L10.5,12.5";
            public const string Delete = "M2.5,4 L13.5,4 M6,4 L6,2 L10,2 L10,4 M4,4 L4.8,14.5 L11.2,14.5 L12,4 M6.5,6.5 L6.5,12 M9.5,6.5 L9.5,12";
            public const string Measure = "M1.5,10.5 L14.5,10.5 L14.5,14.5 L1.5,14.5 Z M4.5,10.5 L4.5,12.5 M8,10.5 L8,13 M11.5,10.5 L11.5,12.5 M1.5,5 L14.5,5 M1.5,3 L1.5,7 M14.5,3 L14.5,7";
            public const string Undo = "M5,3 L1.5,6.5 L5,10 M1.5,6.5 L10,6.5 C13,6.5 14.5,8.5 14.5,10.5 C14.5,12.5 13,14 10,14 L7,14";
            public const string Redo = "M11,3 L14.5,6.5 L11,10 M14.5,6.5 L6,6.5 C3,6.5 1.5,8.5 1.5,10.5 C1.5,12.5 3,14 6,14 L9,14";
            public const string ZoomAll = "M1.5,5 L1.5,1.5 L5,1.5 M11,1.5 L14.5,1.5 L14.5,5 M14.5,11 L14.5,14.5 L11,14.5 M5,14.5 L1.5,14.5 L1.5,11 M5,5 L11,5 L11,11 L5,11 Z";
        }
    }
}
