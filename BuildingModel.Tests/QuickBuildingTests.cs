using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>"New from size": a box L × B with its stories, a diaphragm on each level and the outside shear walls.</summary>
    [TestClass]
    public class QuickBuildingTests
    {
        private const double Tol = 1e-9;

        [TestMethod]
        public void ThreeStories_LevelsDiaphragmsOutsideWalls_StandardNames()
        {
            var b = Building.New("Clinic");
            var q = new BuildingEdits.QuickBuilding { Length = 80, Width = 36, StoryHeights = { 15, 12, 12 } };
            Assert.IsTrue(BuildingEdits.BuildFromSize(b, q));
            var up = b.LevelsBottomUp.Where(l => l.Elevation > 0).ToList();
            CollectionAssert.AreEqual(new[] { 15.0, 27.0, 39.0 }, up.Select(l => l.Elevation).ToArray());
            CollectionAssert.AreEqual(new[] { "Level 2", "Level 3", "Roof" }, up.Select(l => l.Name).ToArray());
            Assert.AreEqual(3, b.Diaphragms.Count);
            Assert.IsTrue(up.All(l => b.GrossDiaphragm(l).Area == 80 * 36));
            Assert.AreEqual(12, b.Walls.Count);
            Assert.IsTrue(b.Walls.All(w => !w.IsShearWall && b.IsExterior(w)), "all outside walls, plain: the shear walls are declared in the Shear Wall Calculator");
            Assert.AreEqual(4, b.Walls.Count(w => w.TopLevelId == up[1].Id && w.BaseLevelId == up[0].Id), "one story each");
            Assert.AreEqual("Clinic", b.Name);
            Assert.IsFalse(ModelChecksErrors(b), "a clean model");
            var env = b.Envelope();
            Assert.AreEqual(80, env.Length, Tol);
            Assert.AreEqual(36, env.Width, Tol);
        }

        [TestMethod]
        public void ReplacesTheDrawing_KeepsRoofDefaultsAndData_NoWallsWhenAsked()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 20, 20));
            b.AddWall(b.Ground, l2, 5, 5, 5, 15);
            b.AddNote(l2, 1, 1, "old");
            b.Roof = new Roof { Shape = RoofShape.Hip, Pitch = 30 };
            b.Defaults.StoryHeight = 11;
            b.SetData("Wind", "{\"Exposure\":\"B\"}");
            var q = new BuildingEdits.QuickBuilding { Length = 50, Width = 30, StoryHeights = { 9 }, OutsideShearWalls = false };
            Assert.IsTrue(BuildingEdits.BuildFromSize(b, q));
            Assert.AreEqual(0, b.Walls.Count);
            Assert.AreEqual(0, b.Notes.Count);
            Assert.AreEqual(1, b.Diaphragms.Count);
            Assert.AreEqual("Level 2", b.LevelsBottomUp.Last().Name, "one story: Level 2");
            Assert.AreEqual(RoofShape.Hip, b.Roof.Shape);
            Assert.AreEqual(11, b.Defaults.StoryHeight);
            Assert.AreEqual("{\"Exposure\":\"B\"}", b.GetData("Wind"));
            Assert.IsNotNull(b.Ground);
        }

        [TestMethod]
        public void BadSizes_ChangeNothing()
        {
            var b = Building.New();
            BuildingEdits.AddStory(b, 10);
            foreach (var q in new[]
            {
                new BuildingEdits.QuickBuilding { Length = 0, Width = 30, StoryHeights = { 10 } },
                new BuildingEdits.QuickBuilding { Length = 30, Width = 30 },
                new BuildingEdits.QuickBuilding { Length = 30, Width = 30, StoryHeights = { 10, -2 } },
            })
            {
                Assert.IsNotNull(q.Problem());
                Assert.IsFalse(BuildingEdits.BuildFromSize(b, q));
            }
            Assert.AreEqual(2, b.Levels.Count);
        }

        /// <summary>The window starts from the model: its defaults for a new building, the box it is for one.</summary>
        [TestMethod]
        public void StartsFromTheModelsDefaults_OrTheBuildingAsItIs()
        {
            var b = Building.New();
            b.Defaults.NewLength = 72;
            b.Defaults.StoryHeight = 13;
            var q = BuildingEdits.QuickBuilding.Of(b);
            Assert.AreEqual(72, q.Length);
            Assert.AreEqual(b.Defaults.NewWidth, q.Width);
            CollectionAssert.AreEqual(new[] { 13.0 }, q.StoryHeights);
            BuildingEdits.BuildFromSize(b, new BuildingEdits.QuickBuilding { Length = 44, Width = 22, StoryHeights = { 10, 9 } });
            q = BuildingEdits.QuickBuilding.Of(b);
            Assert.AreEqual(44, q.Length, Tol);
            CollectionAssert.AreEqual(new[] { 10.0, 9.0 }, q.StoryHeights);
        }

        [TestMethod]
        public void NameStories_FollowsTheStories_KeepsTheUsersNames()
        {
            var b = Building.New();
            var a = BuildingEdits.AddStory(b, 10, "Roof");
            BuildingEdits.NameStories(b);
            Assert.AreEqual("Level 2", a.Name, "one story");
            var c = BuildingEdits.AddStory(b, 10);
            var d = BuildingEdits.AddStory(b, 10, "Mechanical");
            BuildingEdits.NameStories(b);
            Assert.AreEqual("Level 2", a.Name);
            Assert.AreEqual("Level 3", c.Name);
            Assert.AreEqual("Mechanical", d.Name, "the user's name stays");
        }

        private static bool ModelChecksErrors(Building b) => Checks.ModelChecks.Run(b).Any(i => i.Severity == Checks.Severity.Error);
    }
}
