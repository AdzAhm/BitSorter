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
    }
}
