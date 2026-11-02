using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// Import a DXF plan: the linework comes in at true size from the drawing units (read from the file, or chosen when
    /// the file does not say), on the layers ticked. No scaling step.
    /// </summary>
    internal sealed class ImportDxfDialog : Window
    {
        private readonly DxfDrawing drawing;
        private readonly ComboBox cbUnits = new ComboBox { Width = 130, Margin = new Thickness(8, 0, 0, 0) };
        private readonly TextBlock tbSize = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Foreground = DialogUi.InkSecondary, TextWrapping = TextWrapping.Wrap };
        private readonly StackPanel layerList = new StackPanel();
        private readonly Slider slOpacity = new Slider { Minimum = 0.15, Maximum = 1, Value = 0.8, Width = 160, Margin = new Thickness(8, 0, 0, 0) };
        private readonly string path;

        /// <summary>The placed linework (when the dialog returns true).</summary>
        public PlanImage Result { get; private set; }

        public ImportDxfDialog(string path, DxfDrawing drawing)
        {
            this.path = path;
            this.drawing = drawing;
            Title = "Import DXF plan";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            Background = DialogUi.Surface;

            bool known = !double.IsNaN(DxfReader.FeetPerUnit(drawing.InsUnits));
            foreach (var u in DxfReader.UnitChoices) cbUnits.Items.Add(u.label);
            int pick = Array.FindIndex(DxfReader.UnitChoices, u => u.code == drawing.InsUnits);
            if (known && pick < 0)
            {
                cbUnits.Items.Add(char.ToUpper(DxfReader.UnitName(drawing.InsUnits)[0]) + DxfReader.UnitName(drawing.InsUnits).Substring(1));
                pick = cbUnits.Items.Count - 1;
            }
            cbUnits.SelectedIndex = pick >= 0 ? pick : 0; // inches when the file does not say (US architectural drawings)

            var root = new StackPanel { Margin = new Thickness(16), Width = 460 };
            root.Children.Add(DialogUi.Heading(Path.GetFileName(path)));
            string skipped = drawing.Skipped.Count == 0 ? "" :
                " Not imported: " + string.Join(", ", drawing.Skipped.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Value} {kv.Key.ToLowerInvariant()}")) +
                (drawing.Skipped.Count > 5 ? ", ..." : "") + " (text, dimensions and hatches are left out).";
            root.Children.Add(DialogUi.Note($"{drawing.Paths.Count} lines, polylines and arcs on {drawing.Layers().Count} layers.{skipped}"));

            var unitsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            unitsRow.Children.Add(new TextBlock { Text = "Drawing units", VerticalAlignment = VerticalAlignment.Center });
            unitsRow.Children.Add(cbUnits);
            unitsRow.Children.Add(new TextBlock
            {
                Text = known ? "  from the file" : "  the file does not say: check the size below",
                VerticalAlignment = VerticalAlignment.Center, Foreground = known ? DialogUi.InkMuted : DialogUi.WarnInk,
            });
            root.Children.Add(unitsRow);
            root.Children.Add(tbSize);

            root.Children.Add(new TextBlock { Text = "Layers", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var (layer, count) in drawing.Layers())
            {
                var cb = new CheckBox { Content = $"{layer}  ({count})", Tag = layer, IsChecked = true, Margin = new Thickness(0, 1, 0, 1) };
                cb.Checked += (s, e) => ShowSize();
                cb.Unchecked += (s, e) => ShowSize();
                layerList.Children.Add(cb);
            }
            root.Children.Add(new Border
            {
                BorderBrush = DialogUi.Hairline, BorderThickness = new Thickness(1), Background = System.Windows.Media.Brushes.White, Padding = new Thickness(6, 4, 6, 4),
                Child = new ScrollViewer { Content = layerList, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            });
            var allNone = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            allNone.Children.Add(SmallButton("All", () => SetAll(true)));
            allNone.Children.Add(SmallButton("None", () => SetAll(false)));
            allNone.Children.Add(new TextBlock
            {
                Text = "Untick furniture, text-like and title-block layers to keep the plan clear.",
                Foreground = DialogUi.InkMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            });
            root.Children.Add(allNone);

            var op = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            op.Children.Add(new TextBlock { Text = "Opacity", VerticalAlignment = VerticalAlignment.Center });
            op.Children.Add(slOpacity);
            root.Children.Add(op);
            root.Children.Add(new TextBlock
            {
                Text = "The plan comes in at true size with its lower-left corner at (0, 0); its lines become snap points while drawing. " +
                       "Afterwards: Image ▾ > Move, Straighten, Lock.",
                Margin = new Thickness(0, 10, 0, 0), Foreground = DialogUi.InkMuted, TextWrapping = TextWrapping.Wrap,
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "Import", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += (s, e) => Accept();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;

            cbUnits.SelectionChanged += (s, e) => ShowSize();
            ShowSize();
        }

        private static Button SmallButton(string text, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0) };
            b.Click += (s, e) => act();
            return b;
        }

        private void SetAll(bool on)
        {
            foreach (var cb in layerList.Children.OfType<CheckBox>()) cb.IsChecked = on;
        }

        private HashSet<string> Layers() =>
            new HashSet<string>(layerList.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag));

        private double FeetPerUnit()
        {
            int i = cbUnits.SelectedIndex;
            if (i >= 0 && i < DxfReader.UnitChoices.Length) return DxfReader.FeetPerUnit(DxfReader.UnitChoices[i].code);
            return DxfReader.FeetPerUnit(drawing.InsUnits);
        }

        private string UnitsLabel() => cbUnits.SelectedItem as string ?? "";

        /// <summary>The plan size the units give, so wrong units show at once.</summary>
        private void ShowSize()
        {
            var box = drawing.Bounds(Layers());
            double f = FeetPerUnit();
            tbSize.Text = box.IsEmpty ? "No layer ticked." :
                $"Plan covers {Feet(box.Width * f)} × {Feet(box.Height * f)} of building.";
        }

        private static string Feet(double ft)
        {
            int whole = (int)Math.Floor(ft);
            double inches = Math.Round((ft - whole) * 12, 1);
            if (inches >= 12) { whole++; inches = 0; }
            return $"{whole}'-{inches:0.#}\"";
        }

        private void Accept()
        {
            try
            {
                Result = PlanImage.FromDxf(path, drawing, Layers(), FeetPerUnit(), "DXF, " + UnitsLabel().ToLowerInvariant());
                Result.Opacity = slOpacity.Value;
                DialogResult = true;
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
