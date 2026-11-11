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
    public sealed class DxfPath
    {
        public string Layer { get; set; }
        public bool Closed { get; set; }
        public List<Point> Points { get; } = new List<Point>();
    }

    /// <summary>
    /// A line of text of a CAD drawing (TEXT, MTEXT), in drawing units: where it is, its height (the capitals'), its angle
    /// (degrees, counter-clockwise from the x axis) and how it hangs off its point.
    /// </summary>
    public sealed class DxfText
    {
        public string Layer { get; set; }
        public string Text { get; set; }
        public Point At { get; set; }
        public double Height { get; set; }
        public double Angle { get; set; }
        /// <summary>0 left, 1 centre, 2 right of <see cref="At"/>.</summary>
        public int Across { get; set; }
        /// <summary>0 baseline, 1 bottom, 2 middle, 3 top at <see cref="At"/>.</summary>
        public int Up { get; set; }
    }

    /// <summary>What was read from a DXF file: the model-space linework and text, the drawing units and the layers.</summary>
    public sealed class DxfDrawing
    {
        public List<DxfPath> Paths { get; } = new List<DxfPath>();

        /// <summary>The text (room names, labels, notes): drawn with the plan, not snapped to (2026-11-11).</summary>
        public List<DxfText> Texts { get; } = new List<DxfText>();

        /// <summary>$INSUNITS from the header (0 = unitless or not given).</summary>
        public int InsUnits { get; set; }

        /// <summary>Entities that were not read (dimensions, hatches...), by type, with their counts.</summary>
        public SortedDictionary<string, int> Skipped { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The layers that hold linework or text, with the number of paths and texts on each, most first.</summary>
        public List<(string layer, int count)> Layers() =>
            Paths.Select(p => p.Layer).Concat(Texts.Select(t => t.Layer)).GroupBy(l => l).Select(g => (g.Key, g.Count()))
                 .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();

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
    /// bulges become short straight segments. Model space only; TEXT and MTEXT are read (drawn by the plan, 2026-11-11), dimensions and hatches are skipped. The units
    /// come from $INSUNITS. No outside library: DXF is Autodesk's published text format (group code / value pairs).
    /// </summary>
    public static class DxfReader
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

                case "TEXT":
                {
                    // the alignment point when justified (72 or 73 not 0), else the insertion point
                    int h = e.Int(72), v = e.Int(73);
                    bool justified = h != 0 || v != 0;
                    var at = justified ? new Point(e.Num(11), e.Num(21)) : new Point(e.Num(10), e.Num(20));
                    if (h == 4) { h = 1; v = 2; } // "middle"
                    AddText(d, layer, local, e.Str(1), at, e.Num(40, 1), e.Num(50), h > 2 ? 1 : h, v);
                    break;
                }

                case "MTEXT":
                {
                    // the text in pieces (3, then 1), its attachment point 1 to 9 (top left ... bottom right), its angle (50, or the x direction 11/21)
                    string s = string.Concat(e.Data.Where(p => p.code == 3).Select(p => p.value)) + (e.Str(1) ?? "");
                    int attach = Math.Max(1, Math.Min(9, e.Int(71, 1)));
                    double angle = e.Data.Any(p => p.code == 11) ? Math.Atan2(e.Num(21), e.Num(11)) * 180 / Math.PI : e.Num(50);
                    AddText(d, layer, local, PlainMText(s), new Point(e.Num(10), e.Num(20)), e.Num(40, 1), angle, (attach - 1) % 3, 3 - (attach - 1) / 3);
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

        /// <summary>A text placed through the block transforms: its point moved, its height scaled and its angle turned with them.</summary>
        private static void AddText(DxfDrawing d, string layer, Matrix m, string text, Point at, double height, double angleDeg, int across, int up)
        {
            if (string.IsNullOrWhiteSpace(text) || !(height > 0)) return;
            double a = angleDeg * Math.PI / 180;
            var dir = m.Transform(new Vector(Math.Cos(a), Math.Sin(a)));
            var upv = m.Transform(new Vector(-Math.Sin(a), Math.Cos(a)));
            d.Texts.Add(new DxfText
            {
                Layer = layer, Text = text.Trim(), At = m.Transform(at), Height = height * upv.Length,
                Angle = Math.Atan2(dir.Y, dir.X) * 180 / Math.PI, Across = across, Up = up,
            });
        }

        /// <summary>
        /// MTEXT's words without its formatting: \P a new line, \~ a space, {\f...;} fonts, \H \C \A \W \Q \T and the like
        /// dropped, \S stacked fractions as a/b, %%c %%d %%p as ⌀ ° ±, the braces gone.
        /// </summary>
        public static string PlainMText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\\[Pp]", "\n");
            s = s.Replace("\\~", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\\S([^;^#/]*)[\^#/]([^;]*);", "$1/$2");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\\[fFHhCcAaWwQqTtp][^;]*;", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\\[LlOoKk]", "");
            s = s.Replace("\\\\", "\\").Replace("\\{", "\u0001").Replace("\\}", "\u0002").Replace("{", "").Replace("}", "").Replace("\u0001", "{").Replace("\u0002", "}");
            s = s.Replace("%%c", "⌀").Replace("%%C", "⌀").Replace("%%d", "°").Replace("%%D", "°").Replace("%%p", "±").Replace("%%P", "±");
            return s;
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
