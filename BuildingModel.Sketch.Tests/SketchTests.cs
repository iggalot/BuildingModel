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
        public void GeometryLocked_DrawingAndMovingArePutBack_DataStillChanges()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            BuildingEdits.BuildFromSize(b, new BuildingEdits.QuickBuilding { Length = 30, Width = 20, StoryHeights = { 10 } });
            var s = new SketchCanvas();
            s.SetBuilding(b, b.LevelsBottomUp.Last().Id);
            s.GeometryLocked = true;
            int refused = 0;
            s.GeometryEditRefused += (o, e) => refused++;
            int walls = b.Walls.Count;
            var start = b.Walls[0].Start;

            Assert.IsFalse(s.Edit("moved", x => x.Walls[0].Start = new BuildingModel.Geometry.Point2(start.X - 5, start.Y)));
            Assert.AreEqual(start, s.Building.Walls[0].Start, "put back");
            Assert.IsTrue(s.HandleKey(Key.W, ModifierKeys.None));
            s.TypePoint("100,100");
            s.TypePoint("@10,0");
            Assert.AreEqual(walls, s.Building.Walls.Count, "no wall drawn");
            Assert.IsTrue(refused >= 2);
            StringAssert.Contains(s.GeometryLockedMessage, "Building Model");

            Assert.IsTrue(s.Edit("designed", x => x.Walls[0].SetData("ShearWall", "7/16 OSB")), "a wall's design is data: it changes");
            Assert.AreEqual("7/16 OSB", s.Building.Walls[0].GetData("ShearWall"));
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

        /// <summary>New from size: the window reads the inputs, the editor builds the box in one undo step, the host's limit holds.</summary>
        [STATestMethod]
        public void NewFromSize_PanelAndBuild_OneUndo()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 20, 20));
            var p = new QuickBuildingPanel(b, maxStories: 3);
            Assert.IsNotNull(p.Result, "starts from the building: 20 × 20, one 10 ft story");
            Assert.AreEqual(20, p.Result.Length);
            p.Type("length", "60");
            p.Type("width", "40");
            p.Type("stories", "3");
            p.Type("heights", "15, 12");
            Assert.IsNull(p.Result, "two heights for three stories");
            StringAssert.Contains(p.Problem, "3 heights");
            p.Type("heights", "15, 12, 12");
            Assert.IsNotNull(p.Result);
            p.Type("stories", "4");
            Assert.IsNull(p.Result, "over the host's limit");
            p.Type("stories", "3");
            p.Type("heights", "14");
            CollectionAssert.AreEqual(new[] { 14.0, 14.0, 14.0 }, p.Result.StoryHeights, "one height for every story");

            var view = new SketchView();
            view.Canvas.SetBuilding(b, l2.Id);
            Assert.IsTrue(view.BuildFromSize(p.Result));
            Assert.AreEqual(3, b.Levels.Count(l => l.Elevation > 0));
            Assert.AreEqual(12, b.Walls.Count);
            Assert.AreEqual("Level 2", view.Canvas.ActiveLevel.Name, "the first floor is shown");
            view.Canvas.Undo();
            Assert.AreEqual(1, view.Canvas.Building.Levels.Count(l => l.Elevation > 0), "one undo puts the old drawing back");
            Assert.AreEqual(20, view.Canvas.Building.Footprint().Bounds.Width, 1e-9);
        }

        /// <summary>The roof window: made sloped it takes the project's default slope; the model's checks disable OK.</summary>
        [STATestMethod]
        public void RoofPanel_DefaultSlope_ChecksFromTheModel_BuildingUnchanged()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            b.Defaults.RoofPitch = 26.57; // 6:12, this project's choice
            b.Roof.Pitch = 0;
            var p = new RoofPanel(b);
            Assert.IsFalse(p.HasErrors);
            p.SetShape(RoofShape.Gable);
            Assert.AreEqual(26.57, p.Roof.Pitch, 1e-9, "the project default, not a number in the program (kept exact, though shown as 6:12)");
            Assert.AreEqual("6.0:12", p.Roof.RisePer12);
            p.Type("pitch", "0");
            Assert.IsTrue(p.HasErrors, "a 0° gable");
            p.Type("pitch", "abc");
            Assert.IsTrue(p.HasErrors, "not a number");
            p.Type("pitch", "8");
            Assert.AreEqual(Math.Atan(8 / 12.0) * 180 / Math.PI, p.Roof.Pitch, 1e-9, "8 = 8:12");
            p.Type("eaves", "2");
            Assert.IsFalse(p.HasErrors);
            Assert.AreEqual(2, p.Roof.Overhang);
            Assert.AreEqual(RoofShape.Flat, b.Roof.Shape, "the building changes only on OK");
        }

        /// <summary>
        /// Grips: every selected wall has a middle grip that moves the whole selection straight across or up (one undo),
        /// and end grips that change that wall's length; nothing selected, no grips.
        /// </summary>
        [STATestMethod]
        public void Grips_MiddleMovesTheSelection_EndChangesTheLength()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            string w1 = b.AddWall(b.Ground, l2, 0, 0, 20, 0).Id;
            string w2 = b.AddWall(b.Ground, l2, 0, 10, 20, 10).Id;
            string w3 = b.AddWall(b.Ground, l2, 30, 0, 30, 20).Id;
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            Wall W(string id) => (Wall)s.Building.Find(id);
            Assert.IsFalse(s.DragGrip(new Point2(10, 0), new Point2(10, 5)), "nothing selected: no grips");

            s.Select(new[] { w1, w2 });
            Assert.IsTrue(s.DragGrip(new Point2(10, 10), new Point2(13, 4.5)), "w2's middle, dragged mostly south");
            Assert.AreEqual(new Point2(0, -5.5), W(w1).Start, "both selected walls move, straight south");
            Assert.AreEqual(new Point2(20, 4.5), W(w2).End);
            Assert.AreEqual(new Point2(30, 0), W(w3).Start, "w3 was not selected");

            Assert.IsTrue(s.DragGrip(new Point2(20, -5.5), new Point2(26, -1)), "w1's east end");
            Assert.AreEqual(new Point2(26, -5.5), W(w1).End, "longer, still on its line");
            Assert.AreEqual(20, W(w2).Length, 1e-9, "only the wall whose grip it was");

            s.Undo();
            Assert.AreEqual(20, W(w1).Length, 1e-9);
            s.Undo();
            Assert.AreEqual(new Point2(0, 0), W(w1).Start, "the move is one undo step");
            Assert.AreEqual(new Point2(0, 10), W(w2).Start);
        }

        /// <summary>K is the Cut tool: two typed corners take a notch out of the diaphragm; one undo puts it back.</summary>
        [STATestMethod]
        public void CutTool_TakesANotchOut_UndoPutsItBack()
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            var d = b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            Assert.IsTrue(s.HandleKey(Key.K, ModifierKeys.None));
            Assert.AreEqual(SketchTool.Cut, s.Tool);
            Assert.IsTrue(s.TypePoint("40,20"));
            Assert.IsTrue(s.TypePoint("@20,20"));        // the north-east corner, 20 × 20
            Assert.AreEqual(2400 - 400, Region.Of(b.DiaphragmsOn(l2).Single().Pieces).Area, 1e-9);
            Assert.AreEqual(d.Id, b.DiaphragmsOn(l2).Single().Id, "the same diaphragm, smaller");
            s.Undo();
            Assert.AreEqual(2400, Region.Of(b.DiaphragmsOn(l2).Single().Pieces).Area, 1e-9);
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
        public void View3D_ShowsBeamsAndColumns()
        {
            var b = Sample(out var l2, out _);
            var bb = b.Footprint().Bounds;
            double y = (bb.Y0 + bb.Y1) / 2;
            b.AddBeam(l2, bb.X0, y, bb.X1, y);
            b.AddColumn(b.Ground, l2, (bb.X0 + bb.X1) / 2, y);
            var v = new Building3DView { HighlightLevelId = l2.Id };
            v.Show(b);
            var bmp = Render(v, "building-3d-beams");
            Assert.IsTrue(Count(bmp, Color.FromRgb(0x8B, 0x4A, 0x14), 70) > 50, "the beam and the column in timber brown");
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

    }
}
