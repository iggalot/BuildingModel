using System.Windows;
using BuildingModel.Sketch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>Windows never larger than the screen's work area, and moved back on screen title bar first.</summary>
    [TestClass]
    public class WindowFitTests
    {
        [TestMethod]
        public void Size_WantedOr95PercentOfTheRoom()
        {
            Assert.AreEqual(640, WindowFit.Size(640, 830), "room enough: as wanted");
            Assert.AreEqual(788, WindowFit.Size(900, 830), "0.95 × 830 = 788.5 → 788");
            Assert.AreEqual(900, WindowFit.Size(900, 0), "no screen information: as wanted");
            Assert.IsTrue(double.IsNaN(WindowFit.Size(double.NaN, 830)), "sized to content: left alone");
        }

        [TestMethod]
        public void OnScreen_TitleBarAboveTheTop_MovedDown()
        {
            var area = new Rect(0, 0, 1550, 800);
            Assert.AreEqual(new Rect(85, 0, 1200, 700), WindowFit.OnScreen(new Rect(85, -53, 1200, 700), area));
            Assert.AreEqual(new Rect(350, 100, 1200, 700), WindowFit.OnScreen(new Rect(600, 300, 1200, 700), area), "off the bottom right");
            Assert.AreEqual(new Rect(0, 0, 1700, 900), WindowFit.OnScreen(new Rect(-75, -50, 1700, 900), area), "larger than the area: top-left kept on screen");
            Assert.AreEqual(new Rect(10, 10, 400, 300), WindowFit.OnScreen(new Rect(10, 10, 400, 300), area), "already on screen: unchanged");
        }

        [STATestMethod]
        public void Fit_CapsSizeAndMinimum()
        {
            var area = SystemParameters.WorkArea;
            var w = WindowFit.Fit(new Window { Width = 5000, Height = 5000, MinWidth = 4000, MinHeight = 4000 });
            Assert.IsTrue(w.Width <= area.Width && w.Height <= area.Height, $"{w.Width} × {w.Height} in {area.Width} × {area.Height}");
            Assert.IsTrue(w.MinWidth <= w.Width && w.MinHeight <= w.Height);
            var c = WindowFit.Fit(new Window { SizeToContent = SizeToContent.WidthAndHeight });
            Assert.IsTrue(double.IsNaN(c.Width) && c.MaxHeight <= area.Height, "content-sized: may not grow past the screen");
        }
    }
}
