using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using calculator;
using Newtonsoft.Json;
using ShearWallCalculator;
using ShearWallCalculator.Design;

namespace BuildingModel.ShearWalls
{
    /// <summary>
    /// One story of the designer: the model level on top of it, its calculator, and how the calculator's integer keys
    /// map to the model's IDs.
    /// </summary>
    public sealed class StoryMap
    {
        public Level Level { get; set; }
        public ShearWallCalculatorBase Calculator { get; set; }

        /// <summary>The story height under the level (ft).</summary>
        public double PlateHeight { get; set; }

        /// <summary>Calculator wall key → model wall ID (W12 → 12: the same key on every story the wall spans).</summary>
        public Dictionary<int, string> Walls { get; } = new Dictionary<int, string>();

        /// <summary>Calculator diaphragm key → model diaphragm ID (D3 → 3, or 301, 302... when openings split it).</summary>
        public Dictionary<int, string> Diaphragms { get; } = new Dictionary<int, string>();

        public string ModelId(string kind, int key) =>
            kind == "Wall" ? (Walls.TryGetValue(key, out var w) ? w : null) : kind == "Diaphragm" ? (Diaphragms.TryGetValue(key, out var d) ? d : null) : null;

        public int? Key(string modelId)
        {
            foreach (var kv in Walls) if (kv.Value == modelId) return kv.Key;
            foreach (var kv in Diaphragms) if (kv.Value == modelId) return kv.Key;
            return null;
        }
    }

    /// <summary>
    /// The building model is the source of truth for the Shear Wall Designer's geometry. Each level above the ground is
    /// one designer story: its net diaphragm (openings taken out, as rectangles) and the shear walls under it that its
    /// diaphragm reaches. The designer's own data rides in the model's data bags:
    /// <list type="bullet">
    /// <item>a wall's design inputs, per story, under <see cref="WallKey"/>;</item>
    /// <item>a diaphragm's type (rigid, flexible) under <see cref="DiaphragmKey"/>;</item>
    /// <item>the project's design settings on the building under <see cref="SettingsKey"/>.</item>
    /// </list>
    /// </summary>
    public static class ShearWallAdapter
    {
        public const string WallKey = "ShearWall";
        public const string DiaphragmKey = "ShearWall";
        public const string SettingsKey = "ShearWall.DesignSettings";

        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>The designer's data about one wall: its design inputs on each story (by level ID) and its type.</summary>
        public sealed class WallExtras
        {
            public Dictionary<string, WallDesignInputs> Design { get; set; } = new Dictionary<string, WallDesignInputs>();
            public WallTypes WallType { get; set; } = WallTypes.Type_01;
        }

        public sealed class DiaphragmExtras
        {
            public DiaphragmTypes Type { get; set; } = DiaphragmTypes.DIAPHRAGM_RIGID;
        }

        public static WallExtras ReadWall(Wall w) =>
            Parse<WallExtras>(w.GetData(WallKey)) ?? new WallExtras();

        public static void WriteWall(Wall w, WallExtras x) => w.SetData(WallKey, JsonConvert.SerializeObject(x, Json));

        public static DiaphragmExtras ReadDiaphragm(Diaphragm d) => Parse<DiaphragmExtras>(d.GetData(DiaphragmKey)) ?? new DiaphragmExtras();

