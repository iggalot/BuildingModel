using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingModel.Model
{
    /// <summary>
    /// The sawn beam sizes a beam can be given (owner 2026-11-12, "beam sizes": a size per beam, in the model): one 2x or 2 to 4
    /// plies of it nailed side by side ("3-2x12"), lightest first (by area, the deeper first at the same area). The design
    /// (WoodFraming's beam check) checks the beam's own size and names the lightest that works.
    /// </summary>
    public static class BeamSizes
    {
        public const int MostPlies = 4;
        private static readonly string[] Plies = { "2x6", "2x8", "2x10", "2x12" };

        /// <summary>Every size, lightest first.</summary>
        public static readonly string[] All =
            Enumerable.Range(1, MostPlies).SelectMany(n => Plies.Select(p => n == 1 ? p : $"{n}-{p}"))
                      .OrderBy(s => PliesOf(s) * DepthOf(s)).ThenByDescending(DepthOf).ToArray();

        /// <summary>
        /// The engineered beams (owner 2026-11-12, "LVL and glulam"), lightest first: LVL 1-3/4" plies, 1 to 3 of them
        /// ("2-1.75x11.875 LVL"), and glulam 24F-1.8E ("5.125x12 GL": 3-1/8", 5-1/8" and 6-3/4" wide, 1-1/2" laminations).
        /// Actual sizes (in). Sized only when no sawn size works (the design names the lightest that does).
        /// </summary>
        public static readonly string[] Engineered =
            new[] { 9.25, 9.5, 11.25, 11.875, 14, 16, 18 }.SelectMany(d => Enumerable.Range(1, 3).Select(n => (n == 1 ? "" : $"{n}-") + $"1.75x{Num(d)} LVL"))
            .Concat(new[] { 3.125, 5.125, 6.75 }.SelectMany(w => Enumerable.Range(6, 11).Select(k => 1.5 * k).Where(d => d >= 9 && d <= 24 && d / w <= 6.5).Select(d => $"{Num(w)}x{Num(d)} GL")))
            .OrderBy(s => BreadthOf0(s) * DepthOf0(s)).ThenByDescending(DepthOf0).ToArray();

        private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        public static bool IsSize(string size) => Array.IndexOf(All, size) >= 0 || Array.IndexOf(Engineered, size) >= 0;

        /// <summary>An LVL or glulam size ("… LVL", "… GL").</summary>
        public static bool IsEngineered(string size) => size != null && (size.EndsWith(" LVL") || size.EndsWith(" GL"));

        // an engineered size's actual breadth and depth (in), from its name
        private static double BreadthOf0(string size)
        {
            string core = size.Substring(0, size.LastIndexOf(' '));
            int n = PliesOf(size);
            if (core.Contains("-")) core = core.Substring(core.IndexOf('-') + 1);
            return n * double.Parse(core.Split('x')[0], System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double DepthOf0(string size)
        {
            string core = size.Substring(0, size.LastIndexOf(' '));
            return double.Parse(core.Split('x')[1], System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>"3-2x12": 3; "2x10": 1; not a size: 0.</summary>
        public static int PliesOf(string size)
        {
            if (string.IsNullOrEmpty(size)) return 0;
            int dash = size.IndexOf('-');
            return dash < 0 ? 1 : int.TryParse(size.Substring(0, dash), out int n) ? n : 0;
        }

        /// <summary>One ply's size: "3-2x12" is "2x12".</summary>
        public static string PlyOf(string size) => string.IsNullOrEmpty(size) ? size : size.Substring(size.IndexOf('-') + 1);

        /// <summary>The actual breadth (in): 1-1/2 a ply; 0 when not a size.</summary>
        public static double BreadthOf(string size) => !IsSize(size) ? 0 : IsEngineered(size) ? BreadthOf0(size) : 1.5 * PliesOf(size);

        /// <summary>The actual depth (in): 2x6 5.5, 2x8 7.25, 2x10 9.25, 2x12 11.25; 0 when not a size.</summary>
        public static double DepthOf(string size)
        {
            if (IsEngineered(size)) return IsSize(size) ? DepthOf0(size) : 0;
            switch (PlyOf(size))
            {
                case "2x6": return 5.5;
                case "2x8": return 7.25;
                case "2x10": return 9.25;
                case "2x12": return 11.25;
                default: return 0;
            }
        }
    }
}
