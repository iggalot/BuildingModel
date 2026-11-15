using BuildingModel.Checks;
using BuildingModel.Diagnostics;
using BuildingModel.Sketch;

namespace BuildingModel.App
{
    /// <summary>
    /// The plan's view modes, layers, status filter and saved views (owner 2026-11-14, UI audit A1, A2), in the plan's top bar.
    /// The plan's own buttons (Grid, Issues, Underlay) are layers too: a mode sets them, they still work one by one. A saved
    /// view is this app's own data in the project (<c>Views.building</c>), one undo step, saved with the building.
    /// </summary>
    public partial class MainWindow
    {
        private ViewBar viewBar;

        private ViewBar CreateViewBar()
        {
            viewBar = new ViewBar(Sketch) { Margin = new System.Windows.Thickness(8, 0, 0, 0) };
            viewBar.Layers.Add(new ViewLayer("grid", "Grid", () => Sketch.ShowGrid, v => Sketch.ShowGrid = v));
            viewBar.Layers.Add(new ViewLayer("issues", "Issues", () => Sketch.ShowIssues, v => Sketch.ShowIssues = v, "The model's issues on the plan"));
            viewBar.Layers.Add(new ViewLayer("underlay", "Underlay", () => Sketch.ShowUnderlay, v => Sketch.ShowUnderlay = v, "The drawing under this level, to trace"));
            viewBar.Layers.Add(new ViewLayer("below", "Level below", () => Sketch.ShowLevelBelow, v => Sketch.ShowLevelBelow = v, "The level below, faint, under this one"));
            viewBar.Layers.Add(new ViewLayer("spans", "Spans", () => Sketch.ShowSpans, v => Sketch.ShowSpans = v, "Each framing area's span arrow and size"));

            viewBar.Modes.Add(new ViewMode("Model", "Drawing and editing the building: the grid, the issues, the level below, the spans", "grid", "issues", "below", "spans", "underlay")
                              { Labels = LabelDensity.Critical, Status = StatusFilter.All });
            viewBar.Modes.Add(new ViewMode("Problems", "Only what fails or is NOT DESIGNED in colour, with the issues: the rest plain", "issues", "spans")
                              { Labels = LabelDensity.Critical, Status = StatusFilter.Problems });
            viewBar.Modes.Add(new ViewMode("Trace", "The underlay and the grid, to draw over: no issues, no level below", "grid", "underlay")
                              { Labels = LabelDensity.Critical, Status = StatusFilter.All });
            viewBar.Modes.Add(new ViewMode("Presentation", "The building alone, every label: no grid, no issues", "spans") { Labels = LabelDensity.All, Status = StatusFilter.All });
            viewBar.ReadViews = () => SavedViews.Read(Sketch.Building, SuiteApps.Building);
            viewBar.WriteViews = (views, what) =>
            {
                if (Sketch.Building == null) return;
                SavedViews.Write(Sketch.Building, SuiteApps.Building, views);
                Sketch.Commit(what); // one undo step; saved with the building (this app's own data)
                Log.Info("Views", what);
            };
            viewBar.Changed += (s, e) =>
            {
                Log.Info("Views", $"View: {viewBar.Mode ?? "custom"}; labels {LabelDensities.Name(Sketch.Labels)}; {StatusFilters.Name(Sketch.ShowStates)}");
                view.UpdateButtons();
                Sketch.Refresh();
            };
            viewBar.Fill();
            return viewBar;
        }
    }
}
