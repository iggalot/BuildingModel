using System;
using System.IO;
using System.Linq;
using BuildingModel.Diagnostics;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// A design project is a folder: the model and the outputs side by side, the logs in its logs subfolder, one file per
    /// app per day, with every open, save and edit written there.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // the log and the project folder are static
    public class ProjectFolderTests
    {
        private string root;

        [TestInitialize]
        public void Init()
        {
            root = Path.Combine(Path.GetTempPath(), "bldg-project-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Log.Start("BuildingModel", "bldg");
        }

        [TestCleanup]
        public void Cleanup()
        {
            ProjectFolder.Close();
            Log.Folder = Path.Combine(Path.GetTempPath(), "bldg-test-logs");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [TestMethod]
        public void NewModelPath_IsNamedAfterTheFolder()
        {
            string folder = Path.Combine(root, "Smith Residence");
            Assert.AreEqual(Path.Combine(folder, "Smith Residence.bldg.json"), ProjectFolder.NewModelPath(folder));
            Assert.AreEqual(Path.Combine(folder, "Smith Residence.bldg.json"), ProjectFolder.NewModelPath(folder + "\\"));
        }

        [TestMethod]
        public void OpenSaveAndOutputs_LiveInTheProjectFolder_LogsInItsLogsFolder()
        {
            string folder = Path.Combine(root, "Job 101");
            Directory.CreateDirectory(folder);
            string model = ProjectFolder.NewModelPath(folder);

            ProjectFolder.OpenFor(model);
            Assert.AreEqual(folder, ProjectFolder.Current);
            Assert.AreEqual(Path.Combine(folder, "logs"), Log.Folder);
            Assert.AreEqual(folder, ProjectFolder.DialogFolder);
            Assert.AreEqual(Path.Combine(folder, "Job 101 wall schedule.pdf"), ProjectFolder.PathFor("Job 101 wall schedule.pdf"));

            ModelFile.Save(model, new ModelDocument { Building = Building.New("Job 101") });
            ModelFile.Load(model);

            string[] logs = Directory.GetFiles(Path.Combine(folder, "logs"), "bldg-*.log");
            Assert.AreEqual(1, logs.Length, "one file per app per day");
            string text = File.ReadAllText(logs[0]);
            StringAssert.Contains(text, "opened the project " + folder);
            StringAssert.Contains(text, "[File]  Saved " + model);
            StringAssert.Contains(text, "[File]  Opened " + model);
        }

        [TestMethod]
        public void Close_SendsTheLogBackToTheAppsFolder()
        {
            ProjectFolder.OpenFor(Path.Combine(root, "a.bldg.json"));
            ProjectFolder.Close();
            Assert.IsNull(ProjectFolder.Current);
            Assert.IsNull(ProjectFolder.PathFor("x.pdf"));
            Assert.AreEqual(Log.FolderFor("BuildingModel"), Log.Folder);
        }

        [TestMethod]
        public void ASecondApp_WritesItsOwnFile_InTheSameLogsFolder()
        {
            ProjectFolder.OpenFor(Path.Combine(root, "a.bldg.json"));
            Log.Start("ShearWallCalculator", "swc");
            Log.Info("Design", "Wall 3 designed.");
            Log.Start("BuildingModel", "bldg");
            var names = Directory.GetFiles(Path.Combine(root, "logs")).Select(Path.GetFileName).ToList();
            Assert.IsTrue(names.Any(n => n.StartsWith("swc-")), string.Join(", ", names));
            Assert.IsTrue(names.Any(n => n.StartsWith("bldg-")), string.Join(", ", names));
        }

        [TestMethod]
        public void AFailedSave_IsLogged_AndStillThrows()
        {
            ProjectFolder.OpenFor(Path.Combine(root, "a.bldg.json"));
            string bad = Path.Combine(root, "missing folder", "x.bldg.json");
            Assert.Throws<DirectoryNotFoundException>(() => ModelFile.Save(bad, new ModelDocument { Building = Building.New() }));
            StringAssert.Contains(File.ReadAllText(Log.TodayFile), "Could not save " + bad);
        }
    }
}
