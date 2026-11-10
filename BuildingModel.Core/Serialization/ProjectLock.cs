using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace BuildingModel.Serialization
{
    /// <summary>Who has a project open in an app: from that app's lock file.</summary>
    public sealed class LockHolder
    {
        /// <summary>The app's short id (building, wind, shearwalls, loads).</summary>
        public string AppId { get; set; }
        public string AppName { get; set; }
        public int ProcessId { get; set; }
        public string Machine { get; set; }
        public string User { get; set; }
        public DateTime Since { get; set; }

        /// <summary>True when the holder is this PC (its window can be found and brought to the front).</summary>
        public bool OnThisPc => string.Equals(Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

        /// <summary>"the Shear Wall Calculator (since 10:42 AM, jim on DESK-1)".</summary>
        public string Describe() =>
            $"the {AppName ?? AppId} (since {Since.ToLocalTime():h:mm tt}" + (OnThisPc ? "" : $", {User} on {Machine}") + ")";

        internal string ToText() => string.Join("\n", "App=" + AppId, "Name=" + AppName, "Pid=" + ProcessId, "Machine=" + Machine, "User=" + User,
                                                "Since=" + Since.ToUniversalTime().ToString("o"));

        internal static LockHolder FromText(string text, string fallbackApp)
        {
            var h = new LockHolder { AppId = fallbackApp };
            foreach (string line in (text ?? "").Split('\n'))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                switch (k)
                {
                    case "App": h.AppId = v; break;
                    case "Name": h.AppName = v; break;
                    case "Pid": int.TryParse(v, out int pid); h.ProcessId = pid; break;
                    case "Machine": h.Machine = v; break;
                    case "User": h.User = v; break;
                    case "Since":
                        if (DateTime.TryParse(v, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)) h.Since = t;
                        break;
                }
            }
            return h;
        }
    }

    /// <summary>
    /// An app's hold on a project: the file &lt;name&gt;.bldg.json.&lt;app&gt;.lock beside the model, kept open (others may read
    /// it, nobody else may write it) for as long as the app has the project open. It says which app, which process, which
    /// PC and user, and since when. One copy of each app per project: a second copy of the same app is refused. Different
    /// apps may share a project, as their saves merge (<see cref="ModelFile.SaveMerged"/>), and each sees the others
    /// (<see cref="Holders"/>). Windows closes the file when the app ends, even in a crash, so a lock left behind is stale
    /// and is taken over (or cleared) by the next one.
    /// </summary>
    public sealed class ProjectLock : IDisposable
    {
        private FileStream stream;

        public string ModelPath { get; }
        public string AppId { get; }
        public string LockPath { get; }

        private ProjectLock(string modelPath, string appId, string lockPath, FileStream s)
        {
            ModelPath = modelPath;
            AppId = appId;
            LockPath = lockPath;
            stream = s;
        }

        public static string PathFor(string modelPath, string appId) => Path.GetFullPath(modelPath) + "." + appId + ".lock";

        /// <summary>
        /// Takes the app's lock on the project, or returns null with <paramref name="heldBy"/> set when another copy of this
        /// app has it. A lock that cannot be written for another reason (a read-only folder) also returns null, with
        /// <paramref name="heldBy"/> null: the app may go on without one.
        /// </summary>
        public static ProjectLock TryTake(string modelPath, string appId, string appName, out LockHolder heldBy)
        {
            heldBy = null;
            string path = PathFor(modelPath, appId);
            FileStream s;
            try
            {
                s = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            }
            catch (IOException) when (File.Exists(path))
            {
                heldBy = Read(path, appId);
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Diagnostics.Log.Warning("Lock", "Could not make the lock file " + path, ex);
                return null;
            }
            var me = new LockHolder
            {
                AppId = appId, AppName = appName, ProcessId = Process.GetCurrentProcess().Id, Machine = Environment.MachineName,
                User = Environment.UserName, Since = DateTime.UtcNow,
            };
            byte[] bytes = Encoding.UTF8.GetBytes(me.ToText());
            s.SetLength(0);
            s.Write(bytes, 0, bytes.Length);
            s.Flush(true);
            try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Diagnostics.Log.Info("Lock", $"Took the {appId} lock on {modelPath}.");
            return new ProjectLock(modelPath, appId, path, s);
        }

        /// <summary>
        /// Who else is working on this project: the hub or an app open by another person or on another PC. One person works
        /// on a project at a time (the suite's rule: two people saving the same project overwrite each other's work), so the
        /// hub and every app refuse a project someone else has. Empty when it is free or only this person has it here.
        /// </summary>
        public static IReadOnlyList<LockHolder> HeldByOthers(string modelPath) =>
            Holders(modelPath).Where(h => !(h.OnThisPc && string.Equals(h.User, Environment.UserName, StringComparison.OrdinalIgnoreCase))).ToList();

        /// <summary>The hub's lock id: the person whose hub has the project.</summary>
        public const string HubId = "hub";

        /// <summary>
        /// The argument the hub gives an app it opens from its own view only (someone else is working on the project): the
        /// app opens it view only without asking (<c>ProjectDialogs.TakeLock</c>, offerViewOnly).
        /// </summary>
        public const string ViewOnlyArgument = "--view-only";

        /// <summary>"jim on DESK-2 has it open in the Shear Wall Calculator. One person works on a project at a time..."; null when free.</summary>
        public static string OthersMessage(string modelPath)
        {
            var others = HeldByOthers(modelPath);
            if (others.Count == 0) return null;
            var h = others[0];
            return $"{h.User} on {h.Machine} has this project open (in the {h.AppName}, since {h.Since.ToLocalTime():g}).\n\n" +
                   "One person works on a project at a time, so no one saves over another's work. Ask them to close it first.";
        }

        /// <summary>The apps that have this project open now (any PC that shares the folder). Stale locks are cleared.</summary>
        public static IReadOnlyList<LockHolder> Holders(string modelPath)
        {
            var list = new List<LockHolder>();
            string full;
            try { full = Path.GetFullPath(modelPath); } catch (ArgumentException) { return list; }
            string dir = Path.GetDirectoryName(full), prefix = Path.GetFileName(full) + ".";
            if (dir == null || !Directory.Exists(dir)) return list;
            string[] files;
            try { files = Directory.GetFiles(dir, prefix + "*.lock"); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return list; }
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                string app = name.Substring(prefix.Length, name.Length - prefix.Length - ".lock".Length);
                if (app.Length == 0 || app.Contains(".")) continue;
                if (IsStale(f)) continue;
                var h = Read(f, app);
                if (h != null) list.Add(h);
            }
            return list.OrderBy(h => h.Since).ToList();
        }

        /// <summary>True (and the file removed) when nobody holds it: its app ended without letting go.</summary>
        private static bool IsStale(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            catch (FileNotFoundException) { return true; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
            try { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return true;
        }

        private static LockHolder Read(string path, string appId)
        {
            try
            {
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return LockHolder.FromText(r.ReadToEnd(), appId);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new LockHolder { AppId = appId };
            }
        }

        /// <summary>Lets go of the project (closing it, opening another, or the app ending).</summary>
        public void Dispose()
        {
            if (stream == null) return;
            stream.Dispose();
            stream = null;
            try { File.SetAttributes(LockPath, FileAttributes.Normal); File.Delete(LockPath); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            Diagnostics.Log.Info("Lock", $"Let go of the {AppId} lock on {ModelPath}.");
        }
    }
}
