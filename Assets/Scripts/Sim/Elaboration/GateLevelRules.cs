using System;
using System.Collections.Generic;

namespace BitSorter.Sim
{
    /// <summary>One cell a design held to a library may use: a gate of this kind and inputs, at these delays, by its name.</summary>
    public readonly struct GateLevelCell
    {
        public GateLevelCell(GateKind kind, int inputs, long rise, long fall, string name)
        {
            Kind = kind;
            Inputs = inputs;
            Rise = rise;
            Fall = fall;
            Name = name;
        }

        public GateKind Kind { get; }
        public int Inputs { get; }
        public long Rise { get; }
        public long Fall { get; }

        /// <summary>What the course calls it: <c>NAND2</c>, <c>INV</c>.</summary>
        public string Name { get; }
    }

    /// <summary>
    /// What a gate library holds written code to (Part 14g), for the elaborator to check as it builds the modules it names:
    /// a gate-level design, every gate one of the library's cells at the cell's delays -- written as a primitive or as an
    /// instance of the cell's module -- no logic written with operators, and no constant where the library takes none.
    /// </summary>
    /// <remarks>
    /// The engine knows no library: the document's (<c>GateLibrary</c>) is laid out here as the cells, the modules that are
    /// cells, and the parameters a cell's delays are set by, so the elaborator can hold code to it without knowing where it
    /// came from. A register written as an <c>always_ff</c> of plain writes is allowed, as a drawn register is.
    /// </remarks>
    public sealed class GateLevelRules
    {
        /// <summary>The modules held to the library, by name: every written one but the testbench.</summary>
        public HashSet<string> Held { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The cells the library has.</summary>
        public List<GateLevelCell> Cells { get; } = new List<GateLevelCell>();

        /// <summary>The modules that are cells, by name -- <c>NAND2</c> -- each the gate it is.</summary>
        public Dictionary<string, (GateKind Kind, int Inputs)> CellModules { get; } = new Dictionary<string, (GateKind, int)>(StringComparer.Ordinal);

        /// <summary>The parameters a cell's module takes its rise and fall delays from: <c>Tpdlh</c> and <c>Tpdhl</c>.</summary>
        public string RiseParameter { get; set; } = "Tpdlh";

        /// <inheritdoc cref="RiseParameter"/>
        public string FallParameter { get; set; } = "Tpdhl";

        /// <summary>Whether a constant is refused: the design takes its 0s and 1s from its inputs.</summary>
        public bool NoConstants { get; set; }

        /// <summary>The library's cell of <paramref name="kind"/> with <paramref name="inputs"/> inputs, or null.</summary>
        public GateLevelCell? CellFor(GateKind kind, int inputs)
        {
            foreach (GateLevelCell cell in Cells)
                if (cell.Kind == kind && cell.Inputs == inputs)
                    return cell;
            return null;
        }

        /// <summary>The cells' names, in order: <c>INV, NAND2, NAND3</c>.</summary>
        public string Listed()
        {
            var names = new List<string>(Cells.Count);
            foreach (GateLevelCell cell in Cells)
                names.Add(cell.Name);
            return names.Count == 0 ? "no cells" : string.Join(", ", names);
        }
    }
}
