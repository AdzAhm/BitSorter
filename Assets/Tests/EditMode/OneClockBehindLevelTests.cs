using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The same detector as Rising edge, reporting one clock later: a Moore machine.
    /// </summary>
    /// <remarks>
    /// Mealy and Moore differ by exactly one register. A Mealy output is the gate's answer about
    /// the bit arriving now; a Moore output is a value the machine is holding, so it appears a
    /// clock after the bits that caused it -- and, because a register hands on what it was holding
    /// first, one more bit comes out than went in.
    ///
    /// That is the whole level: the same circuit the player built two levels ago, with its answer
    /// kept for a clock before it is reported. Wiring the gate straight to the bin is the Mealy
    /// reading, and it fails on the first cycle.
    /// </remarks>
    public class OneClockBehindLevelTests
    {
        private static readonly Vector2Int SourceCell = new Vector2Int(-3, 0);
        private static readonly Vector2Int Previous = new Vector2Int(-1, 1);
        private static readonly Vector2Int NotCell = new Vector2Int(0, 1);
        private static readonly Vector2Int AndCell = new Vector2Int(1, 0);
        private static readonly Vector2Int Report = new Vector2Int(2, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("one-clock-behind", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped one-clock-behind.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>The edge detector, with or without the register that holds its answer.</summary>
        private static CircuitBlueprint Detector(bool reportThroughARegister)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Previous, GateKind.Register);
            blueprint.Place(NotCell, GateKind.Not);
            blueprint.Place(AndCell, GateKind.And);

            LevelTestFixtures.Wire(blueprint, SourceCell, Previous);
            LevelTestFixtures.Wire(blueprint, Previous, NotCell);
            LevelTestFixtures.Wire(blueprint, NotCell, AndCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceCell, AndCell, toPort: 0, delay: 2);

            if (!reportThroughARegister)
            {
                LevelTestFixtures.Wire(blueprint, AndCell, OutCell);
                return blueprint;
            }

            blueprint.Place(Report, GateKind.Register);
            LevelTestFixtures.Wire(blueprint, AndCell, Report);
            LevelTestFixtures.Wire(blueprint, Report, OutCell);

            return blueprint;
        }

        [Test]
        public void ReportingThroughARegister_Passes()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(
                _level, Detector(reportThroughARegister: true));

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void ReportingStraightFromTheGate_IsACycleEarly()
        {
            // The Mealy reading of the same machine, and the answer the previous level wanted. Here
            // it is one bit short and a cycle out, which is the difference between the two stated
            // as a failure rather than as a definition.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(
                _level, Detector(reportThroughARegister: false));

            Assert.IsFalse(verdict.IsPass, "the bin wants the answer a clock after the gate has it");
        }

        [Test]
        public void ItStocksTwoRegisters_OneToRememberAndOneToReport()
        {
            Assert.AreEqual(2, _level.BudgetFor(GateKind.Register));
        }

        [Test]
        public void TheBinExpectsTheHeldAnswerFirst()
        {
            Assert.AreEqual(_level.VectorCount + 1, _level.Expectations[0].Expected.Count);
        }
    }
}
