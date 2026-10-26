using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
    }
}
