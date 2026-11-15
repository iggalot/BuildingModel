using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingModel.Sketch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>The results grid and the layout switch (owner 2026-11-14, UI audit C and §5.4).</summary>
    [TestClass]
    public class ResultsGridTests
    {
        private static ResultRow Row(string mark, MemberHealth h, double v) =>
            new ResultRow { Mark = mark, Health = h, Ids = { mark }, Tag = v };

        private static ResultTable Walls()
        {
            var t = new ResultTable("Shear walls");
            t.Columns.Add(new ResultColumn("v", r => ((double)r.Tag).ToString("0", CultureInfo.InvariantCulture) + " plf", r => (double)r.Tag, 80, true));
            t.Columns.Add(new ResultColumn("Hold-down", r => "HDU2", width: 80, shownAtFirst: true));
            t.Columns.Add(new ResultColumn("Sheathing", r => "Structural I " + r.Mark, width: 160));
            t.Rows.Add(Row("SW2-10", MemberHealth.Of(0.50), 190));
            t.Rows.Add(Row("SW2-2", MemberHealth.Of(0.95), 361));
            t.Rows.Add(Row("SW2-9", MemberHealth.Of(1.40), 532));
            t.Rows.Add(Row("SW2-3", MemberHealth.NotDesigned(), 0));
            t.Rows.Add(Row("SW2-1", MemberHealth.Of(0.72), 274));
            return t;
        }

        [STATestMethod]
        public void WorstFirst_ThenAnyColumn_ThenBack()
        {
            var g = new ResultsGrid();
            g.Show(new[] { Walls() });
            CollectionAssert.AreEqual(new[] { "SW2-9", "SW2-3", "SW2-2", "SW2-1", "SW2-10" }, g.Shown().Select(r => r.Mark).ToArray(), "fails, not designed, close, then by ratio");
            g.SortBy(0);
            CollectionAssert.AreEqual(new[] { "SW2-1", "SW2-2", "SW2-3", "SW2-9", "SW2-10" }, g.Shown().Select(r => r.Mark).ToArray(), "the marks in their natural order");
            g.SortBy(0);
            Assert.AreEqual("SW2-10", g.Shown().First().Mark, "again: turned round");
            g.SortBy(0);
            Assert.AreEqual("SW2-9", g.Shown().First().Mark, "a third time: worst first again");
            g.SortBy(3);
            Assert.AreEqual("SW2-3", g.Shown().First().Mark, "the v column, smallest first");
        }

        [STATestMethod]
        public void FilterChips_CountAndFilter_SearchToo()
        {
            var g = new ResultsGrid();
            g.Show(new[] { Walls() });
            Assert.AreEqual(1, g.Count(ResultFilter.Fails));
            Assert.AreEqual(1, g.Count(ResultFilter.NotDesigned));
            Assert.AreEqual(1, g.Count(ResultFilter.Close));
            Assert.AreEqual(2, g.Count(ResultFilter.Passes));
            g.Filter = ResultFilter.Close;
            CollectionAssert.AreEqual(new[] { "SW2-2" }, g.Shown().Select(r => r.Mark).ToArray());
            g.Filter = ResultFilter.All;
            g.Search = "532";
            CollectionAssert.AreEqual(new[] { "SW2-9" }, g.Shown().Select(r => r.Mark).ToArray(), "any value found");
        }

        [STATestMethod]
        public void Columns_TheChosenInSplit_AllInResults_AndTheTsvAsShown()
        {
            var g = new ResultsGrid();
            g.Show(new[] { Walls() });
            CollectionAssert.AreEqual(new[] { "v", "Hold-down" }, g.ShownColumns().Select(c => c.Header).ToArray(), "Mark, Status, Ratio and these: the first few");
            g.Wide = true;
            Assert.AreEqual(3, g.ShownColumns().Count, "Results: every column");
            g.Wide = false;
            g.SetColumn("Sheathing", true);
            Assert.AreEqual(3, g.ShownColumns().Count, "opted in");
            g.SetColumn("Sheathing", false);
            g.Filter = ResultFilter.Fails;
            var tsv = g.ToTsv().Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual("Mark\tStatus\tRatio\tv\tHold-down", tsv[0]);
            Assert.AreEqual("SW2-9\tfails\t1.40\t532 plf\tHDU2", tsv[1]);
            Assert.AreEqual(2, tsv.Length, "only the filtered rows");
        }

        [STATestMethod]
        public void TwoWays_ARowPicked_AndThePlansPickRevealed()
        {
            var g = new ResultsGrid();
            g.Show(new[] { Walls(), new ResultTable("Hold-downs") });
            ResultRow picked = null;
            g.RowPicked += (s, r) => picked = r;
            g.Reveal(new[] { "SW2-2" });
            Assert.IsNull(picked, "revealing what the plan picked is not a pick");
            g.ShowTable("Hold-downs");
            Assert.AreEqual("Hold-downs", g.TableName);
            g.Show(new[] { Walls(), new ResultTable("Hold-downs") });
            Assert.AreEqual("Hold-downs", g.TableName, "the table shown stays when the design runs again");
        }

        [STATestMethod]
        public void LayoutSwitch_RaisesOnlyOnAChange()
        {
            var s = new LayoutSwitch();
            var seen = new List<PanelLayout>();
            s.Changed += (o, l) => seen.Add(l);
            Assert.AreEqual(PanelLayout.Split, s.Layout, "Split by default");
            s.Layout = PanelLayout.Split;
            s.Layout = PanelLayout.Results;
            s.Layout = PanelLayout.Results;
            s.Layout = PanelLayout.Plan;
            CollectionAssert.AreEqual(new[] { PanelLayout.Results, PanelLayout.Plan }, seen);
        }

        [STATestMethod]
        public void Render_TheGrid()
        {
            var g = new ResultsGrid { Width = 640, Height = 260 };
            var hold = new ResultTable("Hold-downs");
            hold.Rows.Add(Row("SW2-9", MemberHealth.Of(1.10), 0));
            g.Show(new[] { Walls(), hold, new ResultTable("Diaphragm lines") });
            g.Reveal(new[] { "SW2-1" });
            var host = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.White, Child = g, Padding = new System.Windows.Thickness(8) };
            var bmp = SketchTests.Render(host, "results-grid");
            Assert.IsTrue(SketchTests.Count(bmp, PlanStatus.Fail, 30) > 50, "the failing row and its tab in red");
        }
    }
}
