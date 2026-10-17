using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>What an event control waits for on one of its expressions.</summary>
    public enum EdgeKind : byte
    {
        /// <summary><c>@(a)</c>: any change of value.</summary>
        Any,

        /// <summary><c>@(posedge a)</c>: the lowest bit goes 0 to 1, 0 to x or z, or x or z to 1.</summary>
        Posedge,

        /// <summary><c>@(negedge a)</c>: the lowest bit goes 1 to 0, 1 to x or z, or x or z to 0.</summary>
        Negedge,

        /// <summary><c>@(edge a)</c>: either.</summary>
        Edge,
    }

    /// <summary>How a <c>case</c> compares its subject with a label.</summary>
    public enum CaseKind : byte
    {
        /// <summary><c>case</c>: every bit the same, x and z included, as <c>===</c> compares.</summary>
        Exact,

        /// <summary><c>casez</c>: a z (or <c>?</c>) bit on either side matches anything.</summary>
        Z,

        /// <summary><c>casex</c>: an x or z bit on either side matches anything.</summary>
        X,
    }

    internal enum OpCode : byte
    {
        /// <summary>Blocking: <see cref="Instruction.Target"/> takes <see cref="Instruction.Value"/> at once.</summary>
        Assign,

        /// <summary>
        /// Nonblocking: the value is worked out now and written in this time step's NBA region, or
        /// after <see cref="Instruction.Other"/> time units when that is set.
        /// </summary>
        AssignLater,

        Jump,

        /// <summary>Jumps unless <see cref="Instruction.Value"/> is true; x, z and 0 all jump.</summary>
        JumpUnless,

        /// <summary>Jumps if <see cref="Instruction.Value"/> is true.</summary>
        JumpIf,

        /// <summary>Jumps if the subject (<see cref="Instruction.Value"/>) matches the label (<see cref="Instruction.Other"/>).</summary>
        JumpIfMatch,

        /// <summary>Waits <see cref="Instruction.Value"/> time units; <c>#0</c> waits for the inactive region.</summary>
        Delay,

        /// <summary>Waits for the event control <see cref="Instruction.Wait"/>.</summary>
        WaitEvent,

        /// <summary><c>wait (c)</c>: goes on if <see cref="Instruction.Value"/> is true, else waits until it may be.</summary>
        WaitUntil,

        /// <summary><c>$finish</c> or <c>$fatal</c>: the run ends.</summary>
        Finish,

        /// <summary><c>$stop</c>: the run is suspended, and carries on from the next statement when it is run again.</summary>
        Stop,

        /// <summary><c>$display</c> and its kind: a line for the transcript (<see cref="Instruction.Print"/>).</summary>
        Print,

        /// <summary><c>$readmemh</c> or <c>$readmemb</c>: a memory image into a memory (<see cref="Instruction.Memory"/>).</summary>
        ReadMemory,

        /// <summary><c>$writememh</c> or <c>$writememb</c>: a memory out to a file.</summary>
        WriteMemory,

        /// <summary>The process has nothing left to do.</summary>
        End,
    }

    /// <summary>One expression of an event control: <c>posedge clk</c>.</summary>
    internal sealed class EventItem
    {
        public EventItem(EdgeKind edge, Expr value)
        {
            Edge = edge;
            Value = value;
        }

        public EdgeKind Edge { get; }
        public Expr Value { get; }
    }

    /// <summary>An event control's expressions: <c>@(posedge clk, posedge rst)</c> is two.</summary>
    internal sealed class WaitSpec
    {
        public WaitSpec(EventItem[] items) => Items = items;

        public EventItem[] Items { get; set; }
    }

    /// <summary>
    /// One piece of what an assignment writes: bits of a variable from <see cref="Lowest"/>, this
    /// many, or from wherever <see cref="Offset"/> says when the select is worked out at run time
    /// (<c>y[i] = a</c>). In a memory, the word is <see cref="Word"/> after <see cref="Signal"/>, worked
    /// out at run time too (<c>mem[addr] &lt;= d</c>). A piece whose place is unknown or outside the
    /// variable or the memory writes nothing, as the standard has it.
    /// </summary>
    internal readonly struct LPiece
    {
        public LPiece(int signal, int lowest, int width, Expr offset, Expr word = null, int depth = 0)
        {
            Signal = signal;
            Lowest = lowest;
            Width = width;
            Offset = offset;
            Word = word;
            Depth = depth;
        }

        public readonly int Signal;
        public readonly int Lowest;
        public readonly int Width;
        public readonly Expr Offset;

        /// <summary>Which word of a memory, counted from its lowest; null when the piece is not in one.</summary>
        public readonly Expr Word;

        /// <summary>How many words the memory has.</summary>
        public readonly int Depth;
    }

    /// <summary>A <c>$readmemh</c>, <c>$readmemb</c>, <c>$writememh</c> or <c>$writememb</c>.</summary>
    internal sealed class MemoryTask
    {
        public string Task;
        public MemoryInfo Memory;
        public bool Hex;
        public string File;

        /// <summary>The first and last word to load or write, as declared; null for the memory's own ends.</summary>
        public Expr Start;

        /// <inheritdoc cref="Start"/>
        public Expr End;

        /// <summary>Where the call is written, for messages.</summary>
        public string Where;
    }

    internal struct Instruction
    {
        public OpCode Op;
        public Expr Value;
        public Expr Other;

        /// <summary>The pieces written, most significant first, and their total width.</summary>
        public LPiece[] Target;
        public int TargetWidth;

        public int Jump;
        public CaseKind Case;
        public WaitSpec Wait;

        /// <summary>For <see cref="OpCode.Finish"/>: which task, and where it is written.</summary>
        public string Text;

        public PrintSpec Print;
        public MemoryTask Memory;
    }

    /// <summary>
    /// A process: an <c>initial</c>, an <c>always</c> of any kind, compiled to a flat array of
    /// instructions and run by a program counter (ADR-5). <c>if</c>, <c>case</c> and the loops are
    /// jumps; a delay or an event control saves the counter and hands back to the scheduler.
    /// </summary>
    public sealed class ProcessInfo
    {
        internal ProcessInfo(string name, string kind, Instruction[] code, int[] writes)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Writes = writes ?? Array.Empty<int>();

            int items = 0;
            foreach (Instruction i in code)
                if (i.Wait != null)
                    items = Math.Max(items, i.Wait.Items.Length);
            MaxItems = items;
        }

        /// <summary>Where it is and what it is: <c>traffic_light.always_ff@21</c>.</summary>
        public string Name { get; }

        /// <summary><c>initial</c>, <c>always</c>, <c>always_ff</c>, <c>always_comb</c> or <c>always_latch</c>.</summary>
        public string Kind { get; }

        public int InstructionCount => Code.Length;

        internal Instruction[] Code { get; }

        /// <summary>The signals it writes.</summary>
        internal int[] Writes { get; }

        /// <summary>The most expressions any one of its event controls waits on.</summary>
        internal int MaxItems { get; }

        /// <summary>Every signal its expressions read, for the scheduler to wake it on.</summary>
        internal void CollectWaitReads(int pc, ICollection<int> reads)
        {
            WaitSpec wait = Code[pc].Wait;
            if (wait == null)
                return;

            foreach (EventItem item in wait.Items)
                item.Value.CollectReads(reads);
        }

        public override string ToString() => Name;
    }

    /// <summary>The bit-by-bit rules for <c>case</c>, <c>casez</c> and <c>casex</c>, and for edges.</summary>
    internal static class Matching
    {
        /// <summary>Whether a subject matches a label; the two are already the same width.</summary>
        public static bool CaseMatches(in LogicVec subject, in LogicVec label, CaseKind kind)
        {
            int width = Math.Min(subject.Width, label.Width);
            for (int i = 0; i < width; i++)
            {
                Logic4 a = subject[i], b = label[i];
                if (a == b)
                    continue;

                if (kind == CaseKind.Z && (a == Logic4.Z || b == Logic4.Z))
                    continue;

                if (kind == CaseKind.X && (!a.IsKnown() || !b.IsKnown()))
                    continue;

                return false;
            }

            return true;
        }

        /// <summary>Whether going from <paramref name="before"/> to <paramref name="after"/> is the edge an item waits for (IEEE 1800, 9.4.2).</summary>
        public static bool Fires(EdgeKind edge, in LogicVec before, in LogicVec after)
        {
            if (edge == EdgeKind.Any)
                return !SameBits(before, after);

            Logic4 from = before[0], to = after[0];
            if (from == to)
                return false;

            bool rises = from == Logic4.Zero || to == Logic4.One;
            bool falls = from == Logic4.One || to == Logic4.Zero;

            // x to z and z to x are neither.
            if (!from.IsKnown() && !to.IsKnown())
                return false;

            switch (edge)
            {
                case EdgeKind.Posedge: return rises;
                case EdgeKind.Negedge: return falls;
                default: return rises || falls;
            }
        }

        /// <summary>The same bits, whatever either's signedness.</summary>
        public static bool SameBits(in LogicVec a, in LogicVec b)
        {
            if (a.Width != b.Width)
                return false;

            return a.IsSigned == b.IsSigned ? a.Equals(b) : a.AsUnsigned().Equals(b.AsUnsigned());
        }
    }
}
