using BuildingModel.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// Import a PDF sheet: pick the page and its printed drawing scale. A PDF knows its paper size, so the scale is exact
    /// with no resolution to guess. For a sheet that is not to scale, set the scale on the drawing afterwards.
    /// </summary>
    internal sealed class ImportPdfDialog : Window
    {
        private readonly ComboBox cbPage = new ComboBox { Width = 240, Margin = new Thickness(8, 0, 0, 0) };
        private readonly RadioButton rbScale = new RadioButton { Content = "Printed drawing scale", Margin = new Thickness(0, 12, 0, 4), IsChecked = true };
        private readonly RadioButton rbPick = new RadioButton { Content = "Not to scale: set the scale on the drawing (click two points a known distance apart)", Margin = new Thickness(0, 10, 0, 0) };
        private readonly ComboBox cbScale = new ComboBox { Width = 130, Margin = new Thickness(20, 0, 8, 0) };
        private readonly TextBlock tbSize = new TextBlock { Margin = new Thickness(20, 6, 0, 0), Foreground = DialogUi.InkSecondary };
        private readonly Slider slOpacity = new Slider { Minimum = 0.15, Maximum = 1, Value = 0.6, Width = 160, Margin = new Thickness(8, 0, 0, 0) };
        private readonly Button ok = new Button { Content = "Import", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        private readonly List<(double width, double height)> pages;
        private readonly string path;
        private readonly double fitWidthFeet;

        /// <summary>The placed page (when the dialog returns true).</summary>
        public PlanImage Result { get; private set; }

        /// <summary>The rendered page (PNG bytes): the caller stores it in the project's plans folder.</summary>
        public byte[] Png { get; private set; }

        /// <summary>The page shown (0-based), for the stored file's name.</summary>
        public int PageIndex => Math.Max(0, cbPage.SelectedIndex);

        /// <summary>True when the user chose to set the scale with two points on the drawing.</summary>
        public bool CalibrateOnDrawing => rbPick.IsChecked == true;

        /// <param name="pages">Paper size of each page, inches (from <see cref="PdfPages.PaperSizes"/>).</param>
        /// <param name="fitWidthFeet">Width the page is drawn at until it is calibrated, when it is not to scale.</param>
        public ImportPdfDialog(string path, List<(double width, double height)> pages, double fitWidthFeet)
        {
            this.path = path;
            this.pages = pages;
            this.fitWidthFeet = fitWidthFeet > 0 ? fitWidthFeet : 60;
            Title = "Import PDF plan";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowFit.Fit(this); // on screen whatever the content
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            Background = DialogUi.Surface;

            for (int i = 0; i < pages.Count; i++)
                cbPage.Items.Add($"Page {i + 1} of {pages.Count}  ({pages[i].width:0.#} × {pages[i].height:0.#} in)");
            cbPage.SelectedIndex = 0;
            foreach (var s in PlanImage.DrawingScales) cbScale.Items.Add(s.label);
            cbScale.SelectedIndex = Array.FindIndex(PlanImage.DrawingScales, s => s.feetPerInch == 4); // 1/4" = 1'-0"

            var root = new StackPanel { Margin = new Thickness(16), Width = 460 };
            root.Children.Add(DialogUi.Heading(Path.GetFileName(path)));
            var pageRow = new StackPanel { Orientation = Orientation.Horizontal };
            pageRow.Children.Add(new TextBlock { Text = "Sheet", VerticalAlignment = VerticalAlignment.Center });
            pageRow.Children.Add(cbPage);
            root.Children.Add(pageRow);

            root.Children.Add(rbScale);
            root.Children.Add(cbScale);
            root.Children.Add(tbSize);
            root.Children.Add(new TextBlock
            {
                Text = "Use the scale printed under the plan. The sheet must have been plotted at 100 % (\"fit to paper\" changes the scale).",
                Margin = new Thickness(20, 2, 0, 0), Foreground = DialogUi.InkMuted, TextWrapping = TextWrapping.Wrap,
            });
            root.Children.Add(rbPick);

            var op = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            op.Children.Add(new TextBlock { Text = "Opacity", VerticalAlignment = VerticalAlignment.Center });
            op.Children.Add(slOpacity);
            root.Children.Add(op);
            root.Children.Add(new TextBlock
            {
                Text = "The sheet is placed with its lower-left corner at (0, 0). Afterwards: Image ▾ > Move, Straighten, Lock.",
                Margin = new Thickness(0, 10, 0, 0), Foreground = DialogUi.InkMuted, TextWrapping = TextWrapping.Wrap,
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += async (s, e) => await Accept();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;

            cbPage.SelectionChanged += (s, e) => ShowSize();
            cbScale.SelectionChanged += (s, e) => { rbScale.IsChecked = true; ShowSize(); };
            rbScale.Checked += (s, e) => ShowSize();
            rbPick.Checked += (s, e) => ShowSize();
            ShowSize();
        }

        private (double width, double height) Page => pages[Math.Max(0, cbPage.SelectedIndex)];

        private double FeetPerPaperInch => PlanImage.DrawingScales[Math.Max(0, cbScale.SelectedIndex)].feetPerInch;

        private void ShowSize()
        {
            tbSize.Text = rbPick.IsChecked == true ? "" :
                $"Sheet covers {Page.width * FeetPerPaperInch:0.#} ft × {Page.height * FeetPerPaperInch:0.#} ft of building.";
        }

        private async System.Threading.Tasks.Task Accept()
        {
            int index = Math.Max(0, cbPage.SelectedIndex);
            var page = Page;
            double dpi = PdfPages.RenderDpi(page.width, page.height);
            bool toScale = rbScale.IsChecked == true;
            double feetPerPaperInch = FeetPerPaperInch;
            ok.IsEnabled = false;
            Cursor = Cursors.Wait;
            try
            {
                byte[] png = await PdfPages.InBackground(() => PdfPages.RenderPng(path, index, dpi));
                var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                int w = frame.PixelWidth, h = frame.PixelHeight;
                double f = toScale ? PlanImage.FeetPerPixelFromDrawingScale(feetPerPaperInch, w / page.width) : fitWidthFeet / Math.Max(1, w);
                Result = PlanImage.Imported(path, w, h, f);
                Png = png; // stored as a file in the project by the caller
                Result.Calibrated = toScale;
                Result.Opacity = slOpacity.Value;
                Result.Source = pages.Count > 1 ? $"PDF page {index + 1} of {pages.Count}" : "PDF";
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Log.Warning("Could not render the PDF page " + path, ex);
                MessageBox.Show(this, "The page could not be rendered:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                ok.IsEnabled = true;
                Cursor = null;
            }
        }
    }
}
