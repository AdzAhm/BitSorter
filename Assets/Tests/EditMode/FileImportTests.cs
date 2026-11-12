using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.View;
using NUnit.Framework;

namespace BitSorter.EditMode.Tests
{
    /// <summary>
    /// What the desktop reads for an import -- a folder chosen (<see cref="FileImport.ReadFolder"/>, Part 13f), or files
    /// chosen (<see cref="FileImport.ReadFiles"/>, 5.1): sizes looked at before anything is read, so a file past the import's
    /// largest is left unread, and every file when there are more than an import takes -- and a hidden folder's files neither
    /// read nor counted. Always a scratch folder, never the player's.
    /// </summary>
    public class FileImportTests
    {
        private const string Mux = "module mux2 (input logic a, b, s, output logic y);\n    assign y = s ? b : a;\nendmodule\n";

        private string _folder;

        [SetUp]
        public void MakeAScratchFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "bitsorter-import-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void DeleteTheScratchFolder() => Directory.Delete(_folder, recursive: true);

        private void Put(string path, string text)
        {
            string full = Path.Combine(_folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
        }

        [Test]
        public void AFileTooLarge_IsLeftUnread_ByItsSize_AndAHiddenFolderIsNotRead()
        {
            Put("mux2.sv", Mux);
            Put("big.hex", new string('0', (int)ProjectImporter.MaxFileSize + 1));
            Put(".git/HEAD", "ref: refs/heads/main\n");

            List<SourceFile> files = FileImport.ReadFolder(_folder);

            CollectionAssert.AreEqual(new[] { "big.hex", "mux2.sv" }, files.Select(f => f.Path), "the .git folder is neither read nor counted");
            SourceFile big = files.Single(f => f.Path == "big.hex");
            Assert.IsFalse(big.WasRead, "never read into memory");
            Assert.AreEqual(ProjectImporter.MaxFileSize + 1, big.Size);
            Assert.AreEqual(Mux, files.Single(f => f.Path == "mux2.sv").Text);
        }

        [Test]
        public void MoreFilesThanAnImportTakes_AreAllLeftUnread_AndTheImportSaysWhy()
        {
            Put("mux2.sv", Mux);
            for (int i = 0; i < ProjectImporter.MaxFiles; i++)
                Put($"notes/{i}.txt", "a note\n");

            List<SourceFile> files = FileImport.ReadFolder(_folder);

            Assert.AreEqual(ProjectImporter.MaxFiles + 1, files.Count);
            Assert.IsTrue(files.All(f => !f.WasRead), "none read: the import refuses them anyway");
            ProjectImport import = ProjectImporter.Import(files);
            Assert.IsFalse(import.Succeeded);
            StringAssert.Contains("an import takes at most 200", string.Join("\n", import.Notes));
        }

        /// <summary>
        /// A folder chosen a level too high -- Documents, a whole drive -- is not walked to its last file (5.1): the walk stops
        /// at the first file past what an import takes, every one it found left unread, the same ones every time.
        /// </summary>
        [Test]
        public void AFolderOfFarMoreFiles_IsWalkedOnlyPastTheLimit_TheSameWayEveryTime()
        {
            for (int i = 0; i < 3 * ProjectImporter.MaxFiles; i++)
                Put($"folder{i % 9}/file{i:D4}.sv", "// nothing\n");

            List<SourceFile> files = FileImport.ReadFolder(_folder);

            Assert.AreEqual(ProjectImporter.MaxFiles + 1, files.Count, "found up to one past the limit, and no further");
            Assert.IsTrue(files.All(f => !f.WasRead), "none read: the import refuses them anyway");
            CollectionAssert.AreEqual(files.Select(f => f.Path), FileImport.ReadFolder(_folder).Select(f => f.Path), "the same files found again");
        }

        /// <summary>The folder chosen gone, or never there, throws: the workbench says it could not be read, not that it is empty.</summary>
        [Test]
        public void AFolderThatIsNotThere_Throws_AndIsNotTakenForAnEmptyOne()
        {
            Assert.Throws<DirectoryNotFoundException>(() => FileImport.ReadFolder(Path.Combine(_folder, "gone")));
        }

        /// <summary>
        /// Files chosen (5.1), from any folder: each by its name, in order, read; one too large left unread by its size; a hidden
        /// one passed over; and one gone since it was chosen left unread, never thrown.
        /// </summary>
        [Test]
        public void FilesChosen_AreEachReadByTheirName_TheTooLargeAndTheGoneLeftUnread()
        {
            Put("deep/inside/mux2.sv", Mux);
            Put("deep/inside/big.hex", new string('0', (int)ProjectImporter.MaxFileSize + 1));
            Put("deep/inside/.hidden.sv", Mux);
            string inside = Path.Combine(_folder, "deep", "inside");

            List<SourceFile> files = FileImport.ReadFiles(new[]
            {
                Path.Combine(inside, "mux2.sv"), Path.Combine(inside, "big.hex"), Path.Combine(inside, ".hidden.sv"), Path.Combine(inside, "gone.sv"),
            });

            CollectionAssert.AreEqual(new[] { "big.hex", "gone.sv", "mux2.sv" }, files.Select(f => f.Path), "by name, in order, the hidden one passed over");
            Assert.AreEqual(Mux, files.Single(f => f.Path == "mux2.sv").Text);
            Assert.IsFalse(files.Single(f => f.Path == "big.hex").WasRead, "never read into memory");
            Assert.IsFalse(files.Single(f => f.Path == "gone.sv").WasRead, "gone since it was chosen: unread, and the import says so");
        }

        /// <summary>
        /// The files a browser's page read come to the import in the order the desktop reads a folder in, by their paths: they
        /// came in the order the browser listed them, and the import runs the first testbench it finds, so the same folder could
        /// run another testbench in a browser than on the desktop, or another browser's (browser-7).
        /// </summary>
        [Test]
        public void TheFilesABrowserRead_ComeInTheOrderTheDesktopReadsAFolderIn()
        {
            Put("tb_mux.sv", "module tb_mux;\nendmodule\n");
            Put("mux2.sv", Mux);
            Put("lib/and2.sv", "module and2 (input logic a, b, output logic y);\n    assign y = a & b;\nendmodule\n");
            List<string> desktop = FileImport.ReadFolder(_folder).Select(f => f.Path).ToList();

            const string Picked = "{\"files\":[" +
                "{\"path\":\"tb_mux.sv\",\"text\":\"module tb_mux;\\nendmodule\\n\",\"read\":true,\"size\":24}," +
                "{\"path\":\"lib/and2.sv\",\"text\":\"\",\"read\":false,\"size\":80}," +
                "{\"path\":\"mux2.sv\",\"text\":\"\",\"read\":false,\"size\":70}]}";
            List<SourceFile> browser = FileImport.FilesPicked(Picked);

            CollectionAssert.AreEqual(desktop, browser.Select(f => f.Path), "the browser's in the desktop's order");
            Assert.AreEqual("module tb_mux;\nendmodule\n", browser.Single(f => f.Path == "tb_mux.sv").Text, "each with what was read of it");
        }
    }
}
