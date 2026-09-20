using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The first level with feedback, and therefore the first with a clock.
    /// </summary>
    /// <remarks>
    /// A register whose next value comes from its own output is a loop, and a loop cannot keep up
    /// with a bit every tick: the shortest one is two wires, so the state is still on its way round
    /// when the next input lands. That is what the clock is for, and why this level is the one that
    /// introduces it.
    ///
    /// The two mistakes it is built around are both about the loop. Making it longer than the clock
    /// -- padding a wire inside it -- means the state comes back late and the inputs pile up. Taking
    /// the output from the register instead of the gate is the Moore/Mealy confusion, and it shows
    /// up as the whole answer arriving a cycle late.
    /// </remarks>
    public class FlipOnOneLevelTests
    {
        private static readonly Vector2Int SourceCell = new Vector2Int(-3, 0);
        private static readonly Vector2Int XorCell = new Vector2Int(0, 0);
        private static readonly Vector2Int RegisterCell = new Vector2Int(0, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("flip-on-one", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped flip-on-one.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// The toggle: the kept bit and the arriving bit meet at the gate, and the answer is both
        /// the bin's and the register's next value.
        /// </summary>
        private static CircuitBlueprint Toggle(int intoRegister = 1, bool outputFromRegister = false)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(XorCell, GateKind.Xor);
            blueprint.Place(RegisterCell, GateKind.Register);

            LevelTestFixtures.Wire(blueprint, SourceCell, XorCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, RegisterCell, XorCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorCell, RegisterCell, delay: intoRegister);
            LevelTestFixtures.Wire(blueprint, outputFromRegister ? RegisterCell : XorCell, OutCell);

            return blueprint;
        }

        [Test]
        public void TheToggle_WithTheLoopInsideItsClock_Passes()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Toggle());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void ALoopLongerThanTheClock_DestroysBits()
        {
            // One extra tick inside the loop, which is all it takes: the state comes back after the
            // next input has already arrived, and the inputs queue up behind it until two meet.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Toggle(intoRegister: 2));

            Assert.IsFalse(verdict.IsPass, "a three-tick loop cannot run on a two-tick clock");
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TakingTheOutputFromTheRegister_IsACycleBehind()
        {
            // The Moore reading of the same machine: the register shows the state, which is last
            // cycle's answer. Right values, one cycle late, and one bit too many.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(
                _level, Toggle(outputFromRegister: true));

            Assert.IsFalse(verdict.IsPass, "the register shows the answer a cycle after the gate does");
        }

        [Test]
        public void ItRunsOnAClock()
        {
            // The first level that does. Its loop is two wires, so two ticks is exactly enough --
            // tight on purpose, because a loop with room to spare teaches nothing about the clock.
            Assert.IsTrue(_level.HasClock);
            Assert.AreEqual(2, _level.ClockPeriod);
        }

        [Test]
        public void ItStocksTheLoopAndNothingSpare()
        {
            Assert.AreEqual(2, _level.Budget.Count);
            Assert.AreEqual(1, _level.BudgetFor(GateKind.Register));
            Assert.AreEqual(1, _level.BudgetFor(GateKind.Xor));
        }
    }
}
