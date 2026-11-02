using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// A plan to trace over, under one level: a CAD drawing (DXF, at true size from its units) or an image (scaled by
    /// calibrating two points a known distance apart). Kept in the level's data bag as a path, a scale and a position;
    /// the file itself is read again when the model is opened.
    /// </summary>
    [DataContract]
    public sealed class PlanUnderlay
    {
        public const string DataKey = "Sketch.Underlay";

        [DataMember(Order = 0)] public string Path { get; set; }

        /// <summary>Feet per drawing unit (DXF) or per pixel (image).</summary>
        [DataMember(Order = 1)] public double Scale { get; set; } = 1;

        /// <summary>Where the drawing's origin (DXF) or the image's top-left corner (image) is, in ft.</summary>
        [DataMember(Order = 2)] public double OriginX { get; set; }
        [DataMember(Order = 3)] public double OriginY { get; set; }

        [DataMember(Order = 4)] public bool Visible { get; set; } = true;

        public bool IsDxf => (System.IO.Path.GetExtension(Path ?? "") ?? "").Equals(".dxf", StringComparison.OrdinalIgnoreCase);

        // loaded content (not saved)
        private System.Windows.Media.Geometry lines;
        private BitmapSource image;
        private bool loaded;

        /// <summary>A message when the file could not be read (shown once in the status).</summary>
        public string LoadError { get; private set; }

        /// <summary>Reads a new underlay; a DXF comes in at true size from its units ($INSUNITS; feet when unknown).</summary>
        public static PlanUnderlay Import(string path)
        {
            var u = new PlanUnderlay { Path = path };
            if (u.IsDxf)
            {
                var d = DxfReader.ReadFile(path);
                if (d.Paths.Count == 0) throw new InvalidDataException("No lines, polylines or arcs were found in model space.");
                double f = DxfReader.FeetPerUnit(d.InsUnits);
                u.Scale = f > 0 ? f : 1;
            }
            else
            {
                var bmp = Load(path);
                u.Scale = 60.0 / Math.Max(1, bmp.PixelWidth); // 60 ft wide until calibrated
                u.OriginY = bmp.PixelHeight * u.Scale;
            }
            u.EnsureLoaded();
            if (u.LoadError != null) throw new InvalidDataException(u.LoadError);
            return u;
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(Path)) { LoadError = "The plan file is not there any more: " + Path; return; }
                if (IsDxf)
                {
                    var d = DxfReader.ReadFile(Path);
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                        foreach (var p in d.Paths.Where(p => p.Points.Count > 1))
                        {
                            ctx.BeginFigure(p.Points[0], false, p.Closed);
                            ctx.PolyLineTo(p.Points.Skip(1).ToList(), true, false);
                        }
                    g.Freeze();
                    lines = g;
                }
                else image = Load(Path);
            }
            catch (Exception ex)
            {
                LoadError = "The plan could not be read: " + ex.Message;
            }
        }

        private static BitmapSource Load(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static readonly Pen LinePen = MakePen();
        private static Pen MakePen()
        {
            var p = new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6)), 1);
            p.Freeze();
            return p;
        }

        /// <summary>The underlay's box in ft (empty when it could not be read).</summary>
        public Rect WorldBounds()
        {
            EnsureLoaded();
            if (lines != null)
            {
                var b = lines.Bounds;
                return new Rect(OriginX + b.X * Scale, OriginY + b.Y * Scale, b.Width * Scale, b.Height * Scale);
            }
            if (image != null) return new Rect(OriginX, OriginY - image.PixelHeight * Scale, image.PixelWidth * Scale, image.PixelHeight * Scale);
            return Rect.Empty;
        }

        /// <summary>Draws the plan under the model with the sketch's transform.</summary>
        public void Draw(DrawingContext ctx, SketchCanvas s)
        {
            if (!Visible) return;
            EnsureLoaded();
            if (lines != null)
            {
                // world = origin + scale·p; screen = (ox + x·z, oy − y·z)
                var m = new Matrix(Scale * s.Zoom, 0, 0, -Scale * s.Zoom, s.OriginX + OriginX * s.Zoom, s.OriginY - OriginY * s.Zoom);
                ctx.PushTransform(new MatrixTransform(m));
                var pen = new Pen(LinePen.Brush, 1 / Math.Max(1e-9, Scale * s.Zoom));
                ctx.DrawGeometry(null, pen, lines);
                ctx.Pop();
            }
            else if (image != null)
            {
                var tl = s.ToScreen(new Geometry.Point2(OriginX, OriginY));
                double w = image.PixelWidth * Scale * s.Zoom, h = image.PixelHeight * Scale * s.Zoom;
                ctx.PushOpacity(0.45);
                ctx.DrawImage(image, new Rect(tl.X, tl.Y, w, h));
                ctx.Pop();
            }
        }

        /// <summary>Scales the plan about a point so that two picked points become <paramref name="feet"/> apart.</summary>
        public bool Calibrate(Point a, Point b, double feet)
        {
            double d = (b - a).Length;
            if (!(d > 1e-9) || !(feet > 0)) return false;
            double k = feet / d;
            // scale about a: origin' = a + k·(origin − a)
            OriginX = a.X + k * (OriginX - a.X);
            OriginY = a.Y + k * (OriginY - a.Y);
            Scale *= k;
            return true;
        }

        // ------------------------------------------------------------------ saved in the level's data bag

        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(PlanUnderlay));

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static PlanUnderlay FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (PlanUnderlay)Serializer().ReadObject(ms);
            }
            catch (SerializationException) { return null; }
        }
    }
}
