using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The free-play sink readout: when it appears, what it says, and that what it says is what the
    /// simulation actually recorded.
    /// </summary>
    /// <remarks>
    /// This is the only account of a run the sandbox has. A graded level states what each sink was
    /// supposed to catch and then says whether it did; free play grades nothing, so if this readout is
    /// wrong or absent the player has run a circuit and been told nothing at all about the result.
    /// </remarks>
    public class SinkReadoutRulesTests
    {
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        private static SandboxConfig Config(int sources, int sinks, int vectors, params string[] streams)
        {
            var config = new SandboxConfig
            {
                sources = new string[sources],
                sinks = sinks,
                vectors = vectors,
            };

            for (int i = 0; i < sources; i++)
                config.sources[i] = i < streams.Length ? streams[i] : string.Empty;

            return config;
        }

        private static IReadOnlyList<SinkNode.Reception> Caught(params int[] values)
        {
            var list = new List<SinkNode.Reception>();

            for (int i = 0; i < values.Length; i++)
                list.Add(new SinkNode.Reception(values[i] == 1 ? Bit.One : Bit.Zero, i));

            return list;
        }

        private static LevelFixture First(LevelDefinition level, FixtureKind kind)
        {
            foreach (LevelFixture fixture in level.Fixtures)
            {
                if (fixture.Kind == kind)
                    return fixture;
            }

            Assert.Fail($"the level has no {kind}");
            return null;
        }

        // -----------------------------------------------------------------
        // When it appears
        // -----------------------------------------------------------------

        [Test]
        public void TheReadoutIsFreePlayOnly()
        {
            Assert.IsTrue(SinkReadoutRules.IsVisible(hasLevel: true, isGraded: false, runnerReady: true));

            Assert.IsFalse(SinkReadoutRules.IsVisible(true, true, true),
                "a graded level already states what each sink should catch, and then grades it");

            Assert.IsFalse(SinkReadoutRules.IsVisible(false, false, true),
                "there is nothing to report without a level");

            Assert.IsFalse(SinkReadoutRules.IsVisible(true, false, false),
                "there is nothing to read until the runner is ready");
        }

        [Test]
        public void AnAbsentLevelIsNotVisible()
        {
            // The overload the component calls, on the frame before the first level finishes loading.
            Assert.IsFalse(SinkReadoutRules.IsVisible(null, true));
        }

        [Test]
        public void TheSandboxShowsItAndAShippedLevelDoesNot()
        {
            LevelDefinition sandbox = SandboxLevel.Build(SandboxLevel.Default(Board), Board);

            Assert.IsTrue(SinkReadoutRules.IsVisible(sandbox, true));
            Assert.IsFalse(SinkReadoutRules.IsVisible(LevelTestFixtures.Routing(), true));
        }

        // -----------------------------------------------------------------
        // What it says
        // -----------------------------------------------------------------

        [Test]
        public void NothingCaughtReadsAsAStatementRatherThanAGap()
        {
            Assert.AreEqual(SinkReadoutRules.Nothing, SinkReadoutRules.Describe(Caught()));
            Assert.AreEqual(SinkReadoutRules.Nothing, SinkReadoutRules.Describe(null));

            Assert.IsFalse(SinkReadoutRules.CaughtAnything(null));
            Assert.IsFalse(SinkReadoutRules.CaughtAnything(Caught()));
        }

        [Test]
        public void CaughtBitsReadInArrivalOrder()
        {
            Assert.AreEqual("1", SinkReadoutRules.Describe(Caught(1)));
            Assert.AreEqual("1 0 1", SinkReadoutRules.Describe(Caught(1, 0, 1)));
            Assert.AreEqual("0 0 1 1", SinkReadoutRules.Describe(Caught(0, 0, 1, 1)));
        }

        [Test]
        public void ACaughtZeroIsAResultRatherThanAnAbsence()
        {
            // Bit.Zero is a value; an empty port is the absence. Showing "--" for a caught zero would
            // tell the player nothing arrived on a tick where something did, which in free play is the
            // one thing the readout exists to get right.
            Assert.AreEqual("0", SinkReadoutRules.Describe(Caught(0)));
            Assert.IsTrue(SinkReadoutRules.CaughtAnything(Caught(0)));
        }

        [Test]
        public void TheSharedBuilderIsResetRatherThanAppendedTo()
        {
            // The component keeps one builder and walks every row with it, so a call that appended
            // would print the previous sink's bits in front of this one's.
            var builder = new StringBuilder();

            SinkReadoutRules.Write(builder, Caught(1, 1));
            Assert.AreEqual("1 1", builder.ToString());

            SinkReadoutRules.Write(builder, Caught(0));
            Assert.AreEqual("0", builder.ToString());

            SinkReadoutRules.Write(builder, null);
            Assert.AreEqual(string.Empty, builder.ToString());
        }

        // -----------------------------------------------------------------
        // How big it is
        // -----------------------------------------------------------------

        [Test]
        public void AnEmptyListStillHasABody()
        {
            // A sandbox with the sinks stepped down to zero still draws its frame and heading. A panel
            // collapsed onto its chrome reads as a rendering fault rather than as an empty list.
            Assert.AreEqual(SinkReadoutRules.PanelHeight(1), SinkReadoutRules.PanelHeight(0));
            Assert.Greater(SinkReadoutRules.PanelHeight(0), SinkReadoutRules.Chrome);
        }

        [Test]
        public void ThePanelGrowsOneRowAtATime()
        {
            Assert.AreEqual(
                SinkReadoutRules.RowHeight,
                SinkReadoutRules.PanelHeight(3) - SinkReadoutRules.PanelHeight(2),
                0.001f);
        }

        // -----------------------------------------------------------------
        // When the rows are rebuilt
        // -----------------------------------------------------------------

        [Test]
        public void TheSignatureChangesWhenASinkIsAddedOrRemoved()
        {
            string two = SinkReadoutRules.Signature(SandboxLevel.Build(Config(2, 2, 4), Board));
            string three = SinkReadoutRules.Signature(SandboxLevel.Build(Config(2, 3, 4), Board));

            Assert.AreNotEqual(two, three);

            Assert.AreEqual(two, SinkReadoutRules.Signature(SandboxLevel.Build(Config(2, 2, 4), Board)),
                "the same sinks must produce the same signature, or the rows rebuild every frame");
        }

        [Test]
        public void AddingASourceDoesNotRebuildTheRows()
        {
            // Sources are not rows here. A signature that moved with them would tear down and rebuild
            // the whole list on an edit that cannot change it.
            string twoSources = SinkReadoutRules.Signature(SandboxLevel.Build(Config(2, 2, 4), Board));
            string threeSources = SinkReadoutRules.Signature(SandboxLevel.Build(Config(3, 2, 4), Board));

            Assert.AreEqual(twoSources, threeSources);
        }

        [Test]
        public void AnAbsentLevelHasAnEmptySignature()
        {
            Assert.AreEqual(string.Empty, SinkReadoutRules.Signature(null));
        }

        // -----------------------------------------------------------------
        // The acceptance criterion, driven through the real simulation
        // -----------------------------------------------------------------

        [Test]
        public void TheReadoutShowsExactlyWhatTheSinkCaught()
        {
            // A sandbox source wired straight to a sandbox sink. Whatever the stream said should come
            // out the far end in the same order, and that is what the player is shown.
            LevelDefinition level = SandboxLevel.Build(Config(1, 1, 4, "1011"), Board);

            LevelFixture source = First(level, FixtureKind.Source);
            LevelFixture sink = First(level, FixtureKind.Sink);

            var blueprint = new CircuitBlueprint();
            LevelTestFixtures.Wire(blueprint, source.Cell, sink.Cell);

            BuiltCircuit built = CircuitBuilder.Build(level, blueprint);

            for (int tick = 0; tick < level.TickLimit; tick++)
            {
                if (LevelGrader.IsSettled(built.Simulation.View))
                    break;

                built.Simulation.Tick();
            }

            var caught = built.Simulation.View.GetNode(built.FixtureNodeIds[sink.Id]) as SinkNode;

            Assert.IsNotNull(caught, "the sink fixture should have built a SinkNode");
            Assert.AreEqual("1 0 1 1", SinkReadoutRules.Describe(caught.Received));
        }

        [Test]
        public void AnUnwiredSinkReadsAsNothingCaught()
        {
            // The opening state of every sandbox: fixtures on the board and no wires yet. The readout
            // has to say "nothing arrived" rather than go blank or show a stale row.
            LevelDefinition level = SandboxLevel.Build(Config(1, 1, 4, "1011"), Board);
            LevelFixture sink = First(level, FixtureKind.Sink);

            BuiltCircuit built = CircuitBuilder.Build(level, new CircuitBlueprint());

            for (int tick = 0; tick < 8; tick++)
                built.Simulation.Tick();

            var caught = built.Simulation.View.GetNode(built.FixtureNodeIds[sink.Id]) as SinkNode;

            Assert.AreEqual(SinkReadoutRules.Nothing, SinkReadoutRules.Describe(caught.Received));
        }
    }
}
