using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Every part described by the code's own words (Ahmad, 2026-11-02: "they should have good description every part of
    /// them"): a part of the drawing of code says first what the comment for its line says, a block with none of its own what
    /// its module's says -- and every block, register, port and mux of the three RISC-V processors has one.
    /// </summary>
    public class ProcessorDescriptionTests
    {
        private const string Pick =
            "// A chooser, written for this test.\n" +            // 1
            "module pick (\n" +                                    // 2
            "    input  logic [1:0] a, b,\n" +                     // 3
            "    input  logic       s,     // which one: 1 for b\n" + // 4
            "    output logic [1:0] y\n" +                         // 5
            ");\n" +                                               // 6
            "    // The choice itself.\n" +                        // 7
            "    assign y = s ? b : a;\n" +                        // 8
            "endmodule\n";                                         // 9

        [Test]
        public void APart_SaysFirstWhatTheCommentForItsLineSays()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("pick", Pick).Done);
            double now = 0;
            var session = new WorkbenchSession(editor.Project with { Top = "pick" }, () => now += 0.01);
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: the drawing of its code");

            int mux = session.DrawnModule.Parts.OfType<MuxPart>().Single().Id;
            int s = session.DrawnModule.Parts.OfType<PortPart>().Single(p => p.Name == "s").Id;
            int a = session.DrawnModule.Parts.OfType<PortPart>().Single(p => p.Name == "a").Id;
            Assert.AreEqual("The choice itself.", session.CommentOfPart(mux), "the comment line right above its line");
            Assert.AreEqual("which one: 1 for b", session.CommentOfPart(s), "the comment at the end of its line");
            Assert.IsNull(session.CommentOfPart(a), "a line the code says nothing of");

            Inspection inspected = Inspector.Of(session.DrawnProject, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: s),
                session.DescribePart, session.LineOfPart, session.CommentOfPart);
            StringAssert.StartsWith("Which one: 1 for b. ", inspected.Says, "the code's words first, as a sentence");
            StringAssert.EndsWith("From line 4.", inspected.Says, "and where it comes from, last");
        }

        [Test]
        public void ABlockWithNoCommentOfItsOwn_TakesItsModules()
        {
            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddWrittenModule("pick", Pick).Done);
            Assert.IsTrue(editor.Add("top", new BlockPart { Name = "p", Module = "pick" }, new Placement(5, 5)).Done);
            double now = 0;
            var session = new WorkbenchSession(editor.Project, () => now += 0.01);
            int block = session.DrawnModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.AreEqual("A chooser, written for this test.", session.CommentOfPart(block));
        }

        /// <summary>
        /// The checklist: every block, register, port and mux of each processor's drawing -- at its top and in every module
        /// stepped into, a controller's drawing included -- described by a comment of its code.
        /// </summary>
        [TestCase("Single-cycle RISC-V")]
        [TestCase("Multicycle RISC-V")]
        [TestCase("Pipelined RISC-V")]
        public void EveryPartOfAProcessor_IsDescribedByItsCode(string sample)
        {
            Project project = WorkbenchSamples.All.Single(x => x.Name == sample).Build();
            double now = 0;
            var session = new WorkbenchSession(project, () => now += 0.01);
            var missing = new List<string>();
            int described = 0;
            Walk(session, missing, new HashSet<string>(), ref described);
            TestContext.WriteLine($"{described} parts described, {missing.Count} not");
            Assert.IsEmpty(missing, $"{missing.Count} parts of {sample} the code says nothing of:\n" + string.Join("\n", missing));
        }

        private static void Walk(WorkbenchSession session, List<string> missing, HashSet<string> seen, ref int described)
        {
            if (!seen.Add(session.Module))
                return;
            if (session.ShowsController)
                session.DrawController(false);   // the reading has the code's words; the drawing's parts are what is checked
            Module drawn = session.DrawnModule;
            if (drawn == null)
                return;

            List<Part> parts = drawn.Parts.ToList();
            foreach (Part part in parts)
            {
                if (!(part is BlockPart || part is RegisterPart || part is PortPart || part is MuxPart))
                    continue;
                if (session.CommentOfPart(part.Id) != null)
                {
                    described++;
                    continue;
                }
                string kind = part.GetType().Name.Replace("Part", "").ToLowerInvariant();
                int line = session.LineOfPart(part.Id);
                string[] code = (session.Project.Find(session.Module)?.Source ?? "").Replace("\r\n", "\n").Split('\n');
                string text = line > 0 && line <= code.Length ? code[line - 1].Trim() : "";
                missing.Add($"{session.Module}: {kind} {part.Name} (line {line}: {text})");
            }

            foreach (Part part in parts)
                if (part is BlockPart && session.CanStepInto(part.Id) && session.StepInto(part.Id))
                {
                    Walk(session, missing, seen, ref described);
                    session.StepOut();
                }
        }
    }
}
