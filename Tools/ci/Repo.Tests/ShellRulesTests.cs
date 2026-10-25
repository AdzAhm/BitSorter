using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// The workbench's shell is built in code (Part 11a), as the rest of the interface is: its builders set every
    /// element's layout, colours and sizes, from the palette and <c>UiType</c>. So there is no UXML or USS anywhere --
    /// a style sheet would be a second place a colour or a size could live, and one that a builder's own style would
    /// silently override -- and no colour written into the shell's code.
    /// </summary>
    public class ShellRulesTests
    {
        private static readonly Regex ColourLiteral = new Regex(
            @"new\s+Color(32)?\s*\(|\bColor\.(red|green|blue|white|black|yellow|cyan|magenta|gray|grey)\b|#[0-9A-Fa-f]{6}\b",
            RegexOptions.Compiled);

        [Test]
        public void NoStyleSheetNorUxml_IsUnderAssets()
        {
            string[] found = Directory.EnumerateFiles(RepoRoot.Combine("Assets"), "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".uss", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".tss", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(RepoRoot.Path, f))
                .ToArray();

            Assert.IsEmpty(found, "the shell is built in code, its styles set by ShellTheme: " + string.Join(", ", found));
        }

        [Test]
        public void TheShellsCode_WritesNoColour_ButTakesEachFromThePalette()
        {
            var found = new List<string>();
            foreach (string file in Directory.EnumerateFiles(RepoRoot.Combine("Assets/Scripts/View/Shell"), "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) && ColourLiteral.IsMatch(lines[i]))
                        found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }

            Assert.IsEmpty(found, "every colour comes from the palette:\n" + string.Join("\n", found));
        }

        /// <summary>
        /// Nothing the game runs makes a value by reflection: the browser build strips managed code it cannot see used,
        /// and a constructor reached only through <c>Activator</c> -- <c>ConditionalWeakTable.GetOrCreateValue</c> makes
        /// its value that way -- is stripped. The shell's RUN TO threw on Enter in the browser for it, and never let the
        /// keyboard go (the Part 11a smoke test); every editor test had passed, since the editor strips nothing.
        /// </summary>
        [Test]
        public void NothingTheGameRuns_MakesAValueByReflection()
        {
            var found = new List<string>();
            foreach (string file in Directory.EnumerateFiles(RepoRoot.Combine("Assets/Scripts"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/Editor/", StringComparison.Ordinal))
                    continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) && Reflective.IsMatch(lines[i]))
                        found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }

            Assert.IsEmpty(found, "the browser build may strip what these make by reflection:\n" + string.Join("\n", found));
        }

        private static readonly Regex Reflective = new Regex(@"\bGetOrCreateValue\s*\(|\bActivator\.|\bType\.GetType\s*\(", RegexOptions.Compiled);

        [Test]
        public void TheColourCheck_SeesALiteral()
        {
            Assert.IsTrue(ColourLiteral.IsMatch("style.color = new Color(1f, 0f, 0f);"));
            Assert.IsTrue(ColourLiteral.IsMatch("style.color = Color.white;"));
            Assert.IsFalse(ColourLiteral.IsMatch("style.color = palette.Text;"));
            Assert.IsFalse(ColourLiteral.IsMatch("Color selection = palette.Accent;"));
        }
    }
}
