using System.Collections.Generic;
using System.IO;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="Credits"/>: who made the game and what it borrows, and that everything borrowed is
    /// credited as its licence asks.
    /// </summary>
    public class CreditsTests
    {
        private static List<string> RollText(string version = "v9.9.9")
        {
            var text = new List<string>();

            foreach (Credits.Line line in Credits.Roll(Credits.Tagline, version))
                text.Add(line.Text);

            return text;
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
            IReadOnlyList<Credits.Line> roll = Credits.Roll(Credits.Tagline, "v9.9.9");
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
        /// BitSorter has no sound since Part 16b (Ahmad, 2026-11-03), so the roll has no music and no sound role. Paired with
        /// the tests above and below, which show the roll does list what it should, so this is not true of an empty roll.
        /// </summary>
        [Test]
        public void TheRoll_CreditsNoMusicAndNoSound()
        {
            string roll = string.Join("\n", RollText()).ToLowerInvariant();

            foreach (string word in new[] { "music", "sound", "jkjkke", "matthew pablo", "cc by" })
                StringAssert.DoesNotContain(word, roll, $"\"{word}\" is on the roll of a game with no sound");

            CollectionAssert.DoesNotContain(Credits.Roles, "SOUND DESIGN");
            CollectionAssert.DoesNotContain(Credits.Roles, "LEVEL MUSIC");
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
        /// 5.0's lines (Ahmad's choice, 2026-10-28): the roles of the simulator's own, the typefaces it draws its words in
        /// and the simulator its engine is checked against, and who it is for -- no university named.
        /// </summary>
        [Test]
        public void TheRoll_HasFivePointOhsRoles_ItsTypefaces_ItsOracle_AndItsDedication()
        {
            List<string> roll = RollText();

            foreach (string role in new[] { "SYSTEMVERILOG LANGUAGE", "EXERCISE DESIGN", "WORKBENCH DESIGN" })
                CollectionAssert.Contains(Credits.Roles, role);
            CollectionAssert.Contains(roll, "IBM Plex Sans and IBM Plex Mono");
            Assert.IsTrue(roll.Exists(line => line.Contains("SIL Open Font License")), "the typefaces' licence is not named");
            CollectionAssert.Contains(roll, "CHECKED AGAINST");
            CollectionAssert.Contains(roll, "Icarus Verilog");
            CollectionAssert.Contains(roll, "DEDICATED TO");
            CollectionAssert.Contains(roll, Credits.Dedication);
            Assert.IsFalse(roll.Exists(line => line.Contains("Technion") || line.Contains("University")), "a university is named");
        }

        /// <summary>
        /// The roll ends on its farewell, with the copyright and the version under it -- the version
        /// the build says, not one written here.
        /// </summary>
        [Test]
        public void TheRoll_EndsOnItsFarewell_WithTheCopyrightAndVersion()
        {
            IReadOnlyList<Credits.Line> roll = Credits.Roll(Credits.Tagline, "v9.9.9");

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
    }
}
