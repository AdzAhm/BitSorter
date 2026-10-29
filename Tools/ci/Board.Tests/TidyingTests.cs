using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// TIDY on a drawn circuit (Part 15h-4f): every sample and every exercise's reference solution laid out as the drawing of
    /// code is -- nothing standing on anything, no part turned, no wire bent, the circuit printed to the character as it was,
    /// tidy once tidied, and one step back.
    /// </summary>
    public class TidyingTests
    {
        private static IEnumerable<TestCaseData> Circuits()
        {
            foreach (string name in SampleBoards.DrawnNames)
                yield return new TestCaseData("sample " + name).SetName("Tidy_" + name);
            foreach (string id in ExerciseFiles.Ids())
                yield return new TestCaseData("exercise " + id).SetName("Tidy_" + id);
        }

        private static Project Build(string what)
        {
            string[] split = what.Split(' ');
            return split[0] == "sample" ? SampleBoards.Build(split[1]) : ExerciseFiles.Solution(ExerciseFiles.Load(split[1]));
        }

        [TestCaseSource(nameof(Circuits))]
        public void ACircuitTidied_StandsApart_IsTheSameCircuit_AndIsTidyAfter(string what)
        {
            var editor = new BoardEditor(Build(what));
            string printed = Printed(editor.Project);
            int tidied = 0;
            foreach (Module drawn in editor.Project.Modules.Where(m => !m.IsWritten && m.Parts.Count > 0).ToList())
            {
                Project before = editor.Project;
                EditResult done = editor.Tidy(drawn.Name);
                if (!done.Done)
                {
                    Assert.AreEqual("It is tidy already.", done.Refusal, drawn.Name);
                    continue;
                }
                tidied++;
                Module tidy = editor.Project.Find(drawn.Name);
                AssertStandApart(editor.Project, tidy);
                foreach (Placement placed in tidy.Layout.Parts.Values)
                    Assert.IsTrue(placed.QuarterTurns == 0 && !placed.Flipped, $"{drawn.Name}: nothing turned");
                foreach (Route route in tidy.Layout.Routes.Values)
                    Assert.AreEqual(0, route.Bends.Count, $"{drawn.Name}: no wire bent");
                Assert.AreEqual("It is tidy already.", editor.Tidy(drawn.Name).Refusal, $"{drawn.Name}: tidy once tidied");

                Assert.IsTrue(editor.Undo());
                Assert.AreSame(before, editor.Project, $"{drawn.Name}: one step back");
                Assert.IsTrue(editor.Redo());
            }
            Assert.AreEqual(printed, Printed(editor.Project), "layout only: the circuit prints as it did");
            Assert.Greater(editor.Project.Modules.Count(m => !m.IsWritten && m.Parts.Count > 0), 0, $"{what} has a drawn module");
            TestContext.WriteLine($"{what}: {tidied} tidied");
        }

        [Test]
        public void AWrittenModule_IsRefused_ItsDrawingLaysItselfOut()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("m", "module m (input logic a, output logic y);\n    assign y = ~a;\nendmodule\n").Done);
            StringAssert.Contains("lays itself out", editor.Tidy("m").Refusal);
        }

        [Test]
        public void AnEmptyModule_HasNothingToTidy()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.AreEqual("There is nothing to tidy.", editor.Tidy("scratch").Refusal);
        }

        private static string Printed(Project project) =>
            string.Join("\n", BoardPrinter.Print(project).Select(f => f.Path + "\n" + f.Text));

        private static void AssertStandApart(Project project, Module module)
        {
            List<PartOutline> outlines = module.Parts.Select(p => PartGeometry.Placed(project, p, module.Layout.PlacementOf(p.Id))).ToList();
            for (int i = 0; i < outlines.Count; i++)
                for (int j = i + 1; j < outlines.Count; j++)
                    Assert.IsFalse(outlines[i].Left < outlines[j].Right && outlines[j].Left < outlines[i].Right
                        && outlines[i].Bottom < outlines[j].Top && outlines[j].Bottom < outlines[i].Top,
                        $"{module.Name}: {module.Parts[i].Name} and {module.Parts[j].Name} stand apart");
        }
    }
}
