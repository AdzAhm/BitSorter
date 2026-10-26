using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A block made from a selection (Part 12b): the parts moved into a new module with a port for every net crossing the
    /// selection's edge, one instance in their place, as one step -- and the circuit doing exactly what it did, which the
    /// run before and after says.
    /// </summary>
    public class BlockMakerTests
    {
        private static int Must(EditResult r)
        {
            Assert.IsTrue(r.Done, r.Refusal);
            return r.Id;
        }

        /// <summary>A full adder drawn flat -- two XORs, two ANDs and an OR -- stepped through its eight inputs: the gates by name.</summary>
        private static (BoardEditor Editor, Dictionary<string, int> Ids) FlatFullAdder()
        {
            var editor = new BoardEditor(Project.New("fa"));
            var ids = new Dictionary<string, int>();
            int x = 0;
            int Add(string name, Part part) => ids[name] = Must(editor.Add("fa", part with { Name = name }, new Placement(x++, 0)));
            void Wire(string from, string fromPin, string to, string toPin) =>
                Must(editor.Connect("fa", Endpoint.At(ids[from], fromPin), Endpoint.At(ids[to], toPin)));

            foreach (string port in new[] { "a", "b", "cin" })
                Add(port, new PortPart());
            foreach (string port in new[] { "sum", "cout" })
                Add(port, new PortPart { Direction = PortDirection.Output });
            Add("x1", new GatePart { Kind = GateKind.Xor, Rise = 2, Fall = 2 });
            Add("n1", new GatePart { Kind = GateKind.And, Rise = 1, Fall = 1 });
            Add("x2", new GatePart { Kind = GateKind.Xor, Rise = 2, Fall = 2 });
            Add("n2", new GatePart { Kind = GateKind.And, Rise = 1, Fall = 1 });
            Add("o", new GatePart { Kind = GateKind.Or, Rise = 1, Fall = 1 });

            Wire("a", "out", "x1", "a");
            Wire("b", "out", "x1", "b");
            Wire("a", "out", "n1", "a");
            Wire("b", "out", "n1", "b");
            Wire("x1", "y", "x2", "a");
            Wire("cin", "out", "x2", "b");
            Wire("x1", "y", "n2", "a");
            Wire("cin", "out", "n2", "b");
            Wire("x2", "y", "sum", "in");
            Wire("n1", "y", "o", "a");
            Wire("n2", "y", "o", "b");
            Wire("o", "y", "cout", "in");

            var tracks = new List<Track>();
            string[] inputs = { "a", "b", "cin" };
            for (int i = 0; i < inputs.Length; i++)
            {
                var steps = new List<Step>();
                for (int v = 0; v < 8; v++)
                    steps.Add(new Step(v * 20, LogicVec.FromUInt64((ulong)(v >> (2 - i)) & 1, 1)));
                tracks.Add(new Track { Port = inputs[i], Steps = steps });
            }

            Must(editor.SetStimulus(new Stimulus { Tracks = tracks, End = 160 }));
            return (editor, ids);
        }

        /// <summary>The top's outputs over the whole run, as settled changes: what must not change.</summary>
        private static string[] Outputs(Project project)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(200);
            var seen = new List<string>();
            foreach (string port in new[] { "sum", "cout" })
                seen.AddRange(sim.History.SettledChanges(sim.Design.Find(compiled.Top + "." + port)).Select(c => $"{port} {c.Time} {c.Value.Format(Radix.Binary)}"));
            return seen.ToArray();
        }

        [Test]
        public void AHalfAdderSelected_BecomesABlockOfItsTwoGates_AndTheRunIsTheSame()
        {
            (BoardEditor editor, Dictionary<string, int> ids) = FlatFullAdder();
            string[] before = Outputs(editor.Project);
            Assert.IsNotEmpty(before, "sanity: the outputs change");

            int block = Must(BlockMaker.Make(editor, "fa", new[] { ids["x1"], ids["n1"] }));

            Module made = editor.Project.Find("block1");
            Assert.IsNotNull(made, "a module called block1");
            CollectionAssert.AreEquivalent(new[] { "x1", "n1" }, made.Parts.Where(p => !(p is PortPart)).Select(p => p.Name), "of the two gates");
            Assert.AreEqual(2, made.Ports.Count(p => p.Direction == PortDirection.Input), "a and b come in: " + string.Join(", ", made.Ports.Select(p => p.Name)));
            Assert.AreEqual(2, made.Ports.Count(p => p.Direction == PortDirection.Output), "the sum and the carry go out");
            CollectionAssert.IsSubsetOf(new[] { "a", "b" }, made.Ports.Select(p => p.Name).ToArray(), "ports named for their nets");

            Module top = editor.Project.Find("fa");
            Assert.IsNull(top.FindPart(ids["x1"]), "the gates have left the top");
            Assert.IsInstanceOf<BlockPart>(top.FindPart(block), "and an instance stands in their place");

            CollectionAssert.AreEqual(before, Outputs(editor.Project), "the circuit does exactly what it did");
        }

        [Test]
        public void ABlockMadeOfTheSecondHalf_TakesItsInputFromTheFirst_AndTheRunIsTheSame()
        {
            (BoardEditor editor, Dictionary<string, int> ids) = FlatFullAdder();
            string[] before = Outputs(editor.Project);

            // x2, n2 and the OR: x1's sum and n1's carry come in from outside, and both sum and cout go out.
            Must(BlockMaker.Make(editor, "fa", new[] { ids["x2"], ids["n2"], ids["o"] }, "rest"));
            Module made = editor.Project.Find("rest");
            Assert.AreEqual(3, made.Ports.Count(p => p.Direction == PortDirection.Input), "x1's sum, n1's carry and cin");
            Assert.AreEqual(2, made.Ports.Count(p => p.Direction == PortDirection.Output));

            CollectionAssert.AreEqual(before, Outputs(editor.Project));
        }

        [Test]
        public void ABlockInsideABlock_IsMade_AndTheRunIsTheSame()
        {
            (BoardEditor editor, Dictionary<string, int> ids) = FlatFullAdder();
            string[] before = Outputs(editor.Project);

            int inner = Must(BlockMaker.Make(editor, "fa", new[] { ids["x1"], ids["n1"] }));
            Must(BlockMaker.Make(editor, "fa", new[] { inner, ids["x2"] }, "outer"));
            Assert.IsTrue(editor.Project.Find("outer").Parts.OfType<BlockPart>().Any(b => b.Module == "block1"), "the first block, inside the second");

            CollectionAssert.AreEqual(before, Outputs(editor.Project));
        }

        [Test]
        public void MakingABlock_IsOneStep_AndUndoPutsTheGatesBack()
        {
            (BoardEditor editor, Dictionary<string, int> ids) = FlatFullAdder();
            Project before = editor.Project;
            Must(BlockMaker.Make(editor, "fa", new[] { ids["x1"], ids["n1"] }));
            Assert.IsTrue(editor.Undo());
            Assert.AreSame(before, editor.Project, "one undo, and the document is as it was");
        }

        [Test]
        public void APortOfTheModule_ANameTaken_AndNothingSelected_AreRefused_AndChangeNothing()
        {
            (BoardEditor editor, Dictionary<string, int> ids) = FlatFullAdder();
            Project before = editor.Project;

            EditResult port = BlockMaker.Make(editor, "fa", new[] { ids["a"], ids["x1"] });
            Assert.IsFalse(port.Done);
            StringAssert.Contains("port", port.Refusal);

            EditResult taken = BlockMaker.Make(editor, "fa", new[] { ids["x1"] }, "fa");
            Assert.IsFalse(taken.Done, "a module of that name is there already");

            Assert.IsFalse(BlockMaker.Make(editor, "fa", new int[0]).Done);
            Assert.AreSame(before, editor.Project, "and nothing changed");
        }
    }
}