        private static T Parse<T>(string s) where T : class
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return JsonConvert.DeserializeObject<T>(s, Json); }
            catch (JsonException) { return null; }
        }

        /// <summary>The numeric part of an ID (W12 → 12); null when it has none.</summary>
        public static int? Number(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            int i = 0;
            while (i < id.Length && !char.IsDigit(id[i])) i++;
            return int.TryParse(id.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : (int?)null;
        }

        /// <summary>The designer's stories: every level above the ground, bottom up.</summary>
        public static List<Level> Stories(Building b) => b.LevelsBottomUp.Where(l => l.Elevation > 0).ToList();

        /// <summary>
        /// Puts the model into the designer's calculators, one per story (reusing <paramref name="existing"/> in order,
        /// so their loads and plan images stay; new ones are rigid-diaphragm calculators). The walls and diaphragms of
        /// each are replaced from the model. Returns the stories, bottom up.
        /// </summary>
        public static List<StoryMap> ToCalculators(Building b, IList<ShearWallCalculatorBase> existing = null)
        {
            var settings = Parse<DesignSettings>(b.GetData(SettingsKey)) ?? existing?.FirstOrDefault()?.DesignSettings ?? new DesignSettings();
            var stories = new List<StoryMap>();
            var levels = Stories(b);
            for (int i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                var calc = existing != null && i < existing.Count && existing[i] != null ? existing[i] : new ShearWallCalculator_RigidDiaphragm();
                calc.DesignSettings = settings;
                var map = new StoryMap { Level = level, Calculator = calc, PlateHeight = b.StoryHeight(level) };
                Fill(b, map);
                stories.Add(map);
            }
            return stories;
        }

        private static void Fill(Building b, StoryMap map)
        {
            var calc = map.Calculator;
            var level = map.Level;
            if (calc._wall_system == null) calc._wall_system = new WallSystem();
            if (calc._diaphragm_system == null) calc._diaphragm_system = new DiaphragmSystem();
            calc._wall_system._walls.Clear();
            calc._wall_system.EW_Walls.Clear();
            calc._wall_system.NS_Walls.Clear();
            calc._diaphragm_system._diaphragms.Clear();

            // the net diaphragm as rectangles, each given to the model diaphragm that holds its centre
            var mine = b.DiaphragmsOn(level).ToList();
            var cells = b.NetDiaphragm(level).Cells().ToList();
            var byDiaphragm = mine.ToDictionary(d => d, d => new List<Rect2>());
            foreach (var c in cells)
            {
                double cx = (c.X0 + c.X1) / 2, cy = (c.Y0 + c.Y1) / 2;
                var owner = mine.FirstOrDefault(d => d.Pieces.Any(p => p.Contains(cx, cy))) ?? mine.FirstOrDefault();
                if (owner != null) byDiaphragm[owner].Add(c);
            }
            var usedKeys = new HashSet<int>();
            foreach (var kv in byDiaphragm)
            {
                int n = Number(kv.Key.Id) ?? 0;
                var type = ReadDiaphragm(kv.Key).Type;
                for (int k = 0; k < kv.Value.Count; k++)
                {
                    int key = kv.Value.Count == 1 ? n : n * 100 + k + 1;
                    while (usedKeys.Contains(key)) key += 10000;
                    usedKeys.Add(key);
                    var r = kv.Value[k];
                    var d = new DiaphragmData_Rectangular(new Point(r.X0, r.Y0), new Point(r.X1, r.Y1)) { ID = key, DiaphragmType = type };
                    calc._diaphragm_system._diaphragms[key] = d;
                    map.Diaphragms[key] = kv.Key.Id;
                }
            }

            // the shear walls under the level that its diaphragm reaches
            var net = b.NetDiaphragm(level);
            foreach (var w in b.WallsUnder(level).Where(w => w.IsShearWall && w.Direction != WallDirection.NotOrthogonal))
            {
                if (!(net.LengthAlong(w.Start, w.End, BuildingQueries.WallTolerance) > 0)) continue;
                int key = Number(w.Id) ?? 0;
                if (calc._wall_system._walls.ContainsKey(key)) continue;
                double height = HeightAt(b, w, level);
                if (!(height > 0)) continue;
                var extras = ReadWall(w);
                var data = new WallData(height, w.Start.X, w.Start.Y, w.End.X, w.End.Y) { ID = key, WallType = extras.WallType };
                if (extras.Design.TryGetValue(level.Id, out var design) && design != null) data.Design = design;
                else if (calc.DesignSettings?.NewWallDesign != null) data.Design = calc.DesignSettings.NewWallDesign.Clone(); // a new wall: the project's default
                calc._wall_system._walls[key] = data;
                map.Walls[key] = w.Id;
            }
            calc._wall_system.Update();
            calc._diaphragm_system.Update();
        }

        /// <summary>
        /// A wall's height for the diaphragm of <paramref name="level"/>: from the level below that braces it (or its base)
        /// up to this level.
        /// </summary>
        public static double HeightAt(Building b, Wall w, Level level)
        {
            var lo = b.Level(w.BaseLevelId);
            if (lo == null) return 0;
            double from = lo.Elevation;
            foreach (var br in b.BracedAt(w))
                if (br.Level.Elevation < level.Elevation && br.Level.Elevation > from) from = br.Level.Elevation;
            return level.Elevation - from;
        }

        /// <summary>
        /// Writes what the designer changed in its calculators back into the model's bags: each wall's design inputs on
        /// that story, its type, and the design settings. Geometry is not written back (it is edited in the sketch).
        /// Returns true when a bag changed.
        /// </summary>
        public static bool StoreDesign(Building b, IEnumerable<StoryMap> stories)
        {
            bool changed = false;
            ShearWallCalculatorBase any = null;
            foreach (var s in stories)
            {
                any = any ?? s.Calculator;
                foreach (var kv in s.Walls)
                {
                    if (!(b.Find(kv.Value) is Wall w) || !s.Calculator._wall_system._walls.TryGetValue(kv.Key, out var data)) continue;
                    var x = ReadWall(w);
                    string before = w.GetData(WallKey);
                    x.Design[s.Level.Id] = data.Design;
                    x.WallType = data.WallType;
                    WriteWall(w, x);
                    changed |= before != w.GetData(WallKey);
                }
                foreach (var kv in s.Diaphragms)
                {
                    if (!(b.Find(kv.Value) is Diaphragm d) || !s.Calculator._diaphragm_system._diaphragms.TryGetValue(kv.Key, out var data)) continue;
                    string before = d.GetData(DiaphragmKey);
                    d.SetData(DiaphragmKey, data.DiaphragmType == DiaphragmTypes.DIAPHRAGM_RIGID ? null : JsonConvert.SerializeObject(new DiaphragmExtras { Type = data.DiaphragmType }, Json));
                    changed |= before != d.GetData(DiaphragmKey);
                }
            }
            if (any?.DesignSettings != null)
            {
                string before = b.GetData(SettingsKey);
                b.SetData(SettingsKey, JsonConvert.SerializeObject(any.DesignSettings, Json));
                changed |= before != b.GetData(SettingsKey);
            }
            return changed;
        }

        /// <summary>
        /// Writes coordinates typed in the designer's tables back into the model: a wall's ends, and a diaphragm's
        /// rectangle when it is one rectangle on the story (not split by openings). Returns true when the model changed.
        /// </summary>
        public static bool StoreGeometry(Building b, StoryMap s)
        {
            bool changed = false;
            foreach (var kv in s.Walls)
            {
                if (!(b.Find(kv.Value) is Wall w) || !s.Calculator._wall_system._walls.TryGetValue(kv.Key, out var data)) continue;
                var a = new Point2(data.Start.X, data.Start.Y);
                var e = new Point2(data.End.X, data.End.Y);
                bool same = (a.Equals(w.Start) && e.Equals(w.End)) || (a.Equals(w.End) && e.Equals(w.Start));
                if (same) continue;
                w.Start = a;
                w.End = e;
                changed = true;
            }
            foreach (var g in s.Diaphragms.GroupBy(kv => kv.Value).Where(g => g.Count() == 1))
            {
                var kv = g.First();
                if (!(b.Find(kv.Value) is Diaphragm d) || d.Pieces.Count != 1 || !s.Calculator._diaphragm_system._diaphragms.TryGetValue(kv.Key, out var data)) continue;
                if (b.OpeningsCutting(s.Level).Count > 0) continue;
                var r = new Rect2(data.P1.X, data.P1.Y, data.P3.X, data.P3.Y);
                if (r.IsEmpty || r.Equals(d.Pieces[0])) continue;
                d.Pieces[0] = r;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// An older designer project (calculators only) as a building model: level n at the sum of the plate heights
        /// (or each story's tallest wall, else 9 ft), each diaphragm rectangle a diaphragm, each wall a shear wall of its
        /// story with its design. Keys become IDs where they are free (W3 for wall 3 of the first story).
        /// </summary>
        public static Building FromCalculators(IList<ShearWallCalculatorBase> calcs, IList<double> plateHeights = null, string name = "")
        {
            var b = Building.New(name);
            var below = b.Ground;
            double z = 0;
            for (int i = 0; i < calcs.Count; i++)
            {
                var c = calcs[i];
                var walls = c?._wall_system?._walls ?? new Dictionary<int, WallData>();
                double h = plateHeights != null && i < plateHeights.Count && plateHeights[i] > 0 ? plateHeights[i]
                         : walls.Count > 0 ? walls.Values.Max(w => w.WallHeight) : 9;
                z += h;
                var level = b.AddLevel(i == calcs.Count - 1 && calcs.Count > 1 ? "Roof" : "Level " + (i + 2), z);
                foreach (var kv in (c?._diaphragm_system?._diaphragms ?? new Dictionary<int, DiaphragmData_Rectangular>()).OrderBy(kv => kv.Key))
                {
                    var d = new Diaphragm { Id = Free(b, "D", kv.Key), LevelId = level.Id, Pieces = { new Rect2(kv.Value.P1.X, kv.Value.P1.Y, kv.Value.P3.X, kv.Value.P3.Y) } };
                    if (kv.Value.DiaphragmType != DiaphragmTypes.DIAPHRAGM_RIGID)
                        d.SetData(DiaphragmKey, JsonConvert.SerializeObject(new DiaphragmExtras { Type = kv.Value.DiaphragmType }, Json));
                    b.Diaphragms.Add(d);
                }
                foreach (var kv in walls.OrderBy(kv => kv.Key))
                {
                    var w = new Wall
                    {
                        Id = Free(b, "W", kv.Key),
                        Start = new Point2(kv.Value.Start.X, kv.Value.Start.Y),
                        End = new Point2(kv.Value.End.X, kv.Value.End.Y),
                        BaseLevelId = below.Id,
                        TopLevelId = level.Id,
                    };
                    var x = new WallExtras { WallType = kv.Value.WallType };
                    x.Design[level.Id] = kv.Value.Design;
                    WriteWall(w, x);
                    b.Walls.Add(w);
                }
                below = level;
            }
            var settings = calcs.FirstOrDefault(c => c != null)?.DesignSettings;
            if (settings != null) b.SetData(SettingsKey, JsonConvert.SerializeObject(settings, Json));
            return b;
        }

        private static string Free(Building b, string prefix, int key)
        {
            string id = prefix + key;
            return key > 0 && b.Find(id) == null ? id : b.NewId(prefix);
        }
    }
}
