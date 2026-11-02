using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// Import a plan image: the scale comes from the printed drawing scale and the image resolution (read from the
    /// file when it has one), or is set on the drawing afterwards by clicking two points a known distance apart.
    /// </summary>
    internal sealed class ImportImageDialog : Window
    {
        private readonly RadioButton rbScale = new RadioButton { Content = "Printed drawing scale", Margin = new Thickness(0, 0, 0, 4) };
        private readonly RadioButton rbPick = new RadioButton { Content = "Set the scale on the drawing: click two points a known distance apart", Margin = new Thickness(0, 10, 0, 0) };
        private readonly ComboBox cbScale = new ComboBox { Width = 130, Margin = new Thickness(20, 0, 8, 0) };
        private readonly TextBox tbDpi = new TextBox { Width = 56, Margin = new Thickness(6, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBlock tbSize = new TextBlock { Margin = new Thickness(20, 6, 0, 0), Foreground = DialogUi.InkSecondary };
        private readonly Slider slOpacity = new Slider { Minimum = 0.15, Maximum = 1, Value = 0.6, Width = 160, Margin = new Thickness(8, 0, 0, 0) };
        private readonly int pixelWidth, pixelHeight;
        private readonly double guessFeetPerPixel;

        /// <summary>The placed image (when the dialog returns true).</summary>
        public PlanImage Result { get; private set; }

        /// <summary>True when the user chose to set the scale with two points on the drawing.</summary>
        public bool CalibrateOnDrawing => rbPick.IsChecked == true;

        /// <param name="fitWidthFeet">Width the image is drawn at until it is calibrated (e.g. the drawing's width).</param>
        public ImportImageDialog(string path, double fitWidthFeet)
        {
            Title = "Import plan image";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            Background = DialogUi.Surface;

            var frame = BitmapFrame.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.None);
            pixelWidth = frame.PixelWidth;
            pixelHeight = frame.PixelHeight;
            double fileDpi = frame.DpiX;
            bool hasDpi = fileDpi > 0 && Math.Abs(fileDpi - 96) > 0.5 && Math.Abs(fileDpi - 72) > 0.5; // 96 and 72 are usually defaults, not a scan resolution
            guessFeetPerPixel = (fitWidthFeet > 0 ? fitWidthFeet : 60) / Math.Max(1, pixelWidth);

            foreach (var s in PlanImage.DrawingScales) cbScale.Items.Add(s.label);
            cbScale.SelectedIndex = Array.FindIndex(PlanImage.DrawingScales, s => s.feetPerInch == 4); // 1/4" = 1'-0"
            tbDpi.Text = (hasDpi ? fileDpi : 150).ToString("0", CultureInfo.InvariantCulture);
            rbScale.IsChecked = hasDpi;
            rbPick.IsChecked = !hasDpi;

            var root = new StackPanel { Margin = new Thickness(16), Width = 460 };
            root.Children.Add(DialogUi.Heading(Path.GetFileName(path)));
            root.Children.Add(DialogUi.Note($"{pixelWidth} × {pixelHeight} pixels; " +
                                      (hasDpi ? $"{fileDpi:0} dpi from the file." : "the file does not give its print resolution (enter the scan or export dpi).")));

            root.Children.Add(new Border { Height = 10 });
            root.Children.Add(rbScale);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(cbScale);
            row.Children.Add(new TextBlock { Text = "at", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(tbDpi);
            row.Children.Add(new TextBlock { Text = "dpi", VerticalAlignment = VerticalAlignment.Center });
            root.Children.Add(row);
            root.Children.Add(tbSize);

            root.Children.Add(rbPick);
            root.Children.Add(new TextBlock
            {
                Text = "Best when the resolution is unknown, e.g. a screenshot or photo. Pick a long dimension for accuracy.",
                Margin = new Thickness(20, 2, 0, 0), Foreground = DialogUi.InkMuted, TextWrapping = TextWrapping.Wrap,
            });

            var op = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            op.Children.Add(new TextBlock { Text = "Opacity", VerticalAlignment = VerticalAlignment.Center });
            op.Children.Add(slOpacity);
            root.Children.Add(op);
            root.Children.Add(new TextBlock
            {
                Text = "The image is placed with its lower-left corner at (0, 0). Afterwards: Image ▾ > Move, Straighten, Lock.",
                Margin = new Thickness(0, 10, 0, 0), Foreground = DialogUi.InkMuted, TextWrapping = TextWrapping.Wrap,
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "Import", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += (s, e) => Accept(path);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;

            cbScale.SelectionChanged += (s, e) => { rbScale.IsChecked = true; ShowSize(); };
            tbDpi.TextChanged += (s, e) => { if (tbDpi.IsKeyboardFocusWithin) rbScale.IsChecked = true; ShowSize(); };
            rbScale.Checked += (s, e) => ShowSize();
            rbPick.Checked += (s, e) => ShowSize();
            ShowSize();
        }

        private bool TryScale(out double feetPerPixel)
        {
            feetPerPixel = 0;
            if (cbScale.SelectedIndex < 0) return false;
            if (!double.TryParse(tbDpi.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dpi) || dpi <= 0) return false;
            feetPerPixel = PlanImage.FeetPerPixelFromDrawingScale(PlanImage.DrawingScales[cbScale.SelectedIndex].feetPerInch, dpi);
            return true;
        }

        /// <summary>The plan size the scale gives, so a wrong dpi shows at once.</summary>
        private void ShowSize()
        {
            if (rbPick.IsChecked == true)
            {
                tbSize.Text = "";
                return;
            }
            tbSize.Text = TryScale(out double f)
                ? $"Image covers {pixelWidth * f:0.#} ft × {pixelHeight * f:0.#} ft of building."
                : "Enter the resolution in dpi (pixels per inch of paper).";
        }

        private void Accept(string path)
        {
            double f = guessFeetPerPixel;
            if (rbScale.IsChecked == true && !TryScale(out f))
            {
                tbDpi.Focus();
                tbDpi.SelectAll();
                return;
            }
            Result = PlanImage.Imported(path, pixelWidth, pixelHeight, f);
            Result.Calibrated = rbScale.IsChecked == true;
            Result.Opacity = slOpacity.Value;
            DialogResult = true;
        }
    }
}
