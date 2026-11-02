using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingModel.Model;
using BuildingModel.Serialization;

namespace BuildingModel.Edits
{
    public enum ChangeKind { Added, Deleted, Changed }

    /// <summary>One object added, deleted or changed: its JSON before and after (null when it did not exist).</summary>
    [DataContract]
    public sealed class Change
    {
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public ChangeKind Kind { get; set; }
        [DataMember(Order = 2)] public string Type { get; set; }   // "level", "diaphragm", "opening", "wall", "note", "building"
        [DataMember(Order = 3)] public string Before { get; set; }
        [DataMember(Order = 4)] public string After { get; set; }

        public override string ToString() => $"{Type} {Id} {Kind.ToString().ToLowerInvariant()}";
    }

    /// <summary>A change that could not be applied: the model's object is not what the change set started from.</summary>
    public sealed class Conflict
    {
        public Change Change { get; set; }
        public string Reason { get; set; }
        public override string ToString() => $"{Change}: {Reason}";
    }

    /// <summary>
    /// The edits one app made to the building, against the model revision it started from (docs/DESIGN.md, the round
    /// trip). The model app applies a change set; a change whose object has since been changed in the model is a
    /// conflict and is left for the user to choose.
    /// </summary>
    [DataContract]
    public sealed class ChangeSet
    {
        public const string FormatName = "BuildingModelChanges";

        [DataMember(Order = 0)] public string Format { get; set; } = FormatName;
        [DataMember(Order = 1)] public int BaseRevision { get; set; }
        [DataMember(Order = 2)] public string Author { get; set; } = "";
        [DataMember(Order = 3)] public List<Change> Changes { get; set; } = new List<Change>();

        public bool IsEmpty => Changes == null || Changes.Count == 0;

        /// <summary>Every object added, deleted or changed from <paramref name="before"/> to <paramref name="after"/>, by ID.</summary>
        public static ChangeSet Between(Building before, Building after, int baseRevision = 0)
        {
            var a = Items(before);
            var b = Items(after);
            var cs = new ChangeSet { BaseRevision = baseRevision };
            foreach (var kv in b.Where(kv => !a.ContainsKey(kv.Key)))
                cs.Changes.Add(new Change { Id = kv.Key, Kind = ChangeKind.Added, Type = kv.Value.type, After = kv.Value.json });
            foreach (var kv in a.Where(kv => !b.ContainsKey(kv.Key)))
                cs.Changes.Add(new Change { Id = kv.Key, Kind = ChangeKind.Deleted, Type = kv.Value.type, Before = kv.Value.json });
            foreach (var kv in a.Where(kv => b.ContainsKey(kv.Key) && b[kv.Key].json != kv.Value.json))
                cs.Changes.Add(new Change { Id = kv.Key, Kind = ChangeKind.Changed, Type = kv.Value.type, Before = kv.Value.json, After = b[kv.Key].json });
            return cs;
        }

        /// <summary>
        /// Applies the changes to <paramref name="target"/>. A change is applied when the target's object is still what
        /// the change started from (or already what it ends as); otherwise it is a conflict and skipped, unless
        /// <paramref name="force"/>. Returns the conflicts.
        /// </summary>
        public List<Conflict> ApplyTo(Building target, bool force = false)
        {
            var conflicts = new List<Conflict>();
            var now = Items(target);
            foreach (var c in Changes ?? new List<Change>())
            {
                now.TryGetValue(c.Id, out var cur);
                string current = cur.json;
                if (current == c.After) continue; // already so
                if (!force && current != c.Before)
                {
                    conflicts.Add(new Conflict
                    {
                        Change = c,
                        Reason = c.Kind == ChangeKind.Added ? "the ID is already used in the model"
                               : current == null ? "it was deleted in the model"
                               : "it was changed in the model since",
                    });
                    continue;
                }
                Put(target, c.Id, c.Type, c.After);
            }
            return conflicts;
        }

        /// <summary>"W3 added, D1 changed", or "2 walls deleted"; "no change" when empty.</summary>
        public string Summary()
        {
            if (IsEmpty) return "no change";
            var parts = new List<string>();
            foreach (var g in Changes.GroupBy(c => (c.Kind, c.Type)).OrderBy(g => g.Key.Kind))
            {
                var ids = g.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
                string verb = g.Key.Kind.ToString().ToLowerInvariant();
                parts.Add(ids.Count <= 2 ? $"{string.Join(", ", ids)} {verb}" : $"{ids.Count} {g.Key.Type}s {verb}");
            }
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------------ file

        private static DataContractJsonSerializer Serializer() =>
            new DataContractJsonSerializer(typeof(ChangeSet), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    Serializer().WriteObject(w, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static ChangeSet FromJson(string json)
        {
            ChangeSet cs;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                    cs = (ChangeSet)Serializer().ReadObject(ms);
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException("This is not a building model change set: " + ex.Message, ex);
            }
            if (cs == null || cs.Format != FormatName) throw new InvalidDataException("This is not a building model change set.");
            cs.Changes = cs.Changes ?? new List<Change>();
            return cs;
        }

        // ------------------------------------------------------------------ objects as JSON

        private static Dictionary<string, (string type, string json)> Items(Building b)
        {
            var d = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (var o in b.Objects()) d[o.Id] = (BuildingEdits.KindName(o), ObjectJson(o));
            // the building's own name and data
            var head = new Building { Name = b.Name, Id = b.Id };
            foreach (var kv in b.Data) head.Data[kv.Key] = kv.Value;
            d["#building"] = ("building", ModelFile.BuildingToJson(head));
            return d;
        }

        private static string ObjectJson(ModelObject o)
        {
            var tmp = new Building { Id = "" };
            switch (o)
            {
                case Level l: tmp.Levels.Add(l); break;
                case Diaphragm dd: tmp.Diaphragms.Add(dd); break;
                case Opening op: tmp.Openings.Add(op); break;
                case Wall w: tmp.Walls.Add(w); break;
                case Note n: tmp.Notes.Add(n); break;
            }
            return ModelFile.BuildingToJson(tmp);
        }

        /// <summary>Puts the object (from its JSON) in the building in place of the one with the ID; null JSON deletes it.</summary>
        private static void Put(Building b, string id, string type, string json)
        {
            if (id == "#building")
            {
                var head = json == null ? null : ModelFile.BuildingFromJson(json);
                if (head == null) return;
                b.Name = head.Name;
                b.Data.Clear();
                foreach (var kv in head.Data) b.Data[kv.Key] = kv.Value;
                return;
            }
            var old = b.Find(id);
            ModelObject o = json == null ? null : ModelFile.BuildingFromJson(json).Objects().FirstOrDefault();
            void Swap<T>(List<T> list) where T : ModelObject
            {
                int i = old is T t ? list.IndexOf(t) : -1;
                if (o == null) { if (i >= 0) list.RemoveAt(i); return; }
                if (i >= 0) list[i] = (T)o; else list.Add((T)o);
            }
            switch (type)
            {
                case "level": Swap(b.Levels); break;
                case "diaphragm": Swap(b.Diaphragms); break;
                case "opening": Swap(b.Openings); break;
                case "wall": Swap(b.Walls); break;
                case "note": Swap(b.Notes); break;
            }
        }
    }
}
