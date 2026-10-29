using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.Sim;
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

        private static void Hold(RtlDrawing drawing, RtlNetlist netlist, string what)
        {
            Module module = drawing.Module;
            Project project = drawing.Project;

            // Every operator, register, gate and instance a part of its own; glue -- a widening, a sign, a bit's truth, a join
            // of one driver -- is the wire it stands on, and a case label's constant is said on its match.
            int operators = netlist.Cells.Count(c => c.Kind == RtlKind.Binary && Operators.TryFrom(c.Binary, out _)
                || c.Kind == RtlKind.Unary && Operators.TryFrom(c.Unary, out _));
            Assert.AreEqual(operators, module.Parts.OfType<OperatorPart>().Count(), $"{what}: an operator a part");
            Assert.AreEqual(netlist.Cells.Count(c => c.Kind == RtlKind.Register && c.Clock > 0 && (c.Inputs.Count == 2 || c.Inputs.Count == 3)),
                module.Parts.OfType<RegisterPart>().Count(), $"{what}: a register a part");
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
