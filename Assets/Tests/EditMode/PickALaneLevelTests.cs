using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 2:1 multiplexer. Two circuits build it, and they trade gates against delay budget.
    /// </summary>
    /// <remarks>
    /// The parts list is the only hint: stocking a NOT, two ANDs and an OR *and* two XORs puts both
    /// the textbook mux and the XOR-trick mux on the table without naming either.
    ///
    /// This is the first level to set maxLatency, and it is set to a figure both routes already meet.
    /// That is deliberate -- it introduces the idea that a circuit can be correct and still too slow
    /// before the adder chapter needs the player to understand it, and it costs nothing here.
    /// </remarks>
    public class PickALaneLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 2);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, 0);
        private static readonly Vector2Int SourceS = new Vector2Int(-4, -2);
        private static readonly Vector2Int OutCell = new Vector2Int(4, 0);

        private static readonly Vector2Int NotCell = new Vector2Int(-2, -2);
        private static readonly Vector2Int UpperCell = new Vector2Int(0, 1);
        private static readonly Vector2Int LowerCell = new Vector2Int(0, -1);
        private static readonly Vector2Int JoinCell = new Vector2Int(2, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("pick-a-lane", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped pick-a-lane.json is invalid: {result.Error}");

            _level = result.Level;
        }

        // -----------------------------------------------------------------
        // Route one: the textbook mux
        // -----------------------------------------------------------------

        /// <summary>
        /// (A AND NOT S) OR (B AND S).
        /// </summary>
        /// <remarks>
        /// S has to be in two places at once: inverted for one branch and bare for the other. The
        /// inverted branch sits a level deeper, so bare A waits for it and the bare branch waits on
        /// its way into the OR.
        /// </remarks>
        private static CircuitBlueprint TextbookMux(int aDelay = 2, int bareBranchToOr = 2)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(NotCell, GateKind.Not);
            blueprint.Place(UpperCell, GateKind.And);
            blueprint.Place(LowerCell, GateKind.And);
            blueprint.Place(JoinCell, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceS, NotCell);

            // A AND NOT S -- the inverter puts this branch at level 2, so bare A waits.
            LevelTestFixtures.Wire(blueprint, SourceA, UpperCell, toPort: 0, delay: aDelay);
            LevelTestFixtures.Wire(blueprint, NotCell, UpperCell, toPort: 1);

            // B AND S -- two bare literals, so this branch fires at level 1.
            LevelTestFixtures.Wire(blueprint, SourceB, LowerCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceS, LowerCell, toPort: 1);

            LevelTestFixtures.Wire(blueprint, UpperCell, JoinCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, LowerCell, JoinCell, toPort: 1, delay: bareBranchToOr);

            LevelTestFixtures.Wire(blueprint, JoinCell, OutCell);

            return blueprint;
        }

        [Test]
        public void TheTextbookMux_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, TextbookMux());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // Route two: the XOR trick
        // -----------------------------------------------------------------

        /// <summary>A XOR (S AND (A XOR B)) -- one gate fewer, one tick of budget more.</summary>
        private static CircuitBlueprint XorTrickMux(int aDelay = 3)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(UpperCell, GateKind.Xor);    // A XOR B
            blueprint.Place(LowerCell, GateKind.And);    // S AND that
            blueprint.Place(JoinCell, GateKind.Xor);     // A XOR that

            LevelTestFixtures.Wire(blueprint, SourceA, UpperCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, UpperCell, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SourceS, LowerCell, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, UpperCell, LowerCell, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SourceA, JoinCell, toPort: 0, delay: aDelay);
            LevelTestFixtures.Wire(blueprint, LowerCell, JoinCell, toPort: 1);

            LevelTestFixtures.Wire(blueprint, JoinCell, OutCell);

            return blueprint;
        }

        [Test]
        public void TheXorTrickMux_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, XorTrickMux());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // The trade between them
        // -----------------------------------------------------------------

        [Test]
        public void TheTwoRoutes_TradeGatesAgainstDelay()
        {
            CircuitBlueprint textbook = TextbookMux();
            CircuitBlueprint trick = XorTrickMux();

            int textbookGates = textbook.CountOf(GateKind.Not) + textbook.CountOf(GateKind.And)
                                + textbook.CountOf(GateKind.Or) + textbook.CountOf(GateKind.Xor);
            int trickGates = trick.CountOf(GateKind.Not) + trick.CountOf(GateKind.And)
                             + trick.CountOf(GateKind.Or) + trick.CountOf(GateKind.Xor);

            Assert.AreEqual(4, textbookGates, "NOT, two ANDs, an OR");
            Assert.AreEqual(3, trickGates, "two XORs and an AND");

            Assert.Greater(trick.ExtraDelay(), textbook.ExtraDelay(),
                "the smaller circuit should be the one that costs more delay budget");
        }

        [Test]
        public void BothRoutes_FitEveryBudget()
        {
            foreach (CircuitBlueprint route in new[] { TextbookMux(), XorTrickMux() })
            {
                Assert.LessOrEqual(route.ExtraDelay(), _level.DelayBudget, "delay budget");
            }

            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.Not), 1);
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.And), 2);
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.Or), 1);
            Assert.GreaterOrEqual(_level.BudgetFor(GateKind.Xor), 2);
        }

        [Test]
        public void BothRoutes_MeetTheLatencyCeiling()
        {
            // The ceiling is set to a figure both already meet, so it teaches the idea without
            // closing off either answer. If a future edit tightens it, one route dies silently.
            Assert.AreEqual(4, _level.MaxLatency);
            Assert.AreEqual(4, LatencyOf(TextbookMux()), "textbook");
            Assert.AreEqual(4, LatencyOf(XorTrickMux()), "xor trick");
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
        public void ForgettingTheSelectLineFansOut_DestroysBits()
        {
            // S has to reach the inverter and the second AND, and those two land a level apart. This
            // is the timing error a select line makes almost inevitable: players get the logic right
            // and then watch it eat bits.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, TextbookMux(aDelay: 1));

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("destroyed", verdict.Reason);
        }

        [Test]
        public void ForgettingTheShallowBranchWaits_AlsoDestroysBits()
        {
            // The other convergence: the bare branch fires a level before the inverted one, so the
            // OR joining them is unbalanced too.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, TextbookMux(bareBranchToOr: 1));

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        // -----------------------------------------------------------------
        // The latency ceiling has teeth
        // -----------------------------------------------------------------

        [Test]
        public void ACorrectButScenicRoute_FailsAsTooSlow()
        {
            // Right answers, right order, no collisions -- and still a failure, because the level
            // grades the critical path. The first time a player meets that idea.
            CircuitBlueprint scenic = TextbookMux();
            scenic.SetDelayAt(scenic.Wires.Count - 1, 2);   // the wire into the bin

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, scenic);

            Assert.IsFalse(verdict.IsPass, "five ticks against a ceiling of four");
            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            Assert.LessOrEqual(scenic.ExtraDelay(), _level.DelayBudget,
                "and it fails on time, not on budget -- the two limits are separate");
        }

        // -----------------------------------------------------------------
        // The function
        // -----------------------------------------------------------------

        [Test]
        public void TheVectorsEnumerateAllThreeInputs()
        {
            Assert.AreEqual(8, _level.VectorCount);
            Assert.AreEqual("00011011", _level.Expectations[0].Values,
                "s picks b when high and a when low, in ABS order");
        }

        [Test]
        public void TheSelectStreamAlternates_SoEveryRowPicksBothWays()
        {
            // Each (a, b) pair appears once with s low and once with s high. A stream that did not
            // alternate would let a circuit ignoring s pass half the table.
            LevelFixture s = _level.FixtureById("s");

            Assert.IsNotNull(s, "the level needs a select source called 's'");

            for (int i = 0; i < s.Stream.Count; i++)
            {
                Bit expected = i % 2 == 0 ? Bit.Zero : Bit.One;
                Assert.AreEqual(expected, s.Stream[i], $"vector {i} of the select line");
            }
        }

        [Test]
        public void WiringASourceStraightToTheBin_CannotPass()
        {
            // The cheapest thing a player tries. Neither input alone is the answer on every row,
            // which is what makes the select line necessary rather than decorative.
            foreach (Vector2Int source in new[] { SourceA, SourceB, SourceS })
            {
                var blueprint = new CircuitBlueprint();
                LevelTestFixtures.Wire(blueprint, source, OutCell);

                Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, blueprint).IsPass,
                    $"a bare wire from {source} must not solve a multiplexer");
            }
        }

        // -----------------------------------------------------------------
        // The hint
        // -----------------------------------------------------------------

        [Test]
        public void TheHint_PointsAtTheSelectLineWithoutNamingACircuit()
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

            foreach (string giveaway in
                     new[] { "and", "or", "not", "xor", "invert", "mux", "multiplexer" })
            {
                Assert.IsFalse(words.Contains(giveaway),
                    $"the hint should not use the word '{giveaway}'. Hint: {hint}");
            }

            Assert.IsTrue(words.Contains("s"), $"the hint should name the select line. Hint: {hint}");
        }
    }
}
