using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace BuildingModel.Sketch
{
    /// <summary>What a hold-down marker says: a hold-down chosen, none strong enough (or none at all where it lifts), none needed.</summary>
    public enum HoldownMark { Sized, Missing, None }

    /// <summary>One hold-down (or member-end connection) to mark on a plan, in screen terms.</summary>
    public sealed class HoldownMarker
    {
        /// <summary>The marker's centre (screen), from <see cref="HoldownMarks.IconAt"/>.</summary>
        public Point Icon { get; set; }
        /// <summary>Along its member, from the end into the member (screen, a unit vector); the label goes beside the member.</summary>
        public Vector Inward { get; set; } = new Vector(1, 0);
        public HoldownMark Mark { get; set; }
        public string Text { get; set; }
        public Brush Ink { get; set; }
        /// <summary>The marker's side (px): <see cref="HoldownMarks.SizeFor"/> its post.</summary>
        public double Size { get; set; } = HoldownMarks.Size;
    }

    /// <summary>
    /// The hold-down markers and their labels, the same in every app that draws them (the Shear Wall Calculator's level plans,
    /// the shared sketch's foundation plan): each hold-down's marker at its true place (its post's inside face, a post in from the end of
    /// the shear part), a member end's connection just in from the end; each label beside its member, outside the building first, kept off every other label and
    /// marker, with an arrow to its marker.
    /// </summary>
    public static class HoldownMarks
    {
        /// <summary>The marker's side (px).</summary>
        public const double Size = 10;

        // (in this order: a static field is set in the order written, the colours before the brushes made of them)
        /// <summary>The marker's colours: a hold-down chosen (dark); none strong enough, or none where it lifts (red).</summary>
        public static readonly Color SizedColor = Color.FromRgb(0x1F, 0x29, 0x33), MissingColor = Color.FromRgb(0xA5, 0x0E, 0x1F);
        public static readonly Brush LabelBack = Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        /// <summary>A label's ink: a hold-down chosen; none where one is needed (the red of the open marker).</summary>
        public static readonly Brush TextInk = Frozen(Color.FromRgb(0x52, 0x51, 0x4E)), MissingInk = Frozen(MissingColor);
        private static readonly Brush SizedFill = Frozen(SizedColor), OpenFill = Frozen(Colors.White);
        private static readonly Pen SizedEdge = Frozen(Brushes.White, 1),
                                    MissingEdge = Frozen(Frozen(MissingColor), 2),
                                    NoneEdge = Frozen(Frozen(Color.FromRgb(0x8B, 0x4A, 0x14)), 1.6);

        /// <summary>The smallest marker (px), for a post too small to show at the drawing's scale.</summary>
        public const double MinSize = 6;

        /// <summary>
        /// Where a member end's marker goes (screen), on the member from its end at <paramref name="end"/> toward
        /// <paramref name="other"/>:
        /// <list type="bullet">
        /// <item>a hold-down (<paramref name="trueInset"/> given, px): exactly at its place, the inside face of the end post the
        /// hold-down needs (a post in from the end of the shear part: the end is the post's other face), at every scale. Where two are close (two walls
        /// at a corner) each label's arrow shows which is which; zoom in to see them apart;</item>
        /// <item>a member end's connection (none given): a marker's size and 1 px in, its edge 6 px from the end, so two
        /// members meeting at one point each show their own.</item>
        /// </list>
        /// </summary>
        public static Point IconAt(Point end, Point other, double size = Size, double? trueInset = null)
        {
            var u = other - end;
            if (u.Length < 1e-9) return end;
            double len = u.Length;
            u.Normalize();
            return end + u * (trueInset is double t ? Math.Min(len, Math.Max(0, t)) : Math.Min(len / 2, size + 1));
        }

        /// <summary>A hold-down marker's side (px) for its post (<paramref name="postPx"/>, along the wall): the post's own width,
        /// between <see cref="MinSize"/> and <see cref="Size"/>.</summary>
        public static double SizeFor(double postPx) => Math.Max(MinSize, Math.Min(Size, postPx));

        /// <summary>The marker centred at <paramref name="p"/>: a dark square; none strong enough, an open red square crossed; none needed, an open brown square. Returns its box.</summary>
        public static Rect DrawIcon(DrawingContext ctx, Point p, HoldownMark mark, double size = Size)
        {
            var box = new Rect(p.X - size / 2, p.Y - size / 2, size, size);
            if (mark == HoldownMark.Sized) ctx.DrawRectangle(SizedFill, SizedEdge, box);
            else if (mark == HoldownMark.None) ctx.DrawRectangle(OpenFill, NoneEdge, box);
            else
            {
                ctx.DrawRectangle(OpenFill, MissingEdge, box);
                ctx.DrawLine(MissingEdge, box.TopLeft, box.BottomRight);
            }
            return box;
        }

        /// <summary>True when the boxes share some area (a shared edge is not an overlap).</summary>
        public static bool Overlaps(Rect a, Rect b)
        {
            var i = Rect.Intersect(a, b);
            return !i.IsEmpty && i.Width > 0.5 && i.Height > 0.5;
        }

        /// <summary>The area of <paramref name="r"/> covered by the boxes (px²; overlaps between them counted twice).</summary>
        public static double Covered(Rect r, IEnumerable<Rect> boxes) =>
            boxes.Sum(t => { var i = Rect.Intersect(r, t); return i.IsEmpty ? 0 : i.Width * i.Height; });

        /// <summary>
        /// Where a marker's label of <paramref name="size"/> goes: beside its member a little in from the end, outside the
        /// building (<paramref name="outline"/>, screen) first when the member is on its edge, on the first spot clear of
        /// <paramref name="taken"/> (else the least covered). The box is added to <paramref name="taken"/>.
        /// </summary>
        public static Rect LabelBox(Point icon, Vector inward, Size size, Rect? outline, List<Rect> taken)
        {
            var u = inward;
            if (u.Length < 1e-9) u = new Vector(1, 0);
            u.Normalize();
            var side = new Vector(-u.Y, u.X);
            var sides = new[] { side, -side };
            if (outline is Rect o && o.Contains(icon + side * 20) && !o.Contains(icon - side * 20)) sides = new[] { -side, side }; // outside first
            var tries = new List<Rect>();
            foreach (double along in new[] { 14.0, 30, 48, 68 })
                foreach (var s in sides)
                {
                    double reach = 16 + (Math.Abs(s.X) * size.Width + Math.Abs(s.Y) * size.Height) / 2;
                    var m = icon + u * along + s * reach;
                    tries.Add(new Rect(m.X - size.Width / 2, m.Y - size.Height / 2, size.Width, size.Height));
                }
            var box = tries.FirstOrDefault(x => !taken.Any(t => Overlaps(t, x)));
            if (box.Width == 0) box = tries.OrderBy(x => Covered(x, taken)).First(); // default is a zero box
            taken.Add(box);
            return box;
        }

        /// <summary>The arrow from the label's nearest edge to the marker's edge (none when the label touches it).</summary>
        public static void Arrow(DrawingContext ctx, Rect box, Point icon, Brush ink, double size = Size)
        {
            var from = new Point(Math.Max(box.Left, Math.Min(box.Right, icon.X)), Math.Max(box.Top, Math.Min(box.Bottom, icon.Y)));
            var d = icon - from;
            double half = size / 2 + 1;
            if (d.Length <= half + 6) return;
            d.Normalize();
            double edge = half / Math.Max(Math.Abs(d.X), Math.Abs(d.Y)); // to the square's side
            var tip = icon - d * edge;
            var pen = new Pen(ink, 0.9);
            pen.Freeze();
            ctx.DrawLine(pen, from, tip - d * 4);
            var n = new Vector(-d.Y, d.X);
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(tip, true, true);
                g.PolyLineTo(new[] { tip - d * 5 + n * 2.5, tip - d * 5 - n * 2.5 }, true, false);
            }
            head.Freeze();
            ctx.DrawGeometry(ink, null, head);
        }

        /// <summary>A label's text on its light back.</summary>
        public static void DrawLabel(DrawingContext ctx, Rect box, FormattedText ft)
        {
            ctx.DrawRectangle(LabelBack, null, box);
            ctx.DrawText(ft, new Point(box.X + 2, box.Y));
        }

        /// <summary>
        /// The whole drawing: every marker first (each kept clear in <paramref name="taken"/>), then each label with its arrow,
        /// then the labels' text over all the arrows. <paramref name="text"/> makes a label's text in the host's font and size.
        /// </summary>
        public static void Draw(DrawingContext ctx, IList<HoldownMarker> markers, Func<HoldownMarker, FormattedText> text, Rect? outline, List<Rect> taken)
        {
            foreach (var m in markers)
            {
                var box = DrawIcon(ctx, m.Icon, m.Mark, m.Size);
                box.Inflate(1, 1);
                taken.Add(box);
            }
            var held = new List<(Rect, FormattedText)>();
            foreach (var m in markers.Where(x => !string.IsNullOrEmpty(x.Text)))
            {
                var ft = text(m);
                var box = LabelBox(m.Icon, m.Inward, new Size(ft.Width + 4, ft.Height), outline, taken);
                Arrow(ctx, box, m.Icon, m.Ink, m.Size);
                held.Add((box, ft));
            }
            foreach (var (box, ft) in held) DrawLabel(ctx, box, ft);
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen Frozen(Brush b, double t) { var p = new Pen(b, t); p.Freeze(); return p; }
    }
}
