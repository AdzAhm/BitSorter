using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BitSorter.Ci
{
    /// <summary>One place the privacy scan objects to.</summary>
    /// <remarks>
    /// It carries where, never what: a finding that printed the number it found would put that number
    /// into a test log, which in CI is public.
    /// </remarks>
    public readonly struct PrivacyFinding
    {
        public readonly string Path;
        public readonly int Line;
        public readonly string Rule;

        public PrivacyFinding(string path, int line, string rule)
        {
            Path = path;
            Line = line;
            Rule = rule;
        }

        public override string ToString() => Line > 0 ? $"{Path}:{Line}: {Rule}" : $"{Path}: {Rule}";
    }

    /// <summary>
    /// Keeps student ID numbers and course material out of a public repository.
    /// </summary>
    /// <remarks>
    /// BitSorter 5.0 is built against a university course whose files hold ID numbers -- in handouts,
    /// in testbench operands, in folder and archive names -- and whose code is the university's. The
    /// repository is public and git keeps everything forever, so the check has to happen before a
    /// commit, not after.
    ///
    /// **Two rules, for two situations.**
    ///
    /// The generic rule runs everywhere, CI included, and needs nothing private: no hand-written file
    /// may hold a standalone nine-digit number, nor an eight-digit one that passes the ID check digit
    /// (an ID with a leading zero is often written without it). An eight-digit number that fails the
    /// check digit is let through, because they are common and none of the 76 in the repository on
    /// 2026-10-12 passed it. Underscores between digits are ignored first, since SystemVerilog writes
    /// a number as <c>32'd65_535</c> as readily as without them. The fraction of a decimal is not a
    /// number of its own. Anything known to be harmless goes on the allowlist, with a reason.
    ///
    /// The private rule runs only on the machine that has the course folder, pointed at by
    /// <c>BITSORTER_PRIVATE_DIR</c>. It reads the real numbers out of that folder -- file contents and
    /// file names both -- at the moment it runs, holds them only in memory, and looks for each in
    /// every file in the working tree and in the whole of git history. It also looks for runs of
    /// lines copied from the course's code. The numbers are never written anywhere, the allowlist
    /// included.
    ///
    /// Unity's serialised YAML (scenes, assets, prefabs, metas) is left out of the generic rule: it is
    /// machine-written, full of nine-digit file ids, and never carries course content. The private
    /// rule reads it anyway, because there the numbers are exact and a match is never a coincidence.
    /// </remarks>
    public static class PrivacyScan
    {
        /// <summary>The kinds of file the generic rule reads: everything a person writes by hand.</summary>
        public static readonly HashSet<string> HandWrittenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "", ".cs", ".md", ".json", ".txt", ".yml", ".yaml", ".csproj", ".slnx", ".sv", ".svh", ".v",
            ".vh", ".inc", ".do", ".hex", ".mem", ".py", ".ps1", ".sh", ".html", ".htm", ".css", ".js",
            ".jslib", ".jspre", ".xml", ".asmdef", ".asmref", ".rsp", ".shader", ".hlsl", ".cginc", ".uss",
            ".uxml", ".gitignore", ".gitattributes", ".editorconfig",
        };

        /// <summary>
        /// Memory images: columns of hex words. An all-digit eight-character word passes the check
        /// digit one time in ten, so the eight-digit rule skips them; nine digits cannot occur in one.
        /// </summary>
        private static readonly HashSet<string> HexImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".hex", ".mem",
        };

        /// <summary>The kinds of file the copied-lines check compares, on both sides.</summary>
        private static readonly HashSet<string> CodeAndProse = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".sv", ".svh", ".v", ".vh", ".inc", ".do", ".tcl", ".txt", ".md", ".cs", ".json", ".html", ".py",
        };

        // A run of digits counts on its own: not inside a longer number, and not the fraction of a
        // decimal -- TextMesh Pro's shaders multiply by 0.001953125, which no one was ever issued.
        private static readonly Regex DigitUnderscore = new Regex(@"(?<=\d)_(?=\d)", RegexOptions.Compiled);
        private static readonly Regex NineDigits = new Regex(@"(?<!\d)(?<!\d\.)\d{9}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex EightDigits = new Regex(@"(?<!\d)(?<!\d\.)\d{8}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex EightOrNine = new Regex(@"(?<!\d)(?<!\d\.)\d{8,9}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public const string NineDigitRule = "a nine-digit number";
        public const string CheckDigitRule = "an eight-digit number that passes the ID check digit";
        public const string PrivateNumberRule = "a number from the private course folder";
        public const string CopiedLinesRule = "a run of lines copied from the private course folder";

        /// <summary>How many consecutive meaningful lines in common count as copying.</summary>
        public const int CopiedRun = 6;

        /// <summary>
        /// Whether these eight or nine digits pass the Israeli ID check digit: digits weighted 1, 2,
        /// 1, 2 ... from the left, a two-digit product counted as the sum of its digits, the total a
        /// multiple of ten. Eight digits are read with the leading zero they were written without.
        /// </summary>
        public static bool PassesIdCheckDigit(string digits)
        {
            if (digits == null || digits.Length < 8 || digits.Length > 9 || !digits.All(char.IsDigit))
                return false;

            string padded = digits.PadLeft(9, '0');
            int sum = 0;

            for (int i = 0; i < 9; i++)
            {
                int product = (padded[i] - '0') * (i % 2 == 0 ? 1 : 2);
                sum += product > 9 ? product - 9 : product;
            }

            return sum % 10 == 0;
        }

        /// <summary>The text with every underscore between two digits taken out. Line breaks are kept.</summary>
        public static string Normalise(string text) => DigitUnderscore.Replace(text, "");

        /// <summary>
        /// Every number in <paramref name="text"/> the generic rule objects to, with its line and rule.
        /// </summary>
        /// <remarks>The number is returned for the allowlist's sake. Callers must not print it.</remarks>
        public static IEnumerable<(int Line, string Number, string Rule)> SuspectNumbers(string text, bool hexImage)
        {
            string[] lines = Normalise(text).Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in NineDigits.Matches(lines[i]))
                    yield return (i + 1, match.Value, NineDigitRule);

                if (hexImage)
                    continue;

                foreach (Match match in EightDigits.Matches(lines[i]))
                {
                    if (PassesIdCheckDigit(match.Value))
                        yield return (i + 1, match.Value, CheckDigitRule);
                }
            }
        }

        /// <summary>The generic rule over the given repository files.</summary>
        /// <param name="root">The repository's root folder.</param>
        /// <param name="files">Repository-relative paths, forward-slashed, as git lists them.</param>
        /// <param name="allowlist">Entries of the form "path number", known to be harmless.</param>
        public static List<PrivacyFinding> ScanGeneric(string root, IEnumerable<string> files, ISet<string> allowlist)
        {
            var findings = new List<PrivacyFinding>();

            foreach (string file in files)
            {
                string extension = System.IO.Path.GetExtension(file);
                if (!HandWrittenExtensions.Contains(extension))
                    continue;

                string text = ReadText(root, file);
                if (text == null)
                    continue;

                foreach ((int line, string number, string rule) in SuspectNumbers(text, HexImages.Contains(extension)))
                {
                    if (!allowlist.Contains(file + " " + number))
                        findings.Add(new PrivacyFinding(file, line, rule));
                }
            }

            return findings;
        }

        /// <summary>
        /// Reads the allowlist: one entry a line, the file and the number then a reason; '#' starts a
        /// comment. Returns "path number" keys.
        /// </summary>
        public static HashSet<string> LoadAllowlist(string path)
        {
            var entries = new HashSet<string>(StringComparer.Ordinal);

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split((char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    throw new FormatException(
                        $"An allowlist entry needs a file, a number and a reason: \"{line}\" in {path}.");

                entries.Add(parts[0] + " " + parts[1]);
            }

            return entries;
        }

        /// <summary>
        /// Every eight- or nine-digit number in the private folder -- in file contents where the file is
        /// text, and in every file and folder name -- under the same check-digit rule as the generic
        /// scan. Held in memory only.
        /// </summary>
        public static HashSet<string> PrivateNumbers(string privateDir)
        {
            var numbers = new HashSet<string>(StringComparer.Ordinal);

            foreach (string file in Directory.EnumerateFiles(privateDir, "*", SearchOption.AllDirectories))
            {
                string relative = System.IO.Path.GetRelativePath(privateDir, file);
                AddNumbers(numbers, relative, hexImage: false);

                string extension = System.IO.Path.GetExtension(file);
                if (CodeAndProse.Contains(extension) || HexImages.Contains(extension))
                    AddNumbers(numbers, File.ReadAllText(file), HexImages.Contains(extension));
            }

            return numbers;
        }

        private static void AddNumbers(HashSet<string> numbers, string text, bool hexImage)
        {
            foreach ((int _, string number, string _) in SuspectNumbers(text, hexImage))
                numbers.Add(number);
        }

        /// <summary>
        /// Every file in <paramref name="files"/>, of any kind, that holds one of
        /// <paramref name="numbers"/> as a standalone number.
        /// </summary>
        public static List<PrivacyFinding> FindNumbers(string root, IEnumerable<string> files, ISet<string> numbers)
        {
            var findings = new List<PrivacyFinding>();
            if (numbers.Count == 0)
                return findings;

            foreach (string file in files)
            {
                string text = ReadText(root, file);
                if (text == null)
                    continue;

                string[] lines = Normalise(text).Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match match in EightOrNine.Matches(lines[i]))
                    {
                        if (numbers.Contains(match.Value))
                            findings.Add(new PrivacyFinding(file, i + 1, PrivateNumberRule));
                    }
                }
            }

            return findings;
        }

        /// <summary>
        /// The commits, on any branch, whose changes added or removed one of <paramref name="numbers"/>.
        /// </summary>
        /// <remarks>
        /// Git's pickaxe, once a number. It matches the digits anywhere, so a hit inside a longer
        /// number is possible: a finding here is a commit to look at, not proof. It does not see a
        /// number written with underscores; the working-tree check does.
        /// </remarks>
        public static List<PrivacyFinding> FindNumbersInHistory(string root, ISet<string> numbers, TimeSpan timeoutEach)
        {
            var findings = new List<PrivacyFinding>();

            foreach (string number in numbers)
            {
                ProcessResult result = ProcessRunner.Run(
                    "git", new[] { "log", "--all", "--format=%h", "-S" + number }, root, timeoutEach);

                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        "git log -S failed or ran out of time while searching history: " + result.StandardError);

                foreach (string commit in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    findings.Add(new PrivacyFinding("commit " + commit.Trim(), 0, PrivateNumberRule));
            }

            return findings;
        }

        /// <summary>
        /// Runs of <see cref="CopiedRun"/> or more meaningful lines in a repository file that all appear
        /// in the private folder's code.
        /// </summary>
        /// <remarks>
        /// Lines are compared after trimming, lower-casing and collapsing whitespace. A line too short or
        /// too bare to mean anything -- <c>end</c>, <c>endmodule</c>, a lone brace -- neither counts
        /// towards a run nor breaks one, so blank lines and <c>end</c>s cannot hide a copied block.
        /// </remarks>
        public static List<PrivacyFinding> FindCopiedLines(
            string root, IEnumerable<string> files, string privateDir, ISet<string> exemptFiles)
        {
            var privateLines = new HashSet<string>(StringComparer.Ordinal);

            foreach (string file in Directory.EnumerateFiles(privateDir, "*", SearchOption.AllDirectories))
            {
                if (!CodeAndProse.Contains(System.IO.Path.GetExtension(file)))
                    continue;

                foreach (string line in File.ReadAllLines(file))
                {
                    string key = Meaningful(line);
                    if (key != null)
                        privateLines.Add(key);
                }
            }

            var findings = new List<PrivacyFinding>();

            foreach (string file in files)
            {
                if (exemptFiles.Contains(file) || !CodeAndProse.Contains(System.IO.Path.GetExtension(file)))
                    continue;

                string text = ReadText(root, file);
                if (text == null)
                    continue;

                string[] lines = text.Split('\n');
                int run = 0, start = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string key = Meaningful(lines[i]);
                    if (key == null)
                        continue;

                    if (!privateLines.Contains(key))
                    {
                        run = 0;
                        continue;
                    }

                    if (run == 0)
                        start = i + 1;

                    if (++run == CopiedRun)
                        findings.Add(new PrivacyFinding(file, start, CopiedLinesRule));
                }
            }

            return findings;
        }

        private static string Meaningful(string line)
        {
            string key = Whitespace.Replace(line.Trim(), " ").ToLowerInvariant();
            return key.Length >= 16 && key.Any(char.IsLetter) ? key : null;
        }

        /// <summary>
        /// The repository's files as git sees them: tracked, plus new files not yet added that are not
        /// ignored -- a file about to be committed is exactly the one to check.
        /// </summary>
        public static List<string> RepositoryFiles(string root)
        {
            ProcessResult result = ProcessRunner.Run(
                "git", new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard" }, root,
                TimeSpan.FromSeconds(60));

            if (!result.Succeeded)
                throw new InvalidOperationException("git ls-files failed: " + result.StandardError);

            return result.StandardOutput
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim('\r', '\n'))
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The file as text, or null if it is gone or larger than any hand-written file.</summary>
        private static string ReadText(string root, string file)
        {
            string full = System.IO.Path.Combine(root, file);
            var info = new FileInfo(full);

            if (!info.Exists || info.Length > 32 * 1024 * 1024)
                return null;

            return File.ReadAllText(full, Encoding.UTF8);
        }
    }
}
