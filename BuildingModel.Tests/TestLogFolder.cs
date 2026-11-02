using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>Code under test logs (saves, edits); keep that out of the user's own log folders.</summary>
    [TestClass]
    public class TestLogFolder
    {
        [AssemblyInitialize]
        public static void Redirect(TestContext _) => BuildingModel.Diagnostics.Log.Folder = Path.Combine(Path.GetTempPath(), "bldg-test-logs");
    }
}
