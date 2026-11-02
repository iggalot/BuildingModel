using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>The four outside faces of the enclosing rectangle; the wind from the west loads the West face first.</summary>
    public enum Face { South, North, West, East }

    /// <summary>
    /// A piece of an outside face whose load goes to one level: <see cref="Length"/> ft along the face, from
    /// <see cref="Bottom"/> to <see cref="Top"/> ft high (Top = infinity: to the top of the wall, the roof).
    /// </summary>
    public sealed class FaceStrip
    {
        public Face Face { get; set; }
        public string LevelId { get; set; }
        public double From { get; set; }   // position along the face (x for South/North, y for West/East)
        public double Length { get; set; }
        public double Bottom { get; set; }
        public double Top { get; set; }
    }

    /// <summary>
    /// The building's outside as any lateral-load app needs it, worked out from the model alone: the enclosing rectangle
    /// (L W-E, B S-N), the levels with a diaphragm, their story heights, and how each outside face is shared between the
    /// levels whose diaphragm reaches it.
    /// </summary>
    public sealed class BuildingEnvelope
    {
        /// <summary>The enclosing rectangle of every level's diaphragm (L = width W-E, B = depth S-N).</summary>
        public Rect2 Bounds { get; set; }

        /// <summary>The levels above the ground with a diaphragm, bottom up (the last is the roof).</summary>
        public List<Level> Diaphragms { get; set; } = new List<Level>();

        public List<FaceStrip> Strips { get; set; } = new List<FaceStrip>();

        /// <summary>What a user of the envelope should know (model errors, a plan that is not a rectangle).</summary>
        public List<string> Notes { get; set; } = new List<string>();

        /// <summary>L: the enclosing rectangle W-E (ft).</summary>
        public double Length => Bounds.Width;

        /// <summary>B: the enclosing rectangle S-N (ft).</summary>
        public double Width => Bounds.Height;

        /// <summary>The top diaphragm's elevation (ft).</summary>
        public double EaveHeight => Diaphragms.Count == 0 ? 0 : Diaphragms[Diaphragms.Count - 1].Elevation;

        /// <summary>The story heights between the diaphragm levels, bottom up (ft).</summary>
        public List<double> StoryHeights()
        {
            var h = new List<double>();
            double z = 0;
            foreach (var l in Diaphragms) { h.Add(l.Elevation - z); z = l.Elevation; }
            return h;
        }

        /// <summary>
        /// Each level's share of a face's projected wall area (to the eave), including the ground's (into the foundation).
        /// The shares of one face add up to 1.
        /// </summary>
        public Dictionary<string, double> Shares(Face face, double eave)
        {
            var area = new Dictionary<string, double>();
            foreach (var s in Strips.Where(s => s.Face == face))
            {
                double a = s.Length * Math.Max(0, Math.Min(s.Top, eave) - s.Bottom);
                area[s.LevelId] = (area.TryGetValue(s.LevelId, out var v) ? v : 0) + a;
            }
            double total = area.Values.Sum();
            return area.ToDictionary(kv => kv.Key, kv => total > 0 ? kv.Value / total : 0);
        }
    }

    /// <summary>
    /// The envelope from the model: the enclosing rectangle gives L and B, the levels give the stories, and each outside
    /// face is shared between the levels whose diaphragm actually reaches it. A wall between two such levels sends half its
    /// height to each; where a floor stops short of the face (a double-height lobby at the façade) that wall spans to the
    /// next floor up or down. The ground takes the bottom half of the first span (into the foundation).
    /// </summary>
    public static class EnvelopeQueries
    {
        public const double FaceTolerance = BuildingQueries.WallTolerance;

        /// <summary>The envelope; throws when there is no ground or no diaphragm above it.</summary>
        public static BuildingEnvelope Envelope(this Building b)
        {
            if (b == null) throw new ArgumentNullException(nameof(b));
            var ground = b.Ground ?? throw new InvalidOperationException("The building has no ground level (elevation 0).");
            var levels = b.LevelsBottomUp.Where(l => l.Elevation > 0 && !b.GrossDiaphragm(l).IsEmpty).ToList();
            if (levels.Count == 0) throw new InvalidOperationException("The building has no diaphragm above the ground.");
            var footprint = b.Footprint();
            var box = footprint.Bounds;
            var env = new BuildingEnvelope { Bounds = box, Diaphragms = levels };

            foreach (var e in ModelChecks.Run(b).Where(i => i.Severity == Severity.Error))
                env.Notes.Add("Model error: " + e.Message);
            if (footprint.Area < box.Area - 1e-6)
                env.Notes.Add($"The plan is not a rectangle ({footprint.Area:0} of {box.Area:0} sq ft): the enclosing {box.Width:0.##} × {box.Height:0.##} ft rectangle " +
                              "is used for L and B (rectangular-building procedures applied by judgment).");

            foreach (Face f in Enum.GetValues(typeof(Face)))
                env.Strips.AddRange(FaceStrips(b, f, box, ground, levels));
            return env;
        }

        /// <summary>The strips of one face: split along the face wherever a level starts or stops reaching it.</summary>
        private static IEnumerable<FaceStrip> FaceStrips(Building b, Face face, Rect2 box, Level ground, List<Level> levels)
        {
            bool alongX = face == Face.South || face == Face.North;
            double line = face == Face.South ? box.Y0 : face == Face.North ? box.Y1 : face == Face.West ? box.X0 : box.X1;
            double s0 = alongX ? box.X0 : box.Y0, s1 = alongX ? box.X1 : box.Y1;
            var nets = levels.ToDictionary(l => l.Id, l => b.NetDiaphragm(l));

            // breakpoints: every rectangle edge across the face
            var rects = b.Diaphragms.SelectMany(d => d.Pieces).Concat(b.Openings.SelectMany(o => o.Pieces));
            var cuts = rects.SelectMany(r => alongX ? new[] { r.X0, r.X1 } : new[] { r.Y0, r.Y1 })
                            .Where(v => v > s0 && v < s1).Concat(new[] { s0, s1 }).Distinct().OrderBy(v => v).ToList();
            var top = levels[levels.Count - 1];

            var pieces = new List<FaceStrip>();
            for (int k = 0; k + 1 < cuts.Count; k++)
            {
                double m = (cuts[k] + cuts[k + 1]) / 2, len = cuts[k + 1] - cuts[k];
                var reach = new List<Level> { ground };
                reach.AddRange(levels.Where(l => alongX ? nets[l.Id].Contains(m, line, FaceTolerance) : nets[l.Id].Contains(line, m, FaceTolerance)));
                if (reach.Count == 1) continue; // nothing above the ground here (a notch in the plan)
                for (int i = 0; i < reach.Count; i++)
                {
                    double z = reach[i].Elevation;
                    double bottom = i == 0 ? 0 : (reach[i - 1].Elevation + z) / 2;
                    double up = i == reach.Count - 1 ? (reach[i] == top ? double.PositiveInfinity : z) : (z + reach[i + 1].Elevation) / 2;
                    pieces.Add(new FaceStrip { Face = face, LevelId = reach[i].Id, From = cuts[k], Length = len, Bottom = bottom, Top = up });
                }
            }
            // join neighbours that are the same strip
            var merged = new List<FaceStrip>();
            foreach (var p in pieces.OrderBy(p => p.LevelId, StringComparer.Ordinal).ThenBy(p => p.Bottom).ThenBy(p => p.From))
            {
                var last = merged.LastOrDefault();
                if (last != null && last.LevelId == p.LevelId && last.Bottom == p.Bottom && last.Top == p.Top && Math.Abs(last.From + last.Length - p.From) < 1e-9)
                    last.Length += p.Length;
                else merged.Add(p);
            }
            return merged;
        }
    }
}
