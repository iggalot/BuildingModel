using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BuildingModel.Model;

namespace BuildingModel.Serialization
{
    /// <summary>
    /// "What a result was based on": short fingerprints of the parts of a project a saved result used (the geometry, the
    /// wind's inputs, a design's inputs). A result stores them when it is saved; comparing them with the project as it is
    /// now says exactly which input has changed since (<see cref="Changed"/>), so a result is out of date only when what
    /// it used changed, not when anything at all was saved.
    /// </summary>
    public static class ModelFingerprint
    {
        /// <summary>The usual part names.</summary>
        public const string GeometryPart = "geometry";

        /// <summary>A short fingerprint of a text ("" for none).</summary>
        public static string Of(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            using (var sha = SHA1.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return string.Concat(h.Take(8).Select(x => x.ToString("x2")));
            }
        }

        /// <summary>The building's geometry: everything but the apps' data, the notes and the building's name.</summary>
        public static string Geometry(Building b)
        {
            if (b == null) return "";
            var c = ModelFile.Clone(b);
            c.Name = "";
            c.Notes?.Clear();
            return Of(ModelMerge.GeometryJson(c));
        }

        /// <summary>One section of the building's own data (e.g. "Wind").</summary>
        public static string Data(Building b, string key) => Of(b?.GetData(key));

        /// <summary>One app's data under <paramref name="key"/> on every object of the building (by ID), with its building keys.</summary>
        public static string AppData(Building b, string objectKey, params string[] buildingKeys)
        {
            if (b == null) return "";
            var sb = new StringBuilder();
            foreach (var k in buildingKeys ?? new string[0]) sb.Append(k).Append('\u0002').Append(b.GetData(k)).Append('\u0001');
            var objects = b.Levels.Cast<ModelObject>().Concat(b.Diaphragms).Concat(b.Openings).Concat(b.Walls)
                           .Concat(b.Beams ?? new List<Beam>()).Concat(b.Columns ?? new List<Column>());
            foreach (var o in objects.Where(o => o != null).OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                string v = o.GetData(objectKey);
                if (v != null) sb.Append(o.Id).Append('\u0002').Append(v).Append('\u0001');
            }
            return Of(sb.ToString());
        }

        /// <summary>The part name of one section of the building's data ("data:Wind"): any app can find its fingerprint again (<see cref="Recompute"/>).</summary>
        public static string DataPart(string key) => "data:" + key;

        /// <summary>
        /// A part's fingerprint found again from the model alone: the geometry, or a data section (<see cref="DataPart"/>).
        /// Null for a part only its app can find (an app's own design inputs).
        /// </summary>
        public static string Recompute(Building b, string part) =>
            part == GeometryPart ? Geometry(b) : part != null && part.StartsWith("data:", StringComparison.Ordinal) ? Data(b, part.Substring(5)) : null;

        /// <summary>
        /// The parts of <paramref name="basedOn"/> that any app can find again (<see cref="Recompute"/>) and that differ now:
        /// for an app that reads a result without its maker's code (the Load Manager reading the shear walls' forces).
        /// Null when there is no <paramref name="basedOn"/>.
        /// </summary>
        public static List<string> ChangedFromModel(IDictionary<string, string> basedOn, Building b)
        {
            if (basedOn == null || basedOn.Count == 0) return null;
            return basedOn.Where(kv => Recompute(b, kv.Key) is string now && now != kv.Value).Select(kv => kv.Key).ToList();
        }

        /// <summary>"the building", "the Wind inputs", or the part's own name.</summary>
        public static string Words(string part) =>
            part == GeometryPart ? "the building" : part != null && part.StartsWith("data:", StringComparison.Ordinal) ? $"the {part.Substring(5).ToLowerInvariant()} inputs" : part;

        /// <summary>
        /// The parts whose fingerprint differs between <paramref name="basedOn"/> (stored with a result) and
        /// <paramref name="now"/>, in <paramref name="now"/>'s order. A part missing on either side counts as changed;
        /// no <paramref name="basedOn"/> at all (a result saved before fingerprints) gives null: unknown.
        /// </summary>
        public static List<string> Changed(IDictionary<string, string> basedOn, IDictionary<string, string> now)
        {
            if (basedOn == null || basedOn.Count == 0) return null;
            var list = new List<string>();
            foreach (var kv in now ?? new Dictionary<string, string>())
                if (!basedOn.TryGetValue(kv.Key, out var was) || was != kv.Value) list.Add(kv.Key);
            return list;
        }
    }
}
