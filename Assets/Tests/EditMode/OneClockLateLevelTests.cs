using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The level that introduces the register, and the failure it is built to produce first.
    /// </summary>
    /// <remarks>
    /// The whole lesson is that a register hands on what it was given one clock later, and that it
    /// starts out holding a 0 -- so the bin receives one bit more than the level has vectors, and
    /// the first of them belongs to nobody's input. A player who wires the source straight to the
    /// bin gets every value right and still fails, because the first bit is missing. That is the
    /// failure this level exists to produce.
    ///
    /// Loads the shipped file rather than an inline copy: the point is that the level the game
    /// ships is solvable, and instructively wrong before it is solved.
    /// </remarks>
    public class OneClockLateLevelTests
    {
        private static readonly Vector2Int SourceCell = new Vector2Int(-3, 0);
        private static readonly Vector2Int RegisterCell = new Vector2Int(0, 0);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("one-clock-late", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped one-clock-late.json is invalid: {result.Error}");

            _level = result.Level;
        }

        // -----------------------------------------------------------------
        // The intended solution
        // -----------------------------------------------------------------

        [Test]
        public void ARegisterBetweenTheSourceAndTheBin_Passes()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(RegisterCell, GateKind.Register);
            LevelTestFixtures.Wire(blueprint, SourceCell, RegisterCell);
            LevelTestFixtures.Wire(blueprint, RegisterCell, OutCell);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // The failure it is built to produce
        // -----------------------------------------------------------------

        [Test]
        public void WiringStraightPast_IsOneBitShort()
        {
            // Every value is right and in the right order. What is missing is the bit the register
            // was holding before the run began, which is the entire point of the level.
            var blueprint = new CircuitBlueprint();
            LevelTestFixtures.Wire(blueprint, SourceCell, OutCell);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

            Assert.IsFalse(verdict.IsPass, "a straight wire cannot produce the register's own bit");
            Assert.AreEqual(RunOutcome.MissingOutput, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TwoRegistersInARow_AreOneBitTooMany()
        {
            // The other way to get it wrong: each register adds a bit of its own, so a second one
            // overshoots by exactly as much as none undershoots.
            var blueprint = new CircuitBlueprint();
            blueprint.Place(RegisterCell, GateKind.Register);
            blueprint.Place(new Vector2Int(1, 0), GateKind.Register);

            LevelTestFixtures.Wire(blueprint, SourceCell, RegisterCell);
            LevelTestFixtures.Wire(blueprint, RegisterCell, new Vector2Int(1, 0));
            LevelTestFixtures.Wire(blueprint, new Vector2Int(1, 0), OutCell);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

            Assert.IsFalse(verdict.IsPass, "two registers put two of their own bits in front");
            Assert.AreEqual(RunOutcome.ExtraOutput, verdict.Outcome, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // What the level stocks
        // -----------------------------------------------------------------

        [Test]
        public void ItStocksARegisterAndNothingElse()
        {
            // The first level of the chapter teaches one thing. A gate on the parts list would be a
            // second thing to wonder about.
            Assert.AreEqual(1, _level.Budget.Count, "the parts list should hold registers alone");
            Assert.AreEqual(GateKind.Register, _level.Budget[0].Kind);
        }

        [Test]
        public void ItExpectsOneBitMoreThanItHasVectors()
        {
            // The shape of every register level: the bit the register started with comes out in
            // front of the stream.
            Assert.AreEqual(_level.VectorCount + 1, _level.Expectations[0].Expected.Count);
        }

        [Test]
        public void ItRunsAVectorEveryTick()
        {
            // No feedback here, so no clock is needed yet. The clock arrives with the first loop,
            // which is a level the player has not reached at this point.
            Assert.AreEqual(1, _level.ClockPeriod);
        }
    }
}
