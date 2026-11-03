using System.Windows;
using BuildingModel.Sketch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>Wall and beam labels run along their member, read left to right or bottom to top (screen y is down).</summary>
    [TestClass]
    public class MemberLabelTests
    {
        [TestMethod]
        public void Angle_ReadsLeftToRightOrBottomToTop()
        {
            Assert.AreEqual(0, SketchCanvas.MemberLabelAngle(new Vector(10, 0)), 1e-9, "level, drawn left to right");
            Assert.AreEqual(0, SketchCanvas.MemberLabelAngle(new Vector(-10, 0)), 1e-9, "level, drawn right to left: not upside down");
            Assert.AreEqual(-90, SketchCanvas.MemberLabelAngle(new Vector(0, -10)), 1e-9, "upright, drawn upward: bottom to top");
            Assert.AreEqual(-90, SketchCanvas.MemberLabelAngle(new Vector(0, 10)), 1e-9, "upright, drawn downward: still bottom to top");
            Assert.AreEqual(45, SketchCanvas.MemberLabelAngle(new Vector(-10, -10)), 1e-9, "sloped up to the left: turned to read left to right");
            Assert.AreEqual(-45, SketchCanvas.MemberLabelAngle(new Vector(10, -10)), 1e-9);
        }
    }
}
