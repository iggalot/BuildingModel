using System;
using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// The Building Model with unsaved changes when another app saves (<see cref="ModelMerge.TakeOthersData"/>): the other apps'
    /// data becomes the file's, its own edits and data stay, and an object the file does not have keeps its own.
    /// </summary>
    [TestClass]
    public class TakeOthersDataTests
    {
        private static bool Owns(string k) => k.StartsWith("Sketch.", StringComparison.Ordinal);

        private static Building Box()
        {
            var b = Building.New("Box");
            BuildingEdits.BuildFromSize(b, new BuildingEdits.QuickBuilding { Length = 40, Width = 30, StoryHeights = { 10 } });
            return b;
        }

        [TestMethod]
        public void TheOtherAppsData_BecomesTheFiles_TheEditsHereStay()
        {
            var opened = Box();
            opened.SetData("Lateral", "old forces");
            opened.SetData("Wind", "115 mph");
            opened.Walls[0].SetData("ShearWall", "old design");

            // here: a wall moved and a plan image, not saved yet
            var here = ModelFile.Clone(opened);
            here.Walls[1].End = new Geometry.Point2(here.Walls[1].End.X + 2, here.Walls[1].End.Y);
            here.SetData("Sketch.Plan", "my plan");
            var drawn = new Wall { Id = "W99", BaseLevelId = here.Walls[0].BaseLevelId, TopLevelId = here.Walls[0].TopLevelId };
            drawn.SetData("ShearWall", "copied with the wall");
            here.Walls.Add(drawn);

            // the shear wall app saves: new forces, a new design, the wind left out of nothing (it removed nothing)
            var file = ModelFile.Clone(opened);
            file.SetData("Lateral", "new forces");
            file.Walls[0].SetData("ShearWall", "new design");
            file.Walls[2].SetData("ShearWall", "declared");
            file.SetData("Wind", null);

            ModelMerge.TakeOthersData(file, here, Owns);

            Assert.AreEqual("new forces", here.GetData("Lateral"));
            Assert.IsNull(here.GetData("Wind"), "a key the file no longer has goes");
            Assert.AreEqual("new design", here.Walls[0].GetData("ShearWall"));
            Assert.AreEqual("declared", here.Walls[2].GetData("ShearWall"));
            Assert.AreEqual("my plan", here.GetData("Sketch.Plan"), "this app's own data stays");
            Assert.AreEqual(opened.Walls[1].End.X + 2, here.Walls[1].End.X, 1e-9, "the unsaved geometry stays");
            Assert.AreEqual("copied with the wall", here.Walls.Single(w => w.Id == "W99").GetData("ShearWall"), "not in the file: its own, as the merge keeps it");
        }
    }
}
