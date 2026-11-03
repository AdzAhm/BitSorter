using System;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// What the workbench asks of the view-model every frame allocates nothing once it has been asked once (the zero-GC
    /// pass, Part 16): the workbench's own frame is a Unity one and can only be measured there, but every byte it made
    /// standing still on a processor came from asks like these -- a find through a project's modules was an enumerator
    /// object each time, and the panel makes a hundred finds a frame.
    /// </summary>
    /// <remarks>
    /// Measured as the other allocation guards are, with <see cref="GC.GetAllocatedBytesForCurrentThread"/> round many
    /// asks after a first. dotnet can keep an object that never leaves a method on the stack, where Unity's runtimes never
    /// do: with <c>DOTNET_JitObjectStackAllocation=0</c> set these hold as well, and that is how they were made.
    /// </remarks>
    public class FrameAllocationTests
    {
        /// <summary>What <paramref name="ask"/> allocates on average, asked <paramref name="times"/> times after twice.</summary>
        internal static long BytesEach(Action ask, int times = 200)
        {
            ask();
            ask();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < times; i++)
                ask();
            return (GC.GetAllocatedBytesForCurrentThread() - before) / times;
        }

        /// <summary>A sample opened as the workbench opens it, its run made whole and shown halfway.</summary>
        internal static WorkbenchSession Opened(string sample)
        {
            var session = new WorkbenchSession(WorkbenchSamples.All.Single(s => s.Name == sample).Build(), () => 0.0);
            session.Finish();
            session.Run.Model.Seek(session.Run.Model.End * 0.5);
            return session;
        }

        [Test]
        public void FindingAModulePartOrWire_AllocatesNothing()
        {
            Project project = WorkbenchSamples.All.Single(s => s.Name == "Pipelined RISC-V").Build();
            Module last = project.Modules[project.Modules.Count - 1];
            Module drawn = ReferenceCircuit.Document().TopModule;
            Part part = drawn.Parts[drawn.Parts.Count - 1];
            Wire wire = drawn.Wires[drawn.Wires.Count - 1];

            Assert.AreEqual(0, BytesEach(() => project.Find(last.Name)), "a find through the modules");
            Assert.AreEqual(0, BytesEach(() => project.Find("nothing of the name")), "a find that finds nothing");
            Assert.AreEqual(0, BytesEach(() => drawn.FindPart(part.Id)), "a part by its id");
            Assert.AreEqual(0, BytesEach(() => drawn.FindPart(part.Name)), "a part by its name");
            Assert.AreEqual(0, BytesEach(() => drawn.FindWire(wire.Id)), "a wire by its id");
            Assert.AreSame(last, project.Find(last.Name), "sanity: and each finds what it did");
            Assert.AreSame(part, drawn.FindPart(part.Id));
            Assert.AreSame(wire, drawn.FindWire(wire.Id));
        }

        /// <summary>
        /// What the panel asks of the session every frame over a processor's drawing of code -- each of them several times a
        /// frame, through the circuit's window, PARTS, the switches over it and the code's editor -- allocates nothing.
        /// </summary>
        [TestCase("Pipelined RISC-V")]
        [TestCase("Multicycle RISC-V")]
        [TestCase("Reference")]
        public void TheSessionsFrameAsks_AllocateNothing(string sample)
        {
            WorkbenchSession session = Opened(sample);
            var editor = new CanvasEditor(session);
            Vec2 middle = session.Run.Model.Layout.Size * 0.5f;
            float nudge = 0f;

            Assert.AreEqual(0, BytesEach(() => _ = session.Module), "the module shown");
            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsWritten), "whether it is written");
            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsDrawingOfCode), "whether it is the drawing of code");
            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsGatesOfCode), "whether its gates are open");
            Assert.AreEqual(0, BytesEach(() => _ = session.EditsDrawingOfCode), "whether it takes edits");
            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsController), "whether it is a controller");
            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsCode), "whether its code is shown");
            Assert.AreEqual(0, BytesEach(() => _ = session.Code), "the code's editor's document");
            Assert.AreEqual(0, BytesEach(() => _ = session.CanTidy), "TIDY up or not");
            Assert.AreEqual(0, BytesEach(() => _ = session.DrawnModule), "what the canvas draws");
            Assert.AreEqual(0, BytesEach(() => _ = session.DrawnProject), "and its project");
            Assert.AreEqual(0, BytesEach(() => _ = session.Choices), "PROGRAM's chip");
            Assert.AreEqual(0, BytesEach(() => _ = session.Diagnostics), "the TRANSCRIPT's document lines");
            Assert.AreEqual(0, BytesEach(() => session.Frame(session.Run.Model.Time)), "a frame of a run made whole");
            Assert.AreEqual(0, BytesEach(() => editor.Point(middle, false, 1f, true)), "the pointer standing still on the circuit");
            Assert.AreEqual(0, BytesEach(() => editor.Point(middle + new Vec2(nudge += 0.37f, 0f), false, 1f, true)),
                "the pointer moving on it, a pin asked for each time");
            Assert.AreEqual(0, BytesEach(() => editor.Point(null, false, 1f, true)), "the pointer off it");
        }
    }
}
