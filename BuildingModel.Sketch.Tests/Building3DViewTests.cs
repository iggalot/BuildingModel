using System;
using BuildingModel.Edits;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    [TestClass]
    public class Building3DViewTests
    {
        private static Building Make(RoofShape shape, double pitch)
        {
            var b = Building.New("t");
            Assert.IsTrue(BuildingEdits.BuildFromSize(b, new BuildingEdits.QuickBuilding { Length = 40, Width = 20, StoryHeights = { 10, 10 } }));
            b.Roof = new Roof { Shape = shape, Pitch = pitch };
            return b;
        }

        /// <summary>By hand: a 40 × 20 gable, ridge W-E, 45°: rise = tan 45° × 20 / 2 = 10 ft over the 20 ft eave, so the top is 30 ft.</summary>
        [STATestMethod]
        public void GableRoof_ReachesTheRidge()
        {
            var b = Make(RoofShape.Gable, 45);
            var v = new Building3DView();
            v.Show(b);
            double top = v.ModelBounds.Y + v.ModelBounds.SizeY;
            Assert.AreEqual(30, top, 1e-6);
            Assert.AreEqual(30, b.RidgeHeight(), 1e-6);
        }

        [STATestMethod]
        public void HipRoof_ReachesTheRidge_FlatStopsAtTheEave()
        {
            var v = new Building3DView();
            v.Show(Make(RoofShape.Hip, 45));
            Assert.AreEqual(30, v.ModelBounds.Y + v.ModelBounds.SizeY, 1e-6, "hip: same rise over the shorter side");
            v.Show(Make(RoofShape.Flat, 0));
            Assert.AreEqual(20, v.ModelBounds.Y + v.ModelBounds.SizeY, 1e-6, "flat, no parapet: the top slab");
        }
    }
}
