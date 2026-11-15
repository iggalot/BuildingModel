using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingModel.Model;

namespace BuildingModel.Checks
{
    /// <summary>
    /// One saved view of a plan (owner 2026-11-14, UI audit A2): its name, the mode it was in, the layers on and off, the
    /// label density and the status filter. Saved with the project in the app's own key (<see cref="SavedViews"/>), so it
    /// travels with the project ("My hold-down check").
    /// </summary>
    [DataContract]
    public sealed class SavedView
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2, EmitDefaultValue = false)] public string Mode { get; set; }
        [DataMember(Order = 3)] public List<string> On { get; set; } = new List<string>();
        [DataMember(Order = 4)] public List<string> Off { get; set; } = new List<string>();
        /// <summary>The label density (BuildingModel.Sketch's LabelDensity: 0 off, 1 critical, 2 normal, 3 all).</summary>
        [DataMember(Order = 5)] public int Labels { get; set; } = 1;
        /// <summary>The status filter (BuildingModel.Sketch's StatusFilter: 0 all, 1 hide passing, 2 problems only).</summary>
        [DataMember(Order = 6)] public int Status { get; set; }
        /// <summary>The level shown, by id (null: as it is).</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public string LevelId { get; set; }
    }

    /// <summary>An app's saved views, in the project under "Views.&lt;app&gt;" (the app's own data: it alone writes it).</summary>
    [DataContract]
    public sealed class SavedViews
    {
        public static string Key(string app) => "Views." + app;

        [DataMember(Order = 1)] public List<SavedView> Views { get; set; } = new List<SavedView>();

        public bool IsEmpty => Views == null || Views.Count == 0;

        public SavedView Find(string name) => Views?.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Adds the view, or replaces the one with its name.</summary>
        public void Put(SavedView v)
        {
            if (v == null || string.IsNullOrWhiteSpace(v.Name)) return;
            Views = Views ?? new List<SavedView>();
            Views.RemoveAll(x => string.Equals(x.Name, v.Name, StringComparison.OrdinalIgnoreCase));
            Views.Add(v);
        }

        public bool Remove(string name) => (Views?.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ?? 0) > 0;

        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(SavedViews));

        /// <summary>The app's views (none when it has none, or they cannot be read).</summary>
        public static SavedViews Read(Building b, string app)
        {
            string json = b?.GetData(Key(app));
            if (string.IsNullOrEmpty(json)) return new SavedViews();
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (SavedViews)Serializer().ReadObject(ms) ?? new SavedViews();
            }
            catch (SerializationException) { return new SavedViews(); }
        }

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>Writes them into the building's data (none: removes the key).</summary>
        public static void Write(Building b, string app, SavedViews s) => b?.SetData(Key(app), s == null || s.IsEmpty ? null : s.ToJson());
    }
}
