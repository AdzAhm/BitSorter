using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="UiColumn"/>, the cursor free play's setup panel and the level list are laid out
    /// down.
    /// </summary>
    public class UiColumnTests
    {
        [Test]
        public void TheFirstRow_StartsAtTheTop()
        {
            Assert.AreEqual(0f, new UiColumn().Take(18f, 4f));
            Assert.AreEqual(36f, new UiColumn(36f).Take(18f, 4f));
        }

        /// <summary>Each row starts below the one before it and the room that one asked for under it.</summary>
        [Test]
        public void EachRow_StartsBelowTheLastAndItsSpace()
        {
            var column = new UiColumn();
            column.Take(18f, 4f);

            Assert.AreEqual(22f, column.Take(24f, 3f));
            Assert.AreEqual(49f, column.Next);
        }

        [Test]
        public void Space_PushesTheNextRowDown()
        {
            var column = new UiColumn();
            column.Take(10f);
            column.Space(8f);

            Assert.AreEqual(18f, column.Take(10f));
        }
    }
}
