using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The properties of the selection (or of the level shown when nothing is selected): typed values change the model
    /// through the sketch, so every change is one undo step and every view updates.
    /// </summary>
    public sealed class ObjectProperties : StackPanel
    {
        private readonly SketchCanvas sketch;
        private bool filling;

        public ObjectProperties(SketchCanvas sketch)
        {
            this.sketch = sketch;
            Margin = new Thickness(8);
            sketch.SelectionChanged += (s, e) => Fill();
            sketch.ModelChanged += (s, e) => Fill();
            sketch.ActiveLevelChanged += (s, e) => Fill();
            Fill();
        }

        private Building B => sketch.Building;

        public void Fill()
        {
            if (filling) return;
            Children.Clear();
            var ids = sketch.Selection.ToList();
            if (ids.Count > 1)
            {
                Title($"{ids.Count} selected");
                var walls = ids.Select(B.Find).OfType<Wall>().ToList();
                if (walls.Count > 0)
                {
                    var cb = new CheckBox { Content = "Shear walls", IsThreeState = false, IsChecked = walls.All(w => w.IsShearWall), Margin = new Thickness(0, 4, 0, 0) };
                    cb.Click += (s, e) => sketch.Edit($"{walls.Count} walls set as {(cb.IsChecked == true ? "shear walls" : "other walls")}.", b => walls.ForEach(w => w.IsShearWall = cb.IsChecked == true));
                    Children.Add(cb);
                }
                return;
            }
            var o = ids.Count == 1 ? B.Find(ids[0]) : null;
            switch (o)
            {
                case Wall w: WallProps(w); break;
                case Diaphragm d: RectProps(d, d.Pieces); Levels("Level", d.LevelId, id => d.LevelId = id); SpanProps(d); break;
                case Opening op:
                    RectProps(op, op.Pieces);
                    Levels("Open from", op.BottomLevelId, id => op.BottomLevelId = id);
                    Levels("Open to", op.TopLevelId, id => op.TopLevelId = id);
                    Hint("It cuts every level strictly between: a lobby from the ground to level 3 cuts level 2.");
                    break;
                case Beam bm:
                    Title($"Beam {bm.Id}");
                    Number("Start x", bm.Start.X, v => bm.Start = new Point2(v, bm.Start.Y));
                    Number("Start y", bm.Start.Y, v => bm.Start = new Point2(bm.Start.X, v));
                    Number("End x", bm.End.X, v => bm.End = new Point2(v, bm.End.Y));
                    Number("End y", bm.End.Y, v => bm.End = new Point2(bm.End.X, v));
                    Levels("Level", bm.LevelId, id => bm.LevelId = id);
                    Hint($"Span {bm.Length:0.##} ft. It carries the hold-downs and posts of the story above that land on it to its ends; Shear Walls sizes the end reactions and the hold-downs under them.");
                    break;
                case Column col:
                    Title($"Column {col.Id}");
                    Number("x", col.At.X, v => col.At = new Point2(v, col.At.Y));
                    Number("y", col.At.Y, v => col.At = new Point2(col.At.X, v));
                    var auto = new CheckBox { Content = "Base: stands on what is under it", IsChecked = !col.BaseFixed, Margin = new Thickness(0, 2, 0, 4),
                                              ToolTip = "Automatic: it stops on the first wall, beam or column under it, else the foundation, and follows as they are drawn and deleted. Off: the base below stays as set (a post that runs through)." };
                    auto.Click += (s, e) => sketch.Edit(auto.IsChecked == true ? $"{col.Id}'s base follows what is under it." : $"{col.Id}'s base is set by hand.", b => col.BaseFixed = auto.IsChecked != true);
                    Children.Add(auto);
                    Levels("Base", col.BaseLevelId, id => { col.BaseLevelId = id; col.BaseFixed = true; });
                    Levels("Top", col.TopLevelId, id => col.TopLevelId = id);
                    var through = BuildingEdits.ThroughLevels(B, col);
                    var baseLevel = B.Level(col.BaseLevelId);
                    string on = baseLevel == null ? "" : baseLevel.Elevation == 0 ? "the foundation" : $"{BuildingEdits.SupportAt(B, baseLevel, col.At, col.Id) ?? "nothing"} at {baseLevel.Name}";
                    Hint($"From {on} up to {B.Level(col.TopLevelId)?.Name}" + (through.Count > 0 ? $", through {string.Join(", ", through.Select(l => l.Name))}" : "") +
                         ". It carries what lands on it down to its base.");
                    break;
                case Note n:
                    Title("Note " + n.Id);
                    var t = new TextBox { Text = n.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, Margin = new Thickness(0, 4, 0, 0) };
                    t.LostFocus += (s, e) => { if (t.Text != n.Text) sketch.Edit($"Note {n.Id} changed.", b => n.Text = t.Text); };
                    Children.Add(t);
                    break;
                default: LevelProps(sketch.ActiveLevel); break;
            }
        }

        private void WallProps(Wall w)
        {
            Title($"Wall {w.Id}");
            var cb = new CheckBox { Content = "Shear wall", IsChecked = w.IsShearWall, Margin = new Thickness(0, 2, 0, 6) };
            cb.Click += (s, e) => sketch.Edit($"{w.Id} {(cb.IsChecked == true ? "is a shear wall" : "is not a shear wall")}.", b => w.IsShearWall = cb.IsChecked == true);
            Children.Add(cb);
            Number("Start x", w.Start.X, v => w.Start = new Point2(v, w.Start.Y));
            Number("Start y", w.Start.Y, v => w.Start = new Point2(w.Start.X, v));
            Number("End x", w.End.X, v => w.End = new Point2(v, w.End.Y));
            Number("End y", w.End.Y, v => w.End = new Point2(w.End.X, v));
            Number("Length", w.Length, v =>
            {
                if (!(v > 0)) return;
                double k = v / w.Length;
                w.End = new Point2(w.Start.X + (w.End.X - w.Start.X) * k, w.Start.Y + (w.End.Y - w.Start.Y) * k);
            });
            Levels("Base", w.BaseLevelId, id => w.BaseLevelId = id);
            Levels("Top", w.TopLevelId, id => w.TopLevelId = id);
            var lo = B.Level(w.BaseLevelId);
            var hi = B.Level(w.TopLevelId);
            if (lo != null && hi != null) Hint($"Height {hi.Elevation - lo.Elevation:0.##} ft; unbraced {B.UnbracedHeight(w):0.##} ft.");
        }

        private void RectProps(ModelObject o, List<Rect2> pieces)
        {
            Title($"{(o is Opening ? "Opening" : "Diaphragm")} {o.Id}");
            if (pieces.Count == 0) return;
            var r = pieces[0];
            Number("West x", r.X0, v => pieces[0] = new Rect2(v, pieces[0].Y0, pieces[0].X1, pieces[0].Y1));
            Number("South y", r.Y0, v => pieces[0] = new Rect2(pieces[0].X0, v, pieces[0].X1, pieces[0].Y1));
            Number("Width", r.Width, v => { if (v > 0) pieces[0] = new Rect2(pieces[0].X0, pieces[0].Y0, pieces[0].X0 + v, pieces[0].Y1); });
            Number("Depth", r.Height, v => { if (v > 0) pieces[0] = new Rect2(pieces[0].X0, pieces[0].Y0, pieces[0].X1, pieces[0].Y0 + v); });
            Hint($"{Region.Of(pieces).Area:0} sq ft" + (pieces.Count > 1 ? $" in {pieces.Count} rectangles" : ""));
        }

        /// <summary>Which way the diaphragm's joists or rafters span (the gravity load path's tributary widths).</summary>
        private void SpanProps(Diaphragm d)
        {
            var roof = B.RoofLevel();
            bool slopedRoof = roof != null && d.LevelId == roof.Id && B.Roof != null && B.Roof.IsSloped;
            string Dir(SpanDirection s) => s == SpanDirection.WestEast ? "W-E" : "S-N";
            var c = new ComboBox { Padding = new Thickness(3, 1, 3, 1), IsEnabled = !slopedRoof };
            var choices = new[]
            {
                (SpanDirection.Auto, $"Auto ({Dir(new Diaphragm { Pieces = d.Pieces }.EffectiveSpan)}, the shorter way)"),
                (SpanDirection.WestEast, "W-E (bearing on N-S walls)"),
                (SpanDirection.SouthNorth, "S-N (bearing on W-E walls)"),
            };
            foreach (var (s, text) in choices) c.Items.Add(new ComboBoxItem { Content = text, Tag = s, IsSelected = s == d.Span });
            c.SelectionChanged += (o, e) =>
            {
                if (c.SelectedItem is ComboBoxItem i && (SpanDirection)i.Tag != d.Span)
                    sketch.Edit($"{d.Id} spans {((SpanDirection)i.Tag == SpanDirection.Auto ? "the shorter way" : Dir((SpanDirection)i.Tag))}.", b => BuildingEdits.SetDiaphragmSpan(b, d.Id, (SpanDirection)i.Tag));
            };
            System.Windows.Automation.AutomationProperties.SetName(c, "Span");
            Row("Span", c);
            Hint(slopedRoof
                ? $"The sloped roof's rafters span across the ridge ({(B.EffectiveRidge() == RidgeAxis.WestEast ? "S-N" : "W-E")}); this setting is not used."
                : "Which way the joists or rafters run: each bearing wall or beam takes half the span on each side.");
        }

        private void LevelProps(Level l)
        {
            if (l == null) return;
            Title(l.Name);
            Text("Name", l.Name, v => l.Name = v);
            if (l.Elevation == 0) { Hint("The ground (elevation 0): walls are drawn on the levels above it."); return; }
            Number("Elevation", l.Elevation, v => l.Elevation = v);
            var h = B.StoryHeight(l);
            Number("Story height", h, v => BuildingEdits.SetStoryHeight(B, l, v), "the levels above move with it");
            var net = B.NetDiaphragm(l);
            Hint($"Diaphragm {net.Area:0} sq ft net; {B.WallsUnder(l).Count(w => w.IsShearWall)} shear walls under it.");
        }

        // ------------------------------------------------------------------ rows

        private void Title(string text) =>
            Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });

        private void Hint(string text) =>
            Children.Add(new TextBlock { Text = text, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 11 });

        private Grid Row(string label, UIElement editor)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted });
            Grid.SetColumn(editor, 1);
            g.Children.Add(editor);
            Children.Add(g);
            return g;
        }

        private void Number(string label, double value, Action<double> set, string tip = null)
        {
            var t = new TextBox { Text = value.ToString("0.###", CultureInfo.InvariantCulture), Padding = new Thickness(2, 1, 2, 1), ToolTip = tip ?? "ft; 12'6\" works too" };
            void Apply()
            {
                if (!CoordinateInput.TryLength(t.Text, out double v)) { t.Text = value.ToString("0.###", CultureInfo.InvariantCulture); sketch.Say($"\"{t.Text}\" is not a length.", true); return; }
                if (Math.Abs(v - value) < 1e-9) return;
                filling = true;
                try { set(v); }
                finally { filling = false; }
                sketch.Commit($"{label} set to {v:0.###} ft.");
                Fill();
            }
            t.LostFocus += (s, e) => Apply();
            t.KeyDown += (s, e) => { if (e.Key == Key.Enter) Apply(); };
            Row(label, t);
        }

        private void Text(string label, string value, Action<string> set)
        {
            var t = new TextBox { Text = value ?? "", Padding = new Thickness(2, 1, 2, 1) };
            void Apply() { if (t.Text != value && t.Text.Trim().Length > 0) sketch.Edit($"{label} set.", b => set(t.Text.Trim())); }
            t.LostFocus += (s, e) => Apply();
            t.KeyDown += (s, e) => { if (e.Key == Key.Enter) Apply(); };
            Row(label, t);
        }

        private void Levels(string label, string current, Action<string> set)
        {
            var c = new ComboBox { Padding = new Thickness(3, 1, 3, 1) };
            foreach (var l in B.LevelsBottomUp) c.Items.Add(new ComboBoxItem { Content = l.ToString(), Tag = l.Id, IsSelected = l.Id == current });
            c.SelectionChanged += (s, e) =>
            {
                if (c.SelectedItem is ComboBoxItem i && (string)i.Tag != current)
                    sketch.Edit($"{label} set to {B.Level((string)i.Tag)?.Name}.", b => set((string)i.Tag));
            };
            Row(label, c);
        }
    }
}
