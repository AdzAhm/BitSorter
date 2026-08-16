using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The level format proved against something non-trivial: two sources fanning out to two gates,
    /// two sinks both carrying real expectations, and four test vectors. A one-wire tutorial exercises
    /// none of that, so if the format is going to buckle it will buckle here.
    /// </summary>
    /// <remarks>
    /// Loads the shipped half-adder.json rather than an inline copy. The point is to prove the file the
    /// game actually ships is solvable, so a copy would defeat the exercise.
    ///
    /// Worth knowing why this level cannot be failed by bad timing: every player wire is delay 1, so
    /// the four source-to-gate paths are balanced by construction and CorruptedCount stays 0. The
    /// unbalanced-delay hazard needs authored fixture wires or player-chosen delays, neither of which
    /// exists yet -- see the note under Level ideas in CLAUDE.md.
    /// </remarks>
    public class HalfAdderLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-3, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-3, -1);
        private static readonly Vector2Int SumSink = new Vector2Int(3, 1);
        private static readonly Vector2Int CarrySink = new Vector2Int(3, -1);
        private static readonly Vector2Int XorCell = new Vector2Int(0, 1);
        private static readonly Vector2Int AndCell = new Vector2Int(0, -1);

        private LevelDefinition _level;
        private CircuitBlueprint _blueprint;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("half-adder", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped half-adder.json is invalid: {result.Error}");

            _level = result.Level;
            _blueprint = new CircuitBlueprint();
        }

        /// <summary>
        /// The intended solution. Each source output feeds both gates, which is the fan-out the
        /// cell-addressed blueprint has to support: one port as the source of several wires.
        /// </summary>
        private void SolveHalfAdder(Vector2Int sumTarget, Vector2Int carryTarget)
        {
            _blueprint.Place(XorCell, GateKind.Xor);
            _blueprint.Place(AndCell, GateKind.And);

            LevelTestFixtures.Wire(_blueprint, SourceA, XorCell, toPort: 0);
            LevelTestFixtures.Wire(_blueprint, SourceB, XorCell, toPort: 1);
            LevelTestFixtures.Wire(_blueprint, SourceA, AndCell, toPort: 0);
            LevelTestFixtures.Wire(_blueprint, SourceB, AndCell, toPort: 1);

            LevelTestFixtures.Wire(_blueprint, XorCell, sumTarget);
            LevelTestFixtures.Wire(_blueprint, AndCell, carryTarget);
        }

        [Test]
        public void TheLevelDescribesFourVectorsAndTwoGradedSinks()
        {
            Assert.AreEqual(4, _level.VectorCount, "the four rows of the truth table");
            Assert.AreEqual(4, _level.Fixtures.Count, "two sources, two sinks");
            Assert.AreEqual(2, _level.Expectations.Count, "both sinks are graded");

            // Neither sink is a throwaway: both expect a bit from every vector, unlike the routing
            // level where one bin is graded as empty.
            for (int i = 0; i < _level.Expectations.Count; i++)
            {
                Assert.AreEqual(4, _level.Expectations[i].Expected.Count,
                    $"'{_level.Expectations[i].SinkId}' should expect one bit per vector");
            }

            Assert.AreEqual(1, _level.BudgetFor(GateKind.Xor), "one XOR");
            Assert.AreEqual(1, _level.BudgetFor(GateKind.And), "one AND");
            Assert.AreEqual(0, _level.BudgetFor(GateKind.Or), "and nothing else");
        }

        [Test]
        public void TheIntendedSolution_PassesAllFourVectors()
        {
            SolveHalfAdder(SumSink, CarrySink);

            BuiltCircuit built = CircuitBuilder.Build(_level, _blueprint);
            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
            Assert.AreEqual(0, built.Simulation.CorruptedCount,
                "all four source paths are delay 1, so nothing should collide");
        }

        [Test]
        public void FanOutReallyHappens()
        {
            // Guards the claim the test above rests on. If each source fed only one gate the solution
            // would still be six wires, but two of the gate inputs would never fill.
            SolveHalfAdder(SumSink, CarrySink);

            BuiltCircuit built = CircuitBuilder.Build(_level, _blueprint);
            var sourceA = (SourceNode)built.Simulation.GetNode(built.FixtureNodeIds["a"]);

            Assert.AreEqual(2, sourceA.Out(0).Edges.Count,
                "source A's single output port should carry two wires");
            Assert.AreEqual(6, built.Simulation.LiveEdgeCount, "four into the gates, two out");
        }

        [Test]
        public void SwappingTheTwoSinks_Fails()
        {
            // Sum and carry differ on the second vector: 1 versus 0. So the swap is caught at vector 1,
            // not vector 0, which is what makes this worth asserting.
            SolveHalfAdder(CarrySink, SumSink);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, _blueprint);

            Assert.IsFalse(verdict.IsPass, "carry is not sum");
            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(1, verdict.Vector, "the streams first disagree on vector 1");
            Assert.AreEqual("sum", verdict.SinkId, "sum is graded first, in file order");
        }

        [Test]
        public void OmittingOneOfTheFourSourceWires_FailsAsCorrupted()
        {
            // Worth reading carefully, because the obvious guess is wrong. Leaving the XOR one input
            // short does not merely leave the sum bin empty: the XOR can never fire, so source A's
            // four bits keep arriving at a port that nothing ever drains, and they collide with each
            // other. So the verdict is about corruption, and it carries no sink -- a pile-up inside the
            // circuit is not any one bin's fault.
            _blueprint.Place(XorCell, GateKind.Xor);
            _blueprint.Place(AndCell, GateKind.And);

            LevelTestFixtures.Wire(_blueprint, SourceA, XorCell, toPort: 0);
            LevelTestFixtures.Wire(_blueprint, SourceA, AndCell, toPort: 0);
            LevelTestFixtures.Wire(_blueprint, SourceB, AndCell, toPort: 1);
            LevelTestFixtures.Wire(_blueprint, XorCell, SumSink);
            LevelTestFixtures.Wire(_blueprint, AndCell, CarrySink);

            BuiltCircuit built = CircuitBuilder.Build(_level, _blueprint);
            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            Assert.IsNull(verdict.SinkId, "a collision inside the circuit belongs to no single sink");
            Assert.AreEqual(-1, verdict.Vector, "nor to a single vector");

            // Three, not four: stream "0011" arrives at one port over four ticks. The second bit
            // matches the waiting one so only the arrival dies (+1); the third differs so both die and
            // the port is poisoned (+2); the fourth then lands in a port that is empty again (+0).
            // CorruptedCount counts destroyed bits, not collision events.
            Assert.AreEqual(3, built.Simulation.CorruptedCount, "bits destroyed, not collisions counted");
        }

        [Test]
        public void WiringBothGatesToOneSink_Fails()
        {
            // Two outputs into one input port on paths of equal length: both land on the same tick and
            // collide whenever they disagree, which for a half adder is every vector where sum and
            // carry differ.
            SolveHalfAdder(SumSink, SumSink);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, _blueprint);

            Assert.IsFalse(verdict.IsPass, "the carry bin is empty and the sum bin is a pile-up");
        }
    }
}
