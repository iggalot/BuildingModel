using System;
using System.Linq;

namespace BuildingModel.Model
{
    /// <summary>
    /// The sawn sizes a column can be given (owner 2026-11-12, a beam's bearing on a column): a solid post, or 2 to 4 2x studs
    /// nailed together ("3-2x6"). Actual sizes (NDS Supplement Table 1B: a 4x is 3-1/2", a 6x and up 1/2" under nominal; a 2x
    /// ply 1-1/2").
    /// </summary>
    public static class ColumnSizes
    {
        public static readonly string[] All =
        {
            "4x4", "4x6", "6x6", "6x8", "8x8",
            "2-2x4", "3-2x4", "2-2x6", "3-2x6", "4-2x6",
        };

        public static bool IsSize(string size) => Array.IndexOf(All, size) >= 0;

        /// <summary>Its two actual dimensions (in), the smaller first; (0, 0) when not a size.</summary>
        public static (double Small, double Large) Dimensions(string size)
        {
            if (!IsSize(size)) return (0, 0);
            int dash = size.IndexOf('-');
            if (dash > 0)
            {
                int n = int.Parse(size.Substring(0, dash));
                double d = Actual(int.Parse(size.Substring(dash + 1).Split('x')[1]));
                double b = 1.5 * n;
                return (Math.Min(b, d), Math.Max(b, d));
            }
            var p = size.Split('x').Select(int.Parse).Select(Actual).ToArray();
            return (Math.Min(p[0], p[1]), Math.Max(p[0], p[1]));
        }

        /// <summary>A nominal dimension's actual (in): 2 → 1.5, 4 → 3.5, 6 → 5.5, 8 → 7.5.</summary>
        private static double Actual(int nominal) => nominal - 0.5;
    }
}
