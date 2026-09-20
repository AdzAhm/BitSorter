using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Two bits of state, and the carry between them.
    /// </summary>
    /// <remarks>
    /// The first machine whose state is wider than one bit. The low bit toggles on every 1 that
    /// arrives; the high bit toggles only when the low one rolls over, which is what the AND is
    /// for -- and getting that condition wrong is the mistake this level exists to produce, because
    /// a counter that toggles its high bit on the low bit alone counts in a pattern that looks
    /// almost right for the first two cycles.
    /// </remarks>
    public class CountTheOnesLevelTests
    {
        private static readonly Vector2Int SourceCell = new Vector2Int(-4, 0);
        private static readonly Vector2Int LowXor = new Vector2Int(-1, -1);
        private static readonly Vector2Int LowRegister = new Vector2Int(1, -1);
        private static readonly Vector2Int Carry = new Vector2Int(-1, 1);
        private static readonly Vector2Int HighXor = new Vector2Int(1, 1);
        private static readonly Vector2Int HighRegister = new Vector2Int(3, 1);
        private static readonly Vector2Int LowOut = new Vector2Int(4, -1);
        private static readonly Vector2Int HighOut = new Vector2Int(4, 1);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("count-the-ones", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped count-the-ones.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// The counter. <paramref name="carryOnTheLowBitAlone"/> builds the mistake instead: the
        /// high bit toggling whenever the low bit is 1, rather than when it is 1 and rolling over.
        /// </summary>
        private static CircuitBlueprint Counter(bool carryOnTheLowBitAlone = false)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(LowXor, GateKind.Xor);
            blueprint.Place(LowRegister, GateKind.Register);
            blueprint.Place(HighXor, GateKind.Xor);
            blueprint.Place(HighRegister, GateKind.Register);

            LevelTestFixtures.Wire(blueprint, SourceCell, LowXor, toPort: 0);
            LevelTestFixtures.Wire(blueprint, LowRegister, LowXor, toPort: 1);
            LevelTestFixtures.Wire(blueprint, LowXor, LowRegister);
            LevelTestFixtures.Wire(blueprint, LowRegister, LowOut);

            if (carryOnTheLowBitAlone)
            {
                // Straight from the low bit into the high bit's toggle, two wires long so it still
                // arrives when the high bit's gate is looking.
                LevelTestFixtures.Wire(blueprint, LowRegister, HighXor, toPort: 1, delay: 2);
            }
            else
            {
                blueprint.Place(Carry, GateKind.And);
                LevelTestFixtures.Wire(blueprint, SourceCell, Carry, toPort: 0);
                LevelTestFixtures.Wire(blueprint, LowRegister, Carry, toPort: 1);
                LevelTestFixtures.Wire(blueprint, Carry, HighXor, toPort: 1);
            }

            LevelTestFixtures.Wire(blueprint, HighRegister, HighXor, toPort: 0);
            LevelTestFixtures.Wire(blueprint, HighXor, HighRegister);
            LevelTestFixtures.Wire(blueprint, HighRegister, HighOut);

            return blueprint;
        }

        [Test]
        public void TheCounter_Passes()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Counter());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void CarryingOnTheLowBitAlone_CountsWrong()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(
                _level, Counter(carryOnTheLowBitAlone: true));

            Assert.IsFalse(verdict.IsPass, "the high bit must wait for the low one to roll over");
        }

        [Test]
        public void ItStocksTwoRegisters()
        {
            // Two bits of state, and the parts list is where the player reads that: four counts
            // need two registers, and the budget says so without a sentence having to.
            Assert.AreEqual(2, _level.BudgetFor(GateKind.Register));
        }

        [Test]
        public void BothBinsShowWhatIsHeld()
        {
            foreach (LevelExpectation expectation in _level.Expectations)
            {
                Assert.AreEqual(_level.VectorCount + 1, expectation.Expected.Count,
                    $"{expectation.SinkId} should show the state it started in as well");
            }
        }
    }
}
