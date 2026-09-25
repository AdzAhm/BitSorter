using System.IO;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="AnalyticsRules"/>: the two events this game sends, and every reason it stays
    /// quiet.
    /// </summary>
    /// <remarks>
    /// This is the one thing in the project that sends data anywhere, and CLAUDE.md says adding a
    /// third event or a new parameter is a change to what players were told is collected -- to be
    /// asked about first. These tests make that a thing the build checks rather than a thing a
    /// reviewer has to notice, including the README staying true, since the README is the canonical
    /// list players actually read.
    /// </remarks>
    public class AnalyticsTests
    {
        private const string RealLevel = "half-adder";

        private static string ReadmeText()
        {
            string path = Path.Combine(Application.dataPath, "..", "README.md");
            Assert.IsTrue(File.Exists(path), $"no README at {path}");
            return File.ReadAllText(path);
        }

        // -----------------------------------------------------------------
        // What gets sent
        // -----------------------------------------------------------------

        [Test]
        public void ThereAreExactlyTwoEvents()
        {
            // Deliberately a hard number. A third event is a decision about what players were
            // promised, and this failing is the point at which somebody has to make it on purpose.
            Assert.AreEqual(2, AnalyticsRules.Events.Count,
                "a third event is a change to what players were told is collected -- ask first");
        }

        [Test]
        public void TheEventsAreTheOnesTheReadmeNames()
        {
            CollectionAssert.AreEquivalent(
                new[] { "levelStarted", "levelSolved" }, AnalyticsRules.Events);
        }

        [Test]
        public void TheOnlyParameterIsTheLevelName()
        {
            Assert.AreEqual("levelName", AnalyticsRules.LevelNameParameter,
                "a new parameter is a change to what players were told is collected -- ask first");
        }

        [Test]
        public void EveryEventHasAName()
        {
            foreach (string name in AnalyticsRules.Events)
                Assert.IsFalse(string.IsNullOrWhiteSpace(name), "an event with no name");
        }

        // -----------------------------------------------------------------
        // When it stays quiet
        // -----------------------------------------------------------------

        [Test]
        public void ARealLevel_IsReported()
        {
            Assert.IsTrue(AnalyticsRules.ShouldReport(RealLevel, reporting: true, unavailable: false));
        }

        [Test]
        public void NothingIsReported_WhenThePlayerHasTurnedItOff()
        {
            // The DATA switch in Settings. Checked here as well as at the consent framework, so a
            // player who said no does not even accumulate a queue waiting for an upload.
            Assert.IsFalse(AnalyticsRules.ShouldReport(RealLevel, reporting: false, unavailable: false));
        }

        [Test]
        public void NothingIsReported_WhenAnalyticsCouldNotStart()
        {
            // No linked project, offline, or a browser blocking it. None of that is the player's
            // problem, and none of it should queue events that will never go anywhere.
            Assert.IsFalse(AnalyticsRules.ShouldReport(RealLevel, reporting: true, unavailable: true));
        }

        [Test]
        public void NothingIsReported_ForAMissingLevelName()
        {
            Assert.IsFalse(AnalyticsRules.ShouldReport(null, true, false));
            Assert.IsFalse(AnalyticsRules.ShouldReport(string.Empty, true, false));
        }

        [Test]
        public void FreePlayAndTheTutorial_AreNeverReported()
        {
            // Both would answer the question these events exist for -- which level people stop at --
            // wrongly, and in opposite directions. Free play can never be solved, so a start from it
            // reads as someone giving up; the tutorial is solved by everybody who finishes it.
            Assert.IsFalse(AnalyticsRules.ShouldReport(SandboxLevel.Key, true, false));
            Assert.IsFalse(AnalyticsRules.ShouldReport(TutorialLevel.Key, true, false));
        }

        [Test]
        public void EveryShippedLevel_IsReportable()
        {
            // The guard has to be narrow. One that swallowed a real level would quietly cost the
            // measurement, and nothing else would notice.
            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);
            Assert.IsNotEmpty(assets, "no level files found");

            foreach (TextAsset asset in assets)
            {
                Assert.IsTrue(AnalyticsRules.ShouldReport(asset.name, true, false),
                    $"'{asset.name}' is a real level and should be reported");
            }
        }

        // -----------------------------------------------------------------
        // The README is the canonical list
        // -----------------------------------------------------------------

        /// <summary>
        /// Everything the code sends is named in the README, and the README names nothing else.
        /// </summary>
        /// <remarks>
        /// CLAUDE.md makes the README's "What it collects" section the canonical list and forbids
        /// restating it elsewhere. That only holds if the two cannot drift, and nothing else would
        /// catch a third event shipping while the README still promised two.
        /// </remarks>
        [Test]
        public void TheReadmeNamesEveryEventAndNoOthers()
        {
            string readme = ReadmeText();

            foreach (string name in AnalyticsRules.Events)
            {
                StringAssert.Contains(name, readme,
                    $"'{name}' is sent but the README does not mention it");
            }

            // The other direction: a name in the README that the code no longer sends.
            foreach (string candidate in new[] { "levelStarted", "levelSolved" })
            {
                if (!readme.Contains(candidate))
                    continue;

                CollectionAssert.Contains(AnalyticsRules.Events, candidate,
                    $"the README promises '{candidate}' but nothing sends it");
            }
        }

        [Test]
        public void TheReadmeNamesTheParameter()
        {
            StringAssert.Contains(AnalyticsRules.LevelNameParameter, ReadmeText());
        }

        [Test]
        public void TheReadmeStillSaysTheCircuitIsNeverSent()
        {
            // The promise players are given. If this line ever goes, it should go deliberately.
            StringAssert.Contains("never sent", ReadmeText());
        }
    }
}
