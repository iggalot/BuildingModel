using System;
using System.Linq;
using BuildingModel.Geometry;

namespace BuildingModel.Model
{
    /// <summary>
    /// The roof's geometry worked out from the model: the top level (the eave), the plan it covers, which way the ridge
    /// runs, how high it rises and the mean roof height. A sloped roof covers the rectangle enclosing the top floor.
    /// </summary>
    public static class RoofQueries
    {
        /// <summary>The highest level above the ground: the roof's eave level. Null when there is only the ground.</summary>
        public static Level RoofLevel(this Building b) =>
            b.Levels.Where(l => l.Elevation > 0).OrderByDescending(l => l.Elevation).FirstOrDefault();

        /// <summary>The roof's plan: the rectangle enclosing the top level's diaphragms (empty when there are none).</summary>
        public static Rect2 RoofPlan(this Building b)
        {
            var top = b.RoofLevel();
            return top == null ? default(Rect2) : b.GrossDiaphragm(top).Bounds;
        }

        /// <summary>
        /// Which way the ridge runs: none for a flat roof; along the longer side for a hip roof and for a gable roof left on
        /// Auto (W-E when the plan is square); else the gable's own choice.
        /// </summary>
        public static RidgeAxis EffectiveRidge(this Building b)
        {
            var roof = b.Roof;
            if (roof == null || !roof.IsSloped) return RidgeAxis.Auto;
            if (roof.Shape == RoofShape.Gable && roof.Ridge != RidgeAxis.Auto) return roof.Ridge;
            var plan = b.RoofPlan();
            return plan.Width >= plan.Height ? RidgeAxis.WestEast : RidgeAxis.SouthNorth;
        }

        /// <summary>The ridge beam's ID (it is the roof's, not one of <see cref="Building.Beams"/>).</summary>
        public const string RidgeBeamId = "RIDGE";

        /// <summary>
        /// A vaulted gable's ridge beam (<see cref="Roof.HasRidgeBeam"/>) as a beam on the roof level: along the ridge over the
        /// roof's plan, wall line to wall line, its size the roof's (<see cref="Roof.RidgeBeamSize"/>). Null: none.
        /// </summary>
        public static Beam RidgeBeam(this Building b)
        {
            var top = b.RoofLevel();
            if (top == null || b.Roof == null || !b.Roof.HasRidgeBeam) return null;
            var plan = b.RoofPlan();
            if (plan.IsEmpty) return null;
            bool x = b.EffectiveRidge() == RidgeAxis.WestEast;
            double m = x ? (plan.Y0 + plan.Y1) / 2 : (plan.X0 + plan.X1) / 2;
            return new Beam
            {
                Id = RidgeBeamId, LevelId = top.Id, Size = b.Roof.RidgeBeamSize ?? b.Roof.ForcedRidgeBeamSize,
                Start = x ? new Point2(plan.X0, m) : new Point2(m, plan.Y0), End = x ? new Point2(plan.X1, m) : new Point2(m, plan.Y1),
            };
        }

        /// <summary>The run the roof rises over twice (ft): the plan across the ridge (the shorter side for a hip). 0 when flat.</summary>
        public static double SpanAcrossRidge(this Building b)
        {
            if (b.Roof == null || !b.Roof.IsSloped) return 0;
            var plan = b.RoofPlan();
            if (b.Roof.Shape == RoofShape.Hip) return Math.Min(plan.Width, plan.Height);
            return b.EffectiveRidge() == RidgeAxis.WestEast ? plan.Height : plan.Width;
        }

        /// <summary>
        /// How high the roof's highest point is above the eave (ft); 0 for a flat roof or a slope out of range. A roof of sections
        /// (wings, dormers): the highest point of its faces as laid out (owner 2026-11-13, "follow the codes": ASCE 7 26.2's mean
        /// roof height is the average of the eave height and the height to the highest point on the roof surface), not a hip or
        /// gable spread over the rectangle around the plan.
        /// </summary>
        public static double RoofRise(this Building b)
        {
            var roof = b.Roof;
            if (roof == null || !roof.IsSloped || !(roof.Pitch > 0 && roof.Pitch < 90)) return 0;
            if (roof.Sections != null && roof.Sections.Count > 0)
            {
                var fr = Statics.RoofFrame.Of(b);
                if (fr.NotLaidOut == null && fr.Faces.Count > 0)
                    return Math.Max(0, fr.Faces.SelectMany(f => f.Pieces.SelectMany(c => c.P).Select(q => f.Plane.Z(q))).Max());
            }
            return Math.Tan(roof.Pitch * Math.PI / 180) * b.SpanAcrossRidge() / 2;
        }

        /// <summary>The eave height: the top level's elevation (ft above the base); 0 when there is only the ground.</summary>
        public static double EaveHeight(this Building b) => b.RoofLevel()?.Elevation ?? 0;

        /// <summary>The ridge height above the base (ft); the eave height for a flat roof.</summary>
        public static double RidgeHeight(this Building b) => b.EaveHeight() + b.RoofRise();

        /// <summary>Mean roof height h (ft): the average of the eave and ridge heights (ASCE 7 26.2).</summary>
        public static double MeanRoofHeight(this Building b) => b.EaveHeight() + b.RoofRise() / 2;

        /// <summary>One line about the roof: "Gable 4.0:12 (18.4°), ridge W-E, rises 6.67 ft; eaves 2 ft".</summary>
        public static string DescribeRoof(this Building b)
        {
            var r = b.Roof ?? new Roof();
            string extras = (r.Overhang > 0 ? $"; eaves {r.Overhang:0.##} ft" : "") + (!r.IsSloped && r.Parapet > 0 ? $"; parapet {r.Parapet:0.##} ft" : "") + (r.IsSloped && r.NoCeilingJoists ? "; no ceiling joists (a vault)" : "");
            if (!r.IsSloped) return "Flat roof" + extras;
            string ridge = b.EffectiveRidge() == RidgeAxis.WestEast ? "W-E" : "S-N";
            if (r.Sections != null && r.Sections.Count > 0)
            {
                // a roof of sections: each its own shape; the highest point is the faces' (ASCE 7 26.2)
                string Of(RoofSection s) => $"{s.Name ?? "a section"} {s.Shape.ToString().ToLowerInvariant()}{(s.Shape == RoofShape.Shed ? "" : " " + (s.Ridge == RidgeAxis.WestEast ? "W-E" : s.Ridge == RidgeAxis.SouthNorth ? "S-N" : "along the longer side"))}{(s.EaveRise > 1e-9 ? $", eave +{s.EaveRise:0.##} ft" : s.EaveRise < -1e-9 ? $", eave {s.EaveRise:0.##} ft" : "")}";
                return $"{r.RisePer12} ({r.Pitch:0.#}°), {r.Sections.Count} section{(r.Sections.Count == 1 ? "" : "s")} ({string.Join("; ", r.Sections.Select(Of))}), its highest point {b.RoofRise():0.##} ft above the eave" + extras;
            }
            return $"{r.Shape} {r.RisePer12} ({r.Pitch:0.#}°), ridge {ridge}, rises {b.RoofRise():0.##} ft" + extras;
        }
    }
}
