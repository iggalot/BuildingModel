using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BuildingModel.Edits;
using BuildingModel.Model;
using BuildingModel.Sketch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>The one card anatomy (owner 2026-11-14, UI audit B1): head, key numbers, why, fixes, deeper, folded sections.</summary>
    [TestClass]
    public class MemberCardTests
    {
        [TestMethod]
        public void Chip_WordsForEveryState()
        {
            Assert.AreEqual("✗ 2.13", MemberHealth.Of(2.13, "shear").Tag);
            Assert.AreEqual("▲ 0.94", MemberHealth.Of(0.94).Tag);
            Assert.AreEqual("⊘ NOT DESIGNED", MemberHealth.NotDesigned().Tag);
            StringAssert.StartsWith(MemberCard.Words(MemberHealth.Of(2.13, "hold-down T")), "Fails: hold-down T, ratio 2.13");
            StringAssert.Contains(MemberCard.Words(MemberHealth.Of(0.94)), "close to the limit");
        }

        [STATestMethod]
        public void Card_KeepsItsOrder_WhateverOrderItIsFilled()
        {
            var c = new MemberCard();
            c.Section("Design");
            c.Deeper("Construction", null, () => { });
            c.Fix("Auto-select nailing", null, () => { });
            c.Why("Shear 532 plf over the capacity 380 plf", Health.Fails);
            c.Number("Unit shear v", "532 plf", "of 380 plf", Health.Fails);
            c.Head("Wall 2", "shear wall", MemberHealth.Of(1.40, "shear"));
            var order = c.Children.OfType<FrameworkElement>().Where(e => e.Visibility == Visibility.Visible).Select(e => e.GetType().Name).ToList();
            // head (DockPanel), numbers (UniformGrid), why (TextBlock), fixes, deeper (WrapPanel), sections (StackPanel)
            CollectionAssert.AreEqual(new[] { "DockPanel", "UniformGrid", "TextBlock", "WrapPanel", "WrapPanel", "StackPanel" }, order);
            Assert.AreEqual("✗ Shear 532 plf over the capacity 380 plf", c.WhyText.Trim());
            CollectionAssert.AreEqual(new[] { "Auto-select nailing" }, c.FixTexts.ToArray());
            CollectionAssert.AreEqual(new[] { "Construction" }, c.DeeperTexts.ToArray());
        }

        [STATestMethod]
        public void Card_AtMostSixKeyNumbers_AndEmptyPartsTakeNoRoom()
        {
            var c = new MemberCard().Head("B1", "beam");
            for (int i = 0; i < 9; i++) c.Number("n" + i, i.ToString());
            Assert.AreEqual(MemberCard.MaxNumbers, c.NumberLabels.Count);
            Assert.IsNull(c.WhyText, "no why: no line");
            c.Fix("Nothing", null, null);
            Assert.AreEqual(0, c.FixTexts.Count, "a fix the host does not offer: no button");
        }

        [STATestMethod]
        public void Section_RemembersOpenOrShut_ForItsKindOfCard()
        {
            var a = new MemberCard().Head("W1", "test wall");
            var body = a.Section("More");
            var e = (Expander)body.Parent;
            Assert.IsFalse(e.IsExpanded);
            e.IsExpanded = true;
            var b = new MemberCard().Head("W2", "test wall");
            Assert.IsTrue(((Expander)b.Section("More").Parent).IsExpanded, "the next card of its kind opens as it was left");
            var c = new MemberCard().Head("B1", "test beam");
            Assert.IsFalse(((Expander)c.Section("More").Parent).IsExpanded, "another kind keeps its own");
        }

        [STATestMethod]
        public void Properties_HeadFromTheHealth_TheIssueAsWhy_TheHintsFolded()
        {
            var b = new Building();
            b.Levels.Add(new Level { Id = "L0", Name = "Ground", Elevation = 0 });
            b.Levels.Add(new Level { Id = "L1", Name = "Level 1", Elevation = 9 });
            b.Beams.Add(new Beam { Id = "BM1", LevelId = "L1", Start = new Geometry.Point2(0, 0), End = new Geometry.Point2(12, 0) });
            var s = new SketchCanvas();
            s.SetBuilding(b, "L1");
            s.HealthOf = id => id == "BM1" ? MemberHealth.Failing("beam not held up") : null;
            var p = new ObjectProperties(s)
            {
                IssuesOf = id => new[] { new BuildingModel.Checks.ModelIssue { Severity = BuildingModel.Checks.Severity.Error, Code = "BeamNotHeldUp", Message = "BM1 has fewer than two supports.", Ids = { "BM1" } } },
            };
            s.Select(new[] { "BM1" });
            Assert.IsNotNull(p.Card);
            Assert.AreEqual(Health.Fails, p.Card.Health.State);
            StringAssert.Contains(p.Card.WhyText, "BM1 has fewer than two supports.");
            Assert.IsTrue(p.Notes.Any(n => n.StartsWith("Span 12 ft")), "the beam's explanation is in the folded notes");
            Assert.IsTrue(p.Children.OfType<Expander>().Any(), "the notes at the bottom");
        }

        [STATestMethod]
        public void Render_AFailingShearWallCard()
        {
            var c = new MemberCard { Width = 316 };
            c.Head("Wall 2", "shear wall", MemberHealth.Of(1.40, "shear"), "E-W · 12 ft long · 9 ft high", () => { });
            c.Number("Unit shear v", "532 plf", "of 380 plf", Health.Fails)
             .Number("Shear ratio", "1.40", null, Health.Fails)
             .Number("Hold-down", "HDU5", "auto", Health.Close)
             .Number("Tension T", "5,120 lb", "of 5,645 lb", Health.Close)
             .Number("Sheathing", "Str I 15/32 @ 6\"", "one face")
             .Number("Design shear V", "4.25 k");
            c.Why("Shear 532 plf over the capacity 380 plf (Structural I 15/32, 8d)", Health.Fails);
            c.Fix("Auto-select nailing", null, () => { });
            c.Deeper("Construction", null, () => { }).Deeper("Design table", null, () => { });
            c.Section("Design", openAtFirst: true).Children.Add(MemberCard.Row("Face 1", "Structural I 15/32, 8d @ 6\""));
            c.Section("More").Children.Add(MemberCard.Row("From – to", "(0, 0) – (12, 0) ft"));
            var host = new Border { Background = System.Windows.Media.Brushes.White, Child = c, Padding = new Thickness(12) };
            var bmp = SketchTests.Render(host, "member-card");
            Assert.IsTrue(SketchTests.Count(bmp, PlanStatus.Fail, 30) > 100, "the failing chip, numbers and why in red");
            Assert.IsTrue(SketchTests.Count(bmp, PlanStatus.Close, 40) > 0 || SketchTests.Count(bmp, ((System.Windows.Media.SolidColorBrush)Ui.Warning).Color, 30) > 10, "the close hold-down in amber");
        }
    }
}
