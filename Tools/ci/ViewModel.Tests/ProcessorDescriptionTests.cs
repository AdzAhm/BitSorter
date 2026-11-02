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

        [Test]
        public void ADrawnBlock_TakesItsDrawnModulesComment()
        {
            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddModule("half").Done);
            Project project = editor.Project.WithModule(editor.Project.Find("half") with { Comment = "adds two bits" });
            editor = new BoardEditor(project);
            Assert.IsTrue(editor.Add("top", new BlockPart { Name = "h", Module = "half" }, new Placement(5, 5)).Done);
            double now = 0;
            var session = new WorkbenchSession(editor.Project, () => now += 0.01);
            Assert.IsFalse(session.ShowsDrawingOfCode, "sanity: a drawn circuit");
            int block = session.DrawnModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.AreEqual("adds two bits", session.CommentOfPart(block));
            Inspection inspected = Inspector.Of(session.DrawnProject, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: block),
                session.DescribePart, session.LineOfPart, session.CommentOfPart);
            StringAssert.StartsWith("Adds two bits. ", inspected.Says, "as a sentence, first");
        }

        /// <summary>
        /// The words are the ones written for each part, not a neighbour's: a block, a port and a mux of each processor, each
        /// held to the start of its comment, and the inspector's line to the comment's first sentence (the M3 review).
        /// </summary>
        [TestCase("Single-cycle RISC-V", "scpu", "control", "The control: turns the instruction, and a branch's comparison, into")]
        [TestCase("Multicycle RISC-V", "mcpu_control", "rst", "asynchronous, active high: the machine goes back to FETCH")]
        [TestCase("Multicycle RISC-V", "mcpu_datapath", null, "The ALU's a: register A (0), the PC (1, to step it by four in FETCH)")]
        [TestCase("Pipelined RISC-V", "pcpu", "if_id", "The IF/ID register: carries the fetched instruction and its address into decode")]
        [TestCase("Pipelined RISC-V", "pcpu_decode", "hazard", "The hazard unit: a stall if the instruction in execute is a load")]
        [TestCase("Pipelined RISC-V", "pcpu_execute", "forward", "The forwarding unit: finds, for each register operand, whether")]
        public void APart_IsDescribedByTheCommentWrittenForIt(string sample, string module, string part, string starts)
        {
            Project project = WorkbenchSamples.All.Single(x => x.Name == sample).Build();
            double now = 0;
            var session = new WorkbenchSession(project, () => now += 0.01);
            if (session.Module != module)
                Assert.IsTrue(session.StepInto(session.DrawnModule.Parts.OfType<BlockPart>().First(b => b.Module == module).Id), "into " + module);
            if (session.ShowsController)
                session.DrawController(false);

            // A mux has no name of its own: the one whose comment it is, found among them.
            Part found = part != null
                ? session.DrawnModule.FindPart(part)
                : session.DrawnModule.Parts.OfType<MuxPart>().FirstOrDefault(m => session.CommentOfPart(m.Id)?.StartsWith(starts) == true);
            Assert.IsNotNull(found, $"{part ?? "a mux"} in {module}");
            StringAssert.StartsWith(starts, session.CommentOfPart(found.Id));

            Inspection inspected = Inspector.Of(session.DrawnProject, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: found.Id),
                session.DescribePart, session.LineOfPart, session.CommentOfPart);
            string first = CodeComments.FirstSentence(session.CommentOfPart(found.Id));
            StringAssert.StartsWith(char.ToUpperInvariant(first[0]) + first.Substring(1).TrimEnd(',', ';'), inspected.Says);
            StringAssert.EndsWith($"From line {session.LineOfPart(found.Id)}.", inspected.Says);
        }

        [Test]
        public void TheSingleCycleDatapathsALU_SaysWhatItIsFor()
        {
            Project project = WorkbenchSamples.All.Single(x => x.Name == "Single-cycle RISC-V").Build();
            double now = 0;
            var session = new WorkbenchSession(project, () => now += 0.01);
            if (session.DrawnModule.FindPart("datapath") == null)
                Assert.IsTrue(session.StepInto(session.DrawnModule.Parts.OfType<BlockPart>().First(b => b.Module == "scpu").Id));
            Assert.IsTrue(session.StepInto(session.DrawnModule.FindPart("datapath").Id));
            BlockPart alu = session.DrawnModule.Parts.OfType<BlockPart>().Single(b => b.Name == "alu");
            StringAssert.StartsWith("The ALU: the result of an arithmetic instruction", session.CommentOfPart(alu.Id),
                "the comment over its instance's line");
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
