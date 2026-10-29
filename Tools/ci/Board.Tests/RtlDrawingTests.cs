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
            Assert.AreEqual(netlist.Count(RtlKind.Register), module.Parts.Count(p => p is RegisterPart || p is BoxPart box && box.Label.Length > 0
                && netlist.Cells.Any(c => c.Kind == RtlKind.Register && c.Inputs.Count > 3)), $"{what}: a register a part");
            Assert.AreEqual(netlist.Count(RtlKind.Gate) + netlist.Count(RtlKind.Instance), module.Parts.Count(p => p is GatePart || p is BlockPart
                || p is BoxPart box && netlist.Cells.Any(c => c.Kind == RtlKind.Instance && c.Module == box.Label)), $"{what}: a gate or a block an instance");
            Assert.AreEqual(netlist.Inputs.Count + netlist.Outputs.Count, module.Parts.OfType<PortPart>().Count(), $"{what}: a port each");

            // Every wire ends on pins its parts have, and the board's netlist of it has no problem.
            Netlist nets = Netlist.Of(project, module);
            Assert.IsEmpty(nets.Problems, $"{what}: " + string.Join("; ", nets.Problems));

            // Every signal something reads has its net named for it, so the drawing reads its value by name.
            foreach (Wire wire in module.Wires.Where(w => w.NetName != null))
                Assert.AreEqual(wire.NetName, nets.NetOfWire(wire.Id).Name.Length > 0 && nets.NetOfWire(wire.Id).Ports.Count > 0
                    ? wire.NetName : nets.NetOfWire(wire.Id).Name, $"{what}: the net of {wire.NetName} is named for it");

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
