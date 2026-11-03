using System;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The one thing selected (<see cref="CanvasEditor.Single"/>), which the inspector asks every frame, is read without making
    /// anything (Part 16e's performance pass): it walked the selection through its interface, and the set's enumerator was
    /// boxed on the heap, 40 bytes a frame for as long as one part or one wire stayed selected.
    /// </summary>
    public class SelectionCostTests
    {
        private static readonly CanvasTarget PartThree = new CanvasTarget(CanvasTargetKind.Part, part: 3);
        private static readonly CanvasTarget WireSeven = new CanvasTarget(CanvasTargetKind.Wire, wire: 7);

        [Test]
        public void TheOneThingSelected_IsAPartOrAWireAlone()
        {
            var selection = new CanvasSelection();
            Assert.IsFalse(selection.TryOnePart(out _), "none");

            selection.Pick(PartThree, extend: false, toggle: false);
            Assert.IsTrue(selection.TryOnePart(out int part));
            Assert.AreEqual(3, part);
            Assert.IsFalse(selection.TryOneWire(out _));

            selection.Pick(WireSeven, extend: true, toggle: false);
            Assert.IsTrue(selection.TryOnePart(out part), "one part, a wire beside it");
            Assert.IsTrue(selection.TryOneWire(out int wire));
            Assert.AreEqual(7, wire);

            selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: 4), extend: true, toggle: false);
            Assert.IsFalse(selection.TryOnePart(out _), "two parts");
        }

        /// <summary>The reference circuit, its run made, an editor on it, and its first part and first wire.</summary>
        private static (CanvasEditor Editor, int Part, int Wire) Reference()
        {
            var session = new WorkbenchSession(WorkbenchSamples.All[0].Build(), () => 0.0);
            session.Finish();
            Module top = session.Project.Find(session.Module);
            return (new CanvasEditor(session), top.Parts[0].Id, top.Wires[0].Id);
        }

        [Test]
        public void TheInspectorsOneThing_IsReadWithoutMakingAnything()
        {
            (CanvasEditor editor, int part, int _) = Reference();
            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: part), extend: false, toggle: false);
            Assert.AreEqual(new CanvasTarget(CanvasTargetKind.Part, part: part), editor.Single, "sanity: the part alone");

            int sink = 0;
            for (int i = 0; i < 100; i++)
                sink += (int)editor.Single.Kind + editor.HandledPart;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                sink += (int)editor.Single.Kind + editor.HandledPart;
            long made = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, made, $"{made / 100.0:F0} bytes a frame ({sink})");
        }

        [Test]
        public void OneWireAlone_IsTheOneThing_AndAPartBesideItIsNot()
        {
            (CanvasEditor editor, int part, int wire) = Reference();

            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Wire, wire: wire), extend: false, toggle: false);
            Assert.AreEqual(new CanvasTarget(CanvasTargetKind.Wire, wire: wire), editor.Single);

            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: part), extend: true, toggle: false);
            Assert.AreEqual(CanvasTargetKind.Nothing, editor.Single.Kind, "a part and a wire: two things");
        }
    }
}
