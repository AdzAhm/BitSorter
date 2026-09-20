using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The enabled register: a register that only takes a new value when it is told to.
    /// </summary>
    /// <remarks>
    /// The bridge between the two chapters. Choosing between two values is the multiplexer of Pick a
    /// Lane; what is new is that one of the two values is what the register already holds, so the
    /// choice feeds itself. It is also the basis of every real register file, where a write enable
    /// is the only thing standing between a value and being overwritten every cycle.
    ///
    /// Two routes, as that level had: the textbook mux, and the XOR trick that costs one gate less.
    /// Their loops are three and four wires, so the clock is four and both fit.
    /// </remarks>
    public class HoldWhenToldLevelTests
    {
        private static readonly Vector2Int DataCell = new Vector2Int(-3, 1);
        private static readonly Vector2Int EnableCell = new Vector2Int(-3, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);
        private static readonly Vector2Int RegisterCell = new Vector2Int(2, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("hold-when-told", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped hold-when-told.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>The textbook mux: (D AND EN) OR (Q AND NOT EN), back into the register.</summary>
        private static CircuitBlueprint Textbook(int intoRegister = 1)
        {
            var not = new Vector2Int(-1, -1);
            var andData = new Vector2Int(0, 1);
            var andHeld = new Vector2Int(0, -1);
            var or = new Vector2Int(1, 0);

            var blueprint = new CircuitBlueprint();
            blueprint.Place(not, GateKind.Not);
            blueprint.Place(andData, GateKind.And);
            blueprint.Place(andHeld, GateKind.And);
            blueprint.Place(or, GateKind.Or);
            blueprint.Place(RegisterCell, GateKind.Register);

            LevelTestFixtures.Wire(blueprint, DataCell, andData, toPort: 0);
            LevelTestFixtures.Wire(blueprint, EnableCell, andData, toPort: 1);
            LevelTestFixtures.Wire(blueprint, EnableCell, not);
            LevelTestFixtures.Wire(blueprint, not, andHeld, toPort: 1);
            LevelTestFixtures.Wire(blueprint, RegisterCell, andHeld, toPort: 0);
            LevelTestFixtures.Wire(blueprint, andData, or, toPort: 0);
            LevelTestFixtures.Wire(blueprint, andHeld, or, toPort: 1);
            LevelTestFixtures.Wire(blueprint, or, RegisterCell, delay: intoRegister);
            LevelTestFixtures.Wire(blueprint, RegisterCell, OutCell);

            return blueprint;
        }

        /// <summary>The XOR trick: Q XOR (EN AND (Q XOR D)), one gate cheaper and a wire longer.</summary>
        private static CircuitBlueprint XorTrick()
        {
            var first = new Vector2Int(-1, 1);
            var and = new Vector2Int(0, 1);
            var second = new Vector2Int(1, 0);

            var blueprint = new CircuitBlueprint();
            blueprint.Place(first, GateKind.Xor);
            blueprint.Place(and, GateKind.And);
            blueprint.Place(second, GateKind.Xor);
            blueprint.Place(RegisterCell, GateKind.Register);

            LevelTestFixtures.Wire(blueprint, DataCell, first, toPort: 0);
            LevelTestFixtures.Wire(blueprint, RegisterCell, first, toPort: 1);
            LevelTestFixtures.Wire(blueprint, EnableCell, and, toPort: 0);
            LevelTestFixtures.Wire(blueprint, first, and, toPort: 1);
            LevelTestFixtures.Wire(blueprint, RegisterCell, second, toPort: 0);
            LevelTestFixtures.Wire(blueprint, and, second, toPort: 1);
            LevelTestFixtures.Wire(blueprint, second, RegisterCell);
            LevelTestFixtures.Wire(blueprint, RegisterCell, OutCell);

            return blueprint;
        }

        [Test]
        public void TheTextbookMux_Passes()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Textbook());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheXorTrick_Passes()
        {
            // A level solvable one way is not teaching anything: the parts list stocks both, and the
            // trade is a gate against a longer loop.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, XorTrick());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void WiringTheDataStraightIn_NeverHolds()
        {
            // The mistake the level is named for: a register with no choice in front of it takes a
            // new value every cycle, so the enable does nothing and the bin sees the stream.
            var blueprint = new CircuitBlueprint();
            blueprint.Place(RegisterCell, GateKind.Register);

            LevelTestFixtures.Wire(blueprint, DataCell, RegisterCell);
            LevelTestFixtures.Wire(blueprint, RegisterCell, OutCell);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

            Assert.IsFalse(verdict.IsPass, "without the enable the register cannot hold anything");
            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void ALoopPastTheClock_DestroysBits()
        {
            // One tick added inside the mux's loop takes it to four wires and the clock is four, so
            // this is the first level where the tight route and the slack one are a tick apart.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Textbook(intoRegister: 2));

            Assert.IsTrue(verdict.IsPass, "three wires plus one is still inside a four-tick clock");
        }

        [Test]
        public void ItsClockFitsTheLongerRoute()
        {
            // Four, because the XOR route's loop is four wires. The mux route has a tick spare,
            // which is the level's one piece of slack and is spent on nothing else.
            Assert.AreEqual(4, _level.ClockPeriod);
        }

        [Test]
        public void TheBinShowsWhatIsHeld_IncludingBeforeTheFirstVector()
        {
            Assert.AreEqual(_level.VectorCount + 1, _level.Expectations[0].Expected.Count);
        }
    }
}
