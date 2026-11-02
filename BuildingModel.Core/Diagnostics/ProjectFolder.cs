using System;
using System.IO;

namespace BuildingModel.Diagnostics
{
    /// <summary>
    /// A design project is a folder the user picks. The model file (&lt;name&gt;.bldg.json) and every file the
    /// three apps write for it (reports, PDFs, change sets) are saved there, next to each other. The logs are in its
    /// <see cref="LogsFolderName"/> subfolder, one file per app per day, so each project carries its own record of how
    /// its files were made and changed. Until a project is open the apps log to their own folder under %AppData%.
    /// </summary>
    public static class ProjectFolder
    {
        public const string LogsFolderName = "logs";
        public const string ModelExtension = ".bldg.json";

        /// <summary>The open project's folder, or null when none is open.</summary>
        public static string Current { get; private set; }

        /// <summary>Raised when a project folder is opened or closed.</summary>
        public static event Action Changed;

        public static string LogsOf(string folder) => Path.Combine(folder, LogsFolderName);

        /// <summary>The model file a new project in <paramref name="folder"/> gets: the folder's name, .bldg.json.</summary>
        public static string NewModelPath(string folder)
        {
            string name = new DirectoryInfo(folder.TrimEnd('\\', '/')).Name;
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith(":")) name = "Building";
            return Path.Combine(folder, name + ModelExtension);
        }

        /// <summary>
        /// Opens the project that holds <paramref name="modelPath"/>: its folder becomes the place for outputs and the
        /// log moves to its logs subfolder (the move is logged at both ends).
        /// </summary>
        public static void OpenFor(string modelPath)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
            if (string.Equals(folder, Current, StringComparison.OrdinalIgnoreCase)) return;
            string logs = LogsOf(folder);
            Log.Info("Project", $"Project folder {folder}; the log continues in {logs}.");
            Current = folder;
            Log.Folder = logs;
            Log.Info("Project", $"{Log.App} opened the project {folder} (model {Path.GetFileName(modelPath)}).");
            Changed?.Invoke();
        }

        /// <summary>No project open: the log goes back to the app's own folder.</summary>
        public static void Close()
        {
            if (Current == null) return;
            Log.Info("Project", "Project closed: " + Current);
            Current = null;
            Log.Folder = null;
            Changed?.Invoke();
        }

        /// <summary>Where an output file of this name goes: in the project folder (null when no project is open).</summary>
        public static string PathFor(string fileName) => Current == null ? null : Path.Combine(Current, fileName);

        /// <summary>The folder file dialogs start in: the project's, else the user's documents.</summary>
        public static string DialogFolder =>
            Current != null && Directory.Exists(Current) ? Current : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
