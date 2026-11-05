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
  - `ProjectDialogs`, `Ui`, `WindowFit`, `QuickBuildingPanel` ("New from size"), `ModelChecks`.
- **Known issue:** `MainWindow.Save` writes the whole file from memory. If another app saved the file meanwhile, its
  sections are lost. The Design Hub works around this (one app at a time per project). The fix, merge on save, is in
  `..\ShearWallCalculator\ROADMAP.md`.
