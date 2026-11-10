using System;
using System.Collections;
using System.Collections.Generic;
using BitSorter.Board;
using NUnit.Framework;
using BitSorter.Ci;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The net under the pointer (<see cref="SchematicLayout.NetAt"/>), which the workbench asks every frame the pointer is on
    /// the circuit, playing or not (Part 16e's performance pass). It walked each net's branches with <c>foreach</c> over the
    /// interface -- an enumerator on the heap a net, 40 bytes, 2.3 KB a frame over the pipelined processor -- and walked every
    /// segment of every wire again for a pointer standing still. It walks by index, and a pointer that has not moved is
    /// answered from the last ask, as the pin under it already was (the 15g-3 review).
    /// </summary>
    public class HoverCostTests
    {
        /// <summary>A list read by index, counting the reads, that refuses to be walked with an enumerator.</summary>
        private sealed class Counted<T> : IReadOnlyList<T>
        {
            private readonly T[] _items;

            public Counted(params T[] items) => _items = items;

            public int Reads { get; set; }

            public T this[int index]
            {
                get
                {
                    Reads++;
                    return _items[index];
                }
            }

            public int Count => _items.Length;

            public IEnumerator<T> GetEnumerator() =>
                throw new InvalidOperationException("walked through an enumerator, which Mono and IL2CPP allocate on the heap");

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>Two nets: one along y = 10 from x 0 to 100, and one down x = 50 from y 40 to 100, in two branches.</summary>
        private static (SchematicLayout Layout, Counted<StageRoute> Across, Counted<StageRoute> Down) TwoNets() =>
            TwoNets(r => new Counted<StageRoute>(r));

        /// <inheritdoc cref="TwoNets()"/>
        /// <remarks>Each net's branches as <paramref name="list"/> makes them: plain arrays, for what it finds alone.</remarks>
        private static (SchematicLayout Layout, TList Across, TList Down) TwoNets<TList>(Func<StageRoute[], TList> list)
            where TList : IReadOnlyList<StageRoute>
        {
            TList across = list(new[] { new StageRoute(new Vec2(0f, 10f), new Vec2(100f, 10f)) });
            TList down = list(new[]
            {
                new StageRoute(new Vec2(50f, 40f), new Vec2(50f, 70f)),
                new StageRoute(new Vec2(50f, 70f), new Vec2(50f, 100f)),
            });
            var nets = new[]
            {
                new NetLayout("a", "top.a", Array.Empty<string>(), 1, Array.Empty<Vec2>(), across, "a"),
                new NetLayout("b", "top.b", Array.Empty<string>(), 1, Array.Empty<Vec2>(), down, "b"),
            };
            var layout = new SchematicLayout(new Vec2(100f, 100f), nets, Array.Empty<PartLayout>(), Array.Empty<PortLayout>(),
                Array.Empty<WaveRow>());
            return (layout, across, down);
        }

        [Test]
        public void TheNetUnderThePointer_IsTheNearestWithinReach()
        {
            SchematicLayout layout = TwoNets(routes => routes).Layout;

            Assert.AreEqual(0, layout.NetAt(new Vec2(30f, 14f), 8f), "on the first, 4 off");
            Assert.AreEqual(1, layout.NetAt(new Vec2(53f, 90f), 8f), "on the second's second branch");
            Assert.AreEqual(-1, layout.NetAt(new Vec2(30f, 30f), 8f), "near neither");
            Assert.AreEqual(1, layout.NetAt(new Vec2(50f, 36f), 30f), "within reach of both: the nearer, 4 off and not 26");
        }

        [Test]
        public void TheNetUnderThePointer_IsFoundWithoutAnEnumerator()
        {
            SchematicLayout layout = TwoNets().Layout;

            Assert.DoesNotThrow(() => layout.NetAt(new Vec2(30f, 14f), 8f));
        }

        [Test]
        public void APointerStandingStill_ReadsNoBranchAgain()
        {
            (SchematicLayout layout, Counted<StageRoute> across, Counted<StageRoute> down) = TwoNets();
            Assert.AreEqual(0, layout.NetAt(new Vec2(30f, 14f), 8f), "sanity: the first ask finds it");
            across.Reads = down.Reads = 0;

            Assert.AreEqual(0, layout.NetAt(new Vec2(30f, 14f), 8f));
            Assert.AreEqual(0, across.Reads + down.Reads, "the same point and reach: answered from the last ask");

            Assert.AreEqual(1, layout.NetAt(new Vec2(53f, 90f), 8f), "moved: asked again");
            Assert.Greater(across.Reads + down.Reads, 0);

            across.Reads = down.Reads = 0;
            Assert.AreEqual(-1, layout.NetAt(new Vec2(53f, 90f), 2f), "the same point, zoomed out of reach: asked again");
            Assert.Greater(across.Reads + down.Reads, 0);
        }

        [Test]
        public void AskingOverAProcessor_MakesNothing()
        {
            var session = new WorkbenchSession(WorkbenchProcessors.Pipelined(), () => 0.0);
            SchematicLayout layout = session.Run.Model.Layout;
            Assert.Greater(layout.Nets.Count, 20, "sanity: a drawing of many nets");

            // A pointer moving across it, a new point every ask: the walk itself, never the last ask's answer.
            for (int i = 0; i < 200; i++)
                layout.NetAt(new Vec2(i, i), SchematicStyle.HoverReach);
            long made = Allocations.Least(() =>
            {
                for (int i = 0; i < 200; i++)
                    layout.NetAt(new Vec2(layout.Size.x * i / 200f, layout.Size.y * 0.5f), SchematicStyle.HoverReach);
            });

            Assert.AreEqual(0, made, $"{made / 200.0:F0} bytes an ask over {layout.Nets.Count} nets");
        }
    }
}
