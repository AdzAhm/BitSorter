using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The sandbox panel's steppers and bit toggles.
    /// </summary>
    /// <remarks>
    /// Free play is the one screen with no verdict, no budget and no goal to push back, so the panel
    /// itself is the only thing stopping a player reaching a setup they cannot leave. The property
    /// worth protecting is not that any particular number is right -- it is that from every value the
    /// player can reach, at least one button is still live.
    /// </remarks>
    public class SandboxRulesTests
    {
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        // -----------------------------------------------------------------
        // The stranding property
        // -----------------------------------------------------------------

        [Test]
        public void EveryStepper_CanAlwaysBeMoved()
        {
            // The failure this guards: two dead buttons and a number the player cannot change. In the
            // sandbox that means a setup they cannot fix, with no verdict and no error to explain it.
            int capacity = SandboxLevel.Capacity(Board);

            AssertAlwaysMovable(SandboxRules.Sources(capacity), "sources");
            AssertAlwaysMovable(SandboxRules.Sinks(capacity), "sinks");
            AssertAlwaysMovable(SandboxRules.Vectors(), "vectors");
        }

        private static void AssertAlwaysMovable(StepRange range, string what)
        {
            Assert.Less(range.Min, range.Max, $"{what} has no room to move at all");

            for (int value = range.Min; value <= range.Max; value++)
            {
                Assert.IsTrue(range.CanMove(value),
                    $"{what} is stuck at {value}: both buttons are dead");
            }
        }

        [Test]
        public void TheEndsOfARange_DisableExactlyOneButton()
        {
            StepRange range = SandboxRules.Vectors();

            Assert.IsFalse(range.CanDecrease(range.Min), "nothing below the floor");
            Assert.IsTrue(range.CanIncrease(range.Min), "but the way up must stay open");

            Assert.IsTrue(range.CanDecrease(range.Max), "the way down must stay open");
            Assert.IsFalse(range.CanIncrease(range.Max), "nothing above the ceiling");
        }

        [Test]
        public void ABoardAlwaysHasRoomForAtLeastOneFixture()
        {
            // Capacity feeds both the sources and the sinks range, so a board that could report zero
            // would strand both steppers at once with no way to add anything.
            for (int halfHeight = 0; halfHeight <= 4; halfHeight++)
            {
                int capacity = SandboxLevel.Capacity(new Vector2Int(4, halfHeight));

                Assert.GreaterOrEqual(capacity, 1, $"a board {halfHeight} half-tall has no room");
                Assert.IsTrue(SandboxRules.Sources(capacity).CanIncrease(0),
                    "the player must always be able to add a first source");
            }
        }

        // -----------------------------------------------------------------
        // The floors, which are not the same for all three
        // -----------------------------------------------------------------

        [Test]
        public void SourcesAndSinksMayReachZero()
        {
            // Not a mistake to prevent: an empty board is a legitimate step while rearranging, and
            // SandboxLevel.Warning says plainly why nothing happens.
            int capacity = SandboxLevel.Capacity(Board);

            Assert.AreEqual(0, SandboxRules.Sources(capacity).Min);
            Assert.AreEqual(0, SandboxRules.Sinks(capacity).Min);

            Assert.IsFalse(SandboxRules.Sources(capacity).CanDecrease(0));
            Assert.AreEqual(0, SandboxRules.Sources(capacity).Clamp(-3));
        }

        [Test]
        public void VectorsMayNot()
        {
            // A stream of length zero emits nothing, leaving the sources drawn on the board and the
            // run silent with nothing to say why. So this floor is one, not zero.
            StepRange vectors = SandboxRules.Vectors();

            Assert.AreEqual(SandboxConfig.MinVectors, vectors.Min);
            Assert.GreaterOrEqual(vectors.Min, 1);

            Assert.IsFalse(vectors.CanDecrease(SandboxConfig.MinVectors));
            Assert.AreEqual(SandboxConfig.MinVectors, vectors.Clamp(0));
            Assert.AreEqual(SandboxConfig.MinVectors, vectors.Clamp(-1));
        }

        [Test]
        public void ZeroAndUnlimitedAreNotConfused()
        {
            // CLAUDE.md's trap: zero and unlimited are opposites that both look falsy. A count of zero
            // here means "none on the board", and must never be read as "no limit".
            int capacity = SandboxLevel.Capacity(Board);

            Assert.AreEqual(0, SandboxRules.Sinks(capacity).Clamp(0),
                "zero sinks is a real setting, not an absent one");

            Assert.AreEqual(capacity, SandboxRules.Sinks(capacity).Max,
                "the ceiling is the board's capacity, never -1");
        }

        // -----------------------------------------------------------------
        // Clamping agrees with the buttons
        // -----------------------------------------------------------------

        [Test]
        public void ClampingNeverContradictsTheEnableRule()
        {
            // The two used to be written separately -- once when enabling a button and again when
            // applying the result. If they disagree the player gets a live button that does nothing.
            int capacity = SandboxLevel.Capacity(Board);

            foreach (StepRange range in new[]
                     { SandboxRules.Sources(capacity), SandboxRules.Sinks(capacity), SandboxRules.Vectors() })
            {
                for (int value = range.Min; value <= range.Max; value++)
                {
                    if (range.CanIncrease(value))
                        Assert.AreEqual(value + 1, range.Clamp(value + 1), "a live + button did nothing");

                    if (range.CanDecrease(value))
                        Assert.AreEqual(value - 1, range.Clamp(value - 1), "a live - button did nothing");
                }
            }
        }

        [Test]
        public void ClampingHoldsAValueInsideTheRange()
        {
            StepRange range = SandboxRules.Sources(5);

            Assert.AreEqual(0, range.Clamp(-10));
            Assert.AreEqual(5, range.Clamp(99));
            Assert.AreEqual(3, range.Clamp(3));
        }

        // -----------------------------------------------------------------
        // Flipping a bit
        // -----------------------------------------------------------------

        [Test]
        public void FlippingInvertsOneBitAndLeavesTheRest()
        {
            Assert.AreEqual("1010", SandboxRules.Flip("0010", 0));
            Assert.AreEqual("0110", SandboxRules.Flip("0010", 1));
            Assert.AreEqual("0000", SandboxRules.Flip("0010", 2));
            Assert.AreEqual("0011", SandboxRules.Flip("0010", 3));
        }

        [Test]
        public void FlippingTwiceReturnsTheStream()
        {
            const string stream = "1101";

            Assert.AreEqual(stream, SandboxRules.Flip(SandboxRules.Flip(stream, 2), 2));
        }

        [Test]
        public void AFlipOutsideTheStreamChangesNothing()
        {
            // The panel rebuilds its rows on every edit, so a click can only land on a bit that was
            // drawn -- but a listener firing after a rebuild should do nothing rather than throw.
            Assert.AreEqual("0010", SandboxRules.Flip("0010", -1));
            Assert.AreEqual("0010", SandboxRules.Flip("0010", 4));
            Assert.AreEqual("0010", SandboxRules.Flip("0010", 99));

            Assert.IsNull(SandboxRules.Flip(null, 0));
        }

        [Test]
        public void AFlippedStreamIsStillAValidStream()
        {
            // What the panel writes back into the config has to survive normalisation unchanged, or
            // the bit the player clicked would visibly snap back.
            string flipped = SandboxRules.Flip("0000", 2);

            Assert.AreEqual(flipped, SandboxConfig.NormaliseStream(flipped, flipped.Length));
        }
    }
}
