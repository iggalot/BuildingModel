using System;
using BuildingModel.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>The hub's mark of the project's problems ("Hub.Status"), as every app reads it: the items kept, a step's words.</summary>
    [TestClass]
    public class ProjectStatusTests
    {
        [TestMethod]
        public void Stamped_KeepsWhenFirstSeen_TheWordsOfAStep()
        {
            var first = ProjectStatus.Stamped(new[]
            {
                new StatusItem { Step = "Shear walls", State = "Fails", Message = "W4 fails on Level 1.", Id = "W4" },
                new StatusItem { Step = "Foundation loads", State = "OutOfDate", Message = "Built on a shear wall design that now fails." },
            }, null, new DateTime(2026, 11, 5, 17, 0, 0, DateTimeKind.Utc), "jim on DESK-1");
            var b = Building.New("Box");
            ProjectStatus.Write(b, first);
            var read = ProjectStatus.Read(b);
            Assert.AreEqual(2, read.Items.Count);

            var next = ProjectStatus.Stamped(new[] { new StatusItem { Step = "Shear walls", State = "Fails", Message = "W4 fails on Level 1.", Id = "W4" } },
                                             read, new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc), "ann on DESK-2");
            Assert.AreEqual("2026-11-05T17:00:00Z", next.Items[0].Since, "the same problem: since it was first seen");
            Assert.AreEqual("jim on DESK-1", next.Items[0].By);

            string words = read.Words("Shear walls");
            StringAssert.Contains(words, "a problem");
            StringAssert.Contains(words, "• W4 fails on Level 1.");
            StringAssert.Contains(words, "jim on DESK-1");
            Assert.IsFalse(words.Contains("Built on"), "another step's: not this app's");
            Assert.IsNull(read.Words("Wind"));
            StringAssert.Contains(read.Words(), "2 problems");

            ProjectStatus.Write(b, new ProjectStatus());
            Assert.IsNull(b.GetData(ProjectStatus.Key), "clear: the section removed");
        }
    }
}
