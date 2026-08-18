using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The full adder, and the capstone of the six. Two outputs, two valid ways to join the carries,
    /// and a latency ceiling set to exactly the critical path.
    /// </summary>
    /// <remarks>
    /// <see cref="FullAdderTests"/> already proves this circuit works at the LogicCore level, wired by
    /// hand. What is tested here is the *level*: that the budget admits both carry joins, that the
    /// documented timing mistake is reachable and fails the right way, and that the latency ceiling is
    /// satisfiable by the intended solution rather than one tick under it.
    ///
    /// The pairing of delayBudget 5 with maxLatency 4 carries a lesson worth keeping: slack spent off
    /// the critical path is free, slack spent on it fails the level.
    /// </remarks>
    public class CarryTheOneLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 2);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, 0);
        private static readonly Vector2Int SourceCin = new Vector2Int(-4, -2);
        private static readonly Vector2Int SumSink = new Vector2Int(4, 1);
        private static readonly Vector2Int CoutSink = new Vector2Int(4, -1);

        private static readonly Vector2Int FirstSum = new Vector2Int(-2, 2);
        private static readonly Vector2Int FirstCarry = new Vector2Int(-2, 0);
        private static readonly Vector2Int SecondSum = new Vector2Int(0, 2);
        private static readonly Vector2Int SecondCarry = new Vector2Int(0, 0);
        private static readonly Vector2Int CarryJoin = new Vector2Int(2, -1);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("carry-the-one", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped carry-the-one.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// Two half adders plus a carry join. <paramref name="carryJoin"/> is the gate that merges the
        /// two carries, and <paramref name="cinDelay"/> is the one number the level is really about.
        /// </summary>
        private static CircuitBlueprint FullAdder(
            GateKind carryJoin = GateKind.Or, int cinDelay = 2, int sumSinkDelay = 1)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(FirstSum, GateKind.Xor);
            blueprint.Place(FirstCarry, GateKind.And);
            blueprint.Place(SecondSum, GateKind.Xor);
            blueprint.Place(SecondCarry, GateKind.And);
            blueprint.Place(CarryJoin, carryJoin);

            // First half adder: A and B fan out to both its gates, which fire at level 1.
            LevelTestFixtures.Wire(blueprint, SourceA, FirstSum, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, FirstSum, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA, FirstCarry, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, FirstCarry, toPort: 1);

            // Second half adder fires at level 2, so Cin is held back to arrive then too.
            LevelTestFixtures.Wire(blueprint, FirstSum, SecondSum, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceCin, SecondSum, toPort: 1, delay: cinDelay);
            LevelTestFixtures.Wire(blueprint, FirstSum, SecondCarry, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceCin, SecondCarry, toPort: 1, delay: cinDelay);

            // The first carry fires a level before the second, so it waits on the way in.
            LevelTestFixtures.Wire(blueprint, FirstCarry, CarryJoin, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, SecondCarry, CarryJoin, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SecondSum, SumSink, delay: sumSinkDelay);
            LevelTestFixtures.Wire(blueprint, CarryJoin, CoutSink);

            return blueprint;
        }

        // -----------------------------------------------------------------
        // Two ways to join the carries
        // -----------------------------------------------------------------

        [Test]
        public void TheStandardFullAdder_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, FullAdder());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void JoiningTheCarriesWithXor_AlsoSolves()
        {
            // Worth discovering rather than being told: the two carries are never both 1, because
            // A AND B being 1 forces A XOR B to 0. Where they cannot overlap, XOR and OR agree.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, FullAdder(carryJoin: GateKind.Xor));

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheTwoCarryJoins_ProduceIdenticalOutput()
        {
            // Proves the equivalence through the real simulator rather than by argument, across all
            // eight rows. If a future edit to the streams ever made the carries overlap, this breaks.
            CollectionAssert.AreEqual(
                CoutFrom(FullAdder(carryJoin: GateKind.Or)),
                CoutFrom(FullAdder(carryJoin: GateKind.Xor)),
                "OR and XOR must agree on every row, or one of the two answers is wrong");
        }

        private Bit[] CoutFrom(CircuitBlueprint blueprint)
        {
            BuiltCircuit built = CircuitBuilder.Build(_level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            var sink = (SinkNode)built.Simulation.GetNode(built.FixtureNodeIds["cout"]);
            var values = new Bit[sink.Received.Count];

            for (int i = 0; i < values.Length; i++)
                values[i] = sink.Received[i].Value;

            return values;
        }

        [Test]
        public void TheBudgetStocksTheThirdXor_SoBothJoinsFit()
        {
            // Two XORs build the sums. A third is what makes the XOR carry join reachable, and
            // budgeting two would quietly close that answer off.
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.Xor), 3);
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.And), 2);
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.Or), 1);
        }

        // -----------------------------------------------------------------
        // The documented mistake
        // -----------------------------------------------------------------

        [Test]
        public void FeedingCarryInTooEarly_DestroysBits()
        {
            // The natural error, and the one FullAdderTests documents: run Cin straight into the
            // second stage, forgetting it has to wait a tick for the first half adder's output.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, FullAdder(cinDelay: 1));

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("destroyed", verdict.Reason);
        }

        [Test]
        public void TheMistakeLosesResults_RatherThanGettingThemWrong()
        {
            // The property the whole model rests on. A mis-timed adder does not emit a full set of
            // plausible wrong answers -- it emits fewer answers, which is a far better clue.
            BuiltCircuit broken = CircuitBuilder.Build(_level, FullAdder(cinDelay: 1));
            LevelGrader.RunToCompletion(broken.Simulation, _level, broken.FixtureNodeIds);

            var sum = (SinkNode)broken.Simulation.GetNode(broken.FixtureNodeIds["sum"]);

            Assert.Greater(broken.Simulation.CorruptedCount, 0, "bits should be destroyed");
            Assert.Less(sum.Received.Count, _level.VectorCount,
                "results should go missing, not merely come out wrong");
        }

        // -----------------------------------------------------------------
        // Latency, and where slack is free
        // -----------------------------------------------------------------

        [Test]
        public void TheIntendedSolution_ExactlyMeetsTheLatencyCeiling()
        {
            // Set to the critical path rather than under it. Balancing pads short paths up to the
            // long one and never past it, so a correctly balanced adder costs nothing here.
            Assert.AreEqual(4, _level.MaxLatency);
            Assert.IsTrue(LevelTestFixtures.RunAndGrade(_level, FullAdder()).IsPass);
        }

        [Test]
        public void SlackSpentOffTheCriticalPath_IsFree()
        {
            // The subtle half of the pairing. Cout is the critical path at four ticks; the sum lands
            // a tick earlier. Padding the sum's wire costs budget but changes no maximum, so it still
            // passes -- which is what "critical path" means, made playable.
            CircuitBlueprint padded = FullAdder(sumSinkDelay: 2);

            Assert.Greater(padded.ExtraDelay(), FullAdder().ExtraDelay(), "it really does cost budget");
            Assert.LessOrEqual(padded.ExtraDelay(), _level.DelayBudget, "and the budget can afford it");
            Assert.IsTrue(LevelTestFixtures.RunAndGrade(_level, padded).IsPass,
                "padding a path that is not the longest must not fail the ceiling");
        }

        [Test]
        public void TheDelayBudget_HasSlackTheLatencyCeilingDoesNot()
        {
            // Two separate limits doing two separate jobs: the budget leaves room to make a mistake
            // and take it back, the ceiling does not let the mistake be left in on the long path.
            Assert.Greater(_level.DelayBudget, FullAdder().ExtraDelay(),
                "leave room for a wrong guess");
        }

        // -----------------------------------------------------------------
        // The function
        // -----------------------------------------------------------------

        [Test]
        public void BothSinksAreGradedAcrossTheWholeTable()
        {
            Assert.AreEqual(8, _level.VectorCount);
            Assert.AreEqual(2, _level.Expectations.Count, "sum and carry-out are both graded");

            Assert.AreEqual("01101001", ExpectationFor("sum"), "A xor B xor Cin");
            Assert.AreEqual("00010111", ExpectationFor("cout"), "the majority of the three");
        }

        private string ExpectationFor(string sinkId)
        {
            foreach (LevelExpectation expectation in _level.Expectations)
            {
                if (expectation.SinkId == sinkId)
                    return expectation.Values;
            }

            Assert.Fail($"no expectation for sink '{sinkId}'");
            return null;
        }

        [Test]
        public void SumAndCarry_DisagreeOnEnoughRowsToNeedBothCircuits()
        {
            // Guards against a stream set where one output happens to equal the other, which would
            // let half the circuit be shared and the lesson collapse.
            string sum = ExpectationFor("sum");
            string cout = ExpectationFor("cout");

            int differences = 0;
            for (int i = 0; i < sum.Length; i++)
            {
                if (sum[i] != cout[i])
                    differences++;
            }

            Assert.GreaterOrEqual(differences, 4,
                "the two outputs must be genuinely different functions");
        }

        // -----------------------------------------------------------------
        // The hint
        // -----------------------------------------------------------------

        [Test]
        public void TheHint_PointsAtTheStructureWithoutGivingTheTiming()
        {
            string hint = _level.Hint;

            Assert.IsNotEmpty(hint, "the level needs a hint");

            var words = new System.Collections.Generic.HashSet<string>();
            foreach (string word in hint.ToLowerInvariant().Split(
                         new[] { ' ', '.', ',', ';', ':', '-', '!', '?', '(', ')', '\'' },
                         System.StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(word);
            }

            // Naming the shape is a fair scaffold -- they built a half adder one level ago. Naming a
            // gate or a tick count is not: the timing is the puzzle.
            foreach (string giveaway in
                     new[] { "and", "or", "xor", "not", "delay", "wait", "tick", "ticks" })
            {
                Assert.IsFalse(words.Contains(giveaway),
                    $"the hint should not use the word '{giveaway}'. Hint: {hint}");
            }

            foreach (char c in hint)
            {
                Assert.IsFalse(char.IsDigit(c),
                    $"the hint should name no number. Hint: {hint}");
            }
        }
    }
}
