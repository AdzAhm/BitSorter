using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The help panel's hint box, measured against the hints it has to hold.
    /// </summary>
    /// <remarks>
    /// The banner's goal has had this guard since a goal overflowed it in both directions and
    /// printed over the level title. The hint is the same shape of problem with none of the
    /// protection: its box was sized by counting the lines the longest hint needed at the time and
    /// writing the total down, so the first hint a line longer than that prints past the bottom
    /// edge of the panel with nothing to say which level did it.
    ///
    /// It matters more here than almost anywhere. The hint is the one line a player went out of
    /// their way to ask for.
    /// </remarks>
    public class HelpPanelTests
    {
        [Test]
        public void EveryLevelsHint_FitsTheHelpPanelsHintBox()
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, asset.name);

                float needed = UiTheme.TextHeight(
                    parsed.Level.Hint,
                    HelpPanel.HintType,
                    HelpPanel.HintWidth,
                    HelpPanel.HintLineSpacing);

                Assert.LessOrEqual(needed, HelpPanel.HintHeight,
                    $"{asset.name}'s hint wraps to {needed:F0}px and the panel gives the hint " +
                    $"{HelpPanel.HintHeight}px, so it prints past the bottom of the panel");
            }
        }

        /// <summary>
        /// The canvas height a browser tab 1920 by 800 gives: the canvas scales halfway between the
        /// window's width and its height, so a wide, short window has less height than 1080.
        /// </summary>
        private const float ShortCanvas = 929f;

        private static IEnumerable<KeyValuePair<string, LevelDefinition>> EveryLevel()
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, asset.name);
                yield return new KeyValuePair<string, LevelDefinition>(asset.name, parsed.Level);
            }

            yield return new KeyValuePair<string, LevelDefinition>(
                "free play", SandboxLevel.Build(new SandboxConfig(), LevelTestFixtures.Board));
        }

        /// <summary>
        /// At the size the interface is laid out for, the panel is never shrunk: a level that needs
        /// shrinking there is a level to shorten, not one to squeeze.
        /// </summary>
        [Test]
        public void EveryLevelsPanel_FitsAboveTheRunButtons_AtFullSize()
        {
            float room = HelpPanel.Room(UiTheme.ReferenceResolution.y);

            foreach (KeyValuePair<string, LevelDefinition> level in EveryLevel())
            {
                Assert.LessOrEqual(HelpPanel.HeightFor(level.Value), room,
                    $"{level.Key}'s help panel is {HelpPanel.HeightFor(level.Value):F0}px tall and " +
                    $"the 1080 canvas leaves {room:F0}px between the badge and the run buttons");
            }
        }

        /// <summary>
        /// In a short window the panel shrinks to fit, and never so far that the table stops being
        /// something to read. Sixteen-row tables are the ones this is about.
        /// </summary>
        [Test]
        public void EveryLevelsPanel_StaysReadable_InAShortWindow()
        {
            float room = HelpPanel.Room(ShortCanvas);

            foreach (KeyValuePair<string, LevelDefinition> level in EveryLevel())
            {
                float scale = SettingsPanel.FitScale(room, HelpPanel.HeightFor(level.Value));

                Assert.GreaterOrEqual(scale, 0.9f,
                    $"{level.Key}'s help panel would be drawn at {scale:P0} in a 1920 x 800 window");
            }
        }

        [Test]
        public void EveryLevelsTabs_FitInsideItsPanel()
        {
            int tabbed = 0;

            foreach (KeyValuePair<string, LevelDefinition> level in EveryLevel())
            {
                float row = HelpPanel.TabRowWidth(level.Value);

                if (row <= 0f)
                    continue;

                tabbed++;
                float inside = HelpPanel.WidthFor(level.Value) - 2f * HelpPanel.ContentPadding;

                Assert.LessOrEqual(row, inside,
                    $"{level.Key}'s tabs are {row:F0}px across and the panel has {inside:F0}px inside it");
            }

            Assert.Greater(tabbed, 0, "sanity: no level has tabs");
        }

        [Test]
        public void OnlyALevelWithAMap_HasTabs()
        {
            Assert.AreEqual(0f, HelpPanel.TabRowWidth(LevelLoader.Load("route-the-bit", LevelTestFixtures.Board).Level));
            Assert.AreEqual(0f, HelpPanel.TabRowWidth(LevelLoader.Load("flip-on-one", LevelTestFixtures.Board).Level));
            Assert.Greater(HelpPanel.TabRowWidth(LevelLoader.Load("half-adder", LevelTestFixtures.Board).Level), 0f);
        }

        /// <summary>
        /// Free play's hint is built in code rather than authored, so the sweep above never sees it.
        /// </summary>
        [Test]
        public void FreePlaysHint_FitsTheSameBox()
        {
            LevelDefinition level = SandboxLevel.Build(new SandboxConfig(), LevelTestFixtures.Board);

            float needed = UiTheme.TextHeight(
                level.Hint, HelpPanel.HintType, HelpPanel.HintWidth, HelpPanel.HintLineSpacing);

            Assert.LessOrEqual(needed, HelpPanel.HintHeight,
                $"free play's hint wraps to {needed:F0}px against {HelpPanel.HintHeight}px");
        }
    }
}
