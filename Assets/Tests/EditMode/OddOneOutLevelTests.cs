using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Four-input parity, against a latency ceiling. Three XORs in a chain compute it and are too
    /// slow; the same three as a tree meet the ceiling with no padding at all.
    /// </summary>
    /// <remarks>
    /// The critical path, taught by changing the circuit's shape rather than its timing. The slow
    /// lane only balanced a deep circuit; here the depth itself is the problem. A chain is three
    /// gates deep and its late inputs have to be held back to meet it, so its latency is four. A
    /// tree is two gates deep, both halves arrive together, and its latency is three.
    /// </remarks>
    public class OddOneOutLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 2);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, 1);
        private static readonly Vector2Int SourceC = new Vector2Int(-4, -1);
        private static readonly Vector2Int SourceD = new Vector2Int(-4, -2);
        private static readonly Vector2Int OddCell = new Vector2Int(4, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("odd-one-out", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped odd-one-out.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>(A XOR B) XOR (C XOR D): two gates deep, both halves level.</summary>
        private static CircuitBlueprint Tree()
        {
            var ab = new Vector2Int(-2, 1);
            var cd = new Vector2Int(-2, -1);
            var top = new Vector2Int(1, 0);

            var blueprint = new CircuitBlueprint();
            blueprint.Place(ab, GateKind.Xor);
            blueprint.Place(cd, GateKind.Xor);
            blueprint.Place(top, GateKind.Xor);

            LevelTestFixtures.Wire(blueprint, SourceA, ab, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, ab, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceC, cd, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceD, cd, toPort: 1);

            LevelTestFixtures.Wire(blueprint, ab, top, toPort: 0);
            LevelTestFixtures.Wire(blueprint, cd, top, toPort: 1);

            LevelTestFixtures.Wire(blueprint, top, OddCell);

            return blueprint;
        }

        /// <summary>((A XOR B) XOR C) XOR D: three gates deep, C and D held back to meet it.</summary>
        private static CircuitBlueprint Chain(int cDelay = 2, int dDelay = 3)
        {
            var first = new Vector2Int(-2, 1);
            var second = new Vector2Int(0, 0);
            var third = new Vector2Int(2, 0);

            var blueprint = new CircuitBlueprint();
            blueprint.Place(first, GateKind.Xor);
            blueprint.Place(second, GateKind.Xor);
            blueprint.Place(third, GateKind.Xor);

            LevelTestFixtures.Wire(blueprint, SourceA, first, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, first, toPort: 1);

            LevelTestFixtures.Wire(blueprint, first, second, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceC, second, toPort: 1, delay: cDelay);

            LevelTestFixtures.Wire(blueprint, second, third, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceD, third, toPort: 1, delay: dDelay);

            LevelTestFixtures.Wire(blueprint, third, OddCell);

            return blueprint;
        }

        private int LatencyOf(CircuitBlueprint blueprint)
        {
            BuiltCircuit built = CircuitBuilder.Build(_level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            return LevelGrader.WorstLatency(built.Simulation.View, _level, built.FixtureNodeIds, out string _);
        }

        [Test]
        public void TheTree_Solves_WithNoPadding()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Tree());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
            Assert.AreEqual(0, Tree().ExtraDelay(), "a tree's halves arrive together");
            Assert.AreEqual(3, LatencyOf(Tree()));
        }

        /// <summary>
        /// The padded chain is correct and too slow -- and within the delay budget, so it fails on
        /// time, not on budget.
        /// </summary>
        [Test]
        public void ThePaddedChain_IsRight_ButTooSlow()
        {
            CircuitBlueprint chain = Chain();
            Assert.LessOrEqual(chain.ExtraDelay(), _level.DelayBudget, "sanity: within the budget");

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, chain);

            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("4 ticks", verdict.Reason);
            StringAssert.Contains("allows 3", verdict.Reason);
        }

        /// <summary>The chain with nothing held back: C and D arrive before the chain reaches them.</summary>
        [Test]
        public void TheUnpaddedChain_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Chain(cDelay: 1, dDelay: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TheExpectation_IsEachRowsParity()
        {
            string wanted = _level.Expectations[0].Values;

            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int ones = 0;

                foreach (string id in new[] { "a", "b", "c", "d" })
                    ones += (int)_level.FixtureById(id).Stream[vector];

                Assert.AreEqual(ones % 2 == 1 ? '1' : '0', wanted[vector], $"row {vector + 1}");
            }
        }

        [Test]
        public void ABareWire_FromAnySource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceA, SourceB, SourceC, SourceD })
            {
                var blueprint = new CircuitBlueprint();
                LevelTestFixtures.Wire(blueprint, source, OddCell);

                Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, blueprint).IsPass,
                    $"a bare wire from {source} passed");
            }
        }

        [Test]
        public void TheHint_SaysNothingOfHowItIsBuilt()
        {
            string hint = " " + _level.Hint.ToLowerInvariant() + " ";

            foreach (string word in new[] { "xor", "tree", "chain", "parity", "pair" })
                StringAssert.DoesNotContain(word, hint, $"the hint says '{word}'");
        }
    }
}
