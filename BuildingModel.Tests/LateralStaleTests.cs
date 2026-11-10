using System.Collections.Generic;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// <see cref="LateralSection.Stale"/>: a saved force is measured from its wall's shear part's ends (the hold-downs are there),
    /// not the wall's own ends.
    /// </summary>
    [TestClass]
    public class LateralStaleTests
    {
        // a 60 ft wall along y = 0, declared a shear wall from 0 to 14 ft
        private static Building Model(double y = 0)
        {
            var b = new Building();
            b.Walls.Add(new Wall { Id = "W1", Start = new Point2(0, y), End = new Point2(60, y) });
            return b;
        }

        private static LateralSection Saved(bool withPart = true)
        {
            var s = new LateralSection();
            s.Points.Add(new LateralPoint { From = "W1 west end", WallId = "W1", X = 0.23, Y = 0 });
            s.Points.Add(new LateralPoint { From = "W1 east end", WallId = "W1", X = 13.77, Y = 0 });
            if (withPart) s.Walls.Add(new ShearWallDesign { WallId = "W1", X0 = 0, Y0 = 0, X1 = 14, Y1 = 0 });
            return s;
        }

        [TestMethod]
        public void APartOfTheWall_ItsEndsInside_IsNotStale()
        {
            Assert.IsNull(Saved().Stale(Model()), "the east hold-down is 14 ft in: the shear part's end, not a moved wall");
        }

        [TestMethod]
        public void TheWallMoved_OffItsShearPart_IsStale()
        {
            string why = Saved().Stale(Model(y: 5));
            StringAssert.Contains(why, "has moved");
        }

        [TestMethod]
        public void APointAwayFromThePartsEnds_IsStale()
        {
            var s = Saved();
            s.Points[1].X = 30; // in the middle of the wall: not a hold-down of this part
            StringAssert.Contains(s.Stale(Model()), "W1 east end");
        }

        [TestMethod]
        public void AnOlderSave_NoParts_MeasuresFromTheWallsEnds()
        {
            Assert.IsNotNull(Saved(withPart: false).Stale(Model()), "no part saved: the wall's own ends, as before");
            var whole = new LateralSection();
            whole.Points.Add(new LateralPoint { From = "W1 east end", WallId = "W1", X = 59.8, Y = 0 });
            Assert.IsNull(whole.Stale(Model()));
        }

        [TestMethod]
        public void TheWallGone_IsStale()
        {
            StringAssert.Contains(Saved().Stale(new Building()), "not in the model any more");
        }
    }
}
