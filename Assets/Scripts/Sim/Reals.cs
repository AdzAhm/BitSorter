using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// SystemVerilog's <c>real</c> (Part L7), as BitSorter holds it: a 2-state 64-bit value whose bits are an
    /// IEEE 754 double. The engine moves it as bits like any value; what makes it a number is the expression
    /// nodes here, which every real operation is built from (IEEE 1800, 6.12 and 11.3.1).
    /// </summary>
    internal static class Reals
    {
        public static double Of(in LogicVec bits)
        {
            bits.Slice(0, Math.Min(64, bits.Width)).ToTwoState().TryGetUInt64(out ulong raw);
            return BitConverter.Int64BitsToDouble(unchecked((long)raw));
        }

        public static LogicVec Bits(double value) => LogicVec.FromUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)), 64).ToTwoState();

        /// <summary>A whole number as a real (IEEE 1800, 6.12.2): an unknown bit counts as 0; a signed value keeps its sign.</summary>
        public static double FromInteger(in LogicVec value, bool signed)
        {
            LogicVec known = value.ToTwoState();
            if (known.Width <= 64)
            {
                if (signed && known.IsSigned && known.TryGetInt64(out long s))
                    return s;
                if (signed && known.Width < 64 && known[known.Width - 1] == Logic4.One)
                {
                    known.AsSigned().TryGetInt64(out long negative);
                    return negative;
                }

                known.AsUnsigned().TryGetUInt64(out ulong u);
                return signed && known.Width == 64 ? unchecked((long)u) : u;
            }

            // Wider than 64 bits: a 32-bit word at a time from the top.
            bool negativeWide = signed && known[known.Width - 1] == Logic4.One;
            LogicVec magnitude = negativeWide ? LogicVec.Negate(known.AsSigned()).AsUnsigned() : known.AsUnsigned();
            double result = 0;
            for (int lowest = (magnitude.Width - 1) / 32 * 32; lowest >= 0; lowest -= 32)
            {
                magnitude.Slice(lowest, Math.Min(32, magnitude.Width - lowest)).TryGetUInt64(out ulong word);
                result = result * 4294967296.0 + word;
            }

            return negativeWide ? -result : result;
        }

        /// <summary>A real as a whole number of <paramref name="width"/> bits (IEEE 1800, 6.12.2): rounded, half away from zero.</summary>
        public static LogicVec ToInteger(double value, int width)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return LogicVec.FromUInt64(0, width);

            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            long whole = rounded >= 9.2233720368547758E18 ? long.MaxValue : rounded <= -9.2233720368547758E18 ? long.MinValue : (long)rounded;
            return LogicVec.FromInt64(whole, 64).Extend(Math.Max(width, 64), true).Slice(0, width);
        }

        /// <summary>A real literal's value: <c>2.5</c>, <c>1e-3</c>, <c>1_000.5</c>.</summary>
        public static bool TryParse(string text, out double value) =>
            double.TryParse(text.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        /// <summary>
        /// A real as <c>%f</c>, <c>%e</c> or <c>%g</c> write it, as C's printf does (IEEE 1800, 21.2.1.3), with
        /// <paramref name="precision"/> places (6 when it is negative); <paramref name="alternate"/> keeps a
        /// <c>%g</c>'s trailing zeros, as <c>%#g</c> does -- how a real printed with no format looks in Icarus.
        /// </summary>
        public static string Format(double value, char conversion, int precision, bool alternate)
        {
            if (double.IsNaN(value))
                return "nan";
            if (double.IsInfinity(value))
                return value > 0 ? "inf" : "-inf";

            int places = precision < 0 ? 6 : precision;
            switch (conversion)
            {
                case 'f':
                    return value.ToString("F" + places, CultureInfo.InvariantCulture);

                case 'e':
                    return Exponent(value, places);

                default:
                {
                    int significant = places == 0 ? 1 : places;
                    int exponent = value == 0 ? 0 : (int)Math.Floor(Math.Log10(Math.Abs(value)));

                    // Rounding to the significant digits may carry into the next power of ten.
                    if (value != 0)
                    {
                        double scaled = Math.Round(Math.Abs(value) / Math.Pow(10, exponent - significant + 1), MidpointRounding.AwayFromZero);
                        if (scaled >= Math.Pow(10, significant))
                            exponent++;
                    }

                    string text = exponent < -4 || exponent >= significant
                        ? Exponent(value, significant - 1)
                        : value.ToString("F" + Math.Max(0, significant - 1 - exponent), CultureInfo.InvariantCulture);

                    if (alternate)
                        return text.Contains(".") || text.Contains("e") ? text : text + ".";

                    // %g drops trailing zeros, and the point if nothing follows it.
                    int e = text.IndexOf('e');
                    string mantissa = e >= 0 ? text.Substring(0, e) : text;
                    string tail = e >= 0 ? text.Substring(e) : string.Empty;
                    if (mantissa.Contains("."))
                        mantissa = mantissa.TrimEnd('0').TrimEnd('.');
                    return mantissa + tail;
                }
            }
        }

        /// <summary>C's <c>%e</c>: one digit, a point, the places, then e, a sign and at least two digits of exponent.</summary>
        private static string Exponent(double value, int places)
        {
            string text = value.ToString((places > 0 ? "0." + new string('0', places) : "0") + "e+00", CultureInfo.InvariantCulture);
            return text;
        }
    }

    /// <summary>The functions of reals (IEEE 1800, 20.8).</summary>
    internal enum RealFunction : byte
    {
        Sqrt, Ln, Log10, Exp, Pow, Floor, Ceil, Sin, Cos, Tan, Asin, Acos, Atan, Atan2, Hypot, Sinh, Cosh, Tanh, Asinh, Acosh, Atanh,
    }

    public abstract partial class Expr
    {
        /// <summary>A whole number as a real.</summary>
        internal static Expr IntegerToReal(Expr a, bool signed) => new RealOfExpr(a, signed);

        /// <summary>A real as a whole number of <paramref name="width"/> bits: rounded half away from zero, or, for <c>$rtoi</c>, cut toward zero.</summary>
        internal static Expr RealToInteger(Expr a, int width, bool truncate = false) => new IntegerOfExpr(a, width, truncate);

        /// <summary><c>a + b</c>, <c>-</c>, <c>*</c>, <c>/</c> and <c>**</c> on reals, by the operator's first character (<c>*</c> for <c>**</c> is <c>^</c>); <c>-a</c> is <c>'-'</c> from <c>null</c>.</summary>
        internal static Expr RealArithmetic(char op, Expr a, Expr b) => new RealArithmeticExpr(op, a, b);

        /// <summary>A comparison of reals: one bit.</summary>
        internal static Expr RealCompare(BinaryOp op, Expr a, Expr b) => new RealCompareExpr(op, a, b);

        /// <summary><c>$sqrt</c>, <c>$pow</c> and the others.</summary>
        internal static Expr RealCall(RealFunction function, Expr[] arguments) => new RealFunctionExpr(function, arguments);

        /// <summary>
        /// A delay or a time in ticks: <paramref name="a"/> (a real when <paramref name="isReal"/>, a whole number otherwise)
        /// times <paramref name="scale"/> ticks a unit, rounded to a multiple of <paramref name="rounding"/>.
        /// </summary>
        internal static Expr Ticks(Expr a, bool isReal, bool signed, long scale, long rounding) => new TicksExpr(a, isReal, signed, scale, rounding);

        /// <summary>A time in ticks given in a module's units: rounded to whole units, or as a real (<c>$time</c>, <c>$realtime</c>).</summary>
        internal static Expr TimeInUnits(Expr ticks, long scale, bool real) => new TimeInUnitsExpr(ticks, scale, real);

        private abstract class RealNode : Expr
        {
            protected readonly Expr[] Operands;

            protected RealNode(params Expr[] operands) => Operands = operands;

            public override void CollectReads(ICollection<int> reads)
            {
                foreach (Expr operand in Operands)
                    operand?.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                foreach (Expr operand in Operands)
                    operand?.CollectCalls(calls);
            }

            internal override int Height => 1 + MaxHeight(Operands);
        }

        private sealed class RealOfExpr : RealNode
        {
            private readonly bool _signed;
            public RealOfExpr(Expr a, bool signed) : base(a) => _signed = signed;
            public override int Width => 64;
            public override LogicVec Evaluate(LogicVec[] values) => Reals.Bits(Reals.FromInteger(Operands[0].Evaluate(values), _signed));
        }

        private sealed class IntegerOfExpr : RealNode
        {
            private readonly int _width;
            private readonly bool _truncate;

            public IntegerOfExpr(Expr a, int width, bool truncate) : base(a)
            {
                _width = width;
                _truncate = truncate;
            }

            public override int Width => _width;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                double value = Reals.Of(Operands[0].Evaluate(values));
                return Reals.ToInteger(_truncate ? Math.Truncate(value) : value, _width);
            }
        }

        private sealed class RealArithmeticExpr : RealNode
        {
            private readonly char _op;
            public RealArithmeticExpr(char op, Expr a, Expr b) : base(a, b) => _op = op;
            public override int Width => 64;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                double a = Operands[0] == null ? 0 : Reals.Of(Operands[0].Evaluate(values));
                double b = Reals.Of(Operands[1].Evaluate(values));
                switch (_op)
                {
                    case '+': return Reals.Bits(a + b);
                    case '-': return Reals.Bits(a - b);
                    case '*': return Reals.Bits(a * b);
                    case '/': return Reals.Bits(a / b);
                    default: return Reals.Bits(Math.Pow(a, b));
                }
            }
        }

        private sealed class RealCompareExpr : RealNode
        {
            private readonly BinaryOp _op;
            public RealCompareExpr(BinaryOp op, Expr a, Expr b) : base(a, b) => _op = op;
            public override int Width => 1;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                double a = Reals.Of(Operands[0].Evaluate(values)), b = Reals.Of(Operands[1].Evaluate(values));
                bool holds;
                switch (_op)
                {
                    case BinaryOp.Less: holds = a < b; break;
                    case BinaryOp.LessOrEqual: holds = a <= b; break;
                    case BinaryOp.Greater: holds = a > b; break;
                    case BinaryOp.GreaterOrEqual: holds = a >= b; break;
                    case BinaryOp.NotEqual: case BinaryOp.CaseNotEqual: holds = a != b; break;
                    case BinaryOp.LogicalAnd: holds = a != 0 && b != 0; break;
                    case BinaryOp.LogicalOr: holds = a != 0 || b != 0; break;
                    default: holds = a == b; break;
                }

                return LogicVec.FromUInt64(holds ? 1UL : 0UL, 1);
            }
        }

        private sealed class RealFunctionExpr : RealNode
        {
            private readonly RealFunction _function;
            public RealFunctionExpr(RealFunction function, Expr[] arguments) : base(arguments) => _function = function;
            public override int Width => 64;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                double a = Reals.Of(Operands[0].Evaluate(values));
                double b = Operands.Length > 1 ? Reals.Of(Operands[1].Evaluate(values)) : 0;
                double r;
                switch (_function)
                {
                    case RealFunction.Sqrt: r = Math.Sqrt(a); break;
                    case RealFunction.Ln: r = Math.Log(a); break;
                    case RealFunction.Log10: r = Math.Log10(a); break;
                    case RealFunction.Exp: r = Math.Exp(a); break;
                    case RealFunction.Pow: r = Math.Pow(a, b); break;
                    case RealFunction.Floor: r = Math.Floor(a); break;
                    case RealFunction.Ceil: r = Math.Ceiling(a); break;
                    case RealFunction.Sin: r = Math.Sin(a); break;
                    case RealFunction.Cos: r = Math.Cos(a); break;
                    case RealFunction.Tan: r = Math.Tan(a); break;
                    case RealFunction.Asin: r = Math.Asin(a); break;
                    case RealFunction.Acos: r = Math.Acos(a); break;
                    case RealFunction.Atan: r = Math.Atan(a); break;
                    case RealFunction.Atan2: r = Math.Atan2(a, b); break;
                    case RealFunction.Hypot: r = Math.Sqrt(a * a + b * b); break;
                    case RealFunction.Sinh: r = Math.Sinh(a); break;
                    case RealFunction.Cosh: r = Math.Cosh(a); break;
                    case RealFunction.Tanh: r = Math.Tanh(a); break;
                    case RealFunction.Asinh: r = Math.Log(a + Math.Sqrt(a * a + 1)); break;
                    case RealFunction.Acosh: r = Math.Log(a + Math.Sqrt(a * a - 1)); break;
                    default: r = 0.5 * Math.Log((1 + a) / (1 - a)); break;
                }

                return Reals.Bits(r);
            }
        }

        private sealed class TicksExpr : RealNode
        {
            private readonly bool _isReal, _signed;
            private readonly long _scale, _rounding;
            private readonly LogicVec _unknown = LogicVec.AllX(64);

            public TicksExpr(Expr a, bool isReal, bool signed, long scale, long rounding) : base(a)
            {
                _isReal = isReal;
                _signed = signed;
                _scale = scale;
                _rounding = Math.Max(1, rounding);
            }

            public override int Width => 64;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                LogicVec value = Operands[0].Evaluate(values);
                if (!_isReal && !value.IsKnown)
                    return _unknown;   // an unknown delay is 0, as the engine reads it

                double units = _isReal ? Reals.Of(value) : Reals.FromInteger(value, _signed);
                double steps = Math.Round(units * _scale / _rounding, MidpointRounding.AwayFromZero);
                if (double.IsNaN(steps) || steps <= 0)
                    return LogicVec.FromUInt64(0, 64);
                return LogicVec.FromUInt64(steps * _rounding >= 9.2E18 ? ulong.MaxValue >> 1 : (ulong)(steps * _rounding), 64);
            }
        }

        private sealed class TimeInUnitsExpr : RealNode
        {
            private readonly long _scale;
            private readonly bool _real;

            public TimeInUnitsExpr(Expr ticks, long scale, bool real) : base(ticks)
            {
                _scale = scale;
                _real = real;
            }

            public override int Width => 64;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                Operands[0].Evaluate(values).TryGetUInt64(out ulong ticks);
                if (_real)
                    return Reals.Bits((double)ticks / _scale);
                return LogicVec.FromUInt64((ticks + (ulong)_scale / 2) / (ulong)_scale, 64);
            }
        }
    }
}
