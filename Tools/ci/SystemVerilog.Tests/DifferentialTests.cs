using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BitSorter.SystemVerilog.Tests
{
    /// <summary>
    /// Every operator agrees with <see cref="ReferenceVec"/>, the slow second implementation, on
    /// thousands of random values of widths from 1 to 130.
    /// </summary>
    /// <remarks>
    /// One test per operator, each with its own seed, so a failure names the operator and repeats
    /// exactly. Each compares the width, the signedness and every bit of the result.
    /// </remarks>
    public class DifferentialTests
    {
        private const int Cases = 2000;

        private delegate LogicVec Fast2(LogicVec a, LogicVec b);
        private delegate ReferenceVec Slow2(ReferenceVec a, ReferenceVec b);

        private static readonly Dictionary<string, (Fast2 Fast, Slow2 Slow)> Binary = new Dictionary<string, (Fast2, Slow2)>
        {
            ["&"] = (LogicVec.And, ReferenceVec.And),
            ["|"] = (LogicVec.Or, ReferenceVec.Or),
            ["^"] = (LogicVec.Xor, ReferenceVec.Xor),
            ["~^"] = (LogicVec.Xnor, ReferenceVec.Xnor),
            ["+"] = (LogicVec.Add, ReferenceVec.Add),
            ["-"] = (LogicVec.Subtract, ReferenceVec.Subtract),
            ["*"] = (LogicVec.Multiply, ReferenceVec.Multiply),
            ["/"] = (LogicVec.Divide, ReferenceVec.Divide),
            ["%"] = (LogicVec.Modulo, ReferenceVec.Modulo),
            ["<"] = (LogicVec.LessThan, ReferenceVec.LessThan),
            ["<="] = (LogicVec.LessOrEqual, ReferenceVec.LessOrEqual),
            [">"] = (LogicVec.GreaterThan, ReferenceVec.GreaterThan),
            [">="] = (LogicVec.GreaterOrEqual, ReferenceVec.GreaterOrEqual),
            ["=="] = (LogicVec.Equal, ReferenceVec.Equal),
            ["!="] = (LogicVec.NotEqual, ReferenceVec.NotEqual),
            ["==="] = (LogicVec.CaseEqual, ReferenceVec.CaseEqual),
            ["!=="] = (LogicVec.CaseNotEqual, ReferenceVec.CaseNotEqual),
            ["&&"] = (LogicVec.LogicalAnd, ReferenceVec.LogicalAnd),
            ["||"] = (LogicVec.LogicalOr, ReferenceVec.LogicalOr),
            ["{,}"] = (LogicVec.Concat, ReferenceVec.Concat),
        };

        private static readonly Dictionary<string, (Func<LogicVec, LogicVec> Fast, Func<ReferenceVec, ReferenceVec> Slow)> Unary =
            new Dictionary<string, (Func<LogicVec, LogicVec>, Func<ReferenceVec, ReferenceVec>)>
            {
                ["~"] = (LogicVec.Not, ReferenceVec.Not),
                ["-"] = (LogicVec.Negate, ReferenceVec.Negate),
                ["!"] = (LogicVec.LogicalNot, ReferenceVec.LogicalNot),
                ["&r"] = (LogicVec.ReduceAnd, ReferenceVec.ReduceAnd),
                ["|r"] = (LogicVec.ReduceOr, ReferenceVec.ReduceOr),
                ["^r"] = (LogicVec.ReduceXor, ReferenceVec.ReduceXor),
                ["~&r"] = (LogicVec.ReduceNand, ReferenceVec.ReduceNand),
                ["~|r"] = (LogicVec.ReduceNor, ReferenceVec.ReduceNor),
                ["~^r"] = (LogicVec.ReduceXnor, ReferenceVec.ReduceXnor),
            };

        private static readonly Dictionary<string, (Fast2 Fast, Slow2 Slow)> Shifts = new Dictionary<string, (Fast2, Slow2)>
        {
            ["<<"] = (LogicVec.ShiftLeft, ReferenceVec.ShiftLeft),
            [">>"] = (LogicVec.ShiftRight, ReferenceVec.ShiftRight),
            ["<<<"] = (LogicVec.ArithmeticShiftLeft, ReferenceVec.ShiftLeft),
            [">>>"] = (LogicVec.ArithmeticShiftRight, ReferenceVec.ArithmeticShiftRight),
        };

        private static IEnumerable<string> BinaryNames => Binary.Keys;
        private static IEnumerable<string> UnaryNames => Unary.Keys;
        private static IEnumerable<string> ShiftNames => Shifts.Keys;

        private static int SeedOf(string name)
        {
            int seed = 17;
            foreach (char c in name)
                seed = seed * 31 + c;
            return seed;
        }

        private static void Same(LogicVec fast, ReferenceVec slow, string what)
        {
            Assert.AreEqual(slow.Width, fast.Width, $"width of {what}");
            Assert.AreEqual(slow.Signed, fast.IsSigned, $"signedness of {what}");
            Assert.AreEqual(slow.Binary, fast.Format(Radix.Binary), what);
        }

        [TestCaseSource(nameof(BinaryNames))]
        public void ABinaryOperator_AgreesWithTheReference(string op)
        {
            var random = new RandomValues(SeedOf(op));
            (Fast2 fast, Slow2 slow) = Binary[op];

            for (int i = 0; i < Cases; i++)
            {
                LogicVec a = random.Value(random.Width());
                LogicVec b = random.Value(random.Width());

                // Division by a wide, mostly-known value is the common case; keep some small divisors too.
                if ((op == "/" || op == "%") && i % 3 == 0)
                    b = random.Count(random.Width(16), 9).Resize(b.Width);

                Same(fast(a, b), slow(ReferenceVec.Of(a), ReferenceVec.Of(b)), $"{a} {op} {b}");
            }
        }

        [TestCaseSource(nameof(UnaryNames))]
        public void AUnaryOperator_AgreesWithTheReference(string op)
        {
            var random = new RandomValues(SeedOf(op));
            (Func<LogicVec, LogicVec> fast, Func<ReferenceVec, ReferenceVec> slow) = Unary[op];

            for (int i = 0; i < Cases; i++)
            {
                LogicVec a = random.Value(random.Width());
                Same(fast(a), slow(ReferenceVec.Of(a)), $"{op}{a}");
            }
        }

        [TestCaseSource(nameof(ShiftNames))]
        public void AShift_AgreesWithTheReference(string op)
        {
            var random = new RandomValues(SeedOf(op));
            (Fast2 fast, Slow2 slow) = Shifts[op];

            for (int i = 0; i < Cases; i++)
            {
                LogicVec a = random.Value(random.Width());

                // Mostly a count from 0 to just past the width, so both the shift and the shift-everything-out are tested.
                LogicVec amount = random.Count(1 + random.Next(9), a.Width + 2);
                Same(fast(a, amount), slow(ReferenceVec.Of(a), ReferenceVec.Of(amount)), $"{a} {op} {amount}");
            }
        }

        [Test]
        public void Extension_AgreesWithTheReference()
        {
            var random = new RandomValues(SeedOf("extend"));

            for (int i = 0; i < Cases; i++)
            {
                LogicVec a = random.Value(random.Width());
                int width = random.Width();
                bool sign = random.Next(2) == 0;

                ReferenceVec slow = width >= a.Width
                    ? ReferenceVec.Of(a).Extend(width, sign)
                    : new ReferenceVec(ReferenceVec.Of(a).Bits[..width], a.IsSigned);

                Same(a.Extend(width, sign), slow, $"{a} extended to {width} ({(sign ? "sign" : "zero")})");
            }
        }

        [Test]
        public void Selects_AgreeWithTheReference()
        {
            var random = new RandomValues(SeedOf("select"));

            for (int i = 0; i < Cases; i++)
            {
                LogicVec a = random.Value(random.Width());

                int lowest = random.Next(a.Width + 8) - 4;
                int width = 1 + random.Next(Math.Min(130, a.Width + 4));
                Same(a.Slice(lowest, width), ReferenceVec.Of(a).Slice(lowest, width), $"{a}[{lowest} +: {width}]");

                LogicVec index = random.Count(1 + random.Next(9), a.Width + 3);
                Same(a.Bit(index), ReferenceVec.Of(a).Bit(ReferenceVec.Of(index)), $"{a}[{index}]");
            }
        }

        [Test]
        public void Replication_AgreesWithTheReference()
        {
            var random = new RandomValues(SeedOf("replicate"));

            for (int i = 0; i < Cases / 4; i++)
            {
                LogicVec a = random.Value(random.Width(40));
                int count = 1 + random.Next(5);
                Same(LogicVec.Replicate(a, count), ReferenceVec.Replicate(ReferenceVec.Of(a), count), $"{{{count}{{{a}}}}}");
            }
        }

        [Test]
        public void TheConditional_AgreesWithTheReference()
        {
            var random = new RandomValues(SeedOf("?:"));

            for (int i = 0; i < Cases; i++)
            {
                LogicVec condition = random.Value(1 + random.Next(4));
                LogicVec t = random.Value(random.Width());
                LogicVec f = random.Value(random.Width());

                Same(LogicVec.Conditional(condition, t, f),
                    ReferenceVec.Conditional(ReferenceVec.Of(condition), ReferenceVec.Of(t), ReferenceVec.Of(f)),
                    $"{condition} ? {t} : {f}");
            }
        }
    }
}
