# Building Model: design

_2026-11-01. Branch `dev/building-model`._

## Purpose

The building is described once and used by many separate apps:
- the wind load calculator;
- the shear wall designer;
- later, dead/live load and seismic analyzers.

This repo holds the **model** (the data, the geometry rules, the checks and the edits, in `BuildingModel.Core`, no UI),
the **shared sketcher** that draws it (`BuildingModel.Sketch`) and the **Building Model app** (`BuildingModel.App`). It
references no analysis app. Each analysis app holds its own **adapter** that reads the model:
`ASCE7Wind.ModelAdapter` (in the wind repo) and `ShearWallCalculator.ModelAdapter` (in the shear wall repo), so the
dependencies run one way: BuildingModel ← Wind ← ShearWall. The Shear Wall Designer draws in the shared sketcher with
the model as its source of truth, and writes what it draws back into the model.

**One source of truth.** Everything about the building (geometry, roof, project defaults such as a new story's
height) is stored in the model and read from it by every app; no app keeps its own copy or its own hard-coded
default. The code's own values (SDPWS tables, ASCE 7 coefficients) live once, in the app's code library.

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
| **Wall** | A line in plan (W-E or S-N) from a base level to a top level. It can span several levels, such as a lobby wall from the ground to level 3. It is drawn on the level it carries (its top), and a level's plan shows the walls under it. The ground's plan (the foundation plan, `IsFoundationPlan`) has no story under it: it shows the walls and columns standing on it, cut at their base (`WallsShown`), to select and edit there; nothing is drawn on it. Shear walls are declared in the Shear Wall Calculator, not here. |
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
| `WallsOverlap` | warning | Two walls of the same story lie on top of each other along a stretch. |

The shear-wall checks (no shear wall reaching a level in a direction, a shear wall no diaphragm reaches, two shear walls
overlapping) are the Shear Wall Calculator's, on the walls declared there (`ShearWallAdapter.Checks`). A wall here is just a wall:
it carries its gravity load whether or not it is a shear wall.

`BadReference` also covers notes. Each issue has the level whose plan shows it and, where there is one, the point to
mark (`ModelIssue.LevelId`, `At`), so the sketcher draws error (red circle) and warning (amber triangle) markers there.
## File

`ModelFile.Save` / `ModelFile.Load` write and read JSON using `DataContractJsonSerializer`, which is part of .NET, so
no packages are needed. The file holds `Format: "BuildingModel"`, `Version: 3` and a revision number that goes up
with every save. Version 2 adds notes and the per-object `Data` bag (below); version 3 adds the roof and the project
defaults. Older files still open (a flat roof, the program's starting defaults).

## Roof and project defaults (version 3)

- **`Roof`** (one, over the top story): `Shape` Flat / Gable / Hip, `Pitch` (degrees), `Ridge` (gable only: Auto =
  along the longer side, W-E or S-N; a hip's ridge is always along the longer side), `Parapet` (flat roofs) and
  `Overhang` (the eaves), in ft. A sloped roof covers the rectangle enclosing the top floor.
- **`RoofQueries`**: `RoofLevel` (the eave), `RoofPlan`, `EffectiveRidge`, `SpanAcrossRidge`, `RoofRise`,
  `RidgeHeight`, `MeanRoofHeight` (ASCE 7 26.2), `DescribeRoof`. The wind adapter's tests check the engine agrees.
- **Checks**: `RoofPitch` (a sloped roof needs 0° < slope < 90°), `RoofParapet` / `RoofEaves` (not negative),
  `ParapetOnSlopedRoof`, `RoofNotRectangular` (a sloped roof over an L-shaped top floor), `HipRidge`. The code's
  limits (the cladding figures' 45° and 60 ft) are the wind app's checks.
- **`ModelDefaults`**: a new story's height (15 ft), a new roof's slope when made sloped (18.43°, 4:12), parapet (0)
  and eaves (0). Saved with the building; every app reads them from here. `ModelDefaults.Start` is the only place the
  starting values are written.
- **Sketcher:** the Roof button (Building group) opens `RoofDialog`; what the roof comes to and the roof checks update
  as you type; OK (one undo step) is off while there is an error.

## Envelope (`EnvelopeQueries`, in the core)

`building.Envelope()` works out, from the model alone, what any lateral-load app needs. The wind app's
`ASCE7Wind.ModelAdapter.WindAdapter.From(building, windOptions)` turns it into the engine's `BuildingData` with the
model's roof; only the enclosure (`WindOptions`) is the wind app's own input. With more levels than the engine splits
(3) it makes one story and adds a note.

- **`Bounds`, `Length`, `Width`:** the enclosing rectangle of the footprint (L W-E, B S-N). A plan that is not a
  rectangle is noted.
- **`Diaphragms`, `StoryHeights()`, `EaveHeight`:** the levels above the ground that have a diaphragm.
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

`SketchView` wraps it, laid out by purpose (2026-11-02):
- **Left:** a narrow strip of icon buttons for the work on the drawing: Select; Undo, Redo; Wall, Diaphragm, Cut out, Opening, Note; Move, Copy, Delete; Measure. Each tooltip names the command and its key.
- **Bottom:** the view (zoom to the drawing; Snap, Ortho, Grid; Issues, Underlay ▾), then the command box, the prompt and the cursor coordinates.
- **Top:** From size… and Roof… (`ShowBuildingTools`), the level buttons (`ShowLevelBar`) and the host's tools. The bar hides when it is empty.
- **Right-click in Select:** the less used edits (`ContextMenuRequested`): copy and place, copy several, copy to another level (`BuildingEdits.CopyToLevel`), mirror, split this wall here (`SplitWallAt`), join, delete.
- **Grips:** a selected rectangle (diaphragm or opening) has corner grips and a grip in the middle of each side, which moves that side only (`BuildingEdits.MoveSide`).
- **Grid:** 1 ft minor and 10 ft major lines (coarser when the minor lines would be closer than 8 px), labelled in feet along the bottom and left edges.
- **Esc** always returns to Select.
- **Roof window:** the slope is typed as rise in 12 (4:12, 6:12, 8:12 and 12:12 are one click each; degrees with a °). The model still keeps degrees, and an untouched slope keeps its exact angle. `ObjectProperties` edits the selection (wall ends and levels, shear wall or
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

`BuildingModel.exe`: a menu bar replaces the row of buttons. Every command appears once, grouped as **File** (new, open, save, apply changes), **Edit** (undo, the edits with their keys), **Building** (name, new from size, roof, what the analysis apps read), **Levels** (add, insert, copy the story below, delete) and **View** (plan, 3D, zoom). The Plan / 3D switch sits at the right.
It covers New, Open, Save (`*.bldg.json`), + level, insert a mezzanine, copy a story up, delete a level,
the plan and 3D, a properties panel, the issue list (live), a summary of what the analysis apps read (the
envelope and the roof; no analysis app is run), the Roof window, and Apply changes (a change set from an analysis app, with its conflicts).

## Shear wall adapter (`ShearWallCalculator.ModelAdapter`, in the shear wall repo)

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

- **Wind:** use each level's face shares (`BuildingEnvelope.Shares`) in the wind engine's per-level forces.
- **Openings in walls** (doors, windows) for the shear-wall segments, and walls at an angle.
- **Seismic and dead/live load apps** reading the same model, with their own bags.
