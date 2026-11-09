using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>The model defaults window: the project's values shown and given back, one not offered kept, the starting values the defaults' own, a bad number refused.</summary>
    [TestClass]
    public class ModelDefaultsWindowTests
    {
        [STATestMethod]
        public void ShowsTheValues_KeepsOneNotOffered_StartsFromTheDefaults()
        {
            var d = new ModelDefaults { StudSizeFirstStory = "2x8", StudSpecies = "SprucePineFir", FloorJoistSize = "2x12", RoofBlockingSize = "2x14", RafterSpacing = 19.2, RoofPitch = 26.57, StoryHeight = 9 };
            var w = new ModelDefaultsWindow(d);
            var v = w.Values();
            Assert.IsNull(w.Problem, w.Problem);
            Assert.AreNotSame(d, v, "a copy");
            Assert.AreEqual("2x8", v.StudSizeFirstStory);
            Assert.AreEqual("SprucePineFir", v.StudSpecies);
            Assert.AreEqual("2x12", v.FloorJoistSize);
            Assert.AreEqual("2x14", v.RoofBlockingSize, "not offered: kept as it is");
            Assert.AreEqual(19.2, v.RafterSpacing, 1e-9);
            Assert.AreEqual(26.57, v.RoofPitch, 0.01, "6:12 shown as a rise and read back");
            Assert.AreEqual(6, ModelDefaultsWindow.RiseOf(26.57), 0.01);
            Assert.AreEqual(9, v.StoryHeight);

            w.StartingValues();
            var s = w.Values();
            var start = new ModelDefaults();
            Assert.AreEqual(start.StudSizeFirstStory, s.StudSizeFirstStory);
            Assert.AreEqual(start.FloorJoistSize, s.FloorJoistSize);
            Assert.AreEqual(start.RoofBlockingSize, s.RoofBlockingSize);
            Assert.AreEqual(start.StoryHeight, s.StoryHeight);
            Assert.AreEqual(start.RoofPitch, s.RoofPitch, 0.01);
            w.Close();
        }

        [STATestMethod]
        public void ABadNumber_KeepsTheOldValue_AndSaysSo()
        {
            var w = new ModelDefaultsWindow(new ModelDefaults { StoryHeight = 10 });
            var box = (System.Windows.Controls.TextBox)((System.Windows.Controls.Grid)w.Content).Children[2]; // the story height
            box.Text = "tall";
            var v = w.Values();
            Assert.AreEqual(10, v.StoryHeight);
            StringAssert.Contains(w.Problem, "Story height: \"tall\"");
            w.Close();
        }
    }
}
