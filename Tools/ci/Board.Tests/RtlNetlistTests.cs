using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BitSorter.Ci;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The netlist of a module's code (Part 15h-1): every scope of a design extracted as cells and nets, and every net that is a
    /// signal worked out from what feeds it holding what the engine has there. Each design is its own top, driven by random
    /// vectors far enough apart for everything to settle, and compared just before the next.
    /// </summary>
    public class RtlNetlistTests
    {
        private const long Gap = 10_000;
        private const int Vectors = 12;

        /// <summary>
        /// Runs <paramref name="design"/> through random vectors and holds every scope's netlist to the engine before each next
        /// one: every net a join drives -- a signal the scope's own logic gives a value -- reads what the engine has. Returns how
        /// many such comparisons there were, so a test can say it compared something.
        /// </summary>
        internal static int HoldToTheEngine(Design design, int seed, string what)
        {
            var random = new Random(seed);
            string top = design.Scopes[0].Path;
            var inputs = new List<SignalId>();
            for (int i = 0; i < design.Signals.Count; i++)
            {
                SignalInfo info = design.Signals[i];
                if (info.Scope == 0 && info.Port == PortDirection.Input)
                    inputs.Add(design.SignalAt(i));
            }

            var stimulus = new BitSorter.Sim.Stimulus();
            for (int v = 0; v < Vectors; v++)
                foreach (SignalId input in inputs)
                    stimulus.Set(v * Gap, input, Random(random, design[input].Width));

            var sim = new Simulator(design, stimulus, null);
            List<RtlNetlist> netlists = Netlists(design);
            int compared = 0;
            for (int v = 0; v < Vectors; v++)
            {
                sim.RunTo(v * Gap + Gap - 1);
                compared += Compare(design, sim, netlists, $"{what} at vector {v}");
            }

            // A design with nothing to compare must be one with nothing combinational: processes and instances alone.
            if (compared == 0)
                Assert.IsTrue(netlists.All(n => n.Count(RtlKind.Join) == 0), $"{what}: joins, and none compared");
            return compared;
        }

        /// <summary>Every scope's netlist that is a module's own: the top's and each instance's.</summary>
        private static List<RtlNetlist> Netlists(Design design)
        {
            var netlists = new List<RtlNetlist>();
            foreach (ScopeInfo scope in design.Scopes)
                if (scope.Kind == ScopeKind.Top || scope.Kind == ScopeKind.Instance)
                    netlists.Add(RtlExtractor.Of(design, scope.Path));
            return netlists;
        }

        /// <summary>Every net a join drives in <paramref name="netlists"/>, worked out, against the engine now; how many there were.</summary>
        private static int Compare(Design design, Simulator sim, List<RtlNetlist> netlists, string when)
        {
            var signals = new LogicVec[design.Signals.Count];
            for (int i = 0; i < signals.Length; i++)
                signals[i] = sim.Value(design.SignalAt(i));

            int compared = 0;
            foreach (RtlNetlist netlist in netlists)
            {
                LogicVec[] values = RtlEvaluator.Evaluate(netlist, signals);
                foreach (RtlNet net in netlist.Nets)
                {
                    if (net.Signal < 0 || net.Driver < 0 || netlist.Cells[net.Driver].Kind != RtlKind.Join)
                        continue;
                    Assert.AreEqual(signals[net.Signal].Format(Radix.Binary), values[net.Id].Format(Radix.Binary),
                        $"{when}: {netlist.Scope}.{net.Name}");
                    compared++;
                }
            }
            return compared;
        }

        /// <summary>A random value of <paramref name="width"/> bits, now and then with an x or a z in it.</summary>
        private static LogicVec Random(Random random, int width)
        {
            var bits = new char[width];
            for (int i = 0; i < width; i++)
            {
                int roll = random.Next(40);
                bits[i] = roll == 0 ? 'x' : roll == 1 ? 'z' : roll < 21 ? '0' : '1';
            }
            return Literal.Parse(width + "'b" + new string(bits)).Value;
        }

        /// <summary>A seed from a name that is the same in every run: a string's own hash is not, across .NET processes.</summary>
        private static int Seed(string name)
        {
            int seed = 17;
            foreach (char c in name)
                seed = unchecked(seed * 31 + c);
            return seed & 0x7fffffff;
        }

        private static Design Elaborate(SyntaxTree tree, string top)
        {
            Elaboration e = Elaborator.Elaborate(tree, top);
            Assert.IsFalse(e.HasErrors, $"{top}: " + string.Join("\n", e.Diagnostics));
            return e.Design;
        }

        private static IEnumerable<string> Ids() => ExerciseFiles.Ids();

        /// <summary>Every exercise's reference solution, its own module the top, its cells' insides with it.</summary>
        [TestCaseSource(nameof(Ids))]
        public void AnExerciseSolutionsNetlist_ComputesWhatTheEngineDoes(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            Project solution = ExerciseFiles.Solution(exercise);
            CompiledProject compiled = BoardCompiler.Compile(solution);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));

            Design design = Elaborate(compiled.Tree, solution.TopModule.Name);
            HoldToTheEngine(design, Seed(id), id);
        }

        private static IEnumerable<string> CorpusFiles()
        {
            foreach (string folder in new[] { "structural", "behavioural", "smoke" })
                foreach (string path in Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder), "*.sv").OrderBy(p => p, StringComparer.Ordinal))
                    yield return folder + "/" + Path.GetFileName(path);
        }

        /// <summary>
        /// Every module with ports in the oracle corpus's structural, behavioural and smoke designs, as its own top -- and a file
        /// that is a testbench alone run as written, to its end: the designs the engine is held to Icarus with.
        /// </summary>
        [TestCaseSource(nameof(CorpusFiles))]
        public void ACorpusDesignsNetlist_ComputesWhatTheEngineDoes(string file)
        {
            // Read with every file of its folder at hand, for its includes: full_adder.sv includes cells.sv.
            string path = RepoRoot.Combine("Tools/ci/Oracle/corpus/" + file);
            var files = new MemorySourceFiles();
            foreach (string other in Directory.GetFiles(Path.GetDirectoryName(path)))
                files.Add(Path.GetFileName(other), File.ReadAllText(other));
            SyntaxTree tree = SyntaxReader.ReadText(File.ReadAllText(path), Path.GetFileName(path), files);
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));

            // Every module with ports a top of its own; one that cannot be -- given an interface, say -- says why.
            int tops = 0;
            var refused = new List<string>();
            foreach (ModuleDeclaration module in tree.Modules)
            {
                if (module.Ports.Count == 0)
                    continue;
                Elaboration e = Elaborator.Elaborate(tree, module.Name);
                if (e.HasErrors)
                {
                    refused.Add(module.Name + ": " + string.Join("; ", e.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                    continue;
                }
                HoldToTheEngine(e.Design, Seed(module.Name), file + " " + module.Name);
                tops++;
            }
            foreach (string why in refused)
                TestContext.WriteLine("not a top alone: " + why);
            Assert.That(refused.All(r => r.Contains("interface")), "a module that does not elaborate alone:\n" + string.Join("\n", refused));

            // A testbench alone: run as written, and compared where it ends, everything having settled.
            if (tops == 0)
                foreach (ModuleDeclaration module in tree.Modules)
                {
                    Elaboration e = Elaborator.Elaborate(tree, module.Name);
                    if (e.HasErrors)
                        continue;
                    var sim = new Simulator(e.Design, null, null);
                    sim.RunTo(10_000_000);
                    Compare(e.Design, sim, Netlists(e.Design), file + " " + module.Name + " at its end");
                    tops++;
                }
            Assert.Greater(tops, 0, "sanity: a module of the file was a top");
        }

        /// <summary>The corpus's structural designs, read with their folder's files at hand for their includes.</summary>
        private static Design Structural(string file, string top)
        {
            string folder = RepoRoot.Combine("Tools/ci/Oracle/corpus/structural");
            var files = new MemorySourceFiles();
            foreach (string other in Directory.GetFiles(folder))
                files.Add(Path.GetFileName(other), File.ReadAllText(other));
            SyntaxTree tree = SyntaxReader.ReadText(File.ReadAllText(Path.Combine(folder, file)), file, files);
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            return Elaborate(tree, top);
        }

        /// <summary>
        /// A structural design is its instances, each a cell with its ports for pins, joined by the nets the code names: the
        /// full adder's five cells, g1's output reaching g2 through half.
        /// </summary>
        [Test]
        public void AStructuralDesign_IsItsInstances_JoinedByTheNetsTheCodeNames()
        {
            RtlNetlist netlist = RtlExtractor.Of(Structural("full_adder.sv", "full_adder"), "full_adder");

            RtlCell[] instances = netlist.Cells.Where(c => c.Kind == RtlKind.Instance).ToArray();
            CollectionAssert.AreEqual(new[] { "g1", "g2", "g3", "g4", "g5" }, instances.Select(c => c.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "XOR2", "XOR2", "AND2", "AND2", "OR2" }, instances.Select(c => c.Module).ToArray());

            RtlCell g1 = instances[0], g2 = instances[1];
            CollectionAssert.AreEqual(new[] { "A", "B" }, g1.InputNames);
            CollectionAssert.AreEqual(new[] { "Z" }, g1.OutputNames);
            Assert.AreEqual(netlist.NetNamed("a"), g1.Inputs[0], "g1's A is the input a");
            Assert.AreEqual(netlist.NetNamed("half"), g2.Inputs[0], "g2's A is half");

            RtlCell half = netlist.Cells[netlist.Nets[netlist.NetNamed("half")].Driver];
            Assert.AreEqual(RtlKind.Join, half.Kind, "half is given its value by a join: one driver, a wire");
            Assert.AreEqual(g1.Output, half.Inputs.Single(), "and that driver is g1's output");
            CollectionAssert.AreEqual(new[] { "a", "b", "cin" }, netlist.Inputs.Select(n => netlist.Nets[n].Name).ToArray());
            CollectionAssert.AreEqual(new[] { "sum", "cout" }, netlist.Outputs.Select(n => netlist.Nets[n].Name).ToArray());
        }

        /// <summary>A generate loop's instances are the scope's own cells, each named for its pass: alu64's 64 slices.</summary>
        [Test]
        public void AGenerateLoopsInstances_AreTheScopesOwn_NamedForTheirPass()
        {
            RtlNetlist netlist = RtlExtractor.Of(Structural("alu64.sv", "alu64"), "alu64");
            string[] slices = netlist.Cells.Where(c => c.Kind == RtlKind.Instance).Select(c => c.Name).ToArray();
            Assert.AreEqual(64, slices.Length);
            Assert.AreEqual("slice[0].s", slices[0]);
            CollectionAssert.Contains(slices, "slice[63].s");
        }

        private const string EveryOperator = @"
module ops (
    input  logic [7:0] a, b,
    input  logic signed [7:0] sa, sb,
    input  logic [2:0] s,
    input  logic c,
    output logic [7:0] y_and, y_or, y_xor, y_xnor, y_not, y_neg,
    output logic [7:0] y_add, y_sub, y_mul, y_div, y_mod,
    output logic       y_lt, y_le, y_gt, y_ge, y_eq, y_ne, y_ceq, y_cne, y_slt, y_sge,
    output logic       y_land, y_lor, y_lnot,
    output logic       y_rand, y_ror, y_rxor, y_rnand, y_rnor, y_rxnor,
    output logic [7:0] y_shl, y_shr, y_ashr, y_ashl,
    output logic [7:0] y_mux, y_cat_lo, y_rep,
    output logic [3:0] y_slice, y_part,
    output logic       y_bit,
    output logic [15:0] y_wide, y_sext,
    output logic [3:0] y_split,
    output int         y_int
);
    assign y_and = a & b;
    assign y_or = a | b;
    assign y_xor = a ^ b;
    assign y_xnor = a ~^ b;
    assign y_not = ~a;
    assign y_neg = -a;
    assign y_add = a + b + {7'b0, c};
    assign y_sub = a - b;
    assign y_mul = a * b;
    assign y_div = a / b;
    assign y_mod = a % b;
    assign y_lt = a < b;
    assign y_le = a <= b;
    assign y_gt = a > b;
    assign y_ge = a >= b;
    assign y_eq = a == b;
    assign y_ne = a != b;
    assign y_ceq = a === b;
    assign y_cne = a !== b;
    assign y_slt = sa < sb;
    assign y_sge = sa >= sb;
    assign y_land = a && b;
    assign y_lor = a || c;
    assign y_lnot = !a;
    assign y_rand = &a;
    assign y_ror = |a;
    assign y_rxor = ^a;
    assign y_rnand = ~&a;
    assign y_rnor = ~|a;
    assign y_rxnor = ~^a;
    assign y_shl = a << s;
    assign y_shr = a >> s;
    assign y_ashr = sa >>> s;
    assign y_ashl = sa <<< 2;
    assign y_mux = c ? a : b;
    assign y_cat_lo = {a[3:0], b[7:4]};
    assign y_rep = {4{a[1:0]}};
    assign y_slice = a[6:3];
    assign y_part = b[s +: 4];
    assign y_bit = a[s];
    assign y_wide = a * b;
    assign y_sext = sa;
    assign y_split[0] = a[0] & b[0];
    assign y_split[3:1] = a[3:1] | b[3:1];
    assign y_int = $signed(a) + 1;
endmodule";

        /// <summary>Every operator a continuous assignment can apply, each its own cell, and a signal driven in slices joined as the engine joins it.</summary>
        [Test]
        public void EveryOperator_IsACell_ThatComputesWhatTheEngineDoes()
        {
            SyntaxTree tree = SyntaxReader.ReadText(EveryOperator, "ops.sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Design design = Elaborate(tree, "ops");

            RtlNetlist netlist = RtlExtractor.Of(design, "ops");
            Assert.AreEqual(6, netlist.Inputs.Count, "a, b, sa, sb, s and c");
            Assert.AreEqual(1, netlist.Count(RtlKind.Mux), "c ? a : b");
            Assert.AreEqual(1, netlist.Count(RtlKind.Part), "b[s +: 4]");
            Assert.AreEqual(1, netlist.Count(RtlKind.Bit), "a[s]");
            Assert.AreEqual(1, netlist.Count(RtlKind.Replicate), "{4{a[1:0]}}");
            RtlCell split = netlist.Cells.Single(c => c.Kind == RtlKind.Join && c.Name == "y_split");
            Assert.AreEqual(2, split.Inputs.Count, "y_split driven in two slices, joined into one");
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, split.Lowests);
            Assert.Greater(HoldToTheEngine(design, 15, "ops"), 0);
        }
    }
}
