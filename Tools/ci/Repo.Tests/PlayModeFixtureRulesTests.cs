using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// Every Play Mode fixture with a simulated keyboard and mouse runs its input in the background, as BitSorter's player
    /// does. Unity tells the event system when the editor goes behind another window (<c>OnApplicationFocus</c>), and the
    /// Input System's UI module then drops every click and key that follows unless input runs in the background -- which
    /// <c>InputTestFixture</c>'s test runtime does not, the player's own setting notwithstanding. On 2026-11-11 a window put in
    /// front of the editor mid-run dropped a test's click into RUN TO, and the test went red at an Escape far past it.
    /// </summary>
    public class PlayModeFixtureRulesTests
    {
        private static readonly Regex InputFixture = new Regex(@":\s*InputTestFixture\b", RegexOptions.Compiled);

        [Test]
        public void EveryInputFixture_RunsItsInputInTheBackground()
        {
            var found = new List<string>();
            foreach (string file in Directory.EnumerateFiles(RepoRoot.Combine("Assets/Tests"), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (InputFixture.IsMatch(text) && !text.Contains("InputSystem.runInBackground = true;"))
                    found.Add(Path.GetRelativePath(RepoRoot.Path, file));
            }

            Assert.IsEmpty(found, "set InputSystem.runInBackground = true after base.Setup(), or a window put in front of the editor "
                + "mid-run drops every click after it:\n" + string.Join("\n", found));
        }

        /// <summary>What the fixtures follow: the player runs in the background, so its UI takes a click behind another window.</summary>
        [Test]
        public void ThePlayer_RunsInTheBackground()
        {
            string settings = File.ReadAllText(RepoRoot.Combine("ProjectSettings/ProjectSettings.asset"));
            StringAssert.Contains("runInBackground: 1", settings, "Run In Background, which every input fixture follows");
        }
    }
}
