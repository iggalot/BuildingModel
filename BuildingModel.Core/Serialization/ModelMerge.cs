using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingModel.Model;

namespace BuildingModel.Serialization
{
    /// <summary>What a merged save did: whether another app had saved the file meanwhile, what was taken from it, and the conflicts.</summary>
    public sealed class MergeReport
    {
        /// <summary>True when the file had changed since this app read it (another app saved): the building was merged.</summary>
        public bool FileChanged { get; set; }

        /// <summary>True when geometry (not only the apps' data) was taken from the file: the app should show the merged building.</summary>
        public bool GeometryTaken { get; set; }

        /// <summary>Changes another app saved meanwhile, kept ("wall W3 (the file's)", "Wind (the file's)").</summary>
        public List<string> TakenFromFile { get; } = new List<string>();

        /// <summary>Things both changed differently: this app's version was kept (the other app's change is lost).</summary>
        public List<string> Conflicts { get; } = new List<string>();

        /// <summary>One line for the log and the status bar, or null when nothing was merged.</summary>
        public string Summary()
        {
            if (!FileChanged) return null;
            var s = new StringBuilder("Another app saved the project meanwhile; its changes were kept");
            if (TakenFromFile.Count > 0) s.Append(" (").Append(string.Join(", ", TakenFromFile)).Append(')');
            s.Append('.');
            if (Conflicts.Count > 0) s.Append(" Both changed: ").Append(string.Join(", ", Conflicts)).Append(" (this app's version was saved).");
            return s.ToString();
        }
    }

    /// <summary>
    /// The three-way merge that lets several apps save one project file without undoing each other's work. Each app keeps
    /// the building as it read it (the base). At save, the file is read again: what only the other app changed (an object,
    /// or one app's data key on it) is taken from the file; what only this app changed is this app's; what both changed is
    /// a conflict, and this app's version wins (reported). Objects are matched by ID, the apps' data per key.
    /// </summary>
    public static class ModelMerge
    {
        /// <summary>Merges <paramref name="mine"/> (base + this app's changes) with <paramref name="theirs"/> (the file now). The inputs are not changed.</summary>
        public static Building Merge(Building baseB, Building mine, Building theirs, MergeReport report = null, Func<string, bool> owns = null)
        {
            report = report ?? new MergeReport();
            if (baseB == null || theirs == null) return ModelFile.Clone(mine);
            bool changed = ModelFile.BuildingToJson(baseB) != ModelFile.BuildingToJson(theirs);
            if (!changed && owns == null) return ModelFile.Clone(mine); // nobody else saved
            report.FileChanged = changed;

            // the building's own values (name, roof, defaults), without its lists and its data
            string sb = BuildingShape(baseB), sm = BuildingShape(mine), st = BuildingShape(theirs);
            Building result;
            if (sm == st || sm != sb && st == sb) result = ShellOf(mine);
            else if (sm == sb) { result = ShellOf(theirs); report.TakenFromFile.Add("the building's name, roof or defaults (the file's)"); report.GeometryTaken = true; }
            else { result = ShellOf(mine); report.Conflicts.Add("the building's name, roof or defaults"); }
            MergeData("the building", baseB, mine, theirs, result, report, owns);

            result.Levels = MergeList("level", baseB.Levels, mine.Levels, theirs.Levels, report, owns);
            result.Diaphragms = MergeList("diaphragm", baseB.Diaphragms, mine.Diaphragms, theirs.Diaphragms, report, owns);
            result.Openings = MergeList("opening", baseB.Openings, mine.Openings, theirs.Openings, report, owns);
            result.Walls = MergeList("wall", baseB.Walls, mine.Walls, theirs.Walls, report, owns);
            result.Notes = MergeList("note", baseB.Notes, mine.Notes, theirs.Notes, report, owns);
            result.Beams = MergeList("beam", baseB.Beams, mine.Beams, theirs.Beams, report, owns);
            result.Columns = MergeList("column", baseB.Columns, mine.Columns, theirs.Columns, report, owns);
            return result;
        }

        private static List<T> MergeList<T>(string kind, List<T> b, List<T> m, List<T> t, MergeReport report, Func<string, bool> owns) where T : ModelObject
        {
            Dictionary<string, T> Map(List<T> l) => (l ?? new List<T>()).Where(x => x?.Id != null).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var bm = Map(b); var mm = Map(m); var tm = Map(t);
            // the file's order, then what only this app has, in its order
            var ids = (t ?? new List<T>()).Select(x => x?.Id).Concat((m ?? new List<T>()).Select(x => x?.Id)).Where(id => id != null).Distinct().ToList();
            var result = new List<T>();
            foreach (string id in ids)
            {
                bm.TryGetValue(id, out var B); mm.TryGetValue(id, out var M); tm.TryGetValue(id, out var F);
                string sb = Shape(B), sm = Shape(M), st = Shape(F);
                T chosen;
                if (sm == st) chosen = M ?? F;
                else if (sm == sb) { chosen = F; report.GeometryTaken = true; report.TakenFromFile.Add(F == null ? $"{kind} {id} deleted (the file's)" : $"{kind} {id} (the file's)"); }
                else if (st == sb) chosen = M;
                else { chosen = M; report.Conflicts.Add(M == null ? $"{kind} {id} (deleted here, changed there)" : $"{kind} {id}"); }
                if (chosen == null)
                {
                    if (M != null && DataJson(M) != DataJson(B)) report.Conflicts.Add($"the data on {kind} {id}, which the other app deleted");
                    continue;
                }
                var x = Clone(chosen);
                MergeData($"{kind} {id}", B, M, F, x, report, owns);
                result.Add(x);
            }
            return result;
        }

