using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BuildingModel.Edits;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>
    /// The hold-down markers and their labels: one rule for every app (the Shear Wall Calculator's plans and the shared
    /// foundation plan), so two walls meeting at a corner each show their own marker and label, never one over the other.
    /// </summary>
    [TestClass]
    public class HoldownMarkTests
    {
        [TestMethod]
        public void IconAt_JustInFromTheEnd_OnItsOwnMember()
        {
            // a 10 px marker: its centre 11 px in from the end, so its edge is 6 px in
            Assert.AreEqual(new Point(111, 200), HoldownMarks.IconAt(new Point(100, 200), new Point(300, 200), 10));
            Assert.AreEqual(new Point(289, 200), HoldownMarks.IconAt(new Point(300, 200), new Point(100, 200), 10));
            // two walls meeting at a corner (0,0): one along x, one along y; each marker on its own wall, clear of the other
            var a = HoldownMarks.IconAt(new Point(0, 0), new Point(200, 0), 10);
            var b = HoldownMarks.IconAt(new Point(0, 0), new Point(0, 200), 10);
            Assert.AreEqual(new Point(11, 0), a);
            Assert.AreEqual(new Point(0, 11), b);
            Assert.IsFalse(HoldownMarks.Overlaps(new Rect(a.X - 5, a.Y - 5, 10, 10), new Rect(b.X - 5, b.Y - 5, 10, 10)));
            Assert.AreEqual(new Point(5, 5), HoldownMarks.IconAt(new Point(5, 5), new Point(5, 5), 10)); // no length: at the end
        }

        [TestMethod]
        public void IconAt_AHoldown_ExactlyAtItsPlace_AtEveryScale()
        {
            Assert.AreEqual(new Point(130, 200), HoldownMarks.IconAt(new Point(100, 200), new Point(300, 200), 10, trueInset: 30), "zoomed in: at the post");
            Assert.AreEqual(new Point(102, 200), HoldownMarks.IconAt(new Point(100, 200), new Point(300, 200), 10, trueInset: 2), "small: still at its place");
            Assert.AreEqual(6, HoldownMarks.SizeFor(2.5), "a post too small to see: the smallest marker");
            Assert.AreEqual(10, HoldownMarks.SizeFor(35), "a large one: the usual marker");
        }

        [TestMethod]
        public void LabelBox_TwoMarkersAtOneCorner_TwoLabelsApart()
        {
            var taken = new List<Rect>();
            var outline = new Rect(0, 0, 200, 200);
            var a = HoldownMarks.LabelBox(new Point(7, 0), new Vector(1, 0), new Size(60, 14), outline, taken);
            var b = HoldownMarks.LabelBox(new Point(0, 7), new Vector(0, 1), new Size(60, 14), outline, taken);
            Assert.IsFalse(HoldownMarks.Overlaps(a, b), $"{a} and {b}");
            Assert.IsFalse(outline.Contains(a.TopLeft) && outline.Contains(a.BottomRight), "on the edge: outside the building first");
            Assert.AreEqual(2, taken.Count);
        }

        [STATestMethod]
        public void Properties_AWallsJoints_ACornerFlips_OneUndoStep()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var w4 = b.AddWall(b.Ground, l2, 0, 0, 20, 0);
            var w9 = b.AddWall(b.Ground, l2, 0, 0, 0, 16);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            var props = new ObjectProperties(s);
            s.Select(new[] { w9.Id });
            var texts = props.Children.OfType<System.Windows.Controls.DockPanel>().SelectMany(d => d.Children.OfType<System.Windows.Controls.TextBlock>()).Select(t => t.Text).ToList();
            CollectionAssert.Contains(texts, $"Corner with {w4.Id}: stops at {w4.Id}'s face");
            var flip = props.Children.OfType<System.Windows.Controls.DockPanel>().SelectMany(d => d.Children.OfType<System.Windows.Controls.Button>()).Single();
            flip.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.AreEqual(w9.Id, WallJoints.Of(s.Building).Single().Through.Id, "turned round: W9 runs past");
            s.Undo();
            Assert.AreEqual(w4.Id, WallJoints.Of(s.Building).Single().Through.Id, "one undo step back to the rule");
        }

        [STATestMethod]
        public void Properties_AWall_FramingAnd3D_OpensTheHostsWallWindow()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var w = b.AddWall(b.Ground, l2, 0, 0, 20, 0);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            var props = new ObjectProperties(s);
            s.Select(new[] { w.Id });
            Assert.IsFalse(props.Children.OfType<System.Windows.Controls.Button>().Any(), "no host window: no button");
            Wall opened = null;
            props.OpenWall = x => opened = x;
            props.Fill();
            var open = props.Children.OfType<System.Windows.Controls.Button>().Single(x => (x.Content as string)?.StartsWith("Framing and 3D") == true);
            open.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.AreSame(w, opened);
        }

        [STATestMethod]
        public void FoundationPlan_TwoWallsAtACorner_EachMarkerExactlyAtItsPoint()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var w4 = b.AddWall(b.Ground, l2, 0, 0, 20, 0);
            var w9 = b.AddWall(b.Ground, l2, 0, 0, 0, 16);
            var s = new SketchCanvas();
            s.SetBuilding(b, b.Ground.Id);
            double post = 3.0 / 12; // a 3 in post, ft: the hold-downs on its inside face, where the shear wall app puts them
            var lat = new LateralSection();
            lat.Points.Add(new LateralPoint { From = $"{w4.Id} west end", WallId = w4.Id, X = post, Y = 0, Connector = "HDU2", Up = { ["Case 1"] = 1000 } });
            lat.Points.Add(new LateralPoint { From = $"{w9.Id} south end", WallId = w9.Id, X = 0, Y = post, Connector = "HDU4", Up = { ["Case 1"] = 1000 } });

            var m = s.ConnectionMarkers(b, lat);
            Assert.AreEqual(2, m.Count);
            Assert.AreEqual(s.ToScreen(lat.Points[0].Where), m[0].Icon, "W4's exactly at its point");
            Assert.AreEqual(s.ToScreen(lat.Points[1].Where), m[1].Icon, "W9's exactly at its point");
            Assert.AreNotEqual(m[0].Icon, m[1].Icon, "each its own (the labels' arrows tell them apart where they are close)");
            Assert.AreEqual(new Vector(1, 0), m[0].Inward, "W4's along W4");
            Assert.AreEqual(new Vector(0, -1), m[1].Inward, "W9's along W9 (up the screen)");
            Assert.AreEqual($"{w4.Id}  HDU2", m[0].Text);
        }

        [STATestMethod]
        public void LevelPlan_ItsWallsHoldowns_AndThoseComingDownFromAbove()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var l3 = BuildingEdits.AddStory(b, 10);
            var w1 = b.AddWall(b.Ground, l2, 0, 0, 20, 0);   // first story, carries level 2
            var w2 = b.AddWall(l2, l3, 0, 0, 20, 0);         // second story, carries level 3, on W1
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            var lat = new LateralSection();
            lat.Holdowns.Add(new StoryHoldown { From = $"{w1.Id} west end", WallId = w1.Id, X = 0.25, Y = 0, LevelId = l2.Id, OntoLevelId = b.Ground.Id, Connector = "HDU5", TensionAsd = 3000, Onto = "the foundation", PostAlongWall = 3 });
            lat.Holdowns.Add(new StoryHoldown { From = $"{w2.Id} west end", WallId = w2.Id, X = 0.25, Y = 0, LevelId = l3.Id, OntoLevelId = l2.Id, Connector = "MSTC40", TensionAsd = 1500, Onto = $"wall {w1.Id}", PostAlongWall = 3 });
            lat.Holdowns.Add(new StoryHoldown { From = $"{w2.Id} east end", WallId = w2.Id, X = 19.75, Y = 0, LevelId = l3.Id, OntoLevelId = l2.Id, Connector = "MSTC40", TensionAsd = 1500, Onto = null, PostAlongWall = 3 });

            var m = s.LevelMarkers(b, lat, l2);
            Assert.AreEqual(3, m.Count, "W1's own, and W2's two coming down onto level 2");
            Assert.AreEqual(HoldownMark.Sized, m[0].Mark);
            Assert.AreEqual($"{w1.Id}  HDU5", m[0].Text);
            Assert.AreEqual(s.ToScreen(lat.Holdowns[0].Where), m[0].Icon);
            Assert.AreEqual(HoldownMark.FromAbove, m[1].Mark);
            Assert.AreEqual($"↓ {w2.Id} 1.5k onto {w1.Id}", m[1].Text);
            Assert.AreEqual(HoldownMark.FromAboveOntoNothing, m[2].Mark, "nothing under it: red");
            StringAssert.Contains(m[2].Text, "nothing below");

            var top = s.LevelMarkers(b, lat, l3);
            Assert.AreEqual(2, top.Count, "level 3: W2's own");
            Assert.IsTrue(top.All(x => x.Mark == HoldownMark.Sized));
        }

        /// <summary>
        /// The Internal forces toggle, the same rule as a free body (WISE): on, both halves of every connection; off, the
        /// selected members are the body (none: the whole structure). W2 on W1 on the foundation; W3's hold-down lands on W1
        /// half a foot from W2's: one label, their sum case by case, ASD (0.6 × (500 + 1000) = 0.9k).
        /// </summary>
        [STATestMethod]
        public void LevelPlan_InternalForces_TheBodyIsTheSelection()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var l3 = BuildingEdits.AddStory(b, 10);
            var w1 = b.AddWall(b.Ground, l2, 0, 0, 20, 0);
            var w2 = b.AddWall(l2, l3, 0, 0, 20, 0);
            var w3 = b.AddWall(l2, l3, 0.5, 0, 0.5, 12);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            StoryHoldown H(Wall w, Level top, Level onto, string to, double x, double y, double up) => new StoryHoldown
            {
                From = $"{w.Id} end at {x}", WallId = w.Id, X = x, Y = y, LevelId = top.Id, OntoLevelId = onto.Id, Onto = to, Connector = "HDU2",
                TensionAsd = 0.6 * System.Math.Abs(up), Up = new Dictionary<string, double> { ["C"] = up }, PostAlongWall = 3,
            };
            var lat = new LateralSection();
            lat.Holdowns.Add(H(w1, l2, b.Ground, "the foundation", 0.25, 0, 1500));
            lat.Holdowns.Add(H(w1, l2, b.Ground, "the foundation", 19.75, 0, -1500));
            lat.Holdowns.Add(H(w2, l3, l2, $"wall {w1.Id}", 0.25, 0, 500));
            lat.Holdowns.Add(H(w2, l3, l2, $"wall {w1.Id}", 19.75, 0, -500));
            lat.Holdowns.Add(H(w3, l3, l2, $"wall {w1.Id}", 0.5, 0.25, 1000));
            lat.Holdowns.Add(H(w3, l3, l2, "wall W99", 0.5, 11.75, -1000));

            // on: every half, each on its plan; the two landing together on W1 in one label
            var on = s.LevelMarkers(b, lat, l2);
            Assert.AreEqual(2 + 3, on.Count, $"W1's two; coming down: W2 + W3 together, W2's other end, W3's other end: {string.Join(" | ", on.Select(m => m.Text))}");
            var both = on.Single(m => m.Text.Contains("+"));
            StringAssert.StartsWith(both.Text, $"↓ {w2.Id} + {w3.Id} 0.9k onto {w1.Id}");
            Assert.AreEqual(4, s.LevelMarkers(b, lat, l3).Count, "level 3: W2's two and W3's two, their own");

            // off, nothing selected: the whole structure, only what is on the foundation (W1's own) and what nothing takes
            s.SetInternalForces(false);
            Assert.AreEqual(2, s.LevelMarkers(b, lat, l2).Count, "W1's own on the foundation: the support");
            Assert.AreEqual(0, s.LevelMarkers(b, lat, l3).Count(m => m.Text.StartsWith(w2.Id)), "W2's on W1: inside");

            // off, W2 selected: cut from W1, its own half only
            s.Select(new[] { w2.Id });
            Assert.AreEqual(2, s.LevelMarkers(b, lat, l3).Count(m => m.Text.StartsWith(w2.Id)), "W2's own: the cut, on W2");
            Assert.IsFalse(s.LevelMarkers(b, lat, l2).Any(), "nothing on W1's plan: W1 is not in the body");

            // off, W1 selected: its own on the foundation, and the halves coming down onto it from W2 and W3 (the cut)
            s.Select(new[] { w1.Id });
            var w1Body = s.LevelMarkers(b, lat, l2);
            Assert.AreEqual(2 + 1 + 1, w1Body.Count, string.Join(" | ", w1Body.Select(m => m.Text)));
            Assert.AreEqual(0, s.LevelMarkers(b, lat, l3).Count, "W2's and W3's own: outside the body");
        }
    }
}
