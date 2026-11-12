using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingModel.Statics
{
    /// <summary>
    /// One set of loads on a beam (+ down): point loads at their places, uniform loads over their parts, and loads varying in a
    /// straight line from WA at A to WB at B (a triangle, a trapezoid; ft from its start, plf).
    /// </summary>
    public sealed class BeamLoads
    {
        public List<(double At, double P)> Points { get; } = new List<(double, double)>();
        public List<(double A, double B, double W)> Spread { get; } = new List<(double, double, double)>();
        public List<(double A, double B, double WA, double WB)> Linear { get; } = new List<(double, double, double, double)>();

        /// <summary>
        /// Every distributed load as a straight line (a uniform one with WA = WB), A before B: the one form the analyses
        /// integrate.
        /// </summary>
        public IEnumerable<(double A, double B, double WA, double WB)> Lines()
        {
            foreach (var s in Spread)
                if (s.B >= s.A) yield return (s.A, s.B, s.W, s.W); else yield return (s.B, s.A, s.W, s.W);
            foreach (var l in Linear)
                if (l.B >= l.A) yield return l; else yield return (l.B, l.A, l.WB, l.WA);
        }

        /// <summary>The intensity (plf) of a straight-line load at x (0 outside it).</summary>
        public static double At((double A, double B, double WA, double WB) l, double x) =>
            x < l.A || x > l.B ? 0 : l.B - l.A <= 0 ? l.WA : l.WA + (l.WB - l.WA) * (x - l.A) / (l.B - l.A);
    }

    /// <summary>
    /// A beam's support reactions by its own free body: a straight member of one homogeneous, prismatic section (EI the same
    /// along it, owner 2026-11-10) on point supports anywhere along it, each holding it up and down but free to turn; its
    /// ends may run past the supports (cantilevers, overhangs). Two supports: statics (ΣF, ΣM), the overhangs included. Three
    /// or more: a continuous beam, the reactions from the deflections being nil at every support. Solved by the suite's one
    /// beam solver, <see cref="global::Statics.Reactions.Of"/> (the Statics repo: the stiffness method with the supports as
    /// the joints, each load in by its exact fixed-end forces). Fewer than two, or all at one place: it cannot stand (a
    /// mechanism), null.
    /// </summary>
    public static class BeamStatics
    {
        /// <summary>The reactions (+ up, holding the beam) at <paramref name="supports"/> for each set of loads; null: it cannot stand.</summary>
        public static double[][] Reactions(double length, IReadOnlyList<double> supports, IReadOnlyList<BeamLoads> sets)
        {
            if (!(length > 0) || supports == null || sets == null) return null;
            if (global::Statics.Reactions.Of(Beam(length, supports, null)) == null) return null;
            var result = new double[sets.Count][];
            for (int s = 0; s < sets.Count; s++)
            {
                result[s] = global::Statics.Reactions.Of(Beam(length, supports, sets[s]));
                if (result[s] == null) return null;
            }
            return result;
        }

        /// <summary>One set of loads: the reactions (+ up) at <paramref name="supports"/>; null: it cannot stand.</summary>
        public static double[] Reactions(double length, IReadOnlyList<double> supports, BeamLoads loads) =>
            Reactions(length, supports, new[] { loads })?[0];

        /// <summary>The beam for the solver: its length, supports and loads (+ down), EI = 1.</summary>
        public static global::Statics.Beam Beam(double length, IReadOnlyList<double> supports, BeamLoads loads)
        {
            var b = new global::Statics.Beam { Length = length };
            b.Supports.AddRange(supports);
            if (loads != null)
            {
                foreach (var p in loads.Points) b.Point(p.At, p.P);
                foreach (var w in loads.Lines()) b.Linear(w.A, w.B, w.WA, w.WB);
            }
            return b;
        }
    }
}
