using System.IO;
using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>Levels, diaphragms with openings (multistory lobbies, mezzanines), walls that span levels, and the checks.</summary>
    [TestClass]
    public class BuildingTests
    {
        private const double Tol = 1e-9;

        /// <summary>
        /// A 60 × 40 ft three-story box: ground, level 2 at 12 ft, level 3 at 22 ft, roof at 31 ft. Exterior shear walls run
        /// story by story; a lobby 20 × 20 ft in the south-west corner is open from the ground to level 3 (it cuts level 2).
        /// </summary>
        private static Building ThreeStoryWithLobby(out Level ground, out Level l2, out Level l3, out Level roof, out Opening lobby)
        {
            var b = Building.New("Sample");
            ground = b.Ground;
            l2 = b.AddLevel("Level 2", 12);
            l3 = b.AddLevel("Level 3", 22);
            roof = b.AddLevel("Roof", 31);
            var levels = new[] { ground, l2, l3, roof };
            for (int i = 1; i < levels.Length; i++)
            {
                b.AddDiaphragm(levels[i], new Rect2(0, 0, 60, 40));
                b.AddWall(levels[i - 1], levels[i], 0, 0, 60, 0);
                b.AddWall(levels[i - 1], levels[i], 0, 40, 60, 40);
                b.AddWall(levels[i - 1], levels[i], 0, 0, 0, 40);
                b.AddWall(levels[i - 1], levels[i], 60, 0, 60, 40);
            }
            lobby = b.AddOpening(ground, l3, new Rect2(5, 5, 25, 25));
            return b;
        }

        [TestMethod]
        public void NewBuilding_HasTheGround_AndUniqueIds()
        {
            var b = Building.New();
            Assert.AreEqual(0, b.Ground.Elevation);
            var l = b.AddLevel("Roof", 10);
            var d = b.AddDiaphragm(l, new Rect2(0, 0, 10, 10));
            var w = b.AddWall(b.Ground, l, 0, 0, 10, 0);
            Assert.AreEqual(4, b.AllIds().Distinct().Count());
            Assert.AreEqual("W1", w.Id);
            Assert.AreEqual("D1", d.Id);
        }

        /// <summary>The lobby cuts level 2 only (strictly between the ground and level 3): area and centroid follow.</summary>
        [TestMethod]
        public void Lobby_CutsOnlyTheLevelsBetween()
        {
            var b = ThreeStoryWithLobby(out var ground, out var l2, out var l3, out var roof, out var lobby);
            CollectionAssert.AreEqual(new[] { lobby }, b.OpeningsCutting(l2));
            Assert.AreEqual(0, b.OpeningsCutting(l3).Count, "level 3 closes the lobby");
            Assert.AreEqual(2400 - 400, b.NetDiaphragm(l2).Area, Tol);
            Assert.AreEqual(2400, b.NetDiaphragm(l3).Area, Tol);
            Assert.AreEqual((2400 * 30 - 400 * 15) / 2000.0, b.NetDiaphragm(l2).Centroid.X, Tol);
            Assert.AreEqual(new Rect2(0, 0, 60, 40), b.Footprint().Bounds);
        }

        /// <summary>A lobby wall from the ground to level 3 inside the opening is unbraced at level 2: 22 ft.</summary>
        [TestMethod]
        public void TallWall_InTheLobby()
        {
            var b = ThreeStoryWithLobby(out var ground, out var l2, out var l3, out var roof, out _);
            var tall = b.AddWall(ground, l3, 15, 15, 15, 25, shearWall: false); // stands in the lobby
            CollectionAssert.AreEqual(new[] { l3 }, b.BracedAt(tall).Select(x => x.Level).ToArray());
            Assert.AreEqual(22, b.UnbracedHeight(tall), Tol);
            var issues = ModelChecks.Run(b);
            Assert.IsTrue(issues.Any(i => i.Code == "TallWall" && i.Ids.Contains(tall.Id)), string.Join("\n", issues));
            var exterior = b.Walls.First();
            Assert.AreEqual(12, b.UnbracedHeight(exterior), Tol);
        }

        /// <summary>A mezzanine over half the ground floor: a partial diaphragm, held up by the walls that reach it.</summary>
        [TestMethod]
        public void Mezzanine_PartialDiaphragm()
        {
            var b = Building.New();
            var ground = b.Ground;
            var mezz = b.AddLevel("Mezzanine", 8);
            var roof = b.AddLevel("Roof", 18);
            b.AddDiaphragm(roof, new Rect2(0, 0, 60, 40));
            b.AddDiaphragm(mezz, new Rect2(0, 0, 30, 40));
            // two-story exterior walls (they pass the mezzanine level), plus walls under the mezzanine's open edge
            b.AddWall(ground, roof, 0, 0, 60, 0);
            b.AddWall(ground, roof, 0, 40, 60, 40);
            b.AddWall(ground, roof, 0, 0, 0, 40);
            b.AddWall(ground, roof, 60, 0, 60, 40);
            var east = b.AddWall(ground, roof, 30, 0, 30, 10, shearWall: false); // a column line, not a shear wall
            Assert.AreEqual(1200, b.NetDiaphragm(mezz).Area, Tol);
            Assert.AreEqual(2, b.ShearWallsUnder(mezz, WallDirection.WestEast).Count, "the south and north walls reach it");
            Assert.AreEqual(1, b.ShearWallsUnder(mezz, WallDirection.SouthNorth).Count, "the west wall");
            var issues = ModelChecks.Run(b);
            Assert.IsFalse(issues.Any(i => i.Code == "NoLateralSupport"), string.Join("\n", issues));
            // the east wall passes the mezzanine level where there is no mezzanine
            Assert.IsTrue(issues.Any(i => i.Code == "TallWall" && i.Ids.Contains(b.Walls[3].Id)));
            Assert.AreEqual(18, b.UnbracedHeight(b.Walls[3]), Tol);
            Assert.AreEqual(10, b.UnbracedHeight(b.Walls[2]), Tol, "the west wall is braced by the mezzanine at 8 ft");
            Assert.AreEqual(8, b.BracedAt(east).Single(x => x.Level == mezz).Level.Elevation, Tol);
        }

        /// <summary>Openings over half a level: ASCE 7 diaphragm discontinuity. An opening at the edge: chord interrupted.</summary>
        [TestMethod]
        public void Checks_DiscontinuityAndOpeningAtEdge()
        {
            var b = ThreeStoryWithLobby(out var ground, out var l2, out var l3, out _, out _);
            var big = b.AddOpening(l2, b.Levels.Single(l => l.Name == "Roof"), new Rect2(0, 0, 50, 30)); // cuts level 3, reaches its edge
            var issues = ModelChecks.Run(b);
            Assert.IsTrue(issues.Any(i => i.Code == "DiaphragmDiscontinuity" && i.Ids.Contains(l3.Id)), string.Join("\n", issues));
            Assert.IsTrue(issues.Any(i => i.Code == "OpeningAtEdge" && i.Ids.Contains(big.Id)));
            Assert.IsFalse(issues.Any(i => i.Code == "OpeningAtEdge" && !i.Ids.Contains(big.Id)), "the lobby is inside level 2");
        }

        [TestMethod]
        public void Checks_ModelErrors()
        {
            var b = Building.New();
            var l = b.AddLevel("Level 2", 10);
            var dup = b.AddLevel("Also 10", 10);
            b.AddWall(b.Ground, l, 0, 0, 10, 5); // diagonal
            b.AddWall(l, b.Ground, 0, 0, 10, 0); // upside down
            b.AddOpening(l, b.Ground, new Rect2(0, 0, 1, 1));
            b.Walls.Add(new Wall { Id = "W9", BaseLevelId = "nope", TopLevelId = l.Id, Start = new Point2(0, 0), End = new Point2(1, 0) });
            var codes = ModelChecks.Run(b).Select(i => i.Code).ToList();
            foreach (var c in new[] { "LevelElevation", "WallNotOrthogonal", "WallLevels", "OpeningLevels", "BadReference", "NoDiaphragm" })
                Assert.IsTrue(codes.Contains(c), c + " in " + string.Join(", ", codes));
            Assert.IsTrue(ModelChecks.Run(new Building()).Any(i => i.Code == "NoGround"));
        }

        /// <summary>The three-story box with its lobby has nothing wrong with it.</summary>
        [TestMethod]
        public void Checks_CleanModel_NoErrors()
        {
            var b = ThreeStoryWithLobby(out _, out _, out _, out _, out _);
            var issues = ModelChecks.Run(b);
            Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
        }

        [TestMethod]
        public void File_RoundTrip_RevisionAndVersion()
        {
            var b = ThreeStoryWithLobby(out _, out var l2, out _, out _, out _);
            var doc = new ModelDocument { Building = b };
            string path = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "bm_roundtrip_" + System.Guid.NewGuid().ToString("N") + ".bldg.json");
            try
            {
                ModelFile.Save(path, doc);
                Assert.AreEqual(1, doc.Revision);
                var read = ModelFile.Load(path);
                Assert.AreEqual(1, read.Revision);
                Assert.AreEqual(b.Levels.Count, read.Building.Levels.Count);
                Assert.AreEqual(b.Walls.Count, read.Building.Walls.Count);
                Assert.AreEqual(b.Walls[5].End, read.Building.Walls[5].End);
                var rl2 = read.Building.Level(l2.Id);
                Assert.AreEqual(b.NetDiaphragm(l2).Area, read.Building.NetDiaphragm(rl2).Area, Tol);
            }
            finally { File.Delete(path); }

            var newer = ModelFile.ToJson(new ModelDocument { Version = 99, Building = b });
            Assert.Throws<InvalidDataException>(() => ModelFile.FromJson(newer));
            Assert.Throws<InvalidDataException>(() => ModelFile.FromJson("{\"Format\":\"Other\",\"Version\":1}"));
            Assert.Throws<InvalidDataException>(() => ModelFile.FromJson("not json"));
        }
    }
}
