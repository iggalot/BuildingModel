# Building Model: notes for assistants

- **Read first:**
  - `..\DesignHub\docs\SUITE.md`: all seven repos, how they fit, the branches, the build, and **the owner's rules**
    (local commits only, no installs, stay in the project folders, never click the owner's windows).
  - `README.md`, then `docs/DESIGN.md`.
- **Branch:** `dev/ui-pass-2`.
- **What this is:** the backbone. The building is drawn once and every app reads it from one project file
  (`<folder>\<folder>.bldg.json`).
  - **No hard-coded engineering defaults anywhere:** they live in `ModelDefaults`, in the file.
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
  - `ProjectWatcher` (`BuildingModel.Core\Serialization`): "the project changed on disk", after a quiet time, without
    the app's own save (`SavedByMe`). Every app that watches the file uses it.
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
    left by a crash is stale and taken over. The app ids are `building`, `wind`, `shearwalls` and `loads`.
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
