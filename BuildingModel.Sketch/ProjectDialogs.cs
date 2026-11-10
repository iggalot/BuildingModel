using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using BuildingModel.Diagnostics;
using BuildingModel.Serialization;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The project folder dialogs the apps share: a design project is a folder the user picks; its model file, every
    /// output and its logs subfolder live there (<see cref="ProjectFolder"/>).
    /// </summary>
    public static class ProjectDialogs
    {
        /// <summary>Asks for a folder (the user can make a new one). Null if cancelled.</summary>
        public static string ChooseFolder(Window owner, string description)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = description,
                ShowNewFolderButton = true,
                SelectedPath = ProjectFolder.DialogFolder,
            })
            {
                var result = owner == null ? dlg.ShowDialog() : dlg.ShowDialog(new OwnerHandle(owner));
                return result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath) ? dlg.SelectedPath : null;
            }
        }

        /// <summary>
        /// Asks for the folder of a new design project and gives the model file it gets there (&lt;folder name&gt;.bldg.json).
        /// If that folder already holds a model the user chooses to open it (<paramref name="openExisting"/> true) or to
        /// pick again. Null if cancelled.
        /// </summary>
        public static string NewProjectPath(Window owner, out bool openExisting)
        {
            openExisting = false;
            while (true)
            {
                string folder = ChooseFolder(owner, "Choose or make the folder for this design project. The building model, the reports and a logs folder are saved there together.");
                if (folder == null) return null;
                string path = ProjectFolder.NewModelPath(folder);
                string[] models = Directory.GetFiles(folder, "*" + ProjectFolder.ModelExtension);
                if (models.Length == 0) return path;
                string existing = File.Exists(path) ? path : models[0];
                var r = MessageBox.Show(owner, $"This folder already has a building model:\n{Path.GetFileName(existing)}\n\nOpen it?\n\nNo: choose another folder.",
                                        "New project", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return null;
                if (r == MessageBoxResult.Yes) { openExisting = true; return existing; }
            }
        }

        /// <summary>Asks for a building model to open, starting in the open project's folder. Null if cancelled.</summary>
        public static string OpenModelPath(Window owner)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open a design project (its building model)",
                Filter = "Building model (*.bldg.json)|*.bldg.json|All files (*.*)|*.*",
                InitialDirectory = ProjectFolder.DialogFolder,
            };
            return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
        }

        private sealed class OwnerHandle : System.Windows.Forms.IWin32Window
        {
            public OwnerHandle(Window w) { Handle = new WindowInteropHelper(w).Handle; }
            public IntPtr Handle { get; }
        }

        /// <summary>
        /// The suite's "save first?" before leaving unsaved changes (closing, opening another project). Yes saves with
        /// <paramref name="save"/>; No leaves them; Cancel stays. True: go on (saved, or not wanted).
        /// </summary>
        public static bool SaveChangesFirst(Window owner, string question, string appName, Func<bool> save)
        {
            var r = MessageBox.Show(owner, question, appName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            return r == MessageBoxResult.No || save();
        }

        /// <summary>
        /// Takes the app's lock on the project (<see cref="ProjectLock.TryTake"/>), saying so when another copy of the app has
        /// it. <paramref name="refused"/>: true when it is held elsewhere (the project must not be opened); false with a null
        /// lock when it could not be written (a read-only folder): the app goes on without one.
        /// </summary>
        public static ProjectLock TakeLock(Window owner, string modelPath, string appId, string appName, out bool refused) =>
            TakeLock(owner, modelPath, appId, appName, false, out refused, out _);

        private static bool viewOnlyArgumentUsed;

        /// <summary>
        /// True once, for the first project held by someone else, when the app was started with
        /// <see cref="ProjectLock.ViewOnlyArgument"/> (the hub's view only: already answered there). Every app reads it here.
        /// </summary>
        internal static bool StartedViewOnly(string[] args = null)
        {
            if (viewOnlyArgumentUsed) return false;
            viewOnlyArgumentUsed = true;
            return Array.Exists(args ?? Environment.GetCommandLineArgs(), a => string.Equals(a, ProjectLock.ViewOnlyArgument, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The question when someone else has the project and the app can open it to look at only.</summary>
        public static string ViewOnlyQuestion(string others) =>
            others + "\n\nOpen it view only? You can look at the plan, the results and the reports; nothing can be changed or saved, and no one's work is held up.";

        /// <summary>
        /// As above; with <paramref name="offerViewOnly"/>, someone else's project (another person, or another PC) can be
        /// opened to look at only: <paramref name="viewOnly"/> true, no lock taken, not refused. This person's own other copy
        /// of the app is still refused (switch to that window).
        /// </summary>
        public static ProjectLock TakeLock(Window owner, string modelPath, string appId, string appName, bool offerViewOnly, out bool refused, out bool viewOnly)
        {
            viewOnly = false;
            // one person per project: someone else's hub or app has it (another person, or another PC)
            string others = ProjectLock.OthersMessage(modelPath);
            if (others != null)
            {
                if (offerViewOnly)
                {
                    // the hub's view only opened this app (--view-only): already answered there
                    viewOnly = StartedViewOnly() ||
                               MessageBox.Show(owner, ViewOnlyQuestion(others), appName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                    refused = !viewOnly;
                    if (viewOnly) Log.Info("Lock", "Opened view only (someone else has it): " + modelPath);
                    return null;
                }
                refused = true;
                MessageBox.Show(owner, others, appName, MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            var taken = ProjectLock.TryTake(modelPath, appId, appName, out var heldBy);
            refused = taken == null && heldBy != null;
            if (refused)
                MessageBox.Show(owner, $"This project is already open in another {appName}: {heldBy.Describe()}.\n\nSwitch to that window, or close it first.",
                                appName, MessageBoxButton.OK, MessageBoxImage.Information);
            return taken;
        }
    }
}
