using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Everything the player reads, checked against the interface it describes.
    /// </summary>
    /// <remarks>
    /// Player-facing text lives in five places -- the levels' `goal` and `hint`, `HintRules`,
    /// `TutorialScript`, `ControlsReference`, and labels built into the panels -- and nothing
    /// connected any of them to the buttons they name. So when the help badge changed from "!" to
    /// "?", `four-corners` went on telling players to press "!" and shipped that way. Nothing
    /// recompiled, no test turned red, and it was found by someone looking at the screen.
    ///
    /// This is the seam that was missing. It cannot check that the writing is any good; it can check
    /// that the writing still describes the game.
    /// </remarks>
    public class PlayerTextTests
    {
        /// <summary>The board the levels are parsed against. Same as <c>CurriculumTests</c>.</summary>
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        /// <summary>One string a player can read, and where it came from.</summary>
        private readonly struct Line
        {
            public readonly string Source;
            public readonly string Text;

            public Line(string source, string text)
            {
                Source = source;
                Text = text;
            }

            public override string ToString() => Source + ": \"" + Text + "\"";
        }

        /// <summary>
        /// Every string the game puts in front of a player, from every place they are kept.
        /// </summary>
        /// <remarks>
        /// Panel labels are the one category deliberately left out: they are literals inside the
        /// builders rather than data, and gathering them would mean either a registry nothing else
        /// needs or reflection over private fields. They are also the least likely to drift, because
        /// changing a button's label and its behaviour is one edit in one file.
        /// </remarks>
        private static List<Line> EverythingAPlayerReads()
        {
            var lines = new List<Line>();

            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, Board);

                Assert.IsTrue(parsed.IsValid, asset.name + ": " + parsed.Error);

                lines.Add(new Line(asset.name + ".goal", parsed.Level.Goal));
                lines.Add(new Line(asset.name + ".hint", parsed.Level.Hint));
            }

            foreach (string id in HintRules.All)
                lines.Add(new Line("HintRules." + id, HintRules.TextFor(id)));

            foreach (TutorialStep step in TutorialScript.Steps)
                lines.Add(new Line("TutorialScript." + step.Id, step.Text));

            // The lines said in place of a step, which are on the same strip.
            lines.Add(new Line("TutorialScript.RecoveryText", TutorialScript.RecoveryText(new BoardFacts(
                TutorialLevel.Part, true, true, true, false, false, runFailed: true))));
            lines.Add(new Line("TutorialScript.CorrectionText", TutorialScript.CorrectionText(new BoardFacts(
                TutorialLevel.Part, false, false, false, false, false, partOnCell: TutorialLevel.Decoy))));
            lines.Add(new Line("TutorialScript.CorrectionText elsewhere", TutorialScript.CorrectionText(new BoardFacts(
                TutorialLevel.Decoy, false, false, false, false, false, partElsewhere: true))));
            lines.Add(new Line("TutorialScript.CorrectionText both", TutorialScript.CorrectionText(new BoardFacts(
                TutorialLevel.Decoy, false, false, false, false, false,
                partOnCell: TutorialLevel.Decoy, partElsewhere: true))));

            foreach (ControlEntry control in ControlsReference.All)
                lines.Add(new Line("ControlsReference", control.Text));

            // Settings, whose words are constants rather than literals inside its builder.
            lines.Add(new Line("SettingsPanel.AudioText", SettingsPanel.AudioText));
            lines.Add(new Line("SettingsPanel.DisplayText", SettingsPanel.DisplayText));
            lines.Add(new Line("SettingsPanel.PrivacyText", SettingsPanel.PrivacyText));
            lines.Add(new Line("SettingsPanel.ProgressText", SettingsPanel.ProgressText));
            lines.Add(new Line("SettingsPanel.Question", SettingsPanel.Question));
            lines.Add(new Line("SettingsPanel.Done", SettingsPanel.Done));

            // What Q and E say where they cannot go.
            lines.Add(new Line("SimulationInput.BeforeTheFirst", SimulationInput.BeforeTheFirst));
            lines.Add(new Line("SimulationInput.NotALevel", SimulationInput.NotALevel));

            lines.Add(new Line("CreditsPanel.HelpText", CreditsPanel.HelpText));

            // The credits. Gaps are the roll's own spacing and carry no text on purpose.
            foreach (Credits.Line line in Credits.Roll(MainMenu.Tagline, "v0.0.0"))
            {
                if (line.Kind != Credits.Kind.Gap)
                    lines.Add(new Line("Credits." + line.Kind, line.Text));
            }

            return lines;
        }

        /// <summary>
        /// The corpus is big enough to be the real one.
        /// </summary>
        /// <remarks>
        /// Every other test here passes trivially if the gatherer returns nothing, which is exactly
        /// how a test can look green while proving nothing. This is the one that says it looked.
        /// </remarks>
        [Test]
        public void ThereIsActuallySomethingToCheck()
        {
            List<Line> lines = EverythingAPlayerReads();

            Assert.Greater(lines.Count, 30,
                "the text gatherer found almost nothing, so every check below is vacuous");

            // Nine levels contribute two lines each, and the other three sources are all non-empty.
            Assert.GreaterOrEqual(lines.FindAll(l => l.Source.EndsWith(".goal")).Count, 9);
            Assert.GreaterOrEqual(lines.FindAll(l => l.Source.StartsWith("HintRules")).Count, 3);
            Assert.GreaterOrEqual(lines.FindAll(l => l.Source.StartsWith("TutorialScript")).Count, 6);
        }

        [Test]
        public void NothingAPlayerReadsIsBlank()
        {
            foreach (Line line in EverythingAPlayerReads())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(line.Text),
                    line.Source + " is empty, and will render as a gap the player cannot explain");
            }
        }

        // -----------------------------------------------------------------
        // Buttons the text tells the player to press
        // -----------------------------------------------------------------

        /// <summary>"press X", and "press X or Y", however it is capitalised.</summary>
        private static readonly Regex Press =
            new Regex(@"\bpress\s+(\S+?)[.,]?(?:\s+or\s+(\S+?)[.,]?)?(?:\s|$)",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static IEnumerable<string> ButtonsNamedIn(string text)
        {
            foreach (Match match in Press.Matches(text))
            {
                for (int group = 1; group <= 2; group++)
                {
                    string token = match.Groups[group].Value;

                    if (!string.IsNullOrEmpty(token))
                        yield return token;
                }
            }
        }

        /// <summary>
        /// A symbol the player is told to press must be one the interface actually shows.
        /// </summary>
        /// <remarks>
        /// Restricted to tokens that are a single non-alphanumeric character, which is precisely the
        /// class that broke. Letter keys are skipped on purpose: "press R" is checked by nothing
        /// here, and asserting against a list of key names would mean restating the bindings a third
        /// time -- the drift this test exists to prevent.
        ///
        /// The badge is the only such symbol in the game. If a second one ever appears, it belongs in
        /// the set below rather than in a second copy of this test.
        /// </remarks>
        [Test]
        public void EverySymbolThePlayerIsToldToPress_IsOneTheInterfaceShows()
        {
            var shown = new HashSet<string> { HelpPanel.BadgeGlyph };
            var wrong = new List<string>();

            foreach (Line line in EverythingAPlayerReads())
            {
                foreach (string button in ButtonsNamedIn(line.Text))
                {
                    // Letter and number keys are somebody else's problem; symbols are this one's.
                    if (button.Length != 1 || char.IsLetterOrDigit(button[0]))
                        continue;

                    if (!shown.Contains(button))
                        wrong.Add(line.Source + " says press \"" + button + "\"");
                }
            }

            CollectionAssert.IsEmpty(wrong,
                "text naming a button the interface does not have:\n  " + string.Join("\n  ", wrong)
                + "\nthe badge currently shows \"" + HelpPanel.BadgeGlyph + "\"");
        }

        /// <summary>
        /// The regex finds the thing it is supposed to find.
        /// </summary>
        /// <remarks>
        /// The test above is an assertion about absence, and an assertion about absence is worthless
        /// unless something proves the search works -- the lesson from a mute test that passed for
        /// ninety frames because nothing could have made a sound anyway. These are the exact strings
        /// that shipped, before and after the fix.
        /// </remarks>
        [Test]
        public void TheSearchFindsAButtonWhenThereIsOne()
        {
            CollectionAssert.AreEqual(new[] { "?", "H" },
                new List<string>(ButtonsNamedIn("Match the truth table. Press ? or H to see it.")));

            CollectionAssert.AreEqual(new[] { "!", "H" },
                new List<string>(ButtonsNamedIn("Match the truth table. Press ! or H to see it.")),
                "this is the text that shipped wrong; the search must still catch it");

            CollectionAssert.IsEmpty(
                new List<string>(ButtonsNamedIn("Wire the gate to the bin.")));
        }

    }
}
