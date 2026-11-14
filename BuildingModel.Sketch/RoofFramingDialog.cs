using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildingModel.Checks;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using BuildingModel.Statics;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The roof's framing (owner 2026-11-12: hips, valleys, wings, dormers, purlins): its sections (each a rectangle of walls
    /// on plan, a gable, hip or shed of its own pitch, a dormer's eave above the roof below), its purlins (a line across the
    /// rafters, braced to a bearing wall), and its members' sizes; a plan of what that frames to (the faces, the ridges, hips,
    /// valleys and headers, every rafter, the purlins and their braces) and what is wrong, as you type. Built in code so it can
    /// be tested without a window; the building is not changed (<see cref="Roof"/> is the result).
    /// </summary>
    public sealed class RoofFramingPanel : DockPanel
    {
        private readonly Building building;
        private readonly Roof roof;
        private readonly List<RoofSection> sections;
        private readonly List<RoofPurlin> purlins;
        private bool sectionsTouched;
        private readonly ListBox sectionList, purlinList;
        private readonly TextBox sName, sX0, sY0, sX1, sY1, sPitch, sRise, sEaves, pId, pX0, pY0, pX1, pY1, pSpacing;
        private readonly ComboBox sShape, sRidge, sLow, pBraceTo, hipSize, valleySize, headerSize, cheekBeamSize, ridgeBoardSize, purlinSize, braceSize;
        private readonly Canvas plan;
        private readonly TextBlock issues;
        private bool loading;

        public event EventHandler Changed;

        /// <summary>The roof as framed here (a copy of the building's with its sections, purlins and sizes).</summary>
        public Roof Roof { get; private set; }
        public bool HasErrors { get; private set; }
        /// <summary>What the plan shows, for tests: faces, creases, rafters, purlins, braces.</summary>
        public int Faces { get; private set; }
        public int Creases { get; private set; }
        public int Rafters { get; private set; }

        public RoofFramingPanel(Building b, Roof r = null)
        {
            building = b ?? throw new ArgumentNullException(nameof(b));
            roof = (r ?? b.Roof ?? new Roof()).Clone();
            sections = roof.Sections != null && roof.Sections.Count > 0 ? roof.Sections.Select(x => x.Clone()).ToList() : Derived();
            sectionsTouched = roof.Sections != null && roof.Sections.Count > 0;
            purlins = (roof.Purlins ?? new List<RoofPurlin>()).Select(x => x.Clone()).ToList();
            Margin = new Thickness(12);

            var left = new StackPanel { Width = 390, Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(left, Dock.Left);
            Children.Add(left);

            // the sections
            left.Children.Add(Head("Sections"));
            left.Children.Add(Note("Each a rectangle of walls on plan; the roof is the highest section at each point: where two meet, a valley. A dormer's eave stands above the roof below (its eave rise)."));
            sectionList = new ListBox { Height = 96, Margin = new Thickness(0, 2, 0, 4) };
            System.Windows.Automation.AutomationProperties.SetName(sectionList, "Roof sections");
            sectionList.SelectionChanged += (s, e) => ShowSection();
            left.Children.Add(sectionList);
            left.Children.Add(Buttons(("Add section", AddSection), ("Add dormer", AddDormer), ("Remove", RemoveSection)));
            sName = Box(140); sX0 = Box(); sY0 = Box(); sX1 = Box(); sY1 = Box(); sPitch = Box(); sRise = Box(); sEaves = Box();
            sShape = Combo(new[] { "Gable", "Hip", "Shed" });
            sRidge = Combo(new[] { "Along the longer side", "West-East", "South-North" });
            sLow = Combo(new[] { "South", "North", "West", "East" });
            left.Children.Add(Row("Name", sName));
            left.Children.Add(Row("Walls", Line(Label("x"), sX0, Label("y"), sY0, Label("to x"), sX1, Label("y"), sY1, Label("ft"))));
            left.Children.Add(Row("Shape", Line(sShape, Label("  ridge"), sRidge)));
            left.Children.Add(Row("Shed low", sLow));
            left.Children.Add(Row("Slope", Line(sPitch, Label(": 12 (blank: the roof's)"))));
            left.Children.Add(Row("Eave rise", Line(sRise, Label("ft above its walls' tops (a dormer's eave over the roof; a wing lower or higher: its diaphragm's height offset)"))));
            left.Children.Add(Row("Eaves", Line(sEaves, Label("ft (blank: the roof's)"))));
            foreach (var t in new[] { sName, sX0, sY0, sX1, sY1, sPitch, sRise, sEaves }) t.TextChanged += (s, e) => SectionEdited();
            foreach (var c in new[] { sShape, sRidge, sLow }) c.SelectionChanged += (s, e) => SectionEdited();

            // the purlins
            left.Children.Add(Head("Purlins"));
            left.Children.Add(Note("A line on plan across the rafters, each rafter crossing it held there; braced at its spacing (4 ft at most) to the bearing wall under it, 45° or steeper (IRC R802.4.5)."));
            purlinList = new ListBox { Height = 64, Margin = new Thickness(0, 2, 0, 4) };
            System.Windows.Automation.AutomationProperties.SetName(purlinList, "Purlins");
            purlinList.SelectionChanged += (s, e) => ShowPurlin();
            left.Children.Add(purlinList);
            left.Children.Add(Buttons(("Add purlin", AddPurlin), ("Remove", RemovePurlin)));
            pId = Box(60); pX0 = Box(); pY0 = Box(); pX1 = Box(); pY1 = Box(); pSpacing = Box();
            pBraceTo = new ComboBox { Width = 160 };
            pBraceTo.Items.Add("(the nearest under it)");
            foreach (var w in building.Walls) pBraceTo.Items.Add(w.Id);
            left.Children.Add(Row("Name", pId));
            left.Children.Add(Row("Line", Line(Label("x"), pX0, Label("y"), pY0, Label("to x"), pX1, Label("y"), pY1, Label("ft"))));
            left.Children.Add(Row("Braces", Line(Label("at"), pSpacing, Label("ft (blank: 4) to"), pBraceTo)));
            foreach (var t in new[] { pId, pX0, pY0, pX1, pY1, pSpacing }) t.TextChanged += (s, e) => PurlinEdited();
            pBraceTo.SelectionChanged += (s, e) => PurlinEdited();

            // the members' sizes
            left.Children.Add(Head("Sizes"));
            hipSize = SizeCombo("(not sized)", roof.HipSize); valleySize = SizeCombo("(not sized)", roof.ValleySize); headerSize = SizeCombo("(not sized)", roof.HeaderSize);
            cheekBeamSize = SizeCombo("(not sized)", roof.CheekBeamSize);
            ridgeBoardSize = SizeCombo("(not sized)", roof.RidgeBoardSize); purlinSize = SizeCombo("(the rafters')", roof.PurlinSize);
            braceSize = new ComboBox { Width = 120 };
            foreach (var z in new[] { "(2x4)", "2x4", "2x6" }) braceSize.Items.Add(z);
            braceSize.SelectedIndex = roof.BraceSize == "2x6" ? 2 : roof.BraceSize == "2x4" ? 1 : 0;
            left.Children.Add(Row("Hips", Line(hipSize, Label("  valleys"), valleySize)));
            left.Children.Add(Row("Headers", Line(headerSize, Label("  ridge boards"), ridgeBoardSize)));
            left.Children.Add(Row("Dormers", Line(Label("cheek beams"), cheekBeamSize)));
            left.Children.Add(Row("Purlins", Line(purlinSize, Label("  braces"), braceSize)));
            foreach (var c in new[] { hipSize, valleySize, headerSize, cheekBeamSize, ridgeBoardSize, purlinSize, braceSize }) c.SelectionChanged += (s, e) => Update();
            left.Children.Add(Note("Not sized: NOT DESIGNED, the lightest that works named (the joist window)."));

            // the plan and what is wrong
            var right = new DockPanel();
            issues = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 520 };
            DockPanel.SetDock(issues, Dock.Bottom);
            right.Children.Add(issues);
            var legend = Note("Faces shaded by section; ridges red, hips orange, valleys blue, headers purple; rafters grey; purlins green with their braces; walls dark.");
            DockPanel.SetDock(legend, Dock.Bottom);
            right.Children.Add(legend);
            plan = new Canvas { Width = 520, Height = 420, Background = Brushes.White, ClipToBounds = true };
            right.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = plan });
            Children.Add(right);

            loading = true;
            RefreshLists();
            loading = false;
            if (sections.Count > 0) sectionList.SelectedIndex = 0;
            if (purlins.Count > 0) purlinList.SelectedIndex = 0;
            Update();
        }

        /// <summary>The one section the roof is without its own: its shape and ridge over the plan (a gable or hip).</summary>
        private List<RoofSection> Derived()
        {
            var trial = ModelFile.Clone(building);
            trial.Roof = roof.Clone();
            trial.Roof.Sections = null;
            var list = RoofFrame.SectionsOf(trial);
            foreach (var s in list) s.Name = "main";
            return list;
        }

        // ------------------------------------------------------------ the sections
        private void AddSection()
        {
            var plan2 = building.RoofPlan();
            var s = new RoofSection { Name = $"wing {sections.Count}", Walls = plan2.IsEmpty ? new Rect2(0, 0, 20, 20) : new Rect2(plan2.X0, plan2.Y0 - 12, plan2.X0 + 12, plan2.Y0 + 4), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth };
            sections.Add(s); sectionsTouched = true;
            RefreshLists(); sectionList.SelectedIndex = sections.Count - 1; Update();
        }

        private void AddDormer()
        {
            var plan2 = building.RoofPlan();
            double cx = plan2.IsEmpty ? 10 : (plan2.X0 + plan2.X1) / 2, y0 = plan2.IsEmpty ? 2 : plan2.Y0 + 2;
            var s = new RoofSection { Name = $"dormer {sections.Count}", Walls = new Rect2(cx - 4, y0, cx + 4, y0 + 7), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth, EaveRise = 2 };
            sections.Add(s); sectionsTouched = true;
            RefreshLists(); sectionList.SelectedIndex = sections.Count - 1; Update();
        }

        private void RemoveSection()
        {
            int i = sectionList.SelectedIndex;
            if (i < 0 || i >= sections.Count || sections.Count == 1) return;
            sections.RemoveAt(i); sectionsTouched = true;
            RefreshLists(); sectionList.SelectedIndex = Math.Min(i, sections.Count - 1); Update();
        }

        private void ShowSection()
        {
            if (loading) return;
            int i = sectionList.SelectedIndex;
            if (i < 0 || i >= sections.Count) return;
            var s = sections[i];
            loading = true;
            sName.Text = s.Name ?? "";
            sX0.Text = Num(s.Walls.X0); sY0.Text = Num(s.Walls.Y0); sX1.Text = Num(s.Walls.X1); sY1.Text = Num(s.Walls.Y1);
            sShape.SelectedIndex = s.Shape == RoofShape.Hip ? 1 : s.Shape == RoofShape.Shed ? 2 : 0;
            sRidge.SelectedIndex = (int)s.Ridge;
            sLow.SelectedIndex = (int)s.Low;
            sPitch.Text = s.Pitch > 0 && s.Pitch < 90 ? Num(12 * Math.Tan(s.Pitch * Math.PI / 180)) : "";
            sRise.Text = Math.Abs(s.EaveRise) > 1e-9 ? Num(s.EaveRise) : "";
            sEaves.Text = s.Overhang.HasValue ? Num(s.Overhang.Value) : "";
            loading = false;
        }

        private void SectionEdited()
        {
            if (loading) return;
            int i = sectionList.SelectedIndex;
            if (i < 0 || i >= sections.Count) return;
            var s = sections[i];
            s.Name = string.IsNullOrWhiteSpace(sName.Text) ? null : sName.Text.Trim();
            double x0 = Parse(sX0.Text), y0 = Parse(sY0.Text), x1 = Parse(sX1.Text), y1 = Parse(sY1.Text);
            if (!new[] { x0, y0, x1, y1 }.Any(double.IsNaN)) s.Walls = new Rect2(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
            s.Shape = sShape.SelectedIndex == 1 ? RoofShape.Hip : sShape.SelectedIndex == 2 ? RoofShape.Shed : RoofShape.Gable;
            s.Ridge = (RidgeAxis)Math.Max(0, sRidge.SelectedIndex);
            s.Low = (RoofSide)Math.Max(0, sLow.SelectedIndex);
            double p = RoofPanel.ParseSlope(sPitch.Text);
            s.Pitch = string.IsNullOrWhiteSpace(sPitch.Text) || double.IsNaN(p) ? 0 : p;
            double rise = Parse(sRise.Text);
            s.EaveRise = double.IsNaN(rise) ? 0 : rise;   // a dormer's up; a wing's plate may sit higher or lower than the main roof's
            double eaves = Parse(sEaves.Text);
            s.Overhang = string.IsNullOrWhiteSpace(sEaves.Text) || double.IsNaN(eaves) ? (double?)null : Math.Max(0, eaves);
            sectionsTouched = true;
            loading = true;
            sectionList.Items[i] = s.ToString();
            sectionList.SelectedIndex = i;
            loading = false;
            Update();
        }

        // ------------------------------------------------------------ the purlins
        private void AddPurlin()
        {
            var plan2 = building.RoofPlan();
            var p = new RoofPurlin { Id = $"PL{purlins.Count + 1}", Start = new Point2(plan2.X0, plan2.Y0 + plan2.Height / 4), End = new Point2(plan2.X1, plan2.Y0 + plan2.Height / 4) };
            purlins.Add(p);
            RefreshLists(); purlinList.SelectedIndex = purlins.Count - 1; Update();
        }

        private void RemovePurlin()
        {
            int i = purlinList.SelectedIndex;
            if (i < 0 || i >= purlins.Count) return;
            purlins.RemoveAt(i);
            RefreshLists(); purlinList.SelectedIndex = Math.Min(i, purlins.Count - 1); Update();
        }

        private void ShowPurlin()
        {
            if (loading) return;
            int i = purlinList.SelectedIndex;
            if (i < 0 || i >= purlins.Count) return;
            var p = purlins[i];
            loading = true;
            pId.Text = p.Id ?? "";
            pX0.Text = Num(p.Start.X); pY0.Text = Num(p.Start.Y); pX1.Text = Num(p.End.X); pY1.Text = Num(p.End.Y);
            pSpacing.Text = p.BraceSpacing > 0 ? Num(p.BraceSpacing) : "";
            pBraceTo.SelectedIndex = p.BraceTo == null ? 0 : Math.Max(0, pBraceTo.Items.IndexOf(p.BraceTo));
            loading = false;
        }

        private void PurlinEdited()
        {
            if (loading) return;
            int i = purlinList.SelectedIndex;
            if (i < 0 || i >= purlins.Count) return;
            var p = purlins[i];
            p.Id = string.IsNullOrWhiteSpace(pId.Text) ? null : pId.Text.Trim();
            double x0 = Parse(pX0.Text), y0 = Parse(pY0.Text), x1 = Parse(pX1.Text), y1 = Parse(pY1.Text);
            if (!new[] { x0, y0, x1, y1 }.Any(double.IsNaN)) { p.Start = new Point2(x0, y0); p.End = new Point2(x1, y1); }
            double sp = Parse(pSpacing.Text);
            p.BraceSpacing = double.IsNaN(sp) ? 0 : Math.Max(0, sp);
            p.BraceTo = pBraceTo.SelectedIndex > 0 ? (string)pBraceTo.SelectedItem : null;
            loading = true;
            purlinList.Items[i] = p.ToString();
            purlinList.SelectedIndex = i;
            loading = false;
            Update();
        }

        private void RefreshLists()
        {
            bool was = loading;
            loading = true;
            sectionList.Items.Clear();
            foreach (var s in sections) sectionList.Items.Add(s.ToString());
            purlinList.Items.Clear();
            foreach (var p in purlins) purlinList.Items.Add(p.ToString());
            loading = was;
        }

        // ------------------------------------------------------------ the result and its plan
        private void Update()
        {
            if (loading) return;
            var r = roof.Clone();
            r.Sections = sectionsTouched ? sections.Select(x => x.Clone()).ToList() : roof.Sections;
            r.Purlins = purlins.Count == 0 ? null : purlins.Select(x => x.Clone()).ToList();
            string Size(ComboBox c) => c.SelectedIndex > 0 ? (string)c.SelectedItem : null;
            r.HipSize = Size(hipSize); r.ValleySize = Size(valleySize); r.HeaderSize = Size(headerSize); r.CheekBeamSize = Size(cheekBeamSize);
            r.RidgeBoardSize = Size(ridgeBoardSize); r.PurlinSize = Size(purlinSize); r.BraceSize = Size(braceSize);
            Roof = r;

            var trial = ModelFile.Clone(building);
            trial.Roof = r.Clone();
            var found = ModelChecks.Run(trial).Where(i => i.Code.StartsWith("Roof", StringComparison.Ordinal)).ToList();
            HasErrors = found.Any(i => i.Severity == Severity.Error);
            issues.Inlines.Clear();
            foreach (var i in found)
                issues.Inlines.Add(new System.Windows.Documents.Run((i.Severity == Severity.Error ? "✗ " : "▲ ") + i.Message + "\n") { Foreground = i.Severity == Severity.Error ? Ui.Error : Ui.Warning });
            Draw(trial);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static readonly Color[] Tints = { Color.FromRgb(0xE8, 0xF0, 0xFA), Color.FromRgb(0xFB, 0xEE, 0xDC), Color.FromRgb(0xE4, 0xF4, 0xE6), Color.FromRgb(0xF3, 0xE6, 0xF5), Color.FromRgb(0xFA, 0xF6, 0xD8) };

        private void Draw(Building trial)
        {
            plan.Children.Clear();
            Faces = Creases = Rafters = 0;
            var top = trial.RoofLevel();
            if (top == null || trial.Roof == null || !trial.Roof.IsSloped) { plan.Children.Add(new TextBlock { Text = "A flat roof: no framing to lay out here.", Margin = new Thickness(10) }); return; }
            // the plan's box: the sections' eaves and the walls under the roof
            var frame = RoofFrame.Of(trial, RoofFrame.SectionsOf(trial), trial.Roof.Pitch, Math.Max(0, trial.Roof.Overhang), Joists.SpacingOf(trial, top));
            var walls = BeamSupports.WallsUnder(trial, top);
            var pts = frame.Faces.SelectMany(f => f.Pieces).SelectMany(p => p.P).Concat(walls.SelectMany(w => new[] { w.Start, w.End }))
                           .Concat(RoofFrame.SectionsOf(trial).SelectMany(s => new[] { new Point2(s.Walls.X0, s.Walls.Y0), new Point2(s.Walls.X1, s.Walls.Y1) })).ToList();
            if (pts.Count == 0) return;
            double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
            double k = Math.Min((plan.Width - 30) / Math.Max(1, x1 - x0), (plan.Height - 30) / Math.Max(1, y1 - y0));
            Point P(Point2 p) => new Point(15 + (p.X - x0) * k, plan.Height - 15 - (p.Y - y0) * k);
            void Seg(Point2 a, Point2 b, Brush br, double w, DoubleCollection dash = null)
            {
                var pa = P(a); var pb = P(b);
                plan.Children.Add(new System.Windows.Shapes.Line { X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y, Stroke = br, StrokeThickness = w, StrokeDashArray = dash });
            }
            if (frame.NotLaidOut != null)
            {
                foreach (var s in RoofFrame.SectionsOf(trial))
                {
                    var c = s.Walls;
                    var pg = new Polygon { Stroke = Ui.Error, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
                    foreach (var q in new[] { new Point2(c.X0, c.Y0), new Point2(c.X1, c.Y0), new Point2(c.X1, c.Y1), new Point2(c.X0, c.Y1) }) pg.Points.Add(P(q));
                    plan.Children.Add(pg);
                }
                return;
            }
            foreach (var f in frame.Faces)
            {
                var tint = new SolidColorBrush(Tints[Math.Abs(f.Plane.Section) % Tints.Length]);
                foreach (var pc in f.Pieces)
                {
                    var pg = new Polygon { Fill = tint, Stroke = Ui.Line, StrokeThickness = 0.5 };
                    foreach (var q in pc.P) pg.Points.Add(P(q));
                    plan.Children.Add(pg);
                }
                Faces++;
            }
            foreach (var r in frame.Rafters) { Seg(r.Start, r.End, Brushes.Gray, 0.6); Rafters++; }
            foreach (var w in walls) Seg(w.Start, w.End, Ui.Ink, 2.5);
            foreach (var c in frame.Creases)
            {
                var br = c.Kind == CreaseKind.Ridge ? Brushes.Firebrick : c.Kind == CreaseKind.Hip ? Brushes.DarkOrange : c.Kind == CreaseKind.Valley ? Brushes.RoyalBlue : Brushes.Purple;
                Seg(c.A, c.B, br, 2);
                Creases++;
            }
            foreach (var l in frame.Lines.Where(l => l.Kind == CreaseKind.Header)) { Seg(l.A, l.B, Brushes.Purple, 2.5); Creases++; }
            var st = RoofStatics.Of(trial, top, frame);
            foreach (var m in st.Members.Where(x => x.Kind == "purlin"))
            {
                Seg(m.Start, m.End, Brushes.ForestGreen, 3);
                foreach (var s in m.Supports.Where(x => x.IsBrace)) Seg(m.At(s.At), s.Where, Brushes.ForestGreen, 1.2, new DoubleCollection { 3, 2 });
                foreach (var s in m.Supports.Where(x => x.Kind == RoofTargetKind.None))
                {
                    var c = P(s.Where);
                    var e = new Ellipse { Width = 8, Height = 8, Stroke = Ui.Error, StrokeThickness = 1.5 };
                    Canvas.SetLeft(e, c.X - 4); Canvas.SetTop(e, c.Y - 4);
                    plan.Children.Add(e);
                }
            }
        }

        // ------------------------------------------------------------ small pieces
        /// <summary>For tests: types into a field as a person would ("x0", "y0", "x1", "y1", "pitch", "rise", "eaves", "name"; "p.x0", ... for the purlin).</summary>
        public void Type(string field, string text)
        {
            var map = new Dictionary<string, TextBox>
            {
                ["name"] = sName, ["x0"] = sX0, ["y0"] = sY0, ["x1"] = sX1, ["y1"] = sY1, ["pitch"] = sPitch, ["rise"] = sRise, ["eaves"] = sEaves,
                ["p.name"] = pId, ["p.x0"] = pX0, ["p.y0"] = pY0, ["p.x1"] = pX1, ["p.y1"] = pY1, ["p.spacing"] = pSpacing,
            };
            map[field].Text = text;
        }

        public void ClickAddSection() => AddSection();
        public void ClickAddDormer() => AddDormer();
        public void ClickAddPurlin() => AddPurlin();
        public void SelectSection(int i) => sectionList.SelectedIndex = i;
        public void SetShape(RoofShape s) => sShape.SelectedIndex = s == RoofShape.Hip ? 1 : s == RoofShape.Shed ? 2 : 0;
        public void SetRidge(RidgeAxis r) => sRidge.SelectedIndex = (int)r;
        public void SetHipSize(string size) => hipSize.SelectedItem = size;

        private ComboBox SizeCombo(string none, string value)
        {
            var c = new ComboBox { Width = 120 };
            c.Items.Add(none);
            foreach (var z in BeamSizes.All) c.Items.Add(z);
            c.SelectedIndex = Math.Max(0, Array.IndexOf(BeamSizes.All, value) + 1);
            return c;
        }

        private static ComboBox Combo(string[] items)
        {
            var c = new ComboBox { Width = 150 };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedIndex = 0;
            return c;
        }

        private static TextBox Box(double width = 44) => new TextBox { Width = width, Padding = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        private static TextBlock Label(string t) => new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0), Foreground = Ui.Muted };
        private static TextBlock Head(string t) => new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2), Foreground = Ui.Ink };
        private static TextBlock Note(string t) => new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(0, 0, 0, 2) };

        private static FrameworkElement Line(params FrameworkElement[] parts)
        {
            var sp = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var p in parts) sp.Children.Add(p);
            return sp;
        }

        private static FrameworkElement Row(string label, FrameworkElement input)
        {
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var caption = new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Ink };
            DockPanel.SetDock(caption, Dock.Left);
            d.Children.Add(caption);
            d.Children.Add(input);
            return d;
        }

        private static FrameworkElement Buttons(params (string text, Action click)[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var (text, click) in items)
            {
                var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
                b.Click += (s, e) => click();
                sp.Children.Add(b);
            }
            return sp;
        }

        private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.##", CultureInfo.CurrentCulture);
        private static double Parse(string s) => double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double v) ? v : double.NaN;
    }

    /// <summary>The roof framing window: the <see cref="RoofFramingPanel"/> with OK and Cancel; the roof framed, or null when cancelled.</summary>
    public static class RoofFramingDialog
    {
        public static Roof Ask(Building b, Roof r, Window owner = null)
        {
            var panel = new RoofFramingPanel(b, r);
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var root = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            ok.IsEnabled = !panel.HasErrors;
            panel.Changed += (s, e) => ok.IsEnabled = !panel.HasErrors;
            var w = new Window
            {
                Title = "Roof framing", Content = root, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false, Owner = owner ?? Application.Current?.MainWindow, Background = Brushes.White,
            };
            WindowFit.Fit(w);
            ok.Click += (s, e) => w.DialogResult = true;
            return w.ShowDialog() == true ? panel.Roof : null;
        }
    }
}
