using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// An exercise's document is the exercise's where it says so (Part 15d): its test, its library, its top and the blocks it
    /// stocks are refused any edit, in words, while everything the student builds round them is theirs; and the document says
    /// which exercise it is, and where a register starts, through a save.
    /// </summary>
    public class ExerciseLockTests
    {
        private static Project Opened(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            var problems = new List<string>();
            Project project = exercise.ToProject(problems);
            Assert.IsNotNull(project, string.Join("\n", problems));
            return project;
        }

        [Test]
        public void AnExercise_SaysItIsOne_AndWhereItsRegistersStart()
        {
            Project late = Opened("one-clock-late");
            Assert.AreEqual("one-clock-late", late.Settings.Exercise);
            Assert.AreEqual("0", late.Settings.RegisterStart?.Format(Radix.Binary));
            Assert.IsNull(Opened("start-from-nothing").Settings.RegisterStart, "x, until a reset");

            var problems = new List<string>();
            Project back = ProjectRecord.From(late).ToProject(problems);
            Assert.IsEmpty(problems);
            Assert.AreEqual("one-clock-late", back.Settings.Exercise);
            Assert.AreEqual("0", back.Settings.RegisterStart?.Format(Radix.Binary));
        }

        [Test]
        public void ItsTest_ItsLibrary_ItsTopAndItsPorts_AreRefusedAnyEdit()
        {
            var editor = new BoardEditor(Opened("half-adder"));
            string top = editor.Project.Top;

            StringAssert.Contains("The exercise's own test drives the inputs", editor.SetInput("a", 0, 10, LogicVec.FromBinary("1")).Refusal);
            StringAssert.Contains("its library is not changed here", editor.SetLibrary(GateLibrary.CourseCells(1)).Refusal);
            PortPart a = editor.Project.TopModule.Ports.Single(p => p.Name == "a");
            Assert.IsFalse(editor.Change(top, a with { Name = "x" }).Done, "a port the test drives keeps its name");
            Assert.IsFalse(editor.Remove(top, a.Id).Done, "and stays");
            StringAssert.Contains("stays one", editor.SetSettings(editor.Project.Settings with { Exercise = null }).Refusal);

            EditResult xor = editor.Add(top, new GatePart { Kind = GateKind.Xor, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(8, 0));
            Assert.IsTrue(xor.Done, "what the student builds is theirs");
            Assert.IsTrue(editor.Remove(top, xor.Id).Done);
        }

        [Test]
        public void ABlockItStocks_KeepsItsInsides()
        {
            var editor = new BoardEditor(Opened("four-at-once"));
            Module fa = editor.Project.Find("FA");
            Assert.IsNotNull(fa, "sanity: the full adder it stocks");
            GatePart gate = fa.Parts.OfType<GatePart>().First();
            EditResult changed = editor.Change("FA", gate with { Kind = gate.Kind == GateKind.Xor ? GateKind.Xnor : GateKind.Xor });
            StringAssert.Contains("FA is a block the exercise gives: its insides are not yours to change.", changed.Refusal);
            StringAssert.Contains("FA is a block the exercise gives", editor.RemoveModule("FA").Refusal);

            EditResult placed = editor.Add(editor.Project.Top, new BlockPart { Module = "FA" }, new Placement(8, 0));
            Assert.IsTrue(placed.Done, "placing one is the student's");
        }

        [Test]
        public void AnAnswerKeptFromBefore_IsMadeTheExercisesAgain()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            var editor = new BoardEditor(Opened("half-adder"));
            string top = editor.Project.Top;
            EditResult xor = editor.Add(top, new GatePart { Kind = GateKind.Xor, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(8, 0));
            Assert.IsTrue(xor.Done);

            // A save from before the exercise's test changed: its own stimulus and none of its settings.
            Project kept = editor.Project with
            {
                Stimulus = new Stimulus(),
                Settings = editor.Project.Settings with { Exercise = null, Library = null },
            };
            var problems = new List<string>();
            Project adopted = exercise.Adopt(kept, problems);
            Assert.IsNotNull(adopted, string.Join("\n", problems));
            Assert.AreEqual("half-adder", adopted.Settings.Exercise);
            Assert.IsNotNull(adopted.Settings.Library, "its library again");
            Assert.AreEqual(Opened("half-adder").Stimulus.Tracks.Count, adopted.Stimulus.Tracks.Count);
            Assert.IsNotEmpty(adopted.Stimulus.Checks, "its own test, checks and all");
            Assert.IsTrue(adopted.TopModule.Parts.Any(p => p.Id == xor.Id), "and the student's own gate kept");
        }

        [Test]
        public void ACircuitThatIsNotAnAnswer_IsNotAdopted()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            var problems = new List<string>();
            Assert.IsNull(exercise.Adopt(SampleBoards.Build("half_adder"), problems), "another top");
            Assert.IsNotEmpty(problems);

            Project opened = Opened("half-adder");
            PortPart a = opened.TopModule.Ports.Single(p => p.Name == "a");
            Module without = opened.TopModule with { Parts = opened.TopModule.Parts.Where(p => p.Id != a.Id).ToList() };
            problems.Clear();
            Assert.IsNull(exercise.Adopt(opened with { Modules = opened.Modules.Select(m => m.Name == opened.Top ? without : m).ToList() }, problems),
                "a port the test drives missing");
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void ACircuitOfTheStudentsOwn_IsLockedByNothing()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            Assert.IsTrue(editor.SetInput("a", 0, 10, LogicVec.FromBinary("1")).Done);
            Assert.IsTrue(editor.SetLibrary(GateLibrary.CourseCells(0)).Done);
        }
    }
}
