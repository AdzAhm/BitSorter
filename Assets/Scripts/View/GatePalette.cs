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
