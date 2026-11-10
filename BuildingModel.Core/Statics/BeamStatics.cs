using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingModel.Statics
{
    /// <summary>One set of loads on a beam (+ down): point loads at their places, uniform loads over their parts (ft from its start).</summary>
    public sealed class BeamLoads
    {
        public List<(double At, double P)> Points { get; } = new List<(double, double)>();
        public List<(double A, double B, double W)> Spread { get; } = new List<(double, double, double)>();
    }

    /// <summary>
    /// A beam's support reactions by its own free body: a straight member of one homogeneous, prismatic section (EI the same
    /// along it, owner 2026-11-10) on point supports anywhere along it, each holding it up and down but free to turn; its
    /// ends may run past the supports (cantilevers, overhangs). Two supports: statics (ΣF, ΣM), the overhangs included. Three
    /// or more: a continuous beam, the reactions from the deflections being nil at every support (the stiffness method: beam
    /// elements between the supports, the loads' places and the ends of the loads, EI = 1; exact for a prismatic member).
    /// Fewer than two, or all at one place: it cannot stand (a mechanism), null.
    /// </summary>
    public static class BeamStatics
    {
        private const double Merge = 1e-6;

        /// <summary>The reactions (+ up, holding the beam) at <paramref name="supports"/> for each set of loads; null: it cannot stand.</summary>
        public static double[][] Reactions(double length, IReadOnlyList<double> supports, IReadOnlyList<BeamLoads> sets)
        {
            if (!(length > 0) || supports == null || sets == null) return null;
            double L = length;
            double Clamp(double x) => Math.Max(0, Math.Min(L, x));
            // the nodes: the ends, the supports, the point loads, the ends of the uniform loads
            var at = new List<double> { 0, L };
            at.AddRange(supports.Select(Clamp));
            foreach (var s in sets)
            {
                at.AddRange(s.Points.Select(p => Clamp(p.At)));
                foreach (var w in s.Spread) { at.Add(Clamp(w.A)); at.Add(Clamp(w.B)); }
            }
            at.Sort();
            var x = new List<double>();
            foreach (var v in at) if (x.Count == 0 || v - x[x.Count - 1] > Merge) x.Add(v);
            int nodes = x.Count;
            int Node(double v) { v = Clamp(v); int best = 0; for (int i = 1; i < nodes; i++) if (Math.Abs(x[i] - v) < Math.Abs(x[best] - v)) best = i; return best; }

            var sNode = supports.Select(Node).ToList();
            if (sNode.Distinct().Count() < 2) return null;
            var fixedW = new HashSet<int>(sNode);

            // the free degrees of freedom: each node's deflection (unless supported) and rotation, in order along the beam
            int n = 0;
            var dofW = new int[nodes];
            var dofT = new int[nodes];
            for (int i = 0; i < nodes; i++)
            {
                dofW[i] = fixedW.Contains(i) ? -1 : n++;
                dofT[i] = n++;
            }
            const int band = 3; // half-bandwidth: each element joins two nodes' two each
            var K = new double[n, 2 * band + 1];
            void AddK(int r, int c, double v) { if (r >= 0 && c >= 0) K[r, c - r + band] += v; }
            for (int e = 0; e + 1 < nodes; e++)
            {
                double h = x[e + 1] - x[e];
                var k = ElementStiffness(h);
                var d = new[] { dofW[e], dofT[e], dofW[e + 1], dofT[e + 1] };
                for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) AddK(d[i], d[j], k[i, j]);
            }

            // the loads: consistent nodal forces (+ down, and the moments that go with them)
            int m = sets.Count;
            var F = new double[m][];
            var Fall = new double[m][]; // every node's deflection force and moment, for the reactions
            for (int s = 0; s < m; s++)
            {
                F[s] = new double[n];
                Fall[s] = new double[2 * nodes];
                foreach (var p in sets[s].Points) Fall[s][2 * Node(p.At)] += p.P;
                foreach (var w in sets[s].Spread)
                {
                    double a = Clamp(Math.Min(w.A, w.B)), b = Clamp(Math.Max(w.A, w.B));
                    for (int e = 0; e + 1 < nodes; e++)
                    {
                        double x0 = x[e], x1 = x[e + 1], h = x1 - x0;
                        if (x0 < a - Merge || x1 > b + Merge) continue; // the nodes are at its ends: a whole element or none
                        Fall[s][2 * e] += w.W * h / 2;
                        Fall[s][2 * e + 1] += w.W * h * h / 12;
                        Fall[s][2 * e + 2] += w.W * h / 2;
                        Fall[s][2 * e + 3] -= w.W * h * h / 12;
                    }
                }
                for (int i = 0; i < nodes; i++)
                {
                    if (dofW[i] >= 0) F[s][dofW[i]] = Fall[s][2 * i];
                    F[s][dofT[i]] = Fall[s][2 * i + 1];
                }
            }

            // solve K d = F (banded, symmetric positive definite when it can stand: no pivoting needed)
            double scale = 0;
            for (int i = 0; i < n; i++) scale = Math.Max(scale, Math.Abs(K[i, band]));
            for (int i = 0; i < n; i++)
            {
                double piv = K[i, band];
                if (!(Math.Abs(piv) > 1e-10 * Math.Max(1, scale))) return null;
                for (int r = i + 1; r <= Math.Min(n - 1, i + band); r++)
                {
                    double f = K[r, i - r + band] / piv;
                    if (f == 0) continue;
                    for (int c = i; c <= Math.Min(n - 1, i + band); c++) K[r, c - r + band] -= f * K[i, c - i + band];
                    for (int s = 0; s < m; s++) F[s][r] -= f * F[s][i];
                }
            }
            var D = new double[m][];
            for (int s = 0; s < m; s++)
            {
                var d = new double[n];
                for (int i = n - 1; i >= 0; i--)
                {
                    double sum = F[s][i];
                    for (int c = i + 1; c <= Math.Min(n - 1, i + band); c++) sum -= K[i, c - i + band] * d[c];
                    d[i] = sum / K[i, band];
                }
                D[s] = d;
            }

            // each support's reaction: what the beam's elements push down on it less the load put on it there, turned to + up
            var result = new double[m][];
            for (int s = 0; s < m; s++)
            {
                var push = new double[nodes];
                for (int e = 0; e + 1 < nodes; e++)
                {
                    double h = x[e + 1] - x[e];
                    var k = ElementStiffness(h);
                    var dd = new[] { dofW[e], dofT[e], dofW[e + 1], dofT[e + 1] }.Select(i => i < 0 ? 0 : D[s][i]).ToArray();
                    for (int j = 0; j < 4; j++) { push[e] += k[0, j] * dd[j]; push[e + 1] += k[2, j] * dd[j]; }
                }
                var r = new double[supports.Count];
                var done = new HashSet<int>();
                for (int i = 0; i < supports.Count; i++)
                    if (done.Add(sNode[i])) r[i] = Fall[s][2 * sNode[i]] - push[sNode[i]]; // two supports at one place: the first takes it
                result[s] = r;
            }
            return result;
        }

        /// <summary>One set of loads: the reactions (+ up) at <paramref name="supports"/>; null: it cannot stand.</summary>
        public static double[] Reactions(double length, IReadOnlyList<double> supports, BeamLoads loads) =>
            Reactions(length, supports, new[] { loads })?[0];

        /// <summary>A beam element's stiffness (EI = 1): deflection (+ down) and rotation at each end.</summary>
        private static double[,] ElementStiffness(double h)
        {
            double h2 = h * h, h3 = h2 * h;
            return new double[,]
            {
                { 12 / h3, 6 / h2, -12 / h3, 6 / h2 },
                { 6 / h2, 4 / h, -6 / h2, 2 / h },
                { -12 / h3, -6 / h2, 12 / h3, -6 / h2 },
                { 6 / h2, 2 / h, -6 / h2, 4 / h },
            };
        }
    }
}
