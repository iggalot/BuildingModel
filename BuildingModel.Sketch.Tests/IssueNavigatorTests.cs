using System.Collections.Generic;
using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Sketch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>The issues button's tiers and the problem navigator (owner 2026-11-14, UI audit A6, A7).</summary>
    [TestClass]
    public class IssueNavigatorTests
    {
        private static List<ModelIssue> Issues() => new List<ModelIssue>
        {
            new ModelIssue { Severity = Severity.Warning, Code = "WallOffFloor", Message = "w" },
            new ModelIssue { Severity = Severity.Error, Code = "DiaphragmNotDesigned", Message = "nd" },
            new ModelIssue { Severity = Severity.Error, Code = "BeamNotHeldUp", Message = "e" },
        };

        [TestMethod]
        public void Words_EachTierCounted()
        {
            Assert.AreEqual("✓ No issues", IssuesButton.Words(0, 0, 0, 0));
            Assert.AreEqual("✗ 1 error  ·  ⊘ 2 not designed  ·  ⚠ 3 warnings  ·  ⓘ 4 to review", IssuesButton.Words(1, 2, 3, 4));
            Assert.AreEqual("ⓘ 1 to review", IssuesButton.Words(0, 0, 0, 1));
        }

        [TestMethod]
        public void Ordered_WorstFirst()
        {
            var o = IssueList.Ordered(Issues());
            CollectionAssert.AreEqual(new[] { "e", "nd", "w" }, o.Select(i => i.Message).ToArray());
            CollectionAssert.AreEqual(new[] { "✗", "⊘", "⚠" }, o.Select(IssueList.Glyph).ToArray());
        }

        [STATestMethod]
        public void F8_StepsWorstFirst_AndWrapsBothWays()
        {
            var b = new IssuesButton();
            b.Show(Issues());
            var seen = new List<string>();
            b.Picked += (s, i) => seen.Add(i.Message);
            Assert.IsTrue(b.StepTo(1));
            b.StepTo(1);
            b.StepTo(1);
            b.StepTo(1);   // wraps to the first
            b.StepTo(-1);  // back to the last
            CollectionAssert.AreEqual(new[] { "e", "nd", "w", "e", "w" }, seen);
            b.Show(new List<ModelIssue>());
            Assert.IsFalse(b.StepTo(1), "nothing to go to");
        }

        [STATestMethod]
        public void Button_CountsTheReviewItemsNotYetTicked()
        {
            var b = new IssuesButton();
            var items = new List<ReviewItem>
            {
                new ReviewItem { Id = "a", App = SuiteApps.ShearWalls, Title = "A", Text = "a" },
                new ReviewItem { Id = "b", App = SuiteApps.ShearWalls, Title = "B", Text = "b" },
            };
            var ticks = new ReviewSection();
            ticks.Mark(items[0], true);
            b.ShowReview(items, ticks);
            Assert.AreEqual(1, b.ToReview);
            Assert.AreEqual("ⓘ 1 to review", b.Content);
        }

        /// <summary>The list drawn: each tier in its colour, a fix link, the review section apart in blue.</summary>
        [STATestMethod]
        public void Render_TheListWithEveryTier()
        {
            var issues = Issues();
            issues[2].FixIn = SuiteApps.Building;
            issues[2].Message = "Beams BM1 and BM2 hold each other up over nothing: each needs two supports.";
            issues[1].Message = "D1: the boundary nailing the SDPWS tables do not have: NOT DESIGNED.";
            issues[0].Message = "W7 (Level 2): 3 of its 12 ft is beyond the edge of the Level 1 floor.";
            var list = new IssueList(null) { Here = SuiteApps.ShearWalls, FixElsewhere = i => { }, Width = 440 };
            list.Show(issues);
            var items = new List<ReviewItem>
            {
                new ReviewItem { Id = "a", App = SuiteApps.ShearWalls, Title = "Hold-downs with no dead load", Text = "each hold-down is sized for the overturning alone." },
                new ReviewItem { Id = "b", App = SuiteApps.ShearWalls, Title = "The ledger's gap", Text = "the ledger nails cross the thickest sheathing: W5 0.5 in." },
            };
            var ticks = new ReviewSection();
            ticks.Mark(items[0], true, new System.DateTime(2026, 11, 14));
            list.ShowReview(items, ticks);
            var host = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.White, Child = list, Padding = new System.Windows.Thickness(8) };
            var bmp = SketchTests.Render(host, "issue-list");
            Assert.IsTrue(SketchTests.Count(bmp, ((System.Windows.Media.SolidColorBrush)Ui.Info).Color, 30) > 50, "the review tier and the fix link in blue");
            Assert.IsTrue(SketchTests.Count(bmp, PlanStatus.Fail, 30) > 20, "the errors in red");
        }

        [STATestMethod]
        public void FixLink_OnlyForAnotherAppsIssue_WhenTheHostCanOpenIt()
        {
            var list = new IssueList(null) { Here = SuiteApps.ShearWalls };
            var model = new ModelIssue { Severity = Severity.Error, FixIn = SuiteApps.Building, Message = "m" };
            var own = new ModelIssue { Severity = Severity.Error, FixIn = null, Message = "o" };
            Assert.IsFalse(list.LinksOut(model), "no way to open it: no link");
            ModelIssue opened = null;
            list.FixElsewhere = i => opened = i;
            Assert.IsTrue(list.LinksOut(model));
            Assert.IsFalse(list.LinksOut(own), "its own");
            Assert.IsFalse(new IssueList(null) { Here = SuiteApps.Building, FixElsewhere = i => { } }.LinksOut(model), "the Building Model's own");
            Assert.AreEqual("Fix in the Building Model ↗", list.FixLink(model).Text);
        }
    }
}
