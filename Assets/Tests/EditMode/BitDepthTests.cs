using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The depth each bit is drawn at, which is what decides which of two overlapping bits is on top.
    /// </summary>
    /// <remarks>
    /// Every bit shares its sorting orders with every other, so depth is the only thing left to
    /// break a tie, and a tie Unity breaks is broken differently from run to run. The Play Mode
    /// test watches real bits meet; these pin the rule itself, for boards larger than the half
    /// adder it runs on.
    /// </remarks>
    public class BitDepthTests
    {
        /// <summary>The most bits one wire holds at once that these tests allow for.</summary>
        private const int BitsOnOneWire = 32;

        /// <summary>
        /// No two bits that can be in flight together share a depth: none on different wires, and
        /// none of the consecutive serials one wire can hold at once -- across the point where the
        /// serial's low bits wrap round, too.
        /// </summary>
        [Test]
        public void BitsInFlightTogether_NeverShareADepth()
        {
            foreach (int oldest in new[] { 0, 1000, 250 })
            {
                var seen = new Dictionary<float, string>();

                for (int edge = 0; edge < 100; edge++)
                {
                    for (int serial = oldest; serial < oldest + BitsOnOneWire; serial++)
                    {
                        float depth = BitRenderer.DepthOf(edge, serial);

                        if (seen.TryGetValue(depth, out string other))
                            Assert.Fail($"edge {edge} serial {serial} is drawn at the same depth as {other}");

                        seen[depth] = $"edge {edge} serial {serial}";
                    }
                }
            }
        }

        /// <summary>A bit keeps one depth for its whole flight: nothing but its identity decides it.</summary>
        [Test]
        public void ABit_KeepsItsDepth()
        {
            Assert.AreEqual(BitRenderer.DepthOf(7, 41), BitRenderer.DepthOf(7, 41));
        }

        /// <summary>
        /// Every bit is behind the board's own depth, where Unity had been drawing its ties, and a
        /// hundred-wire board keeps them all within a unit of it.
        /// </summary>
        [Test]
        public void EveryBit_IsJustBehindTheBoard()
        {
            for (int edge = 0; edge < 100; edge++)
            {
                for (int serial = 0; serial < 300; serial += 7)
                {
                    float depth = BitRenderer.DepthOf(edge, serial);

                    Assert.Greater(depth, 0f, "a bit at depth zero ties with the sparks, labels and sockets");
                    Assert.Less(depth, 1f, "a bit this far back is further than the order needs");
                }
            }
        }
    }
}
