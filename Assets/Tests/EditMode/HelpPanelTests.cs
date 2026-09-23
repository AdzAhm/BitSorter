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
