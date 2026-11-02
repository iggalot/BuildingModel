using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>A named elevation where a diaphragm can be (ft above the base). The ground level is at 0.</summary>
    [DataContract]
    public sealed class Level
    {
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public double Elevation { get; set; }

        public override string ToString() => $"{Name} ({Elevation:0.##} ft)";
    }

    /// <summary>A floor or roof on one level: rectangles that merge into one outline (it can cover part of the footprint).</summary>
    [DataContract]
    public sealed class Diaphragm
    {
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public string LevelId { get; set; }
        [DataMember(Order = 2)] public List<Rect2> Pieces { get; set; } = new List<Rect2>();
    }

    /// <summary>
    /// A hole through the floors, drawn once: open from the bottom level to the top level, it cuts the diaphragm of every
    /// level strictly between them (a lobby from the ground to the roof cuts every floor in between).
    /// </summary>
    [DataContract]
    public sealed class Opening
    {
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public string BottomLevelId { get; set; }
        [DataMember(Order = 2)] public string TopLevelId { get; set; }
        [DataMember(Order = 3)] public List<Rect2> Pieces { get; set; } = new List<Rect2>();
    }

    public enum WallDirection { WestEast, SouthNorth, NotOrthogonal }

    /// <summary>A wall in plan (W-E or S-N) from its base level to its top level; it can span several levels.</summary>
    [DataContract]
    public sealed class Wall
    {
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public Point2 Start { get; set; }
        [DataMember(Order = 2)] public Point2 End { get; set; }
        [DataMember(Order = 3)] public string BaseLevelId { get; set; }
        [DataMember(Order = 4)] public string TopLevelId { get; set; }
        [DataMember(Order = 5)] public bool IsShearWall { get; set; } = true;

        public double Length => new Segment2(Start, End).Length;

        public WallDirection Direction =>
            !(Length > 0) ? WallDirection.NotOrthogonal
            : Start.Y == End.Y ? WallDirection.WestEast
            : Start.X == End.X ? WallDirection.SouthNorth
            : WallDirection.NotOrthogonal;
    }

    /// <summary>
    /// The building, described once for every analysis app: levels, diaphragms, openings and walls (ft; x east, y north).
    /// Geometry is edited here only; the apps key their own data to the IDs.
    /// </summary>
    [DataContract]
    public sealed class Building
    {
        [DataMember(Order = 0)] public string Name { get; set; } = "";
        [DataMember(Order = 1)] public List<Level> Levels { get; set; } = new List<Level>();
        [DataMember(Order = 2)] public List<Diaphragm> Diaphragms { get; set; } = new List<Diaphragm>();
        [DataMember(Order = 3)] public List<Opening> Openings { get; set; } = new List<Opening>();
        [DataMember(Order = 4)] public List<Wall> Walls { get; set; } = new List<Wall>();

        /// <summary>A building with only the ground level.</summary>
        public static Building New(string name = "")
        {
            var b = new Building { Name = name };
            b.Levels.Add(new Level { Id = b.NewId("L"), Name = "Ground", Elevation = 0 });
            return b;
        }

        /// <summary>An ID not used in this building: the prefix and the next number (L1, D3, W12...).</summary>
        public string NewId(string prefix)
        {
            var used = new HashSet<string>(AllIds(), StringComparer.Ordinal);
            for (int n = 1; ; n++)
                if (!used.Contains(prefix + n)) return prefix + n;
        }

        public IEnumerable<string> AllIds() =>
            Levels.Select(l => l.Id).Concat(Diaphragms.Select(d => d.Id)).Concat(Openings.Select(o => o.Id)).Concat(Walls.Select(w => w.Id));

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

        // ------------------------------------------------------------------ lookups

        public Level Level(string id) => Levels.FirstOrDefault(l => l.Id == id);

        /// <summary>The ground level (elevation 0), or null.</summary>
        public Level Ground => Levels.FirstOrDefault(l => l.Elevation == 0);

        /// <summary>The levels from the bottom up.</summary>
        public List<Level> LevelsBottomUp => Levels.OrderBy(l => l.Elevation).ToList();
    }
}
