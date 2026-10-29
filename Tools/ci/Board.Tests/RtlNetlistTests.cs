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
        /// Runs <paramref name="design"/> through random vectors and holds every scope's netlist to the engine: every net a join
        /// drives -- a signal the scope's own logic gives a value -- reads what the engine has, after the data settles and again
        /// after the clock's edge; and every register clocked then holds what its D was worked out to be just before (Part
        /// 15h-2). A clock -- a one-bit input named as one -- moves half a vector after the data, so the two never race. Returns
        /// how many comparisons there were, so a test can say it compared something.
        /// </summary>
        internal static int HoldToTheEngine(Design design, int seed, string what)
        {
            var random = new Random(seed);
            var inputs = new List<SignalId>();
            var clocks = new List<SignalId>();
            for (int i = 0; i < design.Signals.Count; i++)
            {
                SignalInfo info = design.Signals[i];
                if (info.Scope == 0 && info.Port == PortDirection.Input)
                    (IsClock(info.Name) && info.Width == 1 ? clocks : inputs).Add(design.SignalAt(i));
            }

            var stimulus = new BitSorter.Sim.Stimulus();
            foreach (SignalId clock in clocks)
                stimulus.Set(0, clock, Logic4.Zero);
            for (int v = 0; v < Vectors; v++)
            {
                foreach (SignalId input in inputs)
                    stimulus.Set(v * Gap, input, Random(random, design[input].Width));
                foreach (SignalId clock in clocks)
                    stimulus.Set(v * Gap + Gap / 2, clock, v % 2 == 0 ? Logic4.One : Logic4.Zero);
            }

            var sim = new Simulator(design, stimulus, null);
            List<RtlNetlist> netlists = Netlists(design);
            foreach (RtlNetlist netlist in netlists)
                HoldItsShape(design, netlist, what);
            int compared = 0, clocked = 0;
            for (int v = 0; v < Vectors; v++)
            {
                // Before the clock: the logic settled on the data, and each register's D worked out.
                sim.RunTo(v * Gap + Gap / 2 - 1);
                compared += Compare(design, sim, netlists, $"{what} at vector {v}");
                LogicVec[] before = Values(design, sim);
                var expected = new List<(RtlNetlist Netlist, RtlCell Register, LogicVec D)>();
                foreach (RtlNetlist netlist in netlists)
                {
                    LogicVec[] values = RtlEvaluator.Evaluate(netlist, before);
                    foreach (RtlCell cell in netlist.Cells)
                        if (cell.Kind == RtlKind.Register)
                            expected.Add((netlist, cell, values[cell.Inputs[0]]));
                }

                // After it: the logic again, and every register its clock's edge reached holds its D.
                sim.RunTo(v * Gap + Gap - 1);
                compared += Compare(design, sim, netlists, $"{what} after the clock at vector {v}");
                LogicVec[] after = Values(design, sim);
                foreach ((RtlNetlist netlist, RtlCell register, LogicVec d) in expected)
                {
                    if (!Clocked(netlist, register, before, after))
                        continue;
                    RtlNet q = netlist.Nets[register.Output];
                    Assert.AreEqual(d.Format(Radix.Binary), after[q.Signal].Format(Radix.Binary),
                        $"{what}: {netlist.Scope}.{q.Name} after the clock at vector {v}: its D before");
                    clocked++;
                    compared++;
                }
            }

            // Every register on a clock the test moves is reached by its edge now and then.
            foreach (RtlNetlist netlist in netlists)
                foreach (RtlCell register in netlist.Cells.Where(c => c.Kind == RtlKind.Register))
                    if (register.InputNames.Skip(1).Any(IsClock))
                        Assert.Greater(clocked, 0, $"{what}: {netlist.Scope}.{register.Name} on a clock, and no register clocked");

            // A design with nothing to compare must be one with nothing combinational -- processes and instances alone -- but what
            // passes through an opaque box.
            if (compared == 0)
                foreach (RtlNetlist n in netlists)
                {
                    bool[] opaque = ThroughAnOpaqueBox(n);
                    Assert.IsTrue(n.Cells.Where(c => c.Kind == RtlKind.Join).All(c => opaque[c.Output]), $"{what}: joins, and none compared");
                }
            return compared;
        }

        /// <summary>A clock as its name says: clk, clock, or one ending or starting so (sys_clk, clk_in).</summary>
        private static bool IsClock(string name)
        {
            string last = name.Substring(name.LastIndexOf('.') + 1).ToLowerInvariant();
            return last == "clk" || last == "clock" || last.EndsWith("_clk", StringComparison.Ordinal) || last.StartsWith("clk_", StringComparison.Ordinal);
        }

        private static LogicVec[] Values(Design design, Simulator sim)
        {
            var signals = new LogicVec[design.Signals.Count];
            for (int i = 0; i < signals.Length; i++)
                signals[i] = sim.Value(design.SignalAt(i));
            return signals;
        }

        /// <summary>
        /// Whether a register's clock moved the way it waits for between <paramref name="before"/> and <paramref name="after"/>,
        /// and none of its other edges moved at all: only a clock named as one is followed, since what one register's edge gives
        /// another clocked by it is not settled before that edge.
        /// </summary>
        private static bool Clocked(RtlNetlist netlist, RtlCell register, LogicVec[] before, LogicVec[] after)
        {
            bool clocked = false;
            for (int pin = 1; pin < register.Inputs.Count; pin++)
            {
                RtlNet net = netlist.Nets[register.Inputs[pin]];
                if (net.Signal < 0)
                    return false;
                Logic4 was = before[net.Signal][0], now = after[net.Signal][0];
                if (was == now)
                    continue;
                if (!IsClock(register.InputNames[pin]))
                    return false;
                EdgeKind edge = register.Edges[pin - 1];
                bool rise = was == Logic4.Zero && now == Logic4.One, fall = was == Logic4.One && now == Logic4.Zero;
                clocked |= edge == EdgeKind.Posedge ? rise : edge == EdgeKind.Negedge ? fall : rise || fall;
            }
            return clocked;
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

        /// <summary>
        /// Every net a join drives in <paramref name="netlists"/>, worked out, against the engine now, and every instance's input
        /// pin against the instance's own port; how many there were. A net whose value passes through an opaque box is left out:
        /// the box is worked out from the signals as they are now, and the engine leaves a function's value stale when a signal it
        /// reads but is not given changes -- as Icarus does, and as no circuit computes.
        /// </summary>
        private static int Compare(Design design, Simulator sim, List<RtlNetlist> netlists, string when)
        {
            var signals = new LogicVec[design.Signals.Count];
            for (int i = 0; i < signals.Length; i++)
                signals[i] = sim.Value(design.SignalAt(i));

            int compared = 0;
            foreach (RtlNetlist netlist in netlists)
            {
                LogicVec[] values = RtlEvaluator.Evaluate(netlist, signals);
                bool[] opaque = ThroughAnOpaqueBox(netlist);
                foreach (RtlNet net in netlist.Nets)
                {
                    if (net.Signal < 0 || net.Driver < 0 || netlist.Cells[net.Driver].Kind != RtlKind.Join || opaque[net.Id])
                        continue;
                    Assert.AreEqual(signals[net.Signal].Format(Radix.Binary), values[net.Id].Format(Radix.Binary),
                        $"{when}: {netlist.Scope}.{net.Name}");
                    compared++;
                }

                foreach (RtlCell cell in netlist.Cells)
                {
                    if (cell.Kind != RtlKind.Instance)
                        continue;
                    for (int pin = 0; pin < cell.Inputs.Count; pin++)
                    {
                        int net = cell.Inputs[pin];
                        if (net < 0 || opaque[net] || !design.TryFind(netlist.Scope + "." + cell.Name + "." + cell.InputNames[pin], out SignalId port))
                            continue;
                        Assert.AreEqual(signals[port.Index].Format(Radix.Binary), values[net].Format(Radix.Binary),
                            $"{when}: {netlist.Scope}.{cell.Name}'s pin {cell.InputNames[pin]}");
                        compared++;
                    }
                }
            }
            return compared;
        }

        /// <summary>Each net whose value passes through an opaque box, working back through the logic to the box.</summary>
        private static bool[] ThroughAnOpaqueBox(RtlNetlist netlist)
        {
            var through = new bool[netlist.Nets.Count];
            var seen = new bool[netlist.Nets.Count];
            bool Through(int net)
            {
                if (net < 0 || seen[net])
                    return net >= 0 && through[net];
                seen[net] = true;
                int driver = netlist.Nets[net].Driver;
                if (driver < 0)
                    return false;
                RtlCell cell = netlist.Cells[driver];
                if (cell.Kind == RtlKind.Instance || cell.Kind == RtlKind.Process)
                    return false;
                bool any = cell.Kind == RtlKind.Opaque;
                foreach (int input in cell.Inputs)
                    any |= Through(input);
                return through[net] = any;
            }
            for (int n = 0; n < through.Length; n++)
                Through(n);
            return through;
        }

        /// <summary>
        /// What every netlist is: its undriven nets -- its inputs -- signals of its own scopes or of what is inside them, an
        /// inout's net by an alias, never a signal of its parent's or another's. A child's netlist once read its parent's logic
        /// for its inputs (the 15h-1 review).
        /// </summary>
        private static void HoldItsShape(Design design, RtlNetlist netlist, string what)
        {
            int scope = design.Scopes.ToList().FindIndex(s => s.Path == netlist.Scope);
            var within = new HashSet<int> { scope };
            for (int i = scope + 1; i < design.Scopes.Count; i++)
                if (within.Contains(design.Scopes[i].Parent))
                    within.Add(i);
            var aliased = new HashSet<int>(design.Aliases.Where(a => a.Name.StartsWith(netlist.Scope + ".", StringComparison.Ordinal)).Select(a => a.Signal.Index));
            foreach (RtlNet net in netlist.Nets)
                if (net.Driver < 0 && net.Signal >= 0)
                    Assert.IsTrue(within.Contains(design.Signals[net.Signal].Scope) || aliased.Contains(net.Signal),
                        $"{what}: {netlist.Scope} reads {design.Signals[net.Signal].Name}, of another scope, as an input");
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

            // Every testbench as written, compared where it ends, everything having settled (the 15h-1 review: only a file with
            // no module with ports was run this way, and eight testbenches never were).
            foreach (ModuleDeclaration module in tree.Modules)
            {
                if (module.Ports.Count > 0)
                    continue;
                Elaboration e = Elaborator.Elaborate(tree, module.Name);
                if (e.HasErrors)
                    continue;
                var sim = new Simulator(e.Design, null, null);
                sim.RunTo(10_000_000);
                List<RtlNetlist> netlists = Netlists(e.Design);
                foreach (RtlNetlist netlist in netlists)
                    HoldItsShape(e.Design, netlist, file + " " + module.Name);
                Compare(e.Design, sim, netlists, file + " " + module.Name + " at its end");
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

        /// <summary>
        /// A value used in two places is one cell: <c>{cout, sum} = a + b + cin</c> is sliced once for each, and is two adders,
        /// not four (the 15h-1 review).
        /// </summary>
        [Test]
        public void AValueUsedTwice_IsOneCell()
        {
            SyntaxTree tree = SyntaxReader.ReadText(
                "module add (input logic [3:0] a, b, input logic cin, output logic [3:0] sum, output logic cout);\n" +
                "    assign {cout, sum} = a + b + cin;\n" +
                "endmodule\n", "add.sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            RtlNetlist netlist = RtlExtractor.Of(Elaborate(tree, "add"), "add");
            Assert.AreEqual(2, netlist.Count(RtlKind.Binary), "a + b, and + cin");
        }

        /// <summary>
        /// A child's netlist has its own input ports for inputs, never its parent's logic: the connection is the parent's, as a
        /// pin of the instance (the 15h-1 review found the child's a and b driven by its parent's p and ~q).
        /// </summary>
        [Test]
        public void AChildsInputPorts_AreItsInputs_AndTheConnectionIsTheParents()
        {
            SyntaxTree tree = SyntaxReader.ReadText(
                "module inner (input logic [3:0] a, b, output logic [3:0] y);\n" +
                "    assign y = a + b;\n" +
                "endmodule\n" +
                "module outer (input logic [3:0] p, q, output logic [3:0] r);\n" +
                "    inner u (.a(p), .b(~q), .y(r));\n" +
                "endmodule\n", "nest.sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Design design = Elaborate(tree, "outer");

            RtlNetlist inner = RtlExtractor.Of(design, "outer.u");
            Assert.AreEqual(0, inner.Count(RtlKind.Unary), "no ~q of its parent's");
            Assert.AreEqual(1, inner.Count(RtlKind.Join), "y alone is driven in it: not a, not b");
            foreach (int input in inner.Inputs)
                Assert.AreEqual(-1, inner.Nets[input].Driver, inner.Nets[input].Name + " is an input");

            RtlNetlist outer = RtlExtractor.Of(design, "outer");
            RtlCell u = outer.Cells.Single(c => c.Kind == RtlKind.Instance);
            Assert.AreEqual(RtlKind.Unary, outer.Cells[outer.Nets[u.Inputs[1]].Driver].Kind, "the ~q on u's pin b is the parent's");
        }

        /// <summary>
        /// An inout bus driven from several scopes: each device's netlist has its own driver of the bus, on a net of its own, and
        /// the testbench's has its own and the devices as pins on the bus -- never a device's logic (the 15h-1 review found the
        /// testbench's join reading a device's en and value, and the devices with no driver at all).
        /// </summary>
        [Test]
        public void AnInoutBus_IsDrivenInEachScopeByWhatThatScopeGivesIt()
        {
            string folder = RepoRoot.Combine("Tools/ci/Oracle/corpus/behavioural");
            SyntaxTree tree = SyntaxReader.ReadText(File.ReadAllText(Path.Combine(folder, "shared_bus.sv")), "shared_bus.sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Design design = Elaborate(tree, "shared_bus_tb");

            RtlNetlist device = RtlExtractor.Of(design, "shared_bus_tb.a");
            RtlCell drives = device.Cells.Single(c => c.Kind == RtlKind.Join && device.Nets[c.Output].Name == "bus (driven here)");
            Assert.AreEqual(RtlKind.Mux, device.Cells[device.Nets[drives.Inputs.Single()].Driver].Kind, "en ? value : z, in the device");

            RtlNetlist bench = RtlExtractor.Of(design, "shared_bus_tb");
            RtlCell a = bench.Cells.Single(c => c.Kind == RtlKind.Instance && c.Name == "a");
            int pin = a.InputNames.ToList().IndexOf("bus");
            Assert.GreaterOrEqual(pin, 0, "the device's inout is a pin of it");
            Assert.AreEqual(bench.NetNamed("bus"), a.Inputs[pin], "on the bus");
            Assert.IsFalse(bench.Cells.Any(c => c.Kind == RtlKind.Mux && c.Inputs.Any(i => bench.Nets[i].Name.StartsWith("a.", StringComparison.Ordinal))),
                "and nothing of the device's own logic in the testbench");
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
