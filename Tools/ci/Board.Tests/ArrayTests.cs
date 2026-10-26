using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Arrays of blocks (Part 12c): a block of N copies has every pin a bus of its copies' ports side by side, is printed as
    /// a <c>generate for</c> in the course's style, is held to 1 to 64 copies, and runs -- an eight-bit ripple-carry adder
    /// of eight full adders adds. Icarus runs the samples as well (<c>BoardOracleTests</c>), and the save keeps the count
    /// (<c>ProjectRecordTests</c>).
    /// </summary>
    public class ArrayTests
    {
        [Test]
        public void AnArraysPins_AreItsCopiesPortsSideBySide()
        {
            Project project = SampleBoards.Build("ripple8");
            BlockPart fa = project.TopModule.Parts.OfType<BlockPart>().Single();
            Assert.AreEqual(8, fa.Copies);
            Dictionary<string, int> widths = fa.Pins(project).ToDictionary(p => p.Name, p => p.Width);
            CollectionAssert.AreEquivalent(new[] { "a", "b", "cin", "sum", "cout" }, widths.Keys);
            Assert.IsTrue(widths.Values.All(w => w == 8), "every one-bit port of a full adder, eight times");
            Assert.AreEqual(1, (fa with { Copies = 1 }).Pins(project).First().Width, "and one copy is the block as it was");
        }

        [Test]
        public void AnArray_IsPrintedAsAGenerateFor_InTheCoursesStyle()
        {
            Project project = SampleBoards.Build("ripple8");
            string top = BoardPrinter.Print(project).Single(p => p.Path == "ripple8.sv").Text;
            BlockPart fa = project.TopModule.Parts.OfType<BlockPart>().Single();
            StringAssert.Contains($"for (genvar i = 0; i < 8; i++) begin : {fa.Name}", top);
            StringAssert.Contains("full_adder u (", top);
            StringAssert.Contains("[i])", top, "each copy takes its own bit");

            // A port wider than a bit takes its copy's bits with an indexed part-select.
            CompiledProject compiled = BoardCompiler.Compile(new Project
            {
                Name = "wide", Top = "wide",
                Modules = new[] { WideModule(), WideTop(4) },
                Settings = project.Settings,
            });
            Assert.IsTrue(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
            string wide = BoardPrinter.Print(compiled).Single(p => p.Text.Contains("module wide")).Text;
            StringAssert.Contains("[i*2 +: 2]", wide, "copy i's two bits");
        }

        /// <summary>A module that passes a two-bit input to a two-bit output.</summary>
        private static Module WideModule()
        {
            var editor = new BoardEditor(Project.New("pass"));
            int i = editor.Add("pass", new PortPart { Name = "d", Width = 2 }, new Placement(0, 0)).Id;
            int o = editor.Add("pass", new PortPart { Name = "q", Width = 2, Direction = PortDirection.Output }, new Placement(4, 0)).Id;
            Assert.IsTrue(editor.Connect("pass", Endpoint.At(i, "out"), Endpoint.At(o, "in")).Done);
            return editor.Project.Find("pass");
        }

        /// <summary>A top with an array of <paramref name="copies"/> of it, between an eight-bit input and output.</summary>
        private static Module WideTop(int copies)
        {
            var editor = new BoardEditor(new Project { Name = "wide", Top = "pass", Modules = new[] { WideModule() } });
            Assert.IsTrue(editor.AddModule("wide").Done);
            int d = editor.Add("wide", new PortPart { Name = "d", Width = 2 * copies }, new Placement(0, 0)).Id;
            int q = editor.Add("wide", new PortPart { Name = "q", Width = 2 * copies, Direction = PortDirection.Output }, new Placement(8, 0)).Id;
            int u = editor.Add("wide", new BlockPart { Module = "pass", Copies = copies }, new Placement(4, 0)).Id;
            Assert.IsTrue(editor.Connect("wide", Endpoint.At(d, "out"), Endpoint.At(u, "d")).Done);
            Assert.IsTrue(editor.Connect("wide", Endpoint.At(u, "q"), Endpoint.At(q, "in")).Done);
            return editor.Project.Find("wide");
        }

        [Test]
        public void ACountOutside1To64_IsRefused_AsIsABusWiderThanABusCanBe()
        {
            Project project = SampleBoards.Build("half_adders");
            BlockPart ha = project.TopModule.Parts.OfType<BlockPart>().Single();
            var editor = new BoardEditor(project);

            EditResult none = editor.Change(project.Top, ha with { Copies = 0 });
            Assert.IsFalse(none.Done);
            StringAssert.Contains("1 to 64", none.Refusal);
            Assert.IsFalse(editor.Change(project.Top, ha with { Copies = 65 }).Done);
            Assert.AreSame(project, editor.Project, "and nothing changed");
        }

        [Test]
        public void TheRippleCarryAdder_Adds()
        {
            Project project = SampleBoards.Build("ripple8");
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(1_000_000);

            SignalId Of(string port)
            {
                Assert.IsTrue(sim.Design.TryFind(compiled.DesignPath + "." + port, out SignalId signal), port);
                return signal;
            }

            SignalId a = Of("a"), b = Of("b"), cin = Of("cin"), sum = Of("sum"), cout = Of("cout");
            Track first = project.Stimulus.Tracks.Single(t => t.Port == "a");
            int checkedVectors = 0;
            ulong At(SignalId signal, long time)
            {
                Assert.IsTrue(sim.History.ValueAt(signal, time).TryGetUInt64(out ulong value), $"a known value at {time}");
                return value;
            }

            foreach (Step step in first.Steps)
            {
                long settled = step.Time + 39;   // the moment before the next vector: the carry has rippled
                ulong x = At(a, settled), y = At(b, settled), c = At(cin, settled);
                ulong total = At(sum, settled) | (At(cout, settled) << 8);
                Assert.AreEqual(x + y + c, total, $"{x} + {y} + {c} at {settled}");
                checkedVectors++;
            }

            Assert.AreEqual(12, checkedVectors);
        }
    }
}
