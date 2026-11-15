using System;
using BuildingModel.Sketch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>The one status language (owner 2026-11-14, DesignHub docs/UI_AUDIT.md): amber from 0.90, red over 1.00, labels by rank.</summary>
    [TestClass]
    public class PlanStatusTests
    {
        [TestMethod]
        public void Ratio_ClosesAt090_FailsOver1()
        {
            Assert.AreEqual(Health.None, PlanStatus.Of(double.NaN));
            Assert.AreEqual(Health.Passes, PlanStatus.Of(0.0));
            Assert.AreEqual(Health.Passes, PlanStatus.Of(0.899));
            Assert.AreEqual(Health.Close, PlanStatus.Of(0.90), "0.90 is close (owner)");
            Assert.AreEqual(Health.Close, PlanStatus.Of(1.00), "1.00 still passes, close");
            Assert.AreEqual(Health.Fails, PlanStatus.Of(1.001));
        }

        [TestMethod]
        public void RedOnlyForUnsafeOrUnchecked()
        {
            Assert.AreEqual(PlanStatus.Fail, PlanStatus.ColorOf(Health.Fails));
            Assert.AreEqual(PlanStatus.Fail, PlanStatus.ColorOf(Health.NotDesigned));
            Assert.AreEqual(PlanStatus.Close, PlanStatus.ColorOf(Health.Close));
            Assert.AreEqual(PlanStatus.Pass, PlanStatus.ColorOf(Health.Passes));
            Assert.AreEqual(PlanStatus.Info, PlanStatus.ColorOf(Health.None));
            Assert.IsTrue(PlanStatus.Attention(Health.Close) && PlanStatus.Attention(Health.Fails) && PlanStatus.Attention(Health.NotDesigned));
            Assert.IsFalse(PlanStatus.Attention(Health.Passes) || PlanStatus.Attention(Health.None));
            Assert.IsTrue(PlanStatus.PenOf(Health.NotDesigned, 2).DashStyle.Dashes.Count > 0, "NOT DESIGNED is dashed");
            Assert.AreEqual(0, PlanStatus.PenOf(Health.Fails, 2).DashStyle.Dashes.Count, "a failure is solid");
        }

        [TestMethod]
        public void Tag_HasItsGlyph_SoColourIsNeverTheOnlyCue()
        {
            Assert.AreEqual("✗ 2.13", MemberHealth.Of(2.13).Tag);
            Assert.AreEqual("▲ 0.94", MemberHealth.Of(0.94).Tag);
            Assert.AreEqual("✓ 0.72", MemberHealth.Of(0.72).Tag);
            Assert.AreEqual("⊘ NOT DESIGNED", MemberHealth.NotDesigned().Tag);
            Assert.AreEqual("✗ fails", MemberHealth.Failing("no supports").Tag);
        }

        [TestMethod]
        public void Density_ShowsByRank()
        {
            // Off: only the tools' live feedback
            Assert.IsTrue(LabelDensities.Shows(LabelDensity.Off, LabelRank.Live));
            Assert.IsFalse(LabelDensities.Shows(LabelDensity.Off, LabelRank.Critical));
            Assert.IsFalse(LabelDensities.Shows(LabelDensity.Off, LabelRank.Mark));
            // Critical (the default): the marks and what needs the eye
            Assert.IsTrue(LabelDensities.Shows(LabelDensity.Critical, LabelRank.Critical));
            Assert.IsTrue(LabelDensities.Shows(LabelDensity.Critical, LabelRank.Mark));
            Assert.IsFalse(LabelDensities.Shows(LabelDensity.Critical, LabelRank.Value));
            Assert.IsFalse(LabelDensities.Shows(LabelDensity.Critical, LabelRank.Detail));
            // Normal: and the governing values
            Assert.IsTrue(LabelDensities.Shows(LabelDensity.Normal, LabelRank.Value));
            Assert.IsFalse(LabelDensities.Shows(LabelDensity.Normal, LabelRank.Detail));
            // All
            Assert.IsTrue(LabelDensities.Shows(LabelDensity.All, LabelRank.Detail));
        }

        [STATestMethod]
        public void Canvas_OpensAtCritical()
        {
            var c = new SketchCanvas();
            Assert.AreEqual(LabelDensity.Critical, c.Labels);
            Assert.IsTrue(c.ShowsLabel(LabelRank.Mark));
            Assert.IsFalse(c.ShowsLabel(LabelRank.Value));
            bool raised = false;
            c.LabelsChanged += (s, e) => raised = true;
            c.Labels = LabelDensity.All;
            Assert.IsTrue(raised && c.ShowsLabel(LabelRank.Detail));
        }

        /// <summary>The model's errors colour the members they name; a warning colours nothing; NOT DESIGNED wins over a plain error.</summary>
        [TestMethod]
        public void FromIssues_ErrorsNameTheirMembers()
        {
            var issues = new System.Collections.Generic.List<BuildingModel.Checks.ModelIssue>
            {
                new BuildingModel.Checks.ModelIssue { Severity = BuildingModel.Checks.Severity.Error, Code = "BeamNotHeldUp", Message = "BM1 and BM2 hold each other up", Ids = { "BM1", "BM2" } },
                new BuildingModel.Checks.ModelIssue { Severity = BuildingModel.Checks.Severity.Warning, Code = "WallShort", Message = "short", Ids = { "W3" } },
                new BuildingModel.Checks.ModelIssue { Severity = BuildingModel.Checks.Severity.Error, Code = "DiaphragmStep", Message = "a step with no wall under it: NOT DESIGNED", Ids = { "BM2" } },
            };
            var map = PlanStatus.FromIssues(issues);
            Assert.AreEqual(Health.Fails, map["BM1"].State);
            Assert.AreEqual("beam not held up", map["BM1"].What);
            Assert.AreEqual(Health.NotDesigned, map["BM2"].State, "NOT DESIGNED wins");
            Assert.IsFalse(map.ContainsKey("W3"), "a warning is not red");
        }

        /// <summary>A host's ratios colour its members: a failing wall red, a close one amber; none given, no status colour at all.</summary>
        [STATestMethod]
        public void Render_MembersInTheirState_OnlyWhenTheHostSaysSo()
        {
            (int red, int amber) Draw(Func<string, MemberHealth> health)
            {
                var b = SketchTests.Sample(out var l2, out _);
                var v = new SketchView();
                v.Canvas.SetBuilding(b, l2.Id);
                v.Canvas.HealthOf = health;
                v.Canvas.ZoomExtents();
                var bmp = SketchTests.Render(v, health == null ? "health-none" : "health-given");
                return (SketchTests.Count(bmp, PlanStatus.Fail, 12), SketchTests.Count(bmp, PlanStatus.Close, 12));
            }
            var plain = Draw(null);
            var b0 = SketchTests.Sample(out _, out _);
            string first = b0.Walls[0].Id, second = b0.Walls[1].Id;
            var coloured = Draw(id => id == first ? MemberHealth.Of(1.4, "shear") : id == second ? MemberHealth.Of(0.95, "shear") : null);
            Assert.IsTrue(coloured.red > plain.red + 200, $"the failing wall drawn red ({plain.red} → {coloured.red})");
            Assert.IsTrue(coloured.amber > plain.amber + 200, $"the close wall drawn amber ({plain.amber} → {coloured.amber})");
        }

        /// <summary>Problems only (UI audit A2): the failing wall stays red, the close one goes plain.</summary>
        [STATestMethod]
        public void Render_ProblemsOnly_LeavesTheCloseOnePlain()
        {
            var b = SketchTests.Sample(out var l2, out _);
            string first = b.Walls[0].Id, second = b.Walls[1].Id;
            (int red, int amber) Draw(StatusFilter f)
            {
                var v = new SketchView();
                v.Canvas.SetBuilding(SketchTests.Sample(out var lv, out _), lv.Id);
                v.Canvas.HealthOf = id => id == first ? MemberHealth.Of(1.4, "shear") : id == second ? MemberHealth.Of(0.95, "shear") : null;
                v.Canvas.ShowStates = f;
                v.Canvas.ZoomExtents();
                var bmp = SketchTests.Render(v, "health-" + f);
                return (SketchTests.Count(bmp, PlanStatus.Fail, 12), SketchTests.Count(bmp, PlanStatus.Close, 12));
            }
            var all = Draw(StatusFilter.All);
            var problems = Draw(StatusFilter.Problems);
            Assert.IsTrue(problems.red > all.red - 50, $"the failure still red ({all.red} → {problems.red})");
            Assert.IsTrue(problems.amber < all.amber - 200, $"the close one plain ({all.amber} → {problems.amber})");
        }
    }
}
