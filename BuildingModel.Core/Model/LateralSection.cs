using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BuildingModel.Model
{
    /// <summary>
    /// One vertical force the lateral system puts on the foundation at a point, per load case: a shear wall's end (its hold-down, or
    /// its compression post), a column or beam carrying hold-downs down. At the load's own level (W as the wind calculator gives it,
    /// strength level, not × 0.6), without any dead load: the load manager adds the dead load and combines.
    /// </summary>
    [DataContract]
    public sealed class LateralPoint
    {
        /// <summary>"SW1-2 end", "column C2 base".</summary>
        [DataMember(Order = 1)] public string From { get; set; }
        /// <summary>The model's wall it is the end of (null for a column or a beam end).</summary>
        [DataMember(Order = 2, EmitDefaultValue = false)] public string WallId { get; set; }
        [DataMember(Order = 3)] public double X { get; set; }
        [DataMember(Order = 4)] public double Y { get; set; }
        /// <summary>"Wind" (earthquake later).</summary>
        [DataMember(Order = 5)] public string Kind { get; set; } = "Wind";
        /// <summary>Load case → the force (lb): + up (uplift, the hold-down's), − down (bearing, the compression post's).</summary>
        [DataMember(Order = 6)] public Dictionary<string, double> Up { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The connector at the point, as the lateral design chose it ("HDU5-SDS2.5"): a hold-down at a wall end, a
        /// connection at a column base. Null: none chosen (none strong enough, or no uplift to hold).
        /// </summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public string Connector { get; set; }

        /// <summary>The largest uplift over the cases (lb, 0 when it only presses down).</summary>
        public double MaxUp => Up == null || Up.Count == 0 ? 0 : Math.Max(0, Up.Values.Max());

        public BuildingModel.Geometry.Point2 Where => new BuildingModel.Geometry.Point2(X, Y);
    }

    /// <summary>
    /// A hold-down (or a connection carrying one down) on any story, as the lateral design placed and sized it, for every plan
    /// and wall view to draw (they draw it, they do not size it): where it is, what it is, and what it anchors to on the level
    /// below. A wall's hold-down is drawn on its wall's own plan (the level the wall carries) and, coming down, on the plan of
    /// the level it lands on.
    /// </summary>
    [DataContract]
    public sealed class StoryHoldown
    {
        /// <summary>"W7 west end", "beam B2 end", "column C1 base".</summary>
        [DataMember(Order = 1)] public string From { get; set; }
        /// <summary>The model's wall it is a hold-down of (null for a beam end or a column base).</summary>
        [DataMember(Order = 2, EmitDefaultValue = false)] public string WallId { get; set; }
        /// <summary>Where (ft): on the end post's inside face.</summary>
        [DataMember(Order = 3)] public double X { get; set; }
        [DataMember(Order = 4)] public double Y { get; set; }
        /// <summary>The level whose plan shows it as its own: the level its wall carries (the top of its story).</summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public string LevelId { get; set; }
        /// <summary>The level it lands on (its base): the ground for the first story.</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public string OntoLevelId { get; set; }
        /// <summary>The connector chosen ("HDU5-SDS2.5", "MSTC40"); null: none strong enough.</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public string Connector { get; set; }
        /// <summary>Its tension (lb, ASD), the worst case.</summary>
        [DataMember(Order = 8)] public double TensionAsd { get; set; }
        /// <summary>What it anchors to: "the foundation", "wall W3", "beam B2", "column C1"; null: nothing under it.</summary>
        [DataMember(Order = 9, EmitDefaultValue = false)] public string Onto { get; set; }
        /// <summary>"wall", "beam end" or "column base".</summary>
        [DataMember(Order = 10)] public string Kind { get; set; } = "wall";
        /// <summary>The end post's width along the wall (in) the hold-down needs (0: not a wall's).</summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public double PostAlongWall { get; set; }
        /// <summary>Its connector's family in a word ("bolted" for a hold-down bolted to the post, "strap"), for the views.</summary>
        [DataMember(Order = 12, EmitDefaultValue = false)] public string Family { get; set; }

        /// <summary>
        /// Wind case → the force this point puts on what is under it (lb, W at strength level: the lateral design's ASD ÷ 0.6),
        /// + up (the hold-down's tension, in the cases this end lifts) / − down (the overturning compression, in the cases it
        /// does not), as <see cref="LateralPoint.Up"/> on the foundation: the couple alone (the walls above included), no dead
        /// load. A beam end or column base carries tension only. The equal and opposite force acts on the member above. Null
        /// in a file saved before it was kept (only <see cref="TensionAsd"/> then).
        /// </summary>
        [DataMember(Order = 13, EmitDefaultValue = false)] public Dictionary<string, double> Up { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Where its end presses down in the cases it does not lift (ft): the end post's centreline, half a post from the end
        /// (the tension is at <see cref="X"/>, <see cref="Y"/>, the post's inside face; the lever arm between them is
        /// L − 1.5 post). Null for a beam end or column base, and in a file saved before it was kept (then at X, Y).
        /// </summary>
        [DataMember(Order = 14, EmitDefaultValue = false)] public double? CompressionX { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public double? CompressionY { get; set; }

        /// <summary>Where its force acts in one case: at the hold-down when it pulls (+ up), at the post's centreline when it presses.</summary>
        public BuildingModel.Geometry.Point2 WhereIn(string windCase) =>
            Up != null && windCase != null && Up.TryGetValue(windCase, out var v) && v < 0 && CompressionX != null && CompressionY != null
                ? new BuildingModel.Geometry.Point2(CompressionX.Value, CompressionY.Value) : Where;

        /// <summary>The largest uplift over the cases (lb, W at strength level; 0 when it only presses down, or none kept).</summary>
        public double MaxUp => Up == null || Up.Count == 0 ? 0 : Math.Max(0, Up.Values.Max());

        /// <summary>The largest downward force over the cases (lb, W at strength level, as a positive number; 0: none).</summary>
        public double MaxDown => Up == null || Up.Count == 0 ? 0 : Math.Max(0, -Up.Values.Min());

        public bool IsWall => Kind == "wall";
        public bool Supported => Onto != null;
        public BuildingModel.Geometry.Point2 Where => new BuildingModel.Geometry.Point2(X, Y);
    }

    /// <summary>One face of a shear wall's sheathing as designed, in words and numbers the views draw.</summary>
    [DataContract]
    public sealed class ShearFace
    {
        /// <summary>"Structural I 15/32\"", "Gypsum board 1/2\"".</summary>
        [DataMember(Order = 1)] public string Sheathing { get; set; }
        /// <summary>Its thickness (in).</summary>
        [DataMember(Order = 2)] public double ThicknessIn { get; set; } = 0.5;
        [DataMember(Order = 3, EmitDefaultValue = false)] public bool Gypsum { get; set; }
        /// <summary>"8d common"; null when it is not nailed at a spacing (gypsum, plaster).</summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public string Nail { get; set; }
        /// <summary>The nails' spacing (in) at the panel edges and in the field (0: not nailed at a spacing).</summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public double EdgeIn { get; set; }
        [DataMember(Order = 6, EmitDefaultValue = false)] public double FieldIn { get; set; }
    }

    /// <summary>A first-story shear wall's sill anchor bolts as designed.</summary>
    [DataContract]
    public sealed class SillAnchors
    {
        /// <summary>Each bolt (in) from the shear part's low end (west or south).</summary>
        [DataMember(Order = 1)] public List<double> PositionsIn { get; set; } = new List<double>();
        [DataMember(Order = 2)] public double BoltDiaIn { get; set; }
        [DataMember(Order = 3)] public double SpacingIn { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public bool PlateWasher { get; set; }
    }

    /// <summary>
    /// An upper-story shear wall's sill nailed through the floor sheathing into the rim or blocking, as designed (the wall's
    /// shear out of its base, SDPWS 4.3.6.4.1): the nail, its spacing, and what one nail takes.
    /// </summary>
    [DataContract]
    public sealed class SillNails
    {
        /// <summary>"16d common".</summary>
        [DataMember(Order = 1)] public string Nail { get; set; }
        [DataMember(Order = 2)] public double SpacingIn { get; set; }
        /// <summary>One nail's adjusted lateral value Z' (lb, ASD, CD = 1.6) and the floor sheathing it crosses (in).</summary>
        [DataMember(Order = 3)] public double ZPrimeLb { get; set; }
        [DataMember(Order = 4)] public double SheathingIn { get; set; }
        /// <summary>No nail can take it at the closest spacing: what fails, in a sentence (null: it works).</summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public string Fails { get; set; }
    }

    /// <summary>
    /// A shear wall as the lateral design found it, for the wall views (they draw it, they do not design it): the part that is
    /// shear wall, its sheathing and nailing, its hold-downs' lever arm, its check, and the sill bolts of a first-story wall.
    /// </summary>
    [DataContract]
    public sealed class ShearWallDesign
    {
        [DataMember(Order = 1)] public string WallId { get; set; }
        /// <summary>The level its wall carries (the top of its story).</summary>
        [DataMember(Order = 2)] public string LevelId { get; set; }
        /// <summary>The shear part as built, its two ends on plan (ft): from the face of a wall it butts into.</summary>
        [DataMember(Order = 3)] public double X0 { get; set; }
        [DataMember(Order = 4)] public double Y0 { get; set; }
        [DataMember(Order = 11)] public double X1 { get; set; }
        [DataMember(Order = 12)] public double Y1 { get; set; }

        /// <summary>The shear part along <paramref name="w"/> (ft from its low end, west or south), as built.</summary>
        public (double from, double to) Along(Wall w)
        {
            bool ew = Math.Abs(w.End.X - w.Start.X) >= Math.Abs(w.End.Y - w.Start.Y);
            double low = ew ? Math.Min(w.Start.X, w.End.X) : Math.Min(w.Start.Y, w.End.Y);
            double a = (ew ? X0 : Y0) - low, b = (ew ? X1 : Y1) - low;
            return (Math.Max(0, Math.Min(a, b)), Math.Min(w.Length, Math.Max(a, b)));
        }
        /// <summary>Face 1, then face 2 when it has one.</summary>
        [DataMember(Order = 5)] public List<ShearFace> Faces { get; set; } = new List<ShearFace>();
        /// <summary>The hold-downs' lever arm d (in).</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public double ArmIn { get; set; }
        /// <summary>"OK" or what fails, with the shear: "OK: v = 310 plf of 380 plf".</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public string Check { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public bool Fails { get; set; }
        /// <summary>The hold-down's fasteners ("(12) SDS 1/4 x 2-1/2"), for its label.</summary>
        [DataMember(Order = 9, EmitDefaultValue = false)] public string Fasteners { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public SillAnchors Anchors { get; set; }
        /// <summary>An upper-story wall's sill nailing (null on the first story, or before a design).</summary>
        [DataMember(Order = 15, EmitDefaultValue = false)] public SillNails SillNails { get; set; }

        /// <summary>
        /// Wind case → the wind's shear on the top of the wall from its diaphragm (lb, W at strength level), + toward its high
        /// end (east for an east-west wall, north for a north-south one): the external load on its free body, which its
        /// hold-downs, compression posts and sill take. Null in a file saved before it was kept, or not designed.
        /// </summary>
        [DataMember(Order = 13, EmitDefaultValue = false)] public Dictionary<string, double> ShearOnTop { get; set; }

        /// <summary>The height the shear acts at above the wall's base (ft): its story's height, as designed.</summary>
        [DataMember(Order = 14, EmitDefaultValue = false)] public double HeightFt { get; set; }
    }

    /// <summary>
    /// One line of shear walls in one level's diaphragm, as the lateral design found it (ROADMAP step 6, owner 2026-11-07:
    /// the diaphragm's line shear and collectors): the walls on the line (under the diaphragm, their tops, and above it, their
    /// sills) take or give the line's force R per case; the diaphragm delivers it along its whole depth on the line, v = R /
    /// depth; the boundary nailing along the line is sized for it (SDPWS Table 4.2A or 4.2C), and the top plates carry the
    /// difference between the diaphragm's even delivery and the walls' shear parts (the collector, SDPWS 4.1.4).
    /// </summary>
    [DataContract]
    public sealed class DiaphragmLine
    {
        [DataMember(Order = 1)] public string LevelId { get; set; }
        /// <summary>The roof's diaphragm (else a floor's): which sheathing defaults it was designed with.</summary>
        [DataMember(Order = 2)] public bool Roof { get; set; }
        /// <summary>The line where the diaphragm reaches along it (ft): its two ends, west to east or south to north.</summary>
        [DataMember(Order = 3)] public double X0 { get; set; }
        [DataMember(Order = 4)] public double Y0 { get; set; }
        [DataMember(Order = 5)] public double X1 { get; set; }
        [DataMember(Order = 6)] public double Y1 { get; set; }
        /// <summary>The diaphragm's depth along the line (ft): its length on the line, any gap left out.</summary>
        [DataMember(Order = 7)] public double DepthFt { get; set; }
        /// <summary>The shear walls on the line (under it and standing on it).</summary>
        [DataMember(Order = 8)] public List<string> WallIds { get; set; } = new List<string>();
        /// <summary>Wind case → the line's unit shear (plf, W at strength level, R / depth, + east or north on the diaphragm).</summary>
        [DataMember(Order = 9)] public Dictionary<string, double> Shear { get; set; } = new Dictionary<string, double>();
        /// <summary>The worst unit shear, ASD (0.6 W, plf).</summary>
        [DataMember(Order = 10)] public double ShearAsdPlf { get; set; }
        /// <summary>The sheathing ("Sheathing and Single-Floor, 10d, 19/32\" min., 2\" framing").</summary>
        [DataMember(Order = 11)] public string Sheathing { get; set; }
        [DataMember(Order = 12)] public bool Blocked { get; set; }
        /// <summary>The nail spacing at the boundary (along the line) and at the other panel edges, or the supported edges (in).</summary>
        [DataMember(Order = 13)] public double BoundaryIn { get; set; }
        [DataMember(Order = 14)] public double EdgesIn { get; set; }
        [DataMember(Order = 15)] public string Nail { get; set; }
        /// <summary>The nailing's ASD allowable unit shear (plf).</summary>
        [DataMember(Order = 16)] public double AllowablePlf { get; set; }
        /// <summary>The worst collector force along the line (lb, ASD), and where (ft from the line's start).</summary>
        [DataMember(Order = 17)] public double CollectorLb { get; set; }
        [DataMember(Order = 18)] public double CollectorAtFt { get; set; }
        /// <summary>What carries it ("DF-L No. 2 2x4 double top plate: 5 16d common each side of a splice"), or why not designed.</summary>
        [DataMember(Order = 19, EmitDefaultValue = false)] public string Collector { get; set; }
        /// <summary>A construction requirement with the nailing (3" framing, staggered), else null.</summary>
        [DataMember(Order = 20, EmitDefaultValue = false)] public string Note { get; set; }
        /// <summary>What does not work (null: it works).</summary>
        [DataMember(Order = 21, EmitDefaultValue = false)] public string Fails { get; set; }

        /// <summary>"10d at 4\" o.c. at the boundary, 6\" at the other panel edges (blocked)".</summary>
        public string Nailing => BoundaryIn <= 0 ? "(not designed)" :
            $"{Nail} at {BoundaryIn.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}\" o.c. at the boundary, {EdgesIn.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}\" at the {(Blocked ? "other panel edges (blocked)" : "supported edges (unblocked)")}";
    }

    /// <summary>
    /// One level's own wind on its diaphragm, per case, as the lateral design applied it (W at strength level): the force
    /// (lb, + east and north) at <see cref="X"/>, <see cref="Y"/> (its center of mass) and the moment about that point (lb·ft,
    /// counter-clockwise +). The shears of the walls of the story above (standing on it) are not in it: they are pairs
    /// (<see cref="ShearPairs"/>).
    /// </summary>
    [DataContract]
    public sealed class DiaphragmWind
    {
        [DataMember(Order = 1)] public string LevelId { get; set; }
        [DataMember(Order = 2)] public double X { get; set; }
        [DataMember(Order = 3)] public double Y { get; set; }
        [DataMember(Order = 4)] public Dictionary<string, double> Fx { get; set; } = new Dictionary<string, double>();
        [DataMember(Order = 5)] public Dictionary<string, double> Fy { get; set; } = new Dictionary<string, double>();
        /// <summary>
        /// The moment about (X, Y) per case where the analysis puts the load at a known place (the rigid diaphragm: at the
        /// center of mass with the case's torsion). A flexible diaphragm's case is not in it: it spreads the load by
        /// tributary width, so where its resultant acts is the analysis's own assumption, and only ΣF is checked.
        /// </summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public Dictionary<string, double> Mz { get; set; } = new Dictionary<string, double>();

        public double FxOf(string c) => c != null && Fx != null && Fx.TryGetValue(c, out var v) ? v : 0;
        public double FyOf(string c) => c != null && Fy != null && Fy.TryGetValue(c, out var v) ? v : 0;
        public double? MzOf(string c) => c != null && Mz != null && Mz.TryGetValue(c, out var v) ? v : (double?)null;
    }

    /// <summary>
    /// What the lateral design (the shear wall app) puts on the foundation: its own section of the project file ("Lateral"), written
    /// by the shear wall app each time it saves. The load manager reads its forces; every plan (the shared sketch) draws its points
    /// and connectors on the foundation plan. The format is here, in the building model, so no app reads another app's own code.
    /// </summary>
    [DataContract]
    public sealed class LateralSection
    {
        public const string Key = "Lateral";

        /// <summary>The app that wrote it ("Shear Wall Calculator").</summary>
        [DataMember(Order = 1)] public string Source { get; set; }
        /// <summary>When (UTC, ISO 8601).</summary>
        [DataMember(Order = 2)] public string Saved { get; set; }
        /// <summary>The model's revision it was found on (the file's revision after that save).</summary>
        [DataMember(Order = 3)] public int ModelRevision { get; set; }
        /// <summary>
        /// The load cases, in order: each wind calculator case by its own name, once for each way the shear walls share it
        /// ("&lt;name&gt; (rigid)", "&lt;name&gt; (flexible)": the diaphragm taken rigid and flexible, owner 2026-11-07: the same
        /// wind, two analyses, kept apart and never mixed, so every free body is one analysis and balances). The load manager
        /// takes everything else about the wind, its direction and roof pressures, from the wind calculator by
        /// <see cref="WindOf"/>. A "From" map in files saved before is ignored.
        /// </summary>
        [DataMember(Order = 4)] public List<string> Cases { get; set; } = new List<string>();

        /// <summary>The two ways the shear walls share a wind among them, as the case names end.</summary>
        public const string Rigid = "rigid", Flexible = "flexible";

        /// <summary>The wind calculator's case a lateral case is ("Case 1 … (rigid)" → "Case 1 …"; a name with no sharing as it is).</summary>
        public static string WindOf(string lateralCase)
        {
            if (lateralCase == null) return null;
            foreach (var tag in new[] { Rigid, Flexible })
            {
                string tail = $" ({tag})";
                if (lateralCase.EndsWith(tail, StringComparison.Ordinal)) return lateralCase.Substring(0, lateralCase.Length - tail.Length);
            }
            return lateralCase;
        }
        [DataMember(Order = 5)] public List<LateralPoint> Points { get; set; } = new List<LateralPoint>();
        /// <summary>How the forces were found, in a sentence (shown with them).</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public string Note { get; set; }

        /// <summary>
        /// What the forces were found from (<see cref="Serialization.ModelFingerprint"/>): part → fingerprint ("geometry",
        /// "wind", "shearwalls"). The hub compares them with the project as it is to say which input has changed since.
        /// </summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public Dictionary<string, string> BasedOn { get; set; }

        /// <summary>
        /// Every story's hold-downs and the connections carrying them down (<see cref="StoryHoldown"/>), for the plans of
        /// every level and the wall views; the foundation's forces stay in <see cref="Points"/>.
        /// </summary>
        [DataMember(Order = 8, EmitDefaultValue = false)] public List<StoryHoldown> Holdowns { get; set; } = new List<StoryHoldown>();

        /// <summary>Every story's shear walls as designed (<see cref="ShearWallDesign"/>), for the wall views.</summary>
        [DataMember(Order = 9, EmitDefaultValue = false)] public List<ShearWallDesign> Walls { get; set; } = new List<ShearWallDesign>();

        /// <summary>
        /// Every level's own wind on its diaphragm per case (<see cref="DiaphragmWind"/>): with the shear walls' shear
        /// (<see cref="ShearPairs"/>) each diaphragm's free body. Empty in a file saved before it was kept.
        /// </summary>
        [DataMember(Order = 10, EmitDefaultValue = false)] public List<DiaphragmWind> Diaphragms { get; set; } = new List<DiaphragmWind>();

        /// <summary>
        /// Each diaphragm's lines of shear walls as designed (<see cref="DiaphragmLine"/>): the line's unit shear, the boundary
        /// nailing, and the collector along it. Empty in a file saved before it was kept.
        /// </summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public List<DiaphragmLine> DiaphragmLines { get; set; } = new List<DiaphragmLine>();

        /// <summary>The line of <paramref name="levelId"/>'s diaphragm that <paramref name="wallId"/> is on (null: none).</summary>
        public DiaphragmLine LineOf(string levelId, string wallId) =>
            (DiaphragmLines ?? new List<DiaphragmLine>()).FirstOrDefault(l => l.LevelId == levelId && l.WallIds != null && l.WallIds.Contains(wallId));

        /// <summary>The design of <paramref name="wallId"/> (null: not a shear wall, or not designed).</summary>
        public ShearWallDesign DesignOf(string wallId) => (Walls ?? new List<ShearWallDesign>()).FirstOrDefault(w => w.WallId == wallId);

        /// <summary>The hold-downs of <paramref name="wallId"/> (its own, at its base).</summary>
        public IEnumerable<StoryHoldown> HoldownsOf(string wallId) => (Holdowns ?? new List<StoryHoldown>()).Where(h => h.IsWall && h.WallId == wallId);

        /// <summary>The hold-downs and connections from the story above that land on <paramref name="what"/> ("wall W3", "beam B2").</summary>
        public IEnumerable<StoryHoldown> LandingOn(string what) => (Holdowns ?? new List<StoryHoldown>()).Where(h => h.Onto == what);

        public bool IsEmpty => (Points == null || Points.Count == 0) && (Holdowns == null || Holdowns.Count == 0) && (Walls == null || Walls.Count == 0);

        private static DataContractJsonSerializer Serializer() =>
            new DataContractJsonSerializer(typeof(LateralSection), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static LateralSection Read(Building b)
        {
            string json = b?.GetData(Key);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (LateralSection)Serializer().ReadObject(ms);
            }
            catch (SerializationException)
            {
                return null;
            }
        }

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>Writes it into the building's data (null or empty: removes it).</summary>
        public static void Write(Building b, LateralSection s) => b?.SetData(Key, s == null || s.IsEmpty ? null : s.ToJson());

        /// <summary>
        /// Why these forces may not match the model any more (null: they do): a wall they name is gone, or its end is not where the
        /// force is.
        /// </summary>
        public string Stale(Building b)
        {
            foreach (var p in Points ?? new List<LateralPoint>())
            {
                if (p.WallId == null) continue;
                var w = b?.Walls.FirstOrDefault(x => x.Id == p.WallId);
                if (w == null) return $"{p.From}: wall {p.WallId} is not in the model any more.";
                double d = Math.Min(Dist(w.Start, p.Where), Dist(w.End, p.Where));
                if (d > 1.5) return $"{p.From}: wall {p.WallId} has moved ({d:0.#} ft from its end).";
            }
            foreach (var h in Holdowns ?? new List<StoryHoldown>())
            {
                if (h.WallId == null) continue;
                if (b?.Walls.Any(x => x.Id == h.WallId) != true) return $"{h.From}: wall {h.WallId} is not in the model any more.";
            }
            return null;
        }

        private static double Dist(BuildingModel.Geometry.Point2 a, BuildingModel.Geometry.Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
