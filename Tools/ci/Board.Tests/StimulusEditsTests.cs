using System.Linq;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// An input changed from the waveform (Part 9b-3): a value over a stretch, a bit flipped, each kept
    /// to the fewest steps that say it, refused for a clock, and committed by the editor like any edit.
    /// </summary>
    public class StimulusEditsTests
    {
        private static LogicVec V(string binary) => Literal.Parse(binary.Length + "'b" + binary).Value;

        private static Step S(long time, string binary) => new Step(time, V(binary));

        /// <summary>A top module with a bit a, a 4-bit bus s and a clock clk, and the steps given for a and s.</summary>
        private static Bench Board(Step[] a = null, Step[] s = null)
        {
            var b = new Bench();
            int ia = b.Input("a"), isb = b.Input("s", 4), clk = b.Input("clk"), y = b.Output("y");
            b.Wire(ia, "out", y, "in");

            var tracks = new System.Collections.Generic.List<Track>();
            if (a != null)
                tracks.Add(new Track { Port = "a", Steps = a });
            if (s != null)
                tracks.Add(new Track { Port = "s", Steps = s });

            Bench.Must(b.Editor.SetStimulus(new Stimulus
            {
                Tracks = tracks,
                Clocks = new[] { new ClockTrack { Port = "clk", Period = 10 } },
                End = 40,
            }));
            return b;
        }

        /// <summary>An input's steps as <c>time:value</c>, in binary, x written as x.</summary>
        private static string Steps(Project project, string port) =>
            string.Join(" ", (project.Stimulus.Tracks.FirstOrDefault(t => t.Port == port)?.Steps ?? new Step[0])
                .Select(step => step.Time + ":" + step.Value.Format(Radix.Binary)));

        private static Stimulus Set(Bench b, string port, long from, long until, string binary)
        {
            Assert.IsTrue(StimulusEdits.TrySet(b.Project.Stimulus, port, from, until, V(binary), out Stimulus edited, out string refusal), refusal);
            return edited;
        }

        // -----------------------------------------------------------------
        // A value over a stretch
        // -----------------------------------------------------------------

        [Test]
        public void AValueSetOverAStretch_HoldsThere_AndWhatHeldAfterItGoesOn()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(20, "1") });
            Assert.AreEqual("0:0 5:1 10:0 20:1", Steps(b.Project with { Stimulus = Set(b, "a", 5, 10, "1") }, "a"));
        }

        [Test]
        public void AValueSetToTheEnd_LeavesNoStepAfterIt()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(20, "1"), S(30, "0") });
            Assert.AreEqual("0:0 10:1", Steps(b.Project with { Stimulus = Set(b, "a", 10, long.MaxValue, "1") }, "a"));
        }

        [Test]
        public void AStretchEndingOnAStep_KeepsThatStep_AndCoversTheStepsInside()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(10, "1"), S(20, "0") });
            Assert.AreEqual("0:0 5:1 20:0", Steps(b.Project with { Stimulus = Set(b, "a", 5, 20, "1") }, "a"));
        }

        [Test]
        public void AValueThatChangesNothing_LeavesNoStepBehind()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(10, "1"), S(20, "0") });
            Assert.AreEqual("0:0", Steps(b.Project with { Stimulus = Set(b, "a", 10, 20, "0") }, "a"),
                "0 over the only 1 is 0 throughout: one step says it");
        }

        [Test]
        public void AStretchBeforeTheFirstStep_IsXAgainAfterIt_AsTheTestbenchStartsIt()
        {
            Bench b = Board(a: new[] { S(10, "1") });
            Assert.AreEqual("2:0 5:x 10:1", Steps(b.Project with { Stimulus = Set(b, "a", 2, 5, "0") }, "a"));
        }

        [Test]
        public void AnInputWithNoStepsYet_IsGivenATrack()
        {
            Bench b = Board(a: new[] { S(0, "0") });
            Assert.AreEqual("0:1010 10:xxxx", Steps(b.Project with { Stimulus = Set(b, "s", 0, 10, "1010") }, "s"));
        }

        [Test]
        public void AStretchThatIsEmpty_OrStartsBeforeZero_IsRefused()
        {
            Bench b = Board(a: new[] { S(0, "0") });
            Assert.IsFalse(StimulusEdits.TrySet(b.Project.Stimulus, "a", 10, 10, V("1"), out _, out string empty));
            StringAssert.Contains("empty", empty);
            Assert.IsFalse(StimulusEdits.TrySet(b.Project.Stimulus, "a", -1, 10, V("1"), out _, out string early));
            StringAssert.Contains("before the run starts", early);
            Assert.IsFalse(StimulusEdits.TrySet(null, "a", 0, 10, V("1"), out _, out string none));
            StringAssert.Contains("no testbench", none);
        }

        // -----------------------------------------------------------------
        // A bit flipped
        // -----------------------------------------------------------------

        [Test]
        public void ABitFlipped_FlipsTheStretchTheMomentIsIn_AndMergesWithItsNeighbours()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(10, "1"), S(20, "0") });

            Assert.IsTrue(StimulusEdits.TryInvert(b.Project.Stimulus, "a", 15, out Stimulus flat, out long from, out string refusal), refusal);
            Assert.AreEqual(10, from, "the stretch 15 is in starts at the step at 10");
            Assert.AreEqual("0:0", Steps(b.Project with { Stimulus = flat }, "a"), "the only 1, flipped, leaves 0 throughout");

            Assert.IsTrue(StimulusEdits.TryInvert(b.Project.Stimulus, "a", 3, out Stimulus first, out from, out refusal), refusal);
            Assert.AreEqual(0, from);
            Assert.AreEqual("0:1 20:0", Steps(b.Project with { Stimulus = first }, "a"), "0 to 10 flipped joins the 1 after it");

            Assert.IsTrue(StimulusEdits.TryInvert(b.Project.Stimulus, "a", 25, out Stimulus last, out from, out refusal), refusal);
            Assert.AreEqual(20, from);
            Assert.AreEqual("0:0 10:1", Steps(b.Project with { Stimulus = last }, "a"), "the last stretch flipped runs on to the end");
        }

        [Test]
        public void ABusAStretchOfXAndAClock_HaveNothingToFlipTo()
        {
            Bench b = Board(a: new[] { S(10, "1") }, s: new[] { S(0, "0011") });

            Assert.IsFalse(StimulusEdits.TryInvert(b.Project.Stimulus, "s", 5, out _, out _, out string bus));
            StringAssert.Contains("4 bits", bus);
            Assert.IsFalse(StimulusEdits.TryInvert(b.Project.Stimulus, "a", 5, out _, out _, out string x));
            StringAssert.Contains("a is x there", x);
            Assert.IsFalse(StimulusEdits.TryInvert(b.Project.Stimulus, "clk", 5, out _, out _, out string clock));
            StringAssert.Contains("clk is a clock", clock);
            Assert.IsFalse(StimulusEdits.TrySet(b.Project.Stimulus, "clk", 0, 5, V("1"), out _, out clock), "nor is a clock set");
        }

        [Test]
        public void AStretch_RunsFromTheStepAtOrBeforeAMoment_ToTheNextStep()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(10, "1"), S(20, "0") });
            Assert.AreEqual((10L, 20L), StimulusEdits.StretchAt(b.Project.Stimulus, "a", 10));
            Assert.AreEqual((10L, 20L), StimulusEdits.StretchAt(b.Project.Stimulus, "a", 19));
            Assert.AreEqual((20L, long.MaxValue), StimulusEdits.StretchAt(b.Project.Stimulus, "a", 35));
            Assert.AreEqual((0L, long.MaxValue), StimulusEdits.StretchAt(b.Project.Stimulus, "s", 7), "an input with no steps is one stretch");
            Assert.IsNull(StimulusEdits.ValueAt(b.Project.Stimulus, "s", 7), "and x throughout");
        }

        // -----------------------------------------------------------------
        // Through the editor
        // -----------------------------------------------------------------

        [Test]
        public void TheEditor_CommitsAnInputEdit_AsOneStepToUndo()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(20, "1") });
            Project before = b.Project;

            Bench.Must(b.Editor.SetInput("a", 5, 10, V("1")));
            Assert.AreEqual("0:0 5:1 10:0 20:1", Steps(b.Project, "a"));

            Bench.Must(b.Editor.InvertInput("a", 25, out long from));
            Assert.AreEqual(20, from);
            Assert.AreEqual("0:0 5:1 10:0", Steps(b.Project, "a"));

            Assert.IsTrue(b.Editor.Undo());
            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(before, b.Project, "two edits, two undos, and the document is the one before");
        }

        [Test]
        public void AValueOfTheWrongWidth_IsRefusedByTheRules_AndChangesNothing()
        {
            Bench b = Board(a: new[] { S(0, "0") });
            Project before = b.Project;

            EditResult result = b.Editor.SetInput("s", 0, 10, V("11"));
            Assert.IsFalse(result.Done);
            StringAssert.Contains("s is 4 bits, and is given 2 bits", result.Refusal);
            Assert.AreSame(before, b.Project);

            EditResult clock = b.Editor.InvertInput("clk", 5);
            Assert.IsFalse(clock.Done);
            StringAssert.Contains("clock", clock.Refusal);
        }

        [Test]
        public void AnEditedInput_IsWhatThePrintedTestbenchDrives()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(20, "1") });
            Bench.Must(b.Editor.SetInput("a", 5, 10, V("1")));

            string bench = BoardPrinter.Print(b.Project).Single(p => p.Path == "top_tb.sv").Text;
            StringAssert.Contains("#5 a = 1'b1;", bench);
            StringAssert.Contains("#5 a = 1'b0;", bench, "and back at 10, where the stretch ends");
        }

        // -----------------------------------------------------------------
        // The INPUTS table (Part 15g)
        // -----------------------------------------------------------------

        private static Stimulus Must(bool done, Stimulus edited, string refusal)
        {
            Assert.IsTrue(done, refusal);
            return edited;
        }

        [Test]
        public void ACell_SetsAStep_ACellEmptied_TakesItAway_AndTheStepsStayTheFewest()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(10, "1"), S(20, "0") }, s: new[] { S(0, "0000") });

            Stimulus set = Must(StimulusEdits.TrySetStep(b.Project.Stimulus, "s", 15, V("0101"), out Stimulus e1, out string r1), e1, r1);
            Bench.Must(b.Editor.SetStimulus(set));
            Assert.AreEqual("0:0000 15:0101", Steps(b.Project, "s"));

            Stimulus same = Must(StimulusEdits.TrySetStep(b.Project.Stimulus, "s", 30, V("0101"), out Stimulus e2, out string r2), e2, r2);
            Bench.Must(b.Editor.SetStimulus(same));
            Assert.AreEqual("0:0000 15:0101", Steps(b.Project, "s"), "the value already held there is no step");

            Stimulus emptied = Must(StimulusEdits.TrySetStep(b.Project.Stimulus, "a", 10, null, out Stimulus e3, out string r3), e3, r3);
            Bench.Must(b.Editor.SetStimulus(emptied));
            Assert.AreEqual("0:0", Steps(b.Project, "a"), "10's step gone, and 20's with it, which gave a the 0 it now holds");

            Assert.IsFalse(StimulusEdits.TrySetStep(b.Project.Stimulus, "clk", 5, V("1"), out _, out string clock));
            StringAssert.Contains("clock", clock);
            Assert.IsFalse(StimulusEdits.TrySetStep(b.Project.Stimulus, "a", -1, V("1"), out _, out _), "before the run");
        }

        [Test]
        public void ARow_IsTakenAway_OrMoved_EveryInputsStepAtItsMomentTogether()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(10, "1"), S(30, "0") }, s: new[] { S(0, "0000"), S(10, "0011") });

            Stimulus moved = Must(StimulusEdits.TryMoveRow(b.Project.Stimulus, 10, 35, out Stimulus e1, out string r1), e1, r1);
            Bench.Must(b.Editor.SetStimulus(moved));
            Assert.AreEqual("0:0 35:1", Steps(b.Project, "a"), "moved past 30, in time order, and 30's step -- the 0 it held -- dropped");
            Assert.AreEqual("0:0000 35:0011", Steps(b.Project, "s"));

            Assert.IsFalse(StimulusEdits.TryMoveRow(b.Project.Stimulus, 35, 0, out _, out string onto), "onto 0, where a and s change already");
            StringAssert.Contains("already changes at 0", onto);

            Stimulus removed = Must(StimulusEdits.TryRemoveRow(b.Project.Stimulus, 35, out Stimulus e2, out string r2), e2, r2);
            Bench.Must(b.Editor.SetStimulus(removed));
            Assert.AreEqual("0:0", Steps(b.Project, "a"));
            Assert.AreEqual("0:0000", Steps(b.Project, "s"));
            Assert.IsFalse(StimulusEdits.TryRemoveRow(b.Project.Stimulus, 35, out _, out _), "nothing changes there now");
        }

        [Test]
        public void AllCombinations_CountsThroughEveryValue_TheFirstInputMostSignificant_AndTheRunLastsForThem()
        {
            Bench b = Board(a: new[] { S(0, "1") });
            Stimulus all = Must(StimulusEdits.TryFillCombinations(b.Project.Stimulus, new[] { ("a", 1), ("s", 4) }, 0, 10,
                out Stimulus e, out string r), e, r);
            Bench.Must(b.Editor.SetStimulus(all));

            Assert.AreEqual("0:0 160:1", Steps(b.Project, "a"), "a, the most significant, 0 for sixteen rows and then 1");
            string sSteps = Steps(b.Project, "s");
            StringAssert.StartsWith("0:0000 10:0001 20:0010", sSteps);
            StringAssert.EndsWith("310:1111", sSteps);
            Assert.AreEqual(320, b.Project.Stimulus.End, "the last row held a step");

            Assert.IsFalse(StimulusEdits.TryFillCombinations(b.Project.Stimulus, new[] { ("s", 11) }, 0, 10, out _, out string many));
            StringAssert.Contains("over a thousand rows", many);
            Assert.IsFalse(StimulusEdits.TryFillCombinations(b.Project.Stimulus, new[] { ("clk", 1) }, 0, 10, out _, out _), "a clock");
            Assert.IsFalse(StimulusEdits.TryFillCombinations(b.Project.Stimulus, new (string, int)[0], 0, 10, out _, out string none));
            StringAssert.Contains("Choose", none);
        }

        [Test]
        public void Count_GivesABusEveryValueUp_AStepApart()
        {
            Bench b = Board();
            Stimulus counted = Must(StimulusEdits.TryFillCount(b.Project.Stimulus, "s", 4, 5, 2, out Stimulus e, out string r), e, r);
            Bench.Must(b.Editor.SetStimulus(counted));
            string steps = Steps(b.Project, "s");
            StringAssert.StartsWith("5:0000 7:0001", steps);
            StringAssert.EndsWith("35:1111", steps);
            Assert.AreEqual(40, b.Project.Stimulus.End, "already long enough: 5 and sixteen steps of 2 end at 37");

            Assert.IsTrue(StimulusEdits.TryFillCount(b.Project.Stimulus, "s", 12, 0, 1, out Stimulus wide, out string _));
            Assert.AreEqual(StimulusEdits.MaxCount, wide.Tracks.Single(t => t.Port == "s").Steps.Count, "a wide bus counts a byte's worth");
        }

        [Test]
        public void AnInput_IsMadeAClock_AndBack_AndOnlyAOneBitInputIsOne()
        {
            Bench b = Board(a: new[] { S(0, "0"), S(10, "1") });
            Stimulus clock = Must(StimulusEdits.TryMakeClock(b.Project.Stimulus, "a", 4, out Stimulus e1, out string r1), e1, r1);
            Bench.Must(b.Editor.SetStimulus(clock));
            Assert.IsNull(b.Project.Stimulus.Tracks.FirstOrDefault(t => t.Port == "a"), "its steps given up");
            Assert.AreEqual(4, b.Project.Stimulus.Clocks.Single(c => c.Port == "a").Period);

            Assert.IsFalse(StimulusEdits.TryMakeClock(b.Project.Stimulus, "s", 3, out _, out string odd));
            StringAssert.Contains("even", odd);
            Assert.IsTrue(StimulusEdits.TryMakeClock(b.Project.Stimulus, "s", 4, out Stimulus bus, out string _));
            EditResult wide = b.Editor.SetStimulus(bus);
            Assert.IsFalse(wide.Done, "a 4-bit clock");
            StringAssert.Contains("a clock is one bit", wide.Refusal);

            Stimulus back = Must(StimulusEdits.TryUnmakeClock(b.Project.Stimulus, "a", out Stimulus e2, out string r2), e2, r2);
            Bench.Must(b.Editor.SetStimulus(back));
            Assert.IsFalse(b.Project.Stimulus.Clocks.Any(c => c.Port == "a"));
            Assert.IsFalse(StimulusEdits.TryUnmakeClock(b.Project.Stimulus, "a", out _, out _), "no clock now");
        }

        /// <summary>A step retyped keeps whether it is late, as CONVERT made it, and a stimulus alike is Same (the 15g-4 review).</summary>
        [Test]
        public void AStepRetyped_StaysLate_AndAlikeStimuliAreTheSame()
        {
            Bench b = Board();
            Stimulus late = b.Project.Stimulus with
            {
                Tracks = new[] { new Track { Port = "a", Steps = new[] { new Step(0, V("0")), new Step(10, V("1"), late: true) } } },
            };
            Bench.Must(b.Editor.SetStimulus(late));
            Assert.IsTrue(StimulusEdits.TrySetStep(b.Project.Stimulus, "a", 10, V("x"), out Stimulus e1, out string r1), r1);
            Assert.IsTrue(e1.Tracks.Single(t => t.Port == "a").Steps.Single(s => s.Time == 10).Late, "retyped x, still late");
            Assert.IsTrue(StimulusEdits.TrySetStep(e1, "a", 10, V("1"), out Stimulus retyped, out string r2), r2);
            Assert.IsTrue(retyped.Tracks.Single(t => t.Port == "a").Steps.Single(s => s.Time == 10).Late, "still made after the rest of its moment");

            Assert.IsTrue(StimulusEdits.Same(b.Project.Stimulus, retyped), "the same steps again: Same, though not one object");
            Assert.IsFalse(StimulusEdits.Same(b.Project.Stimulus, e1));
            Assert.IsFalse(StimulusEdits.TrySetStep(b.Project.Stimulus, "a", StimulusEdits.MaxTime + 1, V("1"), out _, out _), "past the last moment");
        }

        [Test]
        public void TheEnd_IsSet_AndNeverBeforeOne()
        {
            Bench b = Board();
            Stimulus ended = Must(StimulusEdits.TrySetEnd(b.Project.Stimulus, 75, out Stimulus e, out string r), e, r);
            Bench.Must(b.Editor.SetStimulus(ended));
            Assert.AreEqual(75, b.Project.Stimulus.End);
            Assert.IsFalse(StimulusEdits.TrySetEnd(b.Project.Stimulus, 0, out _, out string never));
            StringAssert.Contains("1 or later", never);
        }
    }
}
