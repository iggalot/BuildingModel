using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The project's model defaults (<see cref="ModelDefaults"/>, saved with the building, read by every app): what a new
    /// story, a new roof and a quick building start with, and the framing (the studs, their lumber, the roof's rafters, the
    /// floors' joists and the roof's blocking). It edits a copy; the app that opens it writes the result (one undo step).
    /// The starting values are <see cref="ModelDefaults"/>'s own; a value not offered (a hand-edited file) is kept as a choice.
    /// </summary>
    public sealed class ModelDefaultsWindow : Window
    {
        /// <summary>The choices offered (not defaults: those are <see cref="ModelDefaults"/>'s).</summary>
        public static readonly string[] StudSizes = { "2x4", "2x6", "2x8" };
        public static readonly string[] MemberSizes = { "2x4", "2x6", "2x8", "2x10", "2x12" };
        public static readonly double[] StudSpacings = { 12, 16, 24 };
        public static readonly double[] RafterSpacings = { 12, 16, 19.2, 24 };
        public static readonly double[] DeflectionLimits = { 120, 180, 240, 360 };
        private static readonly (string Key, string Name)[] Species =
            { ("DouglasFirLarch", "Douglas Fir-Larch"), ("HemFir", "Hem-Fir"), ("SprucePineFir", "Spruce-Pine-Fir"), ("SouthernPine", "Southern Pine") };
        private static readonly (string Key, string Name)[] Grades = { ("No2", "No. 2"), ("No3", "No. 3"), ("Stud", "Stud") };

        private readonly TextBox storyHeight = new TextBox(), pitch = new TextBox(), parapet = new TextBox(), overhang = new TextBox(),
                                 newLength = new TextBox(), newWidth = new TextBox(), newStories = new TextBox();
        private readonly ComboBox studFirst = new ComboBox(), studAbove = new ComboBox(), studSpacing = new ComboBox(), species = new ComboBox(),
                                  grade = new ComboBox(), deflection = new ComboBox(), rafterSpacing = new ComboBox(), floorJoist = new ComboBox(),
                                  roofBlocking = new ComboBox();
        private ModelDefaults shown;
        /// <summary>What was wrong with the values typed when OK was pressed (null: nothing).</summary>
        public string Problem { get; private set; }

        public ModelDefaultsWindow(ModelDefaults d)
        {
            Title = "Model defaults";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            int row = 0;
            void Add(string label, UIElement e, string tip = null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetRow(l, row);
                grid.Children.Add(l);
                if (e is FrameworkElement fe) { fe.Margin = new Thickness(0, 4, 0, 4); if (tip != null) fe.ToolTip = tip; }
                Grid.SetRow(e, row);
                Grid.SetColumn(e, 1);
                grid.Children.Add(e);
                System.Windows.Automation.AutomationProperties.SetName(e, label);
                row++;
            }
            void Heading(string text)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 2) };
                Grid.SetRow(t, row);
                Grid.SetColumnSpan(t, 2);
                grid.Children.Add(t);
                row++;
            }

            Heading("New stories, roofs and quick buildings");
            Add("Story height (ft)", storyHeight, "A new story's height.");
            Add("Roof slope (x:12)", pitch, "The slope a roof gets when it is made gable or hip.");
            Add("Parapet (ft)", parapet, "A new roof's parapet.");
            Add("Eaves (ft)", overhang, "A new roof's overhang.");
            Add("New from size: L W-E (ft)", newLength);
            Add("New from size: B S-N (ft)", newWidth);
            Add("New from size: stories", newStories);
            Heading("Framing");
            Add("Studs on the first story", studFirst, "A wall standing on the foundation.");
            Add("Studs above", studAbove);
            Add("Stud spacing", studSpacing);
            Add("Lumber species", species, "The framing's species (NDS Supplement Tables 4A/4B), for the studs, plates, rims and blocking.");
            Add("Lumber grade", grade);
            Add("Wall deflection limit (L/n)", deflection, "Under wind (IBC Table 1604.3: 240 with brittle finishes).");
            Add("Rafter or truss spacing", rafterSpacing, "For the wind uplift on each one's tie to the wall.");
            Add("Floor joists (the rim)", floorJoist, "Their depth is the rim's: a collector or chord where no wall is under a line or an edge.");
            Add("Roof blocking", roofBlocking, "The roof's continuous blocking between its rafters: its collector or chord where no wall is under it.");

            var note = new TextBlock
            {
                Text = "Saved with the project: every app reads these. A wall's own framing, set on the wall, wins over them.",
                TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(0, 10, 0, 0),
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(note, row++);
            Grid.SetColumnSpan(note, 2);
            grid.Children.Add(note);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var start = new Button { Content = "_Starting values", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
            start.Click += (s, e) => StartingValues();
            ok.Click += (s, e) =>
            {
                Values();
                if (Problem != null) { MessageBox.Show(this, Problem, Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                DialogResult = true;
            };
            buttons.Children.Add(start);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttons, row++);
            Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);
            Content = grid;

            Show(d ?? new ModelDefaults());
        }

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>A slope in degrees as rise per 12 (18.43° is 4:12).</summary>
        public static double RiseOf(double degrees) => Math.Round(12 * Math.Tan(degrees * Math.PI / 180), 2);
        public static double DegreesOf(double rise) => Math.Round(Math.Atan(rise / 12) * 180 / Math.PI, 2);

        private static void Fill<T>(ComboBox c, IEnumerable<T> choices, T value, Func<T, string> text, Func<T, T, bool> same)
        {
            c.Items.Clear();
            var list = choices.ToList();
            if (!list.Any(x => same(x, value))) list.Add(value);
            foreach (var x in list) c.Items.Add(new ComboBoxItem { Content = text(x), Tag = x });
            c.SelectedIndex = list.FindIndex(x => same(x, value));
        }

        private static T Picked<T>(ComboBox c, T otherwise) => c.SelectedItem is ComboBoxItem i && i.Tag is T v ? v : otherwise;

        private void Show(ModelDefaults d)
        {
            shown = d.Clone();
            bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
            bool Same(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            storyHeight.Text = Num(d.StoryHeight);
            pitch.Text = Num(RiseOf(d.RoofPitch));
            parapet.Text = Num(d.Parapet);
            overhang.Text = Num(d.Overhang);
            newLength.Text = Num(d.NewLength);
            newWidth.Text = Num(d.NewWidth);
            newStories.Text = d.NewStories.ToString(CultureInfo.InvariantCulture);
            Fill(studFirst, StudSizes, d.StudSizeFirstStory, s => s, Same);
            Fill(studAbove, StudSizes, d.StudSizeAbove, s => s, Same);
            Fill(studSpacing, StudSpacings, d.StudSpacing, v => $"{Num(v)}\" o.c.", Near);
            Fill(species, Species.Select(x => x.Key), d.StudSpecies, k => Species.FirstOrDefault(x => x.Key == k).Name ?? k, Same);
            Fill(grade, Grades.Select(x => x.Key), d.StudGrade, k => Grades.FirstOrDefault(x => x.Key == k).Name ?? k, Same);
            Fill(deflection, DeflectionLimits, d.WallDeflectionLimit, v => $"L/{Num(v)}", Near);
            Fill(rafterSpacing, RafterSpacings, d.RafterSpacing, v => $"{Num(v)}\" o.c.", Near);
            Fill(floorJoist, MemberSizes, d.FloorJoistSize, s => s, Same);
            Fill(roofBlocking, MemberSizes, d.RoofBlockingSize, s => s, Same);
        }

        /// <summary>The program's starting values (<see cref="ModelDefaults"/>), shown to keep or cancel.</summary>
        public void StartingValues() => Show(new ModelDefaults());

        /// <summary>The values as shown now; a number that does not read, or is out of range, keeps its old value and sets <see cref="Problem"/>.</summary>
        public ModelDefaults Values()
        {
            var d = shown.Clone();
            var bad = new List<string>();
            double Read(TextBox t, string name, double old, double lo, double hi)
            {
                if (double.TryParse((t.Text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= lo && v <= hi) return v;
                bad.Add($"{name}: \"{t.Text}\" is not a number from {Num(lo)} to {Num(hi)}.");
                return old;
            }
            d.StoryHeight = Read(storyHeight, "Story height", d.StoryHeight, 1, 60);
            d.RoofPitch = DegreesOf(Read(pitch, "Roof slope", RiseOf(d.RoofPitch), 0, 24));
            d.Parapet = Read(parapet, "Parapet", d.Parapet, 0, 20);
            d.Overhang = Read(overhang, "Eaves", d.Overhang, 0, 10);
            d.NewLength = Read(newLength, "New from size L", d.NewLength, 1, 1000);
            d.NewWidth = Read(newWidth, "New from size B", d.NewWidth, 1, 1000);
            d.NewStories = (int)Math.Round(Read(newStories, "New from size stories", d.NewStories, 1, 20));
            d.StudSizeFirstStory = Picked(studFirst, d.StudSizeFirstStory);
            d.StudSizeAbove = Picked(studAbove, d.StudSizeAbove);
            d.StudSpacing = Picked(studSpacing, d.StudSpacing);
            d.StudSpecies = Picked(species, d.StudSpecies);
            d.StudGrade = Picked(grade, d.StudGrade);
            d.WallDeflectionLimit = Picked(deflection, d.WallDeflectionLimit);
            d.RafterSpacing = Picked(rafterSpacing, d.RafterSpacing);
            d.FloorJoistSize = Picked(floorJoist, d.FloorJoistSize);
            d.RoofBlockingSize = Picked(roofBlocking, d.RoofBlockingSize);
            Problem = bad.Count == 0 ? null : string.Join("\n", bad);
            return d;
        }

        /// <summary>Shows the window over <paramref name="owner"/>: the values chosen, or null when cancelled.</summary>
        public static ModelDefaults Edit(Window owner, ModelDefaults d)
        {
            var w = new ModelDefaultsWindow(d) { Owner = owner };
            return w.ShowDialog() == true ? w.Values() : null;
        }
    }
}
