using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The level that cannot be solved without re-timing a wire, and the failure it is built to produce
    /// first. Loads the shipped file rather than an inline copy, because the point is that the level the
    /// game ships is both solvable and instructively broken before it is solved.
    /// </summary>
    /// <remarks>
    /// The player is meant to build the obvious circuit, watch it fail, and work out from what they saw
    /// that the two paths into the AND are unequal. Nothing tells them so -- which is why the hint is
    /// asserted here as well as the behaviour.
    /// </remarks>
    public class BalanceLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-3, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-3, -1);
        private static readonly Vector2Int XorCell = new Vector2Int(0, 1);
        private static readonly Vector2Int AndCell = new Vector2Int(0, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);

        /// <summary>Index of the direct B-to-AND wire, the one that has to be lengthened.</summary>
        private const int DirectWire = 3;

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("balance-the-paths", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped balance-the-paths.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// The obvious circuit. Wire order is fixed so <see cref="DirectWire"/> stays meaningful, and it
        /// matches the order a player drawing left to right would produce.
        /// </summary>
        private static CircuitBlueprint Wiring(int directDelay = 1, int aToXor = 1)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(XorCell, GateKind.Xor);
            blueprint.Place(AndCell, GateKind.And);

            LevelTestFixtures.Wire(blueprint, SourceA, XorCell, toPort: 0, delay: aToXor);
            LevelTestFixtures.Wire(blueprint, SourceB, XorCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorCell, AndCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, AndCell, toPort: 1, delay: directDelay);
            LevelTestFixtures.Wire(blueprint, AndCell, OutCell);

            return blueprint;
        }

        // -----------------------------------------------------------------
        // The failure the level exists to produce
        // -----------------------------------------------------------------

        [Test]
        public void TheNaiveWiring_CorruptsRatherThanGoingQuiet()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Wiring());

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("destroyed", verdict.Reason);
        }

        [Test]
        public void TheNaiveWiringsCounter_ClimbsOverSeveralSeparateTicks()
        {
            // The whole design rests on this being watchable. A single quiet collision would give the
            // player nothing to notice; a counter ticking up over several seconds does.
            BuiltCircuit built = CircuitBuilder.Build(_level, Wiring());

            var readings = new List<int>();
            while (!LevelGrader.IsSettled(built.Simulation.View)
                   && !LevelGrader.HasTimedOut(built.Simulation.View, _level))
            {
                built.Simulation.Tick();
                readings.Add(built.Simulation.CorruptedCount);
            }

            int climbs = 0;
            for (int i = 1; i < readings.Count; i++)
            {
                if (readings[i] > readings[i - 1])
                    climbs++;
            }

            string trace = string.Join(",", readings);

            Assert.GreaterOrEqual(climbs, 3,
                $"the counter should climb on several ticks, not once -- saw {trace}");
            Assert.AreEqual(8, built.Simulation.CorruptedCount,
                $"eight bits destroyed in total -- saw {trace}");
        }

        [Test]
        public void TheNaiveWiring_LeavesTheBinCompletelyEmpty()
        {
            BuiltCircuit built = CircuitBuilder.Build(_level, Wiring());
            LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            var sink = (SinkNode)built.Simulation.GetNode(built.FixtureNodeIds["out"]);

            Assert.AreEqual(0, sink.Received.Count,
                "nothing should reach the bin, so the player cannot mistake it for a near miss");
        }

        // -----------------------------------------------------------------
        // The fix
        // -----------------------------------------------------------------

        [Test]
        public void LengtheningTheDirectWire_Solves()
        {
            CircuitBlueprint blueprint = Wiring();
            blueprint.SetDelayAt(DirectWire, 2);

            BuiltCircuit built = CircuitBuilder.Build(_level, blueprint);
            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
            Assert.AreEqual(0, built.Simulation.CorruptedCount, "balanced paths do not collide");
        }

        [Test]
        public void TheFix_CostsOneTickAndFitsTheBudget()
        {
            CircuitBlueprint blueprint = Wiring();
            blueprint.SetDelayAt(DirectWire, 2);

            Assert.AreEqual(1, blueprint.ExtraDelay(), "one tick added");
            Assert.LessOrEqual(blueprint.ExtraDelay(), _level.DelayBudget,
                "a level budgeted below its own solution would be unsolvable");
        }

        [Test]
        public void TheSpareTick_DoesNotLetACarelessSolutionThrough()
        {
            // The budget leaves a tick over so the player can experiment. It must not also let a
            // wrong placement pass, or the level stops teaching the thing it exists for.
            CircuitBlueprint insideXor = Wiring(aToXor: 2);
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, insideXor).IsPass,
                "desynchronising the XOR's own inputs must not pass");

            CircuitBlueprint overshoot = Wiring(directDelay: 3);
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, overshoot).IsPass,
                "overshooting the direct wire must not pass");

            CircuitBlueprint wasted = Wiring(directDelay: 2, aToXor: 2);
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, wasted).IsPass,
                "the right tick plus a wasted one in the wrong place must not pass");
        }

        [Test]
        public void TheLevelCannotBeSolvedWithoutReTiming()
        {
            // Every wire at the default, in the only topology the budget allows. If this ever passes,
            // the level has stopped being about delay.
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, Wiring()).IsPass);
        }

        // -----------------------------------------------------------------
        // The hint
        // -----------------------------------------------------------------

        [Test]
        public void TheHint_GesturesAtTimingWithoutNamingTheAnswer()
        {
            string hint = _level.Hint;

            Assert.IsNotEmpty(hint, "the level needs a hint");

            // Whole words, not substrings. "and" is a gate name worth guarding against, but it is also
            // inside "understand" and "random", so a substring check would fail a perfectly good future
            // hint and send someone hunting for a bug that is not there.
            var words = new HashSet<string>();
            foreach (string word in hint.ToLowerInvariant().Split(
                         new[] { ' ', '.', ',', ';', ':', '-', '!', '?', '(', ')', '\'' },
                         System.StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(word);
            }

            // Naming a gate, or telling them to lengthen something, hands over the diagnosis the player
            // is meant to make from watching the failure. Pinned so a future edit cannot give it away.
            foreach (string giveaway in
                     new[] { "xor", "and", "delay", "longer", "lengthen", "unequal", "balance" })
            {
                Assert.IsFalse(words.Contains(giveaway),
                    $"the hint should not use the word '{giveaway}' -- it names the answer. Hint: {hint}");
            }

            foreach (char c in hint)
            {
                Assert.IsFalse(char.IsDigit(c),
                    $"the hint should name no number -- a tick count is the answer. Hint: {hint}");
            }

            Assert.IsTrue(words.Contains("waits") || words.Contains("wait") || words.Contains("both"),
                $"it should still gesture at a gate waiting on its inputs. Hint: {hint}");
        }
    }
}
