using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.View;
using NUnit.Framework;

namespace BitSorter.EditMode.Tests
{
    /// <summary>
    /// What the desktop reads for an import (<see cref="FileImport.ReadFolder"/>, Part 13f): sizes looked at before anything
    /// is read, so a file past the import's largest is left unread, and every file when there are more than an import takes
    /// -- and a hidden folder's files neither read nor counted. Always a scratch folder (<see cref="FileImport.Redirected"/>),
    /// never the player's.
    /// </summary>
    public class FileImportTests
    {
        private const string Mux = "module mux2 (input logic a, b, s, output logic y);\n    assign y = s ? b : a;\nendmodule\n";

        private string _folder;

        [SetUp]
        public void RedirectTheImportFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "bitsorter-import-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            FileImport.Redirected = _folder;
        }

        [TearDown]
        public void PutTheImportFolderBack()
        {
            FileImport.Redirected = null;
            Directory.Delete(_folder, recursive: true);
        }

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

            List<SourceFile> files = FileImport.ReadFolder();

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

            List<SourceFile> files = FileImport.ReadFolder();

            Assert.AreEqual(ProjectImporter.MaxFiles + 1, files.Count);
            Assert.IsTrue(files.All(f => !f.WasRead), "none read: the import refuses them anyway");
            ProjectImport import = ProjectImporter.Import(files);
            Assert.IsFalse(import.Succeeded);
            StringAssert.Contains("an import takes at most 200", string.Join("\n", import.Notes));
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
            List<string> desktop = FileImport.ReadFolder().Select(f => f.Path).ToList();

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
