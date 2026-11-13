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

        /// <summary>
        /// Its short name on the level buttons of every app (owner 2026-11-11: G 2 R, compact): the number its name ends in
        /// ("Level 2": 2), else its first letter ("Ground": G, "Roof": R). The full name goes in the tooltip.
        /// </summary>
        public string Short
        {
            get
            {
                string name = (Name ?? "").Trim();
                var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)\s*$");
                if (m.Success) return m.Groups[1].Value;
                char initial = name.FirstOrDefault(char.IsLetterOrDigit);
                return initial != default(char) ? char.ToUpperInvariant(initial).ToString() : "?";
            }
        }
    }

    /// <summary>Which way a floor's joists (or a flat roof's rafters) span in plan. Auto: across the shorter side of the diaphragm.</summary>
    public enum SpanDirection { Auto = 0, WestEast = 1, SouthNorth = 2 }

    /// <summary>A floor or roof on one level: rectangles that merge into one outline (it can cover part of the footprint).</summary>
    [DataContract]
    public sealed class Diaphragm : ModelObject
    {
        [DataMember(Order = 1)] public string LevelId { get; set; }
        [DataMember(Order = 2)] public List<Rect2> Pieces { get; set; } = new List<Rect2>();

        /// <summary>
        /// Only from a file of format version 6 or 7: which way its joists or rafters spanned, for the whole diaphragm (Auto:
        /// the shorter way of the rectangle enclosing its pieces). Read once into its <see cref="Framing"/> when the file is
        /// opened (<see cref="FramingAreas.Tidy"/>), then cleared: the framing areas are the only span (owner 2026-11-10).
        /// </summary>
        [DataMember(Order = 3, EmitDefaultValue = false)] public SpanDirection Span { get; set; }

        /// <summary>
        /// Its framing (format version 8): rectangles that cover it exactly, no overlaps, each with the way its joists or
        /// rafters run (<see cref="FramingArea"/>). There is no span for the diaphragm itself: a part turned the other way is
        /// its own area, and the areas round it are cut back to it (<see cref="FramingAreas"/>). The diaphragm stays one
        /// piece for the wind and the shear walls.
        /// </summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public List<FramingArea> Framing { get; set; } = new List<FramingArea>();
    }

    /// <summary>
    /// A part of a diaphragm where the joists (or a flat roof's rafters) run one way (format version 8): a rectangle and the
    /// way they span. Its joists run across it from edge to edge, at the project's spacing; where they run into an edge they
    /// end there. Each joist needs two supports (a wall, column, beam or girder under it), as a beam does.
    /// </summary>
    [DataContract]
    public sealed class FramingArea : ModelObject
    {
        [DataMember(Order = 1)] public Rect2 Rect { get; set; }

        /// <summary>W-E or S-N (never Auto).</summary>
        [DataMember(Order = 2)] public SpanDirection Span { get; set; }

        /// <summary>
        /// What the part is (a hall, a balcony, a bath), or null. The Load Manager takes an area's loads by it: an area use of
        /// that name, else the level's (the loads are never here: owner 2026-11-10).
        /// </summary>
        [DataMember(Order = 3, EmitDefaultValue = false)] public string Name { get; set; }

        /// <summary>
        /// A ceiling fastened to the joists' or rafters' bottom edge their whole length (owner 2026-11-12; default none): it holds
        /// that edge in line where it is in compression (over an inner support, an eave, the wind lifting it), so CL = 1.0
        /// there (NDS 3.3.3.3). Without one CL is calculated from the lengths between the bearings and the bridging (3.3.3.4).
        /// </summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public bool Ceiling { get; set; }

        /// <summary>True when the joists run W-E (along x).</summary>
        public bool AlongX => Span != SpanDirection.SouthNorth;
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

    /// <summary>A wall's studs on one story, where they differ from the project's defaults (null or 0: the default).</summary>
    [DataContract]
    public sealed class StudFraming
    {
        /// <summary>Nominal stud size ("2x4", "2x6", "2x8"); the plates are as wide.</summary>
        [DataMember(Order = 1, EmitDefaultValue = false)] public string StudSize { get; set; }

        /// <summary>Stud spacing on centre (in).</summary>
        [DataMember(Order = 2, EmitDefaultValue = false)] public double StudSpacing { get; set; }

        public bool IsEmpty => string.IsNullOrEmpty(StudSize) && !(StudSpacing > 0);
        public StudFraming Clone() => (StudFraming)MemberwiseClone();
    }

    /// <summary>A wall in plan (W-E or S-N) from its base level to its top level; it can span several levels.</summary>
    [DataContract]
    public sealed class Wall : ModelObject
    {
        [DataMember(Order = 1)] public Point2 Start { get; set; }
        [DataMember(Order = 2)] public Point2 End { get; set; }
        [DataMember(Order = 3)] public string BaseLevelId { get; set; }
        [DataMember(Order = 4)] public string TopLevelId { get; set; }
        /// <summary>
        /// Only from older files: the wall was marked a shear wall here. The shear walls are declared in the Shear Wall Calculator
        /// (the part of a wall that counts, with its sheathing, in its own data), which reads this once when it adopts an older project.
        /// Nothing here sets it: a wall is a wall (it carries its gravity load) until it is declared a shear wall there.
        /// </summary>
        [DataMember(Order = 5)] public bool IsShearWall { get; set; }

        /// <summary>
        /// The wall's own framing on a story (format version 5), by the ID of the level at the story's top (the level it
        /// carries, as the apps key their per-story data). A story not listed, or a value not set, takes the project's
        /// defaults (<see cref="ModelDefaults"/>, <see cref="Building.StudSizeOf"/>).
        /// </summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public Dictionary<string, StudFraming> Framing { get; set; }
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
        /// <summary>
        /// Its sawn size (<see cref="BeamSizes"/>: "2x10", "3-2x12"; owner 2026-11-12: a size per beam). Null: not sized yet,
        /// and every app says it is NOT DESIGNED, with the lightest size that works.
        /// </summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public string Size { get; set; }

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

        /// <summary>
        /// The base was set by hand (it passes beside a beam, or stops where the engineer says): the editor leaves it. False:
        /// the editor keeps it standing on the first wall, beam or column under it, or the foundation, as things are drawn
        /// and deleted (<see cref="Edits.BuildingEdits.FollowSupports"/>).
        /// </summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public bool BaseFixed { get; set; }

        /// <summary>
        /// Its sawn size (owner 2026-11-12, a beam's bearing on a column): a post ("4x4", "6x6") or 2x studs nailed together
        /// ("3-2x6"), <see cref="ColumnSizes"/>; null: not sized (the bearing it needs is given, not checked).
        /// </summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public string Size { get; set; }
    }

    /// <summary>A roof's shape; a shed (one slope) only as a section (<see cref="RoofSection"/>: a shed dormer, a lean-to).</summary>
    public enum RoofShape { Flat = 0, Gable = 1, Hip = 2, Shed = 3 }

    /// <summary>A side of a rectangle on plan: a shed section's low side.</summary>
    public enum RoofSide { South = 0, North = 1, West = 2, East = 3 }

    /// <summary>
    /// One part of a sloped roof (owner 2026-11-12: wings, cross gables, hips that stick out, dormers): a rectangle on plan
    /// (its wall lines; the eaves past them by its overhang), a gable, hip or shed of its own pitch, its eave at the wall tops
    /// or higher (a dormer's, standing on the roof below). The roof's surface is the highest section at each point; where two
    /// meet a valley forms, and within one its ridge and hips (<see cref="Statics.RoofFrame"/>).
    /// </summary>
    [DataContract]
    public sealed class RoofSection
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        /// <summary>Its wall lines on plan (ft).</summary>
        [DataMember(Order = 2)] public Rect2 Walls { get; set; }
        /// <summary>Gable, hip or shed.</summary>
        [DataMember(Order = 3)] public RoofShape Shape { get; set; } = RoofShape.Gable;
        /// <summary>A gable's ridge: Auto along the longer side (a hip's always is).</summary>
        [DataMember(Order = 4)] public RidgeAxis Ridge { get; set; } = RidgeAxis.Auto;
        /// <summary>A shed's low side (its eave); the side opposite is its high wall.</summary>
        [DataMember(Order = 5)] public RoofSide Low { get; set; } = RoofSide.South;
        /// <summary>Its slope (degrees); 0: the roof's (<see cref="Roof.Pitch"/>).</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public double Pitch { get; set; }
        /// <summary>Its eave above the roof level (ft): 0 at the wall tops; a dormer's, on the roof below.</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public double EaveRise { get; set; }
        /// <summary>Its eaves past its walls (ft); null: the roof's (<see cref="Roof.Overhang"/>).</summary>
        [DataMember(Order = 8, EmitDefaultValue = false)] public double? Overhang { get; set; }

        public RoofSection Clone() => (RoofSection)MemberwiseClone();
        public override string ToString() => $"{Name ?? "section"}: {Shape} {Walls.X0:0.##},{Walls.Y0:0.##} to {Walls.X1:0.##},{Walls.Y1:0.##}{(EaveRise > 0 ? $", eave +{EaveRise:0.##} ft" : "")}";
    }

    /// <summary>
    /// A purlin under a roof's rafters (owner 2026-11-12: for rafters too long on their own): a line on plan across them
    /// (square to them, on one face), each rafter crossing it held there, braced down to a bearing wall (IRC R802.4.5: the
    /// purlin no smaller than the rafters, continuous; 2x4 braces to a bearing wall at 45° or steeper, no more than 4 ft
    /// apart, each no longer than 8 ft unbraced). Its braces' feet go on the wall its line is over, found or named.
    /// </summary>
    [DataContract]
    public sealed class RoofPurlin
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        /// <summary>Its line on plan (ft).</summary>
        [DataMember(Order = 2)] public Point2 Start { get; set; }
        [DataMember(Order = 3)] public Point2 End { get; set; }
        /// <summary>Its braces' spacing (ft); 0: 4 ft (IRC R802.4.5's most).</summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public double BraceSpacing { get; set; }
        /// <summary>The bearing wall its braces stand on; null: the nearest under it within 45° of plumb.</summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public string BraceTo { get; set; }

        public const double MostBraceSpacing = 4;
        public double Spacing => BraceSpacing > 0 ? BraceSpacing : MostBraceSpacing;
        public double Length => Math.Sqrt((End.X - Start.X) * (End.X - Start.X) + (End.Y - Start.Y) * (End.Y - Start.Y));
        public RoofPurlin Clone() => (RoofPurlin)MemberwiseClone();
        public override string ToString() => $"purlin {Id}: ({Start.X:0.##}, {Start.Y:0.##}) to ({End.X:0.##}, {End.Y:0.##}), braces at {Spacing:0.##} ft";
    }

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

        /// <summary>
        /// A gable's or hip's ceiling joists turned off (owner 2026-11-12: they can be; the roof never is): a vault, nothing at the
        /// wall tops under the rafters. Off by default: a sloped roof has its ceiling, the roof level's diaphragm at the wall tops,
        /// its framing areas the ceiling joists, separate from the roof above. A flat roof's ceiling is in the roof diaphragm.
        /// </summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public bool NoCeilingJoists { get; set; }

        /// <summary>
        /// A vaulted gable's ridge beam (no ceiling joists to tie the rafters: owner 2026-11-12, "a ridge beam instead"): its
        /// sawn size (<see cref="BeamSizes"/>); null: not sized, NOT DESIGNED. It runs the ridge's length, held by the walls
        /// across it (the gable ends) and the columns reaching the roof under it; each rafter bears on it, no thrust.
        /// </summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public string RidgeBeamSize { get; set; }

        public bool IsSloped => Shape != RoofShape.Flat;

        /// <summary>A gable with no ceiling joists: its rafters bear on a ridge beam (<see cref="RoofQueries.RidgeBeam"/>).</summary>
        public bool HasRidgeBeam => Shape == RoofShape.Gable && NoCeilingJoists;

        /// <summary>A gable's or hip's own ceiling joists under the rafters (the roof level's diaphragm framing), not turned off.</summary>
        public bool HasCeilingJoists => IsSloped && !NoCeilingJoists;

        /// <summary>
        /// Its sections (owner 2026-11-12: a roof of several parts, a dormer's too); none: the one section over the roof's plan,
        /// of its shape and ridge (<see cref="Statics.RoofFrame.SectionsOf"/>).
        /// </summary>
        [DataMember(Order = 8, EmitDefaultValue = false)] public List<RoofSection> Sections { get; set; }

        /// <summary>
        /// A sectioned roof's hip rafters (owner 2026-11-12): their sawn size, one 2x or plies of it (<see cref="BeamSizes"/>);
        /// null: not sized, NOT DESIGNED (the lightest that works is named). Each carries its jacks, on its foot and its top.
        /// </summary>
        [DataMember(Order = 9, EmitDefaultValue = false)] public string HipSize { get; set; }

        /// <summary>The valley rafters' sawn size (as <see cref="HipSize"/>): each carries its jacks, its top on the roof above.</summary>
        [DataMember(Order = 10, EmitDefaultValue = false)] public string ValleySize { get; set; }

        /// <summary>A dormer's headers' sawn size (as <see cref="HipSize"/>): each carries the rafters cut at the dormer's front, trimmer to trimmer.</summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public string HeaderSize { get; set; }

        /// <summary>
        /// The ridge boards' sawn size (as <see cref="HipSize"/>) where they carry something (a hip's top, a rafter with no pair
        /// across the ridge), held by the rafter pairs crossing them.
        /// </summary>
        [DataMember(Order = 12, EmitDefaultValue = false)] public string RidgeBoardSize { get; set; }

        /// <summary>Its purlins (owner 2026-11-12): each a support of the rafters crossing it, braced to a bearing wall.</summary>
        [DataMember(Order = 13, EmitDefaultValue = false)] public List<RoofPurlin> Purlins { get; set; }

        /// <summary>The purlins' sawn size (as <see cref="HipSize"/>); null: the rafters' (IRC R802.4.5: no smaller than them).</summary>
        [DataMember(Order = 14, EmitDefaultValue = false)] public string PurlinSize { get; set; }

        /// <summary>The purlins' braces' sawn size; null: 2x4 (IRC R802.4.5).</summary>
        [DataMember(Order = 15, EmitDefaultValue = false)] public string BraceSize { get; set; }

        public Roof Clone()
        {
            var r = (Roof)MemberwiseClone();
            r.Sections = Sections?.Select(s => s.Clone()).ToList();
            r.Purlins = Purlins?.Select(p => p.Clone()).ToList();
            return r;
        }

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

        /// <summary>Wall studs (format version 5): the nominal size on the first story (on the foundation) and above, and the spacing (in).</summary>
        [DataMember(Order = 8)] public string StudSizeFirstStory { get; set; }
        [DataMember(Order = 9)] public string StudSizeAbove { get; set; }
        [DataMember(Order = 10)] public double StudSpacing { get; set; }

        /// <summary>
        /// The studs' lumber (format version 5): species ("DouglasFirLarch", "HemFir", "SprucePineFir", "SouthernPine") and
        /// grade ("No2", "No3", "Stud"), as the NDS library names them; and the walls' deflection limit L/n under wind
        /// (IBC Table 1604.3: 240 with brittle finishes).
        /// </summary>
        [DataMember(Order = 11)] public string StudSpecies { get; set; }
        [DataMember(Order = 12)] public string StudGrade { get; set; }
        [DataMember(Order = 13)] public double WallDeflectionLimit { get; set; }

        /// <summary>The roof's rafters or trusses: their spacing (in), for the wind uplift on each one's tie to the wall, and the roof a beam running along them carries.</summary>
        [DataMember(Order = 14)] public double RafterSpacing { get; set; }

        /// <summary>The floors' joists (nominal sawn size): their depth is the rim's, a collector where no wall is under a line of shear walls.</summary>
        [DataMember(Order = 15)] public string FloorJoistSize { get; set; }

        /// <summary>
        /// The roof's blocking between its rafters or trusses (nominal sawn size): a continuous line of it, strapped across every
        /// joint, is the roof diaphragm's collector or chord where no wall is under it (owner 2026-11-09).
        /// </summary>
        [DataMember(Order = 16)] public string RoofBlockingSize { get; set; }

        /// <summary>The floors' joists: their spacing (in), for the floor a beam running along them carries (half a spacing each side).</summary>
        [DataMember(Order = 17)] public double FloorJoistSpacing { get; set; }

        /// <summary>A flat roof's rafters (nominal sawn size): sized by their loads (WoodFraming's joist check), as the floors' joists.</summary>
        [DataMember(Order = 18)] public string RafterSize { get; set; }

        /// <summary>
        /// The bridging or full-depth blocking between the joists and rafters (ft, the most between rows; 0: none but at their
        /// bearings). Each row holds the bottom edge against buckling sideways: the unsupported length ℓu of NDS 3.3.3.4 where
        /// it is in compression (owner 2026-11-12). Each span gets the fewest evenly spaced rows that keep within it.
        /// </summary>
        [DataMember(Order = 19, EmitDefaultValue = false)] public double JoistBridging { get; set; }

        /// <summary>
        /// A gable's ceiling joists (nominal sawn size; owner 2026-11-12, 2x6 to start): at the rafters' spacing, wall to wall
        /// under them, each the tie of its rafter pair at the heels; they carry the ceiling and the attic (the Load Manager's
        /// ceiling entry), checked for that bending with the tie's tension (WoodFraming).
        /// </summary>
        [DataMember(Order = 20)] public string CeilingJoistSize { get; set; }

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
            StudSizeFirstStory = "2x6";
            StudSizeAbove = "2x4";
            StudSpacing = 16;
            StudSpecies = "DouglasFirLarch";
            StudGrade = "No2";
            WallDeflectionLimit = 240;
            RafterSpacing = 24;
            FloorJoistSize = "2x10";
            RoofBlockingSize = "2x6";
            FloorJoistSpacing = 16;
            RafterSize = "2x10";
            CeilingJoistSize = "2x6";
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

        /// <summary>
        /// The wall joints whose usual rule the engineer turned round (format version 7; none in an older file): at an L, the
        /// longer wall runs past the shorter, an E-W one past an N-S one of the same length (<see cref="WallJoints"/>); a joint
        /// listed here (<see cref="WallJoints.Key"/>) the other way. Only the turned ones are kept.
        /// </summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public List<string> FlippedJoints { get; set; } = new List<string>();

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
            FlippedJoints = FlippedJoints ?? new List<string>();
            Defaults = Defaults ?? new ModelDefaults();
            Roof = Roof ?? Defaults.NewRoof();
            foreach (var d in Diaphragms) { d.Pieces = d.Pieces ?? new List<Rect2>(); d.Framing = d.Framing ?? new List<FramingArea>(); }
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

        /// <summary>Every level, diaphragm, opening, wall, note, beam and column, and each diaphragm's framing areas (after it).</summary>
        public IEnumerable<ModelObject> Objects() =>
            Levels.Cast<ModelObject>().Concat(Diaphragms).Concat(Diaphragms.SelectMany(d => d.Framing ?? Enumerable.Empty<FramingArea>())).Concat(Openings).Concat(Walls).Concat(Notes ?? Enumerable.Empty<Note>())
                  .Concat(Beams ?? Enumerable.Empty<Beam>()).Concat(Columns ?? Enumerable.Empty<Column>());

        /// <summary>The object with this ID (the building itself for its own ID), or null.</summary>
        /// <summary>The object with this ID; the roof's ridge beam (a vault's, <see cref="RoofQueries.RidgeBeam"/>) by its ID too.</summary>
        public ModelObject Find(string id) => id == null ? null : id == Id ? this
            : Objects().FirstOrDefault(o => o.Id == id) ?? (id == RoofQueries.RidgeBeamId ? this.RidgeBeam() : null);

        // ------------------------------------------------------------------ wall framing (the wall's own, else the project's)

        /// <summary>The story under <paramref name="topLevelId"/> stands on the lowest level (the foundation).</summary>
        public bool IsFirstStory(string topLevelId)
        {
            var top = Levels.FirstOrDefault(l => l.Id == topLevelId);
            if (top == null) return false;
            var below = Levels.Where(l => l.Elevation < top.Elevation).OrderByDescending(l => l.Elevation).FirstOrDefault();
            return below != null && !Levels.Any(l => l.Elevation < below.Elevation);
        }

        /// <summary>The wall's own framing on the story under <paramref name="topLevelId"/>, or null.</summary>
        public static StudFraming OwnFraming(Wall w, string topLevelId) =>
            w?.Framing != null && topLevelId != null && w.Framing.TryGetValue(topLevelId, out var f) ? f : null;

        /// <summary>The wall's stud size on the story under <paramref name="topLevelId"/>: its own, else the project's default for the story.</summary>
        public string StudSizeOf(Wall w, string topLevelId) =>
            OwnFraming(w, topLevelId)?.StudSize is string s && s.Length > 0 ? s
            : IsFirstStory(topLevelId) ? Defaults.StudSizeFirstStory : Defaults.StudSizeAbove;


        /// <summary>The wall's stud spacing (in) on the story: its own, else the project's.</summary>
        public double StudSpacingOf(Wall w, string topLevelId) =>
            OwnFraming(w, topLevelId)?.StudSpacing is double s && s > 0 ? s : Defaults.StudSpacing;

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
            FramingAreas.Fill(this, d); // its framing: the shorter way (turn it, or parts of it, with FramingAreas)
            return d;
        }

        public Opening AddOpening(Level bottom, Level top, params Rect2[] pieces)
        {
            var o = new Opening { Id = NewId("O"), BottomLevelId = bottom.Id, TopLevelId = top.Id, Pieces = pieces.ToList() };
            Openings.Add(o);
            return o;
        }

        public Wall AddWall(Level baseLevel, Level top, double x0, double y0, double x1, double y1, bool shearWall = false)
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
