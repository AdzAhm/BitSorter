using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.Board
{
    /// <summary>
    /// What the parts a drawing of code makes up do, in words (Part 15h-4e, asked 2026-10-29: the parts the drawing makes up
    /// should say what they do -- only those): an operator, a mux, a case's choice, a slice, a join, a match, a constant, a
    /// process that is no circuit -- each in its operands' own names, read off the drawing's wires so it stays true after an
    /// edit: a signal's name where the wire into a pin holds one, else the expression of the part that drives it.
    /// </summary>
    public sealed partial class RtlDrawing
    {
        private readonly Dictionary<int, RtlCell> _cellOf;
        private readonly Dictionary<int, int> _caseSubjectWidth;
        private readonly Dictionary<int, List<ulong>> _matchSets;
        private readonly RtlNetlist _netlist;

        /// <summary>
        /// What the part <paramref name="part"/> of the drawing does, in a sentence, or null for one that goes by its own name
        /// -- a port, a register, a block, a gate the code names -- or none drawn from a cell.
        /// </summary>
        public string Describe(int part)
        {
            Part drawn = Module.FindPart(part);
            if (drawn == null || drawn is PortPart || drawn is RegisterPart || drawn is BlockPart || drawn is GatePart && !drawn.Name.StartsWith("$", StringComparison.Ordinal))
                return null;
            if (_caseSubjectWidth != null && _caseSubjectWidth.ContainsKey(part) && drawn is MuxPart chooser)
                return Operand(part, "s") + " picks which input reaches y: d" + "k when it is k, each of the " + chooser.Inputs.ToString(CultureInfo.InvariantCulture) + " values a case arm.";
            if (drawn is ConstantPart constant)
                return "The value " + constant.Value.ToLiteral(Radix.Binary) + ", as the code writes it.";
            if (_matchSets != null && _matchSets.TryGetValue(part, out List<ulong> set))
                return "1 when " + Operand(part, "a") + " is any of " + Join(set) + ": the labels of one arm, or a set the code tests.";
            if (_cellOf == null || !_cellOf.TryGetValue(part, out RtlCell cell))
                return null;

            string a = Operand(part, PinOf(drawn, 0)), b = Operand(part, PinOf(drawn, 1));
            string bits = Bits(cell.Output >= 0 ? WidthOfY(drawn) : 1);
            switch (cell.Kind)
            {
                case RtlKind.Binary:
                    return Binary(cell.Binary, a, b, bits, cell.Signed);
                case RtlKind.Unary:
                    return Unary(cell.Unary, a, bits);
                case RtlKind.Gate:
                    return a + " and the rest through a " + cell.Gate.ToString().ToUpperInvariant() + " gate.";
                case RtlKind.Mux:
                    return Operand(part, "s") + " chooses: " + Operand(part, "d1") + " when it is 1, " + Operand(part, "d0") + " when it is 0.";
                case RtlKind.Slice:
                    return cell.Width == 1
                        ? "Bit " + cell.Lowest.ToString(CultureInfo.InvariantCulture) + " of " + a + "."
                        : "Bits " + (cell.Lowest + cell.Width - 1).ToString(CultureInfo.InvariantCulture) + " to " + cell.Lowest.ToString(CultureInfo.InvariantCulture) + " of " + a + ".";
                case RtlKind.Concat:
                    return a + " and " + b + " side by side, " + a + " the high bits: " + bits + ".";
                case RtlKind.Replicate:
                    return a + " " + cell.Count.ToString(CultureInfo.InvariantCulture) + " times over: " + bits + ".";
                case RtlKind.Bit:
                    return "Bit " + b + " of " + a + ", the place worked out as it runs.";
                case RtlKind.Part:
                    return cell.Width.ToString(CultureInfo.InvariantCulture) + " bits of " + a + ", from the place " + b + " says.";
                case RtlKind.Resize:
                    return a + " made " + bits + (cell.Signed ? ", its sign copied into the new bits." : ", 0s in the new bits or the top ones cut.");
                case RtlKind.Signedness:
                    return a + " read as " + (cell.Signed ? "signed" : "unsigned") + ": the same bits.";
                case RtlKind.TwoState:
                    return a + " as a 2-state value: an x or a z reads as 0.";
                case RtlKind.Truth:
                    return "1 when " + a + " is true -- not 0, and no x or z -- as an if takes it.";
                case RtlKind.Match:
                    return "1 when " + a + " is " + (Label(cell) ?? b) + ": the case's label" + (cell.Case == CaseKind.Exact ? "." : ", an x or a z matching anything.");
                case RtlKind.Join:
                    return cell.Name + " put together from what drives it.";
                case RtlKind.Word:
                    return "The word of the memory " + cell.Name + " at the address " + a + ".";
                case RtlKind.Put:
                    return Operand(part, "old") + " with " + Operand(part, "v") + " written at the place " + Operand(part, "i") + ".";
                case RtlKind.Process:
                    return cell.Why;
                case RtlKind.Opaque:
                    return "A value the code works out as it runs -- a call, a string, a number that is no circuit.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The expression a part of the drawing works out, short, in its operands' names -- <c>a &amp; b</c>, <c>s ? b : a</c>,
        /// <c>a[3:0]</c> -- what a wire out of it is called where no signal holds it; null for a part with a name of its own.
        /// </summary>
        public string ExpressionOf(int part) => ExpressionOf(part, 0);

        private string ExpressionOf(int part, int depth)
        {
            Part drawn = Module.FindPart(part);
            if (drawn == null)
                return null;
            if (drawn is ConstantPart constant)
                return constant.Value.ToLiteral(constant.Value.Width > 16 && constant.Value.IsKnown ? Radix.Hex : Radix.Binary);
            if (!drawn.Name.StartsWith("$", StringComparison.Ordinal))
                return null;
            if (depth > 2)
                return "...";

            string In(string pin) => Operand(part, pin, depth + 1);
            if (_caseSubjectWidth != null && _caseSubjectWidth.ContainsKey(part))
                return "case (" + In("s") + ")";
            if (_matchSets != null && _matchSets.TryGetValue(part, out List<ulong> labels))
                return In("a") + " inside {" + Join(labels) + "}";
            if (_cellOf == null || !_cellOf.TryGetValue(part, out RtlCell cell))
                return drawn is OperatorPart op ? (Operators.IsUnary(op.Op) ? Operators.Token(op.Op) + In("a") : In("a") + " " + Operators.Token(op.Op) + " " + In("b")) : null;

            string a = In(PinOf(drawn, 0)), b = In(PinOf(drawn, 1));
            switch (cell.Kind)
            {
                case RtlKind.Binary:
                    return drawn is OperatorPart binary ? a + " " + Operators.Token(binary.Op) + " " + b : null;
                case RtlKind.Unary:
                    return drawn is OperatorPart unary ? Operators.Token(unary.Op) + a : null;
                case RtlKind.Mux:
                    return In("s") + " ? " + In("d1") + " : " + In("d0");
                case RtlKind.Slice:
                    return a + (cell.Width == 1 ? "[" + cell.Lowest.ToString(CultureInfo.InvariantCulture) + "]"
                        : "[" + (cell.Lowest + cell.Width - 1).ToString(CultureInfo.InvariantCulture) + ":" + cell.Lowest.ToString(CultureInfo.InvariantCulture) + "]");
                case RtlKind.Concat:
                    return "{" + a + ", " + b + "}";
                case RtlKind.Replicate:
                    return "{" + cell.Count.ToString(CultureInfo.InvariantCulture) + "{" + a + "}}";
                case RtlKind.Bit:
                    return a + "[" + b + "]";
                case RtlKind.Part:
                    return a + "[" + b + " +: " + cell.Width.ToString(CultureInfo.InvariantCulture) + "]";
                case RtlKind.Signedness:
                    return (cell.Signed ? "$signed(" : "$unsigned(") + a + ")";
                case RtlKind.Truth:
                    return "|" + a;
                case RtlKind.Match:
                    return a + " == " + (Label(cell) ?? b);
                case RtlKind.Resize:
                case RtlKind.TwoState:
                    return a;
                case RtlKind.Word:
                    return cell.Name + "[" + a + "]";
                case RtlKind.Join:
                    return cell.Name;
                case RtlKind.Put:
                    return In("old") + " with [" + In("i") + "] = " + In("v");
                default:
                    return drawn is BoxPart box ? box.Label : null;
            }
        }

        /// <summary>
        /// What reaches <paramref name="pin"/> of <paramref name="part"/>, by name: the signal on the wire into it, else what the
        /// part driving that wire works out; "nothing" with no wire to it.
        /// </summary>
        private string Operand(int part, string pin, int depth = 0)
        {
            if (pin == null)
                return "";
            foreach (Wire wire in Module.Wires)
            {
                if (wire.To.IsTap || wire.To.Pin.Part != part || wire.To.Pin.Pin != pin)
                    continue;
                if (!string.IsNullOrEmpty(wire.NetName) && wire.NetName[0] != '$')
                    return wire.NetName;
                if (wire.From.IsTap)
                    return "a wire";
                string expression = ExpressionOf(wire.From.Pin.Part, depth);
                if (expression == null)
                    return wire.NetName != null && wire.NetName[0] != '$' ? wire.NetName
                        : Module.FindPart(wire.From.Pin.Part) is BoxPart box ? box.Label + " " + wire.From.Pin.Pin : "a value worked out inside";
                return expression.IndexOf(' ') >= 0 ? "(" + expression + ")" : expression;
            }
            return "nothing";
        }

        /// <summary>The <paramref name="index"/>th input pin of a part, or null.</summary>
        private static string PinOf(Part part, int index)
        {
            int at = 0;
            foreach (Pin pin in part.Pins(null))
            {
                if (pin.Direction != PinDirection.In)
                    continue;
                if (at++ == index)
                    return pin.Name;
            }
            return null;
        }

        private static int WidthOfY(Part part)
        {
            foreach (Pin pin in part.Pins(null))
                if (pin.Direction == PinDirection.Out)
                    return pin.Width;
            return 1;
        }

        private string Label(RtlCell match)
        {
            if (match.Inputs.Count < 2 || match.Inputs[1] < 0 || _netlist == null)
                return null;
            int driver = _netlist.Nets[match.Inputs[1]].Driver;
            RtlCell constant = driver >= 0 ? _netlist.Cells[driver] : null;
            return constant != null && constant.Kind == RtlKind.Constant ? constant.Value.ToLiteral(Radix.Binary) : null;
        }

        private static string Join(List<ulong> labels)
        {
            var text = new List<string>();
            foreach (ulong label in labels)
                text.Add(label.ToString(CultureInfo.InvariantCulture));
            return string.Join(", ", text);
        }

        private static string Bits(int width) => width == 1 ? "1 bit" : width.ToString(CultureInfo.InvariantCulture) + " bits";

        private static string Binary(BinaryOp op, string a, string b, string bits, bool signed)
        {
            string asSigned = signed ? ", as signed numbers" : "";
            switch (op)
            {
                case BinaryOp.And: return a + " AND " + b + ", bit by bit: " + bits + ".";
                case BinaryOp.Or: return a + " OR " + b + ", bit by bit: " + bits + ".";
                case BinaryOp.Xor: return a + " XOR " + b + ", bit by bit: " + bits + ".";
                case BinaryOp.Xnor: return a + " XNOR " + b + ", bit by bit: " + bits + ".";
                case BinaryOp.Add: return a + " plus " + b + ": " + bits + ", a carry past them lost.";
                case BinaryOp.Subtract: return a + " minus " + b + ": " + bits + ".";
                case BinaryOp.Multiply: return a + " times " + b + ": " + bits + ".";
                case BinaryOp.Divide: return a + " divided by " + b + ", the remainder dropped: " + bits + ".";
                case BinaryOp.Modulo: return "What is left of " + a + " divided by " + b + ": " + bits + ".";
                case BinaryOp.ShiftLeft:
                case BinaryOp.ArithmeticShiftLeft: return a + " moved left " + b + " places, 0s in behind: " + bits + ".";
                case BinaryOp.ShiftRight: return a + " moved right " + b + " places, 0s in behind: " + bits + ".";
                case BinaryOp.ArithmeticShiftRight: return a + " moved right " + b + " places, " + (signed ? "its sign copied in behind" : "0s in behind") + ": " + bits + ".";
                case BinaryOp.Equal: return "1 when " + a + " equals " + b + ".";
                case BinaryOp.NotEqual: return "1 when " + a + " is not " + b + ".";
                case BinaryOp.CaseEqual: return "1 when " + a + " is " + b + " to the bit, x and z included.";
                case BinaryOp.CaseNotEqual: return "1 when " + a + " differs from " + b + " in any bit, x and z included.";
                case BinaryOp.Less: return "1 when " + a + " is less than " + b + asSigned + ".";
                case BinaryOp.LessOrEqual: return "1 when " + a + " is at most " + b + asSigned + ".";
                case BinaryOp.Greater: return "1 when " + a + " is more than " + b + asSigned + ".";
                case BinaryOp.GreaterOrEqual: return "1 when " + a + " is at least " + b + asSigned + ".";
                case BinaryOp.LogicalAnd: return "1 when " + a + " and " + b + " are both true.";
                case BinaryOp.LogicalOr: return "1 when " + a + " or " + b + " is true.";
                default: return a + " " + op + " " + b + ": " + bits + ".";
            }
        }

        private static string Unary(UnaryOp op, string a, string bits)
        {
            switch (op)
            {
                case UnaryOp.Not: return a + " with every bit flipped: " + bits + ".";
                case UnaryOp.Negate: return "Minus " + a + ", as two's complement: " + bits + ".";
                case UnaryOp.LogicalNot: return "1 when " + a + " is 0.";
                case UnaryOp.ReduceAnd: return "1 when every bit of " + a + " is 1.";
                case UnaryOp.ReduceOr: return "1 when any bit of " + a + " is 1.";
                case UnaryOp.ReduceXor: return "1 when " + a + " has an odd number of 1s.";
                case UnaryOp.ReduceNand: return "0 when every bit of " + a + " is 1.";
                case UnaryOp.ReduceNor: return "1 when every bit of " + a + " is 0.";
                case UnaryOp.ReduceXnor: return "1 when " + a + " has an even number of 1s.";
                default: return op + " of " + a + ": " + bits + ".";
            }
        }
    }
}
