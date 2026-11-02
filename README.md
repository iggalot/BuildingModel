# Building Model

The building, described once: levels, diaphragms (with openings, including multistory ones such as lobbies and
mezzanines), walls that can span levels, and notes. Analysis apps (wind loads, shear walls, later dead/live loads and
seismic) read it, and each keeps its own inputs on top of it. The design is in `docs/DESIGN.md`.

All repos are local only (no remote). Branch: `dev/building-model`.

## Projects

| Project | What it is |
|---|---|
| `BuildingModel.Core` | The model (levels, diaphragms, openings, walls, notes, data bags), the geometry rules, the checks, the edits (move, copy, mirror, split, join, stories), the undo history, change sets, typed coordinates, the JSON file. No UI. |
| `BuildingModel.Wind` | The model as the wind engine's inputs: L, B, stories, plate heights, and each level's share of each face. |
| `BuildingModel.Sketch` | The shared WPF sketcher (`SketchCanvas`, `SketchView`): draws and edits the model one level at a time, with snaps, typed points, grips, undo, plan underlays (DXF, image), error markers, notes, 3D (`Building3DView`), a properties panel and an issue list. Apps host it and add their own overlays. |
| `BuildingModel.App` | The standalone Building Model app (`BuildingModel.exe`): draw the building, levels, 3D, checks, wind preview, apply change sets. |
| `BuildingModel.ShearWalls` | The model as the Shear Wall Designer's stories and back (`ShearWallAdapter`). Used by `F:\Programming\ShearWallCalculator`. |
| `BuildingModel.Tests` | Model, edits, history, change sets, wind and shear-wall adapters (MSTest). |
| `BuildingModel.Sketch.Tests` | The sketcher on an STA thread: tools, keys, undo, issues, notes, host hooks, 3D, underlays; renders PNG snapshots when `SNAPSHOT_DIR` is set. |

The solution also lists `..\ShearWallCalculator\ShearWallCalculator.csproj` and
`..\ASCE7-Wind-Load-Calculator\ASCE7Wind.Core\ASCE7Wind.Core.csproj`, which the adapters reference.

## Build and test

```
MSBuild BuildingModel.sln -restore
vstest.console BuildingModel.Tests\bin\Debug\net472\BuildingModel.Tests.dll
vstest.console BuildingModel.Sketch.Tests\bin\Debug\net472\BuildingModel.Sketch.Tests.dll
```

The test packages come from the local NuGet cache only (`NuGet.config`). .NET Framework 4.7.2, no other packages
(Newtonsoft.Json for the shear-wall adapter is taken from the Shear Wall Calculator's `packages` folder).

## Sketcher keys

| Key | Does |
|---|---|
| Esc | Ends the command, then clears the selection |
| W / D / O / N | Wall (chained, square) / Diaphragm (two corners) / Opening / Note |
| M / C / I | Move / Copy (repeats) / Mirror the selection |
| T | Measure |
| X / J | Split a wall at a point / Join two walls in line |
| Del | Delete the selection |
| Z | Zoom to the drawing (wheel zooms at the cursor, middle-drag pans) |
| F3 / F8 | Snaps on/off / Ortho |
| Space or Enter | Repeat the last tool |
| Ctrl+Z, Ctrl+Y, Ctrl+A | Undo, redo, select everything on the level |
| Typing | A point in the command box: `12,30`  `@5,0`  `@20<90`  `20` (toward the cursor); feet and inches `12'6"` |
