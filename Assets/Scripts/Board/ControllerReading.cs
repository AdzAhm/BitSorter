using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Board
{
    /// <summary>
    /// A controller as the textbook draws one, read from its code (Part 15h-6e, Ahmad's choice, 2026-11-01): a state machine as
    /// its states and the ways between them, a decoder as its control table. What <see cref="ControllerReader"/> gives.
    /// </summary>
    /// <remarks>
    /// Drawn literally, the multicycle processor's controller -- one <c>case</c> of eleven states -- was 224 parts and some
    /// 400 wires no fit could read; the textbook draws it as eleven boxes and their arrows. Everything here is the code's own
    /// words, printed as written, and every constant in it is held to what the engine makes of the same code.
    /// </remarks>
    public sealed class ControllerReading
    {
        public ControllerReading(string module, MachineReading machine, IReadOnlyList<TableReading> tables, bool whole)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Machine = machine;
            Tables = tables ?? Array.Empty<TableReading>();
            Whole = whole;
        }

        /// <summary>The module read.</summary>
        public string Module { get; }

        /// <summary>Its state machine, or null for none.</summary>
        public MachineReading Machine { get; }

        /// <summary>Its decoders, each a table, in the order written.</summary>
        public IReadOnlyList<TableReading> Tables { get; }

        /// <summary>
        /// Whether the reading is the whole module -- every process of it a part of the machine or a table, and nothing else
        /// in it: no <c>assign</c>, no instance -- so it opens as the reading rather than as its drawing.
        /// </summary>
        public bool Whole { get; }
    }

    /// <summary>A value as the code writes it -- <c>2</c>, <c>ALU_PASSB</c>, <c>taken</c> -- for a signal, and what it is when it is a constant.</summary>
    public readonly struct ReadValue
    {
        public ReadValue(string signal, string text, LogicVec? constant, int width = 0)
        {
            Signal = signal;
            Text = text;
            Constant = constant;
            Width = width;
        }

        /// <summary>The signal's width as declared, 0 where it is not known: a one-bit output set to 1 is written by its name alone.</summary>
        public int Width { get; }

        /// <summary>The signal given the value.</summary>
        public string Signal { get; }

        /// <summary>The value as written, a literal's size left off: <c>2'd2</c> is <c>2</c>, <c>1'b1</c> is <c>1</c>.</summary>
        public string Text { get; }

        /// <summary>The value, where it is a number or a parameter of one; null for anything worked out as it runs.</summary>
        public LogicVec? Constant { get; }

        public override string ToString() => Signal + " = " + Text;
    }

    /// <summary>One way out of a state: to which, and what takes it.</summary>
    public readonly struct MachineArrow
    {
        public MachineArrow(int from, int to, string condition, IReadOnlyList<ReadValue> outputs = null)
        {
            From = from;
            To = to;
            Condition = condition;
            Outputs = outputs ?? Array.Empty<ReadValue>();
        }

        public int From { get; }
        public int To { get; }

        /// <summary>
        /// What takes it, in the code's words -- <c>opcode is OP_REG, OP_IMM or OP_LUI</c>, <c>!walk</c> -- or
        /// <see cref="Otherwise"/> where only what the other ways out do not take does; null where it is the only way out.
        /// </summary>
        public string Condition { get; }

        /// <summary>
        /// What the ways that take it set that the state's other ways do not -- a Mealy machine's outputs, written after the
        /// condition as the textbook writes them: <c>start / load = 1</c>.
        /// </summary>
        public IReadOnlyList<ReadValue> Outputs { get; }

        /// <summary>What an arrow taken when nothing else is says.</summary>
        public const string Otherwise = "otherwise";

        public override string ToString() => $"{From} -> {To}" + (Condition != null ? " [" + Condition + "]" : "");
    }

    /// <summary>A state machine: its register, its states, where it starts, the ways between them, and what each state sets.</summary>
    public sealed class MachineReading
    {
        public MachineReading(string register, string next, IReadOnlyList<string> states, IReadOnlyList<LogicVec> codes, int start,
            IReadOnlyList<MachineArrow> arrows, IReadOnlyList<IReadOnlyList<ReadValue>> settings)
        {
            Register = register;
            Next = next;
            States = states;
            Codes = codes;
            Start = start;
            Arrows = arrows;
            Settings = settings;
        }

        /// <summary>The register holding the state: <c>state</c>.</summary>
        public string Register { get; }

        /// <summary>The signal its next value is worked out in, <c>state_next</c>; the register itself where one process does both.</summary>
        public string Next { get; }

        /// <summary>The states, as the enum names them, in its order.</summary>
        public IReadOnlyList<string> States { get; }

        /// <summary>Each state's value in the register.</summary>
        public IReadOnlyList<LogicVec> Codes { get; }

        /// <summary>
        /// The state a reset puts it in -- or, with no reset, the value the register is declared with -- and -1 where the code
        /// says neither, rather than a guess.
        /// </summary>
        public int Start { get; }

        /// <summary>Every way out of every state, a loop where it stays.</summary>
        public IReadOnlyList<MachineArrow> Arrows { get; }

        /// <summary>
        /// What each state sets on every way out of it that differs from what is set before the <c>case</c>, in the order first
        /// written -- what a Moore machine's box says. What differs by way is on the arrows (<see cref="MachineArrow.Outputs"/>).
        /// </summary>
        public IReadOnlyList<IReadOnlyList<ReadValue>> Settings { get; }

        /// <summary>The state <paramref name="value"/> is, or -1 for none: a code no state has, an x.</summary>
        public int StateOf(LogicVec value)
        {
            if (!value.TryGetUInt64(out ulong v))
                return -1;
            for (int i = 0; i < Codes.Count; i++)
                if (Codes[i].TryGetUInt64(out ulong c) && c == v)
                    return i;
            return -1;
        }
    }

    /// <summary>One row of a control table: the labels that choose it and what it sets each column to.</summary>
    public sealed class TableRow
    {
        public TableRow(string label, IReadOnlyList<LogicVec> matches, bool otherwise, IReadOnlyList<TableCell> cells)
        {
            Label = label;
            Matches = matches;
            Otherwise = otherwise;
            Cells = cells;
        }

        /// <summary>Its labels as written, <c>OP_LOAD</c>, <c>3'b000</c>; <see cref="MachineArrow.Otherwise"/> for the default.</summary>
        public string Label { get; }

        /// <summary>The values its labels are, those that could be worked out from the code; none for the default.</summary>
        public IReadOnlyList<LogicVec> Matches { get; }

        /// <summary>Whether it is the <c>default</c> row.</summary>
        public bool Otherwise { get; }

        /// <summary>A cell for each column.</summary>
        public IReadOnlyList<TableCell> Cells { get; }
    }

    /// <summary>A cell of a control table: the value as written, and whether the row left it to what was set before the <c>case</c>.</summary>
    public readonly struct TableCell
    {
        public TableCell(string text, LogicVec? constant, bool fromDefault)
        {
            Text = text;
            Constant = constant;
            FromDefault = fromDefault;
        }

        /// <summary>The value as written; empty where nothing sets it.</summary>
        public string Text { get; }

        public LogicVec? Constant { get; }

        /// <summary>Whether the row leaves it to the value set before the <c>case</c>.</summary>
        public bool FromDefault { get; }
    }

    /// <summary>A decoder: a <c>case</c> on one subject, each label a row and each signal it sets a column.</summary>
    public sealed class TableReading
    {
        public TableReading(string subject, IReadOnlyList<string> columns, IReadOnlyList<TableRow> rows)
        {
            Subject = subject;
            Columns = columns;
            Rows = rows;
        }

        /// <summary>What the <c>case</c> is on, as written: <c>opcode</c>.</summary>
        public string Subject { get; }

        /// <summary>The signals it sets, those set before the <c>case</c> first.</summary>
        public IReadOnlyList<string> Columns { get; }

        public IReadOnlyList<TableRow> Rows { get; }

        /// <summary>The row <paramref name="subject"/>'s value picks: the first whose labels hold it, else the default's, else -1.</summary>
        public int RowOf(LogicVec subject)
        {
            if (subject.TryGetUInt64(out ulong v))
                for (int r = 0; r < Rows.Count; r++)
                    foreach (LogicVec match in Rows[r].Matches)
                        if (match.TryGetUInt64(out ulong m) && m == v)
                            return r;
            if (!subject.IsKnown)
                return -1;
            for (int r = 0; r < Rows.Count; r++)
                if (Rows[r].Otherwise)
                    return r;
            return -1;
        }
    }
}
