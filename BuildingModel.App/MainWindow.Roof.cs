using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;
using BuildingModel.Statics;
using BuildingModel.Sketch;

namespace BuildingModel.App
{
    /// <summary>
    /// The roof framing dialog's one-click sizes (owner 2026-11-14, UI audit D1): for each kind of roof member not sized, the
    /// lightest sawn size that carries the gravity loads on every member of the kind (WoodFraming's roof check, the Load
    /// Manager's, with no wind: the Building Model has none). The wind is checked with that size in the Load Manager's Joists
    /// window, which says NOT DESIGNED or fails it there if the wind needs more.
    /// </summary>
    public partial class MainWindow
    {
        private static void HookRoofSizes() => RoofFramingPanel.LightestSizes = LightestRoofSizes;

        /// <summary>The kinds ("hip", "valley", "header", "ridgeboard", "cheek") with the lightest size for all their members.</summary>
        internal static IReadOnlyDictionary<string, string> LightestRoofSizes(Building b)
        {
            var found = new Dictionary<string, string>();
            var lv = b?.RoofLevel();
            if (lv == null || b.Roof == null || !b.Roof.IsSloped || !RoofFrame.IsSectioned(b)) return found;
            var def = b.Defaults ?? new ModelDefaults();
            var species = LoadManager.FramingWeight.SpeciesOf(b);
            var grade = Enum.TryParse(def.StudGrade, out NDS_WoodLibrary.NDS2018.Grade g) ? g : NDS_WoodLibrary.NDS2018.Grade.No2;
            var results = WoodFraming.RoofFramingCheck.Run(b, LoadManager.LoadSection.Read(b) ?? new LoadManager.LoadSection(), lv, null, null,
                                                            species, grade, WoodFraming.StandardHardware.For(b, null));
            string KindOf(string area) =>
                area == null ? null : area.StartsWith("hip rafter") ? "hip" : area.StartsWith("valley rafter") ? "valley" : area.StartsWith("header") ? "header"
                : area.StartsWith("ridge board") ? "ridgeboard" : area.StartsWith("cheek beam") ? "cheek" : null;
            foreach (var kind in results.GroupBy(r => KindOf(r.Area)).Where(k => k.Key != null))
            {
                if (kind.Any(r => r.Works == null)) continue; // one none works for: no one size for them all
                var z = kind.Select(r => r.Works).OrderBy(x => Array.IndexOf(BeamSizes.All, x)).Last();
                found[kind.Key] = z;
            }
            return found;
        }
    }
}
