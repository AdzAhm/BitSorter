using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The save format, <c>format: 5</c>: a document written out and read back is the same document.
    /// Here through System.Text.Json reading the same public fields Unity's reader does; Edit Mode holds
    /// it through JsonUtility itself.
    /// </summary>
    public class ProjectRecordTests
    {
        private static readonly JsonSerializerOptions Fields = new JsonSerializerOptions { IncludeFields = true };

        private static Project RoundTrip(Project project, List<string> problems)
        {
            string json = JsonSerializer.Serialize(ProjectRecord.From(project), Fields);
            return JsonSerializer.Deserialize<ProjectRecord>(json, Fields).ToProject(problems);
        }

        [TestCaseSource(typeof(SampleBoards), nameof(SampleBoards.Names))]
        public void ADocument_WrittenAndReadBack_PrintsTheSame_AndKeepsItsLayout(string board)
        {
            Project project = SampleBoards.Build(board);
            var problems = new List<string>();
            Project again = RoundTrip(project, problems);

            Assert.IsEmpty(problems);
            CollectionAssert.AreEqual(BoardPrinter.Print(project).Select(p => p.Text), BoardPrinter.Print(again).Select(p => p.Text));

            foreach (Module module in project.Modules)
            {
                Module read = again.Find(module.Name);
                Assert.AreEqual(module.NextId, read.NextId, "the next id comes back, so no id is given twice");
                foreach (Part part in module.Parts)
                {
                    Assert.AreEqual(part, read.FindPart(part.Id), "every part, field for field");
                    Assert.AreEqual(module.Layout.PlacementOf(part.Id), read.Layout.PlacementOf(part.Id));
                }
            }
        }

        [Test]
        public void TapsRoutesAndNames_ComeBack()
        {
            var b = new Bench();
            int g1 = b.Gate(Sim.GateKind.Not), g2 = b.Gate(Sim.GateKind.Not), g3 = b.Gate(Sim.GateKind.Not);
            int w = b.Wire(g1, "y", g2, "a");
            Bench.Must(b.Editor.NameWire(b.Module, w, "middle"));
            Bench.Must(b.Editor.Reroute(b.Module, w, new Route { Bends = new[] { new Point(1.5, 2) }, Smooth = true }));
            int tap = Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g3, "a"), Endpoint.OnWire(w), new Route { TapAt = 0.25 }));

            Module read = RoundTrip(b.Project, new List<string>()).Find(b.Module);

            Assert.AreEqual("middle", read.FindWire(w).NetName);
            Assert.AreEqual(w, read.FindWire(tap).To.TapOf);
            Assert.AreEqual(new Point(1.5, 2), read.Layout.Routes[w].Bends.Single());
            Assert.IsTrue(read.Layout.Routes[w].Smooth);
            Assert.AreEqual(0.25, read.Layout.Routes[tap].TapAt);
        }

        [Test]
        public void APartOfAKindNotKnown_IsLeftOut_SayingSo()
        {
            ProjectRecord file = ProjectRecord.From(SampleBoards.Build("half_adder"));
            file.modules[0].parts[4].kind = "memory";
            var problems = new List<string>();

            Project read = file.ToProject(problems);

            StringAssert.Contains("a part of a kind this BitSorter does not know, \"memory\"", problems.Single());
            Assert.AreEqual(5, read.Modules[0].Parts.Count, "the rest is there");
        }

        /// <summary>
        /// A constant saved wider than any value can be, with no value it can read -- a hand-edited or hostile save -- reads
        /// as one as wide as a value may be, saying so, never throwing (the 16c pilot: it threw from the workbench's Start, on
        /// every launch, since the circuit is what BitSorter opens on).
        /// </summary>
        [Test]
        public void AConstantSavedWiderThanAnyValue_ReadsAtTheWidest_SayingSo()
        {
            ProjectRecord file = ProjectRecord.From(SampleBoards.Build("half_adder"));
            file.modules[0].parts[4].kind = "constant";
            file.modules[0].parts[4].value = "";
            file.modules[0].parts[4].width = 2_000_000;
            var problems = new List<string>();

            Project read = null;
            Assert.DoesNotThrow(() => read = file.ToProject(problems));

            ConstantPart constant = read.Modules[0].Parts.OfType<ConstantPart>().Single();
            Assert.AreEqual(LogicVec.MaxWidth, constant.Value.Width, "as wide as a value may be");
            StringAssert.Contains("wide", string.Join("\n", problems));
        }

        /// <summary>
        /// A gate saved with a count of inputs no gate has -- a hand-edited or damaged save -- reads, and is checked and netted,
        /// with the count said wrong in words, never throwing (the 16d sweep: its pins threw on every launch, since the circuit is
        /// what BitSorter opens on).
        /// </summary>
        [TestCase(-1)]
        [TestCase(-2)]
        [TestCase(1_000_000)]
        public void AGateSavedWithACountNoGateHas_IsReadAndChecked_SayingSo(int inputs)
        {
            ProjectRecord file = ProjectRecord.From(SampleBoards.Build("half_adder"));
            Assert.AreEqual("gate", file.modules[0].parts[4].kind);
            file.modules[0].parts[4].inputs = inputs;
            Project read = file.ToProject(new List<string>());

            BoardEditor editor = null;
            Assert.DoesNotThrow(() => editor = new BoardEditor(read));
            Assert.DoesNotThrow(() => Netlist.Of(read, read.Modules[0]));
            StringAssert.Contains($"has {inputs} inputs", string.Join("\n", editor.Diagnostics.Select(d => d.Message)));
        }

        /// <summary>
        /// Two wires saved under one id -- a hand-edited or damaged save -- read, and are checked and netted, the id said wrong in
        /// the rules' words, never thrown (the 16d sweep: the netlist numbered its wires by how many ids it had, one short, and
        /// threw before the rule that says so was reached, on every launch).
        /// </summary>
        [Test]
        public void TwoWiresSavedUnderOneId_AreReadAndChecked_SayingSo()
        {
            ProjectRecord file = ProjectRecord.From(SampleBoards.Build("half_adder"));
            WireRecord[] wires = file.modules[0].wires;
            wires[wires.Length - 1].id = wires[0].id;
            Project read = file.ToProject(new List<string>());

            BoardEditor editor = null;
            Assert.DoesNotThrow(() => editor = new BoardEditor(read));
            Assert.DoesNotThrow(() => Netlist.Of(read, read.Modules[0]));
            StringAssert.Contains($"Wire {wires[0].id} has the same id as something else", string.Join("\n", editor.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void AFileFromANewerBitSorter_Reads_WithAWarning()
        {
            ProjectRecord file = ProjectRecord.From(SampleBoards.Build("half_adder"));
            file.format = ProjectRecord.Format + 1;
            var problems = new List<string>();

            file.ToProject(problems);

            StringAssert.Contains("newer BitSorter", problems.Single());
        }
    }
}
