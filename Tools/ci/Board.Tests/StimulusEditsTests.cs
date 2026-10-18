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
    }
}
