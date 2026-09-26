using System.Collections.Generic;
using System.IO;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="Credits"/>: who made the game and what it borrows, and that the menu and the roll
    /// say the same thing.
    /// </summary>
    public class CreditsTests
    {
        private static List<string> RollText(string version = "v9.9.9")
        {
            var text = new List<string>();

            foreach (Credits.Line line in Credits.Roll(MainMenu.Tagline, version))
                text.Add(line.Text);

            return text;
        }

        /// <summary>
        /// The menu's music line is exactly what it was before it was built from the credits.
        /// </summary>
        /// <remarks>
        /// It is the one line a licence requires be shown, and it used to be a literal. Pinned to
        /// the letter, so building it from <see cref="Credits.MenuMusic"/> cannot have dropped the
        /// licence's address or the note that the track was changed.
        /// </remarks>
        [Test]
        public void TheMenusMusicLine_IsWhatItWas()
        {
            Assert.AreEqual(
                "Menu music: \"Dream\" by jkjkke (CC0)  ·  " +
                "\"Woodland Fantasy\" by Matthew Pablo, matthewpablo.com " +
                "(CC BY 3.0, creativecommons.org/licenses/by/3.0; converted to mono)",
                Credits.MenuMusicLine);

            Assert.AreEqual(Credits.MenuMusicLine, GameAudio.MenuMusicCredit, "the menu reads its credit from elsewhere");
        }

        [Test]
        public void TheRoll_NamesTheMakerAndTheCompany()
        {
            List<string> roll = RollText();

            CollectionAssert.Contains(roll, "Ahmad Zoabi");
            CollectionAssert.Contains(roll, "ZADZ");
        }

        [Test]
        public void EveryRole_IsTheMakers()
        {
            IReadOnlyList<Credits.Line> roll = Credits.Roll(MainMenu.Tagline, "v9.9.9");
            int roles = 0;

            for (int i = 0; i < roll.Count; i++)
            {
                if (roll[i].Kind != Credits.Kind.Role || !System.Array.Exists(Credits.Roles, r => r == roll[i].Text))
                    continue;

                roles++;
                Assert.Less(i + 1, roll.Count, $"{roll[i].Text} is the last line, with nobody under it");
                Assert.AreEqual(Credits.Maker, roll[i + 1].Text, $"{roll[i].Text} is not credited to the maker");
            }

            Assert.AreEqual(Credits.Roles.Length, roles, "a role is missing from the roll");
            Assert.GreaterOrEqual(roles, 6, "a credits roll with fewer roles than this is not the usual set");
        }

        /// <summary>
        /// Every borrowed track is on the roll with everything its licence asks for.
        /// </summary>
        [Test]
        public void EveryMenuTrack_IsOnTheRoll_WithItsTerms()
        {
            string roll = string.Join("\n", RollText());

            foreach (Credits.Track track in Credits.MenuMusic)
            {
                StringAssert.Contains(track.Title, roll);
                StringAssert.Contains(track.Author, roll);
                StringAssert.Contains(track.Licence, roll);

                if (track.LicenceAddress != null)
                    StringAssert.Contains(track.LicenceAddress, roll, $"{track.Title}'s licence address is missing");

                if (track.Change != null)
                    StringAssert.Contains(track.Change, roll, $"{track.Title}'s change is not noted");
            }
        }

        /// <summary>Thanks to the playtesters, by role and not by name, and the engine, at the end.</summary>
        [Test]
        public void TheRoll_ThanksThePlaytesters_AndSaysWhatItIsBuiltWith()
        {
            List<string> roll = RollText();

            CollectionAssert.Contains(roll, Credits.PlaytesterThanks);
            StringAssert.Contains("playtester", Credits.PlaytesterThanks.ToLowerInvariant());
            CollectionAssert.Contains(roll, "BUILT WITH");
            CollectionAssert.Contains(roll, "Unity");
        }

        /// <summary>
        /// The roll ends on its farewell, with the copyright and the version under it -- the version
        /// the build says, not one written here.
        /// </summary>
        [Test]
        public void TheRoll_EndsOnItsFarewell_WithTheCopyrightAndVersion()
        {
            IReadOnlyList<Credits.Line> roll = Credits.Roll(MainMenu.Tagline, "v9.9.9");

            int farewells = 0;
            int at = -1;

            for (int i = 0; i < roll.Count; i++)
            {
                if (roll[i].Kind == Credits.Kind.Farewell)
                {
                    farewells++;
                    at = i;
                }
            }

            Assert.AreEqual(1, farewells, "the roll should come to rest on exactly one line");
            Assert.AreEqual(roll.Count - 2, at, "the farewell should be the last line but the copyright");
            StringAssert.Contains("v9.9.9", roll[roll.Count - 1].Text);
            StringAssert.Contains(Credits.Copyright, roll[roll.Count - 1].Text);
        }

        /// <summary>
        /// The copyright line says what the LICENSE file says: the same holder, the same year.
        /// </summary>
        [Test]
        public void TheCopyright_MatchesTheLicence()
        {
            string licence = File.ReadAllText(Path.Combine(Application.dataPath, "..", "LICENSE"));

            StringAssert.Contains("Copyright (c) 2026 Ahmad Zoabi (ZADZ)", licence,
                "the LICENSE file's holder changed; the credits' copyright line must follow it");
            Assert.AreEqual("© 2026 Ahmad Zoabi (ZADZ)", Credits.Copyright);
        }

        [Test]
        public void TheRoll_ComesToRestWithItsLastWordInTheMiddle()
        {
            // The roll's top is hung from the bottom of the screen: it has risen by half the screen
            // plus the depth of the last word when that word's middle is at the screen's middle.
            Assert.AreEqual(1080f * 0.5f + 1500f, CreditsPanel.RestingRise(1080f, 1500f));
        }

        [TestCase(940f, 900f, ExpectedResult = 1f)]
        [TestCase(900f, 900f, ExpectedResult = 1f)]
        [TestCase(450f, 900f, ExpectedResult = 0.5f)]
        [TestCase(0f, 900f, ExpectedResult = 1f)]
        public float TheSettings_ShrinkOnlyWhenTheyMustToFit(float room, float needed) =>
            SettingsPanel.FitScale(room, needed);
    }
}
