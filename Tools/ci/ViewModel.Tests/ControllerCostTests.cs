using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using NUnit.Framework;
using BitSorter.Ci;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A controller's diagram or tables, lit as the run goes, cost nothing a frame (Part 16e's performance pass). The workbench
    /// asks where the controller shown is (<see cref="WorkbenchSession.ControllerNow"/>) and whether it is shown
    /// (<see cref="WorkbenchSession.ShowsController"/>) every frame, playing or paused. The first made the full name of the
    /// state register and of every table's subject again each time, to look each up; a table's row was found by walking each
    /// row's labels through an enumerator, and whether a reading opens as one by walking its tables so -- which Mono and IL2CPP
    /// make on the heap.
    /// </summary>
    public class ControllerCostTests
    {
        /// <summary>A list that can be read by index and refuses to be walked with an enumerator.</summary>
        private sealed class IndexedOnly<T> : IReadOnlyList<T>
        {
            private readonly T[] _items;

            public IndexedOnly(params T[] items) => _items = items;

            public T this[int index] => _items[index];
            public int Count => _items.Length;

            public IEnumerator<T> GetEnumerator() =>
                throw new InvalidOperationException("walked through an enumerator, which Mono and IL2CPP allocate on the heap");

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static WorkbenchSession InsideTheControl(string sample)
        {
            var session = new WorkbenchSession(WorkbenchSamples.All.Single(s => s.Name == sample).Build(), () => 0.0);
            session.Finish();
            int control = session.DrawnModule.Parts.OfType<BlockPart>().Single(b => b.Module.EndsWith("_control", StringComparison.Ordinal)).Id;
            Assert.IsTrue(session.StepInto(control), "sanity: steps into the controller");
            Assert.IsTrue(session.ShowsController, "sanity: shown as the textbook draws it");
            return session;
        }

        [TestCase("Multicycle RISC-V")]   // a state machine, and its tables
        [TestCase("Single-cycle RISC-V")]   // tables alone
        public void AControllerShown_MakesNothingAFrame(string sample)
        {
            WorkbenchSession session = InsideTheControl(sample);
            ControllerMoment first = session.ControllerNow();
            Assert.IsTrue(first.State >= 0 || first.Rows.Any(row => row >= 0), "sanity: lit somewhere");

            int sink = 0;
            long made = Allocations.Least(() =>
            {
                for (int i = 0; i < 100; i++)
                    sink += session.ControllerNow().State + (session.ShowsController ? 1 : 0);
            });

            Assert.AreEqual(0, made, $"{made / 100.0:F0} bytes a frame ({sink})");
        }

        [Test]
        public void WhereTheControllerIs_FollowsTheRun_AsBefore()
        {
            // The same moment asked twice reads the same; another moment is read afresh, not from what was looked up before.
            WorkbenchSession session = InsideTheControl("Multicycle RISC-V");
            var seen = new HashSet<int>();
            WorkbenchModel model = session.Run.Model;
            for (double t = 0; t < Math.Min(model.End, 400.0); t += 5.0)
            {
                model.Seek(t);
                ControllerMoment now = session.ControllerNow(), again = session.ControllerNow();
                Assert.AreEqual(now.State, again.State, $"t = {t}");
                seen.Add(now.State);
            }
            Assert.Greater(seen.Count, 2, "sanity: the run moves through several states");
        }

        [Test]
        public void ATablesRow_IsFoundByIndex()
        {
            var table = new TableReading("op", new IndexedOnly<string>("a", "b"), new IndexedOnly<TableRow>(
                new TableRow("ONE", new IndexedOnly<LogicVec>(LogicVec.FromUInt64(1, 2)), false, new IndexedOnly<TableCell>()),
                new TableRow("TWO, THREE", new IndexedOnly<LogicVec>(LogicVec.FromUInt64(2, 2), LogicVec.FromUInt64(3, 2)), false,
                    new IndexedOnly<TableCell>()),
                new TableRow("default", new IndexedOnly<LogicVec>(), true, new IndexedOnly<TableCell>())));

            Assert.AreEqual(1, table.RowOf(LogicVec.FromUInt64(3, 2)), "the second label of the second row");
            Assert.AreEqual(2, table.RowOf(LogicVec.FromUInt64(0, 2)), "no label holds it: the default");
            Assert.AreEqual(-1, table.RowOf(LogicVec.AllX(2)), "unknown: none");

            var reading = new ControllerReading("decode", null, new IndexedOnly<TableReading>(table), whole: true);
            Assert.IsTrue(WorkbenchSession.OpensAsController(reading), "a table of two columns opens as one");
        }
    }
}
