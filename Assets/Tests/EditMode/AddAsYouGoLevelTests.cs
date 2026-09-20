using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The serial adder: one full adder, used once per cycle, with the carry kept in a register.
    /// </summary>
    /// <remarks>
    /// The chapter's last level, and the one that pays off both halves of the game. Carry the One
    /// built a full adder that adds three bits at once; this adds two whole numbers with the same
    /// five gates by feeding the carry back to itself, one bit per clock, least significant first.
    /// It is the difference between a circuit that is as wide as its numbers and one that is as
    /// long as them, which is the first real engineering trade in the game.
    ///
    /// The loop is three wires -- carry out through the OR, into the register, back to the gate --
    /// so the clock is three.
    /// </remarks>
    public class AddAsYouGoLevelTests
    {
        private static readonly Vector2Int ACell = new Vector2Int(-4, 1);
        private static readonly Vector2Int BCell = new Vector2Int(-4, -1);
        private static readonly Vector2Int FirstXor = new Vector2Int(-2, 0);
        private static readonly Vector2Int AndBoth = new Vector2Int(-2, 2);
        private static readonly Vector2Int AndCarry = new Vector2Int(0, -2);
        private static readonly Vector2Int OrCell = new Vector2Int(1, -1);
        private static readonly Vector2Int CarryRegister = new Vector2Int(2, -2);
        private static readonly Vector2Int SecondXor = new Vector2Int(1, 1);
        private static readonly Vector2Int SumCell = new Vector2Int(4, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("add-as-you-go", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped add-as-you-go.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// The adder. <paramref name="withTheCarry"/> false leaves the carry out entirely, which is
        /// the mistake: two bits added at a time, and every column that overflows forgotten.
        /// </summary>
        private static CircuitBlueprint Adder(bool withTheCarry = true)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(FirstXor, GateKind.Xor);
            blueprint.Place(SecondXor, GateKind.Xor);

            LevelTestFixtures.Wire(blueprint, ACell, FirstXor, toPort: 0);
            LevelTestFixtures.Wire(blueprint, BCell, FirstXor, toPort: 1);

            if (!withTheCarry)
            {
                LevelTestFixtures.Wire(blueprint, FirstXor, SumCell);
                return blueprint;
            }

            blueprint.Place(AndBoth, GateKind.And);
            blueprint.Place(AndCarry, GateKind.And);
            blueprint.Place(OrCell, GateKind.Or);
            blueprint.Place(CarryRegister, GateKind.Register);

            LevelTestFixtures.Wire(blueprint, ACell, AndBoth, toPort: 0);
            LevelTestFixtures.Wire(blueprint, BCell, AndBoth, toPort: 1);

            LevelTestFixtures.Wire(blueprint, FirstXor, AndCarry, toPort: 0);
            LevelTestFixtures.Wire(blueprint, CarryRegister, AndCarry, toPort: 1);

            LevelTestFixtures.Wire(blueprint, AndBoth, OrCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndCarry, OrCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, OrCell, CarryRegister);

            LevelTestFixtures.Wire(blueprint, FirstXor, SecondXor, toPort: 0);
            LevelTestFixtures.Wire(blueprint, CarryRegister, SecondXor, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SecondXor, SumCell);

            return blueprint;
        }

        [Test]
        public void TheSerialAdder_Passes()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Adder());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void WithoutTheCarry_ItAddsEachColumnAlone()
        {
            // Right for the first column and wrong from the second: the level's streams are chosen
            // so the first pair already overflows, and nothing downstream ever hears about it.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Adder(withTheCarry: false));

            Assert.IsFalse(verdict.IsPass, "an adder with no carry is not an adder");
        }

        [Test]
        public void ItsClockFitsTheCarryLoop()
        {
            // Out of the gate, through the OR, into the register and back: three wires, three ticks.
            Assert.AreEqual(3, _level.ClockPeriod);
        }

        [Test]
        public void ItStocksOneRegisterForOneBitOfCarry()
        {
            Assert.AreEqual(1, _level.BudgetFor(GateKind.Register));
        }

        [Test]
        public void TheSumIsOneBitPerColumn()
        {
            // Mealy: every answer is about the pair arriving now, so no bit comes out after the
            // last column. The carry that is left over stays in the register, as it does in the
            // hardware this mirrors.
            Assert.AreEqual(_level.VectorCount, _level.Expectations[0].Expected.Count);
        }
    }
}
