using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The level where a bit is compared with the one before it, and the timing mistake that shape
    /// makes almost inevitable.
    /// </summary>
    /// <remarks>
    /// A register hands its bit on a clock **early**: what leaves it on cycle k is what went in on
    /// cycle k-1. So the path through it -- register, inverter, gate -- arrives at the same moment
    /// as a direct wire two ticks long, and the obvious direct wire of one tick is a cycle out.
    ///
    /// That is the designed mistake, and it fails the way this game always says timing is wrong: a
    /// bit arrives at a port that is still holding the last one, and both are lost.
    /// </remarks>
    public class RisingEdgeLevelTests
    {
        private static readonly Vector2Int SourceCell = new Vector2Int(-3, 0);
        private static readonly Vector2Int RegisterCell = new Vector2Int(-1, 1);
        private static readonly Vector2Int NotCell = new Vector2Int(0, 1);
        private static readonly Vector2Int AndCell = new Vector2Int(1, 0);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("rising-edge", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped rising-edge.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// The detector, with the direct wire's length as the one thing a player gets wrong.
        /// </summary>
        private static CircuitBlueprint Detector(int directDelay)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(RegisterCell, GateKind.Register);
            blueprint.Place(NotCell, GateKind.Not);
            blueprint.Place(AndCell, GateKind.And);

            LevelTestFixtures.Wire(blueprint, SourceCell, RegisterCell);
            LevelTestFixtures.Wire(blueprint, RegisterCell, NotCell);
            LevelTestFixtures.Wire(blueprint, NotCell, AndCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceCell, AndCell, toPort: 0, delay: directDelay);
            LevelTestFixtures.Wire(blueprint, AndCell, OutCell);

            return blueprint;
        }

        [Test]
        public void TheDetector_WithTheDirectWirePadded_Passes()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Detector(directDelay: 2));

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheDetector_WithTheObviousDirectWire_DestroysBits()
        {
            // Every gate is right and every wire is connected where it belongs. The stream simply
            // arrives at the gate a cycle before the register's bit does.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Detector(directDelay: 1));

            Assert.IsFalse(verdict.IsPass, "the two paths cannot be a cycle apart and survive");
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TheBudgetPaysForExactlyThePaddingNeeded()
        {
            // One tick, on the one wire that needs it. No slack, so a wrong guess has to be taken
            // back rather than absorbed -- the same choice The Slow Lane makes.
            Assert.IsTrue(_level.HasDelayBudget);
            Assert.AreEqual(1, _level.DelayBudget);
        }

        [Test]
        public void ItExpectsOneBitPerVector()
        {
            // Nothing here reads the register alone: every output is a gate's answer about the
            // current bit, so the register's own bit never reaches the bin. No tail.
            Assert.AreEqual(_level.VectorCount, _level.Expectations[0].Expected.Count);
        }
    }
}
