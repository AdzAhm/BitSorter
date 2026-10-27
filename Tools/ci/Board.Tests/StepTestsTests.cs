using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Step tests (Part 14c): STEP TEST's stimulus, every input bit stepped in turn from all 0, and the measuring run's, every
    /// bit stepped both ways from every value of the rest -- each step left longer than the slowest path to settle.
    /// </summary>
    public class StepTestsTests
    {
        /// <summary>What a track drives at <paramref name="time"/>: its last step at or before it.</summary>
        private static ulong ValueAt(Track track, long time)
        {
            LogicVec value = default;
            foreach (Step step in track.Steps)
                if (step.Time <= time)
                    value = step.Value;
            Assert.IsTrue(value.TryGetUInt64(out ulong v), "a known value");
            return v;
        }

        [Test]
        public void TheSettleTime_IsTheSlowestPath_AndHalfAgain_AndAtLeastTwo()
        {
            Assert.AreEqual(2, StepTests.SettleFor(0));
            Assert.AreEqual(6, StepTests.SettleFor(4));
            Assert.AreEqual(14, StepTests.SettleFor(9));
        }

        [Test]
        public void STEPTEST_StepsEachInputInTurn_FromAllZero_RiseThenFall()
        {
            Project project = SampleBoards.Build("full_adder");
            StepPlan plan = StepTests.Readable(project, 6);

            CollectionAssert.AreEqual(new[] { "a", "b", "cin" }, plan.Inputs.Select(i => i.Name));
            Assert.AreEqual(6, plan.Windows.Count, "each input up and down");
            Track a = plan.Stimulus.Tracks.Single(t => t.Port == "a"), cin = plan.Stimulus.Tracks.Single(t => t.Port == "cin");
            CollectionAssert.AreEqual(new[] { (0L, 0UL), (6L, 1UL), (12L, 0UL) }, a.Steps.Select(s => (s.Time, ValueAt(a, s.Time))));
            CollectionAssert.AreEqual(new[] { (0L, 0UL), (30L, 1UL), (36L, 0UL) }, cin.Steps.Select(s => (s.Time, ValueAt(cin, s.Time))));
            Assert.AreEqual(42, plan.Stimulus.End, "and the last fall left to settle");
        }

        /// <summary>
        /// Three inputs: each stepped up and down from each of the four values of the other two, 24 steps. Played back, the
        /// inputs stand at each step's word just before it, and only the one stepped has moved at it.
        /// </summary>
        [Test]
        public void TheMeasuringRun_StepsEveryInputBothWays_FromEveryValueOfTheRest()
        {
            Project project = SampleBoards.Build("full_adder");
            StepPlan plan = StepTests.Exhaustive(project, 6);

            Assert.AreEqual(3 * 4 * 2, plan.Windows.Count);
            var seen = new HashSet<(int, bool, ulong)>();
            foreach (StepWindow w in plan.Windows)
            {
                ulong before = 0, at = 0;
                for (int k = 0; k < plan.Inputs.Count; k++)
                {
                    Track track = plan.Stimulus.Tracks.Single(t => t.Port == plan.Inputs[k].Port);
                    before |= ValueAt(track, w.At - 1) << k;
                    at |= ValueAt(track, w.At) << k;
                }

                Assert.AreEqual(w.Word, before, "the word is how the inputs stand before the step");
                Assert.AreEqual(w.Word ^ (1UL << w.Input), at, "and only the bit stepped moves");
                Assert.AreEqual(w.Rising, (w.Word >> w.Input & 1) == 0);
                for (long t = w.At + 1; t < w.At + plan.Settle; t++)
                    Assert.AreEqual(at, Enumerable.Range(0, plan.Inputs.Count).Aggregate(0UL, (word, k) =>
                        word | ValueAt(plan.Stimulus.Tracks.Single(tr => tr.Port == plan.Inputs[k].Port), t) << k), "nothing else moves while it settles");
                Assert.IsTrue(seen.Add((w.Input, w.Rising, w.Word & ~(1UL << w.Input))), "each step once");
            }
        }

        [Test]
        public void ABus_IsSteppedABitAtATime()
        {
            Project project = SampleBoards.Build("half_adders");
            IReadOnlyList<InputBit> bits = StepTests.InputsOf(project);

            CollectionAssert.AreEqual(new[] { "a[0]", "a[1]", "a[2]", "a[3]", "b[0]", "b[1]", "b[2]", "b[3]" }, bits.Select(b => b.Name));
            StepPlan plan = StepTests.Readable(project, 4);
            Track a = plan.Stimulus.Tracks.Single(t => t.Port == "a");
            CollectionAssert.AreEqual(new ulong[] { 0, 1, 0, 2, 0, 4, 0, 8, 0 }, a.Steps.Select(s => ValueAt(a, s.Time)), "a's bits one by one, up and down");
        }

        [Test]
        public void MoreThanTenInputBits_AreTooManyToStepFromEveryValueOfTheRest()
        {
            var editor = new BoardEditor(Project.New("wide"));
            SampleBoards.Must(editor.Add("wide", new PortPart { Name = "a", Width = 11 }, new Placement(0, 0)));

            Assert.Throws<ArgumentException>(() => StepTests.Exhaustive(editor.Project, 4));
            Assert.AreEqual(11 * 2, StepTests.Readable(editor.Project, 4).Windows.Count, "STEP TEST steps them all the same");
        }
    }
}
