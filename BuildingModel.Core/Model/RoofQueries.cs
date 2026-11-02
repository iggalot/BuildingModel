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

        /// <summary>The run the roof rises over twice (ft): the plan across the ridge (the shorter side for a hip). 0 when flat.</summary>
        public static double SpanAcrossRidge(this Building b)
        {
            if (b.Roof == null || !b.Roof.IsSloped) return 0;
            var plan = b.RoofPlan();
            if (b.Roof.Shape == RoofShape.Hip) return Math.Min(plan.Width, plan.Height);
            return b.EffectiveRidge() == RidgeAxis.WestEast ? plan.Height : plan.Width;
        }

        /// <summary>How high the ridge is above the eave (ft); 0 for a flat roof or a slope out of range.</summary>
        public static double RoofRise(this Building b)
        {
            var roof = b.Roof;
            if (roof == null || !roof.IsSloped || !(roof.Pitch > 0 && roof.Pitch < 90)) return 0;
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
            string extras = (r.Overhang > 0 ? $"; eaves {r.Overhang:0.##} ft" : "") + (!r.IsSloped && r.Parapet > 0 ? $"; parapet {r.Parapet:0.##} ft" : "");
            if (!r.IsSloped) return "Flat roof" + extras;
            string ridge = b.EffectiveRidge() == RidgeAxis.WestEast ? "W-E" : "S-N";
            return $"{r.Shape} {r.RisePer12} ({r.Pitch:0.#}°), ridge {ridge}, rises {b.RoofRise():0.##} ft" + extras;
        }
    }
}
