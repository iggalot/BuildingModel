using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using BuildingModel.Diagnostics;

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
    }
}
