using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Board.Tests;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// Part 15i-2: every slip BitSorter answers in a student's words is one Icarus refuses as well -- so the words never stand
    /// in for a refusal the language would not make -- but the procedural assign, which the language has, and Icarus and
    /// BitSorter run, BitSorter with a warning (M4; Docs/oracle-notes.md). Every slip mended, and every look-alike straightened as the export writes it,
    /// Icarus takes. And none of BitSorter's own designs is answered so: the corpus and the exercises' answers.
    /// </summary>
    public class FriendlyErrorOracleTests
    {
        private static IEnumerable<FriendlyErrorCase> Cases() => FriendlyErrorCases.All();

        private static bool IcarusTakes(Icarus icarus, IEnumerable<string> modules, out string explained)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-friendly-oracle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, "m.sv");
                File.WriteAllText(path, string.Join("\n", modules));
                using IcarusRun run = icarus.Simulate(new[] { path }, "m");
                explained = run.Explain();
                return run.Compile.Succeeded;
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { /* cleared with the temp folder */ }
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void IcarusRefusesTheSlip_AndTakesItMended(FriendlyErrorCase slip)
        {
            Icarus icarus = Icarus.Require();
            bool takesTyped = IcarusTakes(icarus, slip.Beside.Append(slip.Typed), out string typed);
            Assert.AreEqual(slip.IcarusTakesIt, takesTyped, typed);
            Assert.IsTrue(IcarusTakes(icarus, slip.Beside.Append(slip.Mended), out string mended), mended);
            if (slip.Lookalike)
                Assert.IsTrue(IcarusTakes(icarus, slip.Beside.Append(Lexer.Straightened(slip.Typed)), out string exported),
                    "what the export writes: " + exported);
        }

        [Test]
        public void NoneOfBitSortersOwnDesigns_IsAnsweredWithASlip()
        {
            string[] says = Cases().Select(c => c.Says).ToArray();
            var files = new List<string>(Directory.GetFiles(RepoRoot.Combine(Corpus.Folder), "*.sv", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(RepoRoot.Combine(Corpus.Folder), "*.svh", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(ExerciseFiles.Solutions, "*.sv", SearchOption.AllDirectories));
            Assert.Greater(files.Count, 50, "sanity: the corpus and the answers were found");

            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                Assert.AreSame(text, Lexer.Straightened(text), file + ": a look-alike in code");
                SyntaxTree tree = SyntaxReader.ReadText(text, Path.GetFileName(file));
                foreach (Diagnostic d in tree.Diagnostics)
                    Assert.IsFalse(says.Any(s => d.Message.Contains(s)), file + ": " + d);
            }
        }
    }
}
