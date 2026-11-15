using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Model;
using BuildingModel.Sketch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>The view modes, layers, status filter and saved views, and the case picker (owner 2026-11-14, UI audit A1, A2, A5).</summary>
    [TestClass]
    public class ViewBarTests
    {
        [TestMethod]
        public void Filter_ProblemsOnly_FailsAndNotDesigned()
        {
            foreach (Health h in System.Enum.GetValues(typeof(Health))) Assert.IsTrue(StatusFilters.Shows(StatusFilter.All, h), h.ToString());
            Assert.IsFalse(StatusFilters.Shows(StatusFilter.HidePassing, Health.Passes));
            Assert.IsTrue(StatusFilters.Shows(StatusFilter.HidePassing, Health.Close), "close stays in hide passing");
            Assert.IsTrue(StatusFilters.Shows(StatusFilter.HidePassing, Health.Fails));
            Assert.IsFalse(StatusFilters.Shows(StatusFilter.Problems, Health.Close), "problems: not the close ones");
            Assert.IsFalse(StatusFilters.Shows(StatusFilter.Problems, Health.Passes));
            Assert.IsTrue(StatusFilters.Shows(StatusFilter.Problems, Health.Fails));
            Assert.IsTrue(StatusFilters.Shows(StatusFilter.Problems, Health.NotDesigned), "NOT DESIGNED is never filtered out");
        }

        [TestMethod]
        public void SavedViews_RoundTrip_InTheAppsOwnKey()
        {
            var b = new Building();
            var s = new SavedViews();
            s.Put(new SavedView { Name = "My hold-down check", Mode = "Reactions", On = { "holdowns" }, Off = { "image" }, Labels = 3, Status = 2, LevelId = "L2" });
            s.Put(new SavedView { Name = "Plain", On = { "image" } });
            s.Put(new SavedView { Name = "my HOLD-DOWN check", Mode = "Analysis" }); // replaces it by name, any case
            Assert.AreEqual(2, s.Views.Count);
            SavedViews.Write(b, SuiteApps.ShearWalls, s);
            Assert.IsNotNull(b.GetData("Views.shearwalls"));
            Assert.IsNull(b.GetData("Views.loads"), "each app its own key");

            var back = SavedViews.Read(b, SuiteApps.ShearWalls);
            Assert.AreEqual("Analysis", back.Find("my hold-down check").Mode);
            Assert.AreEqual("image", back.Find("plain").On.Single());
            Assert.IsTrue(back.Remove("Plain"));
            Assert.IsTrue(back.Remove("my hold-down check"));
            SavedViews.Write(b, SuiteApps.ShearWalls, back);
            Assert.IsNull(b.GetData("Views.shearwalls"), "none left: the key is gone");
            b.SetData("Views.shearwalls", "not json");
            Assert.IsTrue(SavedViews.Read(b, SuiteApps.ShearWalls).IsEmpty, "unreadable: none, no throw");
        }

        private sealed class Host { public bool A = true, B = true, C; }

        private static ViewBar Bar(SketchCanvas c, Host h)
        {
            var v = new ViewBar(c);
            v.Layers.Add(new ViewLayer("a", "A", () => h.A, x => h.A = x));
            v.Layers.Add(new ViewLayer("b", "B", () => h.B, x => h.B = x));
            v.Layers.Add(new ViewLayer("c", "C", () => h.C, x => h.C = x));
            v.Modes.Add(new ViewMode("Problems", "", "a") { Labels = LabelDensity.Critical, Status = StatusFilter.Problems });
            v.Modes.Add(new ViewMode("Reactions", "", "b", "c") { Labels = LabelDensity.All, Status = StatusFilter.All });
            v.Fill();
            return v;
        }

        [STATestMethod]
        public void Mode_SetsItsLayers_LabelsAndFilter_AndAViewComesBack()
        {
            var c = new SketchCanvas();
            var h = new Host();
            var v = Bar(c, h);
            int changed = 0;
            v.Changed += (s, e) => changed++;

            v.Apply("Problems");
            Assert.AreEqual("Problems", v.Mode);
            Assert.IsTrue(h.A && !h.B && !h.C, "its layers on, the rest off");
            Assert.AreEqual(StatusFilter.Problems, c.ShowStates);
            Assert.IsFalse(c.StatusShows(Health.Passes));
            Assert.IsTrue(c.StatusShows(Health.NotDesigned));
            Assert.AreEqual(1, changed, "the host redraws once");

            var saved = v.Capture("Mine");
            CollectionAssert.AreEqual(new[] { "a" }, saved.On);
            CollectionAssert.AreEqual(new[] { "b", "c" }, saved.Off);
            Assert.AreEqual((int)StatusFilter.Problems, saved.Status);

            v.Apply("Reactions");
            Assert.IsTrue(!h.A && h.B && h.C);
            Assert.AreEqual(LabelDensity.All, c.Labels);
            Assert.IsTrue(c.StatusShows(Health.Passes));

            v.Restore(saved);
            Assert.IsTrue(h.A && !h.B && !h.C, "the saved view's layers");
            Assert.AreEqual(LabelDensity.Critical, c.Labels);
            Assert.AreEqual(StatusFilter.Problems, c.ShowStates);
            Assert.AreEqual("Problems", v.Mode);

            v.Restore(new SavedView { Name = "old", On = { "c" }, Labels = 9, Status = -4 }); // a layer it does not name stays; nonsense clamped
            Assert.IsTrue(h.A && h.C);
            Assert.AreEqual(LabelDensity.All, c.Labels);
            Assert.AreEqual(StatusFilter.All, c.ShowStates);
        }

        [STATestMethod]
        public void CasePicker_EnvelopeFirst_StepsRound_KeepsThePick()
        {
            var p = new CasePicker();
            string picked = null;
            p.Picked += (s, n) => picked = n;
            p.Show(new[] { ("C1", "Case 1 (rigid)"), ("C2", (string)null), ("C3", "Case 3") });
            CollectionAssert.AreEqual(new[] { CasePicker.Envelope, "C1", "C2", "C3" }, p.Names.ToArray());
            Assert.AreEqual(CasePicker.Envelope, p.Selected);
            Assert.IsNull(picked, "filling raises nothing");

            p.Step(1);
            Assert.AreEqual("C1", picked);
            p.Step(-1); p.Step(-1);
            Assert.AreEqual("C3", p.Selected, "round through the envelope");
            p.Selected = "C2";
            Assert.AreEqual("C2", picked);

            p.Show(new[] { ("C2", "Case 2"), ("C4", "Case 4") });
            Assert.AreEqual("C2", p.Selected, "still there: kept");
            p.Show(new[] { ("C4", "Case 4") });
            Assert.AreEqual(CasePicker.Envelope, p.Selected, "gone: the envelope");

            p.GoverningOf = () => "C4";
            p.Selected = "C4"; p.Selected = CasePicker.Envelope;
            Assert.AreEqual(CasePicker.Envelope, picked);
        }
    }
}
