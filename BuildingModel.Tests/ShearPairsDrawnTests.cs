using System.Collections.Generic;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>Which half of a shear wall's horizontal pair a drawing shows (the hold-downs' rule, WISE), and its worst case.</summary>
    [TestClass]
    public class ShearPairsDrawnTests
    {
        private static ShearPair Top() => new ShearPair { WallId = "W1", LevelId = "L2", AtTop = true, OnWall = new Dictionary<string, double> { ["A (rigid)"] = 1500, ["B (rigid)"] = -2500 } };
        private static ShearPair Anchors() => new ShearPair { WallId = "W1", LevelId = "L1", OnFoundation = true, OnWall = new Dictionary<string, double> { ["A (rigid)"] = -1500 } };

        [TestMethod]
        public void TheWholeStructure_TheInsidePairsNot_TheFoundationsReaction()
        {
            Assert.IsFalse(ShearPairs.Drawn(Top(), true, null, false), "wall and diaphragm both in the body");
            Assert.IsFalse(ShearPairs.Drawn(Top(), false, null, false));
            Assert.IsTrue(ShearPairs.Drawn(Anchors(), true, null, false), "the support's reaction on the wall");
            Assert.IsFalse(ShearPairs.Drawn(Anchors(), false, null, false), "the foundation is not drawn as a body");
        }

        [TestMethod]
        public void AWallPicked_TheHalfOnIt_AtTheCut()
        {
            var body = new HashSet<string> { "W1" };
            Assert.IsTrue(ShearPairs.Drawn(Top(), true, body, false), "the diaphragm is outside: the half on the wall");
            Assert.IsFalse(ShearPairs.Drawn(Top(), false, body, false));
            Assert.IsFalse(ShearPairs.Drawn(Top(), true, new HashSet<string> { "W9" }, false), "another wall's pair: neither");
        }

        [TestMethod]
        public void InternalForcesShown_BothHalves_TheWorstCaseEachWay()
        {
            Assert.IsTrue(ShearPairs.Drawn(Top(), true, null, true));
            Assert.IsTrue(ShearPairs.Drawn(Top(), false, null, true));
            Assert.AreEqual(-2500, ShearPairs.Worst(Top(), true));
            Assert.AreEqual(2500, ShearPairs.Worst(Top(), false), "the other half: equal and opposite");
        }
    }
}
