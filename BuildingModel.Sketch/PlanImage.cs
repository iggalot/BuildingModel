using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BuildingModel.Diagnostics;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The plan under one level, to trace over: a picture (PNG, JPG, a PDF page rendered to PNG...) or CAD linework (DXF).
    /// It is the building's, so only the Building Model editor changes it; other apps only show or hide it.
    /// <para>
    /// Image pixels (u right, v down from the top-left corner) map to world feet (x right, y up) by a scale, a rotation and
    /// the world position of the top-left corner: world = Origin + R(θ) · (s·u, −s·v). A DXF is held in the same "pixel"
    /// coordinates, <see cref="VectorUnitFeet"/> ft each at import.
    /// </para>
    /// <para>
    /// Stored in the level's data bag (<see cref="DataKey"/>) as a small record: the file and its placement. The file itself
    /// is copied into the project's <see cref="FolderName"/> folder on import, so the model stays small (every undo step
    /// copies the model) and the project folder carries its plans. Older files are read too: the Shear Wall Calculator's
    /// "ShearWall.Image" (image bytes inside the file) and the first editor's "Sketch.Underlay"; see <see cref="Migrate"/>.
    /// </para>
    /// </summary>
    [DataContract]
    public sealed class PlanImage
    {
        public const string DataKey = "Sketch.Plan";
        public const string OldUnderlayKey = "Sketch.Underlay";
        public const string OldShearWallKey = "ShearWall.Image";

        /// <summary>The project subfolder imported plans are copied into.</summary>
        public const string FolderName = "plans";

        /// <summary>Size of one image unit of CAD linework, ft (fine enough that rounding to 0.01 unit is invisible).</summary>
        public const double VectorUnitFeet = 0.01;

        /// <summary>The plan file: relative to the project folder when it is inside it (usually plans\...), otherwise absolute.</summary>
        [DataMember(Order = 0)] public string File { get; set; }

        /// <summary>Feet per image pixel (per image unit for CAD linework).</summary>
        [DataMember(Order = 1)] public double FeetPerPixel { get; set; } = 1.0 / 12.0;

        /// <summary>Counter-clockwise rotation of the image on the plan, degrees.</summary>
        [DataMember(Order = 2)] public double RotationDegrees { get; set; }

        /// <summary>World position (ft) of the image's top-left corner.</summary>
        [DataMember(Order = 3)] public double OriginX { get; set; }
        [DataMember(Order = 4)] public double OriginY { get; set; }

        /// <summary>0 (invisible) to 1 (opaque).</summary>
        [DataMember(Order = 5)] public double Opacity { get; set; } = 0.6;

        /// <summary>A locked plan cannot be moved, rotated or rescaled.</summary>
        [DataMember(Order = 6)] public bool Locked { get; set; }

        [DataMember(Order = 7)] public bool Visible { get; set; } = true;

        /// <summary>Image size in pixels (CAD: in image units).</summary>
        [DataMember(Order = 8)] public int PixelWidth { get; set; }
        [DataMember(Order = 9)] public int PixelHeight { get; set; }

        /// <summary>What it came from, for the status and the log (e.g. "DXF, inches", "PDF page 2 of 5").</summary>
        [DataMember(Order = 10)] public string Source { get; set; }

        /// <summary>True once the scale has been set (calibration, a drawing scale or a DXF's units), not just guessed.</summary>
        [DataMember(Order = 11)] public bool Calibrated { get; set; }

        /// <summary>CAD only: the layers imported (null: all) and the drawing's feet per unit at import (rebuilds the linework).</summary>
        [DataMember(Order = 12)] public List<string> DxfLayers { get; set; }
        [DataMember(Order = 13)] public double DxfFeetPerUnit { get; set; }

        public bool IsDxf => (Path.GetExtension(File ?? "") ?? "").Equals(".dxf", StringComparison.OrdinalIgnoreCase);

        public Point Origin
        {
            get => new Point(OriginX, OriginY);
            set { OriginX = value.X; OriginY = value.Y; }
        }

        public PlanImage Clone()
        {
            var c = (PlanImage)MemberwiseClone();
            c.DxfLayers = DxfLayers?.ToList();
            return c;
        }

        // ------------------------------------------------------------------ placement

        private double Theta => RotationDegrees * Math.PI / 180.0;

        /// <summary>World ft of image pixel (u, v).</summary>
        public Point PixelToWorld(Point px)
        {
            double x = FeetPerPixel * px.X, y = -FeetPerPixel * px.Y;
            double c = Math.Cos(Theta), s = Math.Sin(Theta);
            return new Point(OriginX + c * x - s * y, OriginY + s * x + c * y);
        }

        /// <summary>Image pixel of world point <paramref name="w"/>.</summary>
        public Point WorldToPixel(Point w)
        {
            double dx = w.X - OriginX, dy = w.Y - OriginY;
            double c = Math.Cos(Theta), s = Math.Sin(Theta);
            double x = c * dx + s * dy, y = -s * dx + c * dy;
            return new Point(x / FeetPerPixel, -y / FeetPerPixel);
        }

        /// <summary>The affine map from image pixels to world ft, for drawing (combine with world → screen).</summary>
        public Matrix PixelToWorldMatrix()
        {
            Point o = PixelToWorld(new Point(0, 0)), ux = PixelToWorld(new Point(1, 0)), vy = PixelToWorld(new Point(0, 1));
            return new Matrix(ux.X - o.X, ux.Y - o.Y, vy.X - o.X, vy.Y - o.Y, o.X, o.Y);
        }

        /// <summary>
        /// Two-point scale: the two world points (picked on the plan as it is drawn now) are <paramref name="realFeet"/>
        /// apart. The plan is rescaled about the first point, which stays where it is.
        /// </summary>
        /// <exception cref="ArgumentException">The points are the same or the distance is not positive.</exception>
        public void Calibrate(Point a, Point b, double realFeet)
        {
            double picked = (b - a).Length;
            if (picked < 1e-9) throw new ArgumentException("Pick two different points.");
            if (!(realFeet > 0)) throw new ArgumentException("The distance must be greater than zero.");
            ScaleAbout(a, realFeet / picked);
            Calibrated = true;
        }

        /// <summary>Scales the plan by <paramref name="factor"/> about world point <paramref name="fixedPoint"/>.</summary>
        public void ScaleAbout(Point fixedPoint, double factor)
        {
            Point px = WorldToPixel(fixedPoint);
            FeetPerPixel *= factor;
            Origin += fixedPoint - PixelToWorld(px);
        }

        /// <summary>
        /// Straighten: rotates the plan about <paramref name="a"/> so the line a→b (picked on the plan) runs E-W or N-S,
        /// whichever is nearer. Returns the rotation applied (degrees, CCW).
        /// </summary>
        public double Straighten(Point a, Point b)
        {
            Vector d = b - a;
            if (d.Length < 1e-9) throw new ArgumentException("Pick two different points.");
            double angle = Math.Atan2(d.Y, d.X) * 180.0 / Math.PI;
            double turn = Math.Round(angle / 90.0) * 90.0 - angle;
            RotateAbout(a, turn);
            return turn;
        }

        /// <summary>Rotates the plan by <paramref name="degrees"/> (CCW) about world point <paramref name="pivot"/>.</summary>
        public void RotateAbout(Point pivot, double degrees)
        {
            Point px = WorldToPixel(pivot);
            RotationDegrees = NormalizeDegrees(RotationDegrees + degrees);
            Origin += pivot - PixelToWorld(px);
        }

        /// <summary>Moves the plan so world point <paramref name="from"/> lands on <paramref name="to"/>.</summary>
        public void Move(Point from, Point to) => Origin += to - from;

        /// <summary>World extents of the plan (for zoom to fit); empty when its size is not known yet.</summary>
        public Rect WorldBounds()
        {
            if (PixelWidth <= 0 || PixelHeight <= 0) return Rect.Empty;
            var r = Rect.Empty;
            foreach (var p in new[] { new Point(0, 0), new Point(PixelWidth, 0), new Point(PixelWidth, PixelHeight), new Point(0, PixelHeight) })
                r.Union(PixelToWorld(p));
            return r;
        }

        private static double NormalizeDegrees(double d)
        {
            d %= 360.0;
            if (d > 180) d -= 360;
            if (d <= -180) d += 360;
            return d;
        }

        /// <summary>
        /// Feet per pixel from the printed drawing scale and the image resolution: at <paramref name="dpi"/> pixels per inch
        /// of paper, and <paramref name="feetPerPaperInch"/> ft of building per inch of paper (1/4" = 1'-0" → 4).
        /// </summary>
        public static double FeetPerPixelFromDrawingScale(double feetPerPaperInch, double dpi)
        {
            if (!(feetPerPaperInch > 0) || !(dpi > 0)) throw new ArgumentException("The drawing scale and the resolution must be greater than zero.");
            return feetPerPaperInch / dpi;
        }

        /// <summary>Common architectural and engineering scales: label and feet of building per inch of paper.</summary>
        public static readonly (string label, double feetPerInch)[] DrawingScales =
        {
            ("1/16\" = 1'-0\"", 16), ("3/32\" = 1'-0\"", 32.0 / 3.0), ("1/8\" = 1'-0\"", 8), ("3/16\" = 1'-0\"", 16.0 / 3.0),
            ("1/4\" = 1'-0\"", 4), ("3/8\" = 1'-0\"", 8.0 / 3.0), ("1/2\" = 1'-0\"", 2),
            ("1\" = 10'", 10), ("1\" = 20'", 20), ("1\" = 30'", 30), ("1\" = 40'", 40),
        };

        /// <summary>Placement on import: the image's bottom-left corner at the world origin, unrotated, at <paramref name="feetPerPixel"/>.</summary>
        public static PlanImage Imported(string file, int pixelWidth, int pixelHeight, double feetPerPixel)
        {
            if (pixelWidth <= 0 || pixelHeight <= 0) throw new ArgumentException("The image has no pixels.");
            return new PlanImage
            {
                File = file, PixelWidth = pixelWidth, PixelHeight = pixelHeight, FeetPerPixel = feetPerPixel,
                OriginX = 0, OriginY = pixelHeight * feetPerPixel,
            };
        }

        /// <summary>
        /// CAD linework placed like an imported picture: at true size (<paramref name="feetPerUnit"/> ft per drawing unit),
        /// its lower-left corner at (0, 0), only the paths on <paramref name="layers"/> (null: all).
        /// </summary>
        /// <exception cref="ArgumentException">Nothing to import on those layers, or the units are not positive.</exception>
        public static PlanImage FromDxf(string file, DxfDrawing drawing, ICollection<string> layers, double feetPerUnit, string source)
        {
            if (!(feetPerUnit > 0)) throw new ArgumentException("Choose the drawing units.");
            var img = new PlanImage { File = file, FeetPerPixel = VectorUnitFeet, Source = source, Calibrated = true, Opacity = 0.8,
                                      DxfLayers = layers?.ToList(), DxfFeetPerUnit = feetPerUnit };
            var box = img.SetLinework(drawing);
            img.OriginX = 0;
            img.OriginY = box.Height * feetPerUnit; // exact, not the rounded-up height
            return img;
        }

        // ------------------------------------------------------------------ the file's content (loaded, not saved)

        [IgnoreDataMember] private List<double[]> paths;   // CAD: x, y pairs in image units (the last value of each array is 1 when closed)
        [IgnoreDataMember] private System.Windows.Media.Geometry linework;
        [IgnoreDataMember] private BitmapSource bitmap;
        [IgnoreDataMember] private bool loaded;

        /// <summary>Why the file could not be shown (null when it was).</summary>
        [IgnoreDataMember] public string LoadError { get; private set; }

        /// <summary>The CAD paths of <paramref name="drawing"/> on the chosen layers, in image units; returns the drawing box (units).</summary>
        private Rect SetLinework(DxfDrawing drawing)
        {
            var layers = DxfLayers;
            var kept = drawing.Paths.Where(p => layers == null || layers.Contains(p.Layer)).ToList();
            var box = drawing.Bounds(layers);
            if (kept.Count == 0 || box.IsEmpty) throw new ArgumentException("There is no linework on the chosen layers.");
            double k = DxfFeetPerUnit / VectorUnitFeet; // image units per drawing unit
            paths = kept.Where(p => p.Points.Count > 1).Select(p =>
            {
                var xy = new double[p.Points.Count * 2 + 1];
                for (int i = 0; i < p.Points.Count; i++)
                {
                    xy[2 * i] = Math.Round((p.Points[i].X - box.Left) * k, 2);
                    xy[2 * i + 1] = Math.Round((box.Bottom - p.Points[i].Y) * k, 2);
                }
                xy[xy.Length - 1] = p.Closed ? 1 : 0;
                return xy;
            }).ToList();
            PixelWidth = Math.Max(1, (int)Math.Ceiling(box.Width * k));
            PixelHeight = Math.Max(1, (int)Math.Ceiling(box.Height * k));
            var g = new StreamGeometry();
            using (var c = g.Open())
                foreach (var xy in paths)
                {
                    int n = (xy.Length - 1) / 2;
                    c.BeginFigure(new Point(xy[0], xy[1]), false, xy[xy.Length - 1] > 0);
                    var pts = new List<Point>(n - 1);
                    for (int i = 1; i < n; i++) pts.Add(new Point(xy[2 * i], xy[2 * i + 1]));
                    c.PolyLineTo(pts, true, false);
                }
            g.Freeze();
            linework = g;
            loaded = true;
            return box;
        }

        /// <summary>The full path of <see cref="File"/>: as stored, or under the project folder; also a file of the same name in the project's plans folder.</summary>
        public string FullPath(string projectFolder)
        {
            if (string.IsNullOrEmpty(File)) return null;
            string folder = projectFolder ?? ProjectFolder.Current;
            if (Path.IsPathRooted(File))
            {
                if (System.IO.File.Exists(File) || folder == null) return File;
                string moved = Path.Combine(folder, FolderName, Path.GetFileName(File));
                return System.IO.File.Exists(moved) ? moved : File;
            }
            return folder == null ? File : Path.Combine(folder, File);
        }

        /// <summary>
        /// Keeps the picture or linework <paramref name="previous"/> already read, when it is the same file (and, for CAD, the
        /// same layers and units): a moved or rescaled plan is not read from disk again.
        /// </summary>
        internal void AdoptContent(PlanImage previous)
        {
            if (previous == null || !previous.loaded || previous.LoadError != null) return;
            if (!string.Equals(previous.File, File, StringComparison.OrdinalIgnoreCase)) return;
            if (IsDxf && (previous.DxfFeetPerUnit != DxfFeetPerUnit || !(previous.DxfLayers ?? new List<string>()).SequenceEqual(DxfLayers ?? new List<string>()))) return;
            bitmap = previous.bitmap;
            linework = previous.linework;
            paths = previous.paths;
            loaded = true;
        }

        /// <summary>Reads the file once (a bad or missing file sets <see cref="LoadError"/>).</summary>
        public void EnsureLoaded(string projectFolder = null)
        {
            if (loaded) return;
            loaded = true;
            string path = FullPath(projectFolder);
            try
            {
                if (path == null || !System.IO.File.Exists(path)) { LoadError = "The plan file is not there: " + (path ?? "(none)") + ". Import it again."; return; }
                if (IsDxf)
                {
                    var d = DxfReader.ReadFile(path);
                    if (!(DxfFeetPerUnit > 0))
                    {
                        // converted from the Shear Wall Calculator: its stored width (image units) gives the units it was imported at
                        var box = d.Bounds(DxfLayers);
                        double f = PixelWidth > 0 && box.Width > 0 ? PixelWidth / box.Width * VectorUnitFeet : DxfReader.FeetPerUnit(d.InsUnits);
                        DxfFeetPerUnit = f > 0 ? f : 1;
                    }
                    loaded = false; // SetLinework sets it
                    SetLinework(d);
                }
                else
                {
                    bitmap = LoadBitmap(path);
                    if (PixelWidth <= 0 || PixelHeight <= 0) { PixelWidth = bitmap.PixelWidth; PixelHeight = bitmap.PixelHeight; }
                }
            }
            catch (Exception ex)
            {
                loaded = true;
                LoadError = "The plan " + path + " could not be read: " + ex.Message;
            }
        }

        internal static BitmapSource LoadBitmap(string path)
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(path, UriKind.Absolute);
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            b.EndInit();
            b.Freeze();
            return b;
        }

        private static readonly Pen LinePen = MakePen();
        private static Pen MakePen()
        {
            var p = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x68)), 1.0);
            p.Freeze();
            return p;
        }

        /// <summary>Draws the plan with the sketch's transform (image units → world → screen).</summary>
        public void Draw(DrawingContext ctx, SketchCanvas s)
        {
            if (!Visible) return;
            EnsureLoaded();
            var m = PixelToWorldMatrix();
            m.Append(new Matrix(s.Zoom, 0, 0, -s.Zoom, s.OriginX, s.OriginY));
            double opacity = Math.Max(0.05, Math.Min(1, Opacity));
            if (linework != null)
            {
                // the geometry is transformed, not the pen: lines stay one pixel wide at any zoom
                var g = new GeometryGroup { Transform = new MatrixTransform(m) };
                g.Children.Add(linework);
                ctx.PushOpacity(opacity);
                ctx.DrawGeometry(null, LinePen, g);
                ctx.Pop();
            }
            else if (bitmap != null)
            {
                ctx.PushTransform(new MatrixTransform(m));
                ctx.PushOpacity(opacity);
                ctx.DrawImage(bitmap, new Rect(0, 0, PixelWidth, PixelHeight));
                ctx.Pop();
                ctx.Pop();
            }
        }

        /// <summary>The CAD segments within <paramref name="tolerance"/> ft of a world point, in world ft (for object snaps). Empty for a picture.</summary>
        public List<Segment2> SegmentsNear(Point2 world, double tolerance)
        {
            var list = new List<Segment2>();
            EnsureLoaded();
            if (paths == null || !(FeetPerPixel > 0)) return list;
            Point c = WorldToPixel(new Point(world.X, world.Y));
            double t = tolerance / FeetPerPixel;
            foreach (var xy in paths)
            {
                int n = (xy.Length - 1) / 2;
                bool closed = xy[xy.Length - 1] > 0;
                for (int i = 0; i < (closed ? n : n - 1); i++)
                {
                    int j = (i + 1) % n;
                    double x1 = xy[2 * i], y1 = xy[2 * i + 1], x2 = xy[2 * j], y2 = xy[2 * j + 1];
                    if (c.X < Math.Min(x1, x2) - t || c.X > Math.Max(x1, x2) + t || c.Y < Math.Min(y1, y2) - t || c.Y > Math.Max(y1, y2) + t) continue;
                    Point a = PixelToWorld(new Point(x1, y1)), b = PixelToWorld(new Point(x2, y2));
                    list.Add(new Segment2(new Point2(a.X, a.Y), new Point2(b.X, b.Y)));
                }
            }
            return list;
        }

        // ------------------------------------------------------------------ the project's copy of the file

        /// <summary>
        /// Copies <paramref name="sourceFile"/> (or writes <paramref name="bytes"/>) into the project's plans folder and
        /// points <see cref="File"/> at it, relative to the project. Without a project folder the file is used where it is
        /// (bytes go to the user's local plans folder). An existing different file of the same name gets a numbered name.
        /// </summary>
        public void StoreInProject(string sourceFile, byte[] bytes = null, string fileName = null, string projectFolder = null)
        {
            string folder = projectFolder ?? ProjectFolder.Current;
            string name = fileName ?? Path.GetFileName(sourceFile);
            if (folder == null)
            {
                if (bytes == null) { File = sourceFile; return; }
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildingModel");
            }
            string dir = Path.Combine(folder, FolderName);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, name);
            byte[] content = bytes ?? System.IO.File.ReadAllBytes(sourceFile);
            for (int n = 2; System.IO.File.Exists(target) && !System.IO.File.ReadAllBytes(target).SequenceEqual(content); n++)
                target = Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + $" ({n})" + Path.GetExtension(name));
            if (!System.IO.File.Exists(target)) System.IO.File.WriteAllBytes(target, content);
            bool inProject = (projectFolder ?? ProjectFolder.Current) != null;
            File = inProject ? Path.Combine(FolderName, Path.GetFileName(target)) : target;
            loaded = false;
            LoadError = null;
            if (bytes != null) Log.Info("Plan", $"Plan stored as {target}.");
            else if (!string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                Log.Info("Plan", $"Plan {sourceFile} copied to {target}.");
        }

        // ------------------------------------------------------------------ the level's data bag

        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(PlanImage));

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static PlanImage FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var p = (PlanImage)Serializer().ReadObject(ms);
                    if (p != null && p.FeetPerPixel <= 0) p.FeetPerPixel = 1.0 / 12.0;
                    return p;
                }
            }
            catch (SerializationException) { return null; }
        }

        /// <summary>The plan of a level (null when it has none).</summary>
        public static PlanImage Of(Level l) => l == null ? null : FromJson(l.GetData(DataKey));

        /// <summary>Stores (or, for null, removes) a level's plan. Make the change inside an edit so it can be undone.</summary>
        public static void Put(Level l, PlanImage p) => l.SetData(DataKey, p?.ToJson());

        // ------------------------------------------------------------------ older files

        /// <summary>
        /// Converts the plans of older files on every level to <see cref="DataKey"/>, and removes the old entries:
        /// <list type="bullet">
        /// <item>"ShearWall.Image" (the Shear Wall Calculator's plan, the picture inside the file): the picture is written to the
        /// project's plans folder; CAD linework is rebuilt from its DXF when that is found.</item>
        /// <item>"Sketch.Underlay" (the first editor's plan: a file path, a scale and a position).</item>
        /// </list>
        /// A level that already has a plan keeps it (the old entry is dropped). Returns what was done, one line per level.
        /// </summary>
        public static List<string> Migrate(Building b, string projectFolder = null)
        {
            var done = new List<string>();
            if (b == null) return done;
            foreach (var l in b.Levels)
            {
                string swc = l.GetData(OldShearWallKey), old = l.GetData(OldUnderlayKey);
                if (swc == null && old == null) continue;
                if (l.GetData(DataKey) == null)
                {
                    PlanImage p = null;
                    try { p = swc != null ? FromShearWall(swc, projectFolder) : FromUnderlay(old); }
                    catch (Exception ex) { Log.Warning("Plan", $"The plan of {l.Name} in an older format could not be converted.", ex); }
                    if (p != null)
                    {
                        Put(l, p);
                        done.Add($"{l.Name}: plan {p.File} converted from the " + (swc != null ? "Shear Wall Calculator's format." : "first editor's format."));
                    }
                }
                l.SetData(OldShearWallKey, null);
                l.SetData(OldUnderlayKey, null);
            }
            foreach (var line in done) Log.Info("Plan", line);
            return done;
        }

        [DataContract]
        private sealed class OldShearWallImage
        {
            [DataMember] public string Path { get; set; }
            [DataMember] public double FeetPerPixel { get; set; }
            [DataMember] public double RotationDegrees { get; set; }
            [DataMember] public string Origin { get; set; }   // "x,y" (System.Windows.Point as Newtonsoft writes it)
            [DataMember] public double Opacity { get; set; } = 0.6;
            [DataMember] public bool Locked { get; set; }
            [DataMember] public bool Visible { get; set; } = true;
            [DataMember] public int PixelWidth { get; set; }
            [DataMember] public int PixelHeight { get; set; }
            [DataMember] public string Data { get; set; }     // base64
            [DataMember] public List<OldPath> Paths { get; set; }
            [DataMember] public string Source { get; set; }
            [DataMember] public bool Calibrated { get; set; }
        }

        [DataContract]
        private sealed class OldPath
        {
            [DataMember] public string Layer { get; set; }
            [DataMember] public bool Closed { get; set; }
            [DataMember] public double[] Xy { get; set; }
        }

        [DataContract]
        private sealed class OldUnderlay
        {
            [DataMember] public string Path { get; set; }
            [DataMember] public double Scale { get; set; } = 1;
            [DataMember] public double OriginX { get; set; }
            [DataMember] public double OriginY { get; set; }
            [DataMember] public bool Visible { get; set; } = true;
        }

        private static T Read<T>(string json)
        {
            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T), settings).ReadObject(ms);
        }

        /// <summary>The Shear Wall Calculator's plan (same placement model); its stored picture becomes a file in the project.</summary>
        internal static PlanImage FromShearWall(string json, string projectFolder)
        {
            var o = Read<OldShearWallImage>(json);
            if (o == null) return null;
            var origin = string.IsNullOrEmpty(o.Origin) ? new Point(0, 0) : Point.Parse(o.Origin.Replace(";", ","));
            var p = new PlanImage
            {
                File = o.Path, FeetPerPixel = o.FeetPerPixel > 0 ? o.FeetPerPixel : 1.0 / 12.0, RotationDegrees = o.RotationDegrees, Origin = origin,
                Opacity = o.Opacity, Locked = o.Locked, Visible = o.Visible, PixelWidth = o.PixelWidth, PixelHeight = o.PixelHeight,
                Source = o.Source, Calibrated = o.Calibrated,
            };
            if (!string.IsNullOrEmpty(o.Data))
            {
                string name = Path.GetFileName(o.Path ?? "") ;
                if (string.IsNullOrEmpty(name) || (o.Source ?? "").StartsWith("PDF", StringComparison.OrdinalIgnoreCase))
                    name = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(name) ? "plan" : name) + ".png";
                p.StoreInProject(null, Convert.FromBase64String(o.Data), name, projectFolder);
            }
            else if (o.Paths != null && o.Paths.Count > 0)
            {
                // CAD linework: the old file kept the linework; this format rebuilds it from the DXF (all layers, the units
                // that give the same size). The placement is kept, so it lines up when the DXF is found.
                p.DxfLayers = o.Paths.Select(x => x.Layer).Distinct().ToList();
                p.DxfFeetPerUnit = 0; // from the DXF's units on loading
                p.FeetPerPixel = o.FeetPerPixel > 0 ? o.FeetPerPixel : VectorUnitFeet;
                if (!string.IsNullOrEmpty(o.Path) && System.IO.File.Exists(o.Path)) p.StoreInProject(o.Path, null, null, projectFolder);
            }
            return p;
        }

        /// <summary>The first editor's plan: world = origin + scale·p for CAD (y up), the top-left corner at the origin for a picture.</summary>
        internal static PlanImage FromUnderlay(string json)
        {
            var o = Read<OldUnderlay>(json);
            if (o == null || string.IsNullOrEmpty(o.Path)) return null;
            var p = new PlanImage { File = o.Path, Visible = o.Visible, Opacity = 0.45 };
            if (p.IsDxf)
            {
                p.DxfFeetPerUnit = o.Scale > 0 ? o.Scale : 1;
                p.FeetPerPixel = VectorUnitFeet;
                p.Calibrated = true;
                p.Opacity = 0.8;
                if (System.IO.File.Exists(o.Path))
                {
                    var box = p.SetLinework(DxfReader.ReadFile(o.Path));
                    p.OriginX = o.OriginX + p.DxfFeetPerUnit * box.Left;
                    p.OriginY = o.OriginY + p.DxfFeetPerUnit * box.Bottom;
                }
                else { p.OriginX = o.OriginX; p.OriginY = o.OriginY; }
            }
            else
            {
                p.FeetPerPixel = o.Scale > 0 ? o.Scale : 1.0 / 12.0;
                p.OriginX = o.OriginX;
                p.OriginY = o.OriginY;
                p.Calibrated = true;
            }
            return p;
        }
    }
}
