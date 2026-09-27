using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 4:1 multiplexer: Pick a lane three times over, as a tree.
    /// </summary>
    /// <remarks>
    /// Two muxes choose within each pair on s0, and a third chooses between the pairs on s1. The
    /// parts list stocks exactly that tree -- two NOTs, six ANDs, three ORs -- which leaves one NOT
    /// for s0 and one for s1, so the inverted s0 has to feed both first-stage muxes. Sharing a
    /// signal is the one thing Pick a lane did not ask for.
    ///
    /// The delay is the lesson the room is for. The pairs are chosen three ticks in, and s1 has
    /// been waiting since tick 0: bare, it needs a wire of four to meet them, and inverted, three
    /// after its NOT. That is why the level allows a wire of four and why its board is wider.
    ///
    /// Eight vectors, not sixty-four: for each value of s1 s0, the chosen line is 1 against three
    /// 0s, then 0 against three 1s. A circuit that picks any other line, ignores a select bit or
    /// answers a constant disagrees on at least one of the two.
    /// </remarks>
    public class FourLanesLevelTests
    {
        private static readonly Vector2Int SourceD0 = new Vector2Int(-5, 3);
        private static readonly Vector2Int SourceD1 = new Vector2Int(-5, 2);
        private static readonly Vector2Int SourceD2 = new Vector2Int(-5, 1);
        private static readonly Vector2Int SourceD3 = new Vector2Int(-5, 0);
        private static readonly Vector2Int SourceS1 = new Vector2Int(-5, -1);
        private static readonly Vector2Int SourceS0 = new Vector2Int(-5, -2);
        private static readonly Vector2Int OutCell = new Vector2Int(5, 0);

        private static readonly Vector2Int NotS1 = new Vector2Int(-3, -1);
        private static readonly Vector2Int NotS0 = new Vector2Int(-3, -2);
        private static readonly Vector2Int AndD0 = new Vector2Int(-1, 3);
        private static readonly Vector2Int AndD1 = new Vector2Int(-1, 2);
        private static readonly Vector2Int AndD2 = new Vector2Int(-1, 1);
        private static readonly Vector2Int AndD3 = new Vector2Int(-1, 0);
        private static readonly Vector2Int OrLow = new Vector2Int(1, 2);
        private static readonly Vector2Int OrHigh = new Vector2Int(1, 0);
        private static readonly Vector2Int AndLow = new Vector2Int(3, 2);
        private static readonly Vector2Int AndHigh = new Vector2Int(3, -1);
        private static readonly Vector2Int OrOut = new Vector2Int(4, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("four-lanes", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped four-lanes.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// low = d0 s0' + d1 s0, high = d2 s0' + d3 s0, out = low s1' + high s1.
        /// </summary>
        /// <param name="selectsSwapped">s1 chooses within the pairs and s0 between them.</param>
        /// <param name="bareSelectDelay">The wire bare s1 takes to the last stage.</param>
        /// <param name="outWire">The wire into the bin.</param>
        private static CircuitBlueprint Tree(bool selectsSwapped = false, int bareSelectDelay = 4, int outWire = 1)
        {
            Vector2Int inner = selectsSwapped ? SourceS1 : SourceS0;
            Vector2Int outer = selectsSwapped ? SourceS0 : SourceS1;

            var blueprint = new CircuitBlueprint();
            blueprint.Place(NotS0, GateKind.Not);
            blueprint.Place(NotS1, GateKind.Not);
            blueprint.Place(AndD0, GateKind.And);
            blueprint.Place(AndD1, GateKind.And);
            blueprint.Place(AndD2, GateKind.And);
            blueprint.Place(AndD3, GateKind.And);
            blueprint.Place(OrLow, GateKind.Or);
            blueprint.Place(OrHigh, GateKind.Or);
            blueprint.Place(AndLow, GateKind.And);
            blueprint.Place(AndHigh, GateKind.And);
            blueprint.Place(OrOut, GateKind.Or);

            // The inner select, inverted once and shared by both pairs. Each line it gates waits a
            // tick for the NOT.
            LevelTestFixtures.Wire(blueprint, inner, NotS0);
            LevelTestFixtures.Wire(blueprint, SourceD0, AndD0, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, NotS0, AndD0, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceD2, AndD2, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, NotS0, AndD2, toPort: 1);

            // The bare select: a tick ahead of the inverted branch, so it waits on the way out.
            LevelTestFixtures.Wire(blueprint, SourceD1, AndD1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, inner, AndD1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceD3, AndD3, toPort: 0);
            LevelTestFixtures.Wire(blueprint, inner, AndD3, toPort: 1);

            LevelTestFixtures.Wire(blueprint, AndD0, OrLow, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndD1, OrLow, toPort: 1, delay: 2);
            LevelTestFixtures.Wire(blueprint, AndD2, OrHigh, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndD3, OrHigh, toPort: 1, delay: 2);

            // The outer select has been waiting since tick 0 for pairs chosen at tick 3.
            LevelTestFixtures.Wire(blueprint, outer, NotS1);
            LevelTestFixtures.Wire(blueprint, OrLow, AndLow, toPort: 0);
            LevelTestFixtures.Wire(blueprint, NotS1, AndLow, toPort: 1, delay: 3);
            LevelTestFixtures.Wire(blueprint, OrHigh, AndHigh, toPort: 0);
            LevelTestFixtures.Wire(blueprint, outer, AndHigh, toPort: 1, delay: bareSelectDelay);

            LevelTestFixtures.Wire(blueprint, AndLow, OrOut, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndHigh, OrOut, toPort: 1);
            LevelTestFixtures.Wire(blueprint, OrOut, OutCell, delay: outWire);

            return blueprint;
        }

        private int Latency(CircuitBlueprint blueprint)
        {
            BuiltCircuit built = CircuitBuilder.Build(_level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            var bin = (SinkNode)built.Simulation.GetNode(built.FixtureNodeIds["out"]);
            Assert.Greater(bin.Received.Count, 0, "nothing reached the bin");

            return bin.Received[0].Tick;
        }

        // -----------------------------------------------------------------
        // The level
        // -----------------------------------------------------------------

        [Test]
        public void TheLevel_IsOnAnElevenBySevenBoard()
        {
            Assert.AreEqual(new Vector2Int(5, 3), _level.BoardHalfExtents);
        }

        /// <summary>Six inputs: a table to read, not a map to fold.</summary>
        [Test]
        public void TheLevel_OffersNoKarnaughMap()
        {
            Assert.IsFalse(KarnaughMap.Applies(_level));
        }

        [Test]
        public void TheExpectations_AreTheChosenLine()
        {
            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int chosen = Select(vector);
                Bit line = _level.FixtureById("d" + chosen).Stream[vector];

                Assert.AreEqual(line == Bit.One ? '1' : '0', _level.Expectations[0].Values[vector],
                    $"row {vector + 1} chooses d{chosen}");
            }
        }

        /// <summary>
        /// Every value of s1 s0 comes twice: its line a 1 against three 0s, and a 0 against three 1s.
        /// </summary>
        [Test]
        public void EverySelection_TestsItsLineAgainstTheOtherThree_BothWays()
        {
            for (int chosen = 0; chosen < 4; chosen++)
            {
                bool alone = false, against = false;

                for (int vector = 0; vector < _level.VectorCount; vector++)
                {
                    if (Select(vector) != chosen)
                        continue;

                    int others = 0;
                    for (int line = 0; line < 4; line++)
                    {
                        if (line != chosen && _level.FixtureById("d" + line).Stream[vector] == Bit.One)
                            others++;
                    }

                    Bit mine = _level.FixtureById("d" + chosen).Stream[vector];
                    alone |= mine == Bit.One && others == 0;
                    against |= mine == Bit.Zero && others == 3;
                }

                Assert.IsTrue(alone, $"d{chosen} is never the only 1");
                Assert.IsTrue(against, $"d{chosen} is never the only 0");
            }
        }

        private int Select(int vector) =>
            ((int)_level.FixtureById("s1").Stream[vector] << 1) | (int)_level.FixtureById("s0").Stream[vector];

        // -----------------------------------------------------------------
        // The tree
        // -----------------------------------------------------------------

        [Test]
        public void TheTree_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Tree());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheTree_FitsThePartsListAndTheDelayBudget()
        {
            CircuitBlueprint tree = Tree();

            Assert.AreEqual(9, tree.ExtraDelay(), "the tree's delay, which the budget is set from");
            Assert.LessOrEqual(tree.ExtraDelay(), _level.DelayBudget, "delay budget");
            Assert.AreEqual(_level.BudgetFor(GateKind.Not), tree.CountOf(GateKind.Not), "NOTs");
            Assert.AreEqual(_level.BudgetFor(GateKind.And), tree.CountOf(GateKind.And), "ANDs");
            Assert.AreEqual(_level.BudgetFor(GateKind.Or), tree.CountOf(GateKind.Or), "ORs");

            int longest = 0;
            foreach (BlueprintWire wire in tree.Wires)
                longest = Mathf.Max(longest, wire.Delay);

            Assert.AreEqual(_level.MaxWireDelay, longest, "the longest wire the tree needs is the level's cap");
        }

        /// <summary>Three gate levels and a NOT's worth of waiting: six ticks, the level's ceiling.</summary>
        [Test]
        public void TheTree_TakesSixTicks()
        {
            Assert.AreEqual(6, Latency(Tree()));
            Assert.AreEqual(6, _level.MaxLatency);
        }

        // -----------------------------------------------------------------
        // The mistakes the level is built to catch
        // -----------------------------------------------------------------

        /// <summary>
        /// The selects the wrong way round pick d2 for 01 and d1 for 10 -- with timing identical to
        /// the tree's, so it fails on its answers.
        /// </summary>
        [Test]
        public void SwappingTheSelects_PicksTheWrongLines()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Tree(selectsSwapped: true));

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        /// <summary>The first pair alone, straight to the bin: s1 ignored.</summary>
        [Test]
        public void IgnoringTheOuterSelect_Fails()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(NotS0, GateKind.Not);
            blueprint.Place(AndD0, GateKind.And);
            blueprint.Place(AndD1, GateKind.And);
            blueprint.Place(OrLow, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceS0, NotS0);
            LevelTestFixtures.Wire(blueprint, SourceD0, AndD0, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, NotS0, AndD0, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceD1, AndD1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceS0, AndD1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, AndD0, OrLow, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndD1, OrLow, toPort: 1, delay: 2);
            LevelTestFixtures.Wire(blueprint, OrLow, OutCell);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        /// <summary>A bare s1 on a plain wire reaches the last stage three ticks before its pair.</summary>
        [Test]
        public void AnOuterSelectThatDoesNotWait_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Tree(bareSelectDelay: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        /// <summary>A tick of padding into the bin is right, inside the budget, and too slow.</summary>
        [Test]
        public void ALongerWayOut_IsRight_ButTooSlow()
        {
            CircuitBlueprint slow = Tree(outWire: 2);
            Assert.LessOrEqual(slow.ExtraDelay(), _level.DelayBudget, "sanity: within the budget");

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, slow);

            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("7 ticks", verdict.Reason);
        }

        [Test]
        public void ABareWire_FromAnySource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceD0, SourceD1, SourceD2, SourceD3, SourceS1, SourceS0 })
            {
                var blueprint = new CircuitBlueprint();
                LevelTestFixtures.Wire(blueprint, source, OutCell);

                Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, blueprint).IsPass,
                    $"a bare wire from {source} passed");
            }
        }

        // -----------------------------------------------------------------
        // The hint
        // -----------------------------------------------------------------

        [Test]
        public void TheHint_NamesNoGateAndNoCircuit()
        {
            string hint = " " + _level.Hint.ToLowerInvariant() + " ";

            foreach (string word in new[] { " and ", " or ", " not ", "mux", "multiplexer", "tree", "invert" })
                StringAssert.DoesNotContain(word, hint, $"the hint says '{word.Trim()}'");
        }
    }
}
