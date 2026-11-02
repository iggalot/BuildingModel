using System;
using System.Globalization;
using System.Text.RegularExpressions;
using BuildingModel.Geometry;

namespace BuildingModel.Edits
{
    /// <summary>
    /// Points typed instead of clicked (from the Shear Wall Designer) (AutoCAD style), in feet from the drawing origin:
    /// <list type="bullet">
    /// <item><c>12,30</c> — absolute x,y</item>
    /// <item><c>@5,0</c> — relative to the last point</item>
    /// <item><c>@20&lt;90</c> — relative, length and angle (degrees, counterclockwise from +x)</item>
    /// <item><c>20</c> — a length from the last point toward the cursor</item>
    /// </list>
    /// Every number can also be feet and inches: <c>12'6"</c>, <c>12'-6"</c>, <c>12' 6</c>, <c>6"</c>, <c>-3'</c>.
    /// </summary>
    public static class CoordinateInput
    {
        /// <summary>
        /// Reads a typed point. <paramref name="last"/> is the previous point (null before the first one);
        /// <paramref name="toward"/> gives the direction for a bare length (the cursor). False with a message when the
        /// text cannot be used.
        /// </summary>
        public static bool TryParse(string text, Point2? last, Point2 toward, out Point2 point, out string error)
        {
            point = default(Point2);
            error = null;
            string s = (text ?? "").Trim();
            if (s.Length == 0) { error = "Type a point: x,y   @dx,dy   @length<angle   or a length."; return false; }

            bool relative = s.StartsWith("@");
            if (relative) s = s.Substring(1).Trim();
            if ((relative || !s.Contains(",")) && last == null)
            {
                error = "Pick or type the first point (x,y) before a relative point or a length.";
                return false;
            }

            int lt = s.IndexOf('<');
            if (lt >= 0)
            {
                if (!relative) { error = "A length and angle needs @ in front, e.g. @20<90."; return false; }
                if (!TryLength(s.Substring(0, lt), out double len)) { error = $"\"{s.Substring(0, lt).Trim()}\" is not a length."; return false; }
                if (!double.TryParse(s.Substring(lt + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double deg))
                { error = $"\"{s.Substring(lt + 1).Trim()}\" is not an angle in degrees."; return false; }
                double a = deg * Math.PI / 180;
                point = new Point2(last.Value.X + len * Math.Cos(a), last.Value.Y + len * Math.Sin(a));
                point = Clean(point);
                return true;
            }

            int comma = s.IndexOf(',');
            if (comma >= 0)
            {
                if (!TryLength(s.Substring(0, comma), out double x)) { error = $"\"{s.Substring(0, comma).Trim()}\" is not a number."; return false; }
                if (!TryLength(s.Substring(comma + 1), out double y)) { error = $"\"{s.Substring(comma + 1).Trim()}\" is not a number."; return false; }
                point = relative ? new Point2(last.Value.X + x, last.Value.Y + y) : new Point2(x, y);
                return true;
            }

            // a bare length: from the last point toward the cursor
            if (!TryLength(s, out double l)) { error = $"\"{s}\" is not a point or a length."; return false; }
            double dx = toward.X - last.Value.X, dy = toward.Y - last.Value.Y, dl = Math.Sqrt(dx * dx + dy * dy);
            if (dl < 1e-9) { error = "Move the cursor in the direction to measure the length."; return false; }
            point = Clean(new Point2(last.Value.X + l * dx / dl, last.Value.Y + l * dy / dl));
            return true;
        }

        private static readonly Regex FeetInches = new Regex(
            @"^\s*(?<sign>-)?\s*(?:(?<ft>\d+(?:\.\d*)?|\.\d+)\s*'\s*-?\s*)?(?:(?<in>\d+(?:\.\d*)?|\.\d+)\s*""?)?\s*$");

        /// <summary>A length in feet: a plain number, or feet and inches (12'6", 12'-6", 6", -3').</summary>
        public static bool TryLength(string text, out double feet)
        {
            feet = 0;
            string s = (text ?? "").Trim();
            if (s.Length == 0) return false;
            if (s.IndexOf('\'') < 0 && s.IndexOf('"') < 0)
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out feet) && !double.IsNaN(feet) && !double.IsInfinity(feet);

            var m = FeetInches.Match(s);
            if (!m.Success || (!m.Groups["ft"].Success && !m.Groups["in"].Success)) return false;
            // a number after the feet mark without " is inches; a lone number with " is inches
            double ft = m.Groups["ft"].Success ? double.Parse(m.Groups["ft"].Value, CultureInfo.InvariantCulture) : 0;
            double inch = m.Groups["in"].Success ? double.Parse(m.Groups["in"].Value, CultureInfo.InvariantCulture) : 0;
            feet = ft + inch / 12.0;
            if (m.Groups["sign"].Success) feet = -feet;
            return true;
        }

        /// <summary>Removes round-off such as 1e-15 from cos 90°.</summary>
        private static Point2 Clean(Point2 p) => new Point2(Math.Round(p.X, 9), Math.Round(p.Y, 9));
    }
}
