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
    /// Player-facing text lives in many places -- the exercises' `goal` and `hint`, Settings, ABOUT BITSORTER, the
    /// credits, and labels built into the panels -- and nothing connected any of them to the buttons they name. So when
    /// the 4.x game's help badge changed from "!" to "?", a level went on telling players to press "!" and shipped that
    /// way. Nothing recompiled, no test turned red, and it was found by someone looking at the screen. (The 4.x levels,
    /// hints, tutorial and controls were gathered here too, until Part 16a deleted the game; the simulator's own hints and
    /// tutorial are held by <c>SimulatorHintTests</c>.)
    ///
    /// This is the seam that was missing. It cannot check that the writing is any good; it can check
    /// that the writing still describes the game.
    /// </remarks>
    public class PlayerTextTests
    {
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

            foreach (BitSorter.Board.Exercise exercise in ExerciseCatalog.Run)
            {
                lines.Add(new Line(exercise.Id + ".goal", exercise.Goal));
                lines.Add(new Line(exercise.Id + ".hint", exercise.Hint));
            }

            // Settings, whose words are constants rather than literals inside its builder.
            lines.Add(new Line("SettingsPanel.DisplayText", SettingsPanel.DisplayText));
            lines.Add(new Line("SettingsPanel.BrowserDisplayText", SettingsPanel.BrowserDisplayText));
            lines.Add(new Line("SettingsPanel.StyleText", SettingsPanel.StyleText));
            lines.Add(new Line("SettingsPanel.PrivacyText", SettingsPanel.PrivacyText));
            lines.Add(new Line("SettingsPanel.ProgressText", SettingsPanel.ProgressText));
            lines.Add(new Line("SettingsPanel.Question", SettingsPanel.Question));
            lines.Add(new Line("SettingsPanel.Done", SettingsPanel.Done));

            // The start page's words and ABOUT BITSORTER (Part 15h), which names the workbench's tabs and buttons to say
            // what it does.
            lines.Add(new Line("WorkbenchPanel.Tagline", WorkbenchPanel.Tagline));
            lines.Add(new Line("WorkbenchPanel.StartSays", WorkbenchPanel.StartSays));
            lines.Add(new Line("WorkbenchPanel.AboutTagline", WorkbenchPanel.AboutTagline));
            lines.Add(new Line("WorkbenchPanel.AboutIntro", WorkbenchPanel.AboutIntro));
            foreach ((string heading, string text) in WorkbenchPanel.AboutAbilities)
                lines.Add(new Line("WorkbenchPanel.AboutAbilities " + heading, text));
            foreach ((string heading, string text) in WorkbenchPanel.AboutStory)
                lines.Add(new Line("WorkbenchPanel.AboutStory " + heading, text));

            lines.Add(new Line("CreditsPanel.HelpText", CreditsPanel.HelpText));

            // The credits. Gaps are the roll's own spacing and carry no text on purpose.
            foreach (Credits.Line line in Credits.Roll(Credits.Tagline, "v0.0.0"))
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

            // Every exercise contributes its goal and its hint, and Settings and the credits are non-empty.
            Assert.GreaterOrEqual(lines.FindAll(l => l.Source.EndsWith(".goal")).Count, 30);
            Assert.GreaterOrEqual(lines.FindAll(l => l.Source.StartsWith("SettingsPanel")).Count, 5);
            Assert.GreaterOrEqual(lines.FindAll(l => l.Source.StartsWith("Credits.")).Count, 5);
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
        // The words of a simulator
        // -----------------------------------------------------------------

        /// <summary>The words of the 4.x puzzle game: a game, its levels, its bins, its players.</summary>
        private static readonly Regex GameWords =
            new Regex(@"\b(game|games|level|levels|bin|bins|puzzle|puzzles|player|players)\b",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string WhereItCameFrom = "WorkbenchPanel.AboutStory WHERE IT CAME FROM";

        /// <summary>
        /// BitSorter is a simulator for a course, not a game (Part 16e): nothing it says speaks of a game, a level, a bin or a
        /// player. Two places are excused, and say why: ABOUT BITSORTER's WHERE IT CAME FROM tells the 4.x game's story, and the
        /// roll's GAME DESIGN and LEVEL DESIGN are Ahmad's roles of 2026-09-27, his to change.
        /// </summary>
        [Test]
        public void NothingAPlayerReads_SpeaksOfAGame()
        {
            var offences = new List<string>();

            foreach (Line line in EverythingAPlayerReads())
            {
                if (line.Source == WhereItCameFrom || System.Array.IndexOf(Credits.Roles, line.Text) >= 0)
                    continue;

                if (GameWords.IsMatch(line.Text))
                    offences.Add(line.ToString());
            }

            CollectionAssert.IsEmpty(offences, "words of the 4.x game, in a simulator:\n  " + string.Join("\n  ", offences));
        }

        /// <summary>
        /// The search finds the words it looks for: the story of where BitSorter came from does tell of a game and its
        /// levels, and so do the two roles. An assertion about absence needs one that shows the thing is possible.
        /// </summary>
        [Test]
        public void TheSearchFindsTheGame_WhereTheGameIsTold()
        {
            Line story = EverythingAPlayerReads().Find(l => l.Source == WhereItCameFrom);

            Assert.IsNotNull(story.Text, "sanity: ABOUT BITSORTER no longer has a WHERE IT CAME FROM");
            Assert.IsTrue(GameWords.IsMatch(story.Text), "the story of the 4.x game does not mention a game");
            Assert.IsTrue(GameWords.IsMatch("GAME DESIGN") && GameWords.IsMatch("LEVEL DESIGN"));
            Assert.IsFalse(GameWords.IsMatch("A digital logic workbench and SystemVerilog simulator."));
        }

        // -----------------------------------------------------------------
        // Buttons the text tells the player to press
        // -----------------------------------------------------------------

        /// <summary>The key that opens the keyboard map, as <c>WorkbenchKeys</c> shows it.</summary>
        private static string KeyMapSymbol => BitSorter.ViewModel.WorkbenchKeys.For(BitSorter.ViewModel.WorkbenchCommand.Keys).Cap;

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
        /// The keyboard map's key, <c>?</c> (<c>WorkbenchKeys</c>, Slash), is the only such symbol in the simulator, as the
        /// 4.x game's help badge was in that game. If a second one ever appears, it belongs in the set below rather than in
        /// a second copy of this test.
        /// </remarks>
        [Test]
        public void EverySymbolThePlayerIsToldToPress_IsOneTheInterfaceShows()
        {
            var shown = new HashSet<string> { KeyMapSymbol };
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
                + "\nthe keyboard map's key is \"" + KeyMapSymbol + "\"");
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
