using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>The ground's plan (the foundation plan) shows, picks and selects what stands on it: a view, the same objects.</summary>
    [TestClass]
    public class FoundationPlanTests
    {
        [TestMethod]
        public void FoundationPlan_ShowsWhatStandsOnIt_TheSameObjects()
        {
            var b = Building.New("Two stories");
            var g = b.Ground;
            var l2 = b.AddLevel("Level 2", 10);
            var roof = b.AddLevel("Roof", 19);
            var first = b.AddWall(g, l2, 0, 0, 30, 0);
            var tall = b.AddWall(g, roof, 0, 20, 30, 20);   // two stories from the foundation
            var upper = b.AddWall(l2, roof, 0, 0, 0, 20);   // the second story only
            var post = b.AddColumn(g, l2, 15, 10);

            Assert.IsTrue(b.IsFoundationPlan(g));
            Assert.IsFalse(b.IsFoundationPlan(l2));
            CollectionAssert.AreEquivalent(new[] { first.Id, tall.Id }, b.WallsShown(g).Select(w => w.Id).ToList());
            CollectionAssert.AreEquivalent(new[] { first.Id, tall.Id }, b.WallsShown(l2).Select(w => w.Id).ToList(), "Level 2 shows the walls under it, as before");

            var onGround = b.VisibleIds(g);
            Assert.IsTrue(onGround.SetEquals(new[] { first.Id, tall.Id, post.Id }), "the walls and the column, nothing copied");
            Assert.AreEqual(first.Id, Snapper.Pick(b, g, new Point2(10, 0.2), 0.5));
            Assert.AreEqual(post.Id, Snapper.Pick(b, g, new Point2(15, 10.1), 0.5));
            Assert.IsNull(Snapper.Pick(b, g, new Point2(0.1, 10), 0.5), "the second story's wall does not stand on the ground");
            CollectionAssert.AreEquivalent(new[] { first.Id, tall.Id, post.Id }, Snapper.InWindow(b, g, new Rect2(-1, -1, 31, 21)));

            Assert.AreEqual(4, b.Walls.Count + b.Columns.Count, "a view only: no objects added");
            Assert.IsFalse(b.WallsShown(l2).Any(w => w.Id == upper.Id));
        }
    }
}
