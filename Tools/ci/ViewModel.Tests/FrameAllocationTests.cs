using System;
using System.Linq;
using BitSorter.Sim;
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
        /// <summary>
        /// What <paramref name="ask"/> allocates on average, asked <paramref name="times"/> times after twice: never inlined, so an
        /// optimising JIT cannot move the caller's own allocations -- the 0 an assertion boxes -- into the count.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
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

        /// <summary>
        /// Stepped into a copy of an array of blocks -- <c>ha[0]</c> -- what the panel asks every frame allocates nothing, as it
        /// does over a block alone (the 16d sweep): the module shown was found again on every ask, the step's name and its
        /// copy cut out of it as two new strings each time, and the panel asks about a hundred times a frame.
        /// </summary>
        [Test]
        public void SteppedIntoACopyOfAnArray_TheSessionsFrameAsks_AllocateNothing()
        {
            WorkbenchSession session = Opened("Four half adders");
            BlockPart array = session.DrawnModule.Parts.OfType<BlockPart>().Single(b => b.Copies > 1);
            Assert.IsTrue(session.StepInto(array.Id));
            Assert.AreEqual(array.Name + "[0]", session.Path.Single(), "sanity: stepped into its first copy");
            Assert.AreEqual(array.Module, session.Module);

            Assert.AreEqual(0, BytesEach(() => _ = session.Module), "the module shown");
            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsWritten), "whether it is written");
            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsDrawingOfCode), "whether it is the drawing of code");
            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsCode), "whether its code is shown");
            Assert.AreEqual(0, BytesEach(() => _ = session.Code), "the code's editor's document");
            Assert.AreEqual(0, BytesEach(() => _ = session.CanTidy), "TIDY up or not");
            Assert.AreEqual(0, BytesEach(() => _ = session.DrawnModule), "what the canvas draws");
        }

        /// <summary>
        /// Playing a drawing of code, every wire's value read for a new moment each frame -- the wires no signal holds worked
        /// out from the netlist (<see cref="RtlEvaluator"/>) -- allocates nothing: at the processor's top, and stepped into its
        /// execute stage and its controller.
        /// </summary>
        [TestCase("Pipelined RISC-V", "", 50)]
        [TestCase("Pipelined RISC-V", "pcpu/pcpu_execute", 30)]
        [TestCase("Multicycle RISC-V", "mcpu/mcpu_control", 100)]
        public void EveryWiresValue_AtANewMomentEachFrame_AllocatesNothing(string sample, string into, int nets)
        {
            WorkbenchSession session = Opened(sample);
            foreach (string module in into.Split('/', StringSplitOptions.RemoveEmptyEntries))
                if (session.Module != module)
                    Assert.IsTrue(session.StepInto(session.DrawnModule.Parts.OfType<BlockPart>().First(b => b.Module == module).Id), "into " + module);
            Assert.AreEqual(into.Length == 0 ? session.Project.Top : into.Substring(into.LastIndexOf('/') + 1), session.Module, "sanity: stepped in");
            if (session.ShowsController)
                session.DrawController(false);
            session.Finish();
            WorkbenchModel model = session.Run.Model;
            Assert.IsNotNull(model.Layout.Drawn, "sanity: a drawing of code, its wires worked out");
            Assert.Greater(model.Layout.Nets.Count, nets, "sanity: a drawing of some size");

            double time = model.End * 0.25, step = model.End / 2000.0;
            long bytes = BytesEach(() =>
            {
                model.Seek(time += step);
                for (int i = 0; i < model.Layout.Nets.Count; i++)
                    _ = model.NetValue(i);
            });
            Assert.AreEqual(0, bytes, $"a moment's wires on {model.Layout.Nets.Count} nets");
        }

        /// <summary>
        /// Playing a drawing of code with GATES open, at the workbench's default speed -- 3.2 units a second, a nineteenth of a
        /// unit a frame -- every wire's value read each frame, those inside the gates included, allocates nothing; and a frame
        /// whose moment is in the same step as the last one's works nothing out again, since every value is the step's: it costs
        /// what reading the wires does, under half what working them out at a new step does. The
        /// multicycle controller's netlist and its gates were worked out at every frame, the gates' wires into new arrays: 89,624
        /// bytes and 284 microseconds a frame here (the 16d sweep, waves-8).
        /// </summary>
        [TestCase("Multicycle RISC-V", "mcpu/mcpu_control")]
        public void EveryWiresValue_WithGatesOpen_Playing_AllocatesNothing_AndIsWorkedOutOnceAStep(string sample, string into)
        {
            WorkbenchSession session = Opened(sample);
            foreach (string module in into.Split('/', StringSplitOptions.RemoveEmptyEntries))
                if (session.Module != module)
                    Assert.IsTrue(session.StepInto(session.DrawnModule.Parts.OfType<BlockPart>().First(b => b.Module == module).Id), "into " + module);
            if (session.ShowsController)
                session.DrawController(false);
            Assert.IsNull(session.DrawGates(true), "GATES opens");
            session.Finish();
            WorkbenchModel model = session.Run.Model;
            Assert.Greater(session.GatesOpened, 50, "sanity: its gates open");
            Assert.IsNotNull(model.Layout.Drawn, "sanity: a drawing of code, its wires worked out");

            void Frame(double at)
            {
                model.Seek(at);
                for (int i = 0; i < model.Layout.Nets.Count; i++)
                    _ = model.NetValue(i);
            }

            double time = model.End * 0.25, frame = 3.2 / 60;
            Assert.AreEqual(0, BytesEach(() => Frame(time += frame), 600), $"a frame's wires on {model.Layout.Nets.Count} nets, playing");

            // Each moment in a new step, so worked out each time; and each in the one step, so worked out once.
            double across = Math.Floor(model.End * 0.5), within = Math.Floor(model.End * 0.75) + 0.1;
            Frame(across);
            Frame(within);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 400; i++)
                Frame(across += 1.0);
            double eachStep = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            for (int i = 0; i < 400; i++)
                Frame(within += 0.002);
            double oneStep = watch.Elapsed.TotalMilliseconds;
            Assert.Less(oneStep, eachStep / 2, $"400 frames in one step took {oneStep:F2} ms, 400 in a step each {eachStep:F2} ms");
        }

        /// <summary>The net under the pointer, which the hover chip asks every frame the pointer is on the circuit, allocates nothing.</summary>
        [TestCase("Pipelined RISC-V")]
        [TestCase("Reference")]
        public void TheNetUnderThePointer_AllocatesNothing(string sample)
        {
            SchematicLayout layout = Opened(sample).Run.Model.Layout;
            Vec2 middle = layout.Size * 0.5f;
            NetLayout wired = layout.Nets.First(n => n.Branches.Count > 0 && n.Branches[0].PointCount > 1);
            Vec2 onAWire = wired.Branches[0].Point(0);

            Assert.AreEqual(0, BytesEach(() => layout.NetAt(middle, 3f)), "asked anywhere");
            Assert.AreEqual(0, BytesEach(() => layout.NetAt(onAWire, 3f)), "asked on a wire");
            Assert.GreaterOrEqual(layout.NetAt(onAWire, 3f), 0, "sanity: a wire's own end finds a net");
        }

        /// <summary>
        /// A controller shown as the textbook draws it (Part 15h-6e) is lit every frame it is up -- its state, the arrow it came
        /// by and each table's row (<see cref="WorkbenchSession.ControllerNow"/>) -- and asked whether it is shown: none of it
        /// allocates, the moment moving or not. A state machine's, and a decoder's tables.
        /// </summary>
        [TestCase("Multicycle RISC-V", "mcpu/mcpu_control")]
        [TestCase("Pipelined RISC-V", "pcpu/pcpu_decode/pcpu_control")]
        public void AControllerShown_LitEachFrame_AllocatesNothing(string sample, string into)
        {
            WorkbenchSession session = Opened(sample);
            foreach (string module in into.Split('/'))
                if (session.Module != module)
                    Assert.IsTrue(session.StepInto(session.DrawnModule.Parts.OfType<BlockPart>().First(b => b.Module == module).Id), "into " + module);
            session.Finish();
            Assert.IsTrue(session.ShowsController, "sanity: shown as a controller");
            WorkbenchModel model = session.Run.Model;
            double time = model.End * 0.25, step = model.End / 2000.0;
            ControllerMoment lit = default;

            Assert.AreEqual(0, BytesEach(() => _ = session.ShowsController), "whether it is shown");
            Assert.AreEqual(0, BytesEach(() => lit = session.ControllerNow()), "lit, the moment standing still");
            Assert.AreEqual(0, BytesEach(() =>
            {
                model.Seek(time += step);
                lit = session.ControllerNow();
            }), "lit, the moment moving");
            Assert.IsTrue(lit.State >= 0 || lit.Rows.Any(r => r >= 0), "sanity: and it lit something");
        }

        /// <summary>
        /// With something selected, what the panel asks every frame -- the one thing selected, for the inspector -- and what the
        /// canvas asks each time the pointer moves -- the selected block's handles, the selected wire's corners -- allocate nothing.
        /// </summary>
        [Test]
        public void WithSomethingSelected_TheFrameAsks_AllocateNothing()
        {
            WorkbenchSession session = Opened("Four half adders");
            var editor = new CanvasEditor(session);
            Module module = session.DrawnModule;
            Part block = module.Parts.First(p => p is BlockPart);
            Wire wire = module.Wires[0];
            Vec2 middle = session.Run.Model.Layout.Size * 0.5f;
            float nudge = 0f;

            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: block.Id), extend: false, toggle: false);
            Assert.AreEqual(block.Id, editor.Single.Part, "sanity: the block alone selected");
            Assert.GreaterOrEqual(editor.HandledPart, 0, "sanity: and it shows its handles");
            Assert.AreEqual(0, BytesEach(() => _ = editor.Single), "the one thing selected, a block");
            Assert.AreEqual(0, BytesEach(() => _ = editor.HandledPart), "the block that shows its handles");
            Assert.AreEqual(0, BytesEach(() => editor.Point(middle + new Vec2(nudge += 0.37f, 0f), false, 1f, true)), "the pointer moving, a block selected");

            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Wire, wire: wire.Id), extend: false, toggle: false);
            Assert.AreEqual(wire.Id, editor.Single.Wire, "sanity: the wire alone selected");
            Assert.AreEqual(0, BytesEach(() => _ = editor.Single), "the one thing selected, a wire");
            Assert.AreEqual(0, BytesEach(() => editor.Point(middle + new Vec2(nudge += 0.37f, 0f), false, 1f, true)), "the pointer moving, a wire selected");

            editor.SelectAll();
            Assert.AreEqual(0, BytesEach(() =>
            {
                foreach (int part in editor.Selection.Parts)
                    _ = part;
                foreach (int each in editor.Selection.Wires)
                    _ = each;
            }), "a loop over what is selected");
        }
    }
}
