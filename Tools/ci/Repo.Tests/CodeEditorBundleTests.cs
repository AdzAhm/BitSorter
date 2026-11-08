using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// The browser build's code editor (Part 12f): the source in <c>Tools/codemirror/src</c>, and the bundle made of it that
    /// the game ships, <c>Assets/Plugins/WebGL/codemirror.jspre</c>. Read as text here, since nothing but a browser runs the
    /// editor itself; <c>Tools/codemirror/test</c> holds what Node can run of it.
    /// </summary>
    public class CodeEditorBundleTests
    {
        /// <summary>The editor's <c>create</c> giving up at once on an editor made already, in the source and minified.</summary>
        private static readonly Regex MadeOnceOnly = new Regex(@"create\(\s*\w+\s*\)\s*\{\s*if\s*\(\s*\w+\.view\s*\)\s*return\s*;", RegexOptions.Compiled);

        /// <summary>
        /// The editor made again -- as every scene that shows code makes it, a change of style among them -- takes the new
        /// look's colours: STYLE loads the game again, never the page, so the page's editor outlives it, and <c>create</c>
        /// gave up at once on an editor made already. The code kept the colours of the look the page first loaded with -- a
        /// dark box on Schematic Light's paper -- until the tab was loaded again (browser-3).
        /// </summary>
        [Test]
        public void TheEditorMadeAgain_TakesTheNewLooksColours_InTheSourceAndTheBundleShipped()
        {
            string source = File.ReadAllText(RepoRoot.Combine("Tools/codemirror/src/bitsorter-code.js"));
            string bundle = File.ReadAllText(RepoRoot.Combine("Assets/Plugins/WebGL/codemirror.jspre"));

            StringAssert.Contains("create(coloursJson)", source, "sanity: the source's create");
            Assert.IsFalse(MadeOnceOnly.IsMatch(source), "the source's create gives up on an editor made already, in the first look's colours");
            Assert.IsFalse(MadeOnceOnly.IsMatch(bundle),
                "the bundle shipped gives up on an editor made already: made from an older source? Tools/codemirror/build.ps1 makes it again");
        }

        /// <summary>The search finds the shape it looks for: an assertion of absence needs one that shows the thing is possible.</summary>
        [Test]
        public void TheSearch_FindsAnEditorMadeOnceOnly_WhereThereIsOne()
        {
            Assert.IsTrue(MadeOnceOnly.IsMatch("create(coloursJson) {\n    if (state.view)\n      return;"));
            Assert.IsTrue(MadeOnceOnly.IsMatch("create(n){if(O.view)return;O.colours=JSON.parse(n)"));
            Assert.IsFalse(MadeOnceOnly.IsMatch("create(n){if(O.view){restyle(n);return}"));
        }
    }
}
