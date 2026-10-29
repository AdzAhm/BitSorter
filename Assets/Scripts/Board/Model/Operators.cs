using System;
using BitSorter.Sim;

namespace BitSorter.Board
{
    /// <summary>An operator of the language an <see cref="OperatorPart"/> is: the binary ones, then the unary.</summary>
    public enum OperatorKind
    {
        Add, Subtract, Multiply, Divide, Modulo,
        And, Or, Xor, Xnor,
        Less, LessOrEqual, Greater, GreaterOrEqual,
        Equal, NotEqual, CaseEqual, CaseNotEqual,
        LogicalAnd, LogicalOr,
        ShiftLeft, ShiftRight, ArithmeticShiftLeft, ArithmeticShiftRight,

        Negate, Not, LogicalNot,
        ReduceAnd, ReduceOr, ReduceXor, ReduceNand, ReduceNor, ReduceXnor,
    }

    /// <summary>
    /// What each <see cref="OperatorKind"/> is (Part 15h-3): how SystemVerilog writes it, how many operands it takes, how wide
    /// its answer is and whether a sign changes it -- one table, so the part, its rules, its code and its drawing agree.
    /// </summary>
    public static class Operators
    {
        /// <summary>Every operator, in the order the enum gives them.</summary>
        public static readonly OperatorKind[] All = (OperatorKind[])Enum.GetValues(typeof(OperatorKind));

        /// <summary>Whether it takes one operand, a.</summary>
        public static bool IsUnary(OperatorKind op) => op >= OperatorKind.Negate;

        /// <summary>Whether its answer is one bit: a comparison, a logical operator, a reduction (IEEE 1800, table 11-21).</summary>
        public static bool OneBit(OperatorKind op) =>
            (op >= OperatorKind.Less && op <= OperatorKind.LogicalOr) || op == OperatorKind.LogicalNot || op >= OperatorKind.ReduceAnd;

        /// <summary>Whether it is a shift, whose amount, b, comes in on top as an ALU's control does.</summary>
        public static bool IsShift(OperatorKind op) => op >= OperatorKind.ShiftLeft && op <= OperatorKind.ArithmeticShiftRight;

        /// <summary>Whether b may be a width of its own: a shift's amount, and a logical operator's operand, each self-determined.</summary>
        public static bool BOwnWidth(OperatorKind op) => IsShift(op) || op == OperatorKind.LogicalAnd || op == OperatorKind.LogicalOr;

        /// <summary>Whether signed operands give another answer: a comparison, a division, a remainder, an arithmetic right shift.</summary>
        public static bool SignMatters(OperatorKind op) =>
            (op >= OperatorKind.Less && op <= OperatorKind.GreaterOrEqual) || op == OperatorKind.Divide || op == OperatorKind.Modulo
            || op == OperatorKind.ArithmeticShiftRight;

        /// <summary>How SystemVerilog writes it: <c>+</c>, <c>&lt;&lt;&lt;</c>, <c>~&amp;</c>.</summary>
        public static string Token(OperatorKind op)
        {
            switch (op)
            {
                case OperatorKind.Add: return "+";
                case OperatorKind.Subtract: return "-";
                case OperatorKind.Multiply: return "*";
                case OperatorKind.Divide: return "/";
                case OperatorKind.Modulo: return "%";
                case OperatorKind.And: return "&";
                case OperatorKind.Or: return "|";
                case OperatorKind.Xor: return "^";
                case OperatorKind.Xnor: return "~^";
                case OperatorKind.Less: return "<";
                case OperatorKind.LessOrEqual: return "<=";
                case OperatorKind.Greater: return ">";
                case OperatorKind.GreaterOrEqual: return ">=";
                case OperatorKind.Equal: return "==";
                case OperatorKind.NotEqual: return "!=";
                case OperatorKind.CaseEqual: return "===";
                case OperatorKind.CaseNotEqual: return "!==";
                case OperatorKind.LogicalAnd: return "&&";
                case OperatorKind.LogicalOr: return "||";
                case OperatorKind.ShiftLeft: return "<<";
                case OperatorKind.ShiftRight: return ">>";
                case OperatorKind.ArithmeticShiftLeft: return "<<<";
                case OperatorKind.ArithmeticShiftRight: return ">>>";
                case OperatorKind.Negate: return "-";
                case OperatorKind.Not: return "~";
                case OperatorKind.LogicalNot: return "!";
                case OperatorKind.ReduceAnd: return "&";
                case OperatorKind.ReduceOr: return "|";
                case OperatorKind.ReduceXor: return "^";
                case OperatorKind.ReduceNand: return "~&";
                case OperatorKind.ReduceNor: return "~|";
                default: return "~^";
            }
        }

        /// <summary>
        /// The operator SystemVerilog writes as <paramref name="token"/>: a binary one, or with <paramref name="unary"/> one
        /// before its operand -- where both have it, as <c>&amp;</c> and <c>-</c> do; false for none.
        /// </summary>
        public static bool TryToken(string token, bool unary, out OperatorKind op)
        {
            OperatorKind? other = null;
            foreach (OperatorKind kind in All)
            {
                if (Token(kind) != token)
                    continue;
                if (IsUnary(kind) == unary)
                {
                    op = kind;
                    return true;
                }
                other ??= kind;
            }

            op = other ?? default;
            return other != null;
        }

        /// <summary>What it is called in a sentence: "an adder", "a comparison".</summary>
        public static string Called(OperatorKind op)
        {
            switch (op)
            {
                case OperatorKind.Add: return "an adder";
                case OperatorKind.Subtract: return "a subtractor";
                case OperatorKind.Multiply: return "a multiplier";
                case OperatorKind.Divide: return "a divider";
                case OperatorKind.Modulo: return "a remainder";
                case OperatorKind.Negate: return "a negation";
                default:
                    if (op <= OperatorKind.Xnor || op == OperatorKind.Not)
                        return "a bitwise operator";
                    if (op <= OperatorKind.CaseNotEqual)
                        return "a comparison";
                    if (IsShift(op))
                        return "a shifter";
                    if (op >= OperatorKind.ReduceAnd)
                        return "a reduction";
                    return "a logical operator";
            }
        }

        /// <summary>The engine's own operator for a binary kind; false for a unary one.</summary>
        public static bool TryBinary(OperatorKind op, out BinaryOp binary)
        {
            binary = default;
            if (IsUnary(op))
                return false;
            binary = (BinaryOp)Enum.Parse(typeof(BinaryOp), op.ToString());
            return true;
        }

        /// <summary>The engine's own operator for a unary kind; false for a binary one.</summary>
        public static bool TryUnary(OperatorKind op, out UnaryOp unary)
        {
            unary = default;
            if (!IsUnary(op))
                return false;
            unary = (UnaryOp)Enum.Parse(typeof(UnaryOp), op.ToString());
            return true;
        }

        /// <summary>The part's operator for one of the engine's; false for one no part is -- <c>==?</c>.</summary>
        public static bool TryFrom(BinaryOp binary, out OperatorKind op) => Enum.TryParse(binary.ToString(), out op) && !IsUnary(op);

        /// <summary>The part's operator for one of the engine's; false for one no part is -- <c>$countones</c>, <c>$onehot</c>.</summary>
        public static bool TryFrom(UnaryOp unary, out OperatorKind op) => Enum.TryParse(unary.ToString(), out op) && IsUnary(op);
    }
}
