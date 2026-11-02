using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A project exported for ModelSim (Part 13a): a zip any tool reads, a folder named for the project holding every file
    /// it prints as and a <c>run.do</c> that compiles them in order and runs the top; the same project and moment always
    /// the same bytes. That the printed files run under Icarus as they run here is <c>BoardOracleTests</c>'.
    /// </summary>
    public class ProjectExportTests
    {
        private static readonly DateTime When = new DateTime(2026, 10, 26, 16, 30, 12);

        private static Dictionary<string, string> Unzipped(byte[] zip)
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            var files = new Dictionary<string, string>();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                files[entry.FullName] = reader.ReadToEnd();
            }

            return files;
        }

        [TestCaseSource(typeof(SampleBoards), nameof(SampleBoards.Names))]
        public void AnExport_IsAZipOfEveryPrintedFile_AndARunScript(string board)
        {
            Project project = SampleBoards.Build(board);
            byte[] zip = ProjectExport.Zip(project, When);
            Dictionary<string, string> files = Unzipped(zip);

            IReadOnlyList<PrintedSource> printed = BoardPrinter.Print(project);
            string folder = ProjectExport.FolderOf(project) + "/";
            CollectionAssert.AreEqual(printed.Select(p => folder + p.Path).Append(folder + ProjectExport.RunScript), files.Keys,
                "every printed file, in the order printed, then the script, in a folder named for the project");
            foreach (PrintedSource file in printed)
                Assert.AreEqual(file.Text, files[folder + file.Path], file.Path);
        }

        [Test]
        public void TheRunScript_CompilesInOrder_SimulatesTheTestbench_AndRunsIt()
        {
            Project project = SampleBoards.Build("full_adder");
            CompiledProject compiled = BoardCompiler.Compile(project);
            IReadOnlyList<PrintedSource> printed = BoardPrinter.Print(compiled);
            string[] lines = ProjectExport.RunDo(project, compiled, printed).Split('\n').Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();

            CollectionAssert.AreEqual(new[]
            {
                "vlib work",
                "vlog -sv " + string.Join(" ", printed.Select(p => p.Path)),
                "vsim -voptargs=+acc work." + compiled.Testbench,
                "add wave -r /*",
                "run -all",
            }, lines);
            Assert.AreEqual(compiled.Top, compiled.Testbench, "sanity: the top simulated is the testbench");
        }

        [Test]
        public void TheSameProjectAndMoment_MakeTheSameBytes_AndTheZipKeepsTheMoment()
        {
            Project project = SampleBoards.Build("full_adder");
            CollectionAssert.AreEqual(ProjectExport.Zip(project, When), ProjectExport.Zip(project, When));

            using var archive = new ZipArchive(new MemoryStream(ProjectExport.Zip(project, When)), ZipArchiveMode.Read);
            DateTimeOffset kept = archive.Entries[0].LastWriteTime;
            Assert.AreEqual((When.Year, When.Month, When.Day, When.Hour, When.Minute, When.Second),
                (kept.Year, kept.Month, kept.Day, kept.Hour, kept.Minute, kept.Second), "to the two seconds a zip keeps");
        }

        [Test]
        public void TheChecksum_IsTheZipFormatsCrc32()
        {
            Assert.AreEqual(0x414FA339u, ZipWriter.Crc32(Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog")));
            Assert.AreEqual(0u, ZipWriter.Crc32(Array.Empty<byte>()));
        }

        [Test]
        public void AWrittenModule_IsExportedAsItsOwnText()
        {
            Project project = SampleBoards.Build("written");
            Dictionary<string, string> files = Unzipped(ProjectExport.Zip(project, When));
            StringAssert.EndsWith(SampleBoards.WrittenAdder, files[ProjectExport.FolderOf(project) + "/add4.sv"]);
        }

        /// <summary>
        /// Code pasted from a slide: BitSorter reads a curly quote as an apostrophe and a dash as a minus, with a warning, and
        /// runs it -- so the file ModelSim is given must say what BitSorter read, or ModelSim refuses what ran here. A comment
        /// and a string are left as written: nothing there was read as code.
        /// </summary>
        [Test]
        public void AWrittenModulesLookalikes_AreExportedAsWhatTheyWereReadAs()
        {
            const string pasted =
                "// it’s a “small” adder – pasted\n" +
                "module paste (input logic [1:0] a, b, output logic [1:0] y, output logic z);\n" +
                "    assign y = a – b;\n" +
                "    assign z = a[0] & 1’b1;\n" +
                "    initial $display(“it’s on”);\n" +
                "endmodule\n";
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("paste", pasted).Done);
            Assert.IsTrue(editor.SetTop("paste").Done);
            string exported = Unzipped(ProjectExport.Zip(editor.Project, When))[ProjectExport.FolderOf(editor.Project) + "/paste.sv"];

            StringAssert.Contains("    assign y = a - b;\n", exported);
            StringAssert.Contains("    assign z = a[0] & 1'b1;\n", exported);
            StringAssert.Contains("    initial $display(\"it’s on\");\n", exported, "the quotes that close the string, read as \"; the apostrophe inside it, its text");
            StringAssert.Contains("// it’s a “small” adder – pasted\n", exported, "a comment as written");
        }

        [Test]
        public void AnIncludedHeadersLookalikes_AreExportedAsRead_AndADataFileAsWritten()
        {
            // The 15i-2 review: a header an include reads is code too, and went out as typed.
            var editor = new BoardEditor(Project.New("scratch") with
            {
                Files = new[]
                {
                    new ProjectFile { Name = "defs.svh", Text = "`define K 4’d3\n" },
                    new ProjectFile { Name = "notes.hex", Text = "// ’ kept\n0f\n" },
                },
            });
            Assert.IsTrue(editor.AddWrittenModule("m",
                "`include \"defs.svh\"\nmodule m (output logic [3:0] y);\n    assign y = `K;\nendmodule\n").Done, "read and run, with a warning");
            Project project = editor.Project;
            Dictionary<string, string> files = Unzipped(ProjectExport.Zip(project, When));
            string folder = ProjectExport.FolderOf(project);
            Assert.AreEqual("`define K 4'd3\n", files[folder + "/defs.svh"]);
            Assert.AreEqual("// ’ kept\n0f\n", files[folder + "/notes.hex"], "a memory image is data, written as it is");
        }

        [Test]
        public void TheProjectsOtherFiles_AreBesideTheModules_AndNotCompiled()
        {
            Project project = SampleBoards.Build("half_adder") with
            {
                Files = new[] { new ProjectFile { Name = "table.hex", Text = "0f\n" }, new ProjectFile { Name = "defs.svh", Text = "// shared\n" } },
            };
            Dictionary<string, string> files = Unzipped(ProjectExport.Zip(project, When));
            string folder = ProjectExport.FolderOf(project);

            Assert.AreEqual("0f\n", files[folder + "/table.hex"]);
            Assert.AreEqual("// shared\n", files[folder + "/defs.svh"]);
            string vlog = files[folder + "/" + ProjectExport.RunScript].Split('\n').Single(l => l.StartsWith("vlog"));
            StringAssert.DoesNotContain("table.hex", vlog, "an include and a memory image are read by the code, not compiled");
            StringAssert.DoesNotContain("defs.svh", vlog);
        }

        [Test]
        public void AProjectNameThatIsNoName_IsNotAPathInTheZip()
        {
            // A save edited by hand can say anything; the zip's folder is a name, or "project".
            foreach (string name in new[] { "../outside", "/etc", "C:\\temp", "a/b", "" })
            {
                Project project = SampleBoards.Build("half_adder") with { Name = name };
                Assert.AreEqual("project", ProjectExport.FolderOf(project), name);
                Assert.IsTrue(Unzipped(ProjectExport.Zip(project, When)).Keys.All(k => k.StartsWith("project/", StringComparison.Ordinal)), name);
            }
        }

        [Test]
        public void NoModuleIsWrittenToTheCellsFile_NorToAnotherModulesFile()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            string top = editor.Project.Top;

            Assert.IsFalse(editor.RenameModule(top, "bitsorter_cells").Done, "the cells are printed to bitsorter_cells.sv");
            Assert.IsFalse(editor.RenameModule(top, "BitSorter_Cells").Done, "which Windows takes for the same file");
            Assert.IsTrue(editor.AddModule("adder").Done);
            Assert.IsFalse(editor.AddModule("Adder").Done, "Adder.sv and adder.sv are one file on Windows, where the course's ModelSim runs");
        }

        [Test]
        public void TheRunScript_QuotesANameWithADollar_SoTclDoesNotReadAVariable()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            Assert.IsTrue(editor.RenameModule(editor.Project.Top, "half$adder").Done);
            Project project = editor.Project;
            CompiledProject compiled = BoardCompiler.Compile(project);
            string script = ProjectExport.RunDo(project, compiled, BoardPrinter.Print(compiled));

            foreach (string line in script.Split('\n').Where(l => !l.StartsWith("#")))
                Assert.IsFalse(Regex.Replace(line, @"\{[^}]*\}", "").Contains("$"), "a $ outside braces: " + line);
            StringAssert.Contains("{half$adder.sv}", script);
        }
    }
}
