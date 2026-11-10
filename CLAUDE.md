# Building Model: notes for assistants

- **Read first:**
  - `..\DesignHub\docs\SUITE.md`: all seven repos, how they fit, the branches, the build, and **the owner's rules**
    (local commits only, no installs, stay in the project folders, never click the owner's windows).
  - `README.md`, then `docs/DESIGN.md`.
- **Branch:** `dev/ui-pass-2`.
- **What this is:** the backbone. The building is drawn once and every app reads it from one project file
  (`<folder>\<folder>.bldg.json`).
  - **No hard-coded engineering defaults anywhere:** they live in `ModelDefaults`, in the file. They are edited in
    Building > Model defaults (`BuildingModel.Sketch.ModelDefaultsWindow`, one undo step): the new stories and roofs, and
    the framing (studs, lumber, rafters, `FloorJoistSize`, `RoofBlockingSize`).
  - **No loads in these classes:** loads are the Load Manager's section.
  - **This repo references no analysis app.** The wind and shear wall adapters live in their apps.
- **Build:** `MSBuild BuildingModel.sln -restore`.
- **Test:** `BuildingModel.Tests` and `BuildingModel.Sketch.Tests` (the sketcher on an STA thread, offscreen;
  `SNAPSHOT_DIR` saves PNGs). Never create `MainWindow` in a test.
- **Shared pieces the other apps use:**
  - `ProjectFolder` and `Log`: the project folder and its `logs\`.
  - `ProjectDialogs` (with `SaveChangesFirst` and `TakeLock`, the suite's "Save first?" and "already open"), `Ui`,
    `WindowFit`, `QuickBuildingPanel` ("New from size"), `ModelChecks`.
  - `EditHistory` (`BuildingModel.Core\Edits`): the suite's one undo and redo (text snapshots, step names, the saved
    step for the unsaved `*`). `ModelHistory` is it with the building as the snapshot; `SketchCanvas.Commit(what)`
    names the step.
  - Drawing on a CAD plan that shows the walls' faces (owner 2026-11-09): `Snapper` with `SnapModes.WallCentre` (midway
    between two lines 3.5 to 7.25 in apart, the plan's lines only); `SketchCanvas.SnapToPlan` (the **Plan snap** toggle:
    off, nothing on the underlay snaps, for a busy plan); `SketchCanvas.WallDrawnBy` (**Wall: centreline / left face /
    right face**, `WallsFromPlan.CentreOf`, `RunJoint`); **Walls from the plan…** (Underlay ▾, editor only:
    `WallsFromPlan.Propose`, `PlanWallReview`, `PlanWallsWindow`, one undo step a wall). `SnapAt(point)` is the cursor's
    snap, for the tests.
  - `ProjectWatcher` (`BuildingModel.Core\Serialization`): "the project changed on disk", after a quiet time, without
    the app's own save (`SavedByMe`). Every app that watches the file uses it.
  - `LateralSection` (`BuildingModel.Core\Model`): what the lateral design relays to the model, the foundation points with
    their forces per wind case and their connector. The sketch draws them on the Ground plan (`Connections`, a host may
    give live ones; `ShowConnections`). The format is here so every app reads it without reading another app. Its `Cases` are each wind case once for each way the shear
    walls share it (`<name> (rigid)`, `(flexible)`; `WindOf` gives the wind's name). Its `Walls` are every declared shear
    wall (designed or not): the sketch bands each one's shear part on the plans (`ShowShearParts`; SWC, which draws its own,
    turns it off), so the Building Model shows where the hold-downs go.
  - `ProjectStatus` (`BuildingModel.Core\Model`): the hub's mark of the open problems ("Hub.Status"), here so every app reads
    it without the hub; the hub writes it (`DesignHub.HubStatus.From`), the apps show their steps' items on opening (`Words`).
  - `ConnectionPairs` (`BuildingModel.Core\Model`): every story hold-down as a pair of equal and opposite forces (member
    above, member below or the foundation), and the one rule every drawing uses to show a pair's halves (`Drawn`: the
    Internal forces toggle, `SketchCanvas.ShowInternalForces`, the selection as the body, WISE), and those landing
    within 1 ft together (`Near`, `Sum`). The Load Manager's free bodies read the same pairs.
  - `ShearPairs` (`BuildingModel.Core\Model`): the horizontal pairs, each shear wall story's shear at its top (into its
    diaphragm) and its sill (into the floor, or the foundation's anchor bolts), and each diaphragm's free body from them
    and its own wind (`Balance`, with `LateralSection.Diaphragms`, `DiaphragmWind`, which SWC saves). Each pair has
    `StoryLevelId` (the level on top of its wall's story) and generic words for its connector; `Of`'s optional describer
    gives the designed ones. `Drawn` says which half a plan shows (the hold-downs' WISE rule); a picked diaphragm is in
    the body as `DiaphragmKey(levelId)`, and the plan draws its free body in one case (`SketchCanvas.FreeBodyCase`,
    `DiaphragmCase`).
  - **Connection details are not in this model (owner 2026-11-07: no god class).** Nailing, sill bolts, diaphragm lines and
    their defaults are `..\WoodFraming\WoodFraming.Connections` (its own "Connections" and "ConnectionDefaults" sections,
    keyed by this model's ids; it references BuildingModel.Core, never the other way). The forces stay here: the
    hold-downs, the points, the pairs, the diaphragms' wind (`LateralSection`), and each shear wall's sheathing
    (`ShearFace`: its nailing is in the Connections, owner 2026-11-08). The BuildingModel.App references
    WoodFraming.Connections only for the shared wall window.
  - `ModelFingerprint` (`BuildingModel.Core\Serialization`): short hashes of the geometry (no names or notes), a data
    section, or an app's data. An app stamps what a result was found from (`LateralSection.BasedOn`); `Changed` says
    which part differs now.
- **Several apps, one file** (`BuildingModel.Core\Serialization`; 2026-11-05):
  - `ModelFile.Save` is atomic. It writes `<model>.saving`, then replaces the model, keeping the old one as
    `<model>.bak`. Watchers see a rename: `ProjectWatcher` handles it.
  - `ModelMerge` is a three-way merge (by object ID, and by data key on each object). `ModelFile.SaveMerged(path,
    doc, base, owns)` is used by the Building Model: its own keys (`Sketch.*` plan images) merge, and every other key
    is always the file's. A stale copy in memory or in undo can never overwrite another app's data.
  - `ModelFile.SaveOwnData(path, mine, owns)` is for an app that writes no geometry (the Shear Wall Calculator). It
    re-reads the file and copies only its keys.
  - `ProjectLock` is `<model>.<appId>.lock` (hidden), held open while an app has the project. There is one copy per
    app per project; a second copy is refused. `Holders(model)` lists who has it, on any PC sharing the folder. A lock
    left by a crash is stale and taken over. The app ids are `building`, `wind`, `shearwalls`, `loads` and `hub`
    (`HubId`). One person per project: `HeldByOthers` and `OthersMessage` name another person or PC that has it, and
    `ProjectDialogs.TakeLock` refuses it then, or with `offerViewOnly` asks "Open it view only?" (no lock taken).
  - **View only** (2026-11-09): `SketchCanvas.ViewOnly` puts back every edit at `Commit` (the geometry and the apps' data:
    `ModelHistory.Differs`; `EditRefused` for a host that changed its own copies), and `SketchView.ViewOnly` leaves only
    Select and Measure (false puts the tools back). The Building Model, SWC and the Load Manager use it.
  - `SketchCanvas.GeometryLocked`: a host that only shows the building (the Shear Wall Calculator) gets any edit that
    moves, adds or deletes something put back. The apps' data on objects still changes.
- **Geometry is edited only here.** The other apps show it, and open this app on the project (Edit building).
- **One selection for every app** (`SketchCanvas`, 2026-11-05): click, Shift or Ctrl+click, a window, Ctrl+A, Esc, Delete,
  `Selection` and `SelectionChanged`, `SelectedOf<T>()`. An app never builds its own picking: it says what it picks
  (`PickFilter`), adds its own objects (`HostOwns`, `HostPick`, `HostPickFirst`, `HostInWindow`, `SelectedHostIds`) and
  deletes them (`HostDeleteRequested`). With `GeometryLocked` there are no grips. The Shear Wall Calculator and the Load
  Manager use it this way.
- **Walls are just walls here** (2026-11-05): no shear-wall switch, all drawn alike. The shear walls (the part of a wall
  that counts, its sheathing) are declared in the Shear Wall Calculator, which also does the shear-wall checks.
  `Wall.IsShearWall` is only read from older files.
