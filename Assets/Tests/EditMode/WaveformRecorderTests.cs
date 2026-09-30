using System;
using System.Text;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="WaveformRecorder"/>: every row holds what happened on every tick, collisions are
    /// marked where the bit arrived, and recording costs nothing per tick.
    /// </summary>
    /// <remarks>
    /// Rows are written as one cell a tick: <c>0</c> or <c>1</c> for a bit, <c>-</c> for none, and a
    /// trailing <c>x</c> where the bit arrived into a collision.
    /// </remarks>
    public class WaveformRecorderTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-3, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-3, -1);
        private static readonly Vector2Int SumSink = new Vector2Int(3, 1);
        private static readonly Vector2Int CarrySink = new Vector2Int(3, -1);
        private static readonly Vector2Int XorCell = new Vector2Int(0, 1);
        private static readonly Vector2Int AndCell = new Vector2Int(0, -1);

        private static LevelDefinition HalfAdder()
        {
            LevelLoadResult result = LevelLoader.Load("half-adder", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped half-adder.json is invalid: {result.Error}");
            return result.Level;
        }

        /// <summary>Builds a board, and records it from tick 0 for the given number of ticks.</summary>
        private static WaveformRecorder Record(
            LevelDefinition level, CircuitBlueprint board, int ticks, out BuiltCircuit built)
        {
            built = CircuitBuilder.Build(level, board);
            Simulation simulation = built.Simulation;

            var recorder = new WaveformRecorder();
            recorder.Reset(simulation.View, level, built.FixtureNodeIds);

            for (int i = 0; i < ticks; i++)
            {
                simulation.Tick();
                recorder.AfterTick(simulation.View, simulation.CurrentTick - 1);
            }

            return recorder;
        }

        private static string Row(Func<int, WaveCell> cell, int ticks)
        {
            var text = new StringBuilder();

            for (int tick = 0; tick < ticks; tick++)
            {
                if (tick > 0)
                    text.Append(' ');

                text.Append(cell(tick));
            }

            return text.ToString();
        }

        /// <summary>The edge carrying the wire from one cell's output into another cell's input port.</summary>
        private static int EdgeId(BuiltCircuit built, Vector2Int from, Vector2Int to, int toPort)
        {
            SimulationView view = built.Simulation.View;

            for (int id = 0; id < view.EdgeCount; id++)
            {
                Edge edge = view.GetEdge(id);

                if (edge != null
                    && built.Cells.TryGetValue(edge.Source.Owner.Id, out Vector2Int source) && source == from
                    && built.Cells.TryGetValue(edge.Target.Owner.Id, out Vector2Int target) && target == to
                    && edge.Target.Index == toPort)
                {
                    return id;
                }
            }

            Assert.Fail($"sanity: no wire from {from} into {to} port {toPort}");
            return -1;
        }

        // -----------------------------------------------------------------
        // What each row holds
        // -----------------------------------------------------------------

        /// <summary>
        /// On the half adder as solved, the sources' rows are their streams, the bins' rows are the
        /// sums and carries two ticks later, and a wire's row is what arrived at its far end.
        /// </summary>
        [Test]
        public void ABalancedHalfAdder_RecordsEverySourceBinAndWire()
        {
            LevelDefinition level = HalfAdder();
            var board = new CircuitBlueprint();
            board.Place(XorCell, GateKind.Xor);
            board.Place(AndCell, GateKind.And);
            LevelTestFixtures.Wire(board, SourceA, XorCell, toPort: 0);
            LevelTestFixtures.Wire(board, SourceB, XorCell, toPort: 1);
            LevelTestFixtures.Wire(board, SourceA, AndCell, toPort: 0);
            LevelTestFixtures.Wire(board, SourceB, AndCell, toPort: 1);
            LevelTestFixtures.Wire(board, XorCell, SumSink);
            LevelTestFixtures.Wire(board, AndCell, CarrySink);

            WaveformRecorder recorder = Record(level, board, 8, out BuiltCircuit built);

            Assert.AreEqual(2, recorder.SourceCount);
            Assert.AreEqual(2, recorder.SinkCount);

            Assert.AreEqual("0 0 1 1 - - - -", Row(t => recorder.SourceCell(0, t), 8), "source a");
            Assert.AreEqual("0 1 0 1 - - - -", Row(t => recorder.SourceCell(1, t), 8), "source b");
            Assert.AreEqual("- - 0 1 1 0 - -", Row(t => recorder.SinkCell(0, t), 8), "sum");
            Assert.AreEqual("- - 0 0 0 1 - -", Row(t => recorder.SinkCell(1, t), 8), "carry");

            int aToXor = EdgeId(built, SourceA, XorCell, 0);
            Assert.AreEqual("- 0 0 1 1 - - -", Row(t => recorder.EdgeCell(aToXor, t), 8), "a into the XOR");

            int xorToSum = EdgeId(built, XorCell, SumSink, 0);
            Assert.AreEqual("- - 0 1 1 0 - -", Row(t => recorder.EdgeCell(xorToSum, t), 8), "the XOR into sum");

            Assert.AreEqual(7, recorder.LastTick);
            Assert.AreEqual(0, built.Simulation.CorruptedCount, "sanity: a balanced adder loses nothing");
        }

        /// <summary>
        /// A longer path into one input: the waiting bit is hit by the next one down the short path
        /// (both die, the values differ), then by the next down the long one (only the arrival dies,
        /// they match). Each cross is on the wire whose bit arrived into the collision, on its tick.
        /// </summary>
        /// <remarks>
        /// a is 0011 on a two-tick wire, b is 0101 on a one-tick wire. b's first 0 waits at tick 1;
        /// at tick 2 b's 1 lands on it and both go, while a's first 0 lands in the other port; at tick
        /// 3 a's second 0 lands on a's first. Three bits lost.
        /// </remarks>
        [Test]
        public void AnUnbalancedPath_MarksEachCollisionOnTheWireItArrivedBy()
        {
            LevelDefinition level = HalfAdder();
            var board = new CircuitBlueprint();
            board.Place(XorCell, GateKind.Xor);
            LevelTestFixtures.Wire(board, SourceA, XorCell, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(board, SourceB, XorCell, toPort: 1);
            LevelTestFixtures.Wire(board, XorCell, SumSink);

            WaveformRecorder recorder = Record(level, board, 4, out BuiltCircuit built);

            int aToXor = EdgeId(built, SourceA, XorCell, 0);
            int bToXor = EdgeId(built, SourceB, XorCell, 1);

            Assert.AreEqual("- 0 1x 0", Row(t => recorder.EdgeCell(bToXor, t), 4), "b into the XOR");
            Assert.AreEqual("- - 0 0x", Row(t => recorder.EdgeCell(aToXor, t), 4), "a into the XOR");
            Assert.AreEqual(3, built.Simulation.CorruptedCount, "sanity: three bits should be lost");
        }

        /// <summary>
        /// Two bits of one value landing in an empty port together: one copy survives and the model
        /// does not say which, so both wires are marked, and the bin shows the bit it got and the
        /// collision.
        /// </summary>
        [Test]
        public void TwoBitsOfOneValueTogether_AreBothMarked_AndTheBinStillGetsTheBit()
        {
            LevelDefinition level = LevelTestFixtures.Parse(@"{
                ""name"": ""Two into one"", ""hint"": ""a hint"", ""tickLimit"": 40,
                ""fixtures"": [
                    { ""id"": ""a"",   ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"":  1 }, ""stream"": ""10"" },
                    { ""id"": ""b"",   ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": -1 }, ""stream"": ""10"" },
                    { ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"":  0 } }
                ],
                ""budget"": [ { ""kind"": ""Not"", ""count"": 1 } ],
                ""expected"": [ { ""sink"": ""out"", ""values"": ""10"" } ]
            }");

            var out_ = new Vector2Int(3, 0);
            var board = new CircuitBlueprint();
            LevelTestFixtures.Wire(board, SourceA, out_);
            LevelTestFixtures.Wire(board, SourceB, out_);

            WaveformRecorder recorder = Record(level, board, 3, out BuiltCircuit built);

            Assert.AreEqual("- 1x 0x", Row(t => recorder.EdgeCell(EdgeId(built, SourceA, out_, 0), t), 3), "a's wire");
            Assert.AreEqual("- 1x 0x", Row(t => recorder.EdgeCell(EdgeId(built, SourceB, out_, 0), t), 3), "b's wire");
            Assert.AreEqual("- 1x 0x", Row(t => recorder.SinkCell(0, t), 3), "the bin");
            Assert.AreEqual(2, built.Simulation.CorruptedCount, "sanity: one bit lost on each tick");
        }

        /// <summary>
        /// On a clocked level a source sends only on the ticks the clock is high, and the clock is
        /// the old corner diagram's, one tick on from the executed tick it counts.
        /// </summary>
        [Test]
        public void TheClock_IsHighOnExactlyTheTicksTheSourcesSendOn()
        {
            for (int period = 1; period <= 6; period++)
            {
                for (int tick = 0; tick < 30; tick++)
                {
                    Assert.AreEqual(ClockDiagram.IsHigh(tick + 1, period), WaveformRecorder.ClockOn(tick, period),
                        $"tick {tick}, period {period}");
                }
            }

            LevelDefinition level = LevelTestFixtures.FourVectorsOnAClock("0011", clockPeriod: 3);
            WaveformRecorder recorder = Record(level, new CircuitBlueprint(), 12, out BuiltCircuit _);

            for (int tick = 0; tick < 10; tick++)
            {
                Assert.AreEqual(WaveformRecorder.ClockOn(tick, 3), recorder.SourceCell(0, tick).Value.HasValue,
                    $"tick {tick}: the source and the clock disagree");
            }
        }

        // -----------------------------------------------------------------
        // Keeping and forgetting
        // -----------------------------------------------------------------

        [Test]
        public void AReset_ForgetsEverythingRecorded()
        {
            LevelDefinition level = HalfAdder();
            WaveformRecorder recorder = Record(level, new CircuitBlueprint(), 4, out BuiltCircuit _);
            Assert.IsTrue(recorder.SourceCell(0, 0).Value.HasValue, "sanity: nothing was recorded");

            BuiltCircuit fresh = CircuitBuilder.Build(level, new CircuitBlueprint());
            recorder.Reset(fresh.Simulation.View, level, fresh.FixtureNodeIds);

            Assert.AreEqual(-1, recorder.LastTick);
            Assert.IsFalse(recorder.SourceCell(0, 0).Value.HasValue, "a reset kept a recorded bit");
        }

        /// <summary>Past <see cref="WaveformRecorder.Capacity"/> ticks the oldest are forgotten.</summary>
        [Test]
        public void TheOldestTicks_FallOutOfTheRing()
        {
            int ticks = WaveformRecorder.Capacity + 10;
            WaveformRecorder recorder = Record(HalfAdder(), new CircuitBlueprint(), ticks, out BuiltCircuit _);

            Assert.AreEqual(ticks - 1, recorder.LastTick);
            Assert.AreEqual(ticks - WaveformRecorder.Capacity, recorder.FirstTick);
            Assert.IsFalse(recorder.SourceCell(0, 0).Value.HasValue,
                "tick 0 is past the ring's start and still reads as a bit");
        }

        /// <summary>A tick the recorder was not told about reads as nothing, not as the ring's old contents.</summary>
        [Test]
        public void ASkippedTick_ReadsAsNothing()
        {
            LevelDefinition level = HalfAdder();
            BuiltCircuit built = CircuitBuilder.Build(level, new CircuitBlueprint());
            var recorder = new WaveformRecorder();
            recorder.Reset(built.Simulation.View, level, built.FixtureNodeIds);

            built.Simulation.Tick();
            recorder.AfterTick(built.Simulation.View, 0);
            built.Simulation.Run(3);
            recorder.AfterTick(built.Simulation.View, 3);

            Assert.IsTrue(recorder.SourceCell(0, 0).Value.HasValue, "sanity: tick 0 was not recorded");
            Assert.IsFalse(recorder.SourceCell(0, 1).Value.HasValue, "a skipped tick reads as a bit");
            Assert.IsFalse(recorder.SourceCell(0, 2).Value.HasValue, "a skipped tick reads as a bit");
            Assert.IsTrue(recorder.SourceCell(0, 3).Value.HasValue, "sanity: tick 3 was not recorded");
        }

        /// <summary>
        /// Recording a tick allocates nothing -- it runs for every tick, several to a frame -- and the
        /// measurement can see an allocation, or the first half would prove nothing.
        /// </summary>
        [Test]
        public void RecordingATick_AllocatesNothing()
        {
            LevelDefinition level = HalfAdder();
            var board = new CircuitBlueprint();
            board.Place(XorCell, GateKind.Xor);
            LevelTestFixtures.Wire(board, SourceA, XorCell, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(board, SourceB, XorCell, toPort: 1);
            LevelTestFixtures.Wire(board, XorCell, SumSink);

            BuiltCircuit built = CircuitBuilder.Build(level, board);
            Simulation simulation = built.Simulation;
            var recorder = new WaveformRecorder();
            recorder.Reset(simulation.View, level, built.FixtureNodeIds);

            simulation.Tick();
            recorder.AfterTick(simulation.View, 0);

            for (int tick = 1; tick < 6; tick++)
            {
                simulation.Tick();
                SimulationView view = simulation.View;
                int executed = simulation.CurrentTick - 1;

                Assert.That(() => recorder.AfterTick(view, executed), Is.Not.AllocatingGCMemory(),
                    $"recording tick {executed} allocated");
            }

            Assert.That(() => new byte[16], Is.AllocatingGCMemory(),
                "sanity: the measurement cannot see an allocation, so the checks above prove nothing");
        }
    }
}
