using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BuildingModel.Model
{
    /// <summary>One problem the hub found: a step out of date or failing, or one failing wall or hold-down.</summary>
    [DataContract]
    public sealed class StatusItem
    {
        /// <summary>The step it belongs to ("Shear walls").</summary>
        [DataMember(Order = 1)] public string Step { get; set; }
        /// <summary>"Fails", "OutOfDate", "Blocked", "Attention".</summary>
        [DataMember(Order = 2)] public string State { get; set; }
        [DataMember(Order = 3)] public string Message { get; set; }
        /// <summary>When the hub first saw it (UTC, ISO 8601).</summary>
        [DataMember(Order = 4)] public string Since { get; set; }
        /// <summary>Whose hub first saw it ("jim on DESK-1").</summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public string By { get; set; }
        /// <summary>The model object it is about (a wall), to go to it; null for a whole step.</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public string Id { get; set; }

        public DateTime SinceUtc => DateTime.TryParse(Since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

        /// <summary>The same problem: its step, state and words.</summary>
        public string Key => Step + "\u0001" + State + "\u0001" + Message;
    }

    /// <summary>
    /// The project's open problems, marked in the project file itself (its "Hub.Status" section) so whoever opens it next
    /// sees them at once: a change made in one app (the building, the wind) that was not carried through the others (the
    /// shear walls fail or are out of date, the foundation loads with them). Written by the hub only, and only while no app
    /// has the project (so it never races an app's save); every item keeps when it was first seen and by whom. The format is
    /// here so every app reads it without reading the hub (the apps show the items about them on opening).
    /// </summary>
    [DataContract]
    public sealed class ProjectStatus
    {
        public const string Key = "Hub.Status";

        /// <summary>When the hub last checked (UTC, ISO 8601).</summary>
        [DataMember(Order = 1)] public string Checked { get; set; }
        [DataMember(Order = 2)] public List<StatusItem> Items { get; set; } = new List<StatusItem>();

        public bool IsClear => Items == null || Items.Count == 0;

        /// <summary>The earliest problem still open.</summary>
        public StatusItem Oldest => (Items ?? new List<StatusItem>()).OrderBy(i => i.SinceUtc).FirstOrDefault();

        /// <summary>The items of one step ("Shear walls").</summary>
        public List<StatusItem> Of(string step) => (Items ?? new List<StatusItem>()).Where(i => i.Step == step).ToList();

        /// <summary>
        /// What the hub marked for these steps, in words for an app opening the project (null: nothing): one line each, the
        /// oldest first, with when it was first seen and by whom.
        /// </summary>
        public string Words(params string[] steps)
        {
            var items = (Items ?? new List<StatusItem>()).Where(i => steps == null || steps.Length == 0 || steps.Contains(i.Step)).OrderBy(i => i.SinceUtc).ToList();
            if (items.Count == 0) return null;
            string When(StatusItem i) => i.SinceUtc == DateTime.MinValue ? "" : $" (since {i.SinceUtc.ToLocalTime():yyyy-MM-dd HH:mm}" + (string.IsNullOrEmpty(i.By) ? ")" : $", {i.By})");
            return $"The hub marked {(items.Count == 1 ? "a problem" : items.Count + " problems")} here when it last checked the project:\n" +
                   string.Join("\n", items.Select(i => "• " + i.Message + When(i)));
        }

        /// <summary>
        /// These problems now, each keeping when it was first seen (and by whom) from <paramref name="previous"/>; new ones
        /// seen now by <paramref name="by"/>.
        /// </summary>
        public static ProjectStatus Stamped(IEnumerable<StatusItem> items, ProjectStatus previous, DateTime nowUtc, string by)
        {
            var list = (items ?? Enumerable.Empty<StatusItem>()).ToList();
            var was = (previous?.Items ?? new List<StatusItem>()).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
            string now = nowUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            foreach (var i in list)
            {
                if (was.TryGetValue(i.Key, out var old)) { i.Since = old.Since; i.By = old.By; }
                else { i.Since = now; i.By = by; }
            }
            return new ProjectStatus { Checked = now, Items = list };
        }

        /// <summary>The same problems (when they were seen and checked aside).</summary>
        public bool SameAs(ProjectStatus other)
        {
            var a = (Items ?? new List<StatusItem>()).Select(i => i.Key + "\u0001" + i.Since).OrderBy(x => x, StringComparer.Ordinal);
            var b = (other?.Items ?? new List<StatusItem>()).Select(i => i.Key + "\u0001" + i.Since).OrderBy(x => x, StringComparer.Ordinal);
            return a.SequenceEqual(b);
        }

        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(ProjectStatus));

        public static ProjectStatus Read(Building b)
        {
            string json = b?.GetData(Key);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (ProjectStatus)Serializer().ReadObject(ms);
            }
            catch (SerializationException) { return null; }
        }

        /// <summary>Into the building's data; clear: the section is removed.</summary>
        public static void Write(Building b, ProjectStatus s)
        {
            if (b == null) return;
            if (s == null || s.IsClear) { b.SetData(Key, null); return; }
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, s);
                b.SetData(Key, Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
    }
}
