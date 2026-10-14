using System;
using BitSorter.Board;
using BitSorter.Sim;
using EngineGate = BitSorter.Sim.GateKind;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BoardStimulus = BitSorter.Board.Stimulus;
using BoardTrack = BitSorter.Board.Track;

namespace BitSorter.View
{
    /// <summary>
    /// The workbench preview's circuit, as a document: the look reference's glitch, A AND NOT A, beside
    /// the three things the reference cannot show -- a glitch too short to get through (inertial
    /// delay), a bus, and a tri-state's z.
    /// </summary>
    /// <remarks>
    /// Drawn through <see cref="BoardEditor"/> like any board, compiled to SystemVerilog and run on the
    /// engine, so every value, every change travelling and the one swallowed is the engine's, never
    /// animated to look right:
    /// - Y = A AND NOT A, the NOT 3 ns and the AND rise 2 / fall 3 ns. A rises at 6 and the NOT
    ///   takes 3 to follow, so for those 3 ns both inputs are 1: the glitch, which passes.
    ///   The reference raised A at 2; on a real simulator NOT A is still x until 3, which puts the
    ///   AND at x until 7 -- the reference was drawn by hand. Raising A at 6 shows the glitch alone.
    /// - Y2 is the same with the NOT at 1 ns and the AND at 2: a 1 ns glitch, shorter than the AND's
    ///   delay, so the AND swallows it and the change dies on its wire.
    /// - OUT is S, a 4-bit bus, through a 2 ns buffer: 3, then A, then 5.
    /// - Q is D through a tri-state buffer, z until its enable rises at 17.
    /// Laid out on the grid as the mock was, the A that feeds both a NOT and an AND split by a tap, and
    /// the outputs far enough right for each delay to be written over the wire leaving its gate.
    /// </remarks>
    public static class ReferenceCircuit
    {
        /// <summary>How far the run goes, in nanoseconds: past the last change, so the waveform shows it settled.</summary>
        public const long EndTime = 26;

        /// <summary>The circuit as a document, laid out, with the testbench's steps.</summary>
        public static Project Document()
        {
            var editor = new BoardEditor(Project.New("workbench"));
            const string m = "workbench";

            int Add(Part part, double x, double y)
            {
                EditResult added = editor.Add(m, part, new Placement(x, y));
                if (!added.Done)
                    throw new InvalidOperationException(added.Refusal);
                return added.Id;
            }

            int Wire(Endpoint from, Endpoint to, Board.Route route = null)
            {
                EditResult wired = editor.Connect(m, from, to, route);
                if (!wired.Done)
                    throw new InvalidOperationException(wired.Refusal);
                return wired.Id;
            }

            Endpoint At(int part, string pin) => Endpoint.At(part, pin);

            int In(string name, double y, int width = 1) =>
                Add(new PortPart { Name = name, Width = width }, 1, y);

            int Out(string name, double y, int width = 1) =>
                Add(new PortPart { Name = name, Direction = PortDirection.Output, Width = width }, 15, y);

            int Gate(EngineGate kind, double x, double y, long rise, long fall, int width = 1) =>
                Add(new GatePart { Kind = kind, Inputs = kind == EngineGate.Not || kind == EngineGate.Buf ? 1 : 2, Rise = rise, Fall = fall, Width = width }, x, y);

            // The glitch that passes, and the one that is swallowed.
            foreach ((string a, string y, string n, double row, long not, long riseAnd, long fallAnd) in new[]
            {
                ("a", "y", "na", 0.0, 3L, 2L, 3L),
                ("b", "y2", "nb", -2.0, 1L, 2L, 2L),
            })
            {
                int input = In(a, row), output = Out(y, row - 0.25);
                int inverter = Gate(EngineGate.Not, 5, row - 1, not, not);
                int and = Gate(EngineGate.And, 10, row - 0.25, riseAnd, fallAnd);

                int straight = Wire(At(input, "out"), At(and, "a"));
                Wire(At(inverter, "a"), Endpoint.OnWire(straight), new Board.Route { TapAt = 1.0 / 6 });
                int inverted = Wire(At(inverter, "y"), At(and, "b"));
                editor.NameWire(m, inverted, n);
                Wire(At(and, "y"), At(output, "in"));
            }

            // A bus through a buffer.
            int s = In("s", -4, 4), outBus = Out("out", -4, 4);
            int buffer = Gate(EngineGate.Buf, 7, -4, 2, 2, width: 4);
            Wire(At(s, "out"), At(buffer, "a"));
            Wire(At(buffer, "y"), At(outBus, "in"));

            // A tri-state's z.
            // The enable comes up into the triangle from just below it, as the reference draws it.
            int d = In("d", -5.25), en = In("en", -6.25), q = Out("q", -5.25);
            int tri = Add(new GatePart { Kind = EngineGate.Bufif1, Inputs = 2, Rise = 1, Fall = 1 }, 7, -5.25);
            Wire(At(d, "out"), At(tri, "a"));
            Wire(At(en, "out"), At(tri, "en"));
            Wire(At(tri, "y"), At(q, "in"));

            LogicVec Bit(int value) => LogicVec.FromUInt64((ulong)value, 1);
            LogicVec Nibble(int value) => LogicVec.FromUInt64((ulong)value, 4);

            EditResult set = editor.SetStimulus(new BoardStimulus
            {
                Tracks = new[]
                {
                    new BoardTrack { Port = "a", Steps = new[] { new Step(0, Bit(0)), new Step(6, Bit(1)) } },
                    new BoardTrack { Port = "b", Steps = new[] { new Step(0, Bit(0)), new Step(13, Bit(1)) } },
                    new BoardTrack { Port = "s", Steps = new[] { new Step(1, Nibble(0x3)), new Step(10, Nibble(0xA)), new Step(19, Nibble(0x5)) } },
                    new BoardTrack { Port = "d", Steps = new[] { new Step(0, Bit(1)) } },
                    new BoardTrack { Port = "en", Steps = new[] { new Step(0, Bit(0)), new Step(17, Bit(1)) } },
                },
                End = EndTime,
            });
            if (!set.Done)
                throw new InvalidOperationException(set.Refusal);

            return editor.Project;
        }

        /// <summary>The document compiled, run to <see cref="EndTime"/> and laid out: the workbench's model.</summary>
        public static WorkbenchModel Model() => WorkbenchModel.Of(Document());
    }
}
