using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// What a build ships of the code (5.1, Ahmad: "never ship things/code/libraries that are never used (make sure of that)").
    /// The Windows build shipped every managed library it could be given, 150 of them -- Terrain, Vehicles, VR, System.Data --
    /// with stripping off, while the browser build had stripped at Medium since 5.0. Both are lines in the project's settings
    /// that a drop-down changes and nothing recompiles for, so they are held here; and what stripping must not take, the
    /// <c>link.xml</c> beside Windows' Open dialog keeps (<c>Docs/distribution.md</c>).
    /// </summary>
    public class StrippingTests
    {
        private const string LinkXml = "Assets/Scripts/View/Ui/link.xml";
        private const string Dialog = "Assets/Scripts/View/Ui/WindowsFileDialog.cs";

        private static string Read(string relative) => File.ReadAllText(RepoRoot.Combine(relative));

        /// <summary>The level each build target strips managed code to: Unity's 0 off, 1 Low, 2 Medium, 3 High, 4 Minimal.</summary>
        private static string StrippingOf(string target)
        {
            string settings = Read("ProjectSettings/ProjectSettings.asset");
            Match block = Regex.Match(settings, @"^  managedStrippingLevel:\s*\n((?:    [^\n]+\n)+)", RegexOptions.Multiline);
            Assert.IsTrue(block.Success, "ProjectSettings.asset sets no managed stripping level for any build");
            Match level = Regex.Match(block.Groups[1].Value, @"^    " + target + @":\s*(\d+)\s*$", RegexOptions.Multiline);
            return level.Success ? level.Groups[1].Value : null;
        }

        [TestCase("Standalone", TestName = "TheWindowsBuild_StripsItsCode_AtMedium")]
        [TestCase("WebGL", TestName = "TheBrowserBuild_StripsItsCode_AtMedium")]
        public void ABuild_StripsItsCode_AtMedium(string target) =>
            Assert.AreEqual("2", StrippingOf(target), target + "'s managed stripping level is not Medium (2): it ships code it never runs");

        /// <summary>
        /// Every COM type Windows' Open dialog declares is kept whole: COM calls an interface's methods by their place in it, so a
        /// method stripped as unused would shift every one after it and the dialog would call the wrong one -- nothing would say
        /// so until a student pressed IMPORT. The file names each type as the code declares it, nested in WindowsFileDialog.
        /// </summary>
        [Test]
        public void WindowsOpenDialog_KeepsEveryComTypeWhole()
        {
            string source = Read(Dialog);
            string[] declared = Regex.Matches(source,
                    @"\[ComImport[^\]]*\][^\n]*\n\s*private\s+(?:class|interface)\s+(\w+)", RegexOptions.Multiline)
                .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.Greater(declared.Length, 2, "sanity: the dialog's COM types were found in " + Dialog);

            string link = Read(LinkXml);
            StringAssert.Contains("<assembly fullname=\"BitSorter.View\">", link);
            foreach (string type in declared.Concat(new[] { "FilterSpec" }))
                StringAssert.Contains("<type fullname=\"BitSorter.View.WindowsFileDialog/" + type + "\" preserve=\"all\" />", link,
                    type + " is not kept whole by " + LinkXml);
        }
    }
}
