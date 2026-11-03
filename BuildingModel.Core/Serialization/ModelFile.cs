using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingModel.Model;

namespace BuildingModel.Serialization
{
    /// <summary>The saved file: the format name and version, the revision (one more on every save), and the building.</summary>
    [DataContract]
    public sealed class ModelDocument
    {
        public const string FormatName = "BuildingModel";
        /// <summary>1: levels, diaphragms, openings, walls. 2: adds the data bags and the notes. 3: adds the roof (an older file reads as is, with a flat roof). 4: adds beams and columns (none in an older file). 5: adds wall stud framing (per wall and story) and the stud defaults (an older file takes 2x6 / 2x4 at 16", DF-L No. 2, L/240).</summary>
        public const int CurrentVersion = 5;

        [DataMember(Order = 0)] public string Format { get; set; } = FormatName;
        [DataMember(Order = 1)] public int Version { get; set; } = CurrentVersion;
        [DataMember(Order = 2)] public int Revision { get; set; }
        [DataMember(Order = 3)] public Building Building { get; set; }
    }

    /// <summary>JSON with the serializer built into .NET (no packages).</summary>
    public static class ModelFile
    {
        private static DataContractJsonSerializer Serializer() =>
            new DataContractJsonSerializer(typeof(ModelDocument), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static string ToJson(ModelDocument doc)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    Serializer().WriteObject(w, doc);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>Reads a model; refuses another format or a newer version than this reader knows.</summary>
        public static ModelDocument FromJson(string json)
        {
            ModelDocument doc;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                    doc = (ModelDocument)Serializer().ReadObject(ms);
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException("This is not a building model file: " + ex.Message, ex);
            }
            if (doc == null || doc.Format != ModelDocument.FormatName)
                throw new InvalidDataException("This is not a building model file.");
            if (doc.Version > ModelDocument.CurrentVersion)
                throw new InvalidDataException($"This building model is format version {doc.Version}; this program reads up to version {ModelDocument.CurrentVersion}. Update the program.");
            doc.Building = doc.Building ?? Building.New();
            return doc;
        }

        /// <summary>Saves with the revision one higher than the document's (which is updated).</summary>
        public static void Save(string path, ModelDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            doc.Revision++;
            try
            {
                File.WriteAllText(path, ToJson(doc), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error("File", "Could not save " + path, ex);
                throw;
            }
            var b = doc.Building;
            Diagnostics.Log.Info("File", $"Saved {path}: revision {doc.Revision}, {b?.Levels.Count ?? 0} levels, {b?.Walls.Count ?? 0} walls, {b?.Diaphragms.Count ?? 0} diaphragms" +
                                         (b == null || b.Data.Count == 0 ? "." : $"; sections {string.Join(", ", b.Data.Keys)}."));
        }

        public static ModelDocument Load(string path)
        {
            try
            {
                var doc = FromJson(File.ReadAllText(path));
                Diagnostics.Log.Info("File", $"Opened {path}: format {doc.Version}, revision {doc.Revision}, {doc.Building?.Levels.Count ?? 0} levels, {doc.Building?.Walls.Count ?? 0} walls.");
                return doc;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error("File", "Could not open " + path, ex);
                throw;
            }
        }

        private static DataContractJsonSerializer BuildingSerializer() =>
            new DataContractJsonSerializer(typeof(Building), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        /// <summary>The building alone as compact JSON (undo snapshots, change sets).</summary>
        public static string BuildingToJson(Building b)
        {
            using (var ms = new MemoryStream())
            {
                BuildingSerializer().WriteObject(ms, b);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static Building BuildingFromJson(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                return (Building)BuildingSerializer().ReadObject(ms);
        }

        /// <summary>A deep copy (through JSON).</summary>
        public static Building Clone(Building b) => BuildingFromJson(BuildingToJson(b));
    }
}
