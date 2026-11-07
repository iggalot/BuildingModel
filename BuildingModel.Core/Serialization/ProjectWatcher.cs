using System;
using System.IO;
using System.Threading;

namespace BuildingModel.Serialization
{
    /// <summary>
    /// The suite's one "the project file changed on disk": every app that keeps a project open watches it with this. It sees a
    /// write, a new file and the atomic save's rename (<see cref="ModelFile.Save"/> replaces the model), waits until the file
    /// has been quiet for a moment (a save is several events), and raises <see cref="ChangedOnDisk"/> once, on the thread that
    /// made it (the UI thread). The app's own save is not reported: call <see cref="SavedByMe"/> right after it.
    /// </summary>
    public sealed class ProjectWatcher : IDisposable
    {
        private readonly FileSystemWatcher fsw;
        private readonly Timer timer;
        private readonly SynchronizationContext context;
        private readonly int quietMs;
        private (DateTime, long) mine = (DateTime.MinValue, -1);
        private bool disposed;

        /// <summary>The project file watched.</summary>
        public string ModelPath { get; }

        /// <summary>No events while true (e.g. while the app itself is writing, or reloading).</summary>
        public bool Paused { get; set; }

        /// <summary>Another app (or another PC) changed, replaced or deleted the file. On the thread that made the watcher.</summary>
        public event EventHandler ChangedOnDisk;

        /// <param name="quietMs">How long the file must be quiet before it is reported (a save is several events).</param>
        public ProjectWatcher(string modelPath, int quietMs = 600)
        {
            ModelPath = Path.GetFullPath(modelPath ?? throw new ArgumentNullException(nameof(modelPath)));
            this.quietMs = quietMs;
            context = SynchronizationContext.Current;
            timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
            fsw = new FileSystemWatcher(Path.GetDirectoryName(ModelPath), Path.GetFileName(ModelPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            FileSystemEventHandler poke = (s, e) => CheckAgain();
            fsw.Changed += poke;
            fsw.Created += poke;
            fsw.Deleted += poke;
            fsw.Renamed += (s, e) => CheckAgain();
            fsw.EnableRaisingEvents = true;
        }

        /// <summary>The file as it is now is this app's own save: its events are not reported.</summary>
        public void SavedByMe() => mine = Stamp();

        /// <summary>Looks at the file again after the quiet time (e.g. it was still being written when the app read it).</summary>
        public void CheckAgain()
        {
            if (!disposed) timer.Change(quietMs, Timeout.Infinite);
        }

        private (DateTime, long) Stamp()
        {
            try
            {
                var f = new FileInfo(ModelPath);
                return f.Exists ? (f.LastWriteTimeUtc, f.Length) : (DateTime.MinValue, -1);
            }
            catch (IOException) { return (DateTime.MinValue, -2); }
            catch (UnauthorizedAccessException) { return (DateTime.MinValue, -2); }
        }

        private void Fire()
        {
            if (disposed || Paused) return;
            if (Stamp() == mine) return; // this app's own save
            void Raise() { if (!disposed && !Paused) ChangedOnDisk?.Invoke(this, EventArgs.Empty); }
            if (context != null) context.Post(_ => Raise(), null);
            else Raise();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            fsw.EnableRaisingEvents = false;
            fsw.Dispose();
            timer.Dispose();
        }
    }
}
