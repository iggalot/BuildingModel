using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BuildingModel.Diagnostics
{
    public enum LogLevel { Debug, Info, Warning, Error }

    /// <summary>
    /// The one log for the three apps (Building Model, Wind Load Calculator, Shear Wall Calculator): one file per app per
    /// day, %AppData%\&lt;app&gt;\logs\&lt;prefix&gt;-yyyy-MM-dd.log, kept for <see cref="KeepDays"/> days. Each line is
    /// "time  LEVEL  [source]  message"; an exception adds its full detail below. Each app calls <see cref="Start"/> once
    /// at startup; libraries just log (to the app that runs them). Logging never throws: if the file cannot be written,
    /// the entry is dropped.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string folder;
        private static string prefix = "app";
        private static bool purged;

        public const int KeepDays = 14;

        /// <summary>The app writing the log ("ShearWallCalculator", "BuildingModel", "ASCE7WindLoadCalculator").</summary>
        public static string App { get; private set; } = "BuildingModel";

        public static string FolderFor(string app) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app, "logs");

        /// <summary>Where the files go (tests point it at a temporary folder).</summary>
        public static string Folder
        {
            get => folder ?? FolderFor(App);
            set { lock (Gate) { folder = value; purged = false; } }
        }

        /// <summary>Entries below this level are not written (Debug is off unless asked for).</summary>
        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        /// <summary>Raised for every entry written (an app shows warnings and errors in its status bar).</summary>
        public static event Action<LogLevel, string> Written;

        public static string TodayFile => Path.Combine(Folder, $"{prefix}-{DateTime.Now:yyyy-MM-dd}.log");

        /// <summary>
        /// Names the app (its log folder and file prefix) and writes the start line: the app, its version, the OS, .NET
        /// and the screen-independent facts that help read a report.
        /// </summary>
        public static void Start(string app, string filePrefix)
        {
            lock (Gate) { App = app; prefix = filePrefix; purged = false; }
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            Info("Log", $"{app} {asm.GetName().Version} started · {Environment.OSVersion} · .NET {Environment.Version} · " +
                        $"{(Environment.Is64BitProcess ? "64" : "32")}-bit · culture {CultureInfo.CurrentCulture.Name} · log {TodayFile}");
        }

        public static void Debug(string message) => Write(LogLevel.Debug, null, message, null);
        public static void Info(string message) => Write(LogLevel.Info, null, message, null);
        public static void Warning(string message, Exception ex = null) => Write(LogLevel.Warning, null, message, ex);
        public static void Error(string message, Exception ex = null) => Write(LogLevel.Error, null, message, ex);

        /// <summary>With the part of the program it comes from ("Sketch", "Wind", "File"...).</summary>
        public static void Debug(string source, string message) => Write(LogLevel.Debug, source, message, null);
        public static void Info(string source, string message) => Write(LogLevel.Info, source, message, null);
        public static void Warning(string source, string message, Exception ex) => Write(LogLevel.Warning, source, message, ex);
        public static void Error(string source, string message, Exception ex) => Write(LogLevel.Error, source, message, ex);

        public static void Write(LogLevel level, string message, Exception ex) => Write(level, null, message, ex);

        public static void Write(LogLevel level, string source, string message, Exception ex)
        {
            if (level < MinimumLevel) return;
            string line = $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {level.ToString().ToUpperInvariant(),-7}  " +
                          (string.IsNullOrEmpty(source) ? "" : $"[{source}]  ") + message;
            if (ex != null) line += Environment.NewLine + "    " + ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "    ");
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    if (!purged) PurgeOld();
                    File.AppendAllText(TodayFile, line + Environment.NewLine);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
                {
                    // logging must never cause a second failure
                }
            }
            try { Written?.Invoke(level, message); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { /* a listener's failure is not the caller's */ }
        }

        /// <summary>Deletes this app's log files older than <see cref="KeepDays"/> days (once per session).</summary>
        private static void PurgeOld()
        {
            purged = true;
            var cutoff = DateTime.Now.Date.AddDays(-KeepDays);
            foreach (var f in Directory.GetFiles(Folder, prefix + "-*.log").Where(f => File.GetLastWriteTime(f) < cutoff))
            {
                try { File.Delete(f); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }
    }
}
