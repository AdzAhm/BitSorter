using System;
using BitSorter.LogicCore;

namespace BitSorter.View
{
    /// <summary>The gates the player can place, in palette order (number keys 1-6).</summary>
    public enum GateKind
    {
        Not = 0,
        And = 1,
        Or = 2,
        Xor = 3,
        Nand = 4,
        Nor = 5,
    }

    /// <summary>
    /// Maps palette entries to LogicCore nodes. Lives view-side: a palette is a UI concept and the
    /// simulator has no notion of one.
    /// </summary>
    public static class GatePalette
    {
        public const int Count = 6;

        public static Node Create(GateKind kind)
        {
            switch (kind)
            {
                case GateKind.Not: return new NotGate { Name = "NOT" };
                case GateKind.And: return new AndGate { Name = "AND" };
                case GateKind.Or: return new OrGate { Name = "OR" };
                case GateKind.Xor: return new XorGate { Name = "XOR" };
                case GateKind.Nand: return new NandGate { Name = "NAND" };
                case GateKind.Nor: return new NorGate { Name = "NOR" };
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown gate kind.");
            }
        }

        /// <summary>
        /// Parses a kind name, case-insensitively. False for anything unrecognised.
        /// </summary>
        /// <remarks>
        /// The one place a gate name becomes a <see cref="GateKind"/>. Level files and save files
        /// both spell kinds as strings -- JsonUtility deserializes enums from integers only, never
        /// from names -- so without this there would be two parsers to keep in step.
        /// </remarks>
        public static bool TryParse(string text, out GateKind kind)
        {
            kind = GateKind.Not;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            switch (text.Trim().ToLowerInvariant())
            {
                case "not": kind = GateKind.Not; return true;
                case "and": kind = GateKind.And; return true;
                case "or": kind = GateKind.Or; return true;
                case "xor": kind = GateKind.Xor; return true;
                case "nand": kind = GateKind.Nand; return true;
                case "nor": kind = GateKind.Nor; return true;
                default: return false;
            }
        }

        /// <summary>How many input ports a gate of this kind has.</summary>
        public static int InputsOf(GateKind kind) => kind == GateKind.Not ? 1 : 2;

        public static string Label(GateKind kind)
        {
            switch (kind)
            {
                case GateKind.Not: return "NOT";
                case GateKind.And: return "AND";
                case GateKind.Or: return "OR";
                case GateKind.Xor: return "XOR";
                case GateKind.Nand: return "NAND";
                case GateKind.Nor: return "NOR";
                default: return kind.ToString();
            }
        }
    }
}
