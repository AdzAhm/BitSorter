using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// BitSorter's own cell library, in the style of the gate libraries a course hands out: a module a
    /// gate, inputs A to D and output Z, with a rise delay (<c>Tpdlh</c>, output low to high) and a fall
    /// delay (<c>Tpdhl</c>) that an instance sets: <c>NAND2 #(.Tpdlh(5), .Tpdhl(5)) g1 (...)</c>. A
    /// gate with delay prints as one of these and an ideal gate as a primitive (section 7). Written for
    /// BitSorter; the same cells as the oracle corpus's <c>structural/cells.sv</c>, and more inputs.
    /// </summary>
    public static class CellLibrary
    {
        /// <summary>The file a printed board's cells go in.</summary>
        public const string FileName = "bitsorter_cells.sv";

        /// <summary>The most inputs a cell has; a gate with more prints as a primitive with its delay.</summary>
        public const int MaxInputs = 4;

        public const string Output = "Z";
        public const string Rise = "Tpdlh";
        public const string Fall = "Tpdhl";

        /// <summary>The cell for a one-bit gate, or null when there is none.</summary>
        public static string CellFor(GateKind kind, int inputs)
        {
            switch (kind)
            {
                case GateKind.Not: return inputs == 1 ? "INV" : null;
                case GateKind.Buf: return inputs == 1 ? "BUF" : null;
            }

            if (inputs < 2 || inputs > MaxInputs)
                return null;

            switch (kind)
            {
                case GateKind.And: return "AND" + inputs;
                case GateKind.Or: return "OR" + inputs;
                case GateKind.Nand: return "NAND" + inputs;
                case GateKind.Nor: return "NOR" + inputs;
                case GateKind.Xor: return "XOR" + inputs;
                case GateKind.Xnor: return "XNOR" + inputs;
                default: return null;
            }
        }

        /// <summary>The <paramref name="index"/>th input of a cell: A, B, C, D.</summary>
        public static string Input(int index) => ((char)('A' + index)).ToString();

        /// <summary>A cell's module, from its name: INV, BUF, AND2 to XNOR4.</summary>
        public static ModuleDeclaration Build(string cell)
        {
            (string primitive, int inputs) = Parse(cell);

            var module = new ModuleDeclaration { Name = cell, HasPortList = true };
            for (int i = 0; i < inputs; i++)
                module.Ports.Add(new PortDeclaration { Direction = PortDirection.Input, Type = Sv.Logic(1), Name = Input(i) });
            module.Ports.Add(new PortDeclaration { Direction = PortDirection.Output, Type = Sv.Logic(1), Name = Output });

            foreach (string delay in new[] { Rise, Fall })
                module.Items.Add(new ParameterDeclaration { Type = new DataType(), Name = delay, Value = Sv.Number(1) });

            var gate = new PrimitiveInstantiation { Primitive = primitive, Delay = new DelayControl { Parenthesized = true } };
            gate.Delay.Values.Add(Sv.Name(Rise));
            gate.Delay.Values.Add(Sv.Name(Fall));

            var instance = new PrimitiveInstance { Name = "g" };
            instance.Terminals.Add(Sv.Name(Output));
            for (int i = 0; i < inputs; i++)
                instance.Terminals.Add(Sv.Name(Input(i)));
            gate.Instances.Add(instance);
            module.Items.Add(gate);
            return module;
        }

        private static (string Primitive, int Inputs) Parse(string cell)
        {
            switch (cell)
            {
                case "INV": return ("not", 1);
                case "BUF": return ("buf", 1);
            }

            int digits = cell.Length - 1;
            return (cell.Substring(0, digits).ToLowerInvariant(), cell[digits] - '0');
        }
    }
}
