using System;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="UiStack"/>, the allocator every HUD row is placed by, and the three stacks
    /// <see cref="UiRows"/> declares with it.
    /// </summary>
    /// <remarks>
    /// These replace six tests that each asserted one pair of rows cleared each other -- the toast
    /// and the controls line, the verdict and the hint, the setup panel and the badge, and three
    /// more. Each was added after that pair had shipped on top of each other, and none of them
    /// could catch the next pair. A stack cannot place a row over the one before it, so what is
    /// left to check is that nothing went round the stacks.
    /// </remarks>
    public class UiStackTests
    {
        [Test]
        public void TheFirstRow_StartsAtTheStart()
        {
            var stack = new UiStack("probe", 16f, 8f);

            Assert.AreEqual(16f, stack.Add("first", 44f).Offset);
        }

        [Test]
        public void EachRow_StartsTheGapClearOfTheOneBefore()
        {
            var stack = new UiStack("probe", 16f, 8f);
            stack.Add("first", 44f);

            UiRow second = stack.Add("second", 26f);

            Assert.AreEqual(16f + 44f + 8f, second.Offset);
            Assert.AreEqual(second.Offset + 26f, second.End);
        }

        /// <summary>A row that belongs to the one before it can hang directly under it.</summary>
        [Test]
        public void ARowWithAGapOfItsOwn_UsesThatInstead()
        {
            var stack = new UiStack("probe", 16f, 8f);
            UiRow badge = stack.Add("badge", 38f);

            Assert.AreEqual(badge.End, stack.Add("caption", 20f, 0f).Offset);
        }

        [Test]
        public void Next_IsWhereTheNextRowWouldStart()
        {
            var stack = new UiStack("probe", 16f, 8f);
            Assert.AreEqual(16f, stack.Next, "an empty stack's next row is its first");

            UiRow row = stack.Add("row", 30f);
            Assert.AreEqual(row.End + 8f, stack.Next);
        }

        /// <summary>
        /// The one thing a stack exists to prevent cannot be asked of it: a row starting inside the
        /// one before, or a row of negative height.
        /// </summary>
        [Test]
        public void AStack_RefusesARowThatWouldOverlap()
        {
            var stack = new UiStack("probe", 16f, 8f);
            stack.Add("row", 30f);

            Assert.Throws<ArgumentOutOfRangeException>(() => stack.Add("inside", 10f, -4f));
            Assert.Throws<ArgumentOutOfRangeException>(() => stack.Add("upside down", -10f));
        }

        // -----------------------------------------------------------------
        // The game's own stacks
        // -----------------------------------------------------------------

        /// <summary>No two rows of any of the HUD's stacks overlap.</summary>
        [Test]
        public void NoTwoRowsInAnyStack_Overlap()
        {
            foreach (UiStack stack in UiRows.All)
            {
                for (int i = 1; i < stack.Rows.Count; i++)
                {
                    Assert.GreaterOrEqual(stack.Rows[i].Offset, stack.Rows[i - 1].End,
                        $"in the {stack.Name} stack, {stack.Rows[i]} overlaps {stack.Rows[i - 1]}");
                }
            }
        }

        /// <summary>
        /// Every row the HUD is placed by came out of one of its stacks.
        /// </summary>
        /// <remarks>
        /// The failure this guards against is the one that has actually happened each time: a row
        /// worked out by hand beside the stack rather than taken from it. A row built outside a
        /// stack would not be found in one.
        /// </remarks>
        [Test]
        public void EveryRow_CameOutOfAStack()
        {
            int checkedRows = 0;

            foreach (PropertyInfo property in typeof(UiRows).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType != typeof(UiRow))
                    continue;

                var row = (UiRow)property.GetValue(null);
                checkedRows++;

                Assert.IsTrue(InAStack(row), $"UiRows.{property.Name} ({row}) is in none of the stacks");
            }

            Assert.Greater(checkedRows, 10, "sanity: the rows were found");
        }

        /// <summary>
        /// The panels down the right start below the badge's key hint, and stop above everything
        /// along the bottom.
        /// </summary>
        /// <remarks>
        /// Stated even though the stack already makes it so, because the reason is worth keeping:
        /// the help panel is brought to front when it opens, so a panel starting any higher covers
        /// the "H" explaining the control the player has just used. And the setup panel reaches
        /// down towards the refusal toast, which is a different stack, so only this says they meet.
        /// </remarks>
        [Test]
        public void TheRightHandPanels_ClearTheBadgeAboveAndTheRowsBelow()
        {
            Assert.GreaterOrEqual(UiRows.Panels.Offset, UiRows.BadgeKey.End,
                "the help and setup panels cover the key hint that says how to open help");

            foreach (UiRow row in UiRows.Bottom.Rows)
            {
                // The solved card sits between the right-hand panels, not under them; the test
                // below holds it to that.
                if (row.Name == UiRows.SolvedCard.Name)
                    continue;

                Assert.GreaterOrEqual(UiRows.PanelFloor, row.End,
                    $"a panel down the right reaches down over the {row.Name}");
            }
        }

        /// <summary>
        /// The solved card is narrow enough to sit beside the right-hand panels rather than under
        /// them, at every screen shape the game is played at.
        /// </summary>
        /// <remarks>
        /// It is the one row along the bottom the panels are allowed to reach down beside -- a card
        /// that pushed their floor up would shorten the help panel on every level. That is only
        /// safe while the card stops short of them: the help panel, at its narrowest, in from the
        /// right edge by the margin. The canvas is 1920x1080 scaled to match width and height
        /// equally, so its width in canvas units is sqrt(1920 * 1080 * aspect); the browser build
        /// holds the game at 16:10, and a desktop window is usually 16:9.
        /// </remarks>
        [Test]
        public void TheSolvedCard_StopsShortOfTheRightHandPanels()
        {
            foreach (float aspect in new[] { 16f / 10f, 16f / 9f })
            {
                float halfCanvas = (float)System.Math.Sqrt(1920.0 * 1080.0 * aspect) * 0.5f;
                float panelsFromCentre = halfCanvas - UiTheme.Margin - UiTheme.HelpMinimumWidth;

                Assert.LessOrEqual(UiTheme.SolvedCardWidth * 0.5f + UiTheme.Gap, panelsFromCentre,
                    $"at {aspect:F2} the solved card reaches under the help panel");
            }
        }

        private static bool InAStack(UiRow row)
        {
            foreach (UiStack stack in UiRows.All)
            {
                foreach (UiRow candidate in stack.Rows)
                {
                    if (candidate.Name == row.Name && candidate.Offset == row.Offset && candidate.Height == row.Height)
                        return true;
                }
            }

            return false;
        }
    }
}
