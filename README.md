# Building Model

The building, described once: levels, diaphragms (with openings, including multistory ones such as lobbies and
mezzanines), walls that can span levels, notes, the roof and the project's defaults. It is the single source of truth:
analysis apps (wind loads, shear walls, later dead/live loads and seismic) read it, and each keeps its own inputs on top
of it. The design is in `docs/DESIGN.md`.

**Dependencies run one way.** This repo references no analysis app. The wind app (`..\ASCE7-Wind-Load-Calculator`,
`ASCE7Wind.ModelAdapter`) and the shear wall app (`..\ShearWallCalculator`, `ShearWallCalculator.ModelAdapter`) each
hold their own adapter and read this model: BuildingModel ← Wind ← ShearWall.

The repos are on GitHub; commit locally, push only when asked. Branch: `dev/ui-pass-2` (from `dev/building-model`).

## Projects

| Project | What it is |
|---|---|
| `BuildingModel.Core` | The model (levels, diaphragms, openings, walls, notes, roof, project defaults, data bags), the geometry rules and queries (exterior walls, the roof's ridge and mean height, the envelope: L × B and each level's share of each face), the checks, the edits (move, copy, mirror, split, join, cut, stories), the undo history, change sets, typed coordinates, the JSON file. No UI, no analysis app. |
| `BuildingModel.Sketch` | The shared WPF sketcher (`SketchCanvas`, `SketchView`): draws and edits the model one level at a time, with snaps, typed points, grips (move the selection, change a wall's length), undo, plan underlays (DXF, image), error markers, notes, the roof window, 3D (`Building3DView`), a properties panel and an issue list. Apps host it and add their own overlays. |
| `BuildingModel.App` | The standalone Building Model app (`BuildingModel.exe`): draw the building, levels, 3D, checks, a summary of what the apps read, apply change sets. |
| `BuildingModel.Tests` | Model, geometry, envelope, roof and defaults, edits, history, change sets (MSTest). |
| `BuildingModel.Sketch.Tests` | The sketcher on an STA thread: tools, keys, grips, undo, issues, notes, host hooks, roof window, 3D, underlays; renders PNG snapshots when `SNAPSHOT_DIR` is set. |

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
