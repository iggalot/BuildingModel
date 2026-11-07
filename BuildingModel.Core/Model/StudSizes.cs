using System;

namespace BuildingModel.Model
{
    /// <summary>
    /// The stud sizes a wall can have (<see cref="StudFraming.StudSize"/>, <see cref="ModelDefaults"/>) and their actual depth:
    /// the wall's thickness in its framing (the plates are as wide). Every app reads them here (WoodFraming lays out the
    /// framing with them; the wall joints stop a wall at another's face with them).
    /// </summary>
    public static class StudSizes
    {
        public static readonly string[] All = { "2x4", "2x6", "2x8" };

        /// <summary>The actual depth (in) of a nominal stud size: 2x4 3.5, 2x6 5.5, 2x8 7.25.</summary>
        public static double DepthOf(string size)
        {
            switch (size)
            {
                case "2x4": return 3.5;
                case "2x6": return 5.5;
                case "2x8": return 7.25;
                default: throw new ArgumentException($"Not a stud size: \"{size}\".", nameof(size));
            }
        }

        /// <summary>The depth (in) of a size, or <paramref name="otherwise"/> for one not known.</summary>
        public static double DepthOr(string size, double otherwise)
        {
            switch (size)
            {
                case "2x4": case "2x6": case "2x8": return DepthOf(size);
                default: return otherwise;
            }
        }
    }
}
