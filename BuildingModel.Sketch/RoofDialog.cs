using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingModel.Checks;
using BuildingModel.Model;
using BuildingModel.Serialization;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The roof's inputs: flat, gable or hip; the slope as rise in 12 (4:12, 6:12... one click each; degrees with a °); the gable's ridge direction; the
    /// parapet (flat roofs) and the eaves. What the roof comes to (rise, ridge and mean roof height) and the model's roof
    /// checks update as you type. Built in code so it can be tested without a window.
    /// </summary>
    public sealed class RoofPanel : StackPanel
    {
        private readonly Building building;
        private readonly RadioButton flat, gable, hip;
        private readonly TextBox pitch, parapet, overhang;
        private readonly ComboBox ridge;
        private readonly TextBlock rise, summary, issuesText;
        private readonly FrameworkElement pitchRow, ridgeRow, parapetRow;
        private bool loading;

        /// <summary>Raised when the inputs change (the roof is <see cref="Roof"/>; <see cref="HasErrors"/> says whether it can be used).</summary>
        public event EventHandler Changed;

        public RoofPanel(Building b)
        {
            building = b ?? throw new ArgumentNullException(nameof(b));
            Margin = new Thickness(14);
            Width = 420;

            var shapes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            flat = Shape("Flat", shapes);
            gable = Shape("Gable", shapes);
            hip = Shape("Hip", shapes);
            Children.Add(Row("Roof", shapes, out _));

            // the slope as builders give it, rise in 12 (4:12, 6:12...); the model keeps degrees. "30°" typed is taken as degrees.
            pitch = Box();
            pitch.Width = 48;
            pitch.ToolTip = "Rise in 12: 4 means 4:12. Also 4:12, 4/12, or degrees with a ° (or \"deg\"), e.g. 30°.";
            rise = new TextBlock { Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), MinWidth = 52 };
            var pitchLine = new StackPanel { Orientation = Orientation.Horizontal };
            pitchLine.Children.Add(pitch);
            pitchLine.Children.Add(new TextBlock { Text = ": 12", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            pitchLine.Children.Add(rise);
            foreach (var r in new[] { 4, 6, 8, 12 })
            {
                int riseIn12 = r;
                var preset = new Button { Content = $"{r}:12", Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(3, 0, 0, 0), Focusable = false,
                                          ToolTip = $"{r}:12 = {Math.Atan(r / 12.0) * 180 / Math.PI:0.##}°" };
                preset.Click += (s, e) => pitch.Text = riseIn12.ToString(CultureInfo.CurrentCulture);
                pitchLine.Children.Add(preset);
            }
            Children.Add(pitchRow = Row("Slope", pitchLine, out _));

            ridge = new ComboBox { Width = 200, ItemsSource = new[] { "Along the longer side", "West-East", "South-North" } };
            ridge.SelectionChanged += (s, e) => Update();
            Children.Add(ridgeRow = Row("Ridge", ridge, out _));

            parapet = Box();
            Children.Add(parapetRow = Row("Parapet", Ft(parapet), out _));
            overhang = Box();
            Children.Add(Row("Eaves", Ft(overhang), out _));

            summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = Ui.Ink };
            Children.Add(summary);
            issuesText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            Children.Add(issuesText);

            Load(b.Roof ?? new Roof());
        }

        /// <summary>The roof as entered (a new object; the building is not changed).</summary>
        public Roof Roof { get; private set; }

        /// <summary>True when the roof checks find an error (OK is then disabled).</summary>
        public bool HasErrors { get; private set; }

        /// <summary>Sets the shape as a click would (tests and keyboard).</summary>
        public void SetShape(RoofShape s) { (s == RoofShape.Flat ? flat : s == RoofShape.Gable ? gable : hip).IsChecked = true; }

        /// <summary>Sets the text of a box as typing would: "pitch" (the slope: rise in 12, or degrees with °), "parapet" or "eaves".</summary>
        public void Type(string field, string text) { (field == "pitch" ? pitch : field == "parapet" ? parapet : overhang).Text = text; }

        public void SetRidge(RidgeAxis r) => ridge.SelectedIndex = (int)r;

        private void Load(Roof r)
        {
            loading = true;
            SetShape(r.Shape);
            ShowSlope(r.Pitch);
            ridge.SelectedIndex = (int)r.Ridge;
            parapet.Text = Num(r.Parapet);
            overhang.Text = Num(r.Overhang);
            loading = false;
            Update();
        }

        private void Update()
        {
            if (loading) return;
            var r = new Roof
            {
                Shape = hip.IsChecked == true ? RoofShape.Hip : gable.IsChecked == true ? RoofShape.Gable : RoofShape.Flat,
                Pitch = SlopeDegrees(),
                Ridge = (RidgeAxis)Math.Max(0, ridge.SelectedIndex),
                Parapet = Parse(parapet.Text),
                Overhang = Parse(overhang.Text),
            };
            // a box left empty or unreadable is an error, not a silent 0
            bool unreadable = r.IsSloped && double.IsNaN(r.Pitch) || double.IsNaN(r.Parapet) || double.IsNaN(r.Overhang);
            if (!r.IsSloped && double.IsNaN(r.Pitch)) r.Pitch = (building.Roof ?? new Roof()).Pitch;
            Roof = r;

            pitchRow.Visibility = r.IsSloped ? Visibility.Visible : Visibility.Collapsed;
            ridgeRow.Visibility = r.Shape == RoofShape.Gable ? Visibility.Visible : Visibility.Collapsed;
            parapetRow.Visibility = r.IsSloped ? Visibility.Collapsed : Visibility.Visible;
            rise.Text = r.IsSloped && r.Pitch > 0 && r.Pitch < 90 ? $"= {r.Pitch:0.##}°" : "";

            // the model's own roof checks, on a copy with this roof
            var trial = ModelFile.Clone(building);
            trial.Roof = r.Clone();
            if (r.IsSloped && double.IsNaN(r.Pitch)) trial.Roof.Pitch = 0;
            if (double.IsNaN(r.Parapet)) trial.Roof.Parapet = 0;
            if (double.IsNaN(r.Overhang)) trial.Roof.Overhang = 0;
            var issues = ModelChecks.Run(trial).Where(i => i.Code.StartsWith("Roof", StringComparison.Ordinal) || i.Code == "ParapetOnSlopedRoof" || i.Code == "HipRidge").ToList();
            HasErrors = unreadable || issues.Any(i => i.Severity == Severity.Error);

            var top = trial.RoofLevel();
            summary.Text = top == null ? "There is no story yet: the roof sits on the top level once there is one."
                : $"{trial.DescribeRoof()}. Eave {trial.EaveHeight():0.##} ft ({top.Name}), ridge {trial.RidgeHeight():0.##} ft, mean roof height {trial.MeanRoofHeight():0.##} ft.";
            issuesText.Inlines.Clear();
            if (unreadable) issuesText.Inlines.Add(new System.Windows.Documents.Run("Enter a number in each box. ") { Foreground = Ui.Error });
            foreach (var i in issues)
                issuesText.Inlines.Add(new System.Windows.Documents.Run((i.Severity == Severity.Error ? "✗ " : "▲ ") + i.Message + "\n") { Foreground = i.Severity == Severity.Error ? Ui.Error : Ui.Warning });
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private RadioButton Shape(string text, Panel into)
        {
            var r = new RadioButton { Content = text, GroupName = "roofShape" + GetHashCode(), Margin = new Thickness(0, 0, 14, 0), VerticalContentAlignment = VerticalAlignment.Center };
            r.Checked += (s, e) =>
            {
                // a roof made sloped without a usable slope gets the default one (the project default)
                if (!loading && r != flat && !(SlopeDegrees() > 0)) ShowSlope(building.Defaults.RoofPitch);
                Update();
            };
            into.Children.Add(r);
            return r;
        }

        private TextBox Box()
        {
            var t = new TextBox { Width = 64, Padding = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
            t.TextChanged += (s, e) => Update();
            return t;
        }

        private static FrameworkElement Ft(TextBox t)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(t);
            sp.Children.Add(new TextBlock { Text = "ft", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            return sp;
        }

        private static FrameworkElement Row(string label, FrameworkElement input, out TextBlock caption)
        {
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            caption = new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Ink };
            DockPanel.SetDock(caption, Dock.Left);
            d.Children.Add(caption);
            d.Children.Add(input);
            return d;
        }

        private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.##", CultureInfo.CurrentCulture);

        /// <summary>The slope in degrees that the box was last filled with, and the text it was given (kept exact when not retyped).</summary>
        private double shownDegrees = double.NaN;
        private string shownText;

        /// <summary>Puts a slope (degrees) in the box as rise in 12.</summary>
        private void ShowSlope(double degrees)
        {
            shownDegrees = degrees;
            shownText = double.IsNaN(degrees) || degrees <= 0 || degrees >= 90 ? Num(degrees) : Num(12 * Math.Tan(degrees * Math.PI / 180));
            pitch.Text = shownText;
        }

        /// <summary>The slope in degrees from the box: rise in 12 ("6", "6:12", "6/12") or degrees ("30°", "30 deg"); NaN when unreadable.</summary>
        private double SlopeDegrees()
        {
            string s = (pitch.Text ?? "").Trim();
            if (s == shownText && !double.IsNaN(shownDegrees)) return shownDegrees; // not retyped: exactly what the model had
            return ParseSlope(s);
        }

        /// <summary>A slope typed as rise in 12 ("6", "6:12", "6/12", "6 in 12") or degrees ("30°", "30 deg"), in degrees; NaN when unreadable or not a slope.</summary>
        public static double ParseSlope(string text)
        {
            string s = (text ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0) return double.NaN;
            bool degrees = s.EndsWith("°") || s.EndsWith("deg") || s.EndsWith("degrees");
            if (degrees)
            {
                double d = Parse(s.TrimEnd('°').Replace("degrees", "").Replace("deg", "").Trim());
                return d >= 0 && d < 90 ? d : double.NaN;
            }
            double run = 12;
            foreach (var sep in new[] { ":", "/", " in " })
            {
                int i = s.IndexOf(sep, StringComparison.Ordinal);
                if (i <= 0) continue;
                run = Parse(s.Substring(i + sep.Length));
                s = s.Substring(0, i);
                break;
            }
            double rise = Parse(s);
            if (double.IsNaN(rise) || rise < 0 || !(run > 0)) return double.NaN;
            return Math.Atan(rise / run) * 180 / Math.PI;
        }

        private static double Parse(string s) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double v) ? v : double.NaN;
    }

    /// <summary>The roof window: the <see cref="RoofPanel"/> with OK and Cancel. The roof chosen, or null when cancelled.</summary>
    public static class RoofDialog
    {
        public static Roof Ask(Building b, Window owner = null)
        {
            var panel = new RoofPanel(b);
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 14) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var root = new StackPanel();
            root.Children.Add(panel);
            root.Children.Add(buttons);
            ok.IsEnabled = !panel.HasErrors;
            panel.Changed += (s, e) => ok.IsEnabled = !panel.HasErrors;
            var w = new Window
            {
                Title = "Roof",
                Content = root,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = owner ?? Application.Current?.MainWindow,
                Background = Brushes.White,
            };
            ok.Click += (s, e) => w.DialogResult = true;
            return w.ShowDialog() == true ? panel.Roof : null;
        }
    }
}
