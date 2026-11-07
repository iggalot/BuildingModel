using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BuildingModel.Model
{
    /// <summary>
    /// One vertical force the lateral system puts on the foundation at a point, per load case: a shear wall's end (its hold-down, or
    /// its compression post), a column or beam carrying hold-downs down. At the load's own level (W as the wind calculator gives it,
    /// strength level, not × 0.6), without any dead load: the load manager adds the dead load and combines.
    /// </summary>
    [DataContract]
    public sealed class LateralPoint
    {
        /// <summary>"SW1-2 end", "column C2 base".</summary>
        [DataMember(Order = 1)] public string From { get; set; }
        /// <summary>The model's wall it is the end of (null for a column or a beam end).</summary>
        [DataMember(Order = 2, EmitDefaultValue = false)] public string WallId { get; set; }
        [DataMember(Order = 3)] public double X { get; set; }
        [DataMember(Order = 4)] public double Y { get; set; }
        /// <summary>"Wind" (earthquake later).</summary>
        [DataMember(Order = 5)] public string Kind { get; set; } = "Wind";
        /// <summary>Load case → the force (lb): + up (uplift, the hold-down's), − down (bearing, the compression post's).</summary>
        [DataMember(Order = 6)] public Dictionary<string, double> Up { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The connector at the point, as the lateral design chose it ("HDU5-SDS2.5"): a hold-down at a wall end, a
        /// connection at a column base. Null: none chosen (none strong enough, or no uplift to hold).
        /// </summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public string Connector { get; set; }

        /// <summary>The largest uplift over the cases (lb, 0 when it only presses down).</summary>
        public double MaxUp => Up == null || Up.Count == 0 ? 0 : Math.Max(0, Up.Values.Max());

        public BuildingModel.Geometry.Point2 Where => new BuildingModel.Geometry.Point2(X, Y);
    }

    /// <summary>
    /// What the lateral design (the shear wall app) puts on the foundation: its own section of the project file ("Lateral"), written
    /// by the shear wall app each time it saves. The load manager reads its forces; every plan (the shared sketch) draws its points
    /// and connectors on the foundation plan. The format is here, in the building model, so no app reads another app's own code.
    /// </summary>
    [DataContract]
    public sealed class LateralSection
    {
        public const string Key = "Lateral";

        /// <summary>The app that wrote it ("Shear Wall Calculator").</summary>
        [DataMember(Order = 1)] public string Source { get; set; }
        /// <summary>When (UTC, ISO 8601).</summary>
        [DataMember(Order = 2)] public string Saved { get; set; }
        /// <summary>The model's revision it was found on (the file's revision after that save).</summary>
        [DataMember(Order = 3)] public int ModelRevision { get; set; }
        /// <summary>The load cases, in order: the wind calculator's own case names (the load manager takes everything else about
        /// each case, its direction and roof pressures, from the wind calculator). A "From" map in files saved before is ignored.</summary>
        [DataMember(Order = 4)] public List<string> Cases { get; set; } = new List<string>();
        [DataMember(Order = 5)] public List<LateralPoint> Points { get; set; } = new List<LateralPoint>();
        /// <summary>How the forces were found, in a sentence (shown with them).</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public string Note { get; set; }

        public bool IsEmpty => Points == null || Points.Count == 0;

        private static DataContractJsonSerializer Serializer() =>
            new DataContractJsonSerializer(typeof(LateralSection), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static LateralSection Read(Building b)
        {
            string json = b?.GetData(Key);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (LateralSection)Serializer().ReadObject(ms);
            }
            catch (SerializationException)
            {
                return null;
            }
        }

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>Writes it into the building's data (null or empty: removes it).</summary>
        public static void Write(Building b, LateralSection s) => b?.SetData(Key, s == null || s.IsEmpty ? null : s.ToJson());

        /// <summary>
        /// Why these forces may not match the model any more (null: they do): a wall they name is gone, or its end is not where the
        /// force is.
        /// </summary>
        public string Stale(Building b)
        {
            foreach (var p in Points ?? new List<LateralPoint>())
            {
                if (p.WallId == null) continue;
                var w = b?.Walls.FirstOrDefault(x => x.Id == p.WallId);
                if (w == null) return $"{p.From}: wall {p.WallId} is not in the model any more.";
                double d = Math.Min(Dist(w.Start, p.Where), Dist(w.End, p.Where));
                if (d > 1.5) return $"{p.From}: wall {p.WallId} has moved ({d:0.#} ft from its end).";
            }
            return null;
        }

        private static double Dist(BuildingModel.Geometry.Point2 a, BuildingModel.Geometry.Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
