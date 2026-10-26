using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BitSorter.Ci;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// An import never throws and never hangs (Part 13f). Every project of the oracle corpus that imports is made wrong in
    /// the ways a file goes wrong -- cut off part way, characters changed, lines lost or said twice -- and must come back
    /// imported or refused, saying why, and compile if it imported, within a bound that only a hang would pass. The changes
    /// are drawn from fixed seeds, so a failure is the same on every run, and its message says what was changed where.
    /// </summary>
    public class ImportFuzzTests
    {
        private static readonly string[] Projects = { "import/alu", "import/mux", "import/counter", "processor" };

        /// <summary>How many changed copies of each project are imported.</summary>
        private const int Changes = 12;

        /// <summary>Far past an import's worst honest run, a testbench run to <see cref="ProjectImporter.MaxEvents"/>: only a hang reaches it.</summary>
        private const double Bound = 30;

        private const string Characters = " ;()[]{}`#@=<>!~^&|?:.,'\"\\/*-+0123456789abcxyzXZ\n\t";

        public static IEnumerable<TestCaseData> Cases() =>
            Projects.SelectMany(project => Enumerable.Range(0, Changes).Select(seed => new TestCaseData(project, seed)));

        [TestCaseSource(nameof(Cases))]
        public void AProjectMadeWrong_ImportsOrIsRefused_AndNeverThrowsOrHangs(string folder, int seed)
        {
            string root = RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder);
            var files = Directory.GetFiles(root).OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => (Name: Path.GetFileName(p), Text: File.ReadAllText(p))).ToList();
            var random = new Random(seed * 7919 + folder.Length);
            int which = random.Next(files.Count);
            (string how, string changed) = Change(files[which].Text, random);
            files[which] = (files[which].Name, changed);
            string what = $"{folder}, seed {seed}: {files[which].Name} {how}";

            var clock = Stopwatch.StartNew();
            ProjectImport import = null;
            Assert.DoesNotThrow(() => import = ProjectImporter.Import(files.Select(f => new SourceFile(f.Name, f.Text)).ToList()), what);
            if (import.Succeeded)
                Assert.DoesNotThrow(() => BoardCompiler.Compile(import.Project), what + ", compiled");

            Assert.Less(clock.Elapsed.TotalSeconds, Bound, what);
            Assert.IsTrue(import.Succeeded || import.Notes.Any(n => n.IsError), what + ": a refusal says why");
            TestContext.WriteLine($"{what}: {(import.Succeeded ? "imported" : "refused")} in {clock.ElapsedMilliseconds} ms, {import.Notes.Count} notes");
        }

        /// <summary>One way a file goes wrong, drawn from <paramref name="random"/>, and what it was.</summary>
        private static (string How, string Text) Change(string text, Random random)
        {
            string[] lines = text.Split('\n');
            switch (random.Next(4))
            {
                case 0:
                {
                    int at = random.Next(text.Length + 1);
                    return ($"cut off at {at} of {text.Length}", text.Substring(0, at));
                }

                case 1:
                {
                    var chars = new StringBuilder(text);
                    int count = 1 + random.Next(8);
                    var where = new List<int>();
                    for (int i = 0; i < count && chars.Length > 0; i++)
                    {
                        int at = random.Next(chars.Length);
                        chars[at] = Characters[random.Next(Characters.Length)];
                        where.Add(at);
                    }

                    return ($"with characters changed at {string.Join(", ", where)}", chars.ToString());
                }

                case 2:
                {
                    int first = random.Next(lines.Length), count = 1 + random.Next(5);
                    var kept = lines.Where((_, i) => i < first || i >= first + count);
                    return ($"without lines {first + 1} to {first + count}", string.Join("\n", kept));
                }

                default:
                {
                    int first = random.Next(lines.Length), count = Math.Min(1 + random.Next(5), lines.Length - first);
                    var twice = lines.Take(first + count).Concat(lines.Skip(first).Take(count)).Concat(lines.Skip(first + count));
                    return ($"with lines {first + 1} to {first + count} said twice", string.Join("\n", twice));
                }
            }
        }
    }
}
