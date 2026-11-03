using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>Format version 5: a wall's stud framing per story, and the project's stud defaults.</summary>
    [TestClass]
    public class FramingTests
    {
        /// <summary>Two stories: level 2 at 10 ft, roof at 19 ft; one wall a story.</summary>
        private static Building TwoStory(out Wall first, out Wall upper, out Level l2, out Level roof)
        {
            var b = Building.New("Two");
            l2 = b.AddLevel("Level 2", 10);
            roof = b.AddLevel("Roof", 19);
            first = b.AddWall(b.Ground, l2, 0, 0, 40, 0);
            upper = b.AddWall(l2, roof, 0, 0, 40, 0);
            return b;
        }

        [TestMethod]
        public void Defaults_StartAt2x6Below2x4AboveAt16()
        {
            var d = new ModelDefaults();
            Assert.AreEqual("2x6", d.StudSizeFirstStory);
            Assert.AreEqual("2x4", d.StudSizeAbove);
            Assert.AreEqual(16, d.StudSpacing);
        }

        [TestMethod]
        public void IsFirstStory_OnlyTheStoryOnTheGround()
        {
            var b = TwoStory(out _, out _, out var l2, out var roof);
            Assert.IsTrue(b.IsFirstStory(l2.Id));
            Assert.IsFalse(b.IsFirstStory(roof.Id));
            Assert.IsFalse(b.IsFirstStory(b.Ground.Id));
            Assert.IsFalse(b.IsFirstStory("nope"));
        }

        [TestMethod]
        public void StudSizeAndSpacing_FallBackToTheProjectDefaults()
        {
            var b = TwoStory(out var first, out var upper, out var l2, out var roof);
            Assert.AreEqual("2x6", b.StudSizeOf(first, l2.Id));
            Assert.AreEqual("2x4", b.StudSizeOf(upper, roof.Id));
            Assert.AreEqual(16, b.StudSpacingOf(upper, roof.Id));

            b.Defaults.StudSizeAbove = "2x6";
            b.Defaults.StudSpacing = 24;
            Assert.AreEqual("2x6", b.StudSizeOf(upper, roof.Id));
            Assert.AreEqual(24, b.StudSpacingOf(first, l2.Id));
        }

        [TestMethod]
        public void SetStudFraming_SetsChangesAndClearsBackToTheDefault()
        {
            var b = TwoStory(out var first, out _, out var l2, out _);
            Assert.IsTrue(BuildingEdits.SetStudFraming(b, first.Id, l2.Id, "2x8", 0));
            Assert.AreEqual("2x8", b.StudSizeOf(first, l2.Id));
            Assert.AreEqual(16, b.StudSpacingOf(first, l2.Id));          // spacing still the project's
            Assert.IsFalse(BuildingEdits.SetStudFraming(b, first.Id, l2.Id, "2x8", 0)); // no change
            Assert.IsTrue(BuildingEdits.SetStudFraming(b, first.Id, l2.Id, "2x8", 12));
            Assert.AreEqual(12, b.StudSpacingOf(first, l2.Id));

            Assert.IsTrue(BuildingEdits.SetStudFraming(b, first.Id, l2.Id, null, 0));
            Assert.IsNull(first.Framing);                                   // nothing of its own left
            Assert.AreEqual("2x6", b.StudSizeOf(first, l2.Id));
            Assert.IsFalse(BuildingEdits.SetStudFraming(b, "nope", l2.Id, "2x8", 0));
        }

        [TestMethod]
        public void Framing_RoundTripsAndCopiesWithTheWall()
        {
            var b = TwoStory(out var first, out _, out var l2, out _);
            b.Defaults.StudSizeAbove = "2x6";
            BuildingEdits.SetStudFraming(b, first.Id, l2.Id, "2x8", 12);
            var back = ModelFile.FromJson(ModelFile.ToJson(new ModelDocument { Building = b })).Building;
            var w = back.Walls.Single(x => x.Id == first.Id);
            Assert.AreEqual("2x8", back.StudSizeOf(w, l2.Id));
            Assert.AreEqual(12, back.StudSpacingOf(w, l2.Id));
            Assert.AreEqual("2x6", back.Defaults.StudSizeAbove);

            string copy = BuildingEdits.Copy(b, first.Id, 0, 10);
            var c = (Wall)b.Find(copy);
            Assert.AreEqual("2x8", b.StudSizeOf(c, l2.Id));
            BuildingEdits.SetStudFraming(b, copy, l2.Id, "2x6", 0);
            Assert.AreEqual("2x8", b.StudSizeOf(first, l2.Id));             // the copy has its own
        }

        [TestMethod]
        public void Version4File_ReadsTheStudDefaults()
        {
            const string v4 = "{\"Format\":\"BuildingModel\",\"Version\":4,\"Revision\":1,\"Building\":{\"Name\":\"Old\"," +
                              "\"Levels\":[{\"Id\":\"L1\",\"Name\":\"Ground\",\"Elevation\":0},{\"Id\":\"L2\",\"Name\":\"Roof\",\"Elevation\":10}]," +
                              "\"Walls\":[{\"Id\":\"W1\",\"Start\":{\"X\":0,\"Y\":0},\"End\":{\"X\":20,\"Y\":0},\"BaseLevelId\":\"L1\",\"TopLevelId\":\"L2\",\"IsShearWall\":true}]," +
                              "\"Defaults\":{\"StoryHeight\":9}}}";
            var b = ModelFile.FromJson(v4).Building;
            var w = b.Walls.Single();
            Assert.IsNull(w.Framing);
            Assert.AreEqual(9, b.Defaults.StoryHeight);
            Assert.AreEqual("2x6", b.StudSizeOf(w, "L2"));
            Assert.AreEqual(16, b.StudSpacingOf(w, "L2"));
        }
    }
}
