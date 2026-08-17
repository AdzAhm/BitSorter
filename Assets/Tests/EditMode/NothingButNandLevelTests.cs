using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The functional completeness level: XOR out of NANDs and nothing else.
    /// </summary>
    /// <remarks>
    /// Budgeted at six rather than the minimal four on purpose. Four admits only the canonical
    /// construction, which would make this a level with one topology -- a recipe, not a proof that
    /// NAND is complete. Six also admits the compositional route, and the two trade against each
    /// other: fewer gates costs more delay budget and less latency. That trade is the level.
    ///
    /// Underneath both sits the discovery this chapter exists for: a NAND whose two inputs come from
    /// the same place is an inverter. Nothing in the game teaches that, and every route needs it.
    /// </remarks>
    public class NothingButNandLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-3, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-3, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);

        private static readonly Vector2Int Top = new Vector2Int(-1, 2);
        private static readonly Vector2Int Middle = new Vector2Int(-1, 0);
        private static readonly Vector2Int Bottom = new Vector2Int(-1, -2);
        private static readonly Vector2Int UpperRight = new Vector2Int(1, 1);
        private static readonly Vector2Int LowerRight = new Vector2Int(1, -1);
        private static readonly Vector2Int Final = new Vector2Int(2, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("nothing-but-nand", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped nothing-but-nand.json is invalid: {result.Error}");

            _level = result.Level;
        }

        // -----------------------------------------------------------------
        // The discovery both routes depend on
        // -----------------------------------------------------------------

        [Test]
        public void ANandFedFromOneSourceTwice_IsAnInverter()
        {
            // Wiring one output to both inputs of the same gate is legal -- the duplicate check
            // rejects only the same source-and-target pair, and In(0) and In(1) are different targets.
            // If that ever tightens, this level and the whole functional-completeness chapter die.
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Middle, GateKind.Nand);

            LevelTestFixtures.Wire(blueprint, SourceA, Middle, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceA, Middle, toPort: 1);
            LevelTestFixtures.Wire(blueprint, Middle, OutCell);

            BuiltCircuit built = CircuitBuilder.Build(_level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            var sink = (SinkNode)built.Simulation.GetNode(built.FixtureNodeIds["out"]);

            // Source a streams 0011, so an inverter delivers 1100. Read straight off the sink rather
            // than through the grader: this circuit is a probe, not an attempt at the level.
            Assert.AreEqual(4, sink.Received.Count, "a tied NAND should fire once per vector");
            Assert.AreEqual(0, built.Simulation.CorruptedCount, "both inputs arrive together, so no clash");

            var got = new Bit[sink.Received.Count];
            for (int i = 0; i < got.Length; i++)
                got[i] = sink.Received[i].Value;

            CollectionAssert.AreEqual(
                new[] { Bit.One, Bit.One, Bit.Zero, Bit.Zero }, got, "NOT of 0011");
        }

        // -----------------------------------------------------------------
        // Route one: the canonical four
        // -----------------------------------------------------------------

        /// <summary>
        /// N1 = A NAND B; N2 = A NAND N1; N3 = B NAND N1; N4 = N2 NAND N3.
        /// </summary>
        /// <remarks>
        /// The second stage takes one input straight from a source and the other from N1, a level
        /// behind, so both bare literals have to wait a tick. That is the whole trap.
        /// </remarks>
        private static CircuitBlueprint CanonicalFour(int bareLiteralDelay = 2)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Middle, GateKind.Nand);        // N1
            blueprint.Place(UpperRight, GateKind.Nand);    // N2
            blueprint.Place(LowerRight, GateKind.Nand);    // N3
            blueprint.Place(Final, GateKind.Nand);         // N4

            LevelTestFixtures.Wire(blueprint, SourceA, Middle, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, Middle, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SourceA, UpperRight, toPort: 0, delay: bareLiteralDelay);
            LevelTestFixtures.Wire(blueprint, Middle, UpperRight, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SourceB, LowerRight, toPort: 0, delay: bareLiteralDelay);
            LevelTestFixtures.Wire(blueprint, Middle, LowerRight, toPort: 1);

            LevelTestFixtures.Wire(blueprint, UpperRight, Final, toPort: 0);
            LevelTestFixtures.Wire(blueprint, LowerRight, Final, toPort: 1);

            LevelTestFixtures.Wire(blueprint, Final, OutCell);

            return blueprint;
        }

        [Test]
        public void TheCanonicalFour_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, CanonicalFour());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheCanonicalFour_IsUnbalancedByConstruction()
        {
            // The designed mistake, and it is not a slip -- it is what the textbook diagram looks like
            // when drawn without thinking about time. The player builds a correct XOR and it eats bits.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, CanonicalFour(bareLiteralDelay: 1));

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("destroyed", verdict.Reason);
        }

        // -----------------------------------------------------------------
        // Route two: the compositional six
        // -----------------------------------------------------------------

        /// <summary>
        /// XOR = (A OR B) AND NOT(A AND B), every piece of it built from NANDs.
        /// </summary>
        private static CircuitBlueprint CompositionalSix()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Top, GateKind.Nand);           // NOT A
            blueprint.Place(Bottom, GateKind.Nand);        // NOT B
            blueprint.Place(Middle, GateKind.Nand);        // A NAND B
            blueprint.Place(UpperRight, GateKind.Nand);    // (NOT A) NAND (NOT B) = A OR B
            blueprint.Place(LowerRight, GateKind.Nand);
            blueprint.Place(Final, GateKind.Nand);         // tied, to invert

            // Two inverters, each a NAND fed twice from one source.
            LevelTestFixtures.Wire(blueprint, SourceA, Top, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceA, Top, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceB, Bottom, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, Bottom, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SourceA, Middle, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, Middle, toPort: 1);

            // De Morgan: NOT A NAND NOT B is A OR B.
            LevelTestFixtures.Wire(blueprint, Top, UpperRight, toPort: 0);
            LevelTestFixtures.Wire(blueprint, Bottom, UpperRight, toPort: 1);

            // The OR sits a level deeper than A NAND B, so that term waits.
            LevelTestFixtures.Wire(blueprint, UpperRight, LowerRight, toPort: 0);
            LevelTestFixtures.Wire(blueprint, Middle, LowerRight, toPort: 1, delay: 2);

            LevelTestFixtures.Wire(blueprint, LowerRight, Final, toPort: 0);
            LevelTestFixtures.Wire(blueprint, LowerRight, Final, toPort: 1);

            LevelTestFixtures.Wire(blueprint, Final, OutCell);

            return blueprint;
        }

        [Test]
        public void TheCompositionalSix_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, CompositionalSix());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // The trade between them
        // -----------------------------------------------------------------

        [Test]
        public void TheTwoRoutes_TradeGatesAgainstDelay()
        {
            // This is why the budget is six and not four. Neither route dominates: one is smaller,
            // the other is cheaper to balance. A level where one answer beat the other on every axis
            // would have one answer.
            CircuitBlueprint four = CanonicalFour();
            CircuitBlueprint six = CompositionalSix();

            Assert.AreEqual(4, four.CountOf(GateKind.Nand), "the canonical route is four gates");
            Assert.AreEqual(6, six.CountOf(GateKind.Nand), "the compositional route is six");

            Assert.Greater(four.ExtraDelay(), six.ExtraDelay(),
                "the smaller circuit should be the one that costs more delay budget");
        }

        [Test]
        public void BothRoutes_FitTheDelayBudget()
        {
            Assert.LessOrEqual(CanonicalFour().ExtraDelay(), _level.DelayBudget, "canonical four");
            Assert.LessOrEqual(CompositionalSix().ExtraDelay(), _level.DelayBudget, "compositional six");
        }

        [Test]
        public void BothRoutes_FitTheGateBudget()
        {
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.Nand), 6,
                "budgeting four would close off the compositional route and leave one topology");
        }

        // -----------------------------------------------------------------
        // The premise
        // -----------------------------------------------------------------

        [Test]
        public void TheLevelStocksNothingButNand()
        {
            foreach (GateKind kind in new[]
                     { GateKind.Not, GateKind.And, GateKind.Or, GateKind.Xor, GateKind.Nor })
            {
                Assert.IsFalse(_level.Offers(kind),
                    $"{GatePalette.Label(kind)} would make the completeness argument vanish");
            }

            Assert.IsTrue(_level.Offers(GateKind.Nand));
        }

        [Test]
        public void TheVectorsAreTheWholeXorTable()
        {
            Assert.AreEqual(4, _level.VectorCount);
            Assert.AreEqual("0110", _level.Expectations[0].Values, "XOR, in AB order 00 01 10 11");
        }

        [Test]
        public void TheWiringCanBeReTimed_UnlikeTheDeMorganLevel()
        {
            // Deliberately the opposite of the-long-way-round. There the fix is a gate and re-timing
            // is forbidden; here every route needs padding, so the level has to allow it.
            Assert.GreaterOrEqual(_level.MaxWireDelay, 2,
                "both routes need a wire longer than one tick");
            Assert.Greater(_level.DelayBudget, 0, "and a budget to spend on it");
        }

        // -----------------------------------------------------------------
        // The hint
        // -----------------------------------------------------------------

        [Test]
        public void TheHint_PromisesCompletenessWithoutShowingTheTrick()
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

            // Telling the player NAND is enough is the premise and belongs here. Telling them a gate
            // fed twice from one place inverts is the discovery, and does not.
            foreach (string giveaway in
                     new[] { "not", "and", "or", "xor", "invert", "inverter", "both", "twice", "tie",
                             "tied", "itself", "same" })
            {
                Assert.IsFalse(words.Contains(giveaway),
                    $"the hint should not use the word '{giveaway}'. Hint: {hint}");
            }

            Assert.IsTrue(words.Contains("nand"), $"the hint should name the one gate. Hint: {hint}");
        }
    }
}
