using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>The sketcher drives the model: typed points draw, keys pick tools, undo, issues and notes on the plan, 3D.</summary>
    [TestClass]
    public class SketchTests
    {
        private const int W = 1200, H = 720;

        private static Building Sample(out Level l2, out Level roof)
        {
            var b = Building.New("Sample");
            l2 = b.AddLevel("Level 2", 10);
            roof = b.AddLevel("Roof", 19);
            var lv = new[] { b.Ground, l2, roof };
            for (int i = 1; i < lv.Length; i++)
            {
                b.AddDiaphragm(lv[i], new Rect2(0, 0, 60, 40));
                b.AddWall(lv[i - 1], lv[i], 0, 0, 60, 0);
                b.AddWall(lv[i - 1], lv[i], 0, 40, 60, 40);
                b.AddWall(lv[i - 1], lv[i], 0, 0, 0, 40);
                b.AddWall(lv[i - 1], lv[i], 60, 0, 60, 40, shearWall: i == 1);
            }
            b.AddOpening(b.Ground, roof, new Rect2(20, 10, 30, 20));
            b.AddNote(l2, 45, 30, "Stair: confirm the opening size");
            return b;
        }

        internal static RenderTargetBitmap Render(FrameworkElement e, string name)
        {
            e.Measure(new Size(W, H));
            e.Arrange(new Rect(0, 0, W, H));
            e.UpdateLayout();
            var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(e);
            string dir = Environment.GetEnvironmentVariable("SNAPSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var f = File.Create(Path.Combine(dir, name + ".png"))) enc.Save(f);
            }
            return bmp;
        }

        internal static int Count(RenderTargetBitmap bmp, Color c, int tol = 24)
        {
            var px = new byte[W * H * 4];
            bmp.CopyPixels(px, W * 4, 0);
            int n = 0;
            for (int i = 0; i < px.Length; i += 4)
                if (Math.Abs(px[i] - c.B) <= tol && Math.Abs(px[i + 1] - c.G) <= tol && Math.Abs(px[i + 2] - c.R) <= tol) n++;
            return n;
        }

        [STATestMethod]
        public void Render_ShowsTheLevelWithIssuesAndNotes()
        {
            var b = Sample(out var l2, out _);
            var v = new SketchView();
            v.Canvas.SetBuilding(b, l2.Id);
            v.Canvas.Issues = ModelChecks.Run(b);
            v.Canvas.Select(new[] { b.Walls[0].Id });
            Render(v, "sketch-level2");
            v.Canvas.ZoomExtents();
            var bmp = Render(v, "sketch-level2");
            Assert.IsTrue(Count(bmp, SketchCanvas.Palette.ShearWall) > 500, "shear walls drawn");
            Assert.IsTrue(Count(bmp, SketchCanvas.Palette.NoteFill, 4) > 300, "the note drawn");
            Assert.AreEqual(l2, v.Canvas.ActiveLevel);
        }

        /// <summary>
        /// What a host app (the Shear Wall Designer) hooks into: wall colours from its design check, its own overlay and
        /// level caption, its error markers, and typed text its own command waits for.
        /// </summary>
        [STATestMethod]
        public void HostHooks_WallColourOverlayCaptionTypedText()
        {
            var b = Sample(out var l2, out _);
            var v = new SketchView();
            var s = v.Canvas;
            s.SetBuilding(b, l2.Id);
            var red = Color.FromRgb(0xC6, 0x28, 0x28);
            var failPen = new Pen(new SolidColorBrush(red), 4);
            failPen.Freeze();
            string failing = b.Walls[0].Id;
            s.WallStyle = w => w.Id == failing ? failPen : null;
            int overlays = 0;
            s.DrawOver += (o, e) => overlays++;
            s.LevelCaption = l => $"{l.Name} · story 1 of 2 · 1 of 4 walls fail";
            var issues = ModelChecks.Run(b);
            issues.Add(new ModelIssue { Severity = Severity.Error, Code = "WallFails", Message = $"{failing} fails: shear over the capacity.", Ids = { failing }, LevelId = l2.Id, At = b.Walls[0].Middle });
            s.Issues = issues;
            s.ZoomExtents();
            var bmp = Render(v, "sketch-host");
            Assert.IsTrue(Count(bmp, red, 10) > 300, "the failing wall in the host's colour");
            Assert.IsTrue(overlays > 0, "the host's overlay drawn");
            Assert.IsTrue(s.IssuesHere().Any(i => i.Code == "WallFails"));

            string typed = null;
            s.InterceptTyped = t => { if (t != "25") return false; typed = t; return true; };
            Assert.IsTrue(s.TypePoint("25"), "the host took it, in the Select tool too");
            Assert.AreEqual("25", typed);
            Assert.IsFalse(s.TypePoint("0,0"), "not the host's: the Select tool cannot use a point");
            v.FocusCommand("12");
        }

        [STATestMethod]
        public void IssueMarkers_OnTheirLevel()
        {
            var b = Sample(out var l2, out var roof);
            b.AddWall(b.Ground, l2, 10, 80, 30, 80); // reaches no diaphragm
            var v = new SketchView();
            v.Canvas.SetBuilding(b, l2.Id);
            v.Canvas.Issues = ModelChecks.Run(b);
            Assert.IsTrue(v.Canvas.IssuesHere().Any(i => i.Code == "ShearWallNotReached"));
            v.Canvas.ShowLevel(roof.Id);
            Assert.IsFalse(v.Canvas.IssuesHere().Any(i => i.Code == "ShearWallNotReached"));
            v.Canvas.ShowLevel(l2.Id);
            Render(v, "sketch-issues");
        }

        [STATestMethod]
        public void TypedPoints_DrawWallsAndDiaphragms_UndoRedo()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            int changes = 0;
            s.ModelChanged += (o, e) => changes++;
            Assert.IsTrue(s.HandleKey(Key.W, ModifierKeys.None));
            Assert.AreEqual(SketchTool.Wall, s.Tool);
            Assert.IsTrue(s.TypePoint("0,0"));
            Assert.IsTrue(s.TypePoint("@30,0"));
            Assert.IsTrue(s.TypePoint("@0,20"));       // chained: a second wall from the end of the first
            Assert.AreEqual(2, b.Walls.Count);
            Assert.AreEqual(b.Ground.Id, b.Walls[0].BaseLevelId);
            Assert.AreEqual(l2.Id, b.Walls[0].TopLevelId);
            Assert.AreEqual(20, b.Walls[1].Length, 1e-9);
            Assert.IsFalse(s.TypePoint("abc"));
            s.HandleKey(Key.Escape, ModifierKeys.None);
            s.HandleKey(Key.D, ModifierKeys.None);
            s.TypePoint("0,0");
            s.TypePoint("@30'6\",20");
            Assert.AreEqual(30.5, b.Diaphragms.Single().Pieces[0].Width, 1e-9);
            Assert.AreEqual(3, changes);
            s.Undo();
            Assert.AreEqual(0, b.Diaphragms.Count);
            s.Redo();
            Assert.AreEqual(1, b.Diaphragms.Count);
            Assert.IsTrue(s.HandleKey(Key.Z, ModifierKeys.Control));
            Assert.AreEqual(0, b.Diaphragms.Count);
        }

        [STATestMethod]
        public void Ground_HasNoWallsOrDiaphragms_TopLevelHasNoOpening()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var s = new SketchCanvas();
            string said = null;
            s.Status += (o, e) => { if (e.IsWarning) said = e.Text; };
            s.SetBuilding(b, b.Ground.Id);
            s.SetTool(SketchTool.Wall);
            s.TypePoint("0,0");
            StringAssert.Contains(said, "above the ground");
            s.ShowLevel(l2.Id);
            s.SetTool(SketchTool.Opening);
            s.TypePoint("1,1");
            StringAssert.Contains(said, "top level");
            Assert.AreEqual(0, b.Walls.Count + b.Openings.Count);
        }

        [STATestMethod]
        public void MoveCopyMirror_WorkOnTheSelection()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var w = b.AddWall(b.Ground, l2, 0, 0, 10, 0);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            s.Select(new[] { w.Id });
            s.SetTool(SketchTool.Copy);
            s.TypePoint("0,0");
            s.TypePoint("@0,5");
            s.TypePoint("0,10");   // again from the same base: 10 ft up
            Assert.AreEqual(3, b.Walls.Count);
            Assert.AreEqual(10, b.Walls[2].Start.Y, 1e-9);
            s.SetTool(SketchTool.Move);
            s.TypePoint("0,0");
            s.TypePoint("@2,0");
            Assert.AreEqual(2, w.Start.X, 1e-9);
            s.SetTool(SketchTool.Mirror);
            s.TypePoint("20,-5");
            s.TypePoint("@0,10");  // a vertical line x = 20
            Assert.AreEqual(4, b.Walls.Count);
            Assert.AreEqual(38, b.Walls.Last().Start.X, 1e-9);
            s.SetTool(SketchTool.Select);
            s.Select(b.Walls.Select(x => x.Id));
            s.DeleteSelected();
            Assert.AreEqual(0, b.Walls.Count);
            Assert.AreEqual(0, s.Selection.Count);
        }

        [STATestMethod]
        public void Notes_AskForTheirText()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var s = new SketchCanvas { PromptText = (q, init) => "Check the beam" };
            s.SetBuilding(b, l2.Id);
            s.SetTool(SketchTool.Note);
            s.TypePoint("5,5");
            Assert.AreEqual("Check the beam", b.Notes.Single().Text);
            Assert.AreEqual(l2.Id, b.Notes.Single().LevelId);
        }

        [STATestMethod]
        public void Properties_ChangeTheModelAsOneUndoStep()
        {
            var b = Sample(out var l2, out _);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            var p = new ObjectProperties(s);
            s.Select(new[] { b.Walls[0].Id });
            Assert.IsTrue(p.Children.OfType<System.Windows.Controls.CheckBox>().Any(c => (string)c.Content == "Shear wall"));
            s.Select(new[] { b.Walls[0].Id, b.Walls[1].Id });
            var cb = p.Children.OfType<System.Windows.Controls.CheckBox>().Single();
            cb.IsChecked = false;
            cb.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.IsFalse(b.Walls[0].IsShearWall || b.Walls[1].IsShearWall);
            s.Undo();
            Assert.IsTrue(b.Walls[0].IsShearWall && b.Walls[1].IsShearWall);
        }

        [STATestMethod]
        public void View3D_Renders()
        {
            var b = Sample(out var l2, out _);
            var v = new Building3DView { HighlightLevelId = l2.Id };
            v.Show(b);
            var bmp = Render(v, "building-3d");
            Assert.IsTrue(Count(bmp, Color.FromRgb(0x1D, 0x4E, 0xD8), 60) > 200, "shear walls in blue");
        }

        [STATestMethod]
        public void IssueList_CountsAndSelects()
        {
            var b = Sample(out var l2, out _);
            b.AddWall(b.Ground, l2, 10, 80, 30, 80);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            var list = new IssueList(s);
            list.Show(ModelChecks.Run(b));
            var header = list.Children.OfType<System.Windows.Controls.TextBlock>().First();
            StringAssert.Contains(header.Text, "warning");
        }

        private static string SampleDxf([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "ShearWallCalculator", "samples", "Default_60x40_plan.dxf"));

        [STATestMethod]
        public void Underlay_DxfAtTrueSize_ImageCalibrates()
        {
            string dxf = SampleDxf();
            if (!File.Exists(dxf)) Assert.Inconclusive("The sample plan is in the Shear Wall Designer repo: " + dxf);
            var u = PlanUnderlay.Import(dxf);
            var r = u.WorldBounds();
            Assert.AreEqual(60, r.Width, 1.01, "the 60 x 40 ft plan (walls 6 in thick)");
            var back = PlanUnderlay.FromJson(u.ToJson());
            Assert.AreEqual(u.Scale, back.Scale, 1e-12);

            var img = new PlanUnderlay { Scale = 0.1, OriginX = 0, OriginY = 0 };
            Assert.IsTrue(img.Calibrate(new Point(0, 0), new Point(5, 0), 10));
            Assert.AreEqual(0.2, img.Scale, 1e-12);
            Assert.IsFalse(img.Calibrate(new Point(1, 1), new Point(1, 1), 10));
        }
    }
}
