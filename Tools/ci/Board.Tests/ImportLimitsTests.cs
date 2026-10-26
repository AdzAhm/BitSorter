using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// What an import takes (Part 13f): an import is untrusted text, and the wrong folder is the usual way in, so there are
    /// so many files, so much in all and so much a file -- each refused or left out with the reason, never read into memory
    /// regardless. The readers ask the same rule (<see cref="ProjectImporter.Reads"/>), so a desktop and a browser agree.
    /// </summary>
    public class ImportLimitsTests
    {
        private const string Mux =
            "module mux2 (input logic a, b, s, output logic y);\n    assign y = s ? b : a;\nendmodule\n";

        /// <summary>A file of nothing but comments, <paramref name="size"/> characters long.</summary>
        private static string Filler(long size)
        {
            const string line = "// filler, written for BitSorter's tests\n";
            return string.Concat(Enumerable.Repeat(line, (int)(size / line.Length) + 1)).Substring(0, (int)size);
        }

        private static string Said(ProjectImport import) => string.Join("\n", import.Notes);

        [Test]
        public void MoreFilesThanAnImportTakes_AreRefused_AskingWhetherItIsOneProject()
        {
            var files = new List<SourceFile> { new SourceFile("mux2.sv", Mux) };
            for (int i = 0; i < ProjectImporter.MaxFiles; i++)
                files.Add(new SourceFile($"notes/{i}.txt", "a note\n"));

            ProjectImport import = ProjectImporter.Import(files);

            Assert.IsFalse(import.Succeeded);
            StringAssert.Contains("There are 201 files here, and an import takes at most 200. Is this one project's folder?", Said(import));
        }

        [Test]
        public void MoreInAllThanAnImportTakes_IsRefused()
        {
            var files = new List<SourceFile> { new SourceFile("mux2.sv", Mux) };
            for (int i = 0; i < 5; i++)
                files.Add(new SourceFile($"log{i}.txt", Filler(3_500_000)));

            ProjectImport import = ProjectImporter.Import(files);

            Assert.IsFalse(import.Succeeded);
            StringAssert.Contains("The files hold 16.7 MB together, and an import takes at most 16 MB.", Said(import));
        }

        [Test]
        public void AFileLargerThanAFileCanBe_IsLeftOut_AndTheRestImported()
        {
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("mux2.sv", Mux), new SourceFile("big.hex", Filler(4_718_592)) });

            Assert.IsTrue(import.Succeeded, Said(import));
            Assert.IsNotNull(import.Project.Find("mux2"));
            Assert.IsEmpty(import.Project.Files, "the large file is not one of the project's");
            ImportNote note = import.Notes.Single(n => n.File == "big.hex");
            Assert.IsTrue(note.IsError);
            Assert.AreEqual("It is 4.5 MB, and a file can be at most 4 MB, so it was left out.", note.Message);
        }

        /// <summary>A reader leaves a file unread by its size alone, and the import says it as it would of the text.</summary>
        [Test]
        public void AFileAReaderLeftUnread_IsNamed_ByItsSize_OrAsNotRead()
        {
            ProjectImport import = ProjectImporter.Import(new[]
            {
                new SourceFile("mux2.sv", Mux),
                SourceFile.Unread("lecture.mp4", 9_437_184),
                SourceFile.Unread("locked.sv", 120),
            });

            Assert.IsTrue(import.Succeeded, Said(import));
            Assert.AreEqual("It is 9 MB, and a file can be at most 4 MB, so it was left out.", import.Notes.Single(n => n.File == "lecture.mp4").Message);
            Assert.AreEqual("It was not read, so it was left out.", import.Notes.Single(n => n.File == "locked.sv").Message);
            Assert.IsEmpty(import.Project.Files);
        }

        /// <summary>
        /// A file too large to read is left out on its own and is no part of the whole (Part 13f review): a folder with a
        /// 17 MB lecture recording beside its code was refused outright, where one with 15 MB imported the code.
        /// </summary>
        [Test]
        public void AFileTooLargeToRead_IsNoPartOfTheWhole()
        {
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("mux2.sv", Mux), SourceFile.Unread("lecture.zip", 17L << 20) });

            Assert.IsTrue(import.Succeeded, Said(import));
            Assert.AreEqual("It is 17 MB, and a file can be at most 4 MB, so it was left out.", import.Notes.Single(n => n.File == "lecture.zip").Message);
            Assert.IsTrue(ProjectImporter.Reads(2, Mux.Length), "a reader counts it out too");
        }

        /// <summary>A size is rounded up, so one a byte past a limit never reads as the limit itself (Part 13f review).</summary>
        [Test]
        public void ASizeJustPastTheLimit_DoesNotReadAsTheLimit()
        {
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("mux2.sv", Mux), SourceFile.Unread("big.hex", ProjectImporter.MaxFileSize + 1) });

            Assert.AreEqual("It is 4.1 MB, and a file can be at most 4 MB, so it was left out.", import.Notes.Single(n => n.File == "big.hex").Message);
        }

        /// <summary>
        /// Files that name each other in a chain are found in one pass over the text, whatever order they come in (Part 13f
        /// review): read against every text once a pass, a chain listed the wrong way round took a pass a file -- 20 s for 160.
        /// </summary>
        [Test]
        public void AChainOfFilesNamingEachOther_IsFoundInOnePass()
        {
            // The bench names f149, each file names the one before it, and they are listed f000 first: the order in which
            // reading the list once a pass found one more file a pass.
            const int chain = 150;
            string padding = Filler(20_000);
            var files = new List<SourceFile> { new SourceFile("rom_tb.sv", "module rom_tb;\n    initial $display(\"%s\", \"f149.txt\");\nendmodule\n") };
            for (int i = 0; i < chain; i++)
                files.Add(new SourceFile($"f{i:000}.txt", (i == 0 ? "" : $"see \"f{i - 1:000}.txt\"\n") + padding));

            var clock = System.Diagnostics.Stopwatch.StartNew();
            ProjectImport import = ProjectImporter.Import(files);
            double seconds = clock.Elapsed.TotalSeconds;

            Assert.IsTrue(import.Succeeded, Said(import));
            Assert.AreEqual(chain, import.Project.Files.Count, "every file of the chain kept");
            Assert.Less(seconds, 3);
        }

        [Test]
        public void AReaderReads_OnlyWhatAnImportWouldTake()
        {
            Assert.IsTrue(ProjectImporter.Reads(11, 80_000), "the course's largest project");
            Assert.IsTrue(ProjectImporter.Reads(ProjectImporter.MaxFiles, ProjectImporter.MaxTotalSize));
            Assert.IsFalse(ProjectImporter.Reads(ProjectImporter.MaxFiles + 1, 1_000));
            Assert.IsFalse(ProjectImporter.Reads(2, ProjectImporter.MaxTotalSize + 1));
        }
    }
}
