using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="ControlsReference"/>: the board's controls and the tutorial's card come from one
    /// list, so adding a binding reaches both with no second edit.
    /// </summary>
    /// <remarks>
    /// The list used to be a string literal inside RunControls. The moment the tutorial gained a
    /// card listing the same controls there were two copies, and a changed binding would have left
    /// two different answers on screen. These tests exist to make that regression loud rather than
    /// silent -- there is no compiler error for two strings drifting apart.
    /// </remarks>
    public class ControlsReferenceTests
    {
        [Test]
        public void ThereAreControlsAtAll()
        {
            Assert.IsNotEmpty(ControlsReference.All);

            foreach (ControlEntry entry in ControlsReference.All)
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Text), "a control with no text");
        }

        [Test]
        public void EveryControl_ReachesExactlyOneGroup()
        {
            // The card is the complete reference, so nothing may be missing from it -- and nothing
            // may appear twice, which a hand-written grouping would eventually do.
            foreach (ControlEntry entry in ControlsReference.All)
            {
                int found = 0;

                foreach (ControlGroup group in ControlsReference.Groups)
                {
                    foreach (ControlEntry candidate in group.Entries)
                    {
                        if (candidate.Text == entry.Text)
                            found++;
                    }
                }

                Assert.AreEqual(1, found, $"'{entry.Text}' appears in {found} groups, not one");
            }
        }

        [Test]
        public void TheGroupsSayNothingThatIsNotInTheList()
        {
            // The direction the test above cannot catch: a row invented in the grouping rather than
            // taken from the list would still let every entry through.
            foreach (ControlGroup group in ControlsReference.Groups)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(group.Name), "a group with no heading");

                foreach (ControlEntry entry in group.Entries)
                {
                    bool known = false;

                    foreach (ControlEntry candidate in ControlsReference.All)
                    {
                        if (candidate.Text == entry.Text)
                            known = true;
                    }

                    Assert.IsTrue(known,
                        $"'{group.Name}' lists '{entry.Text}', which is not in ControlsReference.All");
                }
            }
        }

        /// <summary>The four blocks either side of the run buttons.</summary>
        private static readonly ControlSpot[] Blocks =
        {
            ControlSpot.LeftUpper, ControlSpot.LeftLower, ControlSpot.RightUpper, ControlSpot.RightLower,
        };

        /// <summary>The spots that show one key alone, on or under the thing it works.</summary>
        private static readonly ControlSpot[] KeySpots =
        {
            ControlSpot.RunButton, ControlSpot.ResetButton, ControlSpot.UndoButton, ControlSpot.RedoButton,
            ControlSpot.ClearButton, ControlSpot.MenuButton, ControlSpot.HelpBadge, ControlSpot.TimingBadge,
            ControlSpot.LevelBefore, ControlSpot.LevelAfter,
        };

        private static string[] Pieces(string shown) =>
            shown.Split(new[] { ControlsReference.LineSeparator }, StringSplitOptions.RemoveEmptyEntries);

        [Test]
        public void EveryBoardControl_IsShownAtItsSpot()
        {
            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (!entry.OnBoard)
                    continue;

                string shown = ControlsReference.At(entry.Spot);

                if (entry.ShowsKeyOnly)
                    Assert.AreEqual(entry.Key, shown, $"'{entry.Text}' should show its key at {entry.Spot}");
                else
                    CollectionAssert.Contains(Pieces(shown), entry.Text, $"'{entry.Text}' never reaches {entry.Spot}");
            }
        }

        [Test]
        public void TheBoardSaysNothingThatIsNotInTheList()
        {
            foreach (ControlSpot spot in Blocks)
            {
                foreach (string piece in Pieces(ControlsReference.At(spot)))
                {
                    bool known = false;

                    foreach (ControlEntry entry in ControlsReference.All)
                    {
                        if (entry.Text == piece.Trim() && entry.Spot == spot)
                            known = true;
                    }

                    Assert.IsTrue(known, $"{spot} shows '{piece.Trim()}', which the list does not put there");
                }
            }
        }

        /// <summary>
        /// Each key sits over the button that does what its phrase says, so a key cannot end up
        /// over the wrong button. Each spot holds one key.
        /// </summary>
        [TestCase(ControlSpot.RunButton, "to run")]
        [TestCase(ControlSpot.ResetButton, "to reset")]
        [TestCase(ControlSpot.UndoButton, "to undo")]
        [TestCase(ControlSpot.RedoButton, "to redo")]
        [TestCase(ControlSpot.ClearButton, "to clear")]
        [TestCase(ControlSpot.MenuButton, "main menu")]
        [TestCase(ControlSpot.HelpBadge, "for help")]
        [TestCase(ControlSpot.TimingBadge, "timing diagram")]
        [TestCase(ControlSpot.LevelBefore, "level before")]
        [TestCase(ControlSpot.LevelAfter, "level after")]
        public void EachKey_IsOnTheThingItWorks(ControlSpot spot, string does)
        {
            int held = 0;

            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (entry.Spot != spot)
                    continue;

                held++;
                StringAssert.Contains(does, entry.Text, $"{spot} holds '{entry.Text}'");
            }

            Assert.AreEqual(1, held, $"{spot} should hold exactly one key");
        }

        /// <summary>
        /// The banner's level counter names the keys that step through the run -- and only the
        /// ones that go somewhere.
        /// </summary>
        [Test]
        public void TheLevelCounter_NamesQAndE_WhereTheyGoSomewhere()
        {
            string before = ControlsReference.At(ControlSpot.LevelBefore);
            string after = ControlsReference.At(ControlSpot.LevelAfter);

            Assert.AreEqual("Q", before);
            Assert.AreEqual("E", after);

            string middle = StatusBanner.Counter(13, 25);
            StringAssert.Contains(before + " ", middle);
            StringAssert.Contains(" " + after, middle);
            StringAssert.Contains("14 / 25", middle);

            StringAssert.DoesNotContain(before + " ", StatusBanner.Counter(0, 25), "Q offered on the first level");
            StringAssert.DoesNotContain(" " + after, StatusBanner.Counter(24, 25), "E offered on the last level");
        }

        // -----------------------------------------------------------------
        // The menu's own line
        // -----------------------------------------------------------------

        [Test]
        public void EveryMenuControl_ReachesTheMenuLine()
        {
            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (!entry.OnMenu)
                    continue;

                StringAssert.Contains(entry.Text, ControlsReference.MenuLine,
                    $"'{entry.Text}' is flagged for the menu but never reaches it");
            }
        }

        [Test]
        public void TheMenuLineSaysNothingThatIsNotInTheList()
        {
            // The direction that catches a literal creeping back in. The menu used to draw its own
            // string, and that string was the only place M appeared.
            string[] shown = ControlsReference.MenuLine.Split(
                new[] { ControlsReference.LineSeparator }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string piece in shown)
            {
                bool known = false;

                foreach (ControlEntry entry in ControlsReference.All)
                {
                    if (entry.Text == piece.Trim())
                        known = true;
                }

                Assert.IsTrue(known,
                    $"the menu line shows '{piece.Trim()}', which is not in ControlsReference.All");
            }
        }

        /// <summary>
        /// The main menu's own line does not offer the main menu.
        /// </summary>
        /// <remarks>
        /// It said "M for the main menu" at the foot of the main menu, where M did the opposite: it
        /// closed the menu. It went on that line when it was the only place the key was named
        /// anywhere; every board names it now, under MENU, which is where it means what it
        /// says. The key is Escape now, and on the menu Escape closes it just the same.
        /// </remarks>
        [Test]
        public void TheMenuLine_DoesNotOfferTheMenuItIsOn()
        {
            StringAssert.DoesNotContain("main menu", ControlsReference.MenuLine,
                "the main menu's footer offers the main menu, and Escape there closes it");
        }

        /// <summary>
        /// The main menu's footer names only keys that do something while the menu is open.
        /// </summary>
        /// <remarks>
        /// It said "H for help" and "ESC for levels" as well, and with the menu open neither did
        /// anything: the level list opens on its key only when nothing else is open, so that it can
        /// never stack on the menu, and the help panel's H is held back the same way. That key is M
        /// now. Only N, which
        /// mutes from anywhere, did what the line said. The menu's own buttons are the way on.
        /// </remarks>
        [Test]
        public void TheMenuLine_NamesOnlyKeysThatWorkOnTheMenu()
        {
            foreach (string dead in new[] { "H for help", "M for levels" })
            {
                StringAssert.DoesNotContain(dead, ControlsReference.MenuLine,
                    $"the main menu's footer says '{dead}', and that key does nothing while the menu is open");
            }
        }

        [Test]
        public void TheMenuLineIsAShortlist_NotEverything()
        {
            // The menu is a front door: it names the panels and the sound, not how to wire a gate.
            int onMenu = 0;

            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (entry.OnMenu)
                    onMenu++;
            }

            Assert.Greater(onMenu, 0, "the menu line would be empty");

            Assert.Less(onMenu, ControlsReference.All.Count,
                "if the menu line carries everything, the flag has stopped meaning anything");
        }

        /// <summary>
        /// Both renderings are exactly as long as the list says they should be.
        /// </summary>
        /// <remarks>
        /// This is the test that actually fails when someone adds a binding to only one of them. The
        /// others prove every entry gets through; this one proves nothing else does, and that the
        /// counts are derived rather than fixed -- so a new entry necessarily moves both numbers.
        /// </remarks>
        [Test]
        public void AddingAControl_MovesBothRenderings()
        {
            int total = ControlsReference.All.Count;
            int onBoard = 0;

            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (entry.OnBoard)
                    onBoard++;
            }

            int onMenu = 0;

            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (entry.OnMenu)
                    onMenu++;
            }

            int boardCount = 0;

            foreach (ControlSpot spot in Blocks)
                boardCount += Pieces(ControlsReference.At(spot)).Length;

            foreach (ControlSpot spot in KeySpots)
            {
                if (!string.IsNullOrEmpty(ControlsReference.At(spot)))
                    boardCount++;
            }

            int menuCount = ControlsReference.MenuLine.Split(
                new[] { ControlsReference.LineSeparator }, StringSplitOptions.RemoveEmptyEntries).Length;

            int grouped = 0;

            foreach (ControlGroup group in ControlsReference.Groups)
                grouped += group.Entries.Count;

            Assert.AreEqual(onBoard, boardCount, "the board does not show the entries placed on it");
            Assert.AreEqual(onMenu, menuCount, "the menu line is not the flagged entries");
            Assert.AreEqual(total, grouped, "the card's columns are not the whole list");
        }

        /// <summary>
        /// The card names the way back to the main menu.
        /// </summary>
        /// <remarks>
        /// The card is handed to a player who has just finished the tutorial, as the complete
        /// reference -- that is what it is for. The main menu is reachable by its key and the MENU
        /// button and by nothing else except finishing the last level, so a card that does not mention it teaches that there is
        /// no way back to the front door.
        ///
        /// M was bound in MainMenu.Update and written into a literal keys line in MainMenu.Build,
        /// and never added here. Every test above passes with it missing, because they all compare
        /// this list against its own two renderings and nothing compares it against the game.
        /// </remarks>
        [Test]
        public void TheReference_NamesTheWayBackToTheMainMenu()
        {
            bool named = false;

            foreach (ControlGroup group in ControlsReference.Groups)
            {
                foreach (ControlEntry entry in group.Entries)
                {
                    if (entry.Text.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0)
                        named = true;
                }
            }

            Assert.IsTrue(named,
                "no control on the card mentions the main menu, so a player who finishes the " +
                "tutorial is never told how to reach it");
        }

        /// <summary>
        /// The board names the way back to the main menu: its key, under the MENU button.
        /// </summary>
        /// <remarks>
        /// M was kept off the old line for room, which left it named in two places: the menu itself,
        /// which a player has to be on already, and the card at the end of the tutorial, which a
        /// player who skipped the tutorial never sees. The board is the one reference on screen at
        /// every level.
        /// </remarks>
        [Test]
        public void TheBoard_NamesTheWayBackToTheMainMenu()
        {
            Assert.AreEqual("ESC", ControlsReference.At(ControlSpot.MenuButton),
                "the MENU button does not say which key opens the main menu");
        }

        /// <summary>
        /// Escape is the main menu and M is the level list, and the board says so.
        /// </summary>
        /// <remarks>
        /// Swapped after a playtest, 2026-09-26: Escape is the key players reach for to get to a
        /// menu. The bindings live in MainMenu and LevelSelectPanel and the words live here, so this
        /// pins the words to the swap.
        /// </remarks>
        [Test]
        public void TheBoard_NamesEscapeForTheMenu_AndMForTheLevels()
        {
            var blocks = new System.Text.StringBuilder();

            foreach (ControlSpot spot in Blocks)
                blocks.AppendLine(ControlsReference.At(spot));

            StringAssert.Contains("M for levels", blocks.ToString());
            StringAssert.DoesNotContain("ESC for levels", blocks.ToString());
            StringAssert.DoesNotContain("M for the main menu", blocks.ToString());

            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (entry.Spot == ControlSpot.MenuButton)
                    Assert.AreEqual("ESC for the main menu", entry.Text);
            }
        }

        /// <summary>
        /// The timing diagram's key is a control like the rest: on the card, named by F8, and under
        /// its badge on every board.
        /// </summary>
        /// <remarks>
        /// The diagram shows every source and bin, so it is worth naming on every board. It was on
        /// F2 for its first day, which went back to the clock diagram (2026-10-01).
        /// </remarks>
        [Test]
        public void TheTimingDiagram_IsNamedOnTheCardAndUnderItsBadge()
        {
            StringAssert.StartsWith("F8", ControlsReference.TimingDiagram.Text);

            Assert.IsTrue(OnTheCard(ControlsReference.TimingDiagram),
                "the tutorial's card does not name the timing diagram's key");
            Assert.AreEqual(ControlSpot.TimingBadge, ControlsReference.TimingDiagram.Spot,
                "the timing diagram's key is not under its badge");
            Assert.AreEqual("F8", ControlsReference.At(ControlSpot.TimingBadge));
        }

        /// <summary>
        /// The clock diagram's key is F2, named at the end of the clock strip and on neither the
        /// board nor the card.
        /// </summary>
        /// <remarks>
        /// The card's taller column holds as many rows as <c>TheCardsColumns_StayRoughlyBalanced</c>
        /// allows, and the clock diagram is worth nothing before the clocked chapter. The clock
        /// strip is on screen on exactly the levels where it means something, and ends on this
        /// entry's own text, so the key is named where it is wanted and nowhere it is not.
        /// </remarks>
        [Test]
        public void TheClockDiagram_IsF2_NamedByTheClockStripAndNotOnTheCard()
        {
            Assert.AreEqual("F2", ControlsReference.ClockDiagram.Key);
            Assert.IsFalse(ControlsReference.ClockDiagram.OnBoard, "the clock diagram's key is on the board");
            Assert.IsFalse(OnTheCard(ControlsReference.ClockDiagram),
                "the clock diagram's key is on the card, which has no row to spare for it");

            Assert.AreNotEqual(ControlsReference.TimingDiagram.Key, ControlsReference.ClockDiagram.Key,
                "the two diagrams share a key again");
        }

        /// <summary>
        /// The wheel over the timing diagram is named on the strip itself, and nowhere else: not on
        /// the tutorial's card, which has no row to spare, and not in the board's blocks.
        /// </summary>
        [Test]
        public void TheTimingZoom_IsNamedOnTheStripAndNotOnTheCard()
        {
            Assert.IsFalse(ControlsReference.TimingZoom.OnBoard, "the zoom's gestures are in the board's blocks");
            Assert.IsFalse(OnTheCard(ControlsReference.TimingZoom),
                "the zoom's gestures are on the card, which has no row to spare for them");
            StringAssert.Contains("zoom", ControlsReference.TimingZoom.Text);
        }

        private static bool OnTheCard(ControlEntry wanted)
        {
            foreach (ControlGroup group in ControlsReference.Groups)
            {
                foreach (ControlEntry entry in group.Entries)
                {
                    if (entry.Text == wanted.Text)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The only function keys the game reads are F2 and F8, the two no browser keeps for itself.
        /// </summary>
        /// <remarks>
        /// The game runs in a browser, and a key the browser keeps does the browser's thing first:
        /// F3 opened the page's find bar in a playtest (2026-09-26). Checked against the browsers'
        /// own lists (2026-10-01): F1 is help in every browser, F3 find, F4 selects the address bar
        /// in Edge (and Alt+F4 closes the window, with Alt held to pick wires), F5 reloads, F6 moves
        /// focus to the toolbars, F7 is caret browsing, F9 is Edge's Immersive Reader and Firefox's
        /// reader view, and F10 to F12 are the menu, fullscreen and the developer tools.
        ///
        /// A scan of the sources, as <c>UiTextTests</c> scans for <c>Keyboard.current</c>: the read
        /// this guards against is one added later by someone who never saw this list.
        /// </remarks>
        [Test]
        public void TheGame_ReadsNoFunctionKeyABrowserKeeps()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "View");
            var found = new SortedSet<string>();
            // Every way a key can be named: keyboard.f3Key, keyboard[Key.F3], and the old input
            // manager's KeyCode.F3. The first form alone let the other two through unseen.
            var reading = new Regex(@"\.f(\d+)Key\b|\bKey\.F(\d+)\b|\bKeyCode\.F(\d+)\b");

            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in reading.Matches(File.ReadAllText(path)))
                {
                    string number = match.Groups[1].Success ? match.Groups[1].Value
                        : match.Groups[2].Success ? match.Groups[2].Value
                        : match.Groups[3].Value;

                    found.Add("f" + number + " in " + Path.GetFileName(path));
                }
            }

            Assert.Contains("f2 in ClockDiagram.cs", found, "sanity: the scan did not find F2, so it is looking in the wrong place");

            // And the other two forms are recognised, or a read written either way would pass unseen.
            Assert.IsTrue(reading.IsMatch("keyboard[Key.F3].wasPressedThisFrame"), "sanity: the scan does not see Key.F3");
            Assert.IsTrue(reading.IsMatch("Input.GetKeyDown(KeyCode.F5)"), "sanity: the scan does not see KeyCode.F5");

            foreach (string read in found)
            {
                Assert.IsTrue(read.StartsWith("f2 ") || read.StartsWith("f8 "),
                    $"the game reads {read}, a function key a browser keeps for itself -- only F2 and F8 are free");
            }
        }

        /// <summary>
        /// The card names the keys that pick a part and redo, which the game binds.
        /// </summary>
        /// <remarks>
        /// Both were bound and named only in the README -- a player finds out the number keys pick
        /// parts by accident or not at all.
        /// </remarks>
        [Test]
        public void TheCard_NamesThePartKeysAndRedo()
        {
            var named = new System.Text.StringBuilder();

            foreach (ControlGroup group in ControlsReference.Groups)
            {
                foreach (ControlEntry entry in group.Entries)
                    named.AppendLine(entry.Text);
            }

            StringAssert.Contains("1 to 7", named.ToString(), "the card does not say the number keys pick a part");
            StringAssert.Contains("redo", named.ToString(), "the card does not say how to redo");
        }

        [Test]
        public void EveryGroupHasSomethingInIt()
        {
            // An empty column would draw a heading with nothing under it, which reads as a bug.
            foreach (ControlGroup group in ControlsReference.Groups)
                Assert.IsNotEmpty(group.Entries, $"'{group.Name}' has no controls under it");
        }

        [Test]
        public void TheBoardIsAShortlist_NotEverything()
        {
            // Not a style preference: the board's controls are on screen permanently, and putting
            // every one of them there is what a spot of None exists to prevent.
            int onBoard = 0;

            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (entry.OnBoard)
                    onBoard++;
            }

            Assert.Less(onBoard, ControlsReference.All.Count,
                "if the board carries everything, the spots have stopped meaning anything");
        }
    }
}
