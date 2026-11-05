using System;
using System.IO;
using System.Linq;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// Several apps on one project file: a save merges with what the others saved meanwhile (three-way, by object and data key),
    /// is whole or not at all (with a .bak), and each app holds a lock that says who has the project open.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class MergeAndLockTests
    {
        private static string Folder(string name)
        {
            string d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scratch", "merge-" + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(d);
            return d;
        }

        private static Building Box()
        {
            var b = Building.New("Box");
            BuildingEdits.BuildFromSize(b, new BuildingEdits.QuickBuilding { Length = 40, Width = 30, StoryHeights = { 10 } });
            return b;
        }

        private static (string path, ModelDocument doc) Saved(string name, Building b)
        {
            string path = Path.Combine(Folder(name), name + ".bldg.json");
            var doc = new ModelDocument { Building = b };
            ModelFile.Save(path, doc);
            return (path, doc);
        }

        [TestMethod]
        public void NobodyElseSaved_TheAppsBuildingIsSavedAsItIs()
        {
            var (path, doc) = Saved("alone", Box());
            var baseB = ModelFile.Clone(doc.Building);
            doc.Building.Walls[0].End = new Point2(doc.Building.Walls[0].End.X, doc.Building.Walls[0].End.Y);
            doc.Building.Name = "Renamed";
            var r = ModelFile.SaveMerged(path, doc, baseB);
            Assert.IsFalse(r.FileChanged);
            Assert.IsNull(r.Summary());
            Assert.AreEqual("Renamed", ModelFile.Load(path).Building.Name);
            Assert.AreEqual(2, ModelFile.Load(path).Revision);
        }

        [TestMethod]
        public void AnotherAppSavedMeanwhile_BothChangesAreKept()
        {
            var (path, doc) = Saved("both", Box());
            var baseB = ModelFile.Clone(doc.Building);
            string wallId = doc.Building.Walls[0].Id, otherWall = doc.Building.Walls[1].Id;

            // the other app (say the wind calculator) saves its section and a wall's data
            var other = ModelFile.Load(path);
            other.Building.SetData("Wind", "{\"speed\":115}");
            other.Building.Walls.First(w => w.Id == otherWall).SetData("ShearWall", "{\"sheathing\":\"7/16 OSB\"}");
            ModelFile.Save(path, other);

            // this app (the Building Model) renames the building and adds a note, from its old copy
            doc.Building.Name = "Smith";
            doc.Building.Notes.Add(new Note { Id = "N9", Text = "check" });
            var r = ModelFile.SaveMerged(path, doc, baseB);

            Assert.IsTrue(r.FileChanged);
            Assert.AreEqual(0, r.Conflicts.Count, string.Join("; ", r.Conflicts));
            var back = ModelFile.Load(path).Building;
            Assert.AreEqual("Smith", back.Name, "this app's change");
            Assert.AreEqual("{\"speed\":115}", back.GetData("Wind"), "the other app's section");
            Assert.IsNotNull(back.Walls.First(w => w.Id == otherWall).GetData("ShearWall"), "the other app's wall data");
            Assert.IsTrue(back.Notes.Any(n => n.Id == "N9"));
            Assert.AreEqual(3, ModelFile.Load(path).Revision, "one more than the file's");
            StringAssert.Contains(r.Summary(), "Wind (the file's)");
            Assert.IsTrue(back.Walls.Any(w => w.Id == wallId));
        }

        [TestMethod]
        public void BothChangedTheSameThing_ThisAppWins_AndItIsReported()
        {
            var (path, doc) = Saved("conflict", Box());
            var baseB = ModelFile.Clone(doc.Building);
            var other = ModelFile.Load(path);
            other.Building.SetData("Wind", "theirs");
            ModelFile.Save(path, other);
            doc.Building.SetData("Wind", "mine");
            var r = ModelFile.SaveMerged(path, doc, baseB);
            CollectionAssert.Contains(r.Conflicts, "Wind");
            Assert.AreEqual("mine", ModelFile.Load(path).Building.GetData("Wind"));
        }

        [TestMethod]
        public void AWallDeletedThere_StaysDeleted_ItsDataHereIsReported()
        {
            var (path, doc) = Saved("deleted", Box());
            var baseB = ModelFile.Clone(doc.Building);
            string id = doc.Building.Walls[0].Id;
            var other = ModelFile.Load(path);
            other.Building.Walls.RemoveAll(w => w.Id == id);
            ModelFile.Save(path, other);
            doc.Building.Walls[0].SetData("ShearWall", "designed");
            var r = ModelFile.SaveMerged(path, doc, baseB);
            Assert.IsFalse(ModelFile.Load(path).Building.Walls.Any(w => w.Id == id));
            Assert.IsTrue(r.Conflicts.Any(c => c.Contains(id)), string.Join("; ", r.Conflicts));
        }

        [TestMethod]
        public void Owns_AStaleCopyOfAnotherAppsData_NeverOverwritesTheFile()
        {
            var (path, doc) = Saved("owns", Box());
            doc.Building.SetData("Wind", "old");
            ModelFile.Save(path, doc);
            var baseB = ModelFile.Clone(doc.Building);
            // the editor's undo brings back an older building, data and all; meanwhile nobody else saved
            doc.Building.SetData("Wind", "older, from the undo history");
            doc.Building.Name = "Renamed";
            var r = ModelFile.SaveMerged(path, doc, baseB, k => k.StartsWith("Sketch.", StringComparison.Ordinal));
            Assert.IsFalse(r.FileChanged);
            var back = ModelFile.Load(path).Building;
            Assert.AreEqual("old", back.GetData("Wind"), "not the editor's key: the file's");
            Assert.AreEqual("Renamed", back.Name);
        }

        [TestMethod]
        public void SaveOwnData_WritesOnlyTheAppsKeys()
        {
            var (path, doc) = Saved("own", Box());
            var mine = ModelFile.Clone(doc.Building);
            var other = ModelFile.Load(path);
            other.Building.Name = "Edited in the Building Model";
            ModelFile.Save(path, other);
            mine.SetData("Lateral", "hold-down forces");
            mine.Name = "a stale name";
            var saved = ModelFile.SaveOwnData(path, mine, k => k == "Lateral", out var lost);
            Assert.AreEqual(0, lost.Count);
            var back = ModelFile.Load(path);
            Assert.AreEqual("Edited in the Building Model", back.Building.Name);
            Assert.AreEqual("hold-down forces", back.Building.GetData("Lateral"));
            Assert.AreEqual(3, back.Revision);
            Assert.AreEqual(3, saved.Revision);
        }

        [TestMethod]
        public void CopyOwnData_OnlyTheAppsKeysChange()
        {
            var mine = Box();
            var latest = ModelFile.Clone(mine);
            latest.Name = "Moved by the editor";
            latest.SetData("Wind", "the wind's");
            latest.SetData("ShearWall.Project", "old");
            mine.SetData("ShearWall.Project", "new");
            mine.SetData("Wind", "a stale copy");
            mine.Walls[0].SetData("ShearWall", "hold-down HDU5");
            latest.Walls.RemoveAt(1);
            mine.Walls[1].SetData("ShearWall", "for a wall the editor deleted");
            var lost = ModelMerge.CopyOwnData(mine, latest, k => k.StartsWith("ShearWall", StringComparison.Ordinal) || k == "Lateral");
            Assert.AreEqual("Moved by the editor", latest.Name, "geometry is the file's");
            Assert.AreEqual("the wind's", latest.GetData("Wind"), "other apps' keys are the file's");
            Assert.AreEqual("new", latest.GetData("ShearWall.Project"));
            Assert.AreEqual("hold-down HDU5", latest.Walls[0].GetData("ShearWall"));
            Assert.AreEqual(1, lost.Count);
        }

        [TestMethod]
        public void Save_IsWholeOrNothing_AndKeepsTheLastVersionAsBak()
        {
            var (path, doc) = Saved("atomic", Box());
            doc.Building.Name = "Second";
            ModelFile.Save(path, doc);
            Assert.AreEqual("Second", ModelFile.Load(path).Building.Name);
            Assert.AreEqual("Box", ModelFile.FromJson(File.ReadAllText(ModelFile.BackupPath(path))).Building.Name, "the version replaced");
            Assert.IsFalse(File.Exists(path + ".saving"));
        }

        [TestMethod]
        public void Lock_OneCopyOfEachApp_TheOthersSeeWhoHasIt()
        {
            var (path, _) = Saved("lock", Box());
            using (var swc = ProjectLock.TryTake(path, "shearwalls", "Shear Wall Calculator", out var h0))
            {
                Assert.IsNotNull(swc);
                Assert.IsNull(h0);
                Assert.IsNull(ProjectLock.TryTake(path, "shearwalls", "Shear Wall Calculator", out var held), "a second copy is refused");
                Assert.AreEqual("Shear Wall Calculator", held.AppName);
                Assert.IsTrue(held.OnThisPc);
                StringAssert.Contains(held.Describe(), "the Shear Wall Calculator (since ");
                using (var wind = ProjectLock.TryTake(path, "wind", "Wind Load Calculator", out _))
                {
                    Assert.IsNotNull(wind, "another app may share the project");
                    CollectionAssert.AreEquivalent(new[] { "shearwalls", "wind" }, ProjectLock.Holders(path).Select(x => x.AppId).ToArray());
                }
                CollectionAssert.AreEquivalent(new[] { "shearwalls" }, ProjectLock.Holders(path).Select(x => x.AppId).ToArray());
            }
            Assert.AreEqual(0, ProjectLock.Holders(path).Count);
            Assert.IsFalse(File.Exists(ProjectLock.PathFor(path, "shearwalls")), "let go: the file is gone");
        }

        [TestMethod]
        public void Lock_LeftByACrash_IsStale_AndTakenOver()
        {
            var (path, _) = Saved("stale", Box());
            File.WriteAllText(ProjectLock.PathFor(path, "building"), "App=building\nName=Building Model\nPid=1\n");
            Assert.AreEqual(0, ProjectLock.Holders(path).Count, "nobody holds it");
            File.WriteAllText(ProjectLock.PathFor(path, "building"), "App=building\n");
            using (var l = ProjectLock.TryTake(path, "building", "Building Model", out var h))
                Assert.IsNotNull(l);
        }
    }
}
