using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildingModel.Checks;

namespace BuildingModel.Sketch
{
    /// <summary>One layer of a plan the engineer can turn on or off (the host's: hold-downs, wind loads, top loads…).</summary>
    public sealed class ViewLayer
    {
        public ViewLayer(string key, string name, Func<bool> get, Action<bool> set, string tip = null)
        {
            Key = key; Name = name; Get = get; Set = set; Tip = tip;
        }

        /// <summary>Its key in a saved view (kept the same across versions).</summary>
        public string Key { get; }
        public string Name { get; }
        public string Tip { get; }
        public Func<bool> Get { get; }
        public Action<bool> Set { get; }
    }

    /// <summary>
    /// A view mode (owner 2026-11-14, UI audit A1: RISA's plot options as one-click presets): the layers it turns on (the
    /// rest off), its label density and status filter, and anything else the host does (go to the foundation plan).
    /// </summary>
    public sealed class ViewMode
    {
        public ViewMode(string name, string tip, params string[] on)
        {
            Name = name; Tip = tip; On = new HashSet<string>(on ?? new string[0]);
        }

        public string Name { get; }
        public string Tip { get; }
        public HashSet<string> On { get; }
        public LabelDensity? Labels { get; set; }
        public StatusFilter? Status { get; set; }
        /// <summary>The host's own part (another page, the foundation plan); null: none.</summary>
        public Action Extra { get; set; }
    }

    /// <summary>
    /// The plan's view bar, the same in every app (owner 2026-11-14, UI audit A1, A2): View (the modes), Layers ▾ (each layer,
    /// the status filter, the label density) and Views ▾ (the saved views: apply, save this view, delete). A saved view is
    /// the app's own data in the project (<see cref="SavedViews"/>), written by the host as one undo step.
    /// </summary>
    public sealed class ViewBar : StackPanel
    {
        private readonly SketchCanvas sketch;
        private readonly ComboBox modes = new ComboBox { MinWidth = 110, Padding = new Thickness(6, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
        private readonly Button layersButton = new Button { Content = "Layers ▾", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), Focusable = false };
        private readonly Button viewsButton = new Button { Content = "Views ▾", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), Focusable = false };
        private bool filling;
        private string mode;

