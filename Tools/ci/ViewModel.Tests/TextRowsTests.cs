using System.Linq;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A long expression in rows that fit (Part 15h-4d): cut after each product's " + ", so no term is split, and every row
    /// within its room -- LOGIC's expressions ended in "..." and could not be read (Ahmad, 2026-10-29).
    /// </summary>
    public class TextRowsTests
    {
        private static float Chars(string text) => text.Length;

        [Test]
        public void ASumOfProducts_IsCutBetweenItsProducts_EveryRowInItsRoom()
        {
            const string sum = "y[1] = a[1]' a[0]' b[1]' b[0] op[1]' op[0] + a[1]' a[0]' b[1]' op[1]' op[0]' + a[1]' b[1] op[1]' op[0] + a[0] b[1]";
            var rows = TextRows.Of(sum, 40f, Chars, " + ");
            Assert.Greater(rows.Count, 1);
            Assert.IsTrue(rows.All(r => r.Length <= 40), string.Join(" / ", rows));
            Assert.AreEqual(sum, string.Join(" ", rows), "nothing lost, nothing added but the cuts");
            foreach (string row in rows)
                StringAssert.DoesNotStartWith("+", row.Trim(), "a row starts with a product, the + left at the end of the one before");
        }

        [Test]
        public void ShortText_IsOneRow_AndAPieceTooLong_IsCutAtItsSpaces()
        {
            CollectionAssert.AreEqual(new[] { "y = a b" }, TextRows.Of("y = a b", 40f, Chars, " + "));
            var rows = TextRows.Of("one two three four five six", 10f, Chars, " + ");
            Assert.IsTrue(rows.All(r => r.Length <= 10), string.Join(" / ", rows));
            Assert.AreEqual("one two three four five six", string.Join(" ", rows));
        }

        [Test]
        public void AnAssignLine_IsCutAfterEachBar()
        {
            const string assign = "assign y = (~a & b) | (a & ~b) | (a & b & c) | (~a & ~b & ~c);";
            var rows = TextRows.Of(assign, 26f, Chars, " | ");
            Assert.IsTrue(rows.All(r => r.Length <= 26), string.Join(" / ", rows));
            Assert.AreEqual(assign, string.Join(" ", rows));
        }

        /// <summary>
        /// A wave row's name cut from its start to its room is measured a few times, however long: it was measured once for
        /// every character cut, each a whole layout of what was left, so a 5,000-letter port name -- ten kilobytes, inside every
        /// import limit -- laid out twelve million characters in one frame, and thirty thousand ran a browser's page out of
        /// memory before the frame could end (browser-4).
        /// </summary>
        [Test]
        public void ANameCutToItsRoom_IsMeasuredAFewTimes_HoweverLong()
        {
            string name = new string('a', 30_000) + "_port";
            long measured = 0;
            int asked = 0;
            float Counted(string text)
            {
                asked++;
                measured += text.Length;
                return 6f * text.Length;
            }

            string cut = TextRows.Tail(name, 252f, Counted);

            Assert.AreEqual("..." + name.Substring(name.Length - 39), cut, "the longest end that fits behind ...");
            Assert.Less(asked, 40, "times measured");
            Assert.Less(measured, 20_000, "characters laid out");
        }

        /// <summary>The end found by halves is the end found a character at a time, wherever the room falls.</summary>
        [Test]
        public void ANameCut_IsTheLongestEndThatFits_AsCuttingACharacterAtATimeFindsIt()
        {
            var random = new System.Random(7);
            for (int trial = 0; trial < 500; trial++)
            {
                int length = random.Next(0, 120);
                var made = new System.Text.StringBuilder();
                for (int i = 0; i < length; i++)
                    made.Append("aiWm_.0"[random.Next(7)]);
                string name = made.ToString();
                float room = random.Next(0, 400);
                float Width(string text) => text.Sum(c => c == 'i' || c == '.' ? 3f : c == 'W' || c == 'm' ? 9f : 6f);

                string slow = name;
                if (Width(name) > room)
                    for (int cut = 1; cut < name.Length; cut++)
                        if (Width("..." + name.Substring(cut)) <= room)
                        {
                            slow = "..." + name.Substring(cut);
                            break;
                        }

                Assert.AreEqual(slow, TextRows.Tail(name, room, Width), $"'{name}' in {room}");
            }
        }
    }
}
