using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// How a browser tab hears of a save another writes (browser-1; <c>Assets/Plugins/WebGL/BitSorterSave.jslib</c>). Read as
    /// text here, since nothing but a browser runs the page's own code.
    /// </summary>
    /// <remarks>
    /// Every BitSorter build on one origin -- the release and a candidate beside it on Pages -- keeps its files in the one
    /// IndexedDB mount Unity makes, <c>/idbfs</c>, each under a folder of its own, and every tab writes the whole mount back
    /// whenever a file changes. So every tab hears of every save, and takes IndexedDB's files into memory, or it puts an older
    /// copy of another build's files back over the newer; but only a save of its own build's file -- the same path -- is
    /// another window's save to hold its own for.
    /// </remarks>
    public class BrowserSaveTests
    {
        private static string Page() => File.ReadAllText(RepoRoot.Combine("Assets/Plugins/WebGL/BitSorterSave.jslib"));

        /// <summary>A save told to the other tabs with its path beside its stamp.</summary>
        private static readonly Regex ToldWithItsPath =
            new Regex(@"setItem\(\s*'BitSorter\.save'\s*,\s*JSON\.stringify\(\s*\{[^}]*\bpath\s*:[^}]*\bstamp\s*:", RegexOptions.Compiled);

        /// <summary>A tab held only for a save of the path it read.</summary>
        private static readonly Regex HeldForItsOwnPathOnly = new Regex(@"\.path\s*===\s*save\.path\b", RegexOptions.Compiled);

        /// <summary>
        /// A save written by a tab of another build on the same origin holds no tab of this one: the stamp was told under one
        /// key for the origin, and a tab that heard any it did not know was told another BitSorter window had saved, its own
        /// saves refused until the page was loaded again (the browser-1 fix's review). The path goes with the stamp, and only
        /// a save of the tab's own path holds it.
        /// </summary>
        [Test]
        public void AnotherBuildsSave_HoldsNoTabOfThisOne()
        {
            string page = Page();

            StringAssert.Contains("BitSorterSaveWritten", page, "sanity: the page's save");
            Assert.IsTrue(ToldWithItsPath.IsMatch(page), "a save is told to the other tabs without its path, so no tab can tell its own build's");
            Assert.IsTrue(HeldForItsOwnPathOnly.IsMatch(page), "a tab is held for a save of another build's file");

            string glue = File.ReadAllText(RepoRoot.Combine("Assets/Scripts/View/Level/BrowserSave.cs"));
            StringAssert.Contains("BitSorterSaveRead(string stamp, string path)", glue, "the game gives the page the save's path as it reads");
            StringAssert.Contains("BitSorterSaveWritten(string stamp, string path)", glue, "and as it writes");
        }

        /// <summary>The searches find the shapes they look for, and not the ones before the fix.</summary>
        [Test]
        public void TheSearches_FindAPathToldAndHeld_WhereThereIsOne()
        {
            Assert.IsTrue(ToldWithItsPath.IsMatch("window.localStorage.setItem('BitSorter.save', JSON.stringify({ path: save.path, stamp: stamp }));"));
            Assert.IsFalse(ToldWithItsPath.IsMatch("window.localStorage.setItem('BitSorter.save', stamp);"));
            Assert.IsTrue(HeldForItsOwnPathOnly.IsMatch("if (told.path === save.path && told.stamp !== save.own)"));
            Assert.IsFalse(HeldForItsOwnPathOnly.IsMatch("if (e.key !== 'BitSorter.save' || e.newValue === save.own)"));
        }
    }
}
