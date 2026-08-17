using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The De Morgan level: build a NAND on a board that stocks no NAND. Both forms must pass, and
    /// the half-applied one must fail in the specific way the design promises.
    /// </summary>
    /// <remarks>
    /// Two solutions are asserted rather than one, on purpose. A level solvable exactly one way is
    /// not teaching a law, it is teaching a recipe -- so if a future edit to the budget quietly
    /// closes one of these routes off, that is a regression and this file is where it surfaces.
    ///
    /// Wiring is fixed at delay 1 by the level itself, so unlike balance-the-paths there is nothing
    /// here the player can re-time. A mistake has to be fixed by changing the shape of the circuit.
    /// </remarks>
    public class LongWayRoundLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-3, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-3, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);

        private static readonly Vector2Int UpperCell = new Vector2Int(0, 1);
        private static readonly Vector2Int LowerCell = new Vector2Int(0, -1);
        private static readonly Vector2Int JoinCell = new Vector2Int(2, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("the-long-way-round", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped the-long-way-round.json is invalid: {result.Error}");

            _level = result.Level;
        }

        // -----------------------------------------------------------------
        // The two intended solutions
        // -----------------------------------------------------------------

        /// <summary>NOT (A AND B): the AND fires at level 1, the NOT at level 2, the bin at 3.</summary>
        private static CircuitBlueprint AndThenNot()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(UpperCell, GateKind.And);
            blueprint.Place(JoinCell, GateKind.Not);

            LevelTestFixtures.Wire(blueprint, SourceA, UpperCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, UpperCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, UpperCell, JoinCell);
            LevelTestFixtures.Wire(blueprint, JoinCell, OutCell);

            return blueprint;
        }

        /// <summary>NOT A OR NOT B: both inverters at level 1, the OR at 2, the bin at 3.</summary>
        private static CircuitBlueprint TwoInverters()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(UpperCell, GateKind.Not);
            blueprint.Place(LowerCell, GateKind.Not);
            blueprint.Place(JoinCell, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceA, UpperCell);
            LevelTestFixtures.Wire(blueprint, SourceB, LowerCell);
            LevelTestFixtures.Wire(blueprint, UpperCell, JoinCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, LowerCell, JoinCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, JoinCell, OutCell);

            return blueprint;
        }

        [Test]
        public void TheAndThenNotForm_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, AndThenNot());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheTwoInverterForm_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, TwoInverters());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void BothFormsFitTheBudget()
        {
            // The budget is what makes this a De Morgan level rather than a single-answer one. If a
            // future trim closes either route off, the law stops being demonstrable.
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.And), 1, "the AND-then-NOT form needs an AND");
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.Not), 2, "the two-inverter form needs two NOTs");
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.Or), 1, "the two-inverter form needs an OR");
        }

        [Test]
        public void BothFormsSettleOnTheSameTick()
        {
            // Three levels deep either way. Worth pinning: it is why neither route needs padding, and
            // why this level can fix its wiring at delay 1 without making one of the two unbuildable.
            Assert.AreEqual(LatencyOf(AndThenNot()), LatencyOf(TwoInverters()),
                "the two forms should cost the same, or the level quietly prefers one");
        }

        private int LatencyOf(CircuitBlueprint blueprint)
        {
            BuiltCircuit built = CircuitBuilder.Build(_level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            var sink = (SinkNode)built.Simulation.GetNode(built.FixtureNodeIds["out"]);

            Assert.Greater(sink.Received.Count, 0, "nothing reached the bin");
            return sink.Received[0].Tick;   // vector 0 leaves on tick 0, so this is the latency
        }

        // -----------------------------------------------------------------
        // The mistake the level is built to allow
        // -----------------------------------------------------------------

        /// <summary>
        /// De Morgan applied to one input only: B is inverted, A is wired straight into the OR. The
        /// two paths into the OR are a level apart, so the second vector's A bit lands on a port that
        /// still holds the first's.
        /// </summary>
        private static CircuitBlueprint HalfAppliedDeMorgan()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(LowerCell, GateKind.Not);
            blueprint.Place(JoinCell, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceA, JoinCell, toPort: 0);   // straight in, level 1
            LevelTestFixtures.Wire(blueprint, SourceB, LowerCell);
            LevelTestFixtures.Wire(blueprint, LowerCell, JoinCell, toPort: 1); // via a gate, level 2
            LevelTestFixtures.Wire(blueprint, JoinCell, OutCell);

            return blueprint;
        }

        [Test]
        public void TheHalfAppliedDeMorgan_DestroysBitsRatherThanAnsweringWrongly()
        {
            // The whole reason this level exists. Forgetting one inverter is not a logic slip that
            // yields a wrong truth table -- it is a timing fault, and the game says so by eating a bit.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, HalfAppliedDeMorgan());

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("destroyed", verdict.Reason);
        }

        [Test]
        public void TheHalfAppliedDeMorgan_CannotBeRescuedByReTiming()
        {
            // maxWireDelay 1 is the level's teeth. If it ever loosens, the player can paper over the
            // missing inverter by lengthening a wire and never learns the law.
            Assert.AreEqual(1, _level.MaxWireDelay,
                "this level fixes its wiring on purpose -- the fix is a gate, not a tick");
        }

        // -----------------------------------------------------------------
        // The premise
        // -----------------------------------------------------------------

        [Test]
        public void TheLevelStocksNoNand()
        {
            // Stating the obvious, because the obvious is the entire level. A NAND on the parts list
            // turns this into a one-gate wiring exercise.
            Assert.IsFalse(_level.Offers(GateKind.Nand), "a NAND would make the puzzle vanish");
            Assert.IsFalse(_level.Offers(GateKind.Nor), "NOR is functionally complete too");
        }

        [Test]
        public void TheVectorsCoverTheWholeTruthTable()
        {
            // Four vectors, and the answer differs on exactly one of them. Fewer rows and a circuit
            // that is merely nearly right would pass.
            Assert.AreEqual(4, _level.VectorCount);

            LevelExpectation expectation = _level.Expectations[0];
            Assert.AreEqual("1110", expectation.Values, "NAND's truth table, in AB order 00 01 10 11");
        }

        [Test]
        public void MoreThanOneVector_SoATimingFaultIsReachable()
        {
            // A single-vector level cannot corrupt: the early bit simply waits and the circuit fires
            // late. The designed mistake needs a second vector to collide with the first.
            Assert.Greater(_level.VectorCount, 1,
                "with one vector the half-applied form would pass and the level would teach nothing");
        }

        // -----------------------------------------------------------------
        // The hint
        // -----------------------------------------------------------------

        [Test]
        public void TheHint_NamesTheGoalButNeitherRoute()
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

            // Naming the goal is fine -- "build a NAND" is the brief. Naming a route is not: the
            // player is meant to derive De Morgan, not read it here.
            foreach (string giveaway in
                     new[] { "morgan", "invert", "inverter", "inverters", "and", "or", "not" })
            {
                Assert.IsFalse(words.Contains(giveaway),
                    $"the hint should not use the word '{giveaway}' -- it hands over a route. Hint: {hint}");
            }

            Assert.IsTrue(words.Contains("nand"), $"the hint should still name the goal. Hint: {hint}");
        }
    }
}
