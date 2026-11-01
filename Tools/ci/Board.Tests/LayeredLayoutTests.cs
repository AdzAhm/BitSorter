using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The automatic layout's wrap (Part 15h-6): a column far taller than the drawing is wide stands as several side by side,
    /// in its order, nothing on anything; a column short of it stands as it always did. The drawing tests hold every corpus
    /// and exercise drawing's shape; these hold the wrap itself, on columns of inputs alone.
    /// </summary>
    public class LayeredLayoutTests
    {
        [Test]
        public void AShortColumn_StandsAsOne_InItsOrder()
        {
            (Point[] at, _) = Inputs(12);
            Assert.AreEqual(1, at.Select(p => p.X).Distinct().Count(), "one column");
            for (int i = 1; i < at.Length; i++)
                Assert.Less(at[i].Y, at[i - 1].Y, "each under the one before it");
        }

        [Test]
        public void AColumnFarTallerThanTheDrawingIsWide_StandsAsSeveral_InItsOrder_NothingOnAnything()
        {
            (Point[] at, PartOutline[] outlines) = Inputs(200);
            double[] columns = at.Select(p => p.X).Distinct().OrderBy(x => x).ToArray();
            Assert.Greater(columns.Length, 1, "wrapped");

            // Read down each column and then on to the next: the parts as they were given.
            for (int i = 1; i < at.Length; i++)
            {
                if (at[i].X == at[i - 1].X)
                    Assert.Less(at[i].Y, at[i - 1].Y, $"input {i} under input {i - 1}");
                else
                    Assert.AreEqual(System.Array.IndexOf(columns, at[i - 1].X) + 1, System.Array.IndexOf(columns, at[i].X), $"input {i} heads the next column");
            }

            // About twice as wide as tall, never taller than wide, and no two inputs on one another.
            double wide = outlines.Max(o => o.Right) - outlines.Min(o => o.Left);
            double tall = outlines.Max(o => o.Top) - outlines.Min(o => o.Bottom);
            Assert.LessOrEqual(tall, wide, $"{wide} wide, {tall} tall");
            for (int a = 0; a < outlines.Length; a++)
                for (int b = a + 1; b < outlines.Length; b++)
                    Assert.IsFalse(outlines[a].Left < outlines[b].Right && outlines[b].Left < outlines[a].Right
                        && outlines[a].Bottom < outlines[b].Top && outlines[b].Bottom < outlines[a].Top, $"inputs {a} and {b} overlap");
        }

        /// <summary><paramref name="count"/> one-bit inputs and nothing else, laid out: where each stands, and its outline there.</summary>
        private static (Point[] At, PartOutline[] Outlines) Inputs(int count)
        {
            Project project = Project.New("layout");
            var parts = new List<Part>();
            for (int i = 0; i < count; i++)
                parts.Add(new PortPart { Id = i + 1, Name = "in" + i, Width = 1 });
            var first = new HashSet<int>(Enumerable.Range(0, count));
            Placement[] placed = LayeredLayout.Arrange(project, parts, new List<(int, int)>(), first, new HashSet<int>(), new HashSet<int>());
            return (placed.Select(p => p.Position).ToArray(), parts.Select((p, i) => PartGeometry.Placed(project, p, placed[i])).ToArray());
        }
    }
}
