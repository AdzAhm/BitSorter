using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Sim;

namespace BitSorter.Board
{
    /// <summary>One cell of a gate library (Part 14d): a gate of this kind and this many inputs, at these delays.</summary>
    public sealed record LibraryCell
    {
        public LibraryCell(GateKind kind, int inputs, long rise, long fall)
        {
            Kind = kind;
            Inputs = inputs;
            Rise = rise;
            Fall = fall;
        }

        public GateKind Kind { get; init; }
        public int Inputs { get; init; }

        /// <summary>Tpdlh: how long a change to 1 takes to come out.</summary>
        public long Rise { get; init; }

        /// <summary>Tpdhl: a change to 0.</summary>
        public long Fall { get; init; }

        /// <summary>What the course calls it: <c>NAND2</c>, <c>INV</c>; a gate with no cell by its kind, and its inputs when it has a choice.</summary>
        public string Name => CellLibrary.CellFor(Kind, Inputs)
            ?? Kind.ToString().ToUpperInvariant() + (Inputs > 1 && !IsTriState(Kind) ? Inputs.ToString(CultureInfo.InvariantCulture) : "");

        private static bool IsTriState(GateKind kind) =>
            kind == GateKind.Bufif0 || kind == GateKind.Bufif1 || kind == GateKind.Notif0 || kind == GateKind.Notif1;
    }

    /// <summary>
    /// A gate library (Part 14d): the gates a design may be built of, each at the delays its cell has -- what the course's
    /// gate-level assignments hand out -- with, if asked, no constants and a budget of gates. Set on a project
    /// (<see cref="ProjectSettings.Library"/>), it is kept everywhere: PARTS offers only its gates, a gate placed takes its
    /// cell's delays and keeps them, and the rules refuse a gate it has no cell for, a gate off its cell's delays, a MUX --
    /// no cell, and so built of gates -- and a constant where constants are refused.
    /// </summary>
    /// <remarks>
    /// **The course's preset is BitSorter's own cells** (<see cref="CourseCells"/>): INV, BUF, and AND, OR, NAND, NOR, XOR
    /// and XNOR of two to four inputs, each at the default delay to begin with. The course's own table of delays is its
    /// handout's, which nothing here reads, so the table is the student's to fill in -- a delay changed is the library's,
    /// and every gate of that cell follows it (<see cref="BoardEditor.SetLibrary"/>).
    /// </remarks>
    public sealed record GateLibrary
    {
        /// <summary>What the course's preset is called, as the screen says it.</summary>
        public const string CourseCellsName = "COURSE CELLS";

        /// <summary>What a library is called once it is no preset's.</summary>
        public const string CustomName = "CUSTOM";

        public string Name { get; init; } = CustomName;

        public IReadOnlyList<LibraryCell> Cells { get; init; } = Array.Empty<LibraryCell>();

        /// <summary>Whether a constant is refused: the design takes its 0s and 1s from its inputs.</summary>
        public bool NoConstants { get; init; }

        /// <summary>How many gates the design may have, a wide gate's every bit counted; -1 for no budget.</summary>
        public int GateBudget { get; init; } = -1;

        /// <summary>
        /// Whether <paramref name="a"/> and <paramref name="b"/> are the same library, cell for cell -- what record equality
        /// cannot say, since it compares the lists of cells by reference. Two nulls are alike: no library either way.
        /// </summary>
        public static bool Alike(GateLibrary a, GateLibrary b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Name != b.Name || a.NoConstants != b.NoConstants || a.GateBudget != b.GateBudget
                || a.Cells.Count != b.Cells.Count)
                return false;
            for (int i = 0; i < a.Cells.Count; i++)
                if (!Equals(a.Cells[i], b.Cells[i]))
                    return false;
            return true;
        }

        /// <summary>The cell of <paramref name="kind"/> with <paramref name="inputs"/> inputs, or null when the library has none.</summary>
        public LibraryCell CellFor(GateKind kind, int inputs)
        {
            foreach (LibraryCell cell in Cells)
                if (cell.Kind == kind && cell.Inputs == inputs)
                    return cell;
            return null;
        }

        /// <summary>Whether any cell is a gate of <paramref name="kind"/>, of any number of inputs.</summary>
        public bool Allows(GateKind kind)
        {
            foreach (LibraryCell cell in Cells)
                if (cell.Kind == kind)
                    return true;
            return false;
        }

        /// <summary>The fewest inputs a cell of <paramref name="kind"/> has: what a gate of it is placed with. 0 for none.</summary>
        public int FewestInputs(GateKind kind)
        {
            int fewest = 0;
            foreach (LibraryCell cell in Cells)
                if (cell.Kind == kind && (fewest == 0 || cell.Inputs < fewest))
                    fewest = cell.Inputs;
            return fewest;
        }

        /// <summary>The library's cells' names, in its order: <c>INV, NAND2, NAND3</c>.</summary>
        public string Listed()
        {
            var names = new List<string>();
            foreach (LibraryCell cell in Cells)
                names.Add(cell.Name);
            return string.Join(", ", names);
        }

        /// <summary>Every cell BitSorter's library has, in its order, each at <paramref name="delay"/> rise and fall.</summary>
        public static IReadOnlyList<LibraryCell> AllCells(long delay)
        {
            var cells = new List<LibraryCell>
            {
                new LibraryCell(GateKind.Not, 1, delay, delay),
                new LibraryCell(GateKind.Buf, 1, delay, delay),
            };
            foreach (GateKind kind in new[] { GateKind.And, GateKind.Or, GateKind.Nand, GateKind.Nor, GateKind.Xor, GateKind.Xnor })
                for (int inputs = 2; inputs <= CellLibrary.MaxInputs; inputs++)
                    cells.Add(new LibraryCell(kind, inputs, delay, delay));
            return cells;
        }

        /// <summary>
        /// This library with <paramref name="kind"/> taken out, every cell of it, or put back -- every cell of it BitSorter
        /// has, at <paramref name="delay"/>: no preset's any more.
        /// </summary>
        public GateLibrary Toggled(GateKind kind, long delay)
        {
            var cells = new List<LibraryCell>();
            bool had = Allows(kind);
            foreach (LibraryCell cell in Cells)
                if (!had || cell.Kind != kind)
                    cells.Add(cell);
            if (!had)
                foreach (LibraryCell cell in AllCells(delay))
                    if (cell.Kind == kind)
                        cells.Add(cell);
            return this with { Name = CustomName, Cells = cells };
        }

        /// <summary>This library with <paramref name="cell"/>'s delays made <paramref name="rise"/> and <paramref name="fall"/>: no preset's any more.</summary>
        public GateLibrary Retimed(LibraryCell cell, long rise, long fall)
        {
            var cells = new List<LibraryCell>(Cells.Count);
            foreach (LibraryCell each in Cells)
                cells.Add(each.Kind == cell.Kind && each.Inputs == cell.Inputs ? each with { Rise = rise, Fall = fall } : each);
            return this with { Name = CustomName, Cells = cells };
        }

        /// <summary>The course's preset: every cell of BitSorter's library, each at <paramref name="delay"/>, constants allowed, no budget.</summary>
        public static GateLibrary CourseCells(long delay) => new GateLibrary { Name = CourseCellsName, Cells = AllCells(delay) };
    }
}
