using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// What the lessons ask while the workbench stands still cost nothing (Part 16e's performance pass): whether a first-time
    /// hint is due, asked every half second with none up -- which, every hint seen, still listed the design's ports and made
    /// each one's full name, 0.3 to 2 KB an ask -- and the guided tutorial's step, read off the document every frame it runs,
    /// which walked the top's parts and wires through enumerators and listed its ports twice.
    /// </summary>
    public class LessonCostTests
    {
        private static readonly Func<string, bool> EverySeen = _ => true;

        [TestCase("Reference")]
        [TestCase("Pipelined RISC-V")]
        public void EveryHintSeen_AskingWhatIsDue_MakesNothing(string sample)
        {
            var session = new WorkbenchSession(WorkbenchSamples.All.Single(s => s.Name == sample).Build(), () => 0.0);
            session.Finish();
            WorkbenchModel model = session.Run.Model;
            Func<long?> slowest = () => 5L;
            string Ask() => SimulatorHints.Due(session.Project, model.Session.Design, model.Session.History, model.Layout.DesignPath,
                slowest, EverySeen);

            Assert.IsNull(Ask(), "sanity: nothing is due once every hint is seen");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                Ask();
            long made = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, made, $"{made / 20.0:F0} bytes an ask");
        }

        [Test]
        public void TheTutorialsStep_IsReadWithoutMakingAnything()
        {
            Exercise exercise = ExerciseFiles.Load(GuidedTutorial.ExerciseId);
            var editor = new BoardEditor(exercise.ToProject(new List<string>()));
            string top = editor.Project.Top;
            EditResult placed = PartsCatalog.Place(editor, top, PartsCatalog.Find(editor.Project, top, "not"), new Placement(6, 0));
            Assert.IsTrue(placed.Done, placed.Refusal);
            int input = editor.Project.TopModule.Ports.Single(p => p.Name == GuidedTutorial.In).Id;
            Assert.IsTrue(editor.Connect(top, Endpoint.At(input, "out"), Endpoint.At(placed.Id, "a")).Done);
            Project project = editor.Project;
            Assert.AreEqual(GuidedTutorial.Step.WireOut, GuidedTutorial.At(project, null, false), "sanity: every step before it walked");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                GuidedTutorial.At(project, null, false);
            long made = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, made, $"{made / 20.0:F0} bytes a frame");
        }
    }
}
