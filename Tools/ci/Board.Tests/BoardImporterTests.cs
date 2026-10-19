using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using EngineStimulus = BitSorter.Sim.Stimulus;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The first importer: structural SystemVerilog drawn as a board. What the printer writes comes
    /// back as the board it was, and the course's own gate-level style comes in as its gates.
    /// </summary>
    public class BoardImporterTests
    {
        private static SyntaxTree Corpus(string file)
        {
            string root = RepoRoot.Combine("Tools/ci/Oracle/corpus");
            var files = new MemorySourceFiles();
            foreach (string path in Directory.GetFiles(root, "*.sv*", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(path));
            SyntaxTree tree = SyntaxReader.Read(file, files);
            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));
            return tree;
        }

        [TestCaseSource(typeof(SampleBoards), nameof(SampleBoards.Names))]
        public void APrintedBoard_ImportsAndPrintsAgain_ToTheSameText(string board)
        {
            Project original = SampleBoards.Build(board);
            IReadOnlyList<PrintedSource> printed = BoardPrinter.Print(original);

            // The design's files, not the testbench: the stimulus is the project's, and comes across as it is.
            (MemorySourceFiles files, List<string> paths) = BoardPrinter.AsSources(printed);
            CompiledProject compiled = BoardCompiler.Compile(original);
            SyntaxTree read = SyntaxReader.ReadAll(paths.Where(p => p != compiled.Testbench + ".sv"), files);

            ImportResult imported = BoardImporter.Import(read, original.Top);
            Assert.IsTrue(imported.Succeeded, string.Join("\n", imported.Problems));

            Project again = imported.Project with { Stimulus = original.Stimulus };
            IReadOnlyList<PrintedSource> reprinted = BoardPrinter.Print(again);

            CollectionAssert.AreEqual(printed.Select(p => p.Path), reprinted.Select(p => p.Path));
            for (int i = 0; i < printed.Count; i++)
                Assert.AreEqual(printed[i].Text, reprinted[i].Text, printed[i].Path);
        }

        [Test]
        public void TheCoursesGateLevelStyle_ComesInAsItsGates_WithTheirDelaysAndNetNames()
        {
            ImportResult imported = BoardImporter.Import(Corpus("structural/mux2.sv"), "mux2");
            Assert.IsTrue(imported.Succeeded, string.Join("\n", imported.Problems));

            Module mux = imported.Project.Find("mux2");
            CollectionAssert.AreEqual(new[] { "a", "b", "s", "y" }, mux.Ports.Select(p => p.Name));
            Assert.AreEqual("chosen when s is 0", mux.Ports[0].Comment);

            var gates = mux.Parts.OfType<GatePart>().ToDictionary(g => g.Name);
            Assert.AreEqual(GateKind.Not, gates["g1"].Kind);
            Assert.AreEqual(3, gates["g1"].Rise);
            Assert.AreEqual(GateKind.Nand, gates["g4"].Kind);
            Assert.AreEqual(1, gates["g4"].Fall);

            Netlist nets = Netlist.Of(imported.Project, mux);
            Assert.AreEqual("s_n", nets.NetOf(new PinRef(gates["g1"].Id, "y")).Name, "a net keeps the name it was written with");
            Assert.AreEqual(NetNaming.Given, nets.Named("pick_a").Naming);
        }

        [Test]
        public void TheImportedMux_RunsAsTheFileItCameFromDoes_GlitchIncluded()
        {
            SyntaxTree tree = Corpus("structural/mux2.sv");
            Design written = Elaborator.Elaborate(tree, "mux2").Design;

            ImportResult imported = BoardImporter.Import(tree, "mux2");
            CompiledProject compiled = BoardCompiler.Compile(imported.Project);
            Design drawn = Elaborator.Elaborate(compiled.Tree, "mux2").Design;

            // a = b = 1 and s falling: the slow inverter lets y dip -- the static hazard the file is about.
            EngineStimulus Drive(Design design) => new EngineStimulus()
                .Set(0, design.Find("mux2.a"), LogicVec.FromUInt64(1, 1))
                .Set(0, design.Find("mux2.b"), LogicVec.FromUInt64(1, 1))
                .Set(0, design.Find("mux2.s"), LogicVec.FromUInt64(1, 1))
                .Set(20, design.Find("mux2.s"), LogicVec.FromUInt64(0, 1))
                .Set(40, design.Find("mux2.s"), LogicVec.FromUInt64(1, 1));

            var a = new Simulator(written, Drive(written));
            var b = new Simulator(drawn, Drive(drawn));
            a.RunTo(60);
            b.RunTo(60);

            foreach (string name in new[] { "mux2.y", "mux2.s_n", "mux2.pick_a", "mux2.pick_b" })
            {
                string[] Changes(Simulator sim) => sim.History.SettledChanges(sim.Design.Find(name)).Select(c => $"{c.Time} {c.Value.Format(Radix.Binary)}").ToArray();
                CollectionAssert.AreEqual(Changes(a), Changes(b), name);
            }

            Assert.IsTrue(a.History.SettledChanges(written.Find("mux2.y")).Any(c => c.Time > 20 && c.Value.Format(Radix.Binary) == "0"), "sanity: the glitch is there to agree on");
        }

        [Test]
        public void AModuleWithBehaviour_IsRefused_ForWrittenCode()
        {
            SyntaxTree tree = SyntaxReader.ReadText(
                "module flip (input logic a, output logic y);\n    always_comb y = ~a;\nendmodule\n", "flip.sv");

            ImportResult imported = BoardImporter.Import(tree, "flip");

            Assert.IsFalse(imported.Succeeded);
            StringAssert.Contains("flip.sv:2: flip: it has an always_comb block", imported.Problems.Single());
            StringAssert.Contains("Part 12", imported.Problems.Single());
        }

        [Test]
        public void TheCoursesBinaryNand_ComesInAsANandGate()
        {
            // The course's first workshop: assign Z = X ~& Y. Not the standard's; read with a warning.
            SyntaxTree tree = SyntaxReader.ReadText(
                "module first (input logic X, input logic Y, output logic Z, output logic W, output logic V);\n" +
                "    assign Z = X ~& Y;\n" +
                "    assign W = X ~| Y;\n" +
                "    assign V = ~(X ~& Y);\n" +
                "endmodule\n", "first.sv");
            Assert.IsTrue(tree.Diagnostics.All(d => d.Code == DiagnosticCodes.NonStandardOperator), string.Join("\n", tree.Diagnostics));

            ImportResult imported = BoardImporter.Import(tree, "first");

            Assert.IsTrue(imported.Succeeded, string.Join("\n", imported.Problems));
            CollectionAssert.AreEquivalent(new[] { GateKind.Nand, GateKind.Nor, GateKind.And },
                imported.Project.Find("first").Parts.OfType<GatePart>().Select(g => g.Kind));
        }

        [TestCase("structural/full_adder.sv", "full_adder", "parameters in its header")]
        [TestCase("structural/alu_slice.sv", "alu_slice", "is not something the board draws")]
        public void WhatABoardCannotDraw_IsRefused_SayingWhatAndWhere(string file, string top, string said)
        {
            ImportResult imported = BoardImporter.Import(Corpus(file), top);

            Assert.IsFalse(imported.Succeeded);
            string problem = imported.Problems.First(p => p.Contains(said));
            StringAssert.Contains(top, problem);
            StringAssert.Contains(".sv:", problem, "and where");
        }
    }
}
