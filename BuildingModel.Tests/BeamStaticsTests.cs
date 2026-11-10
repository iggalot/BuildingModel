using System.Linq;
using BuildingModel.Statics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// A beam's reactions from its own free body (<see cref="BeamStatics"/>) against the textbook: simple spans, overhangs and
    /// cantilevers by statics, continuous beams (EI constant) by their known coefficients, and a beam that cannot stand.
    /// </summary>
    [TestClass]
    public class BeamStaticsTests
    {
        private const double Tol = 1e-7;

        private static BeamLoads Point(double at, double p) { var l = new BeamLoads(); l.Points.Add((at, p)); return l; }
        private static BeamLoads Uniform(double a, double b, double w) { var l = new BeamLoads(); l.Spread.Add((a, b, w)); return l; }

        [TestMethod]
        public void SimpleSpan_APointLoadOffCentre_ByStatics()
        {
            var r = BeamStatics.Reactions(20, new[] { 0.0, 20 }, Point(6, 1000));
            Assert.AreEqual(1000 * 14 / 20.0, r[0], Tol);
            Assert.AreEqual(1000 * 6 / 20.0, r[1], Tol);
        }

        [TestMethod]
        public void SimpleSpan_APartUniformLoad_AtItsResultant()
        {
            // 100 plf from 12 to 18 ft of 20: 600 lb at 15 ft
            var r = BeamStatics.Reactions(20, new[] { 0.0, 20 }, Uniform(12, 18, 100));
            Assert.AreEqual(150, r[0], Tol);
            Assert.AreEqual(450, r[1], Tol);
        }

        /// <summary>Supports at 0 and 10 ft, the beam to 14 ft: a 4 ft overhang.</summary>
        [TestMethod]
        public void Overhang_ByStatics_TheTipLoadLiftsTheBackSupport()
        {
            // 1000 lb at the tip: ΣM about the first support, R2 = 1000 × 14 / 10 = 1400; R1 = 1000 − 1400 = −400 (held down)
            var r = BeamStatics.Reactions(14, new[] { 0.0, 10 }, Point(14, 1000));
            Assert.AreEqual(-400, r[0], Tol);
            Assert.AreEqual(1400, r[1], Tol);
            // 100 plf over the whole 14 ft: 1400 lb at 7 ft, R2 = 1400 × 7 / 10 = 980, R1 = 420
            r = BeamStatics.Reactions(14, new[] { 0.0, 10 }, Uniform(0, 14, 100));
            Assert.AreEqual(420, r[0], Tol);
            Assert.AreEqual(980, r[1], Tol);
        }

        /// <summary>Supports at 3 and 15 ft of an 18 ft beam: overhangs both ends, a load on each.</summary>
        [TestMethod]
        public void OverhangsBothEnds_ByStatics()
        {
            var l = new BeamLoads();
            l.Points.Add((0, 500));    // 3 ft past the first support
            l.Points.Add((10, 1200));
            l.Points.Add((18, 300));   // 3 ft past the second
            var r = BeamStatics.Reactions(18, new[] { 3.0, 15 }, l);
            // ΣM about 3 ft: R2 × 12 = −500 × 3 + 1200 × 7 + 300 × 15
            double r2 = (-500 * 3 + 1200 * 7 + 300 * 15) / 12.0;
            Assert.AreEqual(r2, r[1], Tol);
            Assert.AreEqual(2000 - r2, r[0], Tol);
        }

        [TestMethod]
        public void TwoEqualSpans_Uniform_TheTextbook()
        {
            // w L: ends 3/8, middle 10/8 (5/4)
            var r = BeamStatics.Reactions(20, new[] { 0.0, 10, 20 }, Uniform(0, 20, 100));
            Assert.AreEqual(0.375 * 1000, r[0], Tol);
            Assert.AreEqual(1.25 * 1000, r[1], Tol);
            Assert.AreEqual(0.375 * 1000, r[2], Tol);
        }

        [TestMethod]
        public void ThreeEqualSpans_Uniform_TheTextbook()
        {
            // w L: 0.4, 1.1, 1.1, 0.4
            var r = BeamStatics.Reactions(30, new[] { 0.0, 10, 20, 30 }, Uniform(0, 30, 100));
            Assert.AreEqual(400, r[0], Tol);
            Assert.AreEqual(1100, r[1], Tol);
            Assert.AreEqual(1100, r[2], Tol);
            Assert.AreEqual(400, r[3], Tol);
        }

        [TestMethod]
        public void TwoEqualSpans_APointLoadMidFirstSpan_TheTextbook()
        {
            // P at the middle of the first span: 13/32 P, 11/16 P, −3/32 P (the far end held down)
            var r = BeamStatics.Reactions(20, new[] { 0.0, 10, 20 }, Point(5, 3200));
            Assert.AreEqual(1300, r[0], Tol);
            Assert.AreEqual(2200, r[1], Tol);
            Assert.AreEqual(-300, r[2], Tol);
        }

        [TestMethod]
        public void SupportsInAnyOrder_EachGetsItsOwn()
        {
            var r = BeamStatics.Reactions(20, new[] { 20.0, 0 }, Point(6, 1000));
            Assert.AreEqual(300, r[0], Tol);
            Assert.AreEqual(700, r[1], Tol);
        }

        [TestMethod]
        public void OneSupport_OrNone_CannotStand()
        {
            Assert.IsNull(BeamStatics.Reactions(10, new[] { 5.0 }, Point(2, 100)));
            Assert.IsNull(BeamStatics.Reactions(10, new double[0], Point(2, 100)));
            Assert.IsNull(BeamStatics.Reactions(10, new[] { 5.0, 5.0 }, Point(2, 100)), "two at one place are one");
        }

        [TestMethod]
        public void SeveralLoadSets_OneSolve_EachItsOwn()
        {
            var r = BeamStatics.Reactions(14, new[] { 0.0, 10 }, new[] { Point(14, 1000), Uniform(0, 14, 100) });
            Assert.AreEqual(-400, r[0][0], Tol);
            Assert.AreEqual(980, r[1][1], Tol);
            Assert.AreEqual(1400, r[1].Sum(), Tol);
        }
    }
}
