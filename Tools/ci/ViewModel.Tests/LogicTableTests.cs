using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The LOGIC tab (Part 15g): the truth table found by running the engine, each output as a minimal sum of products, and
    /// its K-map. Ahmad asked for a way to know more about what is built (2026-10-28).
    /// </summary>
    public class LogicTableTests
    {
        // -----------------------------------------------------------------
        // The minimizer
        // -----------------------------------------------------------------

        private static readonly string[] AB = { "a", "b" };

        [TestCase("0110", "a'b + ab'", "(~a & b) | (a & ~b)")]
        [TestCase("0001", "ab", "a & b")]
        [TestCase("0111", "b + a", "b | a")]
        [TestCase("1000", "a'b'", "~a & ~b")]
        [TestCase("0000", "0", "1'b0")]
        [TestCase("1111", "1", "1'b1")]
        [TestCase("0x01", "b", "b")]
        public void AnOutput_IsWrittenAsAMinimalSumOfProducts_BothWays(string values, string textbook, string systemVerilog)
        {
            Minimizer.Result result = Minimizer.Minimize(2, values.ToCharArray());
            Assert.AreEqual(textbook, Minimizer.Textbook(result.Terms, AB));
            Assert.AreEqual(systemVerilog, Minimizer.SystemVerilog(result.Terms, AB));
            Assert.IsTrue(result.Proven);
        }

        /// <summary>
        /// The smallest cover there is, held against brute force: every function of three variables, and random ones of four
        /// with don't-cares -- the fewest products, then the fewest literals, each covering every 1 and no 0.
        /// </summary>
        [Test]
        public void TheCover_IsAsSmallAsBruteForceFinds()
        {
            int checkedFunctions = 0;
            for (int f = 0; f < 256; f++)
            {
                char[] values = Enumerable.Range(0, 8).Select(r => (f >> r & 1) != 0 ? '1' : '0').ToArray();
                Check(3, values);
                checkedFunctions++;
            }

            var random = new Random(15_4);
            for (int i = 0; i < 60; i++)
            {
                char[] values = Enumerable.Range(0, 16).Select(_ => "01x"[random.Next(3)]).ToArray();
                Check(4, values);
                checkedFunctions++;
            }
            Assert.AreEqual(316, checkedFunctions);
        }

        /// <summary>
        /// Names longer than a letter are spaced apart in a product, as the K-map's corner spaces them: "bcin" could be one name.
        /// The 15g-5 review found the full adder's carry written "bcin + acin + ab".
        /// </summary>
        [Test]
        public void ALongerName_IsSpacedApartInAProduct()
        {
            Minimizer.Result carry = Minimizer.Minimize(3, "00010111".ToCharArray());
            Assert.AreEqual("b cin + a cin + a b", Minimizer.Textbook(carry.Terms, new[] { "a", "b", "cin" }));
            Minimizer.Result sum = Minimizer.Minimize(2, "0110".ToCharArray());
            Assert.AreEqual("a[1]' a[0] + a[1] a[0]'", Minimizer.Textbook(sum.Terms, new[] { "a[1]", "a[0]" }));
            Assert.AreEqual("a'b + ab'", Minimizer.Textbook(sum.Terms, AB), "one letter each, side by side as the textbook writes them");
        }

        /// <summary>
        /// The search made a slice at a time ends where it does all at once, on five to eight variables with don't-cares -- the
        /// tangled ones it cuts short included -- and every cover it keeps, proven the smallest or not, covers every 1 and no 0.
        /// </summary>
        [Test]
        public void TheSearchInSlices_EndsWhereItDoesAtOnce_AndACoverCutShortStillCovers()
        {
            var random = new Random(15_5);
            int cut = 0;
            for (int i = 0; i < 24; i++)
            {
                int n = 5 + i % 4;
                char[] values = Enumerable.Range(0, 1 << n).Select(_ => "011x"[random.Next(4)]).ToArray();
                Minimizer.Result whole = Minimizer.Minimize(n, values);

                var search = new Minimizer.Search(n, values);
                int slices = 0;
                while (!search.Run(997))
                    slices++;
                CollectionAssert.AreEqual(whole.Terms, search.Result.Terms, $"{n} variables, {slices} slices");
                Assert.AreEqual(whole.Proven, search.Result.Proven);
                if (!whole.Proven)
                    cut++;

                for (int r = 0; r < values.Length; r++)
                {
                    bool covered = whole.Terms.Any(t => t.Covers(r));
                    if (values[r] == '1')
                        Assert.IsTrue(covered, $"{n} variables: row {r} is 1 and not covered");
                    if (values[r] == '0')
                        Assert.IsFalse(covered, $"{n} variables: row {r} is 0 and covered");
                }
            }
            Assert.Greater(cut, 0, "sanity: some were tangled enough to be cut short");
        }

        /// <summary>The eight-input lookup the LOGIC tab's Play Mode test draws: no pattern, so its search is cut short.</summary>
        [Test]
        public void ALookupWithNoPattern_IsCutShort_AndSaysSo()
        {
            var table = System.Numerics.BigInteger.Parse("0b26d3d1274b1832c26fbbc9781b79e128ed0c861fa30917cbd97b21055ad85d2",
                System.Globalization.NumberStyles.HexNumber);
            char[] values = Enumerable.Range(0, 256).Select(r => ((table >> r) & 1) == 1 ? '1' : '0').ToArray();
            Minimizer.Result result = Minimizer.Minimize(8, values);
            Assert.IsFalse(result.Proven);
            for (int r = 0; r < 256; r++)
                Assert.AreEqual(values[r] == '1', result.Terms.Any(t => t.Covers(r)), "row " + r);
        }

        private static void Check(int n, char[] values)
        {
            Minimizer.Result result = Minimizer.Minimize(n, values);
            for (int r = 0; r < values.Length; r++)
            {
                bool covered = result.Terms.Any(t => t.Covers(r));
                if (values[r] == '1')
                    Assert.IsTrue(covered, $"{new string(values)}: row {r} is 1 and not covered");
                if (values[r] == '0')
                    Assert.IsFalse(covered, $"{new string(values)}: row {r} is 0 and covered");
            }

            (int Terms, int Literals) best = BruteForce(n, values);
            int literals = result.Terms.Sum(t => t.Literals(n));
            Assert.AreEqual(best, (result.Terms.Count, literals), new string(values));
        }

        /// <summary>The cheapest cover by trying every set of prime implicants, the primes found by trying every cube.</summary>
        private static (int, int) BruteForce(int n, char[] values)
        {
            int rows = 1 << n;
            var cubes = new List<Implicant>();
            for (int mask = 0; mask < rows; mask++)
                for (int value = 0; value < rows; value++)
                    if ((value & mask) == 0)
                        cubes.Add(new Implicant(value, mask));

            bool Implicant(Implicant c) => Enumerable.Range(0, rows).All(r => !c.Covers(r) || values[r] != '0');
            var implicants = cubes.Where(Implicant).ToList();
            var primes = implicants.Where(c => !implicants.Any(d => !d.Equals(c) && (d.Mask & c.Mask) == c.Mask && d.Covers(c.Value))).ToList();
            primes = primes.Where(p => Enumerable.Range(0, rows).Any(r => values[r] == '1' && p.Covers(r))).ToList();
            var ones = Enumerable.Range(0, rows).Where(r => values[r] == '1').ToList();
            if (ones.Count == 0)
                return (0, 0);

            (int, int) best = (int.MaxValue, int.MaxValue);
            for (long set = 1; set < 1L << primes.Count; set++)
            {
                var chosen = Enumerable.Range(0, primes.Count).Where(i => (set >> i & 1) != 0).Select(i => primes[i]).ToList();
                if (!ones.All(r => chosen.Any(t => t.Covers(r))))
                    continue;
                (int, int) cost = (chosen.Count, chosen.Sum(t => t.Literals(n)));
                if (cost.Item1 < best.Item1 || (cost.Item1 == best.Item1 && cost.Item2 < best.Item2))
                    best = cost;
            }
            return best;
        }

        // -----------------------------------------------------------------
        // The K-map
        // -----------------------------------------------------------------

        [Test]
        public void AKMap_LaysTheTableOutInGrayOrder_TheFirstVariablesDownTheSide()
        {
            // f(a,b,c,d) = 1 only where a=1, b=0, c=1, d=1: row 1011 = 11.
            char[] values = Enumerable.Range(0, 16).Select(r => r == 11 ? '1' : r == 5 ? 'x' : '0').ToArray();
            KarnaughLayout map = KarnaughLayout.Of(new[] { "a", "b", "c", "d" }, values);

            Assert.AreEqual("ab", map.Down);
            Assert.AreEqual("cd", map.Across);
            CollectionAssert.AreEqual(new[] { "00", "01", "11", "10" }, map.RowLabels.ToArray());
            CollectionAssert.AreEqual(new[] { "00", "01", "11", "10" }, map.ColumnLabels.ToArray());
            Assert.AreEqual('1', map.Cells[3, 2], "ab = 10 is the fourth row, cd = 11 the third column");
            Assert.AreEqual('x', map.Cells[1, 1], "0101");
            Assert.AreEqual(15, Enumerable.Range(0, 16).Count(i => map.Cells[i / 4, i % 4] != '1'));

            Assert.AreEqual(1, KarnaughLayout.Of(new[] { "a" }, new[] { '0', '1' }).RowLabels.Count, "one variable: one row");
            Assert.IsNull(KarnaughLayout.Of(new[] { "a", "b", "c", "d", "e" }, new char[32]), "five variables: no map");
        }

        // -----------------------------------------------------------------
        // The truth table, run
        // -----------------------------------------------------------------

        private static LogicTable Tabled(Project project, out string why)
        {
            var session = new WorkbenchSession(project, () => 0);
            session.Finish();
            LogicTable table = LogicTable.Start(project, session.Timing, () => 0, out why);
            table?.Finish();
            return table;
        }

        private static Project Written(string source)
        {
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("wide.sv", source) });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            return import.Project;
        }

        /// <summary>
        /// Too many input bits to table says how many rows they would be, exactly. A 64-bit ALU's 131 input bits read "8 rows"
        /// in the 15h browser test: a 64-bit 1 shifted 131 places wraps round to one shifted 3.
        /// </summary>
        [Test]
        public void TooManyInputBits_SayHowManyRowsTheyAre_PastSixtyFour()
        {
            Assert.IsNull(Tabled(Written("module wide (input logic [63:0] a, input logic [63:0] b, input logic [2:0] c, output logic y);\n" +
                                         "    assign y = ^a ^ ^b ^ ^c;\n" +
                                         "endmodule\n"), out string why), "sanity: too many to table");
            StringAssert.StartsWith("131 input bits are 2^131 rows", why);

            Assert.IsNull(Tabled(Written("module forty (input logic [39:0] a, output logic y);\n" +
                                         "    assign y = ^a;\n" +
                                         "endmodule\n"), out why), "sanity");
            StringAssert.StartsWith("40 input bits are 1,099,511,627,776 rows", why, "and below 63, the number itself");
        }

        [Test]
        public void TheFullAdder_IsTabledByRunningIt()
        {
            LogicTable table = Tabled(WorkbenchSamples.FullAdder(), out string why);
            Assert.IsNotNull(table, why);
            Assert.IsNull(table.Problem);
            CollectionAssert.AreEqual(new[] { "a", "b", "cin" }, table.VariableNames.ToArray());
            Assert.AreEqual(8, table.Rows);

            string Column(string name) => new string(table.Outputs.Single(o => o.Name == name).Values.ToArray());
            Assert.AreEqual("01101001", Column("s"), "the sum: an odd number of 1s");
            Assert.AreEqual("00010111", Column("cout"), "the carry: two or more");

            Minimizer.Result carry = Minimizer.Minimize(3, table.Outputs.Single(o => o.Name == "cout").Values);
            Assert.AreEqual("b cin + a cin + a b", Minimizer.Textbook(carry.Terms, table.VariableNames));
        }

        /// <summary>
        /// Ports with ranges of their own: each bit named as it is declared, so a [0:1]'s high bit is y[0] -- the 15g-5 review found
        /// every bus named from 0, and a [0:1]'s two columns each under the other's name.
        /// </summary>
        [Test]
        public void PortsWithRangesOfTheirOwn_NameEachBitAsDeclared()
        {
            ProjectImport import = ProjectImporter.Import(new[]
            {
                new SourceFile("ranged.sv",
                    "module ranged(input logic [7:6] a, input logic b, output logic [0:1] y);\n" +
                    "    assign y = {a[7] & b, a[6] | b};\n" +
                    "endmodule\n"),
            });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            LogicTable table = Tabled(import.Project, out string why);
            Assert.IsNotNull(table, why);
            Assert.AreEqual(8, table.Rows);

            CollectionAssert.AreEqual(new[] { "a[7]", "a[6]", "b" }, table.VariableNames.ToArray(), "a [7:6]'s bits are a[7] and a[6]");
            CollectionAssert.AreEqual(new[] { "y[0]", "y[1]" }, table.Outputs.Select(o => o.Name).ToArray(), "y[0] is a [0:1]'s high bit");
            string Column(string name) => new string(table.Outputs.Single(o => o.Name == name).Values.ToArray());
            Assert.AreEqual("00000101", Column("y[0]"), "y[0] = a[7] & b, rows counting a[7] a[6] b");
            Assert.AreEqual("01110111", Column("y[1]"), "y[1] = a[6] | b");
        }

        [Test]
        public void ACircuitWithRegisters_IsNotTabled_AndSaysWhy()
        {
            Assert.IsNull(Tabled(WorkbenchSamples.Counter(), out string why));
            StringAssert.Contains("registers", why);
        }

        /// <summary>
        /// Every combinational exercise's reference solution, tabled by running it, agrees with the exercise's own test: at each
        /// check, the row its inputs then select reads what the check wants, bit for bit where the check cares.
        /// </summary>
        [Test]
        public void EveryCombinationalExercisesSolution_IsTabledAsItsTestWants()
        {
            int tabled = 0;
            foreach (Exercise exercise in ExerciseFiles.InOrder())
            {
                Project solved = ExerciseFiles.Solution(exercise);
                LogicTable table = Tabled(solved, out string why);
                if (table == null)
                {
                    StringAssert.IsMatch("registers|input bits", why, $"{exercise.Id} is not tabled: {why}");
                    continue;
                }
                Assert.IsNull(table.Problem, exercise.Id);

                Stimulus test = exercise.Test();   // the solution is the design alone; the exercise brings its test
                foreach (Check check in test.Checks)
                {
                    int row = 0;
                    for (int v = 0; v < table.Variables.Count; v++)
                    {
                        InputBit bit = table.Variables[v];
                        LogicVec? value = StimulusEdits.ValueAt(test, bit.Port, check.Time);
                        if (value.HasValue && value.Value[bit.Bit] == Logic4.One)
                            row |= 1 << (table.Variables.Count - 1 - v);
                    }
                    for (int b = 0; b < check.Expected.Width; b++)
                    {
                        Logic4 wanted = check.Expected[b];
                        if (wanted != Logic4.Zero && wanted != Logic4.One)
                            continue;
                        char got = table.Outputs.Single(o => o.Port == check.Port && o.Bit == b).Values[row];
                        Assert.AreEqual(wanted == Logic4.One ? '1' : '0', got, $"{exercise.Id}: {check.Port}[{b}] at {check.Time}, row {row}");
                    }
                }
                tabled++;
            }
            Assert.Greater(tabled, 15, "sanity: the combinational exercises were tabled");
        }
    }
}
