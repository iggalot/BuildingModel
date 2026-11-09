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
            /// <summary>The shear walls' horizontal pairs (their arrows and labels).</summary>
            public static readonly Color Shear = Color.FromRgb(0x0F, 0x76, 0x6E);
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

        /// <summary>A cut wall's band (square ends, so the end of the double line is closed).</summary>
        private static Pen CutPenOf(Brush brush, bool freeze = true)
        {
            var p = new Pen(brush, 7) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
            if (freeze) p.Freeze(); // never with a host's brush: freezing the pen would freeze the host's brush too
            return p;
        }

        private static Pen Flat(Color c, double t)
        {
            var p = new Pen(B(c), t) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
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
        private static readonly Color BeamColor = Color.FromRgb(0x8B, 0x4A, 0x14);
        private static readonly Brush BeamBrush = B(BeamColor);
        private static readonly Pen BeamPen = P(BeamColor, 3.5, 4, 1.5, 0.1, 1.5);
        private static readonly Pen ColumnEdge = P(Colors.White, 1.2);
        private static readonly Pen ColumnBasePen = P(BeamColor, 1.8);
        private static readonly Pen WallPen = P(Palette.Wall, 2.5);
        private static readonly Pen BelowPen = P(Palette.Below, 3);
        // a wall cut at its base on the foundation plan: a dark band (closed, square ends) with a white core
        private static readonly Pen CutWallPen = CutPenOf(B(Palette.Ink));
        private static readonly Pen CutWallCore = Flat(Colors.White, 3);
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
        private static readonly Brush SelectFill = B(Palette.Select);
        private static readonly Pen GripEdge = P(Colors.White, 1.5);

        private static Brush MakeHatch()
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            g.Children.Add(new GeometryDrawing(null, P(Color.FromArgb(0x90, 0x7A, 0x63, 0x3E), 1), new LineGeometry(new Point(0, 8), new Point(8, 0))));
            var brush = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
            brush.Freeze();
            return brush;
        }

        /// <summary>The wall, beam and column labels drawn this frame (screen bounds): a host keeps its own labels off them.</summary>
        public IReadOnlyList<Rect> MemberLabelBoxes => memberLabels;
        private readonly List<Rect> memberLabels = new List<Rect>();

        protected override void OnRender(DrawingContext ctx)
        {
            memberLabels.Clear();
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            ctx.DrawRectangle(PaperBrush, null, bounds);
            if (Building == null || ActiveLevel == null) return;
            if (ShowGrid) DrawGrid(ctx);
            if (ShowUnderlay) PlanOf(ActiveLevel)?.Draw(ctx, this);
            // the north arrow, the building model's one (as in every view), under the drawing
            if (ShowNorth) NorthArrowDrawing.Draw(ctx, NorthArrow.For(Building), ToScreen, VisualTreeHelper.GetDpi(this).PixelsPerDip);
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

            bool foundation = b.IsFoundationPlan(level);
            var walls = b.WallsShown(level).ToList();
            if (foundation)
            {
                // the foundation plan: the walls standing on it, cut at their base (a double line, as on a floor plan); they are
                // drawn and kept on the level they carry, and only shown, selected and edited here
                foreach (var w in walls)
                {
                    var style = WallStyle?.Invoke(w);
                    Line(ctx, style == null ? CutWallPen : CutPenOf(style.Brush, freeze: false), w.Start, w.End);
                    Line(ctx, CutWallCore, w.Start, w.End);
                }
            }
            else
            {
                foreach (var w in b.WallsOver(level)) Line(ctx, AbovePen, w.Start, w.End);
                foreach (var w in walls) Line(ctx, WallStyle?.Invoke(w) ?? WallPen, w.Start, w.End);
            }
            if (ShowJoints) DrawJoints(ctx, b, walls);
            if (ShowShearParts) DrawShearParts(ctx, b, walls);

            // beams in this level's framing (brown, dash-dot as on framing plans) and the columns under it (filled squares)
            foreach (var bm in b.BeamsOn(level))
            {
                Line(ctx, BeamPen, bm.Start, bm.End);
                BeamLabel(ctx, bm);
            }
            foreach (var c in b.ColumnsUnder(level))
            {
                var cs = ToScreen(c.At);
                double half = Math.Max(4, 0.25 * Zoom);
                ctx.DrawRectangle(BeamBrush, ColumnEdge, new Rect(cs.X - half, cs.Y - half, 2 * half, 2 * half));
                ColumnLabel(ctx, cs, half, c.Id, above: true);
            }
            // the columns standing on this level (on the ground: run down to the foundation): an outlined square, "C1 base"
            foreach (var c in b.ColumnsStandingOn(level))
            {
                var cs = ToScreen(c.At);
                double half = Math.Max(4, 0.25 * Zoom);
                ctx.DrawRectangle(Brushes.White, ColumnBasePen, new Rect(cs.X - half, cs.Y - half, 2 * half, 2 * half));
                ctx.DrawLine(ColumnBasePen, new Point(cs.X - half, cs.Y - half), new Point(cs.X + half, cs.Y + half));
                ctx.DrawLine(ColumnBasePen, new Point(cs.X - half, cs.Y + half), new Point(cs.X + half, cs.Y - half));
                ColumnLabel(ctx, cs, half, $"{c.Id} base", above: false);
            }
            // selection under the labels
            foreach (var id in selection) DrawSelected(ctx, b.Find(id));

            if (ShowLabels)
            {
                if (ShowWallLabels) foreach (var w in walls) WallLabel(ctx, w);
                if (!ShowSpans) foreach (var d in b.DiaphragmsOn(level)) { var r = d.Pieces.FirstOrDefault(); if (!r.IsEmpty) Label(ctx, d.Id + SpanTag(b, d), ToScreen(new Point2(r.X0, r.Y1)) + new Vector(4, 3), MutedBrush, 10, false); }
                foreach (var o in cutting) { var r = o.Pieces.FirstOrDefault(); if (!r.IsEmpty) Label(ctx, $"{o.Id} open {b.Level(o.BottomLevelId)?.Name}–{b.Level(o.TopLevelId)?.Name}", ToScreen(new Point2(r.X0, r.Y1)) + new Vector(4, 3), MutedBrush, 10, true); }
            }
            if (ShowSpans) foreach (var d in b.DiaphragmsOn(level)) SpanMarker(ctx, b, d, ShowLabels);
            // the hold-downs after the members' labels, so theirs keep off them (the shared rule, as in every app)
            if (ShowConnections) { DrawShearPairs(ctx, b, level); DrawConnections(ctx, b, level, foundation); }
            foreach (var n in b.NotesOn(level)) DrawNote(ctx, n, selection.Contains(n.Id));

            DrawOver?.Invoke(this, new SketchDrawEventArgs(ctx, this));
            // grips: hollow squares stretch (ends, corners); the filled diamond in a wall's middle moves the selection
            foreach (var g in Grips())
            {
                var s = ToScreen(g.At);
                if (g.IsSide)
                {
                    // a short bar along the side: drag it across to move that side
                    bool ew = g.Side == Edits.RectSide.West || g.Side == Edits.RectSide.East;
                    var bar = ew ? new Rect(s.X - 3, s.Y - 7, 6, 14) : new Rect(s.X - 7, s.Y - 3, 14, 6);
                    ctx.DrawRoundedRectangle(SelectFill, GripEdge, bar, 2, 2);
                    continue;
                }
                if (!g.Middle) { ctx.DrawRectangle(GripBrush, SelectPen, new Rect(s - new Vector(4, 4), new Size(8, 8))); continue; }
                var diamond = new StreamGeometry();
                using (var gc = diamond.Open())
                {
                    gc.BeginFigure(new Point(s.X, s.Y - 6), true, true);
                    gc.PolyLineTo(new[] { new Point(s.X + 6, s.Y), new Point(s.X, s.Y + 6), new Point(s.X - 6, s.Y) }, true, false);
                }
                diamond.Freeze();
                ctx.DrawGeometry(SelectFill, GripEdge, diamond);
            }
            DrawIssues(ctx);
            DrawPreview(ctx);
            DrawLevelTag(ctx);
            DrawScaleBar(ctx);
        }

        // ------------------------------------------------------------------ the foundation plan's connections

        /// <summary>
        /// The connections the foundation plan shows: the points the lateral design puts on the foundation (the model's
        /// "Lateral" section, <see cref="LateralSection"/>), each with its connector. Null: read from the model. A host that
        /// computes them (the shear wall app, before it saves) gives its own; the sketch draws them either way.
        /// </summary>
        public Func<LateralSection> Connections { get; set; }

        /// <summary>Mark where the walls shown meet (<see cref="WallJoints"/>). On by default.</summary>
        public bool ShowJoints { get; set; } = true;

        private static readonly Pen JointPen = P(Palette.Ink, 1.6), FlippedJointPen = P(Palette.Measure, 1.8);

        /// <summary>
        /// Each joint of the walls shown, as built: a short bar across the wall that stops, where it meets the face of the wall
        /// that runs past (a few pixels from the centrelines' point, so it shows at any scale), violet where the engineer turned
        /// the corner round.
        /// </summary>
        private void DrawJoints(DrawingContext ctx, Building b, List<Wall> walls)
        {
            if (walls.Count < 2) return;
            var shown = new HashSet<Wall>(walls);
            foreach (var j in WallJoints.Of(b).Where(x => shown.Contains(x.Through) && shown.Contains(x.Butting)))
            {
                var p = ToScreen(j.At);
                var w = j.Butting;
                var far = ToScreen(Snapper.Dist(j.At, w.Start) < Snapper.Dist(j.At, w.End) ? w.End : w.Start);
                var u = far - p;
                if (u.Length < 16) continue;
                u.Normalize();
                double inset = Math.Max(5, WallJoints.ThicknessOf(b, j.Through, w.TopLevelId) / 2 * Zoom);
                var c = p + u * inset;
                var n = new Vector(-u.Y, u.X) * 5;
                ctx.DrawLine(j.Flipped ? FlippedJointPen : JointPen, c - n, c + n);
            }
        }

        /// <summary>
        /// Mark the shear walls the shear wall app declared (read only: the model's "Lateral" section, <see cref="ShearWallDesign"/>,
        /// every declared wall, designed or not): a band along each one's shear part, where the hold-downs go at its ends. On by
        /// default; the shear wall app, which draws its own, turns it off.
        /// </summary>
        public bool ShowShearParts { get; set; } = true;

        private static readonly Pen ShearPartPen = P(Color.FromArgb(0x66, Palette.ShearWall.R, Palette.ShearWall.G, Palette.ShearWall.B), 7);
        private static readonly Pen StaleShearPartPen = P(Color.FromArgb(0x50, Palette.Muted.R, Palette.Muted.G, Palette.Muted.B), 7);
        private static readonly Brush ShearWallInk = B(Palette.ShearWall), FailsInk = B(Palette.Error);

        /// <summary>The shear parts of the walls shown (<see cref="ShowShearParts"/>), with a short tag at each: "SW", "SW fails".</summary>
        private void DrawShearParts(DrawingContext ctx, Building b, List<Wall> walls)
        {
            var lat = Connections != null ? Connections() : LateralSection.Read(b);
            if (lat?.Walls == null || lat.Walls.Count == 0) return;
            var pen = lat.Stale(b) != null ? StaleShearPartPen : ShearPartPen;
            foreach (var w in walls)
            {
                // one part on every story of the wall: this story's design, else any of the wall's
                var d = lat.Walls.FirstOrDefault(x => x.WallId == w.Id && x.LevelId == w.TopLevelId) ?? lat.Walls.FirstOrDefault(x => x.WallId == w.Id);
                if (d == null) continue;
                var a = new Point2(d.X0, d.Y0);
                var e = new Point2(d.X1, d.Y1);
                if (Snapper.Dist(a, e) < 1e-6) continue;
                Line(ctx, pen, a, e);
                if (ShowLabels)
                    Label(ctx, d.Fails ? "SW fails" : "SW", ToScreen(new Point2(a.X + 0.15 * (e.X - a.X), a.Y + 0.15 * (e.Y - a.Y))) + new Vector(6, -6), d.Fails ? FailsInk : ShearWallInk, 9, false);
            }
        }

        /// <summary>Draw the hold-downs and connections, on the foundation plan and on every level's plan. On by default.</summary>
        public bool ShowConnections { get; set; } = true;

        /// <summary>
        /// Draw the internal forces (the owner's toggle): on (the default), both halves of every connection, each on its plan
        /// (the visual check that everything is accounted for). Off, the cleaner drawing for reports: the selected members are
        /// the body (none selected: the whole structure); a connection between two of them is inside it and not drawn, one at
        /// the cut only on the body's member, one on the foundation on the body's member (<see cref="ConnectionPairs.Drawn"/>).
        /// </summary>
        public bool ShowInternalForces { get; set; } = true;

        /// <summary>Text for the toggle, the same in every app.</summary>
        public const string InternalForcesTip =
            "On: both halves of every connection, each on its plan (the check that everything is accounted for). Off, for reports: the selected members " +
            "are the body (none: the whole structure); the connections inside it are not drawn, those at the cut and on the foundation only on the body's side.";

        /// <summary>Turns <see cref="ShowInternalForces"/> on or off and draws again.</summary>
        public void SetInternalForces(bool on)
        {
            ShowInternalForces = on;
            InvalidateVisual();
        }

        /// <summary>The Internal forces toggle for a host's View menu (the same in every app): checkable, on by default.</summary>
        public System.Windows.Controls.MenuItem InternalForcesMenuItem()
        {
            var mi = new System.Windows.Controls.MenuItem { Header = "_Internal forces", IsCheckable = true, IsChecked = ShowInternalForces, ToolTip = InternalForcesTip };
            mi.Click += (s, e) => SetInternalForces(mi.IsChecked);
            mi.Loaded += (s, e) => mi.IsChecked = ShowInternalForces; // set elsewhere meanwhile (a free body's own box)
            return mi;
        }

        /// <summary>
        /// The walls, beams and columns selected (the body for <see cref="ShowInternalForces"/>), and a level's diaphragm when
        /// one of its pieces is (<see cref="ShearPairs.DiaphragmKey"/>: its free body); null: none selected.
        /// </summary>
        private HashSet<string> BodyIds(Building b)
        {
            var ids = new HashSet<string>(Selection.Where(id => b.Walls.Any(w => w.Id == id) || b.Beams.Any(x => x.Id == id) || b.Columns.Any(x => x.Id == id)));
            foreach (var d in b.Diaphragms.Where(x => Selection.Contains(x.Id))) ids.Add(ShearPairs.DiaphragmKey(d.LevelId));
            return ids.Count == 0 ? null : ids;
        }

        /// <summary>
        /// The hold-downs as markers, each drawn and labelled by the shared rule (<see cref="HoldownMarks"/>, the same in every
        /// app): exactly at the hold-down's point (on the end post's inside face, as the shear wall app found it), on its own
        /// wall; its label beside the wall, kept off the other labels, with an arrow to the marker. On the foundation plan
        /// the foundation's points (<see cref="ConnectionMarkers"/>); on a level's plan its walls' own hold-downs and, a
        /// triangle pointing down, those coming down from the story above onto what is under them (<see cref="LevelMarkers"/>).
        /// Grey and "(out of date)" when the model has changed since they were found (a wall they name is gone or moved).
        /// </summary>
        private void DrawConnections(DrawingContext ctx, Building b, Level level, bool foundation)
        {
            var lat = Connections != null ? Connections() : LateralSection.Read(b);
            if (lat == null || lat.IsEmpty) return;
            var markers = foundation ? ConnectionMarkers(b, lat) : LevelMarkers(b, lat, level);
            if (markers.Count == 0) return;
            var ext = BuildingEdits.Extents(b, ActiveLevel) ?? BuildingEdits.Extents(b);
            Rect? outline = ext == null ? (Rect?)null : ScreenRect(ext.Value);
            var taken = new List<Rect>(memberLabels);
            int had = taken.Count;
            double dip = Dpi;
            HoldownMarks.Draw(ctx, markers, m => new FormattedText(m.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9.5 * LabelScale, m.Ink, dip),
                              outline, taken);
            memberLabels.AddRange(taken.Skip(had)); // a host's own labels keep off these too
        }

        private static readonly Brush ShearInk = B(Palette.Shear);

        /// <summary>
        /// The shear walls' horizontal pairs acting at this level (<see cref="ShearPairs"/>): the tops of the walls under it
        /// into its diaphragm, the sills of the walls standing on it, on the foundation plan the anchor bolts' reactions. Each
        /// half drawn by the shared rule (<see cref="ShearPairs.Drawn"/>, as the hold-downs'): an arrow along the shear part,
        /// beside the wall (the top's on one side, the sill's on the other), the worst case's direction and force (ASD, 0.6 W),
        /// "W4 top 2.0k" on the wall, "on L2 2.0k" on the diaphragm. A picked diaphragm is drawn as its free body in one wind
        /// case (<see cref="DiaphragmCase(LateralSection, string, string)"/>): every pair on it in that case and its own wind at
        /// its point (Fx, Fy; Mz where the analysis gives it), ASD.
        /// </summary>
        private void DrawShearPairs(DrawingContext ctx, Building b, Level level)
        {
            var lat = Connections != null ? Connections() : LateralSection.Read(b);
            if (lat == null || lat.IsEmpty || level == null) return;
            var ink = lat.Stale(b) != null ? MutedBrush : ShearInk;
            var pen = new Pen(ink, 1.4);
            pen.Freeze();
            var body = BodyIds(b);
            // a picked diaphragm: its free body in one case
            var wind = body != null && body.Contains(ShearPairs.DiaphragmKey(level.Id)) ? lat.Diaphragms?.FirstOrDefault(d => d.LevelId == level.Id) : null;
            string one = wind == null ? null : DiaphragmCase(lat, level.Id, FreeBodyCase);
            if (wind != null && one != null) DrawDiaphragmWind(ctx, wind, one, ink);
            foreach (var p in ShearPairs.Of(b, lat).Where(x => x.LevelId == level.Id))
                foreach (bool onWall in new[] { true, false })
                {
                    if (!ShearPairs.Drawn(p, onWall, body, ShowInternalForces)) continue;
                    double f = one != null ? (onWall ? p.OnWallIn(one) : p.OnOtherIn(one)) : ShearPairs.Worst(p, onWall);
                    if (Math.Abs(f) < 1) continue;
                    Point a = ToScreen(p.Start), e = ToScreen(p.End);
                    var u = e - a;
                    if (u.Length < 12) continue;
                    u.Normalize();
                    var n = new Vector(-u.Y, u.X);
                    double off = (p.AtTop ? 1 : -1) * (onWall ? 10 : 17); // clear of the wall's band, the top's on one side, the sill's on the other
                    Point s0 = a + n * off + u * 4, s1 = e + n * off - u * 4;
                    ctx.DrawLine(pen, s0, s1);
                    // the head in the force's direction (+ along the pair's Dir; the screen's y is down)
                    var dir = ToScreen(new Point2(p.Middle.X + p.Dir.X, p.Middle.Y + p.Dir.Y)) - ToScreen(p.Middle);
                    if (dir.Length < 1e-9) continue;
                    dir.Normalize();
                    if (f < 0) dir = -dir;
                    Point tip = Vector.Multiply(dir, u) > 0 ? s1 : s0;
                    var side = new Vector(-dir.Y, dir.X);
                    ctx.DrawLine(pen, tip, tip - dir * 7 + side * 3.5);
                    ctx.DrawLine(pen, tip, tip - dir * 7 - side * 3.5);
                    string text = (onWall ? p.From : $"on {level.Name}") + $" {0.6 * Math.Abs(f) / 1000:0.0}k";
                    // the label beside its arrow, away from the wall, kept for the other labels to keep off
                    var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9 * LabelScale, ink, Dpi);
                    var away = n * Math.Sign(off);
                    Point c = s0 + (s1 - s0) * (onWall ? 0.3 : 0.7) + away * (5 + Math.Abs(away.X) * ft.Width / 2 + Math.Abs(away.Y) * ft.Height / 2);
                    var box = new Rect(c.X - ft.Width / 2 - 2, c.Y - ft.Height / 2 - 1, ft.Width + 4, ft.Height + 2);
                    ctx.DrawRectangle(HoldownMarks.LabelBack, null, box);
                    ctx.DrawText(ft, new Point(box.X + 2, box.Y + 1));
                    memberLabels.Add(box);
                }
        }

        /// <summary>
        /// The wind case a picked diaphragm's free body is drawn in: <paramref name="wanted"/> when the lateral design has it,
        /// else the case with the most wind on the level (|Fx| + |Fy|); null: no wind on it.
        /// </summary>
        public static string DiaphragmCase(LateralSection lat, string levelId, string wanted = null)
        {
            var w = lat?.Diaphragms?.FirstOrDefault(d => d.LevelId == levelId);
            var cases = lat?.Cases ?? new List<string>();
            if (w == null || cases.Count == 0) return null;
            if (wanted != null && cases.Contains(wanted)) return wanted;
            return cases.OrderByDescending(c => Math.Abs(w.FxOf(c)) + Math.Abs(w.FyOf(c))).First();
        }

        /// <summary>The wind case a picked diaphragm's free body is drawn in (null: the one with the most wind on it); an app sets it.</summary>
        public string FreeBodyCase { get; set; }

        /// <summary>A diaphragm's own wind in one case at its point: an arrow each way (ASD, 0.6 W), and its moment where known.</summary>
        private void DrawDiaphragmWind(DrawingContext ctx, DiaphragmWind w, string windCase, Brush ink)
        {
            var at = ToScreen(new Point2(w.X, w.Y));
            double fx = 0.6 * w.FxOf(windCase), fy = 0.6 * w.FyOf(windCase);
            var heavy = new Pen(ink, 2.4);
            heavy.Freeze();
            foreach (var (f, d) in new[] { (fx, new Vector(1, 0)), (fy, new Vector(0, -1)) })
            {
                if (Math.Abs(f) < 1) continue;
                var dir = f > 0 ? d : -d;
                Point tip = at, tail = at - dir * 46;
                ctx.DrawLine(heavy, tail, tip);
                var side = new Vector(-dir.Y, dir.X);
                ctx.DrawLine(heavy, tip, tip - dir * 9 + side * 4.5);
                ctx.DrawLine(heavy, tip, tip - dir * 9 - side * 4.5);
            }
            double? mz = w.MzOf(windCase);
            string text = $"wind {windCase}: Fx {fx / 1000:0.0}k, Fy {fy / 1000:0.0}k" + (mz.HasValue ? $", Mz {0.6 * mz.Value / 1000:0.0}k·ft" : "") + " (ASD)";
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9.5 * LabelScale, ink, Dpi);
            var box = new Rect(at.X + 8, at.Y + 8, ft.Width + 4, ft.Height + 2);
            ctx.DrawRectangle(HoldownMarks.LabelBack, null, box);
            ctx.DrawText(ft, new Point(box.X + 2, box.Y + 1));
            memberLabels.Add(box);
            ctx.DrawEllipse(ink, null, at, 3, 3);
        }

        /// <summary>
        /// The foundation plan's markers, one for each point of the lateral section (screen terms): a wall end's on its wall
        /// (<see cref="LateralPoint.WallId"/>) exactly at its point; a point with no wall (a column base) at the point.
        /// </summary>
        public List<HoldownMarker> ConnectionMarkers(Building b, LateralSection lat)
        {
            var list = new List<HoldownMarker>();
            if (lat == null || lat.IsEmpty) return list;
            bool stale = lat.Stale(b) != null;
            foreach (var p in lat.Points ?? new List<LateralPoint>())
            {
                bool missing = p.Connector == null && p.MaxUp > 0;
                string what = p.Connector ?? (missing ? "no hold-down" : null);
                var m = new HoldownMarker
                {
                    Mark = p.Connector != null ? HoldownMark.Sized : missing ? HoldownMark.Missing : HoldownMark.None,
                    Text = what == null ? null : $"{p.WallId ?? p.From}  {what}" + (stale ? "  (out of date)" : ""),
                    Ink = stale ? MutedBrush : missing ? HoldownMarks.MissingInk : HoldownMarks.TextInk,
                };
                OnWall(b, m, p.WallId, p.Where, 0);
                list.Add(m);
            }
            return list;
        }

        /// <summary>
        /// A level's markers (screen terms), from the lateral design's hold-downs (<see cref="LateralSection.Holdowns"/>): the
        /// walls of the story under it, their own hold-downs on them ("W4  HDU5"; red "W4  no hold-down"); and the hold-downs
        /// and connections of the story above coming down onto this level, a triangle at the point ("↓ W7 2.4k", "on beam B2",
        /// red "nothing below": a beam or a column is needed).
        /// </summary>
        public List<HoldownMarker> LevelMarkers(Building b, LateralSection lat, Level level)
        {
            var list = new List<HoldownMarker>();
            if (lat?.Holdowns == null || level == null) return list;
            bool stale = lat.Stale(b) != null;
            string late = stale ? "  (out of date)" : "";
            // each hold-down is half of a pair of equal and opposite forces: drawn by the one rule of every drawing
            // (ConnectionPairs.Drawn), for the body drawn (the selection; none: the whole structure)
            var pairs = ConnectionPairs.Of(lat);
            var pairOf = new Dictionary<StoryHoldown, ConnectionPair>();
            foreach (var p in pairs) pairOf[p.Source] = p;
            var body = BodyIds(b);
            bool Shown(StoryHoldown h, bool upperHalf) => !pairOf.TryGetValue(h, out var p) || ConnectionPairs.Drawn(p, upperHalf, body, ShowInternalForces);
            foreach (var h in lat.Holdowns.Where(x => x.IsWall && x.LevelId == level.Id && Shown(x, true)))
            {
                bool missing = h.Connector == null;
                var m = new HoldownMarker
                {
                    Mark = missing ? HoldownMark.Missing : HoldownMark.Sized,
                    Text = $"{h.WallId}  {h.Connector ?? "no hold-down"}" + late,
                    Ink = stale ? MutedBrush : missing ? HoldownMarks.MissingInk : HoldownMarks.TextInk,
                };
                OnWall(b, m, h.WallId, h.Where, h.PostAlongWall);
                list.Add(m);
            }
            // a beam's end and a column's base on this level carrying hold-downs on down: a connection of their own, a square
            // at the point ("beam B2 start  MSTI26 ↑ 2.7k"; red: nothing under it, or none strong enough)
            foreach (var h in lat.Holdowns.Where(x => !x.IsWall && x.OntoLevelId == level.Id && Shown(x, true)))
            {
                bool bad = !h.Supported || h.Connector == null;
                string what = !h.Supported ? "nothing under it" : h.Connector ?? "none strong enough";
                var m = new HoldownMarker
                {
                    Mark = bad ? HoldownMark.Missing : HoldownMark.Sized,
                    Text = $"{h.From}  {what} ↑ {h.TensionAsd / 1000:0.0}k" + late,
                    Ink = stale ? MutedBrush : bad ? HoldownMarks.MissingInk : HoldownMarks.TextInk,
                    Icon = ToScreen(h.Where),
                };
                var bm = h.Kind == "beam end" ? b.Beams.FirstOrDefault(x => h.From.StartsWith($"beam {x.Id} ", StringComparison.Ordinal)) : null;
                if (bm != null)
                {
                    bool atStart = Snapper.Dist(h.Where, bm.Start) <= Snapper.Dist(h.Where, bm.End);
                    var u = ToScreen(atStart ? bm.End : bm.Start) - ToScreen(atStart ? bm.Start : bm.End);
                    if (u.Length > 1e-9) { u.Normalize(); m.Inward = u; }
                }
                list.Add(m);
            }
            // the walls' hold-downs coming down from the story above: a triangle pointing down at the point; several landing
            // within 1 ft of each other on one member, one label with their sum, case by case (the worst case of the sum)
            var coming = lat.Holdowns.Where(x => x.IsWall && x.OntoLevelId == level.Id && Shown(x, false)).ToList();
            var together = ConnectionPairs.Near(coming.Where(x => x.Supported && pairOf.ContainsKey(x)).Select(x => pairOf[x]));
            var groups = coming.Where(x => !x.Supported || !pairOf.ContainsKey(x)).Select(x => new List<StoryHoldown> { x })
                               .Concat(together.Select(g => g.Select(p => p.Source).ToList()))
                               .OrderBy(g => coming.IndexOf(g[0])).ToList();
            foreach (var grp in groups)
            {
                var h = grp[0];
                string who = string.Join(" + ", grp.Select(x => x.WallId ?? x.From).Distinct());
                string rest = !h.Supported ? "  nothing below: a beam or a column is needed" : h.Onto.StartsWith("wall ", StringComparison.Ordinal) ? $" onto {h.Onto.Substring(5)}" : $" on {h.Onto}";
                // ASD as the hold-downs are sized: one alone its tension; several, each case's sum (× 0.6 from W at strength level)
                double asd = grp.Count == 1 ? h.TensionAsd
                           : grp.All(x => x.Up != null && x.Up.Count > 0) ? Math.Max(0, ConnectionPairs.Sum(grp.Select(x => pairOf[x])).Values.DefaultIfEmpty(0).Max() * 0.6)
                           : grp.Sum(x => x.TensionAsd);
                var m = new HoldownMarker
                {
                    Mark = h.Supported ? HoldownMark.FromAbove : HoldownMark.FromAboveOntoNothing,
                    Text = $"↓ {who} {asd / 1000:0.0}k{rest}" + late,
                    Ink = stale ? MutedBrush : h.Supported ? HoldownMarks.TextInk : HoldownMarks.MissingInk,
                    Icon = ToScreen(h.Where),
                };
                // along the wall or beam under it, so the label goes beside that
                var under = h.Onto != null && h.Onto.StartsWith("wall ", StringComparison.Ordinal) ? b.Walls.FirstOrDefault(x => x.Id == h.Onto.Substring(5)) : null;
                if (under != null && under.Length > 0) { var u = ToScreen(under.End) - ToScreen(under.Start); if (u.Length > 1e-9) { u.Normalize(); m.Inward = u; } }
                list.Add(m);
            }
            return list;
        }

        /// <summary>
        /// A marker on its wall: exactly at <paramref name="at"/>, labelled beside the wall (along it from its nearer end), as
        /// wide as its post where the drawing shows it (<paramref name="postIn"/>, in; 0: not known, the usual size).
        /// </summary>
        private void OnWall(Building b, HoldownMarker m, string wallId, Point2 at, double postIn)
        {
            m.Icon = ToScreen(at);
            var w = wallId == null ? null : b.Walls.FirstOrDefault(x => x.Id == wallId);
            if (w == null || !(w.Length > 0)) return;
            bool atStart = Snapper.Dist(at, w.Start) <= Snapper.Dist(at, w.End);
            var u = ToScreen(atStart ? w.End : w.Start) - ToScreen(atStart ? w.Start : w.End);
            if (u.Length > 1e-9) { u.Normalize(); m.Inward = u; }
            m.Size = postIn > 0 ? HoldownMarks.SizeFor(postIn / 12.0 * Zoom) : HoldownMarks.Size;
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>The host's pen for a wall of the level shown (e.g. coloured by its design check), or null for the usual one.</summary>
        public Func<Wall, Pen> WallStyle { get; set; }

        public void Line(DrawingContext ctx, Pen pen, Point2 a, Point2 b) => ctx.DrawLine(pen, ToScreen(a), ToScreen(b));

        public Rect ScreenRect(Rect2 r) => new Rect(ToScreen(new Point2(r.X0, r.Y1)), ToScreen(new Point2(r.X1, r.Y0)));

        private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        /// <summary>" · spans W-E" after a diaphragm's ID (a sloped roof's rafters: across the ridge).</summary>
        private static string SpanTag(Building b, Diaphragm d) => SpanOf(b, d) == SpanDirection.WestEast ? " · spans W-E" : " · spans S-N";

        /// <summary>The way a diaphragm's framing runs: a sloped roof's rafters across the ridge, else its own span (Auto: the shorter way).</summary>
        private static SpanDirection SpanOf(Building b, Diaphragm d)
        {
            var roof = b.RoofLevel();
            return roof != null && d.LevelId == roof.Id && b.Roof != null && b.Roof.IsSloped
                ? (b.EffectiveRidge() == RidgeAxis.WestEast ? SpanDirection.SouthNorth : SpanDirection.WestEast)
                : d.EffectiveSpan;
        }

        private static readonly Pen SpanPen = P(Palette.DiaphragmEdge, 1.6);
        private static readonly Brush SpanInk = B(Palette.DiaphragmEdge);

        /// <summary>
        /// A framing zone's span marker, as on a framing plan: in the middle of its largest piece, a line the way its joists or
        /// rafters run with an arrowhead and a bearing bar at each end, and its ID with the span ("D2 · joists S-N", "auto" where
        /// the shorter way was taken, "rafters" across a sloped roof's ridge).
        /// </summary>
        private void SpanMarker(DrawingContext ctx, Building b, Diaphragm d, bool label)
        {
            if (d.Pieces == null || d.Pieces.Count == 0) return;
            var r = d.Pieces.OrderByDescending(p => p.Width * p.Height).First();
            if (r.IsEmpty) return;
            var span = SpanOf(b, d);
            bool we = span == SpanDirection.WestEast;
            var sr = ScreenRect(r);
            double room = we ? sr.Width : sr.Height;
            double half = Math.Min(70, Math.Max(14, 0.3 * room));
            // in the middle, or slid across the span to the first place clear of the walls and beams (an interior wall down the middle)
            double hp = half / Math.Max(1e-9, Zoom);
            var lines = b.WallsUnder(b.Level(d.LevelId)).Select(w => new Segment2(w.Start, w.End))
                         .Concat(b.BeamsOn(b.Level(d.LevelId)).Select(m => new Segment2(m.Start, m.End))).ToList();
            double clear = Math.Max(1.5, 14 / Math.Max(1e-9, Zoom));
            Point2 mid = new Point2((r.X0 + r.X1) / 2, (r.Y0 + r.Y1) / 2);
            foreach (double f in new[] { 0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8 })
            {
                var m = we ? new Point2((r.X0 + r.X1) / 2, r.Y0 + (r.Y1 - r.Y0) * f) : new Point2(r.X0 + (r.X1 - r.X0) * f, (r.Y0 + r.Y1) / 2);
                bool ok = true;
                for (int k = 0; k <= 4 && ok; k++)
                {
                    double t = -hp + 2 * hp * k / 4;
                    var p = we ? new Point2(m.X + t, m.Y) : new Point2(m.X, m.Y + t);
                    ok = lines.All(s => Snapper.DistanceToSegment(p, s) >= clear);
                }
                if (ok) { mid = m; break; }
            }
            var c = ToScreen(mid);
            var u = we ? new Vector(1, 0) : new Vector(0, 1);
            var n = new Vector(-u.Y, u.X);
            var a = c - u * half; var e = c + u * half;
            ctx.DrawLine(SpanPen, a, e);
            double head = Math.Min(8, half * 0.35), bar = Math.Min(9, half * 0.4);
            foreach (var (tip, dir) in new[] { (a, -u), (e, u) })
            {
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(tip, true, true);
                    gc.PolyLineTo(new[] { tip - dir * head + n * head * 0.5, tip - dir * head - n * head * 0.5 }, true, false);
                }
                g.Freeze();
                ctx.DrawGeometry(SpanInk, null, g);
                ctx.DrawLine(SpanPen, tip + n * bar, tip - n * bar);     // the bearing: the wall or beam it lands on
            }
            if (!label) return;
            var roof = b.RoofLevel();
            bool rafters = roof != null && d.LevelId == roof.Id;
            bool across = rafters && b.Roof != null && b.Roof.IsSloped;
            string text = $"{d.Id} · {(rafters ? "rafters" : "joists")} {(we ? "W-E" : "S-N")}" + (across ? " (across the ridge)" : d.Span == SpanDirection.Auto ? " (auto)" : "");
            // beside the marker where it is clear of the walls, beams and wall labels: right, left, under, over
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10 * LabelScale, SpanInk, Dpi);
            double tw = ft.Width + 4, th = ft.Height + 2;
            var tries = we
                ? new[] { new Point(c.X - tw / 2, c.Y + bar + 3), new Point(c.X - tw / 2, c.Y - bar - th - 3), new Point(e.X + 6, c.Y - th / 2), new Point(a.X - tw - 6, c.Y - th / 2) }
                : new[] { new Point(c.X + bar + 4, c.Y - th / 2), new Point(c.X - bar - tw - 4, c.Y - th / 2), new Point(c.X - tw / 2, e.Y + 6), new Point(c.X - tw / 2, a.Y - th - 6) };
            var blocks = lines.Select(s => { var p = ToScreen(s.A); var q = ToScreen(s.B); var rr = new Rect(p, q); rr.Inflate(4, 4); return rr; }).Concat(memberLabels).ToList();
            var at = tries.FirstOrDefault(t => !blocks.Any(x => x.IntersectsWith(new Rect(t.X, t.Y, tw, th))));
            if (at == default(Point)) at = tries[0];
            Label(ctx, text, new Point(at.X + 2, at.Y + 1), SpanInk, 10, true);
        }

        /// <summary>A label at a screen point (top-left), on a light back when asked.</summary>
        public void Label(DrawingContext ctx, string text, Point at, Brush brush, double size = 11, bool back = true, bool bold = false)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                       new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                                       size * LabelScale, brush, Dpi);
            if (back) ctx.DrawRoundedRectangle(LabelBack, null, new Rect(at.X - 2, at.Y - 1, ft.Width + 4, ft.Height + 2), 2, 2);
            ctx.DrawText(ft, at);
        }

        /// <summary>A wall's name and length along the wall, beside its middle (below a level wall, right of an upright one).</summary>
        private void WallLabel(DrawingContext ctx, Wall w) =>
            AlongLabel(ctx, $"{w.Id}  {w.Length:0.##}'", ToScreen(w.Start), ToScreen(w.End), MutedBrush);

        /// <summary>A beam's name along the beam, beside its middle.</summary>
        private void BeamLabel(DrawingContext ctx, Beam bm) => AlongLabel(ctx, bm.Id, ToScreen(bm.Start), ToScreen(bm.End), BeamBrush);

        /// <summary>
        /// A label turned to run along the line a–b (screen), beside its middle: read left to right, or bottom to top on a
        /// line running up the screen (as the walls' design tags), on the side below or right of the line.
        /// </summary>
        private void AlongLabel(DrawingContext ctx, string text, Point a, Point b, Brush brush)
        {
            var along = b - a;
            if (along.Length < 1) return;
            double ang = MemberLabelAngle(along);
            double t = ang * Math.PI / 180;
            var n = new Vector(-Math.Sin(t), Math.Cos(t)); // the label's "down": below a level line, right of an upright one
            var ft = new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10 * LabelScale, brush, Dpi);
            var c = a + along / 2 + n * (ft.Height / 2 + 4);
            var box = new Rect(c.X - ft.Width / 2 - 2, c.Y - ft.Height / 2 - 1, ft.Width + 4, ft.Height + 2);
            var turn = new RotateTransform(ang, c.X, c.Y);
            ctx.PushTransform(turn);
            ctx.DrawRoundedRectangle(LabelBack, null, box, 2, 2);
            ctx.DrawText(ft, new Point(box.X + 2, box.Y + 1));
            ctx.Pop();
            memberLabels.Add(turn.TransformBounds(box));
        }

        /// <summary>The angle (degrees, screen) a label along <paramref name="along"/> is turned by: within [-90, 90), so it reads left to right or bottom to top.</summary>
        public static double MemberLabelAngle(Vector along)
        {
            double ang = Math.Atan2(along.Y, along.X) * 180 / Math.PI;
            if (ang >= 90 - 1e-9) ang -= 180;
            else if (ang < -90 - 1e-9) ang += 180;
            return ang;
        }

        /// <summary>
        /// A column's name set off its square (up and right; "C1 base" down and right) with a short leader ending in an arrow
        /// at the square's corner.
        /// </summary>
        private void ColumnLabel(DrawingContext ctx, Point cs, double half, string text, bool above)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10 * LabelScale, BeamBrush, Dpi);
            var corner = cs + new Vector(half, above ? -half : half);
            const double off = 11;
            var at = new Point(corner.X + off, above ? corner.Y - off - ft.Height : corner.Y + off);
            var box = new Rect(at.X - 2, at.Y - 1, ft.Width + 4, ft.Height + 2);
            var end = new Point(box.Left, above ? box.Bottom : box.Top);
            var d = corner - end;
            d.Normalize();
            var nrm = new Vector(-d.Y, d.X);
            ctx.DrawLine(ColumnLeaderPen, end, corner - d * 4);
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(corner, true, true);
                g.PolyLineTo(new[] { corner - d * 5 + nrm * 2.5, corner - d * 5 - nrm * 2.5 }, true, false);
            }
            head.Freeze();
            ctx.DrawGeometry(BeamBrush, null, head);
            ctx.DrawRoundedRectangle(LabelBack, null, box, 2, 2);
            ctx.DrawText(ft, at);
            var reach = box;
            reach.Union(corner);
            memberLabels.Add(reach);
        }

        private static readonly Pen ColumnLeaderPen = P(BeamColor, 1);

        private void DrawSelected(DrawingContext ctx, ModelObject o)
        {
            switch (o)
            {
                case Wall w:
                    Line(ctx, HaloPen, w.Start, w.End);
                    break;
                case Beam bm:
                    Line(ctx, HaloPen, bm.Start, bm.End);
                    Label(ctx, $"{bm.Length:0.##} ft", ToScreen(bm.Middle) + new Vector(4, 14), B(Palette.Select), 10, true, true);
                    break;
                case Column c:
                {
                    // a column is a small square: a halo ring well clear of it and a crisp circle outside, found at any zoom
                    var cs = ToScreen(c.At);
                    double ring = Math.Max(4, 0.25 * Zoom) + 12;
                    ctx.DrawEllipse(null, HaloPen, cs, ring, ring);
                    ctx.DrawEllipse(null, SelectPen, cs, ring + 8, ring + 8);
                    break;
                }
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
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11 * LabelScale, InkBrush, Dpi);
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

        /// <summary>The grid's line spacings at the present zoom (ft): minor lines (1 ft, coarser when they would be closer than 8 px) and major lines, ten minor apart (10 ft).</summary>
        public (double Minor, double Major) GridSteps()
        {
            double step = GridSpacing > 0 ? GridSpacing : 1;
            while (step * Zoom < 8) step *= step * Zoom < 3 ? 10 : 5;
            return (step, step * 10);
        }

        /// <summary>
        /// The grid: minor lines every foot, major lines every 10 ft (both coarser when zoomed far out), and the scale: each
        /// major line is labelled in feet along the bottom and the left edge of the view (every other one when they crowd).
        /// </summary>
        private void DrawGrid(DrawingContext ctx)
        {
            var (step, major) = GridSteps();
            var a = ToWorld(new Point(0, ActualHeight));
            var c = ToWorld(new Point(ActualWidth, 0));
            var labelsX = new List<(double s, double v)>();
            var labelsY = new List<(double s, double v)>();
            for (double x = Math.Floor(a.X / step) * step; x <= c.X; x += step)
            {
                bool big = Math.Abs(Math.IEEERemainder(x, major)) < step / 2;
                double sx = Math.Round(OriginX + x * Zoom) + 0.5;
                ctx.DrawLine(big ? GridMajorPen : GridMinorPen, new Point(sx, 0), new Point(sx, ActualHeight));
                if (big) labelsX.Add((sx, Math.Round(x / major) * major));
            }
            for (double y = Math.Floor(a.Y / step) * step; y <= c.Y; y += step)
            {
                bool big = Math.Abs(Math.IEEERemainder(y, major)) < step / 2;
                double sy = Math.Round(OriginY - y * Zoom) + 0.5;
                ctx.DrawLine(big ? GridMajorPen : GridMinorPen, new Point(0, sy), new Point(ActualWidth, sy));
                if (big) labelsY.Add((sy, Math.Round(y / major) * major));
            }

            // the scale: feet at the major lines, on a light tag so they read over the drawing
            int every = major * Zoom >= 44 ? 1 : 2;
            foreach (var (s, v) in labelsX)
                if (Math.Round(v / major) % every == 0) GridLabel(ctx, v, new Point(s + 2, ActualHeight - 15));
            foreach (var (s, v) in labelsY)
                if (Math.Round(v / major) % every == 0 && s < ActualHeight - 30) GridLabel(ctx, v, new Point(2, s - 14));
        }

        private static readonly Brush GridLabelBack = B(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF));

        private void GridLabel(DrawingContext ctx, double feet, Point at)
        {
            var ft = new FormattedText(feet.ToString("0.##", CultureInfo.CurrentCulture) + "'", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                       new Typeface("Segoe UI"), 10, MutedBrush, Dpi);
            ctx.DrawRectangle(GridLabelBack, null, new Rect(at.X - 1, at.Y, ft.Width + 3, ft.Height));
            ctx.DrawText(ft, new Point(at.X + 0.5, at.Y));
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
                if (g.Middle)
                {
                    // a ghost of everything selected, where it will go, and the path from the grip
                    var (dx, dy) = BuildingEdits.OrthoOffset(g.At, cursor);
                    var shift = new Vector(dx * Zoom, -dy * Zoom);
                    foreach (var id in selection)
                    {
                        switch (Building.Find(id))
                        {
                            case Wall sw: ctx.DrawLine(PreviewPen, ToScreen(sw.Start) + shift, ToScreen(sw.End) + shift); break;
                            case Note sn: ctx.DrawEllipse(null, PreviewPen, ToScreen(sn.At) + shift, 8, 8); break;
                            case ModelObject so:
                                foreach (var r in LevelView.PiecesOf(so)) { var sr = ScreenRect(r); sr.Offset(shift); ctx.DrawRectangle(null, PreviewPen, sr); }
                                break;
                        }
                    }
                    var to = new Point2(g.At.X + dx, g.At.Y + dy);
                    Line(ctx, WindowPen, g.At, to);
                    Label(ctx, Offset(dx, dy), ToScreen(to) + new Vector(10, -18), B(Palette.Preview));
                }
                else if (g.IsWall && Building.Find(g.Id) is Wall w)
                {
                    var fixedEnd = g.Start ? w.End : w.Start;
                    var p = BuildingEdits.AlongWall(w, cursor);
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
                    case SketchTool.Beam:
                        var be = Ortho ? Snapper.Ortho(cursor, a) : cursor;
                        Line(ctx, BeamPen, a, be);
                        Label(ctx, $"beam {Snapper.Dist(a, be):0.##} ft", ToScreen(be) + new Vector(10, -18), BeamBrush);
                        break;
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
                    case SketchTool.PlanStraighten:
                        DrawMeasure(ctx, a, Ortho ? Snapper.Ortho(cursor, a) : cursor);
                        break;
                    case SketchTool.PlanMove:
                        Line(ctx, RubberPen, a, cursor);
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
                        case SnapModes.Point: // a node: a circle with a cross
                            ctx.DrawEllipse(null, SnapPen, c, 6, 6);
                            ctx.DrawLine(SnapPen, new Point(c.X - 4, c.Y - 4), new Point(c.X + 4, c.Y + 4));
                            ctx.DrawLine(SnapPen, new Point(c.X - 4, c.Y + 4), new Point(c.X + 4, c.Y - 4));
                            break;
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
                    Label(ctx, snap.Label ?? SnapResult.Describe(snap.Kind), c + new Vector(9, 7), B(Palette.Select), 9);
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

        /// <summary>The words of the level tag (the host's own naming), null for the default, "" for no tag.</summary>
        public Func<Level, string> LevelCaption { get; set; }

        /// <summary>The level being drawn, top-left: it must always be clear which level is being edited.</summary>
        private void DrawLevelTag(DrawingContext ctx)
        {
            var l = ActiveLevel;
            double h = Building.StoryHeight(l);
            string text = LevelCaption?.Invoke(l) ?? (l.Elevation == 0 ? $"{l.Name} · 0 ft · the foundation: what stands on it" : $"{l.Name} · {l.Elevation:0.##} ft · story under it {h:0.##} ft");
            if (text.Length == 0) return; // the host shows the level its own way
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                       new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, Brushes.White, Dpi);
            var box = new Rect(8, 8, ft.Width + 16, ft.Height + 8);
            ctx.DrawRoundedRectangle(B(Color.FromRgb(0x33, 0x41, 0x55)), null, box, 4, 4);
            ctx.DrawText(ft, new Point(16, 12));
        }

        /// <summary>The scale bar's length (feet), the shortest round length at least 60 px long; 0: none.</summary>
        private double ScaleBarFeet => new double[] { 1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000 }.FirstOrDefault(n => n * Zoom >= 60);

        /// <summary>Where the scale bar and its "10 ft" are drawn (screen), for a host keeping its labels off it; Empty: none.</summary>
        public Rect ScaleBarArea()
        {
            double len = ScaleBarFeet;
            if (len == 0) return Rect.Empty;
            double px = len * Zoom;
            return new Rect(ActualWidth - px - 58, ActualHeight - 44, px + 58, 18);
        }

        private void DrawScaleBar(DrawingContext ctx)
        {
            double len = ScaleBarFeet;
            if (len == 0) return;
            // bottom right, a row above the grid's numbers (ActualHeight - 15): clear of them and of the line tags along the bottom left
            double px = len * Zoom, x = ActualWidth - px - 56, y = ActualHeight - 34;
            var pen = P(Palette.Muted, 1.5);
            ctx.DrawLine(pen, new Point(x, y), new Point(x + px, y));
            ctx.DrawLine(pen, new Point(x, y - 4), new Point(x, y + 1));
            ctx.DrawLine(pen, new Point(x + px, y - 4), new Point(x + px, y + 1));
            Label(ctx, $"{len:0} ft", new Point(x + px + 6, y - 9), MutedBrush, 10, false);
        }
    }
}
