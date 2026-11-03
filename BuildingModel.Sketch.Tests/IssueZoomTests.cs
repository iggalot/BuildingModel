using System.Linq;
using System.Windows.Input;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>An issue picked from a list: the plan area zoomed to frames what it names, at least 25 ft across.</summary>
    [TestClass]
    public class IssueZoomTests
    {
        private static (Building b, Level l2, SketchCanvas s) OneStory()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            return (b, l2, s);
        }

        [STATestMethod]
        public void LongWall_FramedWithMargin_ShortSideGetsTheMinimumSpan()
        {
            var (b, l2, s) = OneStory();
            s.HandleKey(Key.W, ModifierKeys.None);
            s.TypePoint("10,5");
            s.TypePoint("50,5");
            var w = b.Walls.Single();
            var r = s.IssueBounds(new ModelIssue { Ids = { w.Id }, LevelId = l2.Id });
            // 40 ft long × 1.3 = 52 ft wide, centred at x 30; 0 ft tall → 25 ft, centred at y 5
            Assert.AreEqual(52, r.Width, 1e-9);
            Assert.AreEqual(30, r.X + r.Width / 2, 1e-9);
            Assert.AreEqual(SketchCanvas.IssueMinSpan, r.Height, 1e-9);
            Assert.AreEqual(5, r.Y + r.Height / 2, 1e-9);
        }

        [STATestMethod]
        public void ShortWall_AtLeastTheMinimumSpan()
        {
            var (b, l2, s) = OneStory();
            s.HandleKey(Key.W, ModifierKeys.None);
            s.TypePoint("46,0.5");
            s.TypePoint("46,6.5");
            var r = s.IssueBounds(new ModelIssue { Ids = { b.Walls.Single().Id }, LevelId = l2.Id });
            Assert.AreEqual(SketchCanvas.IssueMinSpan, r.Width, 1e-9);
            Assert.AreEqual(SketchCanvas.IssueMinSpan, r.Height, 1e-9);
            Assert.AreEqual(3.5, r.Y + r.Height / 2, 1e-9);
        }

        [STATestMethod]
        public void NoMembers_UsesTheIssuePlace_NothingAtAll_Empty()
        {
            var (_, l2, s) = OneStory();
            var r = s.IssueBounds(new ModelIssue { LevelId = l2.Id, At = new Point2(12, 8) });
            Assert.AreEqual(12, r.X + r.Width / 2, 1e-9);
            Assert.AreEqual(8, r.Y + r.Height / 2, 1e-9);
            Assert.IsTrue(s.IssueBounds(new ModelIssue { LevelId = l2.Id }).IsEmpty);
        }
    }
}
