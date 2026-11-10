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

    /// <summary>
    /// One face of a shear wall's sheathing as designed, in words and numbers the views draw. Its nailing is a connection
    /// detail, not the model's (owner 2026-11-08): WoodFraming.Connections, the same face by its index.
    /// </summary>
    [DataContract]
    public sealed class ShearFace
    {
        /// <summary>"Structural I 15/32\"", "Gypsum board 1/2\"".</summary>
        [DataMember(Order = 1)] public string Sheathing { get; set; }
        /// <summary>Its thickness (in).</summary>
        [DataMember(Order = 2)] public double ThicknessIn { get; set; } = 0.5;
        [DataMember(Order = 3, EmitDefaultValue = false)] public bool Gypsum { get; set; }
    }

    /// <summary>
    /// A shear wall as the lateral design found it, for the wall views (they draw it, they do not design it): the part that is
    /// shear wall, its sheathing and nailing, its hold-downs' lever arm, its check. Its sill's anchor bolts or nailing are
    /// details, not the model's (owner 2026-11-07): the "Connections" section (WoodFraming.Connections).
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

        /// <summary>
        /// Wind case → the wind's shear on the top of the wall from its diaphragm (lb, W at strength level), + toward its high
        /// end (east for an east-west wall, north for a north-south one): the external load on its free body, which its
        /// hold-downs, compression posts and sill take. Null in a file saved before it was kept, or not designed.
        /// </summary>
        [DataMember(Order = 13, EmitDefaultValue = false)] public Dictionary<string, double> ShearOnTop { get; set; }

        /// <summary>The height the shear acts at above the wall's base (ft): its story's height, as designed.</summary>
        [DataMember(Order = 14, EmitDefaultValue = false)] public double HeightFt { get; set; }

        /// <summary>
        /// Declared but not designed (no shear on it yet: no wind set up, or no story forces): no shear check, nailing or
        /// hold-downs. Its faces are only what its card asks for. Every view says so (<see cref="NotDesignedWarning"/>).
        /// </summary>
        [DataMember(Order = 15, EmitDefaultValue = false)] public bool NotDesigned { get; set; }

        /// <summary>The words every app shows on a shear wall that is not designed (<see cref="NotDesigned"/>).</summary>
        public const string NotDesignedWarning =
            "NOT DESIGNED: no shear check, nailing or hold-downs yet. The sheathing shown is only what its card asks for, not a design. " +
            "The Shear Wall Calculator designs it once the wind is set up; do not build from it until then.";
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

        /// <summary>The design of <paramref name="wallId"/> (null: not a shear wall, or not designed).</summary>
        public ShearWallDesign DesignOf(string wallId) => (Walls ?? new List<ShearWallDesign>()).FirstOrDefault(w => w.WallId == wallId);

        /// <summary>The hold-downs of <paramref name="wallId"/> (its own, at its base).</summary>
        public IEnumerable<StoryHoldown> HoldownsOf(string wallId) => (Holdowns ?? new List<StoryHoldown>()).Where(h => h.IsWall && h.WallId == wallId);

        /// <summary>The hold-downs and connections from the story above that land on <paramref name="what"/> ("wall W3", "beam B2").</summary>
        public IEnumerable<StoryHoldown> LandingOn(string what) => (Holdowns ?? new List<StoryHoldown>()).Where(h => h.Onto == what);

        /// <summary>The declared shear walls not designed yet (<see cref="ShearWallDesign.NotDesigned"/>), one per wall story.</summary>
        public List<ShearWallDesign> NotDesigned() => (Walls ?? new List<ShearWallDesign>()).Where(w => w.NotDesigned).ToList();

        /// <summary>"2 shear walls are not designed (W1, W4): ...", or null when every one is designed.</summary>
        public string NotDesignedWords()
        {
            var n = NotDesigned();
            if (n.Count == 0) return null;
            var ids = n.Select(w => w.WallId).Distinct().ToList();
            return $"{(ids.Count == 1 ? "1 shear wall is" : ids.Count + " shear walls are")} not designed ({string.Join(", ", ids)}): no shear check, " +
                   "nailing or hold-downs, and no forces from them in the foundation. The Shear Wall Calculator designs them once the wind is set up.";
        }

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

        /// <summary>
        /// The inputs any app can find again from the model (the building, the wind's inputs) that have changed since the
        /// design was saved (<see cref="BasedOn"/>), in words: "The building and the wind inputs changed since the shear walls
        /// were saved." Null when none changed, or the design was saved before the stamps (it cannot tell). The shear wall
        /// app's own inputs are its to compare (the hub does, with its code).
        /// </summary>
        public string ChangedSince(Building b)
        {
            var changed = Serialization.ModelFingerprint.ChangedFromModel(BasedOn, b);
            if (changed == null || changed.Count == 0) return null;
            var words = changed.Select(Serialization.ModelFingerprint.Words).ToList();
            string list = words.Count == 1 ? words[0] : string.Join(", ", words.Take(words.Count - 1)) + " and " + words.Last();
            return char.ToUpperInvariant(list[0]) + list.Substring(1) + " changed since the shear walls were saved.";
        }

        /// <summary>
        /// Why this design may not be the project's any more, for every app's views (null: it is): a wall it names gone or moved
        /// (<see cref="Stale"/>), else an input changed since it was saved (<see cref="ChangedSince"/>). One routine, so the
        /// plans, the wall views and the Load Manager say the same.
        /// </summary>
        public string OutOfDate(Building b) => Stale(b) ?? ChangedSince(b);

        private static double Dist(BuildingModel.Geometry.Point2 a, BuildingModel.Geometry.Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
