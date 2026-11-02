# Building Model: design

_2026-11-01. Branch `dev/building-model`._

## Purpose

The building is described once and used by many separate apps:
- the wind load calculator;
- the shear wall designer;
- later, dead/live load and seismic analyzers.

This repo holds the **model** (the data, the geometry rules, the checks and the edits, in `BuildingModel.Core`, no UI),
the **shared sketcher** that draws it (`BuildingModel.Sketch`), the **Building Model app** (`BuildingModel.App`), and the
**adapters** that turn it into each analysis app's inputs (`BuildingModel.Wind`, `BuildingModel.ShearWalls`). The
Shear Wall Designer now draws in the shared sketcher with the model as its source of truth.

The analysis apps never depend on each other. Each one converts the model into its own inputs and keeps its own data
on top of it.

## Rules

1. **Geometry is edited only in the model (the sketcher).**
   - An analysis app adds its own data (for example sheathing for a wall), keyed by the model's IDs.
   - It does not change the geometry.
2. **IDs are stable.**
   - Every level, diaphragm, opening and wall has an ID that survives edits and saves.
   - Apps match their data to the model by ID and can show what changed between two revisions.
3. **The file is versioned** (`Format`, `Version`).
   - A newer version only adds to the format.
   - An older reader refuses a file newer than it understands, instead of guessing.
4. **Start small.** For now the format holds what wind and shear walls need. Openings in walls, materials, load areas
   and seismic weights come in later versions.

## Units and axes

- Units are feet.
- Plan x runs east (W-E) and plan y runs north (S-N).
- Elevations are in feet above the base, which is the ground level at elevation 0.

## Objects

| Object | What it is |
|---|---|
| **Level** | A named elevation where a diaphragm can be, such as "Level 2, 12 ft" or "Mezzanine, 7.5 ft". The ground level (elevation 0) is the base. Levels are set by elevation, not by story count, so a mezzanine is just a level between two others. |
| **Diaphragm** | Drawn on a level as one or more rectangles that merge into one outline (L-shapes, notches). It can be partial: a mezzanine covers part of the footprint. |
| **Opening** | A hole in the floors, drawn once as rectangles, open from a bottom level to a top level. It cuts the diaphragm of every level **strictly between** those two. A lobby open from the ground to the roof cuts every floor in between; a one-floor hole runs from the level below to the level above. |
| **Wall** | A line in plan (W-E or S-N) from a base level to a top level. It can span several levels, such as a lobby wall from the ground to level 3. It is marked as a shear wall or not. |
| **Note** | A short text pinned to a point on a level's plan, for the designer and the checker ("Stair: confirm the opening size"). |

Outlines are kept to right angles, as in the shear wall designer, so areas, centroids and edges are exact. Angled
walls can come later.

## What the model works out

- **Each level's diaphragm:**
  - the gross diaphragm (the union of its rectangles);
  - the net diaphragm (gross minus the openings that cut it);
  - the net area, centroid, edges and extents.
- **The footprint:** the union of all levels' gross diaphragms. Its extents give the building's L and B for wind.
- **Each wall:**
  - its direction;
  - the levels where a diaphragm reaches it (the wall is braced there);
  - its longest unbraced height.

## Checks (`ModelChecks`)

| Code | Severity | When |
|---|---|---|
| `NoGround` | error | There is no level at elevation 0. |
| `LevelElevation` | error | Two levels at the same elevation. |
| `NoDiaphragm` | warning | A level above the ground has no diaphragm. |
| `BadReference` | error | An object names a level that does not exist. |
| `WallNotOrthogonal` | error | A wall is not W-E or S-N, or has no length. |
| `WallLevels` | error | A wall's top is not above its base. |
| `TallWall` | warning | A wall passes a level with no diaphragm at the wall. Check its out-of-plane bending and anchorage over the unbraced height. |
| `OpeningLevels` | error | An opening's top is not above its bottom. |
| `OpeningCutsNothing` | warning | An opening is not inside any diaphragm it should cut. |
| `OpeningAtEdge` | warning | An opening reaches a diaphragm edge, so the chord is interrupted there. |
| `DiaphragmDiscontinuity` | warning | Openings cut more than 50 % of a level's gross diaphragm area. This is ASCE 7-22 Table 12.3-1, horizontal irregularity type 3 (seismic). |
| `NoLateralSupport` | error | A level's diaphragm has no shear wall below it, reaching it, in one direction. |
| `ShearWallNotReached` | warning | A shear wall that no diaphragm reaches at its top or any level it passes: it carries no load. |
| `WallOverlap` | error | Two walls of the same story lie on top of each other along a stretch. |

