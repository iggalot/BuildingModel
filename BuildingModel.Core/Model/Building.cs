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
    /// A beam in the framing of one level (format version 4), from Start to End in plan: it carries what stands on it from
    /// the story above (a hold-down or a post with no wall under it) to its two ends, which bear on the walls or columns
    /// of the story under the level. Drawn in the Building Model editor or the shear wall designer.
    /// </summary>
    [DataContract]
    public sealed class Beam : ModelObject
    {
        [DataMember(Order = 1)] public string LevelId { get; set; }
        [DataMember(Order = 2)] public Point2 Start { get; set; }
        [DataMember(Order = 3)] public Point2 End { get; set; }

        public double Length => new Segment2(Start, End).Length;
        public Point2 Middle => new Point2((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);
    }

    /// <summary>
    /// A column (post) at a point in plan (format version 4), from its base level to its top level: it carries what lands
    /// on it (a beam end, a hold-down) down to its base, onto the foundation at the ground or onto what is under it.
    /// </summary>
    [DataContract]
    public sealed class Column : ModelObject
    {
        [DataMember(Order = 1)] public Point2 At { get; set; }
        [DataMember(Order = 2)] public string BaseLevelId { get; set; }
        [DataMember(Order = 3)] public string TopLevelId { get; set; }
    }

    public enum RoofShape { Flat = 0, Gable = 1, Hip = 2 }

    /// <summary>Which way the ridge runs in plan. Auto: along the longer side (a hip roof always is).</summary>
    public enum RidgeAxis { Auto = 0, WestEast = 1, SouthNorth = 2 }

    /// <summary>
    /// The roof over the top story (format version 3): flat, gable or hip, its slope, which way the ridge runs, the
    /// parapet around a flat roof and the eaves (overhang past the wall line). A sloped roof covers the rectangle
    /// enclosing the top floor's diaphragms and rises from the top level (the eave).
    /// </summary>
    [DataContract]
    public sealed class Roof
    {
        [DataMember(Order = 1)] public RoofShape Shape { get; set; } = RoofShape.Flat;

        /// <summary>Slope (degrees from horizontal); a flat roof ignores it. A new roof takes the model's default (<see cref="ModelDefaults.RoofPitch"/>).</summary>
        [DataMember(Order = 2)] public double Pitch { get; set; }

        /// <summary>Gable only: which way the ridge runs (a hip's ridge is always along the longer side).</summary>
        [DataMember(Order = 3)] public RidgeAxis Ridge { get; set; } = RidgeAxis.Auto;

        /// <summary>Parapet height above a flat roof (ft); 0 for none.</summary>
        [DataMember(Order = 4)] public double Parapet { get; set; }

        /// <summary>Eaves: how far the roof reaches past the wall line (ft); 0 for none.</summary>
        [DataMember(Order = 5)] public double Overhang { get; set; }

        public bool IsSloped => Shape != RoofShape.Flat;

        public Roof Clone() => (Roof)MemberwiseClone();

        /// <summary>The pitch as rise in 12 in. of run, e.g. "4.0:12"; "-" for a flat roof or a bad slope.</summary>
        public string RisePer12 => IsSloped && Pitch > 0 && Pitch < 90 ? (12 * Math.Tan(Pitch * Math.PI / 180)).ToString("0.0") + ":12" : "-";
    }

    /// <summary>
    /// The project's defaults (format version 3), saved with the building so that every app reads the same values: what
    /// a new story, a new roof and a quick building start with. The values below are the only place the program's
    /// starting values are written; after that the file is the source.
    /// </summary>
    [DataContract]
    public sealed class ModelDefaults
    {
        /// <summary>A new story's height (ft).</summary>
        [DataMember(Order = 1)] public double StoryHeight { get; set; }

        /// <summary>The slope a roof gets when it is made gable or hip (degrees; 18.43° is 4:12).</summary>
        [DataMember(Order = 2)] public double RoofPitch { get; set; }

        /// <summary>A new roof's parapet (ft).</summary>
        [DataMember(Order = 3)] public double Parapet { get; set; }

        /// <summary>A new roof's eaves (ft).</summary>
        [DataMember(Order = 4)] public double Overhang { get; set; }

        /// <summary>"New from size": the plan it starts with, L W-E and B S-N (ft), and how many stories.</summary>
        [DataMember(Order = 5)] public double NewLength { get; set; }
        [DataMember(Order = 6)] public double NewWidth { get; set; }
        [DataMember(Order = 7)] public int NewStories { get; set; }

        public ModelDefaults() => Start();

        /// <summary>The program's starting values: the one place they are written. A value missing from a file keeps its starting value.</summary>
        private void Start()
        {
            StoryHeight = 15;
            RoofPitch = 18.43;
            Parapet = 0;
            Overhang = 0;
            NewLength = 60;
            NewWidth = 40;
            NewStories = 1;
        }

        [OnDeserializing]
        private void Starting(StreamingContext c) => Start();

        /// <summary>A flat roof with this project's parapet, eaves and (for when it is made sloped) slope.</summary>
        public Roof NewRoof() => new Roof { Shape = RoofShape.Flat, Pitch = RoofPitch, Parapet = Parapet, Overhang = Overhang };

        public ModelDefaults Clone() => (ModelDefaults)MemberwiseClone();
    }

    /// <summary>
    /// The building, described once for every analysis app: levels, diaphragms, openings, walls, notes, the roof and the
    /// project's defaults (ft; x east, y north). Geometry is edited here only; the apps key their own data to the IDs.
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

        /// <summary>The roof over the top story (a flat roof, no parapet, no eaves, in a file from before version 3). Never null.</summary>
        [DataMember(Order = 7)] public Roof Roof { get; set; }

        /// <summary>The project's defaults (the program's starting values in a file from before version 3). Never null.</summary>
        [DataMember(Order = 8)] public ModelDefaults Defaults { get; set; }

        /// <summary>Beams and columns (format version 4; empty in older files).</summary>
        [DataMember(Order = 9, EmitDefaultValue = false)] public List<Beam> Beams { get; set; } = new List<Beam>();
        [DataMember(Order = 10, EmitDefaultValue = false)] public List<Column> Columns { get; set; } = new List<Column>();

        public Building()
        {
            Defaults = new ModelDefaults();
            Roof = Defaults.NewRoof();
        }

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
            Beams = Beams ?? new List<Beam>();
            Columns = Columns ?? new List<Column>();
            Defaults = Defaults ?? new ModelDefaults();
            Roof = Roof ?? Defaults.NewRoof();
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

        /// <summary>Every level, diaphragm, opening, wall, note, beam and column.</summary>
        public IEnumerable<ModelObject> Objects() =>
            Levels.Cast<ModelObject>().Concat(Diaphragms).Concat(Openings).Concat(Walls).Concat(Notes ?? Enumerable.Empty<Note>())
                  .Concat(Beams ?? Enumerable.Empty<Beam>()).Concat(Columns ?? Enumerable.Empty<Column>());

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

        public Beam AddBeam(Level level, double x0, double y0, double x1, double y1)
        {
            var bm = new Beam { Id = NewId("BM"), LevelId = level.Id, Start = new Point2(x0, y0), End = new Point2(x1, y1) };
            Beams.Add(bm);
            return bm;
        }

        public Column AddColumn(Level baseLevel, Level top, double x, double y)
        {
            var c = new Column { Id = NewId("C"), At = new Point2(x, y), BaseLevelId = baseLevel.Id, TopLevelId = top.Id };
            Columns.Add(c);
            return c;
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
