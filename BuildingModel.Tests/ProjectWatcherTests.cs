using System;
using System.IO;
using System.Threading;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    [TestClass]
    public class ProjectWatcherTests
    {
        [TestMethod]
        public void ProjectWatcher_ReportsOthersSavesNotItsOwn()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bldg-watch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "job.bldg.json");
            File.WriteAllText(file, "{}");
            try
            {
                using (var seen = new AutoResetEvent(false))
                using (var w = new ProjectWatcher(file, 150))
                {
                    w.ChangedOnDisk += (s, e) => seen.Set();

                    File.WriteAllText(file, "{ \"mine\": 1 }");
                    w.SavedByMe();
                    Assert.IsFalse(seen.WaitOne(800), "the app's own save is not reported");

                    File.WriteAllText(file, "{ \"theirs\": 22 }");
                    Assert.IsTrue(seen.WaitOne(3000), "another app's save is reported");

                    w.Paused = true;
                    File.WriteAllText(file, "{ \"paused\": 333 }");
                    Assert.IsFalse(seen.WaitOne(800), "nothing while paused");
                }
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
