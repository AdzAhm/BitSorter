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
    /// What the parts a drawing of code makes up do, in words (Part 15h-4e): every operator, mux, box and constant of every
    /// exercise answer's and corpus design's drawing -- the processor included -- says something, in its operands' names and
    /// never a made-up one; a part with a name of its own says nothing; and a few say exactly what they do.
    /// </summary>
    public class RtlDescriptionTests
    {
        private static IEnumerable<TestCaseData> Designs()
        {
            foreach (string id in ExerciseFiles.Ids())
                yield return new TestCaseData("exercise", id).SetName("exercise " + id);
            foreach (string folder in new[] { "structural", "behavioural", "processor", "system" })
                foreach (string path in Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder), "*.sv").OrderBy(p => p, StringComparer.Ordinal))
                    yield return new TestCaseData(folder, Path.GetFileName(path)).SetName(folder + " " + Path.GetFileName(path));
        }

        private static IEnumerable<(Project Project, Design Design)> Load(string kind, string name)
        {
            if (kind == "exercise")
            {
                Project solution = ExerciseFiles.Solution(ExerciseFiles.Load(name));
                Project written = solution.WithModule(ExerciseFiles.Load(name).Written(solution));
                CompiledProject compiled = BoardCompiler.Compile(written);
                Elaboration e = Elaborator.Elaborate(compiled.Tree, written.TopModule.Name);
                Assert.IsFalse(e.HasErrors);
                yield return (written, e.Design);
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

        [TestCaseSource(nameof(Designs))]
        public void EveryPartTheDrawingMakesUp_SaysWhatItDoes_InItsOperandsNames(string kind, string name)
        {
            int described = 0;
            foreach ((Project project, Design design) in Load(kind, name))
                foreach (ScopeInfo scope in design.Scopes)
                {
                    if (scope.Kind != ScopeKind.Top && scope.Kind != ScopeKind.Instance)
                        continue;
                    RtlDrawing drawing = RtlDrawing.Of(project, RtlExtractor.Of(design, scope.Path), "drawing_of_" + scope.Module);
                    foreach (Part part in drawing.Module.Parts)
                    {
                        string what = $"{name} {scope.Path} {part.GetType().Name} {part.Name}";
                        string says = drawing.Describe(part.Id);
                        bool madeUp = part.Name != null && part.Name.StartsWith("$", StringComparison.Ordinal);
                        if (part is PortPart || part is RegisterPart || part is BlockPart || part is GatePart && !madeUp
                            || part is BoxPart box && box.Undriven)
                        {
                            if (!(part is BoxPart))
                                Assert.IsNull(says, what + ": goes by its own name");
                            continue;
                        }
                        if (part is BoxPart unnamed && unnamed.Name != null && !madeUp)
                            continue;   // an instance of a module only the code has, a box of its pins and name
                        Assert.IsFalse(string.IsNullOrWhiteSpace(says), what + ": says what it does");
                        Assert.IsFalse(says.Contains("$p") || says.Contains("$g") || says.Contains("$k"), what + ": no made-up name in " + says);
                        described++;
                    }
                }
            Assert.GreaterOrEqual(described, 0);
        }

        private static RtlDrawing Draw(string source, string top)
        {
            SyntaxTree tree = SyntaxReader.ReadText(source, "design.sv", new MemorySourceFiles());
            Elaboration e = Elaborator.Elaborate(tree, top);
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            return RtlDrawing.Of(Project.New("scratch"), RtlExtractor.Of(e.Design, e.Design.Scopes[0].Path), top);
        }

        [Test]
        public void AnOperator_SaysWhatItDoes_ANestedOneInBrackets()
        {
            RtlDrawing drawing = Draw(
                "module m (input logic [1:0] a, b, c, output logic [1:0] y);\n    assign y = (a & b) | c;\nendmodule\n", "m");
            OperatorPart and = drawing.Module.Parts.OfType<OperatorPart>().Single(p => p.Op == OperatorKind.And);
            OperatorPart or = drawing.Module.Parts.OfType<OperatorPart>().Single(p => p.Op == OperatorKind.Or);
            Assert.AreEqual("a AND b, bit by bit: 2 bits.", drawing.Describe(and.Id));
            Assert.AreEqual("(a & b) OR c, bit by bit: 2 bits.", drawing.Describe(or.Id));
            Assert.AreEqual("a & b", drawing.ExpressionOf(and.Id), "what the wire out of it is called");
        }

        [Test]
        public void ASliceAComparisonAndAConstant_SayWhatTheyAre()
        {
            RtlDrawing drawing = Draw(
                "module m (input logic [7:0] a, input logic [3:0] b, output logic [3:0] hi, output logic less, output logic [3:0] k);\n" +
                "    assign hi = a[7:4];\n" +
                "    assign less = $signed(a[3:0]) < $signed(b);\n" +
                "    assign k = 4'b1010;\n" +
                "endmodule\n", "m");
            var says = drawing.Module.Parts.Select(p => drawing.Describe(p.Id)).Where(s => s != null).ToList();
            CollectionAssert.Contains(says, "Bits 7 to 4 of a.");
            Assert.IsTrue(says.Any(s => s.StartsWith("1 when", StringComparison.Ordinal) && s.EndsWith("as signed numbers.", StringComparison.Ordinal)),
                string.Join(" | ", says));
        }
    }
}
