using System.Linq;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>
    /// Someone else's project opened to look at only: every edit put back (the geometry and the apps' data alike), the tools
    /// that change anything gone, and all of it back when the window opens a project of its own.
    /// </summary>
    [TestClass]
    public class ViewOnlyTests
    {
        [STATestMethod]
        public void ViewOnly_EveryEditPutBack_TheToolsGone_AndBackAfter()
        {
            var b = SketchTests.Sample(out var l2, out _);
            var v = new SketchView();
            var s = v.Canvas;
            s.SetBuilding(b, l2.Id);
            int walls = b.Walls.Count;
            string said = null;
            s.Status += (o, e) => said = e.Text;
            int refused = 0;
            s.EditRefused += (o, e) => refused++;

            v.ViewOnly = true;
            Assert.IsTrue(s.ViewOnly);
            CollectionAssert.AreEquivalent(new[] { SketchTool.Select, SketchTool.Measure }, s.EnabledTools.ToArray(), "only looking and measuring");
            Assert.IsFalse(v.PlanEditing);

            // the geometry: a wall deleted is put back
            s.Select(new[] { b.Walls[0].Id });
            s.DeleteSelected();
            Assert.AreEqual(walls, b.Walls.Count, "the wall put back");
            StringAssert.Contains(said, "View only");
            // an app's data on an object: put back too (unlike GeometryLocked)
            string id = b.Walls[0].Id;
            Assert.IsFalse(s.Edit("data", x => x.Walls[0].SetData("Test.Key", "1")));
            Assert.IsFalse(b.Walls.First(w => w.Id == id).GetData("Test.Key") != null, "the data put back");
            Assert.AreEqual(2, refused);
            Assert.IsFalse(s.History.IsDirty, "nothing to save");
            Assert.IsFalse(s.History.CanUndo);

            // a project of one's own next: the tools and the edits as they were
            v.ViewOnly = false;
            Assert.IsFalse(s.ViewOnly);
            Assert.IsTrue(s.EnabledTools.Contains(SketchTool.Wall));
            Assert.IsTrue(v.PlanEditing);
            s.Select(new[] { b.Walls[0].Id });
            s.DeleteSelected();
            Assert.AreEqual(walls - 1, b.Walls.Count);
        }
    }
}
