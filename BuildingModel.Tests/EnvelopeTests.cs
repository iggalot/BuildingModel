using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>The building envelope from the model alone: L, B, the story heights, and each outside face shared between the levels that reach it.</summary>
    [TestClass]
    public class EnvelopeTests
    {
        private const double Tol = 1e-9;

        private static Building Box(out Level l2, out Level l3, out Level roof)
        {
            var b = Building.New();
            l2 = b.AddLevel("Level 2", 12);
            l3 = b.AddLevel("Level 3", 22);
            roof = b.AddLevel("Roof", 31);
            foreach (var l in new[] { l2, l3, roof }) b.AddDiaphragm(l, new Rect2(0, 0, 60, 40));
            return b;
        }

        [TestMethod]
        public void Box_LengthWidthStories()
        {
            var b = Box(out _, out _, out _);
            var env = b.Envelope();
            Assert.AreEqual(60, env.Length, Tol);
            Assert.AreEqual(40, env.Width, Tol);
            CollectionAssert.AreEqual(new[] { 12.0, 10.0, 9.0 }, env.StoryHeights());
            Assert.AreEqual(31, env.EaveHeight, Tol);
            Assert.IsFalse(env.Notes.Any(n => n.Contains("not a rectangle")));
        }

        /// <summary>Mid-story to mid-story on every face; the ground takes the bottom half of the first story.</summary>
        [TestMethod]
        public void Box_StripsAndShares()
        {
            var b = Box(out var l2, out var l3, out var roof);
            var env = b.Envelope();
            var south = env.Strips.Where(s => s.Face == Face.South).ToList();
            Assert.AreEqual(4, south.Count, "ground, level 2, level 3, roof; each the full 60 ft");
            Assert.IsTrue(south.All(s => s.Length == 60));
            var s2 = south.Single(s => s.LevelId == l2.Id);
            Assert.AreEqual(6, s2.Bottom, Tol);
            Assert.AreEqual(17, s2.Top, Tol);
            Assert.IsTrue(double.IsPositiveInfinity(south.Single(s => s.LevelId == roof.Id).Top));
            var shares = env.Shares(Face.South, 31);
            Assert.AreEqual(6 / 31.0, shares[b.Ground.Id], Tol);
            Assert.AreEqual(11 / 31.0, shares[l2.Id], Tol);
            Assert.AreEqual(9.5 / 31.0, shares[l3.Id], Tol);
            Assert.AreEqual(4.5 / 31.0, shares[roof.Id], Tol);
            Assert.AreEqual(1, shares.Values.Sum(), Tol);
        }

        /// <summary>
        /// A lobby at the south-west corner, open from the ground to level 3: over its 20 ft of the south face the wall spans
        /// from the ground to level 3, so level 2 takes nothing there and the ground and level 3 each take half the span.
        /// </summary>
        [TestMethod]
        public void LobbyAtTheFacade_WallSpansPastTheMissingFloor()
        {
            var b = Box(out var l2, out var l3, out var roof);
            b.AddOpening(b.Ground, l3, new Rect2(0, 0, 20, 20));
            var env = b.Envelope();
            var south = env.Strips.Where(s => s.Face == Face.South).ToList();
            Assert.AreEqual(40, south.Where(s => s.LevelId == l2.Id).Sum(s => s.Length), Tol, "level 2 reaches only x 20..60");
            var l3OverLobby = south.Single(s => s.LevelId == l3.Id && s.From == 0);
            Assert.AreEqual(20, l3OverLobby.Length, Tol);
            Assert.AreEqual(11, l3OverLobby.Bottom, Tol, "half of the 22 ft span from the ground");
            Assert.AreEqual(26.5, l3OverLobby.Top, Tol);
            var groundOverLobby = south.Single(s => s.LevelId == b.Ground.Id && s.From == 0);
            Assert.AreEqual(11, groundOverLobby.Top, Tol);
            Assert.AreEqual(1, env.Shares(Face.South, 31).Values.Sum(), Tol);
            // the north face is untouched
            Assert.AreEqual(60, env.Strips.Single(s => s.Face == Face.North && s.LevelId == l2.Id).Length, Tol);
        }

        /// <summary>An upper story set back: over the east part the lower roof (level 2) is the top of the wall.</summary>
        [TestMethod]
        public void SetBack_LowerRoofTopsTheWall()
        {
            var b = Building.New();
            var l2 = b.AddLevel("Level 2", 12);
            var roof = b.AddLevel("Roof", 22);
            b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            b.AddDiaphragm(roof, new Rect2(0, 0, 30, 40));
            var env = b.Envelope();
            var east = env.Strips.Where(s => s.Face == Face.East).ToList();
            var e2 = east.Single(s => s.LevelId == l2.Id);
            Assert.AreEqual(6, e2.Bottom, Tol);
            Assert.AreEqual(12, e2.Top, Tol, "nothing above level 2 on the east face");
            Assert.IsFalse(east.Any(s => s.LevelId == roof.Id));
            var south = env.Strips.Where(s => s.Face == Face.South && s.LevelId == l2.Id).ToList();
            Assert.AreEqual(17, south.Single(s => s.From == 0).Top, Tol, "under the upper story: mid-story");
            Assert.AreEqual(12, south.Single(s => s.From == 30).Top, Tol, "beside it: the lower roof");
            Assert.AreEqual(60, env.Length, Tol);
        }

        [TestMethod]
        public void LShape_UsesTheEnclosingRectangle_AndSaysSo()
        {
            var b = Building.New();
            var roof = b.AddLevel("Roof", 12);
            b.AddDiaphragm(roof, new Rect2(0, 0, 60, 20), new Rect2(0, 0, 20, 40));
            var env = b.Envelope();
            Assert.AreEqual(60, env.Length, Tol);
            Assert.AreEqual(40, env.Width, Tol);
            Assert.IsTrue(env.Notes.Any(n => n.Contains("not a rectangle")));
            Assert.AreEqual(20, env.Strips.Where(s => s.Face == Face.North && s.LevelId == roof.Id).Sum(s => s.Length), Tol,
                "only the wing reaches the north line of the enclosing rectangle");
        }

        [TestMethod]
        public void FourLevels_EveryLevelHasStrips()
        {
            var b = Building.New();
            for (int i = 1; i <= 4; i++) b.AddDiaphragm(b.AddLevel("L" + i, 10 * i), new Rect2(0, 0, 30, 30));
            var env = b.Envelope();
            Assert.AreEqual(4, env.Diaphragms.Count);
            Assert.AreEqual(40, env.EaveHeight, Tol);
            Assert.AreEqual(5, env.Strips.Count(s => s.Face == Face.West), "ground and four levels");
        }
    }
}
