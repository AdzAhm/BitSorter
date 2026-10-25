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
