using System.Linq;
using BuildingModel.Geometry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>Right-angled areas: unions, holes, centroid, outline, and how much of a wall line a diaphragm reaches.</summary>
    [TestClass]
    public class RegionTests
    {
        private const double Tol = 1e-9;

        [TestMethod]
        public void OneRectangle()
        {
            var r = Region.Of(new[] { new Rect2(0, 0, 60, 40) });
            Assert.AreEqual(2400, r.Area, Tol);
            Assert.AreEqual(30, r.Centroid.X, Tol);
            Assert.AreEqual(20, r.Centroid.Y, Tol);
            Assert.AreEqual(4, r.Outline().Count);
            Assert.AreEqual(200, r.Outline().Sum(s => s.Length), Tol);
        }

        /// <summary>Overlapping rectangles count once; an L-shape has six edges.</summary>
        [TestMethod]
        public void LShape_OverlapCountsOnce()
        {
            var r = Region.Of(new[] { new Rect2(0, 0, 40, 20), new Rect2(0, 0, 20, 40) });
            Assert.AreEqual(40 * 20 + 20 * 20, r.Area, Tol);
            Assert.AreEqual(6, r.Outline().Count);
            Assert.AreEqual(160, r.Outline().Sum(s => s.Length), Tol);
            // centroid: 800 at (20,10) + 400 at (10,30)
            Assert.AreEqual((800 * 20 + 400 * 10) / 1200.0, r.Centroid.X, Tol);
            Assert.AreEqual((800 * 10 + 400 * 30) / 1200.0, r.Centroid.Y, Tol);
        }

        /// <summary>A hole: the area drops, the centroid moves away from it, and the hole's edges join the outline.</summary>
        [TestMethod]
        public void Hole()
        {
            var r = Region.Of(new[] { new Rect2(0, 0, 60, 40) }).Minus(new[] { new Rect2(40, 10, 50, 30) });
            Assert.AreEqual(2400 - 200, r.Area, Tol);
            Assert.AreEqual((2400 * 30 - 200 * 45) / 2200.0, r.Centroid.X, Tol);
            Assert.AreEqual(20, r.Centroid.Y, Tol);
            Assert.AreEqual(8, r.Outline().Count, "outer 4 + hole 4");
            Assert.AreEqual(200 + 60, r.Outline().Sum(s => s.Length), Tol);
            Assert.IsFalse(r.Contains(45, 20));
            Assert.IsTrue(r.Contains(40, 20), "on the hole's edge");
        }

        /// <summary>The length of a wall line a diaphragm reaches: on its edge, through a hole, and beyond it.</summary>
        [TestMethod]
        public void LengthAlong()
        {
            var r = Region.Of(new[] { new Rect2(0, 0, 60, 40) }).Minus(new[] { new Rect2(20, 0, 30, 10) }); // a notch on the south edge
            Assert.AreEqual(50, r.LengthAlong(new Point2(0, 0), new Point2(60, 0)), Tol, "the notch is not reached");
            Assert.AreEqual(50, r.LengthAlong(new Point2(0, 0.2), new Point2(60, 0.2), 0.25), Tol, "within the tolerance");
            Assert.AreEqual(40, r.LengthAlong(new Point2(60, 0), new Point2(60, 40)), Tol);
            Assert.AreEqual(10, r.LengthAlong(new Point2(50, 20), new Point2(80, 20)), Tol, "only the part over the diaphragm");
            Assert.AreEqual(0, r.LengthAlong(new Point2(0, 50), new Point2(60, 50)), Tol);
        }

        [TestMethod]
        public void UnionAndBounds()
        {
            var a = Region.Of(new[] { new Rect2(0, 0, 30, 40) });
            var b = Region.Of(new[] { new Rect2(30, 0, 60, 20) });
            var u = a.Union(b);
            Assert.AreEqual(1200 + 600, u.Area, Tol);
            Assert.AreEqual(new Rect2(0, 0, 60, 40), u.Bounds);
            Assert.IsTrue(Region.Empty.IsEmpty);
            Assert.IsTrue(double.IsNaN(Region.Empty.Centroid.X));
        }
    }
}