        public ViewBar(SketchCanvas sketch)
        {
            this.sketch = sketch;
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;
            Children.Add(new TextBlock { Text = "View", Foreground = Ui.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            Children.Add(modes);
            Children.Add(layersButton);
            Children.Add(viewsButton);
            System.Windows.Automation.AutomationProperties.SetName(modes, "View mode");
            modes.SelectionChanged += (s, e) => { if (!filling && modes.SelectedItem is ComboBoxItem i) Apply((string)i.Tag); };
            layersButton.Click += (s, e) => LayersMenu().IsOpen = true;
            viewsButton.Click += (s, e) => ViewsMenu().IsOpen = true;
            layersButton.ToolTip = "Each layer on or off, which states are drawn in colour, and the labels";
            viewsButton.ToolTip = "Saved views: name this view (its layers, filter and labels) to come back to it; saved with the project";
            Fill();
        }

        public List<ViewLayer> Layers { get; } = new List<ViewLayer>();
        public List<ViewMode> Modes { get; } = new List<ViewMode>();

        /// <summary>The app's own saved views in the project (the host's; null: no Views button).</summary>
        public Func<SavedViews> ReadViews { get; set; }

        /// <summary>Writes the views (one undo step: the host's), with what changed in words.</summary>
        public Action<SavedViews, string> WriteViews { get; set; }

        /// <summary>The layers, the filter or the labels changed: the host redraws and sets its own buttons.</summary>
        public event EventHandler Changed;

        /// <summary>The mode shown now (null: none picked yet).</summary>
        public string Mode => mode;

        /// <summary>The modes and layers given: the box filled again.</summary>
        public void Fill()
        {
            filling = true;
            try
            {
                modes.Items.Clear();
                foreach (var m in Modes) modes.Items.Add(new ComboBoxItem { Content = m.Name, Tag = m.Name, ToolTip = m.Tip });
                modes.SelectedItem = modes.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == mode);
                modes.Visibility = Modes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ((UIElement)Children[0]).Visibility = modes.Visibility;
                viewsButton.Visibility = ReadViews != null && WriteViews != null ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { filling = false; }
        }

        /// <summary>Goes into a mode: its layers on, the rest off, its labels and filter, the host's part.</summary>
        public void Apply(string name)
        {
            var m = Modes.FirstOrDefault(x => x.Name == name);
            if (m == null) return;
            mode = name;
            foreach (var l in Layers) l.Set(m.On.Contains(l.Key));
            if (m.Labels != null) sketch.Labels = m.Labels.Value;
            if (m.Status != null) sketch.ShowStates = m.Status.Value;
            m.Extra?.Invoke();
            Fill();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>This view, to save: its mode, the layers on and off, the labels, the filter, the level.</summary>
        public SavedView Capture(string name) => new SavedView
        {
            Name = name, Mode = mode, Labels = (int)sketch.Labels, Status = (int)sketch.ShowStates, LevelId = sketch.ActiveLevel?.Id,
            On = Layers.Where(l => l.Get()).Select(l => l.Key).ToList(), Off = Layers.Where(l => !l.Get()).Select(l => l.Key).ToList(),
        };

        /// <summary>Back to a saved view (a layer it does not name stays as it is).</summary>
        public void Restore(SavedView v)
        {
            if (v == null) return;
            mode = Modes.Any(m => m.Name == v.Mode) ? v.Mode : mode;
            foreach (var l in Layers)
            {
                if (v.On?.Contains(l.Key) == true) l.Set(true);
                else if (v.Off?.Contains(l.Key) == true) l.Set(false);
            }
            sketch.Labels = (LabelDensity)Math.Max(0, Math.Min(3, v.Labels));
            sketch.ShowStates = (StatusFilter)Math.Max(0, Math.Min(2, v.Status));
            if (v.LevelId != null && sketch.Building?.Level(v.LevelId) != null) sketch.ShowLevel(v.LevelId);
            Fill();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private ContextMenu LayersMenu()
        {
            var menu = new ContextMenu { PlacementTarget = layersButton, Placement = PlacementMode.Bottom };
            foreach (var l in Layers)
            {
                var layer = l;
                var item = new MenuItem { Header = l.Name, IsCheckable = true, IsChecked = l.Get(), StaysOpenOnClick = true, ToolTip = l.Tip };
                item.Click += (s, e) => { layer.Set(item.IsChecked); Changed?.Invoke(this, EventArgs.Empty); };
                menu.Items.Add(item);
            }
            if (Layers.Count > 0) menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "In colour", IsEnabled = false, FontWeight = FontWeights.SemiBold });
            foreach (StatusFilter f in Enum.GetValues(typeof(StatusFilter)))
            {
                var at = f;
                var item = new MenuItem { Header = "   " + StatusFilters.Name(f), IsCheckable = true, IsChecked = sketch.ShowStates == f, ToolTip = StatusFilters.Help(f) };
                item.Click += (s, e) => { sketch.ShowStates = at; Changed?.Invoke(this, EventArgs.Empty); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Labels", IsEnabled = false, FontWeight = FontWeights.SemiBold });
            foreach (LabelDensity d in Enum.GetValues(typeof(LabelDensity)))
            {
                var at = d;
                var item = new MenuItem { Header = "   " + LabelDensities.Name(d), IsCheckable = true, IsChecked = sketch.Labels == d, ToolTip = LabelDensities.Help(d) };
                item.Click += (s, e) => { sketch.Labels = at; Changed?.Invoke(this, EventArgs.Empty); };
                menu.Items.Add(item);
            }
            return menu;
        }

        private ContextMenu ViewsMenu()
        {
            var menu = new ContextMenu { PlacementTarget = viewsButton, Placement = PlacementMode.Bottom };
            var views = ReadViews?.Invoke() ?? new SavedViews();
            foreach (var v in views.Views.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var view = v;
                var item = new MenuItem { Header = v.Name, ToolTip = $"{v.Mode ?? "Custom"}: {string.Join(", ", v.On.Select(k => Layers.FirstOrDefault(l => l.Key == k)?.Name ?? k))}; labels {LabelDensities.Name((LabelDensity)v.Labels)}; {StatusFilters.Name((StatusFilter)v.Status)}" };
                item.Click += (s, e) => Restore(view);
                menu.Items.Add(item);
            }
            if (views.Views.Count == 0) menu.Items.Add(new MenuItem { Header = "No saved views yet", IsEnabled = false });
            menu.Items.Add(new Separator());
            var save = new MenuItem { Header = "Save this view…", ToolTip = "Name the layers, the filter, the labels and the level as they are now; saved with the project" };
            save.Click += (s, e) =>
            {
                string name = Ask(Window.GetWindow(this), "Save this view", "A name for this view (the layers, the filter, the labels and the level as they are now):", mode == null ? "" : mode + " view");
                if (string.IsNullOrWhiteSpace(name)) return;
                var all = ReadViews?.Invoke() ?? new SavedViews();
                bool had = all.Find(name.Trim()) != null;
                all.Put(Capture(name.Trim()));
                WriteViews?.Invoke(all, had ? $"View \"{name.Trim()}\" saved again." : $"View \"{name.Trim()}\" saved.");
            };
            menu.Items.Add(save);
            if (views.Views.Count > 0)
            {
                var del = new MenuItem { Header = "Delete" };
                foreach (var v in views.Views.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    string name = v.Name;
                    var item = new MenuItem { Header = name };
                    item.Click += (s, e) =>
                    {
                        var all = ReadViews?.Invoke() ?? new SavedViews();
                        if (all.Remove(name)) WriteViews?.Invoke(all, $"View \"{name}\" deleted.");
                    };
                    del.Items.Add(item);
                }
                menu.Items.Add(del);
            }
            return menu;
        }

        /// <summary>A small box asking for a name; null when cancelled.</summary>
        public static string Ask(Window owner, string title, string prompt, string start)
        {
            var box = new TextBox { Text = start ?? "", Margin = new Thickness(0, 6, 0, 10), Padding = new Thickness(3, 2, 3, 2) };
            var ok = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 2, 14, 2) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var body = new StackPanel { Margin = new Thickness(14) };
            body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(box);
            body.Children.Add(buttons);
            var w = new Window
            {
                Title = title, Content = body, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, Owner = owner, ShowInTaskbar = false,
            };
            ok.Click += (s, e) => w.DialogResult = true;
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return w.ShowDialog() == true ? box.Text : null;
        }
    }

    /// <summary>
    /// The case picker (owner 2026-11-14, UI audit A5): Envelope (the default), or any one case by name; ◀ ▶ step through
    /// them; Governing jumps to the case that governs the member picked (the host's).
    /// </summary>
    public sealed class CasePicker : StackPanel
    {
        public const string Envelope = "Envelope";
        private readonly ComboBox box = new ComboBox { Width = 220, Padding = new Thickness(6, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center, MaxDropDownHeight = 520 };
        private readonly Button governing = new Button { Content = "Governing", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), Focusable = false };
        private List<(string name, string label)> cases = new List<(string, string)>();
        private bool filling;

        public CasePicker()
        {
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;
            Children.Add(new TextBlock { Text = "Case", Foreground = Ui.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            Button Arrow(string t, string tip, int by)
            {
                var b = new Button { Content = t, ToolTip = tip, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 2, 0), Focusable = false };
                b.Click += (s, e) => Step(by);
                return b;
            }
            Children.Add(Arrow("◀", "The case before", -1));
            Children.Add(box);
            Children.Add(Arrow("▶", "The next case", 1));
            Children.Add(governing);
            System.Windows.Automation.AutomationProperties.SetName(box, "Load case");
            box.SelectionChanged += (s, e) => { if (!filling) Picked?.Invoke(this, Selected); };
            governing.Click += (s, e) => { var g = GoverningOf?.Invoke(); if (g != null) Selected = g; };
            governing.ToolTip = "The case that governs the member picked";
        }

        /// <summary>A case picked (<see cref="Envelope"/> for the envelope).</summary>
        public event EventHandler<string> Picked;

        /// <summary>The case governing the member picked (the host's); null: the button does nothing.</summary>
        public Func<string> GoverningOf { get; set; }

        /// <summary>What Envelope means here, for its tooltip ("the case designing the most walls").</summary>
        public string EnvelopeTip { get; set; } = "Every case together: the worst of each";

        /// <summary>The cases by name, each with its label (null: its name). The one picked stays when it is still there.</summary>
        public void Show(IEnumerable<(string name, string label)> list)
        {
            var keep = Selected;
            cases = (list ?? Enumerable.Empty<(string, string)>()).ToList();
            filling = true;
            try
            {
                box.Items.Clear();
                box.Items.Add(new ComboBoxItem { Content = Envelope, Tag = Envelope, ToolTip = EnvelopeTip });
                foreach (var (name, label) in cases) box.Items.Add(new ComboBoxItem { Content = label ?? name, Tag = name, ToolTip = name });
                box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == keep) ?? box.Items[0];
            }
            finally { filling = false; }
        }

        /// <summary>The case shown (<see cref="Envelope"/>, or a name); setting it raises <see cref="Picked"/> when it changes.</summary>
        public string Selected
        {
            get => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? Envelope;
            set
            {
                var i = box.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == value);
                if (i != null) box.SelectedItem = i;
            }
        }

        /// <summary>The names, in order (Envelope first).</summary>
        public IReadOnlyList<string> Names => box.Items.OfType<ComboBoxItem>().Select(i => (string)i.Tag).ToList();

        /// <summary>The next (+1) or the one before (−1), round through Envelope.</summary>
        public void Step(int by)
        {
            int n = box.Items.Count;
            if (n == 0) return;
            box.SelectedIndex = ((Math.Max(0, box.SelectedIndex) + by) % n + n) % n;
        }
    }
}
