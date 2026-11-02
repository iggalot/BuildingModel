using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>
    /// Every object of the model has an ID that survives edits and saves, and a data bag (format version 2) where an
    /// app keeps its own data about the object, under its own key ("ShearWall", "Seismic"...). The model stores the
    /// text and never reads it.
    /// </summary>
    [DataContract]
    public abstract class ModelObject
    {
        [DataMember(Order = 0)] public string Id { get; set; }

        [DataMember(Name = "Data", Order = 100, EmitDefaultValue = false)]
        private Dictionary<string, string> data;

        /// <summary>The apps' data about this object, by app key. Never null.</summary>
        public Dictionary<string, string> Data => data ?? (data = new Dictionary<string, string>(StringComparer.Ordinal));

        /// <summary>The app's text, or null.</summary>
        public string GetData(string key) => data != null && data.TryGetValue(key, out var v) ? v : null;

        /// <summary>Sets the app's text (null or empty removes it).</summary>
        public void SetData(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) data?.Remove(key);
            else Data[key] = value;
        }

        [OnSerializing]
        private void DropEmptyData(StreamingContext c)
        {
            if (data != null && data.Count == 0) data = null;
        }
    }

    /// <summary>A named elevation where a diaphragm can be (ft above the base). The ground level is at 0.</summary>
    [DataContract]
    public sealed class Level : ModelObject
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public double Elevation { get; set; }

        public override string ToString() => $"{Name} ({Elevation:0.##} ft)";
    }

    /// <summary>A floor or roof on one level: rectangles that merge into one outline (it can cover part of the footprint).</summary>
    [DataContract]
    public sealed class Diaphragm : ModelObject
    {
        [DataMember(Order = 1)] public string LevelId { get; set; }
        [DataMember(Order = 2)] public List<Rect2> Pieces { get; set; } = new List<Rect2>();
    }

    /// <summary>
    /// A hole through the floors, drawn once: open from the bottom level to the top level, it cuts the diaphragm of every
    /// level strictly between them (a lobby from the ground to the roof cuts every floor in between).
    /// </summary>
    [DataContract]
    public sealed class Opening : ModelObject
    {
        [DataMember(Order = 1)] public string BottomLevelId { get; set; }
        [DataMember(Order = 2)] public string TopLevelId { get; set; }
        [DataMember(Order = 3)] public List<Rect2> Pieces { get; set; } = new List<Rect2>();
    }

    public enum WallDirection { WestEast, SouthNorth, NotOrthogonal }

    /// <summary>A wall in plan (W-E or S-N) from its base level to its top level; it can span several levels.</summary>
    [DataContract]
    public sealed class Wall : ModelObject
    {
        [DataMember(Order = 1)] public Point2 Start { get; set; }
        [DataMember(Order = 2)] public Point2 End { get; set; }
        [DataMember(Order = 3)] public string BaseLevelId { get; set; }
        [DataMember(Order = 4)] public string TopLevelId { get; set; }
        [DataMember(Order = 5)] public bool IsShearWall { get; set; } = true;

        public double Length => new Segment2(Start, End).Length;

        public Point2 Middle => new Point2((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);

        public WallDirection Direction =>
            !(Length > 0) ? WallDirection.NotOrthogonal
            : Start.Y == End.Y ? WallDirection.WestEast
            : Start.X == End.X ? WallDirection.SouthNorth
            : WallDirection.NotOrthogonal;
    }

    /// <summary>A note placed on the plan of one level (format version 2): a reminder, a question, an assumption.</summary>
    [DataContract]
    public sealed class Note : ModelObject
    {
        [DataMember(Order = 1)] public string LevelId { get; set; }
        [DataMember(Order = 2)] public Point2 At { get; set; }
        [DataMember(Order = 3)] public string Text { get; set; } = "";
    }

    /// <summary>
    /// The building, described once for every analysis app: levels, diaphragms, openings, walls and notes (ft; x east,
    /// y north). Geometry is edited here only; the apps key their own data to the IDs.
    /// </summary>
    [DataContract]
    public sealed class Building : ModelObject
    {
        [DataMember(Order = 1)] public string Name { get; set; } = "";
        [DataMember(Order = 2)] public List<Level> Levels { get; set; } = new List<Level>();
        [DataMember(Order = 3)] public List<Diaphragm> Diaphragms { get; set; } = new List<Diaphragm>();
        [DataMember(Order = 4)] public List<Opening> Openings { get; set; } = new List<Opening>();
        [DataMember(Order = 5)] public List<Wall> Walls { get; set; } = new List<Wall>();
        [DataMember(Order = 6, EmitDefaultValue = false)] public List<Note> Notes { get; set; } = new List<Note>();

        /// <summary>A building with only the ground level.</summary>
        public static Building New(string name = "")
        {
            var b = new Building { Name = name, Id = "B" };
            b.Levels.Add(new Level { Id = b.NewId("L"), Name = "Ground", Elevation = 0 });
            return b;
        }

        /// <summary>The serializer does not run constructors: a file without a list (version 1 has no notes) gets an empty one.</summary>
        [OnDeserialized]
        private void Complete(StreamingContext c)
        {
            if (string.IsNullOrEmpty(Id)) Id = "B";
            Levels = Levels ?? new List<Level>();
            Diaphragms = Diaphragms ?? new List<Diaphragm>();
            Openings = Openings ?? new List<Opening>();
            Walls = Walls ?? new List<Wall>();
            Notes = Notes ?? new List<Note>();
            foreach (var d in Diaphragms) d.Pieces = d.Pieces ?? new List<Rect2>();
            foreach (var o in Openings) o.Pieces = o.Pieces ?? new List<Rect2>();
            Name = Name ?? "";
        }

        /// <summary>An ID not used in this building: the prefix and the next number (L1, D3, W12...).</summary>
        public string NewId(string prefix)
        {
            var used = new HashSet<string>(AllIds(), StringComparer.Ordinal);
            for (int n = 1; ; n++)
                if (!used.Contains(prefix + n)) return prefix + n;
        }

        public IEnumerable<string> AllIds() => Objects().Select(o => o.Id);

        /// <summary>Every level, diaphragm, opening, wall and note.</summary>
        public IEnumerable<ModelObject> Objects() =>
            Levels.Cast<ModelObject>().Concat(Diaphragms).Concat(Openings).Concat(Walls).Concat(Notes ?? Enumerable.Empty<Note>());

        /// <summary>The object with this ID (the building itself for its own ID), or null.</summary>
        public ModelObject Find(string id) => id == null ? null : id == Id ? this : Objects().FirstOrDefault(o => o.Id == id);

        // ------------------------------------------------------------------ editing helpers (each returns what it added)

        public Level AddLevel(string name, double elevation)
        {
            var l = new Level { Id = NewId("L"), Name = name, Elevation = elevation };
            Levels.Add(l);
            return l;
        }

        public Diaphragm AddDiaphragm(Level level, params Rect2[] pieces)
        {
            var d = new Diaphragm { Id = NewId("D"), LevelId = level.Id, Pieces = pieces.ToList() };
            Diaphragms.Add(d);
            return d;
        }

        public Opening AddOpening(Level bottom, Level top, params Rect2[] pieces)
        {
            var o = new Opening { Id = NewId("O"), BottomLevelId = bottom.Id, TopLevelId = top.Id, Pieces = pieces.ToList() };
            Openings.Add(o);
            return o;
        }

        public Wall AddWall(Level baseLevel, Level top, double x0, double y0, double x1, double y1, bool shearWall = true)
        {
            var w = new Wall { Id = NewId("W"), Start = new Point2(x0, y0), End = new Point2(x1, y1), BaseLevelId = baseLevel.Id, TopLevelId = top.Id, IsShearWall = shearWall };
            Walls.Add(w);
            return w;
        }

        public Note AddNote(Level level, double x, double y, string text)
        {
            var n = new Note { Id = NewId("N"), LevelId = level.Id, At = new Point2(x, y), Text = text ?? "" };
            Notes.Add(n);
            return n;
        }

        // ------------------------------------------------------------------ lookups

        public Level Level(string id) => Levels.FirstOrDefault(l => l.Id == id);

        /// <summary>The ground level (elevation 0), or null.</summary>
        public Level Ground => Levels.FirstOrDefault(l => l.Elevation == 0);

        /// <summary>The levels from the bottom up.</summary>
        public List<Level> LevelsBottomUp => Levels.OrderBy(l => l.Elevation).ToList();

        /// <summary>The next level down, or null at the bottom.</summary>
        public Level Below(Level l) => l == null ? null : Levels.Where(x => x.Elevation < l.Elevation).OrderByDescending(x => x.Elevation).FirstOrDefault();

        /// <summary>The next level up, or null at the top.</summary>
        public Level Above(Level l) => l == null ? null : Levels.Where(x => x.Elevation > l.Elevation).OrderBy(x => x.Elevation).FirstOrDefault();
    }
}
