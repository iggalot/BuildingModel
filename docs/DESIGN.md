# Building Model: design

_2026-11-01. Branch `dev/building-model`._

## Purpose

The building is described once and used by many separate apps:
- the wind load calculator;
- the shear wall designer;
- later, dead/live load and seismic analyzers.

This repo holds the **model**: the data, the geometry rules and the checks. It has no UI. A sketcher app will write it,
and the analysis apps will read it.

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

## File

`ModelFile.Save` / `ModelFile.Load` write and read JSON using `DataContractJsonSerializer`, which is part of .NET, so
no packages are needed. The file holds `Format: "BuildingModel"`, `Version: 1` and a revision number that goes up
with every save.

## How the other apps will use it (later steps)

- **Wind:**
  - A converter turns the model into the wind engine's `BuildingData`: L and B from the footprint, then the level
    elevations and the roof.
  - It also works out each level's share of each outside face from the diaphragm edges actually present. Where a
    double-height lobby leaves no floor at the façade, that wall's wind goes to the floors above and below it.
  - Standalone, the wind app still takes typed inputs.
- **Shear walls:**
  - Levels, diaphragms (net, so openings are taken out of the mass) and shear walls, with the design data kept by
    wall ID.
  - Its own drawing tools stay until the sketcher replaces them.
- **Sketcher:** draws the model (levels, rectangles, openings, walls), imports DXF and shows it in 3D. It will be a
  separate app; it is not in this repo until approved.
