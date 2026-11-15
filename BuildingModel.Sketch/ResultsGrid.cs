using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BuildingModel.Sketch
{
    /// <summary>One member in a results table: what it selects on the plan, its mark and state, and the host's own object.</summary>
    public sealed class ResultRow
    {
        /// <summary>The model ids it selects on the plan (a wall, the walls on a line).</summary>
        public List<string> Ids { get; set; } = new List<string>();
        public string Mark { get; set; }
        public MemberHealth Health { get; set; }
        /// <summary>The level it is on (the plan goes there when it is picked).</summary>
        public string LevelId { get; set; }
        /// <summary>The host's object (a design result, a connection).</summary>
        public object Tag { get; set; }

        public double Ratio => Health == null ? double.NaN : Health.Ratio;
    }

    /// <summary>A column of a results table: its words and its sort value, from the row.</summary>
    public sealed class ResultColumn
    {
        public ResultColumn(string header, Func<ResultRow, string> text, Func<ResultRow, IComparable> sort = null, double width = 90, bool shownAtFirst = false)
        {
            Header = header;
            Text = text;
            Sort = sort ?? (r => text(r) ?? "");
            Width = width;
            ShownAtFirst = shownAtFirst;
        }

        public string Header { get; }
        public Func<ResultRow, string> Text { get; }
        public Func<ResultRow, IComparable> Sort { get; }
        public double Width { get; }
        /// <summary>One of the few shown in Split at first (with Mark, Status and Ratio: six in all); the rest are opt-in.</summary>
        public bool ShownAtFirst { get; }
        public string Tip { get; set; }
        /// <summary>Its own state for a cell (a hold-down that fails in a wall that passes on shear); null: neutral ink.</summary>
        public Func<ResultRow, Health> State { get; set; }
    }

    /// <summary>One table: a kind of member (Shear walls, Hold-downs, Diaphragm lines…), its columns and rows.</summary>
    public sealed class ResultTable
    {
        public ResultTable(string name) { Name = name; }
        public string Name { get; }
        public List<ResultColumn> Columns { get; } = new List<ResultColumn>();
        public List<ResultRow> Rows { get; } = new List<ResultRow>();
        /// <summary>What the table is, for its tab's tooltip.</summary>
        public string Tip { get; set; }
    }

    /// <summary>The status filter of a results table.</summary>
    public enum ResultFilter { All, Fails, NotDesigned, Close, Passes }

    /// <summary>
    /// The results grid (owner 2026-11-14, UI audit C, "RISA's best feature, only better"): a table per kind of member, each
    /// <list type="bullet">
    /// <item>sorted worst first (failing and not designed, then by ratio), any column by a click on its head;</item>
    /// <item>filtered by status chip (each with its count) and a search box;</item>
    /// <item>with a column chooser: Mark, Status and Ratio always, a few more at first, the rest opt-in and remembered;
    /// <see cref="Wide"/> (the Results layout) shows them all;</item>
    /// <item>two-way with the plan: a row picked selects its members (<see cref="RowPicked"/>); <see cref="Reveal"/> marks and
    /// scrolls to the rows of what the plan picked;</item>
    /// <item>copied as tab-separated values with the filter as it is.</item>
    /// </list>
    /// </summary>
    public sealed class ResultsGrid : DockPanel
    {
        private readonly WrapPanel tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        private readonly WrapPanel chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox search = new TextBox { Width = 140, Padding = new Thickness(3, 1, 3, 1), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Find a mark or any value in the table" };
        private readonly Button columnsButton = new Button { Content = "Columns ▾", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), Focusable = false };
        private readonly Button copyButton = new Button { Content = "Copy", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), Focusable = false,
                                                          ToolTip = "Copy the table as it is shown (filter, search, columns) as tab-separated values: paste it into a spreadsheet." };
        private readonly Grid head = new Grid();
        private readonly StackPanel body = new StackPanel();
        private readonly ScrollViewer scroll;
        private readonly TextBlock empty = new TextBlock { Foreground = Ui.Muted, Margin = new Thickness(4, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

        private List<ResultTable> tables = new List<ResultTable>();
        private ResultTable table;
        private ResultFilter filter = ResultFilter.All;
        private int sortColumn = -1;   // -1: worst first; 0 mark, 1 status, 2 ratio, 3+ the table's columns
        private bool sortDown = true;
        private bool sortTurned;
        private HashSet<string> revealed = new HashSet<string>();
        private bool wide;

        /// <summary>The columns shown in Split, by table and header (remembered for the session); none: the table's first ones.</summary>
        private static readonly Dictionary<string, HashSet<string>> chosen = new Dictionary<string, HashSet<string>>();

        public ResultsGrid()
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(search);
            right.Children.Add(columnsButton);
            right.Children.Add(copyButton);
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            bar.Children.Add(chips);
            DockPanel.SetDock(tabs, Dock.Top);
            DockPanel.SetDock(bar, Dock.Top);
            DockPanel.SetDock(head, Dock.Top);
            Children.Add(tabs);
            Children.Add(bar);
            Children.Add(head);
            var inner = new StackPanel();
            inner.Children.Add(body);
            inner.Children.Add(empty);
            scroll = new ScrollViewer { Content = inner, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Children.Add(scroll);
            search.TextChanged += (s, e) => Rebuild();
            columnsButton.Click += (s, e) => ChooseColumns();
            copyButton.Click += (s, e) => { try { Clipboard.SetText(ToTsv()); } catch (Exception) { /* the clipboard busy: nothing copied */ } };
            System.Windows.Automation.AutomationProperties.SetName(search, "Find in the results");
        }

        /// <summary>A row clicked: the host selects its members on the plan (and opens its card).</summary>
        public event EventHandler<ResultRow> RowPicked;

        /// <summary>The table shown now (by name), kept when the tables are given again.</summary>
        public string TableName => table?.Name;

        /// <summary>The Results layout: every column, not just the chosen ones.</summary>
        public bool Wide
        {
            get => wide;
            set { if (wide == value) return; wide = value; Rebuild(); }
        }

        public ResultFilter Filter
        {
            get => filter;
            set { filter = value; Rebuild(); }
        }

        public string Search
        {
            get => search.Text;
            set => search.Text = value ?? "";
        }

        /// <summary>The tables (a design ran again): the same table stays shown, its filter, search and sort too.</summary>
        public void Show(IEnumerable<ResultTable> list)
        {
            tables = (list ?? Enumerable.Empty<ResultTable>()).ToList();
            var keep = table?.Name;
            table = tables.FirstOrDefault(t => t.Name == keep) ?? tables.FirstOrDefault();
            if (keep != table?.Name) { sortColumn = -1; sortDown = true; }
            Rebuild();
        }

        /// <summary>Shows the table with this name.</summary>
        public void ShowTable(string name)
        {
            var t = tables.FirstOrDefault(x => x.Name == name);
            if (t == null || ReferenceEquals(t, table)) return;
            table = t;
            sortColumn = -1;
            sortDown = true;
            Rebuild();
        }

        /// <summary>What the plan picked: its rows marked and scrolled to, in the table shown (no <see cref="RowPicked"/>).</summary>
        public void Reveal(IEnumerable<string> ids)
        {
            revealed = new HashSet<string>(ids ?? Enumerable.Empty<string>());
            Rebuild();
            var first = body.Children.OfType<FrameworkElement>().FirstOrDefault(f => f.Tag is ResultRow r && r.Ids.Any(revealed.Contains));
            first?.BringIntoView();
        }

        /// <summary>The rows shown now, in order (the filter, the search and the sort applied).</summary>
        public List<ResultRow> Shown()
        {
            if (table == null) return new List<ResultRow>();
            var cols = table.Columns;
            string q = (search.Text ?? "").Trim();
            IEnumerable<ResultRow> rows = table.Rows.Where(r => Passes(r, filter));
            if (q.Length > 0)
                rows = rows.Where(r => (r.Mark ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       cols.Any(c => (c.Text(r) ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            List<ResultRow> list;
            if (sortColumn < 0) list = rows.OrderBy(Tier).ThenByDescending(r => double.IsNaN(r.Ratio) ? double.NegativeInfinity : r.Ratio).ThenBy(r => r.Mark, MarkOrder.Instance).ToList();
            else
            {
                Func<ResultRow, IComparable> key = sortColumn == 0 ? (r => new MarkKey(r.Mark)) : sortColumn == 1 ? (r => Tier(r))
                                                 : sortColumn == 2 ? (r => double.IsNaN(r.Ratio) ? double.NegativeInfinity : r.Ratio) : cols[sortColumn - 3].Sort;
                list = (sortDown ? rows.OrderByDescending(key, Comparer<IComparable>.Create(Compare)) : rows.OrderBy(key, Comparer<IComparable>.Create(Compare))).ToList();
            }
            return list;
        }

        /// <summary>The table's columns shown now (Split: the chosen ones; <see cref="Wide"/>: all of them).</summary>
        public List<ResultColumn> ShownColumns()
        {
            if (table == null) return new List<ResultColumn>();
            if (wide) return table.Columns.ToList();
            var set = Chosen(table);
            return table.Columns.Where(c => set.Contains(c.Header)).ToList();
        }

        /// <summary>Shows or hides a column in Split (remembered for the session).</summary>
        public void SetColumn(string header, bool shown)
        {
            if (table == null) return;
            var set = Chosen(table);
            if (shown) set.Add(header); else set.Remove(header);
            Rebuild();
        }

        /// <summary>How many rows of the table shown are in each state, for the chips.</summary>
        public int Count(ResultFilter f) => table?.Rows.Count(r => Passes(r, f)) ?? 0;

        /// <summary>The table as it is shown, tab-separated, with its head.</summary>
        public string ToTsv()
        {
            var cols = ShownColumns();
            var sb = new StringBuilder();
            sb.Append("Mark\tStatus\tRatio");
            foreach (var c in cols) sb.Append('\t').Append(c.Header);
            sb.AppendLine();
            foreach (var r in Shown())
            {
                sb.Append(r.Mark).Append('\t').Append(StatusWords(r)).Append('\t').Append(double.IsNaN(r.Ratio) ? "" : r.Ratio.ToString("0.00", CultureInfo.InvariantCulture));
                foreach (var c in cols) sb.Append('\t').Append((c.Text(r) ?? "").Replace('\t', ' '));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ building it

        private static HashSet<string> Chosen(ResultTable t)
        {
            if (!chosen.TryGetValue(t.Name, out var set))
            {
                var first = t.Columns.Where(c => c.ShownAtFirst).Select(c => c.Header).ToList();
                if (first.Count == 0) first = t.Columns.Take(3).Select(c => c.Header).ToList();
                chosen[t.Name] = set = new HashSet<string>(first);
            }
            return set;
        }

        private static int Tier(ResultRow r)
        {
            var s = r.Health?.State ?? Health.None;
            return s == Health.Fails ? 0 : s == Health.NotDesigned ? 1 : s == Health.Close ? 2 : s == Health.Passes ? 3 : 4;
        }

        private static bool Passes(ResultRow r, ResultFilter f)
        {
            var s = r.Health?.State ?? Health.None;
            switch (f)
            {
                case ResultFilter.Fails: return s == Health.Fails;
                case ResultFilter.NotDesigned: return s == Health.NotDesigned;
                case ResultFilter.Close: return s == Health.Close;
                case ResultFilter.Passes: return s == Health.Passes;
                default: return true;
            }
        }

        private static int Compare(IComparable a, IComparable b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            if (a.GetType() != b.GetType()) return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
            return a.CompareTo(b);
        }

        private static string StatusWords(ResultRow r) => r.Health == null ? "" : r.Health.State == Health.NotDesigned ? "NOT DESIGNED"
            : r.Health.State == Health.Fails ? "fails" : r.Health.State == Health.Close ? "close" : r.Health.State == Health.Passes ? "passes" : "";

        private void Rebuild()
        {
            BuildTabs();
            BuildChips();
            var cols = ShownColumns();
            var widths = new List<double> { 74, 134, 84 };
            widths.AddRange(cols.Select(c => c.Width));
            BuildHead(cols, widths);
            body.Children.Clear();
            var rows = Shown();
            int i = 0;
            foreach (var r in rows) body.Children.Add(RowOf(r, cols, widths, i++));
            empty.Text = table == null ? "Nothing designed yet." : table.Rows.Count == 0 ? $"No {table.Name.ToLowerInvariant()} here."
                       : rows.Count == 0 ? "None match the filter." : "";
            empty.Visibility = empty.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            columnsButton.ToolTip = wide ? "Results shows every column. In Split, pick which show here." : "Which columns show in Split (Mark, Status and Ratio always)";
        }

        private void BuildTabs()
        {
            tabs.Children.Clear();
            tabs.Visibility = tables.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var t in tables)
            {
                bool on = ReferenceEquals(t, table);
                int bad = t.Rows.Count(r => r.Health != null && (r.Health.State == Health.Fails || r.Health.State == Health.NotDesigned));
                var b = new ToggleButton
                {
                    IsChecked = on, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 4, 2), Focusable = false, ToolTip = t.Tip,
                    Content = bad > 0 ? $"{t.Name}  ✗ {bad}" : $"{t.Name}  {t.Rows.Count}",
                    Foreground = bad > 0 ? PlanStatus.BrushOf(Health.Fails) : Ui.Ink, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                };
                var name = t.Name;
                b.Click += (s, e) => ShowTable(name);
                tabs.Children.Add(b);
            }
        }

        private void BuildChips()
        {
            chips.Children.Clear();
            void Chip(ResultFilter f, string text, Health state)
            {
                int n = f == ResultFilter.All ? table?.Rows.Count ?? 0 : Count(f);
                if (f != ResultFilter.All && n == 0 && filter != f) return; // a state no row is in: no chip
                bool on = filter == f;
                var c = PlanStatus.ColorOf(state);
                var ink = state == Health.None ? Ui.Ink : state == Health.Close ? Ui.Warning : PlanStatus.BrushOf(state);
                var b = new ToggleButton
                {
                    IsChecked = on, Content = $"{text} {n}", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 2), Focusable = false,
                    Foreground = ink, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                    Background = state == Health.None ? Brushes.White : Ui.Frozen(Color.FromArgb(0x20, c.R, c.G, c.B)),
                    ToolTip = f == ResultFilter.All ? "Every row" : $"Only the rows that {StatusWords(new ResultRow { Health = new MemberHealth { State = state } })}",
                };
                b.Click += (s, e) => Filter = on && f != ResultFilter.All ? ResultFilter.All : f;
                chips.Children.Add(b);
            }
            Chip(ResultFilter.All, "All", Health.None);
            Chip(ResultFilter.Fails, "✗", Health.Fails);
            Chip(ResultFilter.NotDesigned, "⊘", Health.NotDesigned);
            Chip(ResultFilter.Close, "▲", Health.Close);
            Chip(ResultFilter.Passes, "✓", Health.Passes);
        }

        private Grid NewGrid(List<double> widths)
        {
            var g = new Grid();
            foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private void BuildHead(List<ResultColumn> cols, List<double> widths)
        {
            head.Children.Clear();
            head.ColumnDefinitions.Clear();
            foreach (var w in widths) head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.Background = Ui.Bar;
            var names = new List<(string text, string tip)> { ("Mark", null), ("Status", "Fails, NOT DESIGNED, close (from 0.90) or passes"), ("Ratio", "Demand over capacity, the worst of its checks") };
            names.AddRange(cols.Select(c => (c.Header, c.Tip)));
            for (int k = 0; k < names.Count; k++)
            {
                int col = k;
                string arrow = sortColumn == k ? (sortDown ? " ▼" : " ▲") : sortColumn < 0 && k == 1 ? " ▼" : "";
                var t = new TextBlock
                {
                    Text = names[k].text + arrow, FontWeight = FontWeights.SemiBold, Foreground = Ui.Muted, FontSize = 11, Padding = new Thickness(4, 3, 4, 3),
                    Cursor = Cursors.Hand, TextTrimming = TextTrimming.CharacterEllipsis, Background = Brushes.Transparent,
                    ToolTip = (names[k].tip == null ? "" : names[k].tip + ". ") + "Click to sort; again to turn it round. Worst first is the default.",
                };
                t.MouseLeftButtonUp += (s, e) => SortBy(col);
                Grid.SetColumn(t, k);
                head.Children.Add(t);
            }
        }

        /// <summary>Sorts by a column (0 mark, 1 status, 2 ratio, 3+ the table's); the same again turns it round, a third time back to worst first.</summary>
        public void SortBy(int column)
        {
            if (sortColumn == column)
            {
                if (!sortTurned) { sortDown = !sortDown; sortTurned = true; }
                else { sortColumn = -1; sortDown = true; sortTurned = false; }
            }
            else
            {
                sortColumn = column;
                sortTurned = false;
                sortDown = column == 2; // the ratio: largest first; the status: worst first (its tier up); the rest A to Z, 1 up
            }
            Rebuild();
        }

        private FrameworkElement RowOf(ResultRow r, List<ResultColumn> cols, List<double> widths, int index)
        {
            var g = NewGrid(widths);
            bool marked = r.Ids.Any(revealed.Contains);
            var plain = index % 2 == 0 ? Brushes.White : Ui.Bar;
            g.Background = marked ? Ui.Checked : plain;
            g.Tag = r;
            g.Cursor = Cursors.Hand;
            var state = r.Health?.State ?? Health.None;
            Brush Ink(Health h) => h == Health.Close ? Ui.Warning : h == Health.Fails || h == Health.NotDesigned ? PlanStatus.BrushOf(h) : Ui.Ink;
            void Cell(int k, UIElement e) { Grid.SetColumn(e, k); g.Children.Add(e); }
            TextBlock Text(string s, Brush ink = null, bool bold = false) => new TextBlock
            {
                Text = s ?? "", Foreground = ink ?? Ui.Ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Padding = new Thickness(4, 3, 4, 3),
                TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = string.IsNullOrEmpty(s) ? null : s, VerticalAlignment = VerticalAlignment.Center,
            };
            Cell(0, Text(r.Mark, null, true));
            var chip = MemberCard.Chip(r.Health);
            if (chip != null) { chip.HorizontalAlignment = HorizontalAlignment.Left; chip.VerticalAlignment = VerticalAlignment.Center; chip.Margin = new Thickness(3, 2, 3, 2); Cell(1, chip); }
            Cell(2, RatioBar(r.Ratio, state));
            for (int k = 0; k < cols.Count; k++)
            {
                var c = cols[k];
                var cs = c.State?.Invoke(r) ?? Health.None;
                Cell(3 + k, Text(c.Text(r), Ink(cs), PlanStatus.Attention(cs)));
            }
            g.MouseEnter += (s, e) => { if (!marked) g.Background = Ui.Hover; };
            g.MouseLeave += (s, e) => { if (!marked) g.Background = plain; };
            g.MouseLeftButtonUp += (s, e) => RowPicked?.Invoke(this, r);
            return g;
        }

        /// <summary>The ratio as a bar to 1.0 (over it: full and red), its value beside it.</summary>
        private static FrameworkElement RatioBar(double ratio, Health state)
        {
            var g = new Grid { Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = double.IsNaN(ratio) ? "No ratio" : $"Ratio {ratio:0.00}" };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (double.IsNaN(ratio)) { g.Children.Add(new TextBlock { Text = "—", Foreground = Ui.Muted }); return g; }
            var track = new Border { Height = 7, Background = Ui.Line, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border
            {
                Height = 7, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Width = 40 * Math.Max(0.02, Math.Min(1.0, ratio)), Background = state == Health.None ? Ui.Muted : PlanStatus.BrushOf(state),
            };
            g.Children.Add(track);
            g.Children.Add(fill);
            var t = new TextBlock { Text = double.IsInfinity(ratio) ? "∞" : ratio.ToString("0.00", CultureInfo.InvariantCulture), FontSize = 11, Margin = new Thickness(4, 0, 0, 0),
                                    Foreground = state == Health.Fails ? PlanStatus.BrushOf(state) : Ui.Ink, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }

        private void ChooseColumns()
        {
            if (table == null) return;
            var menu = new ContextMenu { PlacementTarget = columnsButton, Placement = PlacementMode.Bottom };
            var set = Chosen(table);
            foreach (var c in table.Columns)
            {
                var header = c.Header;
                var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = set.Contains(header), StaysOpenOnClick = true, ToolTip = c.Tip };
                item.Click += (s, e) => SetColumn(header, item.IsChecked);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        /// <summary>Marks in their natural order: W2 before W10.</summary>
        private sealed class MarkOrder : IComparer<string>
        {
            public static readonly MarkOrder Instance = new MarkOrder();
            public int Compare(string a, string b) => new MarkKey(a).CompareTo(new MarkKey(b));
        }

        private sealed class MarkKey : IComparable
        {
            private readonly string text;
            public MarkKey(string t) { text = t ?? ""; }
            public int CompareTo(object o)
            {
                var b = (o as MarkKey)?.text ?? "";
                int i = 0, j = 0;
                while (i < text.Length && j < b.Length)
                {
                    if (char.IsDigit(text[i]) && char.IsDigit(b[j]))
                    {
                        int i0 = i, j0 = j;
                        while (i < text.Length && char.IsDigit(text[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        long x = long.Parse(text.Substring(i0, Math.Min(18, i - i0)), CultureInfo.InvariantCulture), y = long.Parse(b.Substring(j0, Math.Min(18, j - j0)), CultureInfo.InvariantCulture);
                        if (x != y) return x.CompareTo(y);
                    }
                    else
                    {
                        int c = char.ToUpperInvariant(text[i]).CompareTo(char.ToUpperInvariant(b[j]));
                        if (c != 0) return c;
                        i++; j++;
                    }
                }
                return (text.Length - i).CompareTo(b.Length - j);
            }
        }
    }

    /// <summary>The three layouts of a plan and its results (owner 2026-11-14): the plan alone, both side by side, the results alone.</summary>
    public enum PanelLayout { Plan, Split, Results }

    /// <summary>
    /// The one switch for the layout, in every app (UI audit §5.4): Plan (the results folded to their strip), Split (the
    /// default, a splitter between), Results (the plan folded to its strip).
    /// </summary>
    public sealed class LayoutSwitch : StackPanel
    {
        private readonly Dictionary<PanelLayout, ToggleButton> buttons = new Dictionary<PanelLayout, ToggleButton>();
        private PanelLayout layout = PanelLayout.Split;

        public LayoutSwitch()
        {
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;
            foreach (var (l, text, tip) in new[]
            {
                (PanelLayout.Plan, "Plan", "The plan takes the window; the results fold to the strip on the right (Ctrl+1)"),
                (PanelLayout.Split, "Split", "The plan and the results side by side; drag the bar between them (Ctrl+2)"),
                (PanelLayout.Results, "Results", "The results take the window, every column shown; the plan folds to the strip on the left (Ctrl+3)"),
            })
            {
                var b = new ToggleButton { Content = text, ToolTip = tip, Padding = new Thickness(10, 2, 10, 2), Focusable = false, Margin = new Thickness(0, 0, -1, 0) };
                var at = l;
                b.Click += (s, e) => Layout = at;
                buttons[l] = b;
                Children.Add(b);
            }
            System.Windows.Automation.AutomationProperties.SetName(this, "Layout");
            Show();
        }

        /// <summary>The layout chosen (a click or <see cref="Layout"/> set raises <see cref="Changed"/>).</summary>
        public PanelLayout Layout
        {
            get => layout;
            set
            {
                bool changed = layout != value;
                layout = value;
                Show();
                if (changed) Changed?.Invoke(this, value);
            }
        }

        public event EventHandler<PanelLayout> Changed;

        /// <summary>Ctrl+1, 2, 3 in the window: Plan, Split, Results.</summary>
        public void HookKeys(Window w)
        {
            if (w == null) return;
            w.PreviewKeyDown += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || !IsVisible) return;
                if (e.Key == Key.D1 || e.Key == Key.NumPad1) { Layout = PanelLayout.Plan; e.Handled = true; }
                else if (e.Key == Key.D2 || e.Key == Key.NumPad2) { Layout = PanelLayout.Split; e.Handled = true; }
                else if (e.Key == Key.D3 || e.Key == Key.NumPad3) { Layout = PanelLayout.Results; e.Handled = true; }
            };
        }

        private void Show()
        {
            foreach (var kv in buttons)
            {
                kv.Value.IsChecked = kv.Key == layout;
                kv.Value.FontWeight = kv.Key == layout ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }
}
