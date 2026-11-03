using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// The version is stated once, as <c>bundleVersion</c> in the player settings, and everything that shows it reads
    /// it: the start page's version line and the credits (<c>Application.version</c>) and the browser template
    /// (<c>PRODUCT_VERSION</c>). The two documents that write it out are held to it here, since nothing recompiles when
    /// a README goes stale.
    /// </summary>
    public class VersionTests
    {
        private static string BundleVersion()
        {
            string settings = File.ReadAllText(RepoRoot.Combine("ProjectSettings/ProjectSettings.asset"));
            Match match = Regex.Match(settings, @"^\s*bundleVersion:\s*(\S+)\s*$", RegexOptions.Multiline);

            Assert.IsTrue(match.Success, "ProjectSettings.asset has no bundleVersion");
            return match.Groups[1].Value;
        }

        [Test]
        public void TheBundleVersion_IsAThreePartVersion()
        {
            StringAssert.IsMatch(@"^\d+\.\d+\.\d+$", BundleVersion());
        }

        [Test]
        public void TheReadme_SaysTheVersionTheBuildCarries()
        {
            StringAssert.Contains("Version " + BundleVersion() + ".", File.ReadAllText(RepoRoot.Combine("README.md")),
                "the README's version line is not the player settings' bundleVersion");
        }

        [Test]
        public void TheDistributionNotes_NameTheVersionTheBuildCarries()
        {
            StringAssert.Contains("The current version is **" + BundleVersion() + "**",
                File.ReadAllText(RepoRoot.Combine("Docs/distribution.md")),
                "Docs/distribution.md does not name the player settings' bundleVersion as the current version");
        }

        /// <summary>
        /// Paired with the tests above, which show the documents do say the version: the places that show it to a person
        /// read the build's, and write no version of their own.
        /// </summary>
        [TestCase("Assets/Scripts/View/Ui/Credits.cs")]
        [TestCase("Assets/Scripts/View/Workbench/WorkbenchPanel.Home.cs")]
        public void WhatShowsTheVersion_ReadsTheBuilds(string file)
        {
            string code = Regex.Replace(File.ReadAllText(RepoRoot.Combine(file)), "//[^\n]*", "");

            StringAssert.Contains("Application.version", code);
            Assert.IsFalse(Regex.IsMatch(code, "\"v?\\d+\\.\\d+\\.\\d+\""), file + " writes a version out");
        }
    }
}
