using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>
    /// One connection between two members as a pair of equal and opposite forces (the owner's rule, 2026-11-07): the
    /// connector's force once (<see cref="Force"/>, + tension, − compression, per wind case), acting down on the member above
    /// and up on the member below (or the foundation). Made from the lateral design's story hold-downs
    /// (<see cref="StoryHoldown"/>) by <see cref="ConnectionPairs.Of"/>: the Load Manager puts each force on its members'
    /// free bodies and checks they balance; the plans draw them (both, or with the internal ones left out).
    /// </summary>
    public sealed class ConnectionPair
    {
        /// <summary>The story hold-down it is made from.</summary>
        public StoryHoldown Source { get; set; }
        public string From => Source?.From;
        public string Connector => Source?.Connector;

        /// <summary>The member above (the model's id: a wall, a beam or a column) and its kind ("wall", "beam", "column").</summary>
        public string Upper { get; set; }
        public string UpperKind { get; set; }

        /// <summary>The member below (null: the foundation, or nothing under it) and its kind.</summary>
        public string Lower { get; set; }
        public string LowerKind { get; set; }

        /// <summary>It anchors to the foundation: the force on the foundation is a reaction of the whole structure.</summary>
        public bool OnFoundation { get; set; }

        /// <summary>Something takes it: a member below, or the foundation.</summary>
        public bool Supported => OnFoundation || Lower != null;

        /// <summary>
        /// Between two members of the structure: its two forces are internal (equal and opposite, they cancel in the whole
        /// structure's free body). One on the foundation is not: it is a reaction.
        /// </summary>
        public bool Internal => Lower != null;

        /// <summary>Between two stories of one wall (a wall over two stories): inside that wall's own free body.</summary>
        public bool WithinMember => Lower != null && Lower == Upper;

        /// <summary>The level it is at (the level the member above stands on).</summary>
        public string LevelId => Source?.OntoLevelId;
        public Point2 Where => Source?.Where ?? new Point2(0, 0);

        /// <summary>Where its force acts in one case: the hold-down when it pulls, the end post's centreline when it presses.</summary>
        public Point2 WhereIn(string windCase) => Source?.WhereIn(windCase) ?? Where;

        /// <summary>Wind case → the connector's force (lb, W at strength level): + tension, − compression.</summary>
        public Dictionary<string, double> Force { get; set; } = new Dictionary<string, double>();

        /// <summary>The force on the member above in one case (lb, + up): the tension pulls it down.</summary>
        public double OnUpper(string windCase) => -Of(windCase);

        /// <summary>The force on the member below (or the foundation) in one case (lb, + up): the tension pulls it up.</summary>
        public double OnLower(string windCase) => Of(windCase);

        public double Of(string windCase) => windCase != null && Force != null && Force.TryGetValue(windCase, out var v) ? v : 0;

        /// <summary>The largest tension and compression over the cases (lb, both ≥ 0).</summary>
        public double MaxTension => Force == null || Force.Count == 0 ? 0 : Math.Max(0, Force.Values.Max());
        public double MaxCompression => Force == null || Force.Count == 0 ? 0 : Math.Max(0, -Force.Values.Min());
    }

    /// <summary>The connections of the lateral design as pairs of equal and opposite forces (<see cref="ConnectionPair"/>).</summary>
    public static class ConnectionPairs
    {
        /// <summary>
        /// Every story hold-down of <paramref name="lateral"/> as a pair: a wall's hold-down between its wall and what it
        /// anchors to; a beam end between its beam and what holds that end; a column base between its column and what is
        /// under it. Empty when there is no lateral design. A file saved before the cases were kept gives pairs with no forces.
        /// </summary>
        public static List<ConnectionPair> Of(LateralSection lateral)
        {
            var list = new List<ConnectionPair>();
            foreach (var h in lateral?.Holdowns ?? new List<StoryHoldown>())
            {
                var p = new ConnectionPair { Source = h, Force = h.Up == null ? new Dictionary<string, double>() : new Dictionary<string, double>(h.Up) };
                if (h.IsWall) { p.Upper = h.WallId; p.UpperKind = "wall"; }
                else
                {
                    // "beam B2 start", "column C1 base"
                    p.UpperKind = h.Kind == "beam end" ? "beam" : "column";
                    p.Upper = Second(h.From);
                }
                if (h.Onto == "the foundation") p.OnFoundation = true;
                else if (h.Onto != null)
                {
                    // "wall W3", "beam B2", "column C1"
                    var w = h.Onto.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (w.Length >= 2) { p.LowerKind = w[0]; p.Lower = w[1]; }
                }
                list.Add(p);
            }
            return list;
        }

        /// <summary>The pairs at one member: those it hangs from (its own, at its base) and those landing on it.</summary>
        public static IEnumerable<ConnectionPair> At(IEnumerable<ConnectionPair> pairs, string memberId) =>
            (pairs ?? Enumerable.Empty<ConnectionPair>()).Where(p => !p.WithinMember && (p.Upper == memberId || p.Lower == memberId));

        /// <summary>The force of the pairs on one member in one case (lb, + up): ΣFy of its connections.</summary>
        public static double On(IEnumerable<ConnectionPair> pairs, string memberId, string windCase) =>
            At(pairs, memberId).Sum(p => (p.Upper == memberId ? p.OnUpper(windCase) : 0) + (p.Lower == memberId ? p.OnLower(windCase) : 0));

        /// <summary>The wind cases the pairs carry, in their order.</summary>
        public static List<string> Cases(IEnumerable<ConnectionPair> pairs) =>
            (pairs ?? Enumerable.Empty<ConnectionPair>()).SelectMany(p => p.Force?.Keys ?? Enumerable.Empty<string>()).Distinct().ToList();

        private static string Second(string s)
        {
            var w = (s ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return w.Length >= 2 ? w[1] : s;
        }
    }
}