`BadReference` also covers notes. Each issue has the level whose plan shows it and, where there is one, the point to
mark (`ModelIssue.LevelId`, `At`), so the sketcher draws error (red circle) and warning (amber triangle) markers there.
## File

`ModelFile.Save` / `ModelFile.Load` write and read JSON using `DataContractJsonSerializer`, which is part of .NET, so
no packages are needed. The file holds `Format: "BuildingModel"`, `Version: 2` and a revision number that goes up
with every save. Version 2 adds notes and the per-object `Data` bag (below); version 1 files still open.

## Wind adapter (`BuildingModel.Wind`, done)

`WindAdapter.From(building, roofInputs)` converts the model into a `WindEnvelope`.

- **`Building`:** the wind engine's `BuildingData`.
  - L and B come from the enclosing rectangle of the footprint. A plan that is not a rectangle is noted.
  - Stories and plate heights come from the levels above the ground that have a diaphragm.
  - Roof, overhang, parapet and enclosure come from `RoofInputs`, which are typed because they are not drawn.
  - With more levels than the engine splits (3), the building is one story and a note is added.
- **`Strips`:** each outside face, split along its length wherever a level starts or stops reaching it.
  - Each level takes the wall from mid-span below to mid-span above, between the levels that actually reach the face
    there.
  - Where a floor stops short of the face (for example a lobby at the façade), the wall spans past it.
  - A lower roof beside a set-back story tops the wall there.
  - The ground takes the bottom half of the first span (into the foundation).
- **`Shares(face, eave)`:** each level's share of a face's projected wall area. The shares of one face add up to 1.

The wind engine itself is unchanged. Using the face shares in the engine's per-level forces is the next wind step.

## Data bags (version 2)

Every object (and the building itself) has a `Data` bag: app name → that app's JSON, stored but not interpreted by the
model. Copies keep it; undo and change sets cover it. Keys in use:

| Key | On | Holds |
|---|---|---|
| `ShearWall` | wall | `{ Design: { levelId: WallDesignInputs }, WallType }`: the designer's sheathing, nailing, hold-down per story |
| `ShearWall` | diaphragm | `{ Type }` when not rigid |
| `ShearWall.DesignSettings` | building | the designer's project settings (method, edition, hold-down list) |
| `Sketch.Underlay` | level | the plan under that level (DXF linework or image, scale, position) |

## Edits, undo and change sets (`BuildingModel.Edits`)

- **`BuildingEdits`**: delete, move, copy (with the data bag), array copy, mirror, split a wall at a point, join two
  walls in line, stretch a wall end or a rectangle corner, extents. Stories: add one on top, copy one story onto
  another, delete a story (the levels above come down; taller walls end at the level below; what it does is listed
  first by `DeleteStoryConsequences`), set a story height.
