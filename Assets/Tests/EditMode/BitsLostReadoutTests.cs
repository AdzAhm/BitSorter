using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The corruption counter: what it says, when it appears, and -- the part that matters -- that it
    /// tracks the simulation live rather than only at the end.
    /// </summary>
    /// <remarks>
    /// This readout is the reason balance-the-paths teaches anything. The player runs an unbalanced
    /// circuit and watches the number climb while the bits are still moving; the *timing* of the
    /// increments is what points at the junction. A counter that only settled on a final figure would
    /// leave them with a number and nothing to attach it to.
    /// </remarks>
    public class BitsLostReadoutTests
    {
        // -----------------------------------------------------------------
        // Wording
        // -----------------------------------------------------------------

        [Test]
        public void NothingIsShownUntilABitDies()
        {
            // Absent, not zero. A permanent "0 bits lost" is a debug stat, and the eye learns to
            // ignore those -- the whole point here is that appearing is itself the event.
            Assert.IsFalse(BitsLostReadout.IsVisible(0));
            Assert.IsEmpty(BitsLostReadout.Describe(0));

            Assert.IsTrue(BitsLostReadout.IsVisible(1));
        }

        [Test]
        public void ItReadsAsProseRatherThanAsAStat()
        {
            Assert.AreEqual("1 BIT LOST", BitsLostReadout.Describe(1));
            Assert.AreEqual("2 BITS LOST", BitsLostReadout.Describe(2));
            Assert.AreEqual("8 BITS LOST", BitsLostReadout.Describe(8));
        }

        [Test]
        public void ARiseIsWhatTriggersTheReaction()
        {
            Assert.IsTrue(BitsLostReadout.Rose(0, 2));
            Assert.IsTrue(BitsLostReadout.Rose(2, 4));

            Assert.IsFalse(BitsLostReadout.Rose(4, 4), "an unchanged count is not an event");
            Assert.IsFalse(BitsLostReadout.Rose(4, 0), "a rebuild resetting the count is not an event");
        }

        // -----------------------------------------------------------------
        // The acceptance criterion, driven through the real simulation
        // -----------------------------------------------------------------

        [Test]
        public void OnBalanceThePaths_TheCounterClimbsWhileTheRunIsStillGoing()
        {
            // The level this feature exists for, wired the way a player wires it first. Stepping tick
            // by tick and reading the counter at each step is as close as Edit Mode gets to watching
            // it happen.
            LevelLoadResult loaded = LevelLoader.Load("balance-the-paths", LevelTestFixtures.Board);
            Assert.IsTrue(loaded.IsValid, loaded.Error);

            LevelDefinition level = loaded.Level;
            BuiltCircuit built = CircuitBuilder.Build(level, NaiveWiring());

            var readings = new List<string>();
            int settledTick = -1;

            for (int tick = 0; tick < level.TickLimit; tick++)
            {
                if (LevelGrader.IsSettled(built.Simulation.View))
                {
                    settledTick = tick;
                    break;
                }

                built.Simulation.Tick();
                readings.Add(BitsLostReadout.Describe(built.Simulation.CorruptedCount));
            }

            Assert.Greater(settledTick, 0, "the run should have settled");

            // Distinct non-empty readings: how many different things the player actually saw.
            var seen = new List<string>();
            foreach (string reading in readings)
            {
                if (reading.Length > 0 && (seen.Count == 0 || seen[seen.Count - 1] != reading))
                    seen.Add(reading);
            }

            string trace = string.Join(" -> ", seen.ToArray());

            Assert.GreaterOrEqual(seen.Count, 3,
                $"the counter should visibly climb through several values, not appear once. Saw: {trace}");

            Assert.AreEqual("2 BITS LOST", seen[0],
                $"the first collision destroys two bits, because the arrivals disagreed. Saw: {trace}");

            Assert.AreEqual("8 BITS LOST", seen[seen.Count - 1], $"eight in total. Saw: {trace}");
        }

        [Test]
        public void TheCounterAppearsBeforeTheRunEnds()
        {
            // The single assertion that would fail if this were only computed from the verdict. If the
            // first visible reading arrives on the final tick, the player never sees it climb.
            LevelLoadResult loaded = LevelLoader.Load("balance-the-paths", LevelTestFixtures.Board);
            LevelDefinition level = loaded.Level;
            BuiltCircuit built = CircuitBuilder.Build(level, NaiveWiring());

            int firstVisibleTick = -1;
            int totalTicks = 0;

            for (int tick = 0; tick < level.TickLimit; tick++)
            {
                if (LevelGrader.IsSettled(built.Simulation.View))
                    break;

                built.Simulation.Tick();
                totalTicks++;

                if (firstVisibleTick < 0 && BitsLostReadout.IsVisible(built.Simulation.CorruptedCount))
                    firstVisibleTick = totalTicks;
            }

            Assert.Greater(firstVisibleTick, 0, "the counter never appeared at all");
            Assert.Less(firstVisibleTick, totalTicks,
                "the counter must appear while bits are still moving, not on the last tick");
        }

        [Test]
        public void ABalancedCircuit_NeverShowsTheCounter()
        {
            // The other half: a correct circuit must never flash this. A counter that appeared on a
            // clean run would teach the player to ignore it.
            LevelLoadResult loaded = LevelLoader.Load("balance-the-paths", LevelTestFixtures.Board);
            LevelDefinition level = loaded.Level;

            CircuitBlueprint fixed_ = NaiveWiring();
            fixed_.SetDelayAt(DirectWire, 2);

            BuiltCircuit built = CircuitBuilder.Build(level, fixed_);

            for (int tick = 0; tick < level.TickLimit; tick++)
            {
                if (LevelGrader.IsSettled(built.Simulation.View))
                    break;

                built.Simulation.Tick();

                Assert.IsFalse(BitsLostReadout.IsVisible(built.Simulation.CorruptedCount),
                    $"the counter appeared on tick {tick} of a correct circuit");
            }
        }

        // -----------------------------------------------------------------
        // The circuit a player builds first
        // -----------------------------------------------------------------

        private const int DirectWire = 3;

        private static readonly Vector2Int SourceA = new Vector2Int(-3, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-3, -1);
        private static readonly Vector2Int XorCell = new Vector2Int(0, 1);
        private static readonly Vector2Int AndCell = new Vector2Int(0, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);

        private static CircuitBlueprint NaiveWiring()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(XorCell, GateKind.Xor);
            blueprint.Place(AndCell, GateKind.And);

            LevelTestFixtures.Wire(blueprint, SourceA, XorCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, XorCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorCell, AndCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, AndCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, AndCell, OutCell);

            return blueprint;
        }
    }
}
