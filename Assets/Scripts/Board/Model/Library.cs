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
    /// How many of one part a design may have (Part 15b), as an exercise stocks them: a gate of a kind, of any number of
    /// inputs, or a block of a module. Counted by <see cref="PartCounts"/>.
    /// </summary>
    public sealed record PartCount
    {
        public PartCount(string part, int most)
        {
            Part = part;
            Most = most;
        }

        /// <summary>A gate kind's name in lower case -- <c>xor</c>, <c>not</c> (<see cref="Of"/>) -- or a module's name, for a block of it.</summary>
        public string Part { get; init; }

        /// <summary>The most there may be: 0 for none.</summary>
        public int Most { get; init; }

        /// <summary>What a gate of <paramref name="kind"/> is counted as.</summary>
        public static string Of(GateKind kind) => kind.ToString().ToLowerInvariant();

        /// <summary>What the part is called on the screen: a gate's kind in capitals, a block by its module.</summary>
        public string Said => Enum.TryParse(Part, ignoreCase: true, out GateKind kind) && Of(kind) == Part ? Part.ToUpperInvariant() : Part;
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
        /// How many of each part the design may have (Part 15b), as an exercise stocks them; a part not listed has no count.
        /// Past its count a part is refused, where <see cref="GateBudget"/> only warns: the stock is the exercise's, as 4.x's
        /// parts list was, and the budget the student's own aim.
        /// </summary>
        public IReadOnlyList<PartCount> Counts { get; init; } = Array.Empty<PartCount>();

        /// <summary>The most of <paramref name="part"/> there may be; -1 for no count.</summary>
        public int MostOf(string part)
        {
            foreach (PartCount count in Counts)
                if (string.Equals(count.Part, part, StringComparison.Ordinal))
                    return count.Most;
            return -1;
        }

        /// <summary>The parts with a count: what <see cref="PartCounts"/> takes as counted whole.</summary>
        public HashSet<string> Counted()
        {
            var counted = new HashSet<string>(StringComparer.Ordinal);
            foreach (PartCount count in Counts)
                counted.Add(count.Part);
            return counted;
        }

        /// <summary>
        /// Whether <paramref name="a"/> and <paramref name="b"/> are the same library, cell for cell -- what record equality
        /// cannot say, since it compares the lists of cells by reference. Two nulls are alike: no library either way.
        /// </summary>
        public static bool Alike(GateLibrary a, GateLibrary b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Name != b.Name || a.NoConstants != b.NoConstants || a.GateBudget != b.GateBudget
                || a.Cells.Count != b.Cells.Count || a.Counts.Count != b.Counts.Count)
                return false;
            for (int i = 0; i < a.Cells.Count; i++)
                if (!Equals(a.Cells[i], b.Cells[i]))
                    return false;
            for (int i = 0; i < a.Counts.Count; i++)
                if (!Equals(a.Counts[i], b.Counts[i]))
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

        /// <summary>
        /// The rules written code is held to under this library (Part 14g), for the module <paramref name="held"/>: its cells
        /// at their delays, every module that is a cell of BitSorter's, the parameters a cell's delays are set by, and
        /// whether constants are taken.
        /// </summary>
        public GateLevelRules RulesFor(string held)
        {
            var rules = new GateLevelRules { NoConstants = NoConstants, RiseParameter = CellLibrary.Rise, FallParameter = CellLibrary.Fall };
            rules.Held.Add(held);
            foreach (LibraryCell cell in Cells)
                rules.Cells.Add(new GateLevelCell(cell.Kind, cell.Inputs, cell.Rise, cell.Fall, cell.Name));
            foreach (LibraryCell cell in AllCells(0))
                if (CellLibrary.CellFor(cell.Kind, cell.Inputs) is string name)
                    rules.CellModules[name] = (cell.Kind, cell.Inputs);
            return rules;
        }

        /// <summary>What the library is, as text: what a reading of written code held to it is kept under (<c>WrittenModules</c>).</summary>
        public string Key()
        {
            var key = new System.Text.StringBuilder(NoConstants ? "c" : "n");
            foreach (LibraryCell cell in Cells)
                key.Append('|').Append((int)cell.Kind).Append(',').Append(cell.Inputs).Append(',').Append(cell.Rise).Append(',').Append(cell.Fall);
            return key.ToString();
        }

        /// <summary>The course's preset: every cell of BitSorter's library, each at <paramref name="delay"/>, constants allowed, no budget.</summary>
        public static GateLibrary CourseCells(long delay) => new GateLibrary { Name = CourseCellsName, Cells = AllCells(delay) };
    }
}
