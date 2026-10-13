using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
