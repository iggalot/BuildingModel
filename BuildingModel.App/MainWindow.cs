using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BuildingModel.Checks;
using BuildingModel.Diagnostics;
using BuildingModel.Edits;
using BuildingModel.Model;
using BuildingModel.Serialization;
using BuildingModel.Sketch;

namespace BuildingModel.App
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // the log: %AppData%\BuildingModel\logs until a project is open, then the project's logs folder
            Diagnostics.Log.Start("BuildingModel", "bldg");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Diagnostics.Log.Error("App", "Unhandled error (the program is closing)" + (e.IsTerminating ? "" : " (not terminating)"), e.ExceptionObject as Exception);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Diagnostics.Log.Error("App", "Unexpected error; the action was cancelled", e.Exception);
                MessageBox.Show("Something went wrong and that action was cancelled, but your work is still open.\n\n" + e.Exception.Message +
                                "\n\nThe details are in the log:\n" + Diagnostics.Log.TodayFile, "Unexpected problem", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
            app.Exit += (s, e) => Diagnostics.Log.Info("App", "Building Model closed.");
            var w = new MainWindow();
            if (args.Length > 0)
            {
                Diagnostics.Log.Info("App", "Started with " + args[0]);
                if (File.Exists(args[0])) w.Open(args[0]);
                else Diagnostics.Log.Warning("App", "The file to open does not exist: " + args[0], null);
            }
            return app.Run(w);
        }
    }

    /// <summary>
    /// The Building Model app: draw the building once (levels, diaphragms, openings, walls, notes), see its issues as you
    /// go, look at it in 3D, check what the wind calculator will get, and take back the changes another app proposes.
    /// </summary>
    public sealed class MainWindow : Window
    {
        public const string FileFilter = "Building model (*.bldg.json)|*.bldg.json|JSON (*.json)|*.json|All files (*.*)|*.*";

        private readonly SketchView view = new SketchView();
        private readonly Building3DView view3D = new Building3DView { Visibility = Visibility.Collapsed };
        private readonly ObjectProperties properties;
        private readonly IssueList issueList;
        private readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, FontSize = 11.5 };
        private readonly ToggleButton planTab, tab3D;

        private ModelDocument doc = new ModelDocument();
        private string path;
        private bool dirty;

        private SketchCanvas Sketch => view.Canvas;

        public MainWindow()
        {
            Width = 1360;
            Height = 820;
            MinWidth = 900;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowFit.Fit(this); // a smaller screen: fits, title bar on screen
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            Background = Brushes.White;
            properties = new ObjectProperties(Sketch);
            issueList = new IssueList(Sketch);

            // the menu: every command once, named and grouped by what it works on (the keys are shown beside them)
            var menu = new Menu { Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            MenuItem Item(ItemsControl parent, string header, string keys, Action a, string tip = null)
            {
                var m = new MenuItem { Header = header, InputGestureText = keys ?? "", ToolTip = tip };
                if (a != null) m.Click += (s, e) => a();
                parent.Items.Add(m);
                return m;
            }
            var file = Item(menu, "_File", null, null);
            Item(file, "_New project…", "Ctrl+N", () => New(), "A new design project: pick or make its folder");
            Item(file, "_Open…", "Ctrl+O", OpenDialog);
            Item(file, "_Save", "Ctrl+S", () => Save(false));
            Item(file, "Save _as…", null, () => Save(true), "Save as a new project folder");
            file.Items.Add(new Separator());
            Item(file, "Apply changes from another app…", null, ApplyChanges, "Apply a change set proposed by another app (the shear wall designer)");
            file.Items.Add(new Separator());
            Item(file, "E_xit", "Alt+F4", Close);

            var edit = Item(menu, "_Edit", null, null);
            Item(edit, "_Undo", "Ctrl+Z", () => Sketch.Undo());
            Item(edit, "_Redo", "Ctrl+Y", () => Sketch.Redo());
            edit.Items.Add(new Separator());
            Item(edit, "Select all on this level", "Ctrl+A", () => Sketch.Select(Sketch.Building.VisibleIds(Sketch.ActiveLevel)));
            Item(edit, "_Delete", "Del", () => Sketch.DeleteSelected());
            edit.Items.Add(new Separator());
            Item(edit, "_Move", "M", () => Sketch.SetTool(SketchTool.Move));
            Item(edit, "_Copy and place", "Ctrl+C", () => Sketch.CopyAndPlace(), "One copy of the selection follows the cursor straight across or up; click to place it");
            Item(edit, "Copy several", "C", () => Sketch.SetTool(SketchTool.Copy));
            Item(edit, "M_irror", "I", () => Sketch.SetTool(SketchTool.Mirror));
            Item(edit, "_Split a wall", "X", () => Sketch.SetTool(SketchTool.Split));
            Item(edit, "_Join two walls", "J", () => Sketch.JoinSelected());
            Item(edit, "Measure", "T", () => Sketch.SetTool(SketchTool.Measure));

            var building = Item(menu, "_Building", null, null);
            Item(building, "_Name…", null, RenameBuilding);
            Item(building, "New from _size…", null, () => view.NewFromSize(), "A box L × B with its stories, a diaphragm on each level and the outside shear walls (replaces the drawing; Undo puts it back)");
            Item(building, "_Roof…", null, () => view.EditRoof(), "Flat, gable or hip; the slope (4:12, 6:12...), ridge, parapet and eaves");
            building.Items.Add(new Separator());
            Item(building, "What the analysis apps _read…", null, BuildingSummary, "L, B, stories, the roof and each level's share of each face, as the wind and shear wall apps read them");

            var levels = Item(menu, "_Levels", null, null);
            Item(levels, "_Add a level on top…", null, AddLevel, "Asks the story height");
            Item(levels, "_Insert a level under this one…", null, InsertLevel, "A mezzanine or an added floor");
            Item(levels, "_Copy the story below onto this level", null, CopyStoryBelow);
            levels.Items.Add(new Separator());
            Item(levels, "_Delete this level…", null, DeleteLevel, "The level shown and the story under it; the levels above come down");

            var viewMenu = Item(menu, "_View", null, null);
            Item(viewMenu, "_Plan", null, () => ShowPlan(true));
            Item(viewMenu, "_3D", null, () => ShowPlan(false));
            viewMenu.Items.Add(new Separator());
            Item(viewMenu, "_Zoom to the drawing", "Z", () => Sketch.ZoomExtents());

            var bar = new DockPanel { LastChildFill = false };
            var title = new TextBlock { Text = "Building Model", FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
            bar.Children.Add(title);
            bar.Children.Add(menu);
            planTab = Ui.Toggle("", "Plan", "The plan of the level shown", (s, e) => ShowPlan(true));
            tab3D = Ui.Toggle("", "3D", "The building in 3D", (s, e) => ShowPlan(false));
            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            tabs.Children.Add(planTab);
            tabs.Children.Add(tab3D);
            DockPanel.SetDock(tabs, Dock.Right);
            bar.Children.Add(tabs);
            view.ShowBuildingTools = false; // they are in the Building menu

            var appBar = new Border { Child = bar, Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 3, 6, 3) };

            // the side panel: properties, issues, summary
            var side = new DockPanel { Width = 290, Background = Ui.Bar };
            var propsBox = Section("Properties", properties);
            DockPanel.SetDock(propsBox, Dock.Top);
            side.Children.Add(propsBox);
            var sumBox = Section("Building", summary);
            DockPanel.SetDock(sumBox, Dock.Bottom);
            side.Children.Add(sumBox);
            side.Children.Add(new Border { Child = issueList, Padding = new Thickness(10, 8, 8, 8), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0) });
            var sideBorder = new Border { Child = side, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0) };

            var center = new Grid();
            center.Children.Add(view);
            center.Children.Add(view3D);

            var root = new DockPanel();
            DockPanel.SetDock(appBar, Dock.Top);
            DockPanel.SetDock(sideBorder, Dock.Right);
            root.Children.Add(appBar);
            root.Children.Add(sideBorder);
            root.Children.Add(center);
            Content = root;

            Sketch.ModelChanged += (s, e) => { dirty = true; Update(); };
            Sketch.ActiveLevelChanged += (s, e) => Update();
            KeyDown += (s, e) =>
            {
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
                if (e.Key == System.Windows.Input.Key.S) { Save(false); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.O) { OpenDialog(); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.N) { New(); e.Handled = true; }
            };
            Closing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
            New(ask: false);
            ShowPlan(true);
        }

        private static Border Section(string title, UIElement body)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10.5, Foreground = Ui.Muted, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            sp.Children.Add(body);
            return new Border { Child = sp, Padding = new Thickness(10, 8, 8, 8) };
        }

        // ------------------------------------------------------------------ live updates

        private void Update()
        {
            var b = Sketch.Building;
            var issues = ModelChecks.Run(b);
            Sketch.Issues = issues;
            issueList.Show(issues);
            var fp = b.Footprint();
            var above = b.LevelsBottomUp.Where(l => l.Elevation > 0).ToList();
            summary.Text = (string.IsNullOrWhiteSpace(b.Name) ? "" : b.Name + "\n") + (fp.IsEmpty ? "No diaphragm yet." : $"Footprint {fp.Bounds.Width:0.##} × {fp.Bounds.Height:0.##} ft ({fp.Area:0} sq ft).") +
                           $"\n{above.Count} level{(above.Count == 1 ? "" : "s")} above the ground" + (above.Count > 0 ? $", top at {above.Last().Elevation:0.##} ft" : "") +
                           $"\n{b.Walls.Count(w => w.IsShearWall)} shear walls, {b.Walls.Count(w => !w.IsShearWall)} other walls, {b.Openings.Count} openings, {b.Notes.Count} notes." +
                           $"\nRevision {doc.Revision}{(dirty ? " (not saved)" : "")}";
            Title = $"{(string.IsNullOrEmpty(path) ? "New building" : Path.GetFileName(path))}{(dirty ? " *" : "")} — Building Model";
            view3D.HighlightLevelId = Sketch.ActiveLevel?.Id;
            if (view3D.Visibility == Visibility.Visible) view3D.Show(b);
        }

        private void ShowPlan(bool plan)
        {
            view.Visibility = plan ? Visibility.Visible : Visibility.Collapsed;
            view3D.Visibility = plan ? Visibility.Collapsed : Visibility.Visible;
            planTab.IsChecked = plan;
            tab3D.IsChecked = !plan;
            if (!plan) view3D.Show(Sketch.Building);
        }

        // ------------------------------------------------------------------ files

        private bool ConfirmDiscard()
        {
            if (!dirty) return true;
            var r = MessageBox.Show(this, "Save the changes to this building first?", "Building Model", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            return r == MessageBoxResult.No || Save(false);
        }

        /// <summary>
        /// A new design project: the user picks (or makes) its folder; the model is saved there at once as
        /// &lt;folder name&gt;.bldg.json, and the log moves to the folder's logs subfolder. At startup
        /// (<paramref name="ask"/> false) the editor starts with an unsaved building, which gets its folder on its first save.
        /// </summary>
        private void New(bool ask = true)
        {
            string file = null;
            if (ask)
            {
                if (!ConfirmDiscard()) return;
                file = ProjectDialogs.NewProjectPath(this, out bool openExisting);
                if (file == null) return;
                if (openExisting) { Open(file); return; }
            }
            var b = Building.New(file == null ? "New building" : Path.GetFileName(Path.GetDirectoryName(file)));
            var l2 = BuildingEdits.AddStory(b, b.Defaults.StoryHeight);
            doc = new ModelDocument { Building = b };
            path = null;
            Sketch.SetBuilding(b, l2.Id);
            dirty = false;
            if (file != null)
            {
                path = file;
                ProjectFolder.OpenFor(file);
                Diagnostics.Log.Info("Project", "New project: " + file);
                Save(false);
            }
            else
            {
                ProjectFolder.Close();
                Diagnostics.Log.Info("App", "New building (not saved yet; it gets a project folder on its first save).");
            }
            Update();
            Sketch.ZoomExtents();
        }

        private void OpenDialog()
        {
            if (!ConfirmDiscard()) return;
            string file = ProjectDialogs.OpenModelPath(this);
            if (file != null) Open(file);
        }

        public void Open(string file)
        {
            try
            {
                ProjectFolder.OpenFor(file); // first, so the open itself is in the project's log
                doc = ModelFile.Load(file);
                path = file;
                Sketch.SetBuilding(doc.Building, null);
                dirty = false;
                Update();
                Sketch.ZoomExtents();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "This file could not be opened.\n\n" + ex.Message, "Open", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Saves the model in its project folder. A building without one (or Save as) asks for the folder: the model is
        /// saved there as &lt;folder name&gt;.bldg.json and the folder becomes the project's.
        /// </summary>
        private bool Save(bool saveAs)
        {
            if (saveAs || string.IsNullOrEmpty(path))
            {
                string file = ProjectDialogs.NewProjectPath(this, out bool existing);
                if (file == null) return false;
                if (existing && MessageBox.Show(this, $"Replace {Path.GetFileName(file)} in that folder with this building?", "Save",
                                                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
                path = file;
                ProjectFolder.OpenFor(file);
            }
            try
            {
                doc.Building = Sketch.Building;
                doc.Version = ModelDocument.CurrentVersion;
                ModelFile.Save(path, doc);
                dirty = false;
                Update();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The file could not be saved.\n\n" + ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        /// <summary>BUILDING > NAME: the building's name (one undo step).</summary>
        private void RenameBuilding()
        {
            string s = TextPrompt.Ask("The building's name", Sketch.Building.Name ?? "");
            if (s == null || s.Trim() == Sketch.Building.Name) return;
            Sketch.Edit("Name set.", b => b.Name = s.Trim());
        }

        // ------------------------------------------------------------------ levels

        private static double? AskHeight(string question, double initial)
        {
            string s = TextPrompt.Ask(question, initial.ToString("0.##"));
            if (s == null) return null;
            if (CoordinateInput.TryLength(s, out double h) && h >= 1 && h <= 60) return h;
            MessageBox.Show($"\"{s}\" is not a story height between 1 and 60 ft.", "Level", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        private void AddLevel()
        {
            var b = Sketch.Building;
            var top = b.LevelsBottomUp.Last();
            var h = AskHeight($"Story height from {top.Name} up to the new level (ft)", top.Elevation > 0 ? b.StoryHeight(top) : b.Defaults.StoryHeight);
            if (h == null) return;
            var l = BuildingEdits.AddStory(b, h.Value);
            if (top.Elevation > 0 && b.DiaphragmsOn(top).Any() &&
                MessageBox.Show(this, $"Start {l.Name} as a copy of the story under {top.Name} (diaphragms and walls)?", "Add a level", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                BuildingEdits.CopyStory(b, top, l);
            Sketch.Commit($"{l.Name} added at {l.Elevation:0.##} ft.");
            Sketch.ShowLevel(l.Id);
        }

        private void InsertLevel()
        {
            var b = Sketch.Building;
            var at = Sketch.ActiveLevel;
            var below = b.Below(at);
            if (below == null) { MessageBox.Show(this, "Show a level above the ground to insert one under it.", "Insert a level"); return; }
            var h = AskHeight($"Elevation of the new level above {below.Name} (ft, less than {at.Elevation - below.Elevation:0.##})", (at.Elevation - below.Elevation) / 2);
            if (h == null) return;
            if (h.Value >= at.Elevation - below.Elevation) { MessageBox.Show(this, $"It must be under {at.Name}.", "Insert a level"); return; }
            var l = b.AddLevel("Mezzanine " + (b.Levels.Count(x => x.Name.StartsWith("Mezzanine")) + 1), below.Elevation + h.Value);
            Sketch.Commit($"{l.Name} inserted at {l.Elevation:0.##} ft: walls that pass it are not changed; draw its diaphragm.");
            Sketch.ShowLevel(l.Id);
        }

        private void CopyStoryBelow()
        {
            var b = Sketch.Building;
            var at = Sketch.ActiveLevel;
            var from = b.Below(at);
            if (from == null || from.Elevation == 0) { MessageBox.Show(this, "Show a level that has another level under it (not the ground).", "Copy story"); return; }
            int n = BuildingEdits.CopyStory(b, from, at);
            Sketch.Commit($"{n} objects copied from {from.Name} to {at.Name}.");
        }

        private void DeleteLevel()
        {
            var b = Sketch.Building;
            var l = Sketch.ActiveLevel;
            if (l == null || l.Elevation == 0) { MessageBox.Show(this, "The ground level cannot be deleted.", "Delete level"); return; }
            var said = BuildingEdits.DeleteStoryConsequences(b, l);
            string text = $"Delete {l.Name} and the story under it?" + (said.Count > 0 ? "\n\n• " + string.Join("\n• ", said) : "") + "\n\nCtrl+Z brings it back.";
            if (MessageBox.Show(this, text, "Delete level", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var below = b.Below(l);
            BuildingEdits.DeleteStory(b, l);
            Sketch.Commit($"{l.Name} deleted.");
            Sketch.ShowLevel(below.Id);
        }

        // ------------------------------------------------------------------ the other apps

        /// <summary>
        /// What the analysis apps read from this model, worked out by the model alone: L, B, the stories, the roof and each
        /// level's share of each outside face. (The wind and shear wall apps read the file; this app runs neither.)
        /// </summary>
        private void BuildingSummary()
        {
            var b = Sketch.Building;
            string text;
            try
            {
                var env = b.Envelope();
                var h = env.StoryHeights();
                text = $"L (W-E) {env.Length:0.##} ft, B (S-N) {env.Width:0.##} ft, eave {env.EaveHeight:0.##} ft, {h.Count} stor{(h.Count == 1 ? "y" : "ies")} " +
                       $"({string.Join(", ", h.Select(p => p.ToString("0.##")))} ft).\n" +
                       $"{b.DescribeRoof()}; mean roof height {b.MeanRoofHeight():0.##} ft.\n\n";
                foreach (Face f in Enum.GetValues(typeof(Face)))
                {
                    var shares = env.Shares(f, env.EaveHeight);
                    text += $"{f} face: " + string.Join(", ", shares.OrderBy(kv => b.Level(kv.Key)?.Elevation).Select(kv => $"{b.Level(kv.Key)?.Name} {kv.Value:P0}")) + "\n";
                }
                if (env.Notes.Count > 0) text += "\nNotes:\n• " + string.Join("\n• ", env.Notes);
            }
            catch (Exception ex)
            {
                text = "The building cannot be summarised yet: " + ex.Message;
            }
            MessageBox.Show(this, text, "What the analysis apps read from this model", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ApplyChanges()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { InitialDirectory = ProjectFolder.DialogFolder, Filter = "Change set (*.bldgchanges.json)|*.bldgchanges.json|JSON (*.json)|*.json|All files (*.*)|*.*", Title = "Apply a change set" };
            if (dlg.ShowDialog(this) != true) return;
            ChangeSet cs;
            try { cs = ChangeSet.FromJson(File.ReadAllText(dlg.FileName)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Apply changes", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string intro = $"{cs.Author} proposes: {cs.Summary()}." + (cs.BaseRevision != doc.Revision ? $"\n\nIt was made on revision {cs.BaseRevision}; this model is at revision {doc.Revision}." : "");
            if (MessageBox.Show(this, intro + "\n\nApply it?", "Apply changes", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var b = Sketch.Building;
            var conflicts = cs.ApplyTo(b);
            if (conflicts.Count > 0)
            {
                var r = MessageBox.Show(this, $"{conflicts.Count} change{(conflicts.Count == 1 ? "" : "s")} could not be applied:\n\n• " + string.Join("\n• ", conflicts.Take(12)) +
                                              "\n\nYes: take the proposed version of these too.\nNo: keep the model's.", "Conflicts", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r == MessageBoxResult.Yes) cs.ApplyTo(b, force: true);
            }
            Sketch.Commit("Change set applied: " + cs.Summary() + ".");
        }
    }
}
