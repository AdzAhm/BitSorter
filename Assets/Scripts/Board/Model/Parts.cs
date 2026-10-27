using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    public enum PinDirection { In, Out }

    /// <summary>A place on a part a wire can end on: its name, which way the value goes, and its width.</summary>
    public readonly struct Pin
    {
        public Pin(string name, PinDirection direction, int width)
        {
            Name = name;
            Direction = direction;
            Width = width;
        }

        public readonly string Name;
        public readonly PinDirection Direction;
        public readonly int Width;

        public override string ToString() => $"{Name} ({Direction}, {Width})";
    }

    /// <summary>
    /// Something placed on a drawn module: a port, a gate, a register, a block. Immutable: an edit makes
    /// a new part, which is what lets undo keep whole documents for nothing (section 5).
    /// </summary>
    /// <remarks>
    /// <see cref="Id"/> is the part's identity, given by the module and never reused; <see cref="Name"/>
    /// is what the printed SystemVerilog calls it -- an instance name, a port's name, or the start of
    /// the name of the net it drives -- and the player may change it.
    /// </remarks>
    public abstract record Part
    {
        public int Id { get; init; }
        public string Name { get; init; }

        /// <summary>
        /// The pins, in a fixed order. A block's are the ports of the module it names, which is why the
        /// project is asked; every other part ignores it.
        /// </summary>
        public abstract IReadOnlyList<Pin> Pins(Project project);

        public bool TryPin(Project project, string name, out Pin pin)
        {
            foreach (Pin p in Pins(project))
            {
                if (string.Equals(p.Name, name, StringComparison.Ordinal))
                {
                    pin = p;
                    return true;
                }
            }

            pin = default;
            return false;
        }
    }

    /// <summary>
    /// One of the module's ports, drawn as a pin on the board: an input is where a value comes in -- the
    /// testbench drives it, a clock included -- and an output where one leaves.
    /// </summary>
    public sealed record PortPart : Part
    {
        public PortDirection Direction { get; init; } = PortDirection.Input;
        public int Width { get; init; } = 1;
        public string Comment { get; init; }

        /// <summary>The one pin: an input's drives the board, an output's reads it.</summary>
        public string PinName => Direction == PortDirection.Input ? "out" : "in";

        public override IReadOnlyList<Pin> Pins(Project project) =>
            new[] { new Pin(PinName, Direction == PortDirection.Input ? PinDirection.Out : PinDirection.In, Width) };
    }

    /// <summary>
    /// A gate: one of the primitives, on single bits or bitwise on vectors. Its delay is rise and fall,
    /// 0 being ideal: an ideal one-bit gate prints as a primitive, one with delay as a cell of
    /// BitSorter's library, a vector one as an <c>assign</c>.
    /// </summary>
    public sealed record GatePart : Part
    {
        /// <summary>The most inputs a gate takes.</summary>
        public const int MaxInputs = 8;

        public GateKind Kind { get; init; } = GateKind.And;

        /// <summary>How many inputs: one for <c>not</c> and <c>buf</c>, two to <see cref="MaxInputs"/> for the rest.</summary>
        public int Inputs { get; init; } = 2;

        public int Width { get; init; } = 1;
        public long Rise { get; init; }
        public long Fall { get; init; }

        public bool IsIdeal => Rise == 0 && Fall == 0;

        /// <summary>
        /// Whether it is a tri-state gate -- <c>bufif0</c>, <c>bufif1</c>, <c>notif0</c>, <c>notif1</c> --
        /// whose output is z while its control says so. One bit, one driver of its net: buses with
        /// several drivers arrive in Part 14.
        /// </summary>
        public bool IsTriState => IsTriStateKind(Kind);

        public static bool IsTriStateKind(GateKind kind) =>
            kind == GateKind.Bufif0 || kind == GateKind.Bufif1 || kind == GateKind.Notif0 || kind == GateKind.Notif1;

        /// <summary>The <paramref name="index"/>th input's name: a, b, c ...</summary>
        public static string InputName(int index) => ((char)('a' + index)).ToString();

        /// <summary>The <paramref name="index"/>th input of a gate of <paramref name="kind"/>: a, b, c ..., or a tri-state gate's a and en.</summary>
        public static string InputPin(GateKind kind, int index) => IsTriStateKind(kind) && index == 1 ? "en" : InputName(index);

        public override IReadOnlyList<Pin> Pins(Project project)
        {
            var pins = new Pin[Inputs + 1];
            for (int i = 0; i < Inputs; i++)
                pins[i] = new Pin(InputPin(Kind, i), PinDirection.In, IsTriState ? 1 : Width);
            pins[Inputs] = new Pin("y", PinDirection.Out, Width);
            return pins;
        }
    }

    /// <summary>A fixed value, of any width, x and z included.</summary>
    public sealed record ConstantPart : Part
    {
        public LogicVec Value { get; init; } = LogicVec.FromUInt64(0, 1);

        public override IReadOnlyList<Pin> Pins(Project project) => new[] { new Pin("y", PinDirection.Out, Value.Width) };
    }

    /// <summary>
    /// A register: q takes d on the rising edge of clk. With a reset, a rising rst puts the reset value
    /// in at once (an asynchronous, active-high reset, as the course writes them). Without either, it
    /// starts as x -- or as <see cref="Initial"/>, which is how a board that started at 0 in 4.x keeps
    /// doing so.
    /// </summary>
    public sealed record RegisterPart : Part
    {
        public int Width { get; init; } = 1;
        public bool HasReset { get; init; }

        /// <summary>What a reset puts in; zero when not given.</summary>
        public LogicVec? ResetValue { get; init; }

        /// <summary>The value it holds before the first edge; null for x.</summary>
        public LogicVec? Initial { get; init; }

        /// <summary>
        /// Its clock to Q (Part 14a): how long after the clock's edge q changes. The run shows it -- it compiles as
        /// <c>q &lt;= #Tcq d</c> -- and static timing starts a path there. 0, as before Part 14, for an ideal register.
        /// </summary>
        public long Tcq { get; init; }

        /// <summary>
        /// Its setup (Part 14a): how long before the clock's edge d must be still. Static timing adds it to a path's end;
        /// the run does not check it, as the course's simulations do not.
        /// </summary>
        public long Tsetup { get; init; }

        public override IReadOnlyList<Pin> Pins(Project project)
        {
            var pins = new List<Pin> { new Pin("d", PinDirection.In, Width), new Pin("clk", PinDirection.In, 1) };
            if (HasReset)
                pins.Add(new Pin("rst", PinDirection.In, 1));
            pins.Add(new Pin("q", PinDirection.Out, Width));
            return pins;
        }
    }

    /// <summary>A multiplexer: y is d<i>s</i>, for two, four or eight inputs.</summary>
    public sealed record MuxPart : Part
    {
        public int Inputs { get; init; } = 2;
        public int Width { get; init; } = 1;

        /// <summary>How wide s is: one bit for two inputs, two for four, three for eight.</summary>
        public int SelectWidth => Inputs <= 2 ? 1 : Inputs <= 4 ? 2 : 3;

        public override IReadOnlyList<Pin> Pins(Project project)
        {
            var pins = new Pin[Inputs + 2];
            for (int i = 0; i < Inputs; i++)
                pins[i] = new Pin("d" + i, PinDirection.In, Width);
            pins[Inputs] = new Pin("s", PinDirection.In, SelectWidth);
            pins[Inputs + 1] = new Pin("y", PinDirection.Out, Width);
            return pins;
        }
    }

    /// <summary>A bus taken apart: o<i>i</i> is bit <i>i</i> of in.</summary>
    public sealed record SplitterPart : Part
    {
        public int Width { get; init; } = 2;

        public override IReadOnlyList<Pin> Pins(Project project)
        {
            var pins = new Pin[Width + 1];
            pins[0] = new Pin("in", PinDirection.In, Width);
            for (int i = 0; i < Width; i++)
                pins[i + 1] = new Pin("o" + i, PinDirection.Out, 1);
            return pins;
        }
    }

    /// <summary>Bits made into a bus: bit <i>i</i> of out is i<i>i</i>.</summary>
    public sealed record JoinerPart : Part
    {
        public int Width { get; init; } = 2;

        public override IReadOnlyList<Pin> Pins(Project project)
        {
            var pins = new Pin[Width + 1];
            for (int i = 0; i < Width; i++)
                pins[i] = new Pin("i" + i, PinDirection.In, 1);
            pins[Width] = new Pin("out", PinDirection.Out, Width);
            return pins;
        }
    }

    /// <summary>Shows the value on its wire. Nothing of it is printed: it reads, and the board draws it.</summary>
    public sealed record LampPart : Part
    {
        public int Width { get; init; } = 1;

        public override IReadOnlyList<Pin> Pins(Project project) => new[] { new Pin("in", PinDirection.In, Width) };
    }

    /// <summary>
    /// An instance of another module of the project: its pins are that module's ports, drawn or written. With <see cref="Copies"/> above 1 it
    /// is an array of that many instances (Part 12c), each pin a bus of every copy's port side by side -- copy i's on bits
    /// i*w to i*w+w-1 -- printed as a <c>generate for</c>.
    /// </summary>
    public sealed record BlockPart : Part
    {
        /// <summary>How many copies an array of the block may have.</summary>
        public const int MaxCopies = 64;

        public string Module { get; init; }

        /// <summary>How many instances the block is, side by side: 1 for one (Part 12c).</summary>
        public int Copies { get; init; } = 1;

        public override IReadOnlyList<Pin> Pins(Project project)
        {
            Module module = project?.Find(Module);
            if (module == null)
                return Array.Empty<Pin>();

            int copies = Math.Max(1, Copies);
            var pins = new List<Pin>();
            foreach (ModulePort port in WrittenModules.PortsOf(project, module))
                if (port.Direction != PortDirection.Inout)   // no pin for an inout: the rules refuse a block of one (Part 14f)
                    pins.Add(new Pin(port.Name, port.Direction == PortDirection.Input ? PinDirection.In : PinDirection.Out, port.Width * copies));
            return pins;
        }
    }
}
