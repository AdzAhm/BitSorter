using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The K-map level. f = Sm(0,1,2,5,6,7) is the cyclic map: no essential prime implicants, and
    /// exactly two minimal covers of three terms each. Both must pass, and neither may be cheaper.
    /// </summary>
    /// <remarks>
    /// The function is chosen for that symmetry and not for the arithmetic. Every minterm is covered
    /// by exactly two prime implicants, so there is no term the player is forced into and no route
    /// the budget quietly prefers -- which is the whole difference between teaching minimisation and
    /// teaching one answer.
    ///
    /// The timing lesson rides along underneath: product terms sit at different depths depending on
    /// how many inverters they pass through, so the OR tree collecting them is unbalanced before the
    /// player has done anything wrong.
    /// </remarks>
    public class FourCornersLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 2);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, 0);
        private static readonly Vector2Int SourceC = new Vector2Int(-4, -2);
        private static readonly Vector2Int OutCell = new Vector2Int(4, 0);

        private static readonly Vector2Int NotTop = new Vector2Int(-2, 2);
        private static readonly Vector2Int NotMid = new Vector2Int(-2, 0);
        private static readonly Vector2Int NotLow = new Vector2Int(-2, -2);

        private static readonly Vector2Int AndTop = new Vector2Int(0, 2);
        private static readonly Vector2Int AndMid = new Vector2Int(0, 0);
        private static readonly Vector2Int AndLow = new Vector2Int(0, -2);

        private static readonly Vector2Int OrFirst = new Vector2Int(2, 1);
        private static readonly Vector2Int OrSecond = new Vector2Int(2, -1);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("four-corners", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped four-corners.json is invalid: {result.Error}");

            _level = result.Level;
        }

        // -----------------------------------------------------------------
        // The two minimal covers
        // -----------------------------------------------------------------

        /// <summary>
        /// A'B' + BC' + AC.
        /// </summary>
        /// <remarks>
        /// Depths, and why the delays are what they are. The inverters fire at level 1. A'B' takes
        /// both its inputs from inverters, so it lands at level 2 with plain wires. BC' mixes an
        /// inverted literal with a bare one, so the bare one waits a tick to meet it. AC takes two
        /// bare literals and lands a whole level early at 1, so its wire into the second OR carries
        /// the difference.
        /// </remarks>
        private static CircuitBlueprint FirstCover(int shallowTermDelay = 3, int bareLiteralDelay = 2)
        {
            var blueprint = new CircuitBlueprint();

            blueprint.Place(NotTop, GateKind.Not);    // A'
            blueprint.Place(NotMid, GateKind.Not);    // B'
            blueprint.Place(NotLow, GateKind.Not);    // C'
            blueprint.Place(AndTop, GateKind.And);    // A'B'
            blueprint.Place(AndMid, GateKind.And);    // BC'
            blueprint.Place(AndLow, GateKind.And);    // AC
            blueprint.Place(OrFirst, GateKind.Or);
            blueprint.Place(OrSecond, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceA, NotTop);
            LevelTestFixtures.Wire(blueprint, SourceB, NotMid);
            LevelTestFixtures.Wire(blueprint, SourceC, NotLow);

            // A'B' -- both inputs already at level 1.
            LevelTestFixtures.Wire(blueprint, NotTop, AndTop, toPort: 0);
            LevelTestFixtures.Wire(blueprint, NotMid, AndTop, toPort: 1);

            // BC' -- C' is at level 1, so bare B has to wait for it.
            LevelTestFixtures.Wire(blueprint, SourceB, AndMid, toPort: 0, delay: bareLiteralDelay);
            LevelTestFixtures.Wire(blueprint, NotLow, AndMid, toPort: 1);

            // AC -- two bare literals, so this term fires a level ahead of the others.
            LevelTestFixtures.Wire(blueprint, SourceA, AndLow, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceC, AndLow, toPort: 1);

            LevelTestFixtures.Wire(blueprint, AndTop, OrFirst, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndMid, OrFirst, toPort: 1);

            LevelTestFixtures.Wire(blueprint, OrFirst, OrSecond, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndLow, OrSecond, toPort: 1, delay: shallowTermDelay);

            LevelTestFixtures.Wire(blueprint, OrSecond, OutCell);

            return blueprint;
        }

        /// <summary>A'C' + B'C + AB -- the other minimal cover, and the same shape of arithmetic.</summary>
        private static CircuitBlueprint SecondCover()
        {
            var blueprint = new CircuitBlueprint();

            blueprint.Place(NotTop, GateKind.Not);    // A'
            blueprint.Place(NotMid, GateKind.Not);    // B'
            blueprint.Place(NotLow, GateKind.Not);    // C'
            blueprint.Place(AndTop, GateKind.And);    // A'C'
            blueprint.Place(AndMid, GateKind.And);    // B'C
            blueprint.Place(AndLow, GateKind.And);    // AB
            blueprint.Place(OrFirst, GateKind.Or);
            blueprint.Place(OrSecond, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceA, NotTop);
            LevelTestFixtures.Wire(blueprint, SourceB, NotMid);
            LevelTestFixtures.Wire(blueprint, SourceC, NotLow);

            LevelTestFixtures.Wire(blueprint, NotTop, AndTop, toPort: 0);
            LevelTestFixtures.Wire(blueprint, NotLow, AndTop, toPort: 1);

            LevelTestFixtures.Wire(blueprint, NotMid, AndMid, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceC, AndMid, toPort: 1, delay: 2);

            LevelTestFixtures.Wire(blueprint, SourceA, AndLow, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, AndLow, toPort: 1);

            LevelTestFixtures.Wire(blueprint, AndTop, OrFirst, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndMid, OrFirst, toPort: 1);

            LevelTestFixtures.Wire(blueprint, OrFirst, OrSecond, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndLow, OrSecond, toPort: 1, delay: 3);

            LevelTestFixtures.Wire(blueprint, OrSecond, OutCell);

            return blueprint;
        }

        [Test]
        public void TheFirstCover_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, FirstCover());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheSecondCover_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, SecondCover());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // Neither cover may be the cheaper one
        // -----------------------------------------------------------------

        [Test]
        public void BothCovers_UseTheSameGates()
        {
            foreach (GateKind kind in new[] { GateKind.Not, GateKind.And, GateKind.Or })
            {
                Assert.AreEqual(FirstCover().CountOf(kind), SecondCover().CountOf(kind),
                    $"the two covers should need the same number of {GatePalette.Label(kind)} gates");
            }
        }

        [Test]
        public void BothCovers_CostTheSameDelay()
        {
            // If one route were cheaper to balance, the level would have a preferred answer and the
            // K-map choice would be decorative.
            Assert.AreEqual(3, FirstCover().ExtraDelay(), "first cover");
            Assert.AreEqual(3, SecondCover().ExtraDelay(), "second cover");
        }

        [Test]
        public void BothCovers_LandOnTheSameTick()
        {
            Assert.AreEqual(LatencyOf(FirstCover()), LatencyOf(SecondCover()),
                "one cover finishing sooner would make the other the wrong answer");
        }

        private int LatencyOf(CircuitBlueprint blueprint)
        {
            BuiltCircuit built = CircuitBuilder.Build(_level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            var sink = (SinkNode)built.Simulation.GetNode(built.FixtureNodeIds["out"]);

            Assert.Greater(sink.Received.Count, 0, "nothing reached the bin");
            return sink.Received[0].Tick;
        }

        // -----------------------------------------------------------------
        // The mistake the level is built to allow
        // -----------------------------------------------------------------

        [Test]
        public void AnUnpaddedOrTree_DestroysBits()
        {
            // The heart of it. The player groups the map correctly, wires every gate correctly, and
            // the circuit still eats bits -- because AC fires a level before the terms it is being
            // joined to. Correct logic is not yet a correct circuit.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, FirstCover(shallowTermDelay: 1));

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("destroyed", verdict.Reason);
        }

        [Test]
        public void ForgettingTheBareLiteralsWait_AlsoDestroysBits()
        {
            // The other half of the same mistake, one stage earlier: a term mixing an inverted
            // literal with a bare one is unbalanced inside itself.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, FirstCover(bareLiteralDelay: 1));

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // The budget
        // -----------------------------------------------------------------

        [Test]
        public void TheBudgetAdmitsAMinimalCoverAndNothingSlacker()
        {
            // Three terms, three inverters, a two-input OR tree. Exactly a minimal cover, so a fourth
            // product term will not fit and the player has to actually minimise.
            Assert.AreEqual(3, _level.BudgetFor(GateKind.Not));
            Assert.AreEqual(3, _level.BudgetFor(GateKind.And));
            Assert.AreEqual(2, _level.BudgetFor(GateKind.Or));
        }

        [Test]
        public void TheDelayBudget_CoversASolutionWithSomethingToSpare()
        {
            Assert.GreaterOrEqual(_level.DelayBudget, FirstCover().ExtraDelay(),
                "a level budgeted below its own solution would be unsolvable");
            Assert.Greater(_level.DelayBudget, FirstCover().ExtraDelay(),
                "leave a tick spare so a wrong guess can be taken back");
        }

        [Test]
        public void TheLevelStocksNoShortcutGate()
        {
            // XOR, NAND and NOR would each collapse parts of this map and let the player skip the
            // grouping entirely.
            Assert.IsFalse(_level.Offers(GateKind.Xor));
            Assert.IsFalse(_level.Offers(GateKind.Nand));
            Assert.IsFalse(_level.Offers(GateKind.Nor));
        }

        // -----------------------------------------------------------------
        // The function
        // -----------------------------------------------------------------

        [Test]
        public void TheVectorsEnumerateTheWholeMap()
        {
            Assert.AreEqual(8, _level.VectorCount, "three inputs, eight rows");
            Assert.AreEqual("11100111", _level.Expectations[0].Values,
                "f = Sm(0,1,2,5,6,7), in ABC order 000 through 111");
        }

        [Test]
        public void TheMapIsCyclic_SoNeitherCoverIsForced()
        {
            // Guards the property the level rests on. Six ones with exactly two zeros at m3 and m4
            // leaves every minterm covered by two prime implicants and none of them essential. Change
            // the string and this level silently becomes a single-answer puzzle.
            string values = _level.Expectations[0].Values;

            int ones = 0;
            foreach (char c in values)
            {
                if (c == '1')
                    ones++;
            }

            Assert.AreEqual(6, ones, "six minterms, or the ring is broken");
            Assert.AreEqual('0', values[3], "m3 must be the gap");
            Assert.AreEqual('0', values[4], "m4 must be the other gap");
        }

        // -----------------------------------------------------------------
        // The hint
        // -----------------------------------------------------------------

        [Test]
        public void TheHint_PointsAtGroupingWithoutNamingATerm()
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

            // Naming a literal or a gate hands over one of the two covers, which is exactly the
            // choice the player is here to make.
            foreach (string giveaway in
                     new[] { "and", "or", "not", "invert", "karnaugh", "map", "adjacent" })
            {
                Assert.IsFalse(words.Contains(giveaway),
                    $"the hint should not use the word '{giveaway}'. Hint: {hint}");
            }
        }
    }
}