- **`ModelHistory`**: a snapshot of the whole building after each change; undo and redo put a snapshot back into the
  same building object. `Amend` replaces the current step without a new one: an app that fills in its own data after a
  change, an undo or a redo (a new wall's default design) never loses the redo steps.
- **`ChangeSet`**: what was added, deleted or changed between two buildings, by ID, as JSON
  (`Format: "BuildingModelChanges"`). `ApplyTo` applies it to another copy and lists conflicts (the object changed on
  both sides), which can be forced or skipped.
- **`CoordinateInput`**: typed points: `x,y`, `@dx,dy`, `@length<angle`, a length toward the cursor; feet and inches.
- **`Snapper`**: endpoint, midpoint, intersection, perpendicular, nearest and grid snaps; ortho; picking and window
  selection.

## Sketcher (`BuildingModel.Sketch`)

`SketchCanvas` draws one level: its diaphragm pieces and net outline, the openings that cut it (hatched), the walls
under it (shear walls blue), the walls of the level below (gray) and above (dashed), notes, error and warning markers,
the plan underlay, grips, previews, snap glyphs, the level caption and a scale bar. Tools: Select (click, window,
grips), Wall, Diaphragm, Opening, Note, Move, Copy, Mirror, Measure, Split, Calibrate. Every edit goes through
`Commit`, which records an undo step and raises `ModelChanged`.

`SketchView` wraps it with a toolbar of small icon buttons, a level bar, and a status line with the command box,
the prompt and the cursor coordinates. `ObjectProperties` edits the selection (wall ends and levels, shear wall or
not, rectangles, notes, levels; several at once as one undo step). `IssueList` lists the checks and finds each one on
the plan. `Building3DView` shows the building in 3D.

**Host hooks** (how an analysis app puts the sketcher in its own window):

| Hook | Use |
|---|---|
| `DrawUnder`, `DrawOver` | the app's own drawing under or over the plan (its plan image, design marks, centres of mass and rigidity) |
| `WallStyle` | the app's pen for a wall (green / red by its design check) |
| `LevelCaption` | the app's caption for the level shown |
| `Issues` | the markers to draw (the model checks plus the app's own, such as failing walls) |
| `InterceptClick`, `HostWantsSnaps`, `HostFrom`, `ExtraSnapLines` | the app's own two-point commands with the sketch's snaps (plan image scale, straighten, move) and extra lines to snap to (its CAD plan) |
| `InterceptTyped`, `SketchView.FocusCommand` | a value typed for the app's command (a distance) |
| `Escaped`, `ObjectDoubleClicked`, `CursorMoved`, `SelectionChanged`, `ActiveLevelChanged` | the app follows the sketch |
| `ShowLevelBar`, `ShowPlanButton`, `HideTool` | the app hides what it has its own of |

## Building Model app (`BuildingModel.App`)

`BuildingModel.exe`: New, Open, Save (`*.bldg.json`), + level, insert a mezzanine, copy a story up, delete a level,
the plan and 3D, a properties panel, the issue list (live), a building summary, a wind preview through the wind
adapter, and Apply changes (a change set from an analysis app, with its conflicts).

## Shear wall adapter (`BuildingModel.ShearWalls`, done)

The model is the source of truth for the Shear Wall Designer's geometry; the designer keeps its design in the bags.

- **`ToCalculators(building, existing)`**: one designer story (calculator) per level above the ground, reusing the
  given calculators in order (their loads and plan images stay):
  - the net diaphragm (openings taken out) as rectangles, each given to the model diaphragm that holds its centre.
    Key = the diaphragm's ID number (D3 → 3), or n·100 + k when openings split it;
  - the shear walls under the level that its net diaphragm reaches. Key = the wall's ID number (W12 → 12), the same
    on every story the wall spans. Height = from the level below that braces it (or its base) up to this level;
  - each wall's design for that story from its bag, else the project's default for new walls.
- **`StoreDesign`**: the design tabs' changes back into the bags (per wall and story, diaphragm type, settings).
- **`StoreGeometry`**: coordinates typed in the designer's table back into the wall, or into a diaphragm that is one
  rectangle on that story.
- **`FromCalculators`**: an older designer project (calculators only) as a model: level n at the sum of the plate
  heights, walls and diaphragms with their IDs from the keys where free, the design in the bags.
- **`NewBuilding`, `NameStories`, `CopyDesignUp`**: a building from plate heights; default story names (Level 2,
  Level 3, ..., Roof; the user's names stay); a copied story's walls keep their design.

How the designer uses it is in `F:\Programming\ShearWallCalculator\docs\SHARED_SKETCHER.md`.

## Next

- **Wind:** use each level's face shares (`WindAdapter.Shares`) in the wind engine's per-level forces.
- **Openings in walls** (doors, windows) for the shear-wall segments, and walls at an angle.
- **Seismic and dead/live load apps** reading the same model, with their own bags.
