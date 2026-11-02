using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The look of the plan: quiet grid, the model in a few strong colours, the selection in cyan, issues as markers.
    /// Colour rule: structural elements are never drawn in red or green tones (red/green reads as fail/pass and is the
    /// pair colour-blind users confuse). Vermilion and amber mean an error and a warning only, and every status also has a
    /// shape (circle, triangle, ✓, ✗, a dashed line), so it reads without colour.
    /// </summary>
    public partial class SketchCanvas
    {
        public static class Palette
        {
            public static readonly Color Paper = Color.FromRgb(0xFF, 0xFF, 0xFF);
            public static readonly Color GridMinor = Color.FromRgb(0xF1, 0xF2, 0xF4);
            public static readonly Color GridMajor = Color.FromRgb(0xDF, 0xE2, 0xE7);
            public static readonly Color Ink = Color.FromRgb(0x24, 0x29, 0x33);
            public static readonly Color Muted = Color.FromRgb(0x6B, 0x72, 0x80);
            public static readonly Color DiaphragmFill = Color.FromArgb(0x55, 0xD9, 0xC2, 0x8C);   // sand
            public static readonly Color DiaphragmEdge = Color.FromRgb(0x7A, 0x63, 0x3E);          // brown-gray
            public static readonly Color ShearWall = Color.FromRgb(0x1D, 0x4E, 0xD8);
            public static readonly Color Wall = Color.FromRgb(0x8A, 0x8F, 0x98);
            public static readonly Color Below = Color.FromRgb(0xC9, 0xCD, 0xD3);
            public static readonly Color Above = Color.FromRgb(0x93, 0xA8, 0xD8);
            public static readonly Color Select = Color.FromRgb(0x08, 0x91, 0xB2);                 // cyan
            public static readonly Color Preview = Color.FromRgb(0x2A, 0x78, 0xD6);
            public static readonly Color Measure = Color.FromRgb(0x6D, 0x28, 0xD9);                // violet
            public static readonly Color Error = Color.FromRgb(0xD5, 0x5E, 0x00);                  // vermilion (Okabe-Ito)
            public static readonly Color Warning = Color.FromRgb(0xE6, 0x9F, 0x00);                // amber (Okabe-Ito)
            public static readonly Color NoteFill = Color.FromRgb(0xFF, 0xF7, 0xC2);
            public static readonly Color NoteEdge = Color.FromRgb(0xB4, 0x8A, 0x00);
            public static readonly Color Cut = Color.FromRgb(0x6D, 0x28, 0xD9);                    // violet, the area a cut takes away
        }

        private static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen P(Color c, double t, params double[] dash)
        {
            var p = new Pen(B(c), t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dash.Length > 0) p.DashStyle = new DashStyle(dash, 0);
            p.Freeze();
            return p;
        }

        private static readonly Brush PaperBrush = B(Palette.Paper);
        private static readonly Pen GridMinorPen = P(Palette.GridMinor, 1);
        private static readonly Pen GridMajorPen = P(Palette.GridMajor, 1);
        private static readonly Brush DiaphragmBrush = B(Palette.DiaphragmFill);
        private static readonly Pen DiaphragmPen = P(Palette.DiaphragmEdge, 2);
        private static readonly Pen PiecePen = P(Color.FromArgb(0x70, 0x7A, 0x63, 0x3E), 1, 3, 3);
        private static readonly Pen OpeningPen = P(Palette.DiaphragmEdge, 1.5, 5, 3);
        private static readonly Brush OpeningBrush = MakeHatch();
        private static readonly Pen ShearPen = P(Palette.ShearWall, 4);
        private static readonly Pen WallPen = P(Palette.Wall, 2.5);
        private static readonly Pen BelowPen = P(Palette.Below, 3);
        private static readonly Pen AbovePen = P(Palette.Above, 1.2, 4, 3);
        private static readonly Pen HaloPen = P(Color.FromArgb(0x80, 0x08, 0x91, 0xB2), 12);
        private static readonly Pen SelectPen = P(Palette.Select, 2.5);
        private static readonly Pen PreviewPen = P(Palette.Preview, 2, 4, 3);
        private static readonly Pen RubberPen = P(Palette.Muted, 1, 3, 3);
        private static readonly Pen MeasurePen = P(Palette.Measure, 1.5);
        private static readonly Pen CrossPen = P(Color.FromArgb(0x80, 0x52, 0x51, 0x4E), 1);
        private static readonly Pen SnapPen = P(Palette.Select, 2);
        private static readonly Brush CutBrush = B(Color.FromArgb(0x30, 0x6D, 0x28, 0xD9));
        private static readonly Pen CutPen = P(Palette.Cut, 2, 4, 3);
        private static readonly Brush NoteBrush = B(Palette.NoteFill);
        private static readonly Pen NotePen = P(Palette.NoteEdge, 1);
        private static readonly Brush InkBrush = B(Palette.Ink);
        private static readonly Brush MutedBrush = B(Palette.Muted);
        private static readonly Brush LabelBack = B(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF));
        private static readonly Brush WindowBrush = B(Color.FromArgb(0x20, 0x2A, 0x78, 0xD6));
        private static readonly Pen WindowPen = P(Palette.Preview, 1, 4, 2);
        private static readonly Brush GripBrush = B(Colors.White);

        private static Brush MakeHatch()
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            g.Children.Add(new GeometryDrawing(null, P(Color.FromArgb(0x90, 0x7A, 0x63, 0x3E), 1), new LineGeometry(new Point(0, 8), new Point(8, 0))));
            var brush = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
            brush.Freeze();
            return brush;
        }

        protected override void OnRender(DrawingContext ctx)
        {
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            ctx.DrawRectangle(PaperBrush, null, bounds);
            if (Building == null || ActiveLevel == null) return;
            if (ShowGrid) DrawGrid(ctx);
            if (ShowUnderlay) UnderlayOf(ActiveLevel)?.Draw(ctx, this);
            DrawUnder?.Invoke(this, new SketchDrawEventArgs(ctx, this));

            var b = Building;
            var level = ActiveLevel;
            var below = b.Below(level);
            if (ShowLevelBelow && below != null)
                foreach (var w in b.WallsUnder(below)) Line(ctx, BelowPen, w.Start, w.End);

            // diaphragms: the pieces filled, the net outline strong
            foreach (var r in b.DiaphragmsOn(level).SelectMany(d => d.Pieces)) ctx.DrawRectangle(DiaphragmBrush, null, ScreenRect(r));
            var cutting = b.OpeningsCutting(level);
            foreach (var r in cutting.SelectMany(o => o.Pieces)) ctx.DrawRectangle(OpeningBrush, OpeningPen, ScreenRect(r));
            foreach (var r in b.DiaphragmsOn(level).SelectMany(d => d.Pieces)) ctx.DrawRectangle(null, PiecePen, ScreenRect(r));
            foreach (var s in b.NetDiaphragm(level).Outline()) Line(ctx, DiaphragmPen, s.A, s.B);

            foreach (var w in b.WallsOver(level)) Line(ctx, AbovePen, w.Start, w.End);
            var walls = b.WallsUnder(level).ToList();
            foreach (var w in walls) Line(ctx, WallStyle?.Invoke(w) ?? (w.IsShearWall ? ShearPen : WallPen), w.Start, w.End);

            // selection under the labels
            foreach (var id in selection) DrawSelected(ctx, b.Find(id));

            if (ShowLabels)
            {
                foreach (var w in walls) WallLabel(ctx, w);
                foreach (var d in b.DiaphragmsOn(level)) { var r = d.Pieces.FirstOrDefault(); if (!r.IsEmpty) Label(ctx, d.Id, ToScreen(new Point2(r.X0, r.Y1)) + new Vector(4, 3), MutedBrush, 10, false); }
                foreach (var o in cutting) { var r = o.Pieces.FirstOrDefault(); if (!r.IsEmpty) Label(ctx, $"{o.Id} open {b.Level(o.BottomLevelId)?.Name}–{b.Level(o.TopLevelId)?.Name}", ToScreen(new Point2(r.X0, r.Y1)) + new Vector(4, 3), MutedBrush, 10, true); }
            }
            foreach (var n in b.NotesOn(level)) DrawNote(ctx, n, selection.Contains(n.Id));

            DrawOver?.Invoke(this, new SketchDrawEventArgs(ctx, this));
            foreach (var g in Grips()) ctx.DrawRectangle(GripBrush, SelectPen, new Rect(ToScreen(g.At) - new Vector(4, 4), new Size(8, 8)));
            DrawIssues(ctx);
            DrawPreview(ctx);
            DrawLevelTag(ctx);
            DrawScaleBar(ctx);
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>The host's pen for a wall of the level shown (e.g. coloured by its design check), or null for the usual one.</summary>
        public Func<Wall, Pen> WallStyle { get; set; }

        public void Line(DrawingContext ctx, Pen pen, Point2 a, Point2 b) => ctx.DrawLine(pen, ToScreen(a), ToScreen(b));

        public Rect ScreenRect(Rect2 r) => new Rect(ToScreen(new Point2(r.X0, r.Y1)), ToScreen(new Point2(r.X1, r.Y0)));

        private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        /// <summary>A label at a screen point (top-left), on a light back when asked.</summary>
        public void Label(DrawingContext ctx, string text, Point at, Brush brush, double size = 11, bool back = true, bool bold = false)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                       new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                                       size, brush, Dpi);
            if (back) ctx.DrawRoundedRectangle(LabelBack, null, new Rect(at.X - 2, at.Y - 1, ft.Width + 4, ft.Height + 2), 2, 2);
            ctx.DrawText(ft, at);
        }

        private void WallLabel(DrawingContext ctx, Wall w)
        {
            var m = ToScreen(w.Middle);
            bool horizontal = w.Direction != WallDirection.SouthNorth;
            var at = horizontal ? m + new Vector(-14, 5) : m + new Vector(6, -7);
            Label(ctx, $"{w.Id}  {w.Length:0.##}'", at, w.IsShearWall ? B(Palette.ShearWall) : MutedBrush, 10);
        }

        private void DrawSelected(DrawingContext ctx, ModelObject o)
        {
            switch (o)
            {
                case Wall w:
                    Line(ctx, HaloPen, w.Start, w.End);
                    break;
                case Note n:
                    var s = ToScreen(n.At);
                    ctx.DrawEllipse(null, HaloPen, s, 8, 8);
                    break;
                default:
                    foreach (var r in LevelView.PiecesOf(o))
                    {
                        ctx.DrawRectangle(null, SelectPen, ScreenRect(r));
                        Label(ctx, $"{r.Width:0.##} × {r.Height:0.##} ft", ToScreen(new Point2((r.X0 + r.X1) / 2, r.Y0)) + new Vector(-30, 4), B(Palette.Select), 10, true, true);
                    }
                    break;
            }
        }

        private void DrawNote(DrawingContext ctx, Note n, bool selected)
        {
            var s = ToScreen(n.At);
            string text = n.Text.Length > 48 ? n.Text.Substring(0, 47) + "…" : n.Text;
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, InkBrush, Dpi);
            var box = new Rect(s.X + 8, s.Y - ft.Height - 10, ft.Width + 10, ft.Height + 6);
            ctx.DrawLine(NotePen, s, new Point(box.X, box.Bottom));
            ctx.DrawRoundedRectangle(NoteBrush, selected ? SelectPen : NotePen, box, 3, 3);
            ctx.DrawText(ft, new Point(box.X + 5, box.Y + 3));
            ctx.DrawEllipse(B(Palette.NoteEdge), null, s, 3, 3);
        }

        private void DrawIssues(DrawingContext ctx)
        {
            foreach (var i in IssuesHere())
            {
                var s = ToScreen(i.At.Value);
                bool err = i.Severity == Severity.Error;
                var fill = B(err ? Palette.Error : Palette.Warning);
                if (err) ctx.DrawEllipse(fill, P(Colors.White, 1.5), s, 8, 8);
                else
                {
                    var tri = new StreamGeometry();
                    using (var g = tri.Open())
                    {
                        g.BeginFigure(new Point(s.X, s.Y - 9), true, true);
                        g.LineTo(new Point(s.X + 9, s.Y + 7), true, false);
                        g.LineTo(new Point(s.X - 9, s.Y + 7), true, false);
                    }
                    tri.Freeze();
                    ctx.DrawGeometry(fill, P(Colors.White, 1.5), tri);
                }
                var ft = new FormattedText("!", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                           new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 11, Brushes.White, Dpi);
                ctx.DrawText(ft, new Point(s.X - ft.Width / 2, s.Y - ft.Height / 2 + (err ? 0 : 1)));
            }
        }

        private void DrawGrid(DrawingContext ctx)
        {
            double step = GridSpacing > 0 ? GridSpacing : 1;
            while (step * Zoom < 8) step *= step * Zoom < 3 ? 10 : 5;
            double major = step * 10;
            var a = ToWorld(new Point(0, ActualHeight));
            var c = ToWorld(new Point(ActualWidth, 0));
            for (double x = Math.Floor(a.X / step) * step; x <= c.X; x += step)
            {
                bool big = Math.Abs(Math.IEEERemainder(x, major)) < step / 2;
                double sx = Math.Round(OriginX + x * Zoom) + 0.5;
                ctx.DrawLine(big ? GridMajorPen : GridMinorPen, new Point(sx, 0), new Point(sx, ActualHeight));
            }
            for (double y = Math.Floor(a.Y / step) * step; y <= c.Y; y += step)
            {
                bool big = Math.Abs(Math.IEEERemainder(y, major)) < step / 2;
                double sy = Math.Round(OriginY - y * Zoom) + 0.5;
                ctx.DrawLine(big ? GridMajorPen : GridMinorPen, new Point(0, sy), new Point(ActualWidth, sy));
            }
        }

        private void DrawPreview(DrawingContext ctx)
        {
            if (lastMeasure != null) DrawMeasure(ctx, lastMeasure.Value.a, lastMeasure.Value.b);
            if (windowFrom != null && Mouse.Captured == this)
            {
                var now = Mouse.GetPosition(this);
                ctx.DrawRectangle(WindowBrush, WindowPen, new Rect(windowFrom.Value, now));
            }
            if (grip != null)
            {
                var g = grip.Value;
                if (g.IsWall && Building.Find(g.Id) is Wall w)
                {
                    var fixedEnd = g.Start ? w.End : w.Start;
                    var p = Snapper.Ortho(cursor, fixedEnd);
                    Line(ctx, PreviewPen, fixedEnd, p);
                    Label(ctx, $"{Snapper.Dist(fixedEnd, p):0.##} ft", ToScreen(p) + new Vector(10, -18), B(Palette.Preview));
                }
                else if (!g.IsWall)
                {
                    var o = Building.Find(g.Id);
                    var r = LevelView.PiecesOf(o).ElementAtOrDefault(g.Piece);
                    double ox = Math.Abs(g.At.X - r.X0) < Math.Abs(g.At.X - r.X1) ? r.X1 : r.X0;
                    double oy = Math.Abs(g.At.Y - r.Y0) < Math.Abs(g.At.Y - r.Y1) ? r.Y1 : r.Y0;
                    ctx.DrawRectangle(null, PreviewPen, ScreenRect(new Rect2(ox, oy, cursor.X, cursor.Y)));
                }
            }
            if (first != null && mouseInside)
            {
                var a = first.Value;
                switch (Tool)
                {
                    case SketchTool.Wall:
                        var e = Snapper.Ortho(cursor, a);
                        Line(ctx, P(Palette.ShearWall, 4, 2, 1.5), a, e);
                        Label(ctx, $"{Snapper.Dist(a, e):0.##} ft", ToScreen(e) + new Vector(10, -18), B(Palette.Preview));
                        break;
                    case SketchTool.Cut:
                        var cr = new Rect2(a.X, a.Y, cursor.X, cursor.Y);
                        ctx.DrawRectangle(CutBrush, CutPen, ScreenRect(cr));
                        Label(ctx, $"cut {cr.Width:0.##} × {cr.Height:0.##} ft", ToScreen(cursor) + new Vector(10, -18), B(Palette.Cut));
                        break;
                    case SketchTool.Diaphragm:
                    case SketchTool.Opening:
                        var r = new Rect2(a.X, a.Y, cursor.X, cursor.Y);
                        ctx.DrawRectangle(Tool == SketchTool.Opening ? OpeningBrush : DiaphragmBrush, PreviewPen, ScreenRect(r));
                        Label(ctx, $"{r.Width:0.##} × {r.Height:0.##} ft", ToScreen(cursor) + new Vector(10, -18), B(Palette.Preview));
                        break;
                    case SketchTool.Measure:
                    case SketchTool.Calibrate:
                        DrawMeasure(ctx, a, Ortho ? Snapper.Ortho(cursor, a) : cursor);
                        break;
                    case SketchTool.Move:
                    case SketchTool.Copy:
                        var to = Ortho ? Snapper.Ortho(cursor, a) : cursor;
                        Line(ctx, RubberPen, a, to);
                        Ghost(ctx, to.X - a.X, to.Y - a.Y, null);
                        break;
                    case SketchTool.Mirror:
                        var m = Snapper.Ortho(cursor, a);
                        Line(ctx, MeasurePen, a, m);
                        bool vertical = Math.Abs(m.X - a.X) < Math.Abs(m.Y - a.Y);
                        Ghost(ctx, 0, 0, p => vertical ? new Point2(2 * a.X - p.X, p.Y) : new Point2(p.X, 2 * a.Y - p.Y));
                        break;
                }
            }
            // the cursor and the snap glyph
            if (mouseInside && (Tool != SketchTool.Select || HostWantsSnaps?.Invoke() == true))
            {
                var c = ToScreen(cursor);
                ctx.DrawLine(CrossPen, new Point(c.X - 12, c.Y), new Point(c.X + 12, c.Y));
                ctx.DrawLine(CrossPen, new Point(c.X, c.Y - 12), new Point(c.X, c.Y + 12));
                if (snap.Found && snap.Kind != SnapModes.Grid)
                {
                    switch (snap.Kind)
                    {
                        case SnapModes.Endpoint: ctx.DrawRectangle(null, SnapPen, new Rect(c.X - 5, c.Y - 5, 10, 10)); break;
                        case SnapModes.Midpoint:
                            var t = new StreamGeometry();
                            using (var g = t.Open()) { g.BeginFigure(new Point(c.X, c.Y - 6), false, true); g.LineTo(new Point(c.X + 6, c.Y + 5), true, false); g.LineTo(new Point(c.X - 6, c.Y + 5), true, false); }
                            ctx.DrawGeometry(null, SnapPen, t);
                            break;
                        case SnapModes.Intersection:
                            ctx.DrawLine(SnapPen, new Point(c.X - 5, c.Y - 5), new Point(c.X + 5, c.Y + 5));
                            ctx.DrawLine(SnapPen, new Point(c.X - 5, c.Y + 5), new Point(c.X + 5, c.Y - 5));
                            break;
                        default: ctx.DrawEllipse(null, SnapPen, c, 5, 5); break;
                    }
                    Label(ctx, SnapResult.Describe(snap.Kind), c + new Vector(9, 7), B(Palette.Select), 9);
                }
            }
        }

        /// <summary>The selected objects moved by (dx, dy), or mapped (mirror), as outlines.</summary>
        private void Ghost(DrawingContext ctx, double dx, double dy, Func<Point2, Point2> map)
        {
            Point2 M(Point2 p) => map != null ? map(p) : new Point2(p.X + dx, p.Y + dy);
            foreach (var id in selection)
            {
                var o = Building.Find(id);
                if (o is Wall w) Line(ctx, PreviewPen, M(w.Start), M(w.End));
                else if (o is Note n) ctx.DrawEllipse(null, PreviewPen, ToScreen(M(n.At)), 5, 5);
                else foreach (var r in LevelView.PiecesOf(o))
                    {
                        var p0 = M(new Point2(r.X0, r.Y0));
                        var p1 = M(new Point2(r.X1, r.Y1));
                        ctx.DrawRectangle(null, PreviewPen, ScreenRect(new Rect2(p0.X, p0.Y, p1.X, p1.Y)));
                    }
            }
        }

        private void DrawMeasure(DrawingContext ctx, Point2 a, Point2 b)
        {
            Point sa = ToScreen(a), sb = ToScreen(b);
            ctx.DrawLine(MeasurePen, sa, sb);
            ctx.DrawEllipse(null, MeasurePen, sa, 3, 3);
            ctx.DrawEllipse(null, MeasurePen, sb, 3, 3);
            Label(ctx, $"{Snapper.Dist(a, b):0.00} ft", new Point((sa.X + sb.X) / 2 + 6, (sa.Y + sb.Y) / 2 - 18), B(Palette.Measure));
        }

        /// <summary>The words of the level tag (the host's own naming), or null for the default.</summary>
        public Func<Level, string> LevelCaption { get; set; }

        /// <summary>The level being drawn, top-left: it must always be clear which level is being edited.</summary>
        private void DrawLevelTag(DrawingContext ctx)
        {
            var l = ActiveLevel;
            double h = Building.StoryHeight(l);
            string text = LevelCaption?.Invoke(l) ?? (l.Elevation == 0 ? $"{l.Name} · 0 ft" : $"{l.Name} · {l.Elevation:0.##} ft · story under it {h:0.##} ft");
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                       new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, Brushes.White, Dpi);
            var box = new Rect(8, 8, ft.Width + 16, ft.Height + 8);
            ctx.DrawRoundedRectangle(B(Color.FromRgb(0x33, 0x41, 0x55)), null, box, 4, 4);
            ctx.DrawText(ft, new Point(16, 12));
        }

        private void DrawScaleBar(DrawingContext ctx)
        {
            double[] nice = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000 };
            double len = nice.FirstOrDefault(n => n * Zoom >= 60);
            if (len == 0) return;
            double px = len * Zoom, x = 12, y = ActualHeight - 14;
            var pen = P(Palette.Muted, 1.5);
            ctx.DrawLine(pen, new Point(x, y), new Point(x + px, y));
            ctx.DrawLine(pen, new Point(x, y - 4), new Point(x, y + 1));
            ctx.DrawLine(pen, new Point(x + px, y - 4), new Point(x + px, y + 1));
            Label(ctx, $"{len:0} ft", new Point(x + px + 6, y - 9), MutedBrush, 10, false);
        }
    }
}
