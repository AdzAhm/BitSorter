using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// The repository holds no student ID number and no copied course material. See
    /// <see cref="PrivacyScan"/> for the rules and why there are two.
    /// </summary>
    /// <remarks>
    /// Every number planted in these tests is assembled from two halves at run time. Written out
    /// whole, it would be a nine-digit number in a hand-written file, and the scan would rightly fail
    /// on this file.
    /// </remarks>
    public class PrivacyScanTests
    {
        private const string AllowlistPath = "Tools/ci/Repo.Tests/privacy-allowlist.txt";
        private const string PrivateDirVariable = "BITSORTER_PRIVATE_DIR";

        private static string Join(string left, string right) => left + right;

        /// <summary>A made-up number that passes the check digit: eight digits and the digit that completes them.</summary>
        private static string ValidId(string firstEight)
        {
            for (char last = '0'; last <= '9'; last++)
            {
                string candidate = firstEight + last;
                if (PrivacyScan.PassesIdCheckDigit(candidate))
                    return candidate;
            }

            throw new InvalidOperationException("Every eight digits have exactly one completing check digit.");
        }

        [Test]
        public void NoRepositoryFile_HoldsAnIdLikeNumber()
        {
            string root = RepoRoot.Path;

            // The allowlist holds the very numbers it excuses. The private rule still reads it, as it
            // reads every file, so a real ID added to it by mistake is caught there.
            IEnumerable<string> files = PrivacyScan.RepositoryFiles(root).Where(file => file != AllowlistPath);

            List<PrivacyFinding> findings = PrivacyScan.ScanGeneric(
                root, files, PrivacyScan.LoadAllowlist(RepoRoot.Combine(AllowlistPath)));

            Assert.IsEmpty(findings,
                "these could be somebody's ID number. Take it out, or -- only if you have checked it is " +
                "not one -- add it to " + AllowlistPath + " with a reason:\n  " +
                string.Join("\n  ", findings));
        }

        [Test]
        public void EveryAllowlistEntry_StillNamesANumberThatIsThere()
        {
            string root = RepoRoot.Path;
            HashSet<string> allowlist = PrivacyScan.LoadAllowlist(RepoRoot.Combine(AllowlistPath));
            var present = new HashSet<string>(StringComparer.Ordinal);

            foreach (string file in PrivacyScan.RepositoryFiles(root))
            {
                string full = Path.Combine(root, file);
                if (!File.Exists(full) || !PrivacyScan.HandWrittenExtensions.Contains(Path.GetExtension(file)))
                    continue;

                foreach ((int _, string number, string _) in PrivacyScan.SuspectNumbers(File.ReadAllText(full), hexImage: false))
                    present.Add(file + " " + number);
            }

            string[] stale = allowlist.Where(entry => !present.Contains(entry)).Select(entry => entry.Split(' ')[0]).ToArray();

            Assert.IsEmpty(stale,
                "these allowlist entries no longer match anything in their file, so they excuse nothing " +
                "and should go: " + string.Join(", ", stale));
        }

        [TestCase("a = {0};", TestName = "A plain operand")]
        [TestCase("b = 32'd{0};", TestName = "A sized SystemVerilog literal")]
        [TestCase("The number was {0}.", TestName = "The end of a sentence")]
        [TestCase("folder_{0}_sim1", TestName = "Inside a folder name")]
        public void ANineDigitNumber_IsFound(string template)
        {
            string text = string.Format(template, Join("1234", "56782"));

            Assert.That(PrivacyScan.SuspectNumbers(text, hexImage: false).Select(hit => hit.Rule),
                Is.EqualTo(new[] { PrivacyScan.NineDigitRule }));
        }

        [Test]
        public void ANineDigitNumber_WrittenWithUnderscores_IsFound()
        {
            string text = "c = 32'd" + Join("123_4", "56_782") + ";";

            Assert.That(PrivacyScan.SuspectNumbers(text, hexImage: false).Count(), Is.EqualTo(1));
        }

        // The eight digits planted here fail the check digit, so only a nine-digit match could object.
        [TestCase("{0}00", TestName = "Ten digits")]
        [TestCase("1234567", TestName = "Seven digits")]
        [TestCase("32'h{0}", TestName = "Eight digits that fail the check digit")]
        [TestCase("x * 0.{0}9", TestName = "The fraction of a decimal")]
        public void NotEveryLongNumber_IsAnId(string template)
        {
            string eight = Join("1234", "5678");
            Assert.IsFalse(PrivacyScan.PassesIdCheckDigit(eight), "the planted digits must fail the check digit");

            Assert.IsEmpty(PrivacyScan.SuspectNumbers(string.Format(template, eight), hexImage: false).ToList());
        }

        [Test]
        public void AnEightDigitNumber_IsFound_OnlyWhenItsCheckDigitHolds()
        {
            // An ID written without its leading zero: "0" + eight digits passes, the eight alone are tested.
            string padded = ValidId(Join("0123", "4567"));
            string eight = padded.Substring(1);
            string wrong = eight.Substring(0, 7) + (char)('0' + (eight[7] - '0' + 1) % 10);

            Assert.That(PrivacyScan.SuspectNumbers("id " + eight, hexImage: false).Select(hit => hit.Rule),
                Is.EqualTo(new[] { PrivacyScan.CheckDigitRule }), "eight digits that pass the check digit");
            Assert.That(PrivacyScan.SuspectNumbers("id " + wrong, hexImage: false), Is.Empty,
                "the same eight digits with the last one changed");
            Assert.That(PrivacyScan.SuspectNumbers(eight, hexImage: true), Is.Empty,
                "a word in a memory image");
        }

        [Test]
        public void TheCheckDigit_IsTheIdRule()
        {
            // The rule by hand for 0 1 2 3 4 5 6 7 c: weights 1 2 1 2 1 2 1 2 1 give
            // 0 + 2 + 2 + 6 + 4 + (10 -> 1) + 6 + (14 -> 5) = 26, so c = 4 completes it to 30.
            string worked = Join("0123", "45674");

            Assert.IsTrue(PrivacyScan.PassesIdCheckDigit(worked));
            Assert.AreEqual(worked, ValidId(Join("0123", "4567")));

            for (char other = '0'; other <= '9'; other++)
            {
                if (other != '4')
                    Assert.IsFalse(PrivacyScan.PassesIdCheckDigit(worked.Substring(0, 8) + other), "check digit " + other);
            }
        }

        [Test]
        public void CopiedLines_AreFound_AndShortAgreementsAreNot()
        {
            string scratch = Path.Combine(Path.GetTempPath(), "bitsorter-privacy-" + Guid.NewGuid().ToString("N"));
            string course = Path.Combine(scratch, "course");
            string repo = Path.Combine(scratch, "repo");
            Directory.CreateDirectory(course);
            Directory.CreateDirectory(repo);

            try
            {
                string[] block = Enumerable.Range(1, PrivacyScan.CopiedRun)
                    .Select(i => $"assign signal_number_{i} = first_input_{i} & second_input_{i};")
                    .ToArray();

                File.WriteAllLines(Path.Combine(course, "given.sv"), block);

                // The whole block, with blank lines and an "end" between its lines that must not hide it.
                File.WriteAllLines(Path.Combine(repo, "copied.sv"),
                    new[] { "// mine", block[0], "", block[1], "end", block[2], block[3], block[4], block[5] });

                // One line short of a run.
                File.WriteAllLines(Path.Combine(repo, "similar.sv"), block.Take(PrivacyScan.CopiedRun - 1));

                List<PrivacyFinding> findings = PrivacyScan.FindCopiedLines(
                    repo, new[] { "copied.sv", "similar.sv" }, course, new HashSet<string>());

                Assert.That(findings.Select(finding => finding.Path), Is.EqualTo(new[] { "copied.sv" }));
                Assert.AreEqual(2, findings[0].Line, "the run starts at the first copied line");
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        [Test]
        public void PrivateNumbers_AreFound_InFileContentsAndInFileNames()
        {
            string scratch = Path.Combine(Path.GetTempPath(), "bitsorter-privacy-" + Guid.NewGuid().ToString("N"));
            string inName = ValidId(Join("2345", "6789"));
            string inContent = ValidId(Join("3456", "7890"));
            Directory.CreateDirectory(Path.Combine(scratch, "work_" + inName));

            try
            {
                File.WriteAllText(Path.Combine(scratch, "work_" + inName, "tb.sv"), "a = 32'd" + inContent + ";");

                HashSet<string> numbers = PrivacyScan.PrivateNumbers(scratch);

                Assert.That(numbers, Is.EquivalentTo(new[] { inName, inContent }));
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        /// <summary>
        /// The private rule: the course folder's own numbers, nowhere in the working tree or the history,
        /// and no block of its code copied in.
        /// </summary>
        /// <remarks>
        /// Runs only where <c>BITSORTER_PRIVATE_DIR</c> names the course folder, which is only ever the
        /// developer's own machine; everywhere else it is ignored, and says so. The project skill
        /// <c>privacy-scan</c> sets the variable and runs it. Its messages give places and counts, never
        /// a number or a line.
        /// </remarks>
        [Test]
        public void ThePrivateCourseFolder_HasLeftNoTrace()
        {
            string privateDir = Environment.GetEnvironmentVariable(PrivateDirVariable);
            if (string.IsNullOrEmpty(privateDir))
                Assert.Ignore(PrivateDirVariable + " is not set, so there is no private folder to check against.");
            if (!Directory.Exists(privateDir))
                Assert.Fail(PrivateDirVariable + " names a folder that does not exist.");

            string root = RepoRoot.Path;
            List<string> files = PrivacyScan.RepositoryFiles(root);
            HashSet<string> numbers = PrivacyScan.PrivateNumbers(privateDir);

            var findings = new List<PrivacyFinding>();
            findings.AddRange(PrivacyScan.FindNumbers(root, files, numbers));
            findings.AddRange(PrivacyScan.FindNumbersInHistory(root, numbers, TimeSpan.FromMinutes(2)));
            findings.AddRange(PrivacyScan.FindCopiedLines(root, files, privateDir, new HashSet<string>()));

            TestContext.WriteLine($"checked {numbers.Count} private numbers against {files.Count} files and all of history");

            Assert.IsEmpty(findings,
                "the private course folder has left these traces. Nothing here may be committed -- and " +
                "a commit listed has already been, so stop and ask before pushing:\n  " +
                string.Join("\n  ", findings));
        }
    }
}
