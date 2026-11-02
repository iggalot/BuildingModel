using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BuildingModel.Geometry;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Sketch.Tests
{
    /// <summary>
    /// The plan under a level (<see cref="PlanImage"/>): pixel ↔ world, two-point scale, straighten, move, drawing-scale
    /// import (moved here from the Shear Wall Calculator's ReferenceImageTests); the record in the level, the file in the
    /// project's plans folder, older files converted, and the plan edits as undoable model edits.
    /// </summary>
    [TestClass]
    public class PlanImageTests
    {
        private const double Tol = 1e-9;

        private static void Near(Point expected, Point actual, string what = null)
        {
            Assert.AreEqual(expected.X, actual.X, 1e-7, what);
            Assert.AreEqual(expected.Y, actual.Y, 1e-7, what);
        }

        private static string TempFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bm-plan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>A real PNG of <paramref name="w"/> × <paramref name="h"/> pixels.</summary>
        private static byte[] Png(int w, int h)
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, new byte[w * h], w);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }

        // ------------------------------------------------------------------ placement

        [TestMethod]
        public void Imported_BottomLeftAtOrigin_PixelRowsGoDown()
        {
            // 1000 x 500 px at 0.05 ft/px: 50 ft x 25 ft; top-left pixel at (0, 25)
            var img = PlanImage.Imported("plan.png", 1000, 500, 0.05);
            Near(new Point(0, 25), img.PixelToWorld(new Point(0, 0)));
            Near(new Point(0, 0), img.PixelToWorld(new Point(0, 500)), "bottom-left");
            Near(new Point(50, 0), img.PixelToWorld(new Point(1000, 500)), "bottom-right");
            Near(new Point(250, 100), img.WorldToPixel(img.PixelToWorld(new Point(250, 100))), "round trip");
            Assert.AreEqual(new Rect(0, 0, 50, 25), img.WorldBounds());
        }

        [TestMethod]
        public void DrawingScale_QuarterInchAt150Dpi()
        {
            // 1/4" = 1'-0": 4 ft per paper inch; 150 px per inch → 4 / 150 ft per pixel (0.32 in)
            Assert.AreEqual(4.0 / 150.0, PlanImage.FeetPerPixelFromDrawingScale(4, 150), Tol);
            Assert.Throws<ArgumentException>(() => PlanImage.FeetPerPixelFromDrawingScale(4, 0));
        }

        [TestMethod]
        public void Calibrate_TwoPoints_FirstPointStays()
        {
            // a line drawn 10 ft long on the plan is really 30 ft: everything triples about the first point
            var img = PlanImage.Imported("p.png", 400, 400, 0.1);
            Point a = new Point(5, 5), b = new Point(15, 5);
            Point pxA = img.WorldToPixel(a), pxB = img.WorldToPixel(b);
            img.Calibrate(a, b, 30);
            Assert.AreEqual(0.3, img.FeetPerPixel, Tol);
            Near(a, img.PixelToWorld(pxA), "the first point stays");
            Assert.AreEqual(30, (img.PixelToWorld(pxB) - a).Length, 1e-7);
            Assert.IsTrue(img.Calibrated);
            Assert.Throws<ArgumentException>(() => img.Calibrate(a, a, 10));
            Assert.Throws<ArgumentException>(() => img.Calibrate(a, b, 0));
        }

        [TestMethod]
        public void Straighten_ToNearestAxis_AboutFirstPoint()
        {
            // a wall line on the scan runs 3° off E-W: rotate by −3°
            var img = PlanImage.Imported("p.png", 400, 400, 0.1);
            double t = 3 * Math.PI / 180;
            Point a = new Point(10, 10), b = new Point(10 + 20 * Math.Cos(t), 10 + 20 * Math.Sin(t));
            Point pxB = img.WorldToPixel(b);
            Assert.AreEqual(-3, img.Straighten(a, b), 1e-9);
            Assert.AreEqual(-3, img.RotationDegrees, 1e-9);
            Point nb = img.PixelToWorld(pxB);
            Assert.AreEqual(10, nb.Y, 1e-7, "now level");
            Assert.AreEqual(30, nb.X, 1e-7);

            // 88° → 90° (N-S)
            var img2 = PlanImage.Imported("p.png", 400, 400, 0.1);
            double t2 = 88 * Math.PI / 180;
            Assert.AreEqual(2, img2.Straighten(new Point(0, 0), new Point(Math.Cos(t2), Math.Sin(t2))), 1e-9);
        }

        [TestMethod]
        public void Rotated_PixelWorldRoundTrip_AndMatrixAgrees()
        {
            var img = new PlanImage { FeetPerPixel = 0.02, RotationDegrees = 30, Origin = new Point(3, 40), PixelWidth = 800, PixelHeight = 600 };
            var px = new Point(123, 456);
            Near(px, img.WorldToPixel(img.PixelToWorld(px)));
            Near(img.PixelToWorld(px), img.PixelToWorldMatrix().Transform(px), "drawing matrix");
            Vector u = img.PixelToWorld(new Point(100, 0)) - img.Origin; // +u runs at 30° CCW from +x
            Assert.AreEqual(30, Math.Atan2(u.Y, u.X) * 180 / Math.PI, 1e-9);
        }

        [TestMethod]
        public void Move_BasePointToDestination()
        {
            var img = PlanImage.Imported("p.png", 100, 100, 0.1);
            Point px = img.WorldToPixel(new Point(2, 3));
            img.Move(new Point(2, 3), new Point(12, -4));
            Near(new Point(12, -4), img.PixelToWorld(px));
        }

        // ------------------------------------------------------------------ stored in the level, the file in the project

        [TestMethod]
        public void TheLevelKeepsThePlacement_NotThePicture()
        {
            var p = new PlanImage
            {
                File = @"plans\first floor.png", FeetPerPixel = 0.0266, RotationDegrees = -2.5, Origin = new Point(-3.25, 61.5),
                Opacity = 0.4, Locked = true, Visible = false, PixelWidth = 3300, PixelHeight = 2550, Calibrated = true, Source = "PDF page 2 of 5",
            };
            var l = new Level { Id = "L2", Name = "Level 2" };
            PlanImage.Put(l, p);
            string json = l.GetData(PlanImage.DataKey);
            Assert.IsTrue(json.Length < 600, "a small record (every undo step copies the model): " + json.Length);
            var read = PlanImage.Of(l);
            Assert.AreEqual(p.File, read.File);
            Assert.AreEqual(0.0266, read.FeetPerPixel, Tol);
            Assert.AreEqual(-2.5, read.RotationDegrees, Tol);
            Near(new Point(-3.25, 61.5), read.Origin);
            Assert.AreEqual(0.4, read.Opacity, Tol);
            Assert.IsTrue(read.Locked);
            Assert.IsFalse(read.Visible);
            Assert.AreEqual(3300, read.PixelWidth);
            Assert.IsTrue(read.Calibrated);
            Assert.AreEqual("PDF page 2 of 5", read.Source);
            PlanImage.Put(l, null);
            Assert.IsNull(PlanImage.Of(l));
        }

        [STATestMethod]
        public void Import_CopiesTheFileIntoThePlansFolder_AndReadsItFromThere()
        {
            string project = TempFolder(), elsewhere = TempFolder();
            try
            {
                string src = Path.Combine(elsewhere, "scan.png");
                File.WriteAllBytes(src, Png(40, 20));
                var p = PlanImage.Imported(src, 40, 20, 0.5);
                p.StoreInProject(src, projectFolder: project);
                Assert.AreEqual(@"plans\scan.png", p.File, "relative to the project");
                Assert.IsTrue(File.Exists(Path.Combine(project, "plans", "scan.png")));

                File.Delete(src); // the original is gone: the project still has the plan
                var back = PlanImage.FromJson(p.ToJson());
                back.EnsureLoaded(project);
                Assert.IsNull(back.LoadError);
                Assert.AreEqual(new Rect(0, 0, 20, 10), back.WorldBounds());

                // a different file of the same name does not overwrite it; the same file is not copied twice
                File.WriteAllBytes(src, Png(10, 10));
                var other = PlanImage.Imported(src, 10, 10, 1);
                other.StoreInProject(src, projectFolder: project);
                Assert.AreEqual(@"plans\scan (2).png", other.File);
                var again = PlanImage.Imported(src, 10, 10, 1);
                again.StoreInProject(src, projectFolder: project);
                Assert.AreEqual(@"plans\scan (2).png", again.File);
            }
            finally { Directory.Delete(project, true); Directory.Delete(elsewhere, true); }
        }

        [STATestMethod]
        public void AMissingFile_IsReported_NotThrown()
        {
            var p = new PlanImage { File = @"Z:\gone\plan.png" };
            p.EnsureLoaded(TempFolder());
            StringAssert.Contains(p.LoadError, "not there");
            Assert.AreEqual(Rect.Empty, p.WorldBounds());
        }

        // ------------------------------------------------------------------ older files

        [STATestMethod]
        public void ShearWallCalculatorPlan_TheStoredPictureBecomesAFile_AtTheSamePlace()
        {
            string project = TempFolder();
            try
            {
                var bytes = Png(30, 20);
                // as the Shear Wall Calculator wrote it (Newtonsoft: the Point as "x,y", the picture as base64)
                string json = "{\"Path\":\"C:\\\\plans\\\\first floor.png\",\"FeetPerPixel\":0.1,\"RotationDegrees\":3.0,\"Origin\":\"-2.5,17\"," +
                              "\"Opacity\":0.4,\"Locked\":true,\"Visible\":false,\"PixelWidth\":30,\"PixelHeight\":20," +
                              "\"Data\":\"" + Convert.ToBase64String(bytes) + "\",\"Source\":\"Image\",\"IsVector\":false,\"Calibrated\":true}";
                var b = Building.New("Old");
                var l2 = b.AddLevel("Level 2", 10);
                l2.SetData(PlanImage.OldShearWallKey, json);

                var done = PlanImage.Migrate(b, project);
                Assert.AreEqual(1, done.Count);
                Assert.IsNull(l2.GetData(PlanImage.OldShearWallKey), "the old entry is removed");
                var p = PlanImage.Of(l2);
                Assert.AreEqual(@"plans\first floor.png", p.File);
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(project, "plans", "first floor.png")));
                Assert.AreEqual(0.1, p.FeetPerPixel, Tol);
                Assert.AreEqual(3, p.RotationDegrees, Tol);
                Near(new Point(-2.5, 17), p.Origin);
                Assert.AreEqual(0.4, p.Opacity, Tol);
                Assert.IsTrue(p.Locked && !p.Visible && p.Calibrated);
                Assert.AreEqual(0, PlanImage.Migrate(b, project).Count, "nothing left to convert");
            }
            finally { Directory.Delete(project, true); }
        }

        [TestMethod]
        public void FirstEditorPlan_Converted_ALevelWithAPlanKeepsIt()
        {
            var b = Building.New("Old");
            var l2 = b.AddLevel("Level 2", 10);
            var roof = b.AddLevel("Roof", 19);
            l2.SetData(PlanImage.OldUnderlayKey, "{\"Path\":\"C:\\\\plans\\\\scan.png\",\"Scale\":0.05,\"OriginX\":1,\"OriginY\":30,\"Visible\":true}");
            PlanImage.Put(roof, new PlanImage { File = @"plans\roof.png" });
            roof.SetData(PlanImage.OldUnderlayKey, "{\"Path\":\"C:\\\\plans\\\\old roof.png\",\"Scale\":1}");

            PlanImage.Migrate(b);
            var p = PlanImage.Of(l2);
            Assert.AreEqual(@"C:\plans\scan.png", p.File);
            Assert.AreEqual(0.05, p.FeetPerPixel, Tol);
            Near(new Point(1, 30), p.Origin, "the picture's top-left corner");
            Assert.AreEqual(@"plans\roof.png", PlanImage.Of(roof).File, "kept");
            Assert.IsNull(roof.GetData(PlanImage.OldUnderlayKey));
        }

        private static string SampleDxf([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "ShearWallCalculator", "samples", "Default_60x40_plan.dxf"));

        [STATestMethod]
        public void Dxf_TrueSize_TheLineworkIsRebuiltFromTheFile_AndSnapsAtTheRightPlace()
        {
            string dxf = SampleDxf();
            if (!File.Exists(dxf)) Assert.Inconclusive("The sample plan is in the Shear Wall Calculator repo: " + dxf);
            // the first editor's DXF underlay: drawing units in ft (scale 1), at its own coordinates
            var b = Building.New("Old");
            var l2 = b.AddLevel("Level 2", 10);
            l2.SetData(PlanImage.OldUnderlayKey, "{\"Path\":" + "\"" + dxf.Replace("\\", "\\\\") + "\",\"Scale\":1,\"OriginX\":0,\"OriginY\":0}");
            PlanImage.Migrate(b);
            var p = PlanImage.Of(l2);
            Assert.IsTrue(p.IsDxf);
            p.EnsureLoaded();
            Assert.IsNull(p.LoadError);
            var r = p.WorldBounds();
            Assert.AreEqual(60, r.Width, 1.01, "the 60 x 40 ft plan (walls 6 in thick)");
            Assert.IsTrue(p.SegmentsNear(new Point2(r.X + 10, r.Y), 0.6).Count > 0, "a CAD line along the south wall");

            // moved 5 ft east: rebuilt from the file at the new place
            p.Move(new Point(0, 0), new Point(5, 0));
            var again = PlanImage.FromJson(p.ToJson());
            again.EnsureLoaded();
            Assert.AreEqual(r.X + 5, again.WorldBounds().X, 1e-6);
        }

        // ------------------------------------------------------------------ edits in the sketch

        [STATestMethod]
        public void PlanEdits_AreUndoable_AndMoveToAnotherFloor()
        {
            var b = Building.New("Plans");
            var l2 = b.AddLevel("Level 2", 10);
            var roof = b.AddLevel("Roof", 19);
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);

            s.SetPlan(l2, new PlanImage { File = @"plans\a.png", PixelWidth = 100, PixelHeight = 100, FeetPerPixel = 0.1, OriginY = 10 }, null);
            Assert.IsNotNull(s.PlanOf(s.Building.Level(l2.Id)));
            s.EditPlan(s.Building.Level(l2.Id), "moved", p => p.Move(new Point(0, 0), new Point(3, 4)));
            Near(new Point(3, 14), s.PlanOf(s.Building.Level(l2.Id)).Origin);
            s.Undo();
            Near(new Point(0, 10), s.PlanOf(s.Building.Level(l2.Id)).Origin, "undo puts it back");
            s.Redo();
            Near(new Point(3, 14), s.PlanOf(s.Building.Level(l2.Id)).Origin);

            // Move to floor (as the Plan menu does it): one edit
            var plan = s.PlanOf(s.Building.Level(l2.Id)).Clone();
            s.Edit("Plan moved from Level 2 to Roof.", x => { PlanImage.Put(x.Level(roof.Id), plan); PlanImage.Put(x.Level(l2.Id), null); });
            Assert.IsNull(s.PlanOf(s.Building.Level(l2.Id)));
            Near(new Point(3, 14), s.PlanOf(s.Building.Level(roof.Id)).Origin);
            s.Undo();
            Assert.IsNotNull(s.PlanOf(s.Building.Level(l2.Id)));
            Assert.IsNull(s.PlanOf(s.Building.Level(roof.Id)));

            s.SetPlan(s.Building.Level(l2.Id), null, null);
            Assert.IsNull(s.PlanOf(s.Building.Level(l2.Id)));
            s.Undo();
            Assert.IsNotNull(s.PlanOf(s.Building.Level(l2.Id)), "remove is undone too");
        }

        [STATestMethod]
        public void OpeningAnOlderModel_ConvertsItsPlans_WithoutAnUndoStep()
        {
            var b = Building.New("Old");
            var l2 = b.AddLevel("Level 2", 10);
            l2.SetData(PlanImage.OldUnderlayKey, "{\"Path\":\"C:\\\\plans\\\\scan.png\",\"Scale\":0.05,\"OriginX\":0,\"OriginY\":0}");
            var s = new SketchCanvas();
            s.SetBuilding(b, l2.Id);
            Assert.IsNotNull(PlanImage.Of(s.Building.Level(l2.Id)));
            Assert.IsFalse(s.History.CanUndo);
        }

        [STATestMethod]
        public void AViewOnlyHost_HidesThePlansOnItsScreen_TheModelKeepsThem()
        {
            var b = Building.New("View");
            var l2 = b.AddLevel("Level 2", 10);
            PlanImage.Put(l2, new PlanImage { File = @"plans\a.png" });
            var v = new SketchView { PlanEditing = false };
            v.Canvas.SetBuilding(b, l2.Id);
            v.Canvas.ShowUnderlay = false;
            Assert.IsNotNull(PlanImage.Of(v.Canvas.Building.Level(l2.Id)));
            Assert.IsTrue(PlanImage.Of(v.Canvas.Building.Level(l2.Id)).Visible, "hiding on one screen does not change the model");
        }
    }
}
