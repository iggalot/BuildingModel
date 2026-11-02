using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace BuildingModel.Sketch
{
    /// <summary>One polyline of a CAD drawing, in drawing units (x right, y up).</summary>
    internal sealed class DxfPath
    {
        public string Layer { get; set; }
        public bool Closed { get; set; }
        public List<Point> Points { get; } = new List<Point>();
    }

    /// <summary>What was read from a DXF file: the model-space linework, the drawing units and the layers.</summary>
    internal sealed class DxfDrawing
    {
        public List<DxfPath> Paths { get; } = new List<DxfPath>();

        /// <summary>$INSUNITS from the header (0 = unitless or not given).</summary>
        public int InsUnits { get; set; }

        /// <summary>Entities that were not read (text, dimensions, hatches...), by type, with their counts.</summary>
        public SortedDictionary<string, int> Skipped { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The layers that hold linework, with the number of paths on each, most first.</summary>
        public List<(string layer, int count)> Layers() =>
            Paths.GroupBy(p => p.Layer).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Extents of the linework on the given layers (all when null), drawing units.</summary>
        public Rect Bounds(ICollection<string> layers = null)
        {
            var r = Rect.Empty;
            foreach (var p in Paths)
                if (layers == null || layers.Contains(p.Layer))
                    foreach (var q in p.Points) r.Union(q);
            return r;
        }
    }

    /// <summary>
    /// Reads the linework of an ASCII DXF file (AutoCAD SAVEAS / DXFOUT, any release): LINE, LWPOLYLINE, POLYLINE,
    /// ARC, CIRCLE, ELLIPSE, SPLINE (through its fit or control points) and block INSERTs (expanded, nested). Arcs and
    /// bulges become short straight segments. Model space only; text, dimensions and hatches are skipped. The units
    /// come from $INSUNITS. No outside library: DXF is Autodesk's published text format (group code / value pairs).
    /// </summary>
    internal static class DxfReader
    {
        /// <summary>Feet per drawing unit for an $INSUNITS code; NaN when unitless or unknown.</summary>
        public static double FeetPerUnit(int insUnits)
        {
            switch (insUnits)
            {
                case 1: return 1.0 / 12.0;          // inches
                case 2: return 1.0;                 // feet
                case 3: return 5280.0;              // miles
                case 4: return 1.0 / 304.8;         // millimetres
                case 5: return 1.0 / 30.48;         // centimetres
                case 6: return 1.0 / 0.3048;        // metres
                case 8: return 1e-6 / 12.0;         // microinches
                case 9: return 1e-3 / 12.0;         // mils
                case 10: return 3.0;                // yards
                case 14: return 1.0 / 3.048;        // decimetres
                default: return double.NaN;
            }
        }

        /// <summary>The units a designer can pick when the file does not say: label, $INSUNITS code.</summary>
        public static readonly (string label, int code)[] UnitChoices =
        {
            ("Inches", 1), ("Feet", 2), ("Millimetres", 4), ("Centimetres", 5), ("Metres", 6),
        };

        public static string UnitName(int insUnits)
        {
            foreach (var u in UnitChoices) if (u.code == insUnits) return u.label.ToLowerInvariant();
            switch (insUnits)
            {
                case 0: return "unitless";
                case 3: return "miles";
                case 8: return "microinches";
                case 9: return "mils";
                case 10: return "yards";
                case 14: return "decimetres";
                default: return "units code " + insUnits;
            }
        }

        public static DxfDrawing ReadFile(string path)
        {
            byte[] head = new byte[22];
            using (var fs = File.OpenRead(path)) fs.Read(head, 0, head.Length);
            string sig = Encoding.ASCII.GetString(head);
            if (sig.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal))
                throw new InvalidDataException("This is a binary DXF. Save it again as an ASCII DXF (AutoCAD: SAVEAS > DXF, Format: ASCII).");
            if (sig.StartsWith("AC10", StringComparison.Ordinal))
                throw new InvalidDataException("This is a DWG file. Save it as DXF in AutoCAD (SAVEAS or DXFOUT) and import the DXF.");
            using (var reader = new StreamReader(path, Encoding.Default, true))
                return Read(reader);
        }

        public static DxfDrawing Read(TextReader text)
        {
            var pairs = Pairs(text).ToList();
            var drawing = new DxfDrawing();
            var blocks = new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);
            List<Entity> modelSpace = new List<Entity>();

            int i = 0;
            while (i < pairs.Count)
            {
                if (pairs[i].code == 0 && pairs[i].value == "SECTION" && i + 1 < pairs.Count && pairs[i + 1].code == 2)
                {
                    string name = pairs[i + 1].value;
                    i += 2;
                    int end = i;
                    while (end < pairs.Count && !(pairs[end].code == 0 && pairs[end].value == "ENDSEC")) end++;
                    if (name == "HEADER") ReadHeader(pairs, i, end, drawing);
                    else if (name == "BLOCKS") ReadBlocks(pairs, i, end, blocks);
                    else if (name == "ENTITIES") modelSpace = Entities(pairs, i, end);
                    i = end + 1;
                }
                else i++;
            }

            foreach (var e in modelSpace)
            {
                if (e.Int(67) == 1) continue; // paper space
                Emit(e, Matrix.Identity, null, blocks, drawing, 0);
            }
            return drawing;
        }

        // ------------------------------------------------------------------ parsing

        private struct Pair
        {
            public int code;
            public string value;
        }

        private sealed class Entity
        {
            public string Type;
            public List<Pair> Data = new List<Pair>();
            public List<Entity> Vertices; // POLYLINE

            public string Str(int code, string fallback = null)
            {
                foreach (var p in Data) if (p.code == code) return p.value;
                return fallback;
            }

            public double Num(int code, double fallback = 0)
            {
                foreach (var p in Data)
                    if (p.code == code && double.TryParse(p.value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
                return fallback;
            }

            public int Int(int code, int fallback = 0) => (int)Math.Round(Num(code, fallback));

            /// <summary>Extrusion pointing down (0, 0, −1): the entity's 2D coordinates are mirrored in x.</summary>
            public bool Mirrored => Num(230, 1) < 0;
        }

        private sealed class Block
        {
            public Point Base;
            public List<Entity> Entities;
        }

        private static IEnumerable<Pair> Pairs(TextReader text)
        {
            string codeLine;
            while ((codeLine = text.ReadLine()) != null)
            {
                string value = text.ReadLine();
                if (value == null) yield break;
                if (!int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                    throw new InvalidDataException("This file is not an ASCII DXF (expected a group code, found \"" + codeLine.Trim() + "\").");
                yield return new Pair { code = code, value = value.Trim() };
            }
        }

        private static void ReadHeader(List<Pair> pairs, int from, int to, DxfDrawing drawing)
        {
            for (int i = from; i < to - 1; i++)
                if (pairs[i].code == 9 && pairs[i].value == "$INSUNITS" && pairs[i + 1].code == 70 &&
                    int.TryParse(pairs[i + 1].value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int u))
                    drawing.InsUnits = u;
        }

        private static void ReadBlocks(List<Pair> pairs, int from, int to, Dictionary<string, Block> blocks)
        {
            int i = from;
            while (i < to)
            {
                if (pairs[i].code == 0 && pairs[i].value == "BLOCK")
                {
                    var head = new Entity { Type = "BLOCK" };
                    i++;
                    while (i < to && pairs[i].code != 0) head.Data.Add(pairs[i++]);
                    int end = i;
                    while (end < to && !(pairs[end].code == 0 && pairs[end].value == "ENDBLK")) end++;
                    string name = head.Str(2) ?? head.Str(3);
                    if (name != null)
                        blocks[name] = new Block { Base = new Point(head.Num(10), head.Num(20)), Entities = Entities(pairs, i, end) };
                    i = end + 1;
                }
                else i++;
            }
        }

        /// <summary>The entities between two indexes; a POLYLINE takes its VERTEX entities up to SEQEND.</summary>
        private static List<Entity> Entities(List<Pair> pairs, int from, int to)
        {
            var list = new List<Entity>();
            Entity polyline = null;
            int i = from;
            while (i < to)
            {
                if (pairs[i].code != 0) { i++; continue; }
                var e = new Entity { Type = pairs[i].value };
                i++;
                while (i < to && pairs[i].code != 0) e.Data.Add(pairs[i++]);

                if (e.Type == "VERTEX" && polyline != null) { polyline.Vertices.Add(e); continue; }
                if (e.Type == "SEQEND") { polyline = null; continue; }
                polyline = null;
                if (e.Type == "POLYLINE") { e.Vertices = new List<Entity>(); polyline = e; }
                list.Add(e);
            }
            return list;
        }

        // ------------------------------------------------------------------ geometry

        private const int MaxDepth = 12;
        private const double ArcStepDegrees = 7.5;

        private static void Emit(Entity e, Matrix m, string insertLayer, Dictionary<string, Block> blocks, DxfDrawing d, int depth)
        {
            string layer = e.Str(8, "0");
            if (layer == "0" && insertLayer != null) layer = insertLayer; // block entities on layer 0 take the insert's layer
            Matrix local = m;
            bool ocs = e.Type == "ARC" || e.Type == "CIRCLE" || e.Type == "LWPOLYLINE" || e.Type == "POLYLINE" || e.Type == "INSERT";
            if (ocs && e.Mirrored) // LINE, ELLIPSE and SPLINE are in world coordinates already
            {
                var mirror = new Matrix(-1, 0, 0, 1, 0, 0);
                mirror.Append(m);
                local = mirror;
            }

            switch (e.Type)
            {
                case "LINE":
                    Add(d, layer, false, local, new Point(e.Num(10), e.Num(20)), new Point(e.Num(11), e.Num(21)));
                    break;

                case "LWPOLYLINE":
                {
                    var pts = new List<Point>();
                    var bulges = new List<double>();
                    foreach (var p in e.Data)
                    {
                        if (p.code == 10) { pts.Add(new Point(Parse(p.value), 0)); bulges.Add(0); }
                        else if (p.code == 20 && pts.Count > 0) pts[pts.Count - 1] = new Point(pts[pts.Count - 1].X, Parse(p.value));
                        else if (p.code == 42 && bulges.Count > 0) bulges[bulges.Count - 1] = Parse(p.value);
                    }
                    AddBulged(d, layer, (e.Int(70) & 1) != 0, local, pts, bulges);
                    break;
                }

                case "POLYLINE":
                {
                    int flags = e.Int(70);
                    if ((flags & (16 | 64)) != 0) { Skip(d, "POLYLINE (mesh)"); break; }
                    var pts = new List<Point>();
                    var bulges = new List<double>();
                    foreach (var v in e.Vertices ?? new List<Entity>())
                    {
                        if ((v.Int(70) & (16 | 128)) != 0) continue; // spline frame or polyface vertex
                        pts.Add(new Point(v.Num(10), v.Num(20)));
                        bulges.Add(v.Num(42));
                    }
                    AddBulged(d, layer, (flags & 1) != 0, local, pts, bulges);
                    break;
                }

                case "ARC":
                    AddArc(d, layer, local, new Point(e.Num(10), e.Num(20)), e.Num(40), e.Num(50), e.Num(51));
                    break;

                case "CIRCLE":
                    AddArc(d, layer, local, new Point(e.Num(10), e.Num(20)), e.Num(40), 0, 360, closed: true);
                    break;

                case "ELLIPSE":
                {
                    var c = new Point(e.Num(10), e.Num(20));
                    var major = new Vector(e.Num(11), e.Num(21));
                    var minor = new Vector(-major.Y, major.X) * e.Num(40, 1);
                    double t0 = e.Num(41, 0), t1 = e.Num(42, 2 * Math.PI);
                    if (t1 <= t0) t1 += 2 * Math.PI;
                    int n = Math.Max(8, (int)Math.Ceiling((t1 - t0) * 180 / Math.PI / ArcStepDegrees));
                    var pts = new List<Point>();
                    for (int k = 0; k <= n; k++)
                    {
                        double t = t0 + (t1 - t0) * k / n;
                        pts.Add(c + major * Math.Cos(t) + minor * Math.Sin(t));
                    }
                    Add(d, layer, false, local, pts.ToArray());
                    break;
                }

                case "SPLINE":
                {
                    var fit = Points(e, 11, 21);
                    var pts = fit.Count >= 2 ? fit : Points(e, 10, 20);
                    if (pts.Count >= 2) Add(d, layer, (e.Int(70) & 1) != 0, local, pts.ToArray());
                    break;
                }

                case "INSERT":
                {
                    string name = e.Str(2);
                    if (name == null || !blocks.TryGetValue(name, out var block)) { Skip(d, "INSERT (block not found)"); break; }
                    if (depth >= MaxDepth) { Skip(d, "INSERT (nested too deep)"); break; }
                    int cols = Math.Max(1, e.Int(70, 1)), rows = Math.Max(1, e.Int(71, 1));
                    double dx = e.Num(44), dy = e.Num(45);
                    double rot = e.Num(50) * Math.PI / 180;
                    for (int r = 0; r < rows; r++)
                        for (int col = 0; col < cols; col++)
                        {
                            var bm = new Matrix(1, 0, 0, 1, -block.Base.X, -block.Base.Y);
                            bm.Append(new Matrix(e.Num(41, 1), 0, 0, e.Num(42, 1), 0, 0));
                            bm.Append(new Matrix(1, 0, 0, 1, col * dx, r * dy));   // array spacing is in the rotated block frame
                            bm.Append(new Matrix(Math.Cos(rot), Math.Sin(rot), -Math.Sin(rot), Math.Cos(rot), 0, 0));
                            bm.Append(new Matrix(1, 0, 0, 1, e.Num(10), e.Num(20)));
                            bm.Append(local);
                            foreach (var child in block.Entities)
                                Emit(child, bm, layer, blocks, d, depth + 1);
                        }
                    break;
                }

                case "ATTDEF":
                case "ATTRIB":
                case "VIEWPORT":
                    break; // never linework

                default:
                    Skip(d, e.Type);
                    break;
            }
        }

        private static List<Point> Points(Entity e, int xCode, int yCode)
        {
            var pts = new List<Point>();
            foreach (var p in e.Data)
            {
                if (p.code == xCode) pts.Add(new Point(Parse(p.value), 0));
                else if (p.code == yCode && pts.Count > 0) pts[pts.Count - 1] = new Point(pts[pts.Count - 1].X, Parse(p.value));
            }
            return pts;
        }

        private static double Parse(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static void Skip(DxfDrawing d, string type)
        {
            d.Skipped.TryGetValue(type, out int n);
            d.Skipped[type] = n + 1;
        }

        private static void Add(DxfDrawing d, string layer, bool closed, Matrix m, params Point[] pts)
        {
            if (pts.Length < 2) return;
            var path = new DxfPath { Layer = layer, Closed = closed };
            foreach (var p in pts) path.Points.Add(m.Transform(p));
            d.Paths.Add(path);
        }

        /// <summary>A polyline whose segments may be arcs: bulge = tan(included angle / 4), positive counter-clockwise.</summary>
        private static void AddBulged(DxfDrawing d, string layer, bool closed, Matrix m, List<Point> pts, List<double> bulges)
        {
            if (pts.Count < 2) return;
            var outPts = new List<Point> { pts[0] };
            int segs = closed ? pts.Count : pts.Count - 1;
            for (int k = 0; k < segs; k++)
            {
                Point a = pts[k], b = pts[(k + 1) % pts.Count];
                double bulge = bulges[k];
                if (Math.Abs(bulge) > 1e-9 && (b - a).Length > 1e-12)
                    foreach (var p in BulgeArc(a, b, bulge)) outPts.Add(p);
                outPts.Add(b);
            }
            if (closed && outPts.Count > 1 && (outPts[outPts.Count - 1] - outPts[0]).Length < 1e-12) outPts.RemoveAt(outPts.Count - 1);
            Add(d, layer, closed, m, outPts.ToArray());
        }

        /// <summary>The points strictly between a and b on the arc of the given bulge.</summary>
        public static IEnumerable<Point> BulgeArc(Point a, Point b, double bulge)
        {
            double theta = 4 * Math.Atan(bulge);                 // included angle, signed (CCW positive)
            Vector chord = b - a;
            double c = chord.Length;
            double r = c / (2 * Math.Sin(Math.Abs(theta) / 2));
            var mid = a + chord / 2;
            var normal = new Vector(-chord.Y, chord.X) / c;       // left of a→b
            double h = r * Math.Cos(Math.Abs(theta) / 2);         // centre offset from the chord midpoint (negative past a half circle)
            var centre = mid + normal * (h * Math.Sign(bulge));   // left of a→b for counter-clockwise
            double start = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) * 180 / Math.PI / ArcStepDegrees));
            for (int k = 1; k < n; k++)
            {
                double t = start + theta * k / n;
                yield return new Point(centre.X + r * Math.Cos(t), centre.Y + r * Math.Sin(t));
            }
        }

        private static void AddArc(DxfDrawing d, string layer, Matrix m, Point c, double r, double startDeg, double endDeg, bool closed = false)
        {
            if (!(r > 0)) return;
            double sweep = endDeg - startDeg;
            if (!closed) { while (sweep <= 0) sweep += 360; }
            int n = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / ArcStepDegrees));
            var pts = new List<Point>();
            for (int k = 0; k <= (closed ? n - 1 : n); k++)
            {
                double t = (startDeg + sweep * k / n) * Math.PI / 180;
                pts.Add(new Point(c.X + r * Math.Cos(t), c.Y + r * Math.Sin(t)));
            }
            Add(d, layer, closed, m, pts.ToArray());
        }
    }
}
