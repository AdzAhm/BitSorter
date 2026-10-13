using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>Builds boards for tests through the editor, as the view will, a line a part or wire.</summary>
    internal sealed class Bench
    {
        private int _column;

        public Bench(string module = "top")
        {
            Editor = new BoardEditor(Project.New(module));
            Module = module;
        }

        public BoardEditor Editor { get; }
        public string Module { get; set; }
        public Project Project => Editor.Project;
        public Module Current => Project.Find(Module);

        public static int Must(EditResult result)
        {
            Assert.IsTrue(result.Done, "refused: " + result.Refusal);
            return result.Id;
        }

        public Placement Next() => new Placement(_column++, 0);

        public int Add(Part part) => Must(Editor.Add(Module, part, Next()));

        public int Input(string name, int width = 1) =>
            Add(new PortPart { Name = name, Direction = PortDirection.Input, Width = width });

        public int Output(string name, int width = 1) =>
            Add(new PortPart { Name = name, Direction = PortDirection.Output, Width = width });

        public int Gate(GateKind kind, string name = null, int inputs = 2, long rise = 0, long fall = 0, int width = 1) =>
            Add(new GatePart { Kind = kind, Name = name, Inputs = kind == GateKind.Not || kind == GateKind.Buf ? 1 : inputs, Rise = rise, Fall = fall, Width = width });

        public int Register(string name = null, int width = 1, bool reset = false, LogicVec? initial = null) =>
            Add(new RegisterPart { Name = name, Width = width, HasReset = reset, Initial = initial });

        public int Wire(int from, string fromPin, int to, string toPin) =>
            Must(Editor.Connect(Module, Endpoint.At(from, fromPin), Endpoint.At(to, toPin)));

        /// <summary>From a port, gate, register ... to another: an input port's pin is "out", an output's "in".</summary>
        public EditResult TryWire(int from, string fromPin, int to, string toPin) =>
            Editor.Connect(Module, Endpoint.At(from, fromPin), Endpoint.At(to, toPin));

        public Netlist Nets => Netlist.Of(Project, Current);

        public string NetName(int part, string pin) => Nets.NetOf(new PinRef(part, pin)).Name;

        /// <summary>
        /// A half adder in the course's style: s = a ^ b, c = a &amp; b, the gates given the delays
        /// asked for.
        /// </summary>
        public static Bench HalfAdder(long rise = 0, long fall = 0, string module = "half_adder")
        {
            var b = new Bench(module);
            int a = b.Input("a"), bb = b.Input("b"), s = b.Output("s"), c = b.Output("c");
            int x = b.Gate(GateKind.Xor, rise: rise, fall: fall), n = b.Gate(GateKind.And, rise: rise, fall: fall);
            b.Wire(a, "out", x, "a");
            b.Wire(bb, "out", x, "b");
            b.Wire(a, "out", n, "a");
            b.Wire(bb, "out", n, "b");
            b.Wire(x, "y", s, "in");
            b.Wire(n, "y", c, "in");
            return b;
        }
    }
}
