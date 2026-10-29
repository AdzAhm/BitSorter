using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A module's code drawn (Part 15h-4): every exercise solution's and corpus design's netlist, each scope, made a drawn
    /// module -- every cell a part, every net wired from where it comes from to everywhere it goes, every signal's wires named
    /// for it, no two parts on one another, every wire routed, and the same drawing every time.
    /// </summary>
    public class RtlDrawingTests
    {
        private static IEnumerable<TestCaseData> Designs()
        {
            foreach (string id in ExerciseFiles.Ids())
                yield return new TestCaseData("exercise", id).SetName("exercise " + id);
            foreach (string folder in new[] { "structural", "behavioural" })
                foreach (string path in Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder), "*.sv").OrderBy(p => p, StringComparer.Ordinal))
                    yield return new TestCaseData(folder, Path.GetFileName(path)).SetName(folder + " " + Path.GetFileName(path));
        }

        [TestCaseSource(nameof(Designs))]
        public void ADesignsNetlist_IsDrawn_EveryNetWired_NothingOnAnything(string kind, string name)
        {
            int drawn = 0;
            foreach ((Project project, Design design) in Load(kind, name))
                foreach (ScopeInfo scope in design.Scopes)
                {
                    if (scope.Kind != ScopeKind.Top && scope.Kind != ScopeKind.Instance)
                        continue;
                    RtlNetlist netlist = RtlExtractor.Of(design, scope.Path);
                    RtlDrawing drawing = RtlDrawing.Of(project, netlist, "drawing_of_" + scope.Module);
                    Hold(drawing, netlist, name + " " + scope.Path);
                    drawn++;

                    RtlDrawing again = RtlDrawing.Of(project, netlist, "drawing_of_" + scope.Module);
                    CollectionAssert.AreEqual(drawing.Module.Parts.Select(p => drawing.Module.Layout.PlacementOf(p.Id).Position.ToString()).ToArray(),
                        again.Module.Parts.Select(p => again.Module.Layout.PlacementOf(p.Id).Position.ToString()).ToArray(),
                        $"{name} {scope.Path}: drawn the same again");
                }
            Assert.Greater(drawn, 0);
        }

        /// <summary>
        /// With GATES (Part 15h-4) every operator and mux that opens into gates is drawn as them, the drawing held as any is;
        /// and on random values every part of an opening agrees with its wires -- the last of each, out of which the code's
        /// own value comes, included, so every opening computes what its box did.
        /// </summary>
        [TestCaseSource(nameof(Designs))]
        public void ADesignsNetlist_IsDrawnWithItsGates_EachOpeningComputingWhatItsBoxDid(string kind, string name)
        {
            int drawn = 0;
            foreach ((Project project, Design design) in Load(kind, name))
                foreach (ScopeInfo scope in design.Scopes)
                {
                    if (scope.Kind != ScopeKind.Top && scope.Kind != ScopeKind.Instance)
                        continue;
                    RtlNetlist netlist = RtlExtractor.Of(design, scope.Path);
                    RtlDrawing drawing = RtlDrawing.Of(project, netlist, "drawing_of_" + scope.Module, gates: true);
                    string what = name + " " + scope.Path;
                    Hold(drawing, netlist, what, gates: true);
                    var random = new Random(what.Length * 7919 + what[what.Length - 1]);
                    for (int vector = 0; vector < 6; vector++)
                        HoldOpenings(drawing, netlist, design.Signals.Select(s => Known(random, s.Width)).ToArray(), what);
                    drawn++;

                    RtlDrawing again = RtlDrawing.Of(project, netlist, "drawing_of_" + scope.Module, gates: true);
                    CollectionAssert.AreEqual(drawing.Module.Parts.Select(p => drawing.Module.Layout.PlacementOf(p.Id).Position.ToString()).ToArray(),
                        again.Module.Parts.Select(p => again.Module.Layout.PlacementOf(p.Id).Position.ToString()).ToArray(),
                        $"{what}: drawn the same again");
                }
            Assert.Greater(drawn, 0);
        }

        /// <summary>
        /// A small ALU with GATES: its four 4-bit operators and its 4-input mux each drawn as their gates, nothing left a box,
        /// and on every one of the 1,024 values of op, a and b every part of every opening agreeing with its wires.
        /// </summary>
        [Test]
        public void ASmallAlu_WithGates_IsItsGates_OnEveryValue()
        {
            RtlNetlist netlist = Extract(SmallAlu, "alu", out Design design);
            RtlDrawing drawing = RtlDrawing.Of(Project.New("p"), netlist, "alu", gates: true);
            Hold(drawing, netlist, "alu", gates: true);
            Assert.AreEqual(5, drawing.Opened, "+, -, &, | and the mux");
            Assert.IsEmpty(drawing.Module.Parts.OfType<OperatorPart>(), "no operator left a box");
            Assert.IsEmpty(drawing.Module.Parts.OfType<MuxPart>().Where(m => m.Inputs == 4), "no 4-input mux left a box");
            Assert.Greater(drawing.InnerCount, 0);

            int op = Signal(design, "alu.op"), a = Signal(design, "alu.a"), b = Signal(design, "alu.b");
            for (int value = 0; value < 1024; value++)
            {
                var signals = design.Signals.Select(s => LogicVec.AllX(s.Width)).ToArray();
                signals[a] = LogicVec.FromUInt64((ulong)(value & 15), 4);
                signals[b] = LogicVec.FromUInt64((ulong)((value >> 4) & 15), 4);
                signals[op] = LogicVec.FromUInt64((ulong)(value >> 8), 2);
                HoldOpenings(drawing, netlist, signals, "alu " + value);
            }
        }

        /// <summary>A sum with its carry, <c>{cout, s} = a + b</c> of 4-bit a and b, is a 5-bit sum: past four bits, a box.</summary>
        [Test]
        public void ASumWithItsCarry_IsFiveBitsWide_AndStaysABox()
        {
            RtlNetlist netlist = Extract("module c (input logic [3:0] a, b, output logic [3:0] s, output logic cout);\n    assign {cout, s} = a + b;\nendmodule\n", "c", out _);
            RtlDrawing drawing = RtlDrawing.Of(Project.New("p"), netlist, "c", gates: true);
            Assert.AreEqual(0, drawing.Opened);
        }

        /// <summary>Past four bits an operator stays the box it is with GATES, and nothing is opened.</summary>
        [Test]
        public void AWideOperator_StaysABox_WithGates()
        {
            RtlNetlist netlist = Extract("module w (input logic [7:0] a, b, output logic [7:0] y);\n    assign y = a + b;\nendmodule\n", "w", out _);
            RtlDrawing drawing = RtlDrawing.Of(Project.New("p"), netlist, "w", gates: true);
            Assert.AreEqual(0, drawing.Opened);
            Assert.AreEqual(1, drawing.Module.Parts.OfType<OperatorPart>().Count());
            Assert.AreEqual(0, drawing.InnerCount);
        }

        private const string SmallAlu =
            "module alu (input logic [1:0] op, input logic [3:0] a, b, output logic [3:0] y);\n" +
            "    always_comb\n" +
            "        case (op)\n" +
            "            2'b00:   y = a + b;\n" +
            "            2'b01:   y = a - b;\n" +
            "            2'b10:   y = a & b;\n" +
            "            default: y = a | b;\n" +
            "        endcase\n" +
            "endmodule\n";

        private static RtlNetlist Extract(string source, string top, out Design design)
        {
            SyntaxTree tree = SyntaxReader.ReadText(source, top + ".sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, top);
            Assert.IsFalse(e.HasErrors, string.Join("; ", e.Diagnostics));
            design = e.Design;
            return RtlExtractor.Of(design, top);
        }

        private static int Signal(Design design, string name)
        {
            for (int i = 0; i < design.Signals.Count; i++)
                if (design.Signals[i].Name == name)
                    return i;
            Assert.Fail("no signal " + name);
            return -1;
        }

        private static LogicVec Widened(LogicVec value, int width) =>
            value.Width >= width ? value : LogicVec.Concat(LogicVec.FromUInt64(0, width - value.Width), value);

        /// <summary>A random value of <paramref name="width"/> bits, every bit a 0 or a 1.</summary>
        private static LogicVec Known(Random random, int width)
        {
            var bits = new char[width];
            for (int i = 0; i < width; i++)
                bits[i] = random.Next(2) == 0 ? '0' : '1';
            return Literal.Parse(width + "'b" + new string(bits)).Value;
        }

        /// <summary>
        /// On <paramref name="signals"/>, every part with a wire inside an opening into gates gives what its wires show: a gate
        /// its gate of its inputs, a splitter each bit, a joiner the bits joined, a mux the input its select picks. Where an
        /// input is not known it is not asked, and where the code's own value is not known while the gates' is -- a gate knows
        /// more than the language's arithmetic -- it is not asked either.
        /// </summary>
        private static void HoldOpenings(RtlDrawing drawing, RtlNetlist netlist, LogicVec[] signals, string what)
        {
            Module module = drawing.Module;
            LogicVec[] nets = RtlEvaluator.Evaluate(netlist, signals);
            LogicVec[] inner = drawing.Inner(nets);
            LogicVec Of(Wire wire) => drawing.InnerOfWire(wire.Id) >= 0 ? inner[drawing.InnerOfWire(wire.Id)] : nets[drawing.NetOfWire(wire.Id)];
            bool Inside(Wire wire) => drawing.InnerOfWire(wire.Id) >= 0;

            var into = new Dictionary<PinRef, Wire>();
            var outOf = new Dictionary<PinRef, Wire>();
            foreach (Wire wire in module.Wires)
            {
                into[wire.To.Pin] = wire;
                outOf[wire.From.Pin] = wire;
            }

            foreach (Part part in module.Parts)
            {
                if (!(part is GatePart || part is SplitterPart || part is JoinerPart || part is MuxPart))
                    continue;
                Pin[] pins = part.Pins(null).ToArray();
                Wire Into(Pin pin) => into[new PinRef(part.Id, pin.Name)];
                Wire OutOf(Pin pin) => outOf.TryGetValue(new PinRef(part.Id, pin.Name), out Wire wire) ? wire : null;
                Pin[] ins = pins.Where(p => p.Direction == PinDirection.In).ToArray();
                Pin[] outs = pins.Where(p => p.Direction == PinDirection.Out && OutOf(p) != null).ToArray();
                if (!ins.Any(p => Inside(Into(p))) && !outs.Any(p => Inside(OutOf(p))))
                    continue;   // no part of an opening

                // A wire a widening with 0s was folded into shows the narrower value its reader sees widened.
                LogicVec[] given = ins.Select(p => Widened(Of(Into(p)), p.Width)).ToArray();
                if (given.Any(v => !v.IsKnown))
                    continue;
                foreach (Pin pin in outs)
                {
                    LogicVec expected;
                    switch (part)
                    {
                        case GatePart gate:
                            expected = Gates.Evaluate(gate.Kind, given);
                            break;
                        case SplitterPart _:
                            expected = given[0].Slice(int.Parse(pin.Name.Substring(1)), 1);
                            break;
                        case JoinerPart _:
                            expected = given[0];
                            for (int i = 1; i < given.Length; i++)
                                expected = LogicVec.Concat(given[i], expected);
                            break;
                        default:
                            var mux = (MuxPart)part;
                            Assert.AreEqual(2, mux.Inputs, $"{what}: an opening's mux has two inputs");
                            expected = LogicVec.Conditional(given[2], given[1], given[0]);
                            break;
                    }
                    LogicVec actual = Of(OutOf(pin));
                    if (!actual.IsKnown && !Inside(OutOf(pin)))
                        continue;   // the gates know more than the code
                    Assert.AreEqual(expected.ToString(), actual.ToString(), $"{what}: {part.Name}.{pin.Name} of {string.Join(", ", given.Select(g => g.ToString()))}");
                }
            }
        }

        /// <summary>
        /// A case on a subject of a few bits is drawn as the mux it is, each value's arm on its input; and what only widens a
        /// value is the wire it stands on, so a small ALU is its four operators and its two muxes.
        /// </summary>
        [Test]
        public void ACase_IsDrawnAsTheMuxItIs()
        {
            SyntaxTree tree = SyntaxReader.ReadText(
                "module alu (input logic [3:0] a, b, output logic [3:0] s, output logic cout);\n" +
                "    always_comb\n" +
                "        case (a[3:2])\n" +
                "            2'b00:   {cout, s} = a + b;\n" +
                "            2'b01:   {cout, s} = a - b;\n" +
                "            2'b10:   {cout, s} = {1'b0, a & b};\n" +
                "            default: {cout, s} = {1'b0, a | b};\n" +
                "        endcase\n" +
                "endmodule\n", "alu.sv");
            Design design = Elaborator.Elaborate(tree, "alu").Design;
            RtlDrawing drawing = RtlDrawing.Of(Project.New("p"), RtlExtractor.Of(design, "alu"), "alu");

            MuxPart[] muxes = drawing.Module.Parts.OfType<MuxPart>().ToArray();
            Assert.AreEqual(2, muxes.Length, "a mux for s and one for cout");
            Assert.IsTrue(muxes.All(m => m.Inputs == 4), "four inputs each, a value of a[3:2] each");
            Assert.AreEqual(4, drawing.Module.Parts.OfType<OperatorPart>().Count(), "+, -, & and |");
            Assert.IsFalse(drawing.Module.Parts.OfType<BoxPart>().Any(b => b.Label.StartsWith("=", StringComparison.Ordinal) || b.Label == "ext"),
                "no match and no widening left over");
            Hold(drawing, RtlExtractor.Of(design, "alu"), "alu");
        }

        private static RtlDrawing Draw(string source, string top)
        {
            SyntaxTree tree = SyntaxReader.ReadText(source, top + ".sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, top);
            Assert.IsFalse(e.HasErrors, string.Join("; ", e.Diagnostics));
            RtlNetlist netlist = RtlExtractor.Of(e.Design, top);
            RtlDrawing drawing = RtlDrawing.Of(Project.New("p"), netlist, top);
            Hold(drawing, netlist, top);
            return drawing;
        }

        /// <summary>The name of the net a part's pin is on.</summary>
        private static string NetAt(RtlDrawing drawing, Part part, string pin) =>
            Netlist.Of(drawing.Project, drawing.Module).NetOf(new PinRef(part.Id, pin))?.Name;

        /// <summary>A register's clock is on its clk pin and the edge its body tests on rst, which way round they are written (the review of #108).</summary>
        [TestCase("posedge clk or negedge rst_n")]
        [TestCase("negedge rst_n or posedge clk")]
        public void ARegistersClockAndReset_AreOnTheirOwnPins(string edges)
        {
            RtlDrawing drawing = Draw(
                "module r (input logic clk, rst_n, input logic [3:0] d, output logic [3:0] q);\n" +
                "    always_ff @(" + edges + ")\n" +
                "        if (!rst_n) q <= 4'b0;\n" +
                "        else q <= d;\n" +
                "endmodule\n", "r");
            RegisterPart register = drawing.Module.Parts.OfType<RegisterPart>().Single();
            Assert.AreEqual("clk", NetAt(drawing, register, "clk"));
            Assert.AreEqual("rst_n", NetAt(drawing, register, "rst"));
        }

        /// <summary>
        /// Glue is a wire only where its reader sees what the wire shows: a sign extended into y stays a box between t and y,
        /// so the adder reading t is wired from t; and the truth of a bit that may be x stays a box, so the mux's select shows
        /// the 0 it takes for one (the review of #108).
        /// </summary>
        [Test]
        public void GlueIsAWire_OnlyWhereItsReaderSeesWhatTheWireShows()
        {
            RtlDrawing signs = Draw(
                "module g (input logic [3:0] a, b, output logic signed [7:0] y, output logic [3:0] z);\n" +
                "    logic [3:0] t;\n" +
                "    assign t = a ^ b;\n" +
                "    assign y = $signed(t);\n" +
                "    assign z = t + 4'd1;\n" +
                "endmodule\n", "g");
            OperatorPart adder = signs.Module.Parts.OfType<OperatorPart>().Single(o => o.Op == OperatorKind.Add);
            Assert.AreEqual("t", NetAt(signs, adder, "a"), "the adder reads t, not y");

            RtlDrawing truth = Draw(
                "module m (input logic c, input logic [3:0] a, b, output logic [3:0] y);\n" +
                "    always_comb\n" +
                "        if (c) y = a;\n" +
                "        else y = b;\n" +
                "endmodule\n", "m");
            Assert.AreEqual(1, truth.Module.Parts.OfType<BoxPart>().Count(b => b.Label == "true"), "c's truth a box: c may be x");
        }

        /// <summary>A signal named as a part with no name of its own would be keeps its name on its net (the review of #108).</summary>
        [Test]
        public void ASignalNamedLikeAPart_KeepsItsName()
        {
            RtlDrawing drawing = Draw(
                "module cla (input logic a, b, c, output logic y);\n" +
                "    logic p1, p2, p3;\n" +
                "    assign p1 = a & b;\n" +
                "    assign p2 = p1 ^ c;\n" +
                "    assign p3 = p2 | a;\n" +
                "    assign y = p3 & b;\n" +
                "endmodule\n", "cla");
            Netlist nets = Netlist.Of(drawing.Project, drawing.Module);
            foreach (string name in new[] { "p1", "p2", "p3" })
                Assert.IsNotNull(nets.Named(name), name + " keeps its name");
        }

        /// <summary>A process that is a box, reading and writing one signal, has a pin each way, and no wire from itself to itself.</summary>
        [Test]
        public void ABoxThatReadsAndWritesOneSignal_HasAPinEachWay()
        {
            RtlDrawing drawing = Draw(
                "module tick (output logic clk);\n" +
                "    initial clk = 1'b0;\n" +
                "    always #5 clk = ~clk;\n" +
                "endmodule\n", "tick");
            foreach (BoxPart box in drawing.Module.Parts.OfType<BoxPart>())
                Assert.AreEqual(box.Ports.Count, box.Ports.Select(p => p.Name).Distinct().Count(), box.Label + "'s pins each a name of its own");
        }

        private static IEnumerable<(Project Project, Design Design)> Load(string kind, string name)
        {
            if (kind == "exercise")
            {
                Project solution = ExerciseFiles.Solution(ExerciseFiles.Load(name));
                CompiledProject compiled = BoardCompiler.Compile(solution);
                Elaboration e = Elaborator.Elaborate(compiled.Tree, solution.TopModule.Name);
                Assert.IsFalse(e.HasErrors);
                yield return (solution, e.Design);
                yield break;
            }

            string path = RepoRoot.Combine("Tools/ci/Oracle/corpus/" + kind + "/" + name);
            var files = new MemorySourceFiles();
            foreach (string other in Directory.GetFiles(Path.GetDirectoryName(path)))
                files.Add(Path.GetFileName(other), File.ReadAllText(other));
            SyntaxTree tree = SyntaxReader.ReadText(File.ReadAllText(path), name, files);
            foreach (ModuleDeclaration module in tree.Modules)
            {
                Elaboration e = Elaborator.Elaborate(tree, module.Name);
                if (!e.HasErrors)
                    yield return (Project.New("corpus"), e.Design);
            }
        }

        private static void Hold(RtlDrawing drawing, RtlNetlist netlist, string what, bool gates = false)
        {
            Module module = drawing.Module;
            Project project = drawing.Project;

            // Every operator, register, gate and instance a part of its own; glue -- a widening, a sign, a bit's truth, a join
            // of one driver -- is the wire it stands on, and a case label's constant is said on its match. With gates, an
            // operator drawn as its gates is none, and its gates are more.
            int operators = netlist.Cells.Count(c => c.Kind == RtlKind.Binary && Operators.TryFrom(c.Binary, out _)
                || c.Kind == RtlKind.Unary && Operators.TryFrom(c.Unary, out _));
            if (gates)
                Assert.LessOrEqual(module.Parts.OfType<OperatorPart>().Count(), operators, $"{what}: an operator a part or its gates");
            else
                Assert.AreEqual(operators, module.Parts.OfType<OperatorPart>().Count(), $"{what}: an operator a part");
            if (drawing.Opened == 0)
                Assert.AreEqual(0, drawing.InnerCount, $"{what}: wires inside only of an opening");
            Assert.AreEqual(netlist.Cells.Count(c => c.Kind == RtlKind.Register && c.Clock > 0 && (c.Inputs.Count == 2 || c.Inputs.Count == 3)),
                module.Parts.OfType<RegisterPart>().Count(), $"{what}: a register a part");
            if (!gates)
                Assert.AreEqual(netlist.Count(RtlKind.Gate) + netlist.Count(RtlKind.Instance), module.Parts.Count(p => p is GatePart || p is BlockPart
                    || p is BoxPart box && netlist.Cells.Any(c => c.Kind == RtlKind.Instance && c.Module == box.Label)), $"{what}: a gate or a block an instance");
            Assert.AreEqual(netlist.Inputs.Count + netlist.Outputs.Count, module.Parts.OfType<PortPart>().Count(), $"{what}: a port each");

            // Every wire ends on pins its parts have, and the board's netlist of it has no problem.
            Netlist nets = Netlist.Of(project, module);
            Assert.IsEmpty(nets.Problems, $"{what}: " + string.Join("; ", nets.Problems));

            // Every signal something reads has its net named for it, so the drawing reads its value by name -- but a net of a
            // port, which is the port's.
            foreach (Wire wire in module.Wires.Where(w => w.NetName != null))
            {
                Net net = nets.NetOfWire(wire.Id);
                if (net.Ports.Count == 0)
                    Assert.AreEqual(wire.NetName, net.Name, $"{what}: the net of {wire.NetName} is named for it");
            }

            // No wire runs from a pin of a part to a pin of the same part.
            foreach (Wire wire in module.Wires)
                Assert.IsFalse(wire.From.Pin.Equals(wire.To.Pin), $"{what}: a wire from {wire.From.Pin} to itself");

            // Nothing stands on anything.
            var outlines = module.Parts.Select(p => PartGeometry.Placed(project, p, module.Layout.PlacementOf(p.Id))).ToArray();
            for (int i = 0; i < outlines.Length; i++)
                for (int j = i + 1; j < outlines.Length; j++)
                {
                    PartOutline a = outlines[i], b = outlines[j];
                    bool apart = a.Right <= b.Left + 1e-6 || b.Right <= a.Left + 1e-6 || a.Top <= b.Bottom + 1e-6 || b.Top <= a.Bottom + 1e-6;
                    Assert.IsTrue(apart, $"{what}: {module.Parts[i].Name} and {module.Parts[j].Name} stand on one another");
                }

            // Every wire routed, from its pin to its pin.
            Dictionary<int, IReadOnlyList<Point>> routes = WireGeometry.Wires(project, module);
            Assert.AreEqual(module.Wires.Count, routes.Count, $"{what}: every wire routed");
        }
    }
}
