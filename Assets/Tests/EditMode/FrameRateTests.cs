using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="FrameRate"/>: what each choice does, that SCREEN is the game as it ships, and that
    /// the choice is remembered.
    /// </summary>
    public class FrameRateTests
    {
        private Dictionary<string, int> _before;

        [SetUp]
        public void RedirectThePreferences()
        {
            _before = Preferences.Redirected;
            Preferences.Redirected = new Dictionary<string, int>();
        }

        [TearDown]
        public void PutThemBack() => Preferences.Redirected = _before;

        [Test]
        public void Screen_KeepsVerticalSync_WithNoCap()
        {
            Assert.AreEqual((1, -1), FrameRate.SettingsFor(FrameRateChoice.Screen));
        }

        /// <summary>A cap is ignored while vertical sync is on, so sixty has to turn it off.</summary>
        [Test]
        public void Sixty_TurnsVerticalSyncOff_AndHoldsAtSixty()
        {
            Assert.AreEqual((0, 60), FrameRate.SettingsFor(FrameRateChoice.Sixty));
            Assert.AreEqual(60, FrameRate.Held);
        }

        /// <summary>
        /// SCREEN is exactly what a desktop build does without the setting: the vertical sync of the
        /// quality level it starts on.
        /// </summary>
        /// <remarks>
        /// Read from the project file, the way DisplayRulesTests holds the window size to the player
        /// settings. Changed there alone, SCREEN would quietly become a different frame rate from
        /// the one the game shipped with.
        /// </remarks>
        [Test]
        public void Screen_IsTheVerticalSyncTheDesktopBuildStartsWith()
        {
            string path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "QualitySettings.asset");
            string settings = File.ReadAllText(path);

            Match standalone = Regex.Match(settings, @"^\s*Standalone:\s*(\d+)", RegexOptions.Multiline);
            Assert.IsTrue(standalone.Success, "the quality settings no longer name a desktop default");

            MatchCollection levels = Regex.Matches(settings, @"^\s*vSyncCount:\s*(\d+)", RegexOptions.Multiline);
            int level = int.Parse(standalone.Groups[1].Value);
            Assert.Less(level, levels.Count, "the desktop default names a quality level that is not there");

            Assert.AreEqual(int.Parse(levels[level].Groups[1].Value),
                FrameRate.SettingsFor(FrameRateChoice.Screen).vSyncCount,
                "SCREEN is not the vertical sync the desktop build starts with");
        }

        [Test]
        public void AMachineWithNoChoice_IsOnScreen()
        {
            Assert.AreEqual(FrameRateChoice.Screen, FrameRate.Choice);
        }

        [Test]
        public void TheChoice_IsRemembered()
        {
            FrameRate.Set(FrameRateChoice.Sixty);
            Assert.AreEqual(FrameRateChoice.Sixty, FrameRate.Choice);
            Assert.AreEqual((int)FrameRateChoice.Sixty, Preferences.GetInt(FrameRate.PrefKey, -1));

            FrameRate.Set(FrameRateChoice.Screen);
            Assert.AreEqual(FrameRateChoice.Screen, FrameRate.Choice);
        }

        /// <summary>A value written by some later build reads as the game's own default, not as a cap.</summary>
        [Test]
        public void AStoredValueThisBuildDoesNotKnow_ReadsAsScreen()
        {
            Preferences.SetInt(FrameRate.PrefKey, 7);
            Assert.AreEqual(FrameRateChoice.Screen, FrameRate.Choice);
        }

        /// <summary>
        /// The editor stores the choice and does not apply it: a cap there would hold every Play Mode
        /// test to sixty frames a second.
        /// </summary>
        [Test]
        public void TheEditor_StoresTheChoiceWithoutApplyingIt()
        {
            Assert.IsFalse(FrameRate.Applies);
        }

        [Test]
        public void EachChoice_HasItsOwnCaption()
        {
            StringAssert.Contains("60", SettingsPanel.FrameRateCaption(FrameRateChoice.Sixty));
            StringAssert.Contains("SCREEN", SettingsPanel.FrameRateCaption(FrameRateChoice.Screen));
            StringAssert.StartsWith("FRAME RATE", SettingsPanel.FrameRateCaption(FrameRateChoice.Screen));
        }
    }
}
