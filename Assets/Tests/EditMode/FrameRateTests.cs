using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="FrameRate"/>: what vertical sync and the cap each do, that a machine with no
    /// settings gets the game as it ships, and that both are remembered.
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

        /// <summary>With vertical sync on there is no cap to set: Unity would ignore it.</summary>
        [Test]
        public void WithVSyncOn_ThereIsNoCap_WhateverTheCapSays()
        {
            Assert.AreEqual((1, FrameRate.NoCap), FrameRate.SettingsFor(true, 60));
            Assert.AreEqual((1, FrameRate.NoCap), FrameRate.SettingsFor(true, FrameRate.NoCap));
        }

        [Test]
        public void WithVSyncOff_TheCapIsTheTarget()
        {
            Assert.AreEqual((0, 144), FrameRate.SettingsFor(false, 144));
            Assert.AreEqual((0, FrameRate.NoCap), FrameRate.SettingsFor(false, FrameRate.NoCap));
        }

        /// <summary>
        /// A machine with no settings gets exactly what a desktop build does without them: the
        /// vertical sync of the quality level it starts on.
        /// </summary>
        /// <remarks>
        /// Read from the project file, the way DisplayRulesTests holds the window size to the player
        /// settings. Changed there alone, the default would quietly become a different frame rate
        /// from the one the game shipped with.
        /// </remarks>
        [Test]
        public void AMachineWithNoSettings_GetsTheVerticalSyncTheDesktopBuildStartsWith()
        {
            string path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "QualitySettings.asset");
            string settings = File.ReadAllText(path);

            Match standalone = Regex.Match(settings, @"^\s*Standalone:\s*(\d+)", RegexOptions.Multiline);
            Assert.IsTrue(standalone.Success, "the quality settings no longer name a desktop default");

            MatchCollection levels = Regex.Matches(settings, @"^\s*vSyncCount:\s*(\d+)", RegexOptions.Multiline);
            int level = int.Parse(standalone.Groups[1].Value);
            Assert.Less(level, levels.Count, "the desktop default names a quality level that is not there");

            Assert.IsTrue(FrameRate.VSync, "sanity: a machine with no settings should have vertical sync on");
            Assert.AreEqual(int.Parse(levels[level].Groups[1].Value),
                FrameRate.SettingsFor(FrameRate.VSync, FrameRate.Cap).vSyncCount,
                "a machine with no settings is not on the vertical sync the desktop build starts with");
        }

        /// <summary>Turning vertical sync off for the first time caps at sixty, not at nothing.</summary>
        [Test]
        public void ACapNeverSet_IsTheDefault()
        {
            Assert.AreEqual(FrameRate.DefaultCap, FrameRate.Cap);
            Assert.AreNotEqual(FrameRate.NoCap, FrameRate.DefaultCap);
            Assert.AreEqual(FrameRate.DefaultCap, FrameRate.CapAt(FrameRate.CapStop));
        }

        /// <summary>The slider's stops rise, every one is a real rate, and the last is none.</summary>
        [Test]
        public void TheStops_RiseAndEndOnNoCap()
        {
            int last = FrameRate.Stops - 1;
            Assert.AreEqual(FrameRate.NoCap, FrameRate.CapAt(last), "the last stop is not no cap");

            for (int stop = 0; stop < last; stop++)
            {
                Assert.Greater(FrameRate.CapAt(stop), 0, $"stop {stop} is not a rate");

                if (stop > 0)
                    Assert.Greater(FrameRate.CapAt(stop), FrameRate.CapAt(stop - 1), $"stop {stop} does not rise");
            }
        }

        [Test]
        public void BothSettings_AreRemembered()
        {
            FrameRate.SetVSync(false);
            Assert.IsFalse(FrameRate.VSync);
            Assert.AreEqual(0, Preferences.GetInt(FrameRate.VSyncKey, -1));

            int stop = FrameRate.Stops - 2;
            FrameRate.SetCapStop(stop, keep: true);
            Assert.AreEqual(stop, FrameRate.CapStop);
            Assert.AreEqual(FrameRate.CapAt(stop), Preferences.GetInt(FrameRate.CapKey, 0),
                "the cap was not stored as the rate itself");

            FrameRate.SetVSync(true);
            Assert.IsTrue(FrameRate.VSync);
            Assert.AreEqual(stop, FrameRate.CapStop, "turning vertical sync on forgot the cap");
        }

        /// <summary>A stop past either end of the slider is the end it went past.</summary>
        [Test]
        public void AStopOffTheSlider_IsItsNearestEnd()
        {
            FrameRate.SetCapStop(99, keep: true);
            Assert.AreEqual(FrameRate.NoCap, FrameRate.Cap);

            FrameRate.SetCapStop(-5, keep: true);
            Assert.AreEqual(FrameRate.CapAt(0), FrameRate.Cap);
        }

        /// <summary>
        /// A cap written by some later build reads as the default -- never as no cap, the one reading
        /// that could cost the player something.
        /// </summary>
        [Test]
        public void AStoredCapThisBuildDoesNotOffer_ReadsAsTheDefault()
        {
            Preferences.SetInt(FrameRate.CapKey, 7);
            Assert.AreEqual(FrameRate.DefaultCap, FrameRate.Cap);
        }

        /// <summary>
        /// The editor stores the settings and does not apply them: a cap there would hold every Play
        /// Mode test to it.
        /// </summary>
        [Test]
        public void TheEditor_StoresTheSettingsWithoutApplyingThem()
        {
            Assert.IsFalse(FrameRate.Applies);
        }

        [Test]
        public void TheCounter_IsOffUntilAskedFor_AndRemembered()
        {
            Assert.IsFalse(FrameRate.ShowsCounter, "a machine that never asked shows a counter");

            FrameRate.SetCounter(true);
            Assert.IsTrue(FrameRate.ShowsCounter);
            Assert.AreEqual(1, Preferences.GetInt(FrameRate.CounterKey, 0));

            FrameRate.SetCounter(false);
            Assert.IsFalse(FrameRate.ShowsCounter);
        }

        [Test]
        public void TheCountersRate_IsFramesOverSeconds_Rounded()
        {
            Assert.AreEqual(60, FrameRateCounter.Rate(30, 0.5f));
            Assert.AreEqual(165, FrameRateCounter.Rate(83, 0.503f));
            Assert.AreEqual(0, FrameRateCounter.Rate(0, 0.5f));
            Assert.AreEqual(0, FrameRateCounter.Rate(10, 0f), "no time passed is no rate, not a division by zero");
        }

        /// <summary>The counter writes its digits itself, to hand TextMeshPro no new string a frame.</summary>
        [TestCase(0, "0")]
        [TestCase(7, "7")]
        [TestCase(60, "60")]
        [TestCase(144, "144")]
        [TestCase(1000, "1000")]
        [TestCase(2147483647, "2147483647")]
        [TestCase(-3, "0")]
        public void TheCountersDigits_AreTheNumber(int value, string expected)
        {
            var into = new char[10];
            int length = FrameRateCounter.Digits(value, into);
            Assert.AreEqual(expected, new string(into, 0, length));
        }

        [Test]
        public void EachStop_HasItsOwnCaption()
        {
            var seen = new HashSet<string>();

            for (int stop = 0; stop < FrameRate.Stops; stop++)
            {
                string caption = SettingsPanel.CapCaption(stop);
                Assert.IsTrue(seen.Add(caption), $"two stops both read '{caption}'");

                int cap = FrameRate.CapAt(stop);
                if (cap != FrameRate.NoCap)
                    StringAssert.StartsWith(cap.ToString(), caption);
            }

            Assert.AreEqual("NONE", SettingsPanel.CapCaption(FrameRate.Stops - 1));
            StringAssert.StartsWith("VSYNC", SettingsPanel.VSyncCaption(true));
            Assert.AreNotEqual(SettingsPanel.VSyncCaption(true), SettingsPanel.VSyncCaption(false));
            StringAssert.StartsWith("FPS COUNTER", SettingsPanel.CounterCaption(true));
            Assert.AreNotEqual(SettingsPanel.CounterCaption(true), SettingsPanel.CounterCaption(false));
        }
    }
}
