using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Tests;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// Every operator on 4-state values gives what Icarus Verilog gives, on random operands of
    /// widths from 1 to 130.
    /// </summary>
    /// <remarks>
    /// The cases are generated, not stored: one testbench assigns each pair of operands as literals,
    /// applies the operator, and prints the result with <c>$display("%b")</c>, and each printed line is
    /// compared with <see cref="LogicVec"/>'s own answer. Each result is declared exactly as wide as
    /// the operator's own result -- the wider operand for <c>+</c>, one bit for <c>==</c> -- so the
    /// context SystemVerilog sizes the expression by is the one <see cref="LogicVec"/> works at.
    /// Sizing by a wider context is the elaborator's job, and its tests come with it.
    ///
    /// The differential tests hold the fast operators to a slow reference written from the
    /// standard; this holds both to a simulator that students' ModelSim agrees with.
    /// </remarks>
    public class OperatorOracleTests
    {
        private sealed class Case
        {
            public string Declarations;
            public string Statement;
            public string Expected;
            public string Shown;

            /// <summary>
            /// A ?: whose condition is x or z: where both choices are z, the standard gives x (IEEE 1800,
            /// table 11-20) and Icarus z. BitSorter follows the standard (Docs/oracle-notes.md), so that one
            /// difference -- an x here against a z there -- is allowed, and nothing else.
            /// </summary>
            public bool AmbiguousChoice;
        }

        private static string Literal(LogicVec v) => $"{v.Width}'b{v.Format(Radix.Binary)}";

        private static string Declare(string name, int width, bool signed) =>
            $"  logic {(signed ? "signed " : "")}[{width - 1}:0] {name};";

        private delegate LogicVec Binary(LogicVec a, LogicVec b);

        private static readonly (string Op, Binary Fast, bool OneBit)[] BinaryOps =
        {
            ("&", LogicVec.And, false), ("|", LogicVec.Or, false), ("^", LogicVec.Xor, false), ("~^", LogicVec.Xnor, false),
            ("+", LogicVec.Add, false), ("-", LogicVec.Subtract, false), ("*", LogicVec.Multiply, false),
            ("/", LogicVec.Divide, false), ("%", LogicVec.Modulo, false),
            ("<", LogicVec.LessThan, true), ("<=", LogicVec.LessOrEqual, true), (">", LogicVec.GreaterThan, true), (">=", LogicVec.GreaterOrEqual, true),
            ("==", LogicVec.Equal, true), ("!=", LogicVec.NotEqual, true), ("===", LogicVec.CaseEqual, true), ("!==", LogicVec.CaseNotEqual, true),
            ("==?", LogicVec.WildcardEqual, true), ("!=?", LogicVec.WildcardNotEqual, true),
            ("&&", LogicVec.LogicalAnd, true), ("||", LogicVec.LogicalOr, true),
        };

        private static readonly (string Op, Func<LogicVec, LogicVec> Fast, bool OneBit)[] UnaryOps =
        {
            ("~", LogicVec.Not, false), ("-", LogicVec.Negate, false), ("!", LogicVec.LogicalNot, true),
            ("&", LogicVec.ReduceAnd, true), ("|", LogicVec.ReduceOr, true), ("^", LogicVec.ReduceXor, true),
            ("~&", LogicVec.ReduceNand, true), ("~|", LogicVec.ReduceNor, true), ("~^", LogicVec.ReduceXnor, true),
        };

        private static readonly (string Op, Binary Fast)[] ShiftOps =
        {
            ("<<", LogicVec.ShiftLeft), (">>", LogicVec.ShiftRight), ("<<<", LogicVec.ArithmeticShiftLeft), (">>>", LogicVec.ArithmeticShiftRight),
        };

        private const int CasesPerOperator = 40;

        private static List<Case> Generate()
        {
            var random = new RandomValues(1012);
            var cases = new List<Case>();

            void Add(string declarations, string statement, LogicVec expected, string shown) =>
                cases.Add(new Case { Declarations = declarations, Statement = statement, Expected = expected.Format(Radix.Binary), Shown = shown });

            foreach ((string op, Binary fast, bool oneBit) in BinaryOps)
            {
                for (int k = 0; k < CasesPerOperator; k++)
                {
                    int i = cases.Count;
                    LogicVec a = random.Value(random.Width()), b = random.Value(random.Width());
                    if ((op == "/" || op == "%") && k % 3 == 0)
                        b = random.Count(Math.Min(b.Width, 8), 9).Resize(b.Width);

                    LogicVec r = fast(a, b);
                    int width = oneBit ? 1 : r.Width;
                    Add(string.Join("\n", Declare($"a{i}", a.Width, a.IsSigned), Declare($"b{i}", b.Width, b.IsSigned), Declare($"r{i}", width, false)),
                        $"a{i} = {Literal(a)}; b{i} = {Literal(b)}; r{i} = a{i} {op} b{i};", r, $"{a} {op} {b}");
                }
            }

            foreach ((string op, Func<LogicVec, LogicVec> fast, bool oneBit) in UnaryOps)
            {
                for (int k = 0; k < CasesPerOperator; k++)
                {
                    int i = cases.Count;
                    LogicVec a = random.Value(random.Width());
                    LogicVec r = fast(a);
                    Add(string.Join("\n", Declare($"a{i}", a.Width, a.IsSigned), Declare($"r{i}", oneBit ? 1 : a.Width, false)),
                        $"a{i} = {Literal(a)}; r{i} = {op}a{i};", r, $"{op}{a}");
                }
            }

            foreach ((string op, Binary fast) in ShiftOps)
            {
                for (int k = 0; k < CasesPerOperator; k++)
                {
                    int i = cases.Count;
                    LogicVec a = random.Value(random.Width());
                    LogicVec n = random.Count(1 + random.Next(9), a.Width + 2);
                    LogicVec r = fast(a, n);
                    Add(string.Join("\n", Declare($"a{i}", a.Width, a.IsSigned), Declare($"n{i}", n.Width, false), Declare($"r{i}", a.Width, false)),
                        $"a{i} = {Literal(a)}; n{i} = {Literal(n)}; r{i} = a{i} {op} n{i};", r, $"{a} {op} {n}");
                }
            }

            for (int k = 0; k < CasesPerOperator; k++)
            {
                // {a, b}, {3{a}}, c ? a : b, a[n] and a constant part-select.
                LogicVec a = random.Value(random.Width(60)), b = random.Value(random.Width(60));
                LogicVec c = random.Value(1 + random.Next(3));
                LogicVec n = random.Count(1 + random.Next(8), a.Width + 2);

                int i = cases.Count;
                Add(string.Join("\n", Declare($"a{i}", a.Width, a.IsSigned), Declare($"b{i}", b.Width, b.IsSigned), Declare($"r{i}", a.Width + b.Width, false)),
                    $"a{i} = {Literal(a)}; b{i} = {Literal(b)}; r{i} = {{a{i}, b{i}}};", LogicVec.Concat(a, b), $"{{{a}, {b}}}");

                i = cases.Count;
                Add(string.Join("\n", Declare($"a{i}", a.Width, a.IsSigned), Declare($"r{i}", 3 * a.Width, false)),
                    $"a{i} = {Literal(a)}; r{i} = {{3{{a{i}}}}};", LogicVec.Replicate(a, 3), $"{{3{{{a}}}}}");

                i = cases.Count;
                LogicVec chosen = LogicVec.Conditional(c, a, b);
                bool ambiguous = LogicVec.Truth(c) != Logic4.One && LogicVec.Truth(c) != Logic4.Zero;
                Add(string.Join("\n", Declare($"c{i}", c.Width, false), Declare($"a{i}", a.Width, a.IsSigned), Declare($"b{i}", b.Width, b.IsSigned), Declare($"r{i}", chosen.Width, false)),
                    $"c{i} = {Literal(c)}; a{i} = {Literal(a)}; b{i} = {Literal(b)}; r{i} = c{i} ? a{i} : b{i};", chosen, $"{c} ? {a} : {b}");
                cases[cases.Count - 1].AmbiguousChoice = ambiguous;

                i = cases.Count;
                Add(string.Join("\n", Declare($"a{i}", a.Width, a.IsSigned), Declare($"n{i}", n.Width, false), Declare($"r{i}", 1, false)),
                    $"a{i} = {Literal(a)}; n{i} = {Literal(n)}; r{i} = a{i}[n{i}];", a.Bit(n), $"{a}[{n}]");

                int low = random.Next(a.Width), high = low + random.Next(a.Width - low);
                i = cases.Count;
                Add(string.Join("\n", Declare($"a{i}", a.Width, a.IsSigned), Declare($"r{i}", high - low + 1, false)),
                    $"a{i} = {Literal(a)}; r{i} = a{i}[{high}:{low}];", a.Slice(low, high - low + 1), $"{a}[{high}:{low}]");
            }

            return cases;
        }

        [Test]
        public void EveryOperator_AgreesWithIcarus()
        {
            Icarus icarus = Icarus.Require();
            List<Case> cases = Generate();

            var source = new StringBuilder();
            source.AppendLine("// Generated by OperatorOracleTests: every operator on random 4-state operands.");
            source.AppendLine("module operators_tb;");
            foreach (Case c in cases)
                source.AppendLine(c.Declarations);
            source.AppendLine("  initial begin");
            for (int i = 0; i < cases.Count; i++)
            {
                source.AppendLine("    " + cases[i].Statement);
                source.AppendLine($"    $display(\"{i} %b\", r{i});");
            }
            source.AppendLine("  end");
            source.AppendLine("endmodule");

            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-oracle-generated", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "operators.sv");
            File.WriteAllText(file, source.ToString());

            try
            {
                using IcarusRun run = icarus.Simulate(new[] { file }, timeout: TimeSpan.FromMinutes(2));
                Assert.IsTrue(run.Succeeded, run.Explain());

                var printed = new Dictionary<int, string>();
                foreach (Match m in Regex.Matches(run.Simulate.StandardOutput, @"^(\d+) ([01xzXZ]+)\r?$", RegexOptions.Multiline))
                    printed[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value.ToLowerInvariant();

                Assert.AreEqual(cases.Count, printed.Count, "Icarus did not print a line for every case");

                string[] wrong = Enumerable.Range(0, cases.Count)
                    .Where(i => printed[i] != cases[i].Expected && !KnownDeparture(cases[i], printed[i]))
                    .Select(i => $"{cases[i].Shown}\n      BitSorter {cases[i].Expected}\n      Icarus    {printed[i]}")
                    .ToArray();

                Assert.IsEmpty(wrong, $"{wrong.Length} of {cases.Count} cases disagree with Icarus; the first:\n  " +
                                      string.Join("\n  ", wrong.Take(12)));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        /// <summary>The one place Icarus departs from the standard here: z kept where an ambiguous ?: should give x.</summary>
        private static bool KnownDeparture(Case c, string icarus)
        {
            if (!c.AmbiguousChoice || icarus.Length != c.Expected.Length)
                return false;

            for (int k = 0; k < icarus.Length; k++)
                if (icarus[k] != c.Expected[k] && !(c.Expected[k] == 'x' && icarus[k] == 'z'))
                    return false;

            return true;
        }

        [Test]
        public void AnAmbiguousChoice_OfTwoZBits_IsX_AsTheStandardHasIt()
        {
            // Table 11-20: with the condition x, only bits both choices agree on as 0 or 1 come through.
            LogicVec r = LogicVec.Conditional(LogicVec.FromBinary("x"), LogicVec.FromBinary("zz10"), LogicVec.FromBinary("zz10"));
            Assert.AreEqual("xx10", r.Format(Radix.Binary));
        }
    }
}
