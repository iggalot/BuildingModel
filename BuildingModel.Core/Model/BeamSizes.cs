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

        public static bool IsSize(string size) => Array.IndexOf(All, size) >= 0;

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
        public static double BreadthOf(string size) => IsSize(size) ? 1.5 * PliesOf(size) : 0;

        /// <summary>The actual depth (in): 2x6 5.5, 2x8 7.25, 2x10 9.25, 2x12 11.25; 0 when not a size.</summary>
        public static double DepthOf(string size)
        {
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
