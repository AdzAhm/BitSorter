using System.IO;
using NUnit.Framework;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// SaveGuard's safety copy of the real save, run against a scratch folder of its own.
    /// </summary>
    /// <remarks>
    /// The copy was meant to be one per session, and the flag that said so is a static -- which
    /// every Play Mode run resets, because entering play mode reloads the domain. So it was one per
    /// test run: 210 identical copies of an unchanged save had piled up beside it by 2026-09-24.
    /// A copy is worth keeping when there is something new in it, so that is the rule tested here.
    /// </remarks>
    public class SaveGuardTests
    {
        private string _dir;

        [SetUp]
        public void MakeAFolder()
        {
            _dir = Path.Combine(Path.GetTempPath(), "BitSorter-SaveGuardTests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void RemoveTheFolder()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private string Save(string json)
        {
            string save = Path.Combine(_dir, "progress.json");
            File.WriteAllText(save, json);
            return save;
        }

        private int Copies() => Directory.GetFiles(_dir, "progress.json.archive-*").Length;

        [Test]
        public void AnUnchangedSave_IsNotCopiedAgain()
        {
            string save = Save("{\"solved\":[\"route-the-bit\"]}");

            Assert.IsNotNull(SaveGuard.Archive(save, "20260101-000000"), "sanity: the first copy should be made");

            Assert.IsNull(SaveGuard.Archive(save, "20260101-000100"),
                "a save with nothing new in it was copied again");
            Assert.AreEqual(1, Copies(), "copies of an unchanged save piled up");
        }

        /// <summary>The positive control: a save that has changed is copied again.</summary>
        [Test]
        public void AChangedSave_IsCopiedAgain()
        {
            string save = Save("{\"solved\":[\"route-the-bit\"]}");
            SaveGuard.Archive(save, "20260101-000000");

            Save("{\"solved\":[\"route-the-bit\",\"half-adder\"]}");

            Assert.IsNotNull(SaveGuard.Archive(save, "20260101-000100"), "a changed save was not copied");
            Assert.AreEqual(2, Copies());
        }
    }
}
