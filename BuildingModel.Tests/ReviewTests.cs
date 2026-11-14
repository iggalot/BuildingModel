using System;
using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// The engineer's review list (owner 2026-11-14, UI audit A7): each app's ticks in its own key, a tick holding for the
    /// assumption's text only; and where each issue is fixed (A6).
    /// </summary>
    [TestClass]
    public class ReviewTests
    {
        private static ReviewItem Item(string text) => new ReviewItem { Id = "ledger-gap", App = SuiteApps.ShearWalls, Title = "Ledger gap", Text = text };

        [TestMethod]
        public void Tick_SavedInTheAppsOwnKey_AndReadBack()
        {
            var b = Building.New();
            var s = new ReviewSection();
            s.Mark(Item("the gap taken as 0.5 in"), true, new DateTime(2026, 11, 14));
            ReviewSection.Write(b, SuiteApps.ShearWalls, s);
            Assert.IsNotNull(b.GetData("Review.shearwalls"), "its own key");
            Assert.IsNull(b.GetData("Review.loads"), "no other app's");
            var back = ReviewSection.Read(b, SuiteApps.ShearWalls);
            Assert.IsTrue(back.IsReviewed(Item("the gap taken as 0.5 in")));
            Assert.AreEqual("2026-11-14", back.TickOf(Item("the gap taken as 0.5 in")).On);
        }

        [TestMethod]
        public void Tick_HoldsForItsTextOnly_AChangedAssumptionIsReviewedAgain()
        {
            var s = new ReviewSection();
            s.Mark(Item("the gap taken as 0.5 in"), true);
            Assert.IsFalse(s.IsReviewed(Item("the gap taken as 0.625 in")), "the values changed: not reviewed");
            s.Mark(Item("the gap taken as 0.625 in"), true);
            Assert.AreEqual(1, s.Ticks.Count, "the old tick replaced");
            s.Mark(Item("the gap taken as 0.625 in"), false);
            Assert.IsTrue(s.IsEmpty);
        }

        [TestMethod]
        public void Untick_TheLastOne_RemovesTheKey()
        {
            var b = Building.New();
            var s = new ReviewSection();
            s.Mark(Item("x"), true);
            ReviewSection.Write(b, SuiteApps.ShearWalls, s);
            s.Mark(Item("x"), false);
            ReviewSection.Write(b, SuiteApps.ShearWalls, s);
            Assert.IsNull(b.GetData("Review.shearwalls"));
        }

        [TestMethod]
        public void ModelChecks_AreFixedInTheBuildingModel()
        {
            var b = Building.New();
            var l2 = b.AddLevel("Level 2", 10);
            b.AddWall(b.Ground, l2, 0, 0, 20, 0);
            var issues = ModelChecks.Run(b);
            Assert.IsTrue(issues.Count > 0, "a wall with no diaphragm over it");
            Assert.IsTrue(issues.All(i => i.FixIn == SuiteApps.Building));
            Assert.AreEqual("the Building Model", SuiteApps.Name(SuiteApps.Building));
        }

        [TestMethod]
        public void NotDesigned_ByItsCodeOrMessage_ErrorsOnly()
        {
            Assert.IsTrue(new ModelIssue { Severity = Severity.Error, Code = "DiaphragmNotDesigned" }.NotDesigned);
            Assert.IsTrue(new ModelIssue { Severity = Severity.Error, Code = "DiaphragmStep", Message = "no wall under it: NOT DESIGNED" }.NotDesigned);
            Assert.IsFalse(new ModelIssue { Severity = Severity.Warning, Message = "NOT DESIGNED" }.NotDesigned);
            Assert.IsFalse(new ModelIssue { Severity = Severity.Error, Code = "BeamNotHeldUp" }.NotDesigned);
        }
    }
}