        /// <summary>The apps' data on one object, key by key: each key three-way, as the objects are.</summary>
        private static void MergeData(string what, ModelObject b, ModelObject m, ModelObject t, ModelObject into, MergeReport report, Func<string, bool> owns)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in new[] { b, m, t }) if (o != null) keys.UnionWith(o.Data.Keys);
            into.Data.Clear();
            foreach (string k in keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                string vb = b?.GetData(k), vm = m?.GetData(k), vt = t?.GetData(k);
                string v;
                if (owns != null && !owns(k)) v = t != null ? vt : vm; // not this app's: the file's (an object only this app has keeps its own)
                else if (vm == vt) v = vm;
                else if (vm == vb) { v = vt; report.TakenFromFile.Add(what == "the building" ? $"{k} (the file's)" : $"{k} on {what} (the file's)"); }
                else if (vt == vb) v = vm;
                else { v = vm; report.Conflicts.Add(what == "the building" ? k : $"{k} on {what}"); }
                into.SetData(k, v);
            }
        }

        /// <summary>
        /// An app that writes only its own data (no geometry): copies the keys it <paramref name="owns"/> from
        /// <paramref name="mine"/> onto <paramref name="latest"/> (the file as it is now), on the building and on every object
        /// by ID; an owned key that <paramref name="mine"/> no longer has is removed. Everything else stays the file's.
        /// Returns the objects of <paramref name="mine"/> with owned data that the file no longer has (their data is dropped).
        /// </summary>
        public static List<string> CopyOwnData(Building mine, Building latest, Func<string, bool> owns)
        {
            var lost = new List<string>();
            void Copy(ModelObject from, ModelObject to)
            {
                foreach (string k in to.Data.Keys.Where(owns).ToList()) to.SetData(k, null);
                if (from != null) foreach (var kv in from.Data.Where(kv => owns(kv.Key))) to.SetData(kv.Key, kv.Value);
            }
            Copy(mine, latest);
            void Each<T>(string kind, List<T> m, List<T> l) where T : ModelObject
            {
                var byId = (m ?? new List<T>()).Where(x => x?.Id != null).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
                foreach (var x in l ?? new List<T>()) { byId.TryGetValue(x.Id ?? "", out var from); Copy(from, x); byId.Remove(x.Id ?? ""); }
                lost.AddRange(byId.Values.Where(x => x.Data.Keys.Any(owns)).Select(x => $"{kind} {x.Id}"));
            }
            Each("level", mine.Levels, latest.Levels);
            Each("diaphragm", mine.Diaphragms, latest.Diaphragms);
            Each("opening", mine.Openings, latest.Openings);
            Each("wall", mine.Walls, latest.Walls);
            Each("note", mine.Notes, latest.Notes);
            Each("beam", mine.Beams, latest.Beams);
            Each("column", mine.Columns, latest.Columns);
            return lost;
        }

        /// <summary>The building as JSON without any app's data: two buildings with the same geometry give the same text.</summary>
        public static string GeometryJson(Building b)
        {
            var c = ModelFile.Clone(b);
            c.Data.Clear();
            foreach (var o in c.Levels.Cast<ModelObject>().Concat(c.Diaphragms).Concat(c.Openings).Concat(c.Walls).Concat(c.Notes ?? new List<Note>())
                               .Concat(c.Beams ?? new List<Beam>()).Concat(c.Columns ?? new List<Column>()))
                o?.Data.Clear();
            return ModelFile.BuildingToJson(c);
        }

        // ------------------------------------------------------------------ shapes: an object as JSON without the apps' data

        private static string Shape(ModelObject o)
        {
            if (o == null) return null;
            var c = Clone(o);
            c.Data.Clear();
            return Json(c);
        }

        private static string DataJson(ModelObject o) =>
            o == null ? "" : string.Join("\u0001", o.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "\u0002" + kv.Value));

        private static string BuildingShape(Building b)
        {
            var s = ShellOf(b);
            s.Data.Clear();
            return ModelFile.BuildingToJson(s);
        }

        /// <summary>The building's own values (name, roof, defaults, its data) with empty lists.</summary>
        private static Building ShellOf(Building b)
        {
            var c = ModelFile.Clone(b);
            c.Levels = new List<Level>(); c.Diaphragms = new List<Diaphragm>(); c.Openings = new List<Opening>(); c.Walls = new List<Wall>();
            c.Notes = new List<Note>(); c.Beams = new List<Beam>(); c.Columns = new List<Column>();
            return c;
        }

        private static DataContractJsonSerializer SerializerFor(Type t) =>
            new DataContractJsonSerializer(t, new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        private static string Json(ModelObject o)
        {
            using (var ms = new MemoryStream())
            {
                SerializerFor(o.GetType()).WriteObject(ms, o);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static T Clone<T>(T o) where T : ModelObject
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(Json(o))))
                return (T)SerializerFor(o.GetType()).ReadObject(ms);
        }
    }
}
