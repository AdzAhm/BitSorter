using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>Small builders for the syntax nodes a board compiles to, so the compiler reads as the code it makes.</summary>
    internal static class Sv
    {
        public static NameExpression Name(string name)
        {
            var e = new NameExpression();
            e.Path.Add(name);
            return e;
        }

        /// <summary>A whole number written in decimal: a delay, an index, a parameter's value.</summary>
        public static NumberLiteral Number(long value) => Literal(value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>A value at its own width: <c>4'b10x1</c>, or hex when it is wide, whole and known.</summary>
        public static NumberLiteral Value(LogicVec value)
        {
            bool hex = value.Width > 16 && value.Width % 4 == 0 && value.IsKnown;
            return Literal(value.ToLiteral(hex ? Radix.Hex : Radix.Binary));
        }

        private static NumberLiteral Literal(string text)
        {
            LiteralResult read = SystemVerilog.Literal.Parse(text);
            return new NumberLiteral { Text = text, Value = read.Value };
        }

        /// <summary><c>logic</c>, with <c>[width-1:0]</c> when it is wider than a bit.</summary>
        public static DataType Logic(int width)
        {
            var type = new DataType { Keyword = "logic" };
            if (width > 1)
                type.Packed.Add(new Dimension { Left = Number(width - 1), Right = Number(0) });
            return type;
        }

        public static IndexExpression Bit(string name, int index) =>
            new IndexExpression { Target = Name(name), Index = Number(index) };

        public static Expression Not(Expression e) => new UnaryExpression { Operator = "~", Operand = Paren(e) };

        /// <summary><c>$signed(e)</c>: the same bits, taken as signed.</summary>
        public static Expression Signed(Expression e)
        {
            var call = new CallExpression { Name = "$signed", HasParentheses = true };
            call.Arguments.Add(e);
            return call;
        }

        public static Expression Paren(Expression e) =>
            e is NameExpression || e is NumberLiteral || e is IndexExpression || e is ParenthesizedExpression || e is ConcatenationExpression
                ? e
                : new ParenthesizedExpression { Inner = e };

        public static Expression Chain(string op, IReadOnlyList<Expression> operands)
        {
            Expression e = operands[0];
            for (int i = 1; i < operands.Count; i++)
                e = new BinaryExpression { Operator = op, Left = e, Right = operands[i] };
            return e;
        }

        /// <summary><c>#5</c> when rise and fall agree, <c>#(2, 3)</c> when they do not; null for none.</summary>
        public static DelayControl Delay(long rise, long fall)
        {
            if (rise == 0 && fall == 0)
                return null;

            var delay = new DelayControl { Parenthesized = rise != fall };
            delay.Values.Add(Number(rise));
            if (rise != fall)
                delay.Values.Add(Number(fall));
            return delay;
        }

        public static ContinuousAssign Assign(Expression target, Expression value, DelayControl delay = null)
        {
            var assign = new ContinuousAssign { Delay = delay };
            assign.Assignments.Add(new Assignment { Target = target, Value = value });
            return assign;
        }

        public static AssignmentStatement Set(Expression target, Expression value, bool nonBlocking = false) =>
            new AssignmentStatement { Target = target, Operator = nonBlocking ? "<=" : "=", Value = value };

        public static PortConnection Connect(string port, Expression value) => new PortConnection { Name = port, Value = value };

        public static EventControl On(params (string Edge, string Signal)[] events)
        {
            var control = new EventControl();
            foreach ((string edge, string signal) in events)
                control.Events.Add(new EventExpression { Edge = edge, Value = Name(signal) });
            return control;
        }
    }
}
