using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildingModel.Checks;
using System.Windows.Media;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// A checked member's state, the one status language of the suite (owner 2026-11-14, DesignHub docs/UI_AUDIT.md): red
    /// only for what is unsafe (fails) or unchecked (NOT DESIGNED), amber for close to the limit, green for passes; everything
    /// else is drawn in neutral colours. Each state has its glyph too, so colour is never the only cue.
    /// </summary>
    public enum Health { None = 0, Passes, Close, Fails, NotDesigned }

    /// <summary>What a host says of one member on the plan: its state, its worst ratio (demand / capacity; NaN: none) and why, in a few words.</summary>
    public sealed class MemberHealth
    {
        public Health State { get; set; }
        public double Ratio { get; set; } = double.NaN;
        /// <summary>A few words for the label and the hover ("shear", "hold-down T", "bending").</summary>
        public string What { get; set; }

        public static MemberHealth Of(double ratio, string what = null) => new MemberHealth { State = PlanStatus.Of(ratio), Ratio = ratio, What = what };
        public static MemberHealth NotDesigned(string what = null) => new MemberHealth { State = Health.NotDesigned, What = what };
        public static MemberHealth Failing(string what) => new MemberHealth { State = Health.Fails, What = what };

        /// <summary>"✗ 2.13", "▲ 0.94", "✓ 0.72", "⊘ NOT DESIGNED".</summary>
        public string Tag => State == Health.NotDesigned ? $"{PlanStatus.Glyph(State)} NOT DESIGNED"
                           : double.IsNaN(Ratio) ? PlanStatus.Glyph(State) + (State == Health.Fails ? " fails" : "")
                           : $"{PlanStatus.Glyph(State)} {Ratio:0.00}";
    }

    /// <summary>The status colours, glyphs and the close threshold.</summary>
    public static class PlanStatus
    {
        /// <summary>From this ratio up to 1.0 a passing member is close to the limit (owner 2026-11-14: 0.90).</summary>
        public const double CloseRatio = 0.90;

        public static readonly Color Fail = Color.FromRgb(0xC6, 0x28, 0x28);       // red: fails, NOT DESIGNED, model errors
        public static readonly Color Close = Color.FromRgb(0xE6, 0x9F, 0x00);      // amber (Okabe-Ito)
        public static readonly Color Pass = Color.FromRgb(0x2E, 0x7D, 0x32);       // green
        public static readonly Color Info = Color.FromRgb(0x6B, 0x72, 0x80);       // neutral

        public static Health Of(double ratio) =>
            double.IsNaN(ratio) ? Health.None : ratio > 1.0 + 1e-9 ? Health.Fails : ratio >= CloseRatio - 1e-9 ? Health.Close : Health.Passes;

        public static Color ColorOf(Health h) =>
            h == Health.Fails || h == Health.NotDesigned ? Fail : h == Health.Close ? Close : h == Health.Passes ? Pass : Info;

        public static string Glyph(Health h) =>
            h == Health.Fails ? "✗" : h == Health.NotDesigned ? "⊘" : h == Health.Close ? "▲" : h == Health.Passes ? "✓" : "";

        /// <summary>Whether it needs the engineer's eye: fails, close, or not designed (its label is Critical).</summary>
        public static bool Attention(Health h) => h == Health.Fails || h == Health.Close || h == Health.NotDesigned;

        private static readonly Brush[] brushes = new Brush[5];
        public static Brush BrushOf(Health h)
        {
            int i = (int)h;
            if (brushes[i] == null) { var b = new SolidColorBrush(ColorOf(h)); b.Freeze(); brushes[i] = b; }
            return brushes[i];
        }

        /// <summary>
        /// The members the model's errors name, each in its state for the plan: NOT DESIGNED when the error says so (its code
        /// or message), else it fails ("beam not held up"). Warnings colour nothing: red is for what is unsafe.
        /// </summary>
        public static Dictionary<string, MemberHealth> FromIssues(IEnumerable<ModelIssue> issues)
        {
            var map = new Dictionary<string, MemberHealth>();
            foreach (var i in issues ?? Enumerable.Empty<ModelIssue>())
            {
                if (i == null || i.Severity != Severity.Error || i.Ids == null) continue;
                bool nd = (i.Code ?? "").Contains("NotDesigned") || (i.Message ?? "").Contains("NOT DESIGNED");
                string what = Words(i.Code);
                foreach (var id in i.Ids.Where(x => !string.IsNullOrEmpty(x)))
                    if (!map.TryGetValue(id, out var had) || (nd && had.State != Health.NotDesigned))
                        map[id] = nd ? MemberHealth.NotDesigned(what) : MemberHealth.Failing(what);
            }
            return map;
        }

        /// <summary>"BeamNotHeldUp" → "beam not held up".</summary>
        public static string Words(string code) =>
            string.IsNullOrEmpty(code) ? null : Regex.Replace(code, "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();

        /// <summary>A member's line in its state: heavier the worse it is; NOT DESIGNED dashed.</summary>
        public static Pen PenOf(Health h, double width)
        {
            var p = new Pen(BrushOf(h), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (h == Health.NotDesigned) p.DashStyle = new DashStyle(new double[] { 1.6, 1.2 }, 0);
            p.Freeze();
            return p;
        }
    }

    /// <summary>
    /// How much text the plan shows (owner 2026-11-14: Critical when it opens). <see cref="Off"/>: none but what the tools
    /// show while drawing; <see cref="Critical"/>: the members' marks and what fails, is close or is not designed;
    /// <see cref="Normal"/>: and each one's governing values; <see cref="All"/>: everything.
    /// </summary>
    public enum LabelDensity { Off = 0, Critical = 1, Normal = 2, All = 3 }

    /// <summary>What a label is, for <see cref="LabelDensity"/>.</summary>
    public enum LabelRank
    {
        /// <summary>The tools' own feedback while drawing, measuring or snapping: always shown.</summary>
        Live = 0,
        /// <summary>Something unsafe or unchecked: shown from Critical up, and never hidden by another label.</summary>
        Critical,
        /// <summary>A member's mark (W3, BM1, C1, SW2-1): shown from Critical up.</summary>
        Mark,
        /// <summary>A governing value (a force, a load, a size): shown from Normal up.</summary>
        Value,
        /// <summary>Everything else (a zero, a note on a note): All only.</summary>
        Detail,
    }

    public static class LabelDensities
    {
        public static bool Shows(LabelDensity d, LabelRank r) =>
            r == LabelRank.Live || (d != LabelDensity.Off && (r == LabelRank.Critical || r == LabelRank.Mark || (r == LabelRank.Value && d >= LabelDensity.Normal) || d == LabelDensity.All));

        public static string Name(LabelDensity d) => d == LabelDensity.Off ? "Off" : d == LabelDensity.Critical ? "Critical" : d == LabelDensity.Normal ? "Normal" : "All";

        public static string Help(LabelDensity d) =>
            d == LabelDensity.Off ? "No labels (hover a member for its own)." :
            d == LabelDensity.Critical ? "The marks, and what fails, is close to its limit or is NOT DESIGNED." :
            d == LabelDensity.Normal ? "And each one's governing values; labels that would overlap are hidden (hover to see them)." :
            "Everything, as before; labels that would overlap are still hidden (hover to see them).";
    }
}
