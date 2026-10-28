using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The STATES tab (Part 15g): a clocked circuit's states and every way between them, found by running the engine from its
    /// start, breadth first. Ahmad asked for a state diagram (2026-10-28).
    /// </summary>
    public class StateExplorerTests
    {
        private static StateMachine Explored(Project project, out string why)
        {
            var session = new WorkbenchSession(project, () => 0);
            session.Finish();
            StateExplorer explorer = StateExplorer.Start(project, session.Timing, () => 0, out why);
            if (explorer == null)
                return null;
            explorer.Finish();
            Assert.IsNull(explorer.Problem, explorer.Problem);
            return explorer.Machine;
        }

        /// <summary>
        /// Too many input or register bits to draw says how many arrows or states that is, exactly: the counts were capped at
        /// 2^30 and 2^62, so forty input bits read as 2^30 arrows (the 15h browser test found LOGIC's like it).
        /// </summary>
        [Test]
        public void TooManyBits_SayHowManyArrowsOrStatesTheyAre()
        {
            Assert.IsNull(Explored(Written("module many (input logic clk, input logic [39:0] a, output logic q);\n" +
                                           "    always_ff @(posedge clk) q <= ^a;\n" +
                                           "endmodule\n"), out string why), "sanity: too many inputs to draw");
            StringAssert.Contains("40 input bits besides the clock are 1,099,511,627,776 arrows", why);

            Assert.IsNull(Explored(Written("module long (input logic clk, input logic d, output logic [63:0] q);\n" +
                                           "    always_ff @(posedge clk) q <= {q[62:0], d};\n" +
                                           "endmodule\n"), out why), "sanity: too many register bits to draw");
            StringAssert.Contains("64 register bits are up to 2^64 states", why);
        }

        private static StateMachine Exercise(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            Project solved = ExerciseFiles.Solution(exercise) with { Stimulus = exercise.Test(), Settings = exercise.ToProject(new System.Collections.Generic.List<string>()).Settings };
            StateMachine machine = Explored(solved, out string why);
            Assert.IsNotNull(machine, why);
            return machine;
        }

        [Test]
        public void OneClockLate_IsTwoStates_AMooreMachineThatHoldsTheLastInput()
        {
            StateMachine machine = Exercise("one-clock-late");
            Assert.AreEqual(2, machine.States.Count, string.Join(", ", machine.States));
            Assert.AreEqual("0", machine.States[0], "a register here starts at 0");
            Assert.IsTrue(machine.Moore, "out is the state");
            CollectionAssert.AreEqual(new[] { "in" }, machine.InputBits.ToArray(), "the clock is not an input of the diagram");
            foreach (StateTransition t in machine.Transitions)
                Assert.AreEqual(t.Input, machine.States[t.To], "whatever in was, the next state holds it");
        }

        /// <summary>
        /// Spot the pattern, the 1-0-1 detector: three states reach from the start -- nothing yet, a 1, a 1 then a 0 -- and its
        /// output is 1 on one arrow alone, from the last of them on a 1. Its output reads the input as well: a Mealy machine.
        /// </summary>
        [Test]
        public void SpotThePattern_IsTheThreeStateDetectorItsGoalDescribes_AMealyMachine()
        {
            StateMachine machine = Exercise("spot-the-pattern");
            Assert.AreEqual(3, machine.States.Count, string.Join(", ", machine.States));
            Assert.IsFalse(machine.Moore, "out = r2 & in reads the input");
            Assert.AreEqual(6, machine.Transitions.Count, "two arrows out of each of three states");

            // Every arrow, by what each state has seen: none of it yet, a 1, then a 1 and a 0. Found on the last 1, and kept on:
            // the 1 that ends one pattern begins the next.
            int Next(int from, string input) => machine.Transitions.Single(t => t.From == from && t.Input == input).To;
            string Out(int from, string input) => machine.Transitions.Single(t => t.From == from && t.Input == input).Output;
            const int nothing = 0;
            int one = Next(nothing, "1"), oneZero = Next(one, "0");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, new[] { nothing, one, oneZero }, "three states apart");
            Assert.AreEqual(nothing, Next(nothing, "0"));
            Assert.AreEqual(one, Next(one, "1"));
            Assert.AreEqual(nothing, Next(oneZero, "0"));
            Assert.AreEqual(one, Next(oneZero, "1"));
            Assert.AreEqual("1", Out(oneZero, "1"), "1, 0, 1 found");
            Assert.AreEqual(1, machine.Transitions.Count(t => t.Output == "1"), "and on that arrow alone");
        }

        [Test]
        public void OneClockBehind_ReadsAsAMooreMachine()
        {
            StateMachine machine = Exercise("one-clock-behind");
            Assert.IsTrue(machine.Moore, "its output depends on its state alone, as its goal says");
            Assert.AreEqual(3, machine.States.Count, string.Join(", ", machine.States));
            for (int s = 0; s < machine.States.Count; s++)
                Assert.IsNotNull(machine.OutputIn(s), "every state reached has a way out, and its output");
        }

        /// <summary>
        /// A written register on ports with ranges of their own: each bit is named as it is declared, a[7] and a[6] and y[0]
        /// before y[1], and its value is that bit's -- the 15g-5 review found LOGIC naming every bus from 0 and swapping a [0:1]'s two.
        /// </summary>
        [Test]
        public void PortsWithRangesOfTheirOwn_NameEachBitAsDeclared()
        {
            ProjectImport import = ProjectImporter.Import(new[]
            {
                new BitSorter.Board.SourceFile("ranged.sv",
                    "module ranged(input logic clk, input logic [7:6] a, output logic [0:1] y);\n" +
                    "    always_ff @(posedge clk) y <= {a[7], a[6]};\n" +
                    "endmodule\n"),
            });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            StateMachine machine = Explored(import.Project, out string why);
            Assert.IsNotNull(machine, why);

            CollectionAssert.AreEqual(new[] { "a[7]", "a[6]" }, machine.InputBits.ToArray());
            CollectionAssert.AreEqual(new[] { "y[0]", "y[1]" }, machine.OutputBits.ToArray(), "y[0] is a [0:1]'s high bit");
            Assert.AreEqual(5, machine.States.Count, string.Join(", ", machine.States));
            Assert.AreEqual("xx", machine.States[0], "a written register with no reset starts unknown, and every input takes it somewhere known");
            Assert.IsTrue(machine.Moore);
            foreach (StateTransition t in machine.Transitions)
            {
                Assert.AreEqual(t.Input, machine.States[t.To], "y[0] takes a[7], y[1] a[6]: the next state reads as the input");
                Assert.AreEqual(machine.States[t.From], t.Output, "and y is the state");
            }
        }

        private static Project Written(string source)
        {
            ProjectImport import = ProjectImporter.Import(new[] { new BitSorter.Board.SourceFile("machine.sv", source) });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            return import.Project;
        }

        /// <summary>
        /// A register that holds its input, reset by a button: rst_btn is active high whatever its last letter, and reset_btn
        /// written active low is found so by exploring the other way round once the first comes out held in reset. The 15g-6
        /// review found rst_btn taken as active low, and one state that ignored its input.
        /// </summary>
        [TestCase("rst_btn", "if (rst_btn)", TestName = "AResetsWayRound_rst_btn_IsActiveHighByItsName")]
        [TestCase("rst_n", "if (!rst_n)", TestName = "AResetsWayRound_rst_n_IsActiveLowByItsName")]
        [TestCase("reset_btn", "if (!reset_btn)", TestName = "AResetsWayRound_reset_btn_WrittenActiveLow_IsFoundByTryingBoth")]
        public void AResetsWayRound_IsFound(string reset, string condition)
        {
            StateMachine machine = Explored(Written(
                "module held(input logic clk, input logic " + reset + ", input logic in, output logic q);\n" +
                "    always_ff @(posedge clk) " + condition + " q <= 1'b0; else q <= in;\n" +
                "endmodule\n"), out string why);
            Assert.IsNotNull(machine, why);
            Assert.AreEqual(2, machine.States.Count, string.Join(", ", machine.States));
            Assert.AreEqual("0", machine.States[0], "from its reset");
            CollectionAssert.AreEqual(new[] { "in" }, machine.InputBits.ToArray(), "the reset is not an arrow's input");
            foreach (StateTransition t in machine.Transitions)
                Assert.AreEqual(t.Input, machine.States[t.To], "out of reset, q takes in");
        }

        /// <summary>
        /// A shift register with no reset starts unknown, and is run on from every state with an x in it: x0 and x1 are where
        /// it is after one clock, and after two it is somewhere known. The 15g-6 review found a state with an x never run on.
        /// </summary>
        [Test]
        public void AStateWithAnXInIt_IsRunOnFrom()
        {
            StateMachine machine = Explored(Written(
                "module shift(input logic clk, input logic in, output logic [1:0] q);\n" +
                "    always_ff @(posedge clk) q <= {q[0], in};\n" +
                "endmodule\n"), out string why);
            Assert.IsNotNull(machine, why);
            CollectionAssert.AreEquivalent(new[] { "xx", "x0", "x1", "00", "01", "10", "11" }, machine.States.ToArray());
            Assert.AreEqual("xx", machine.States[0]);
            Assert.AreEqual(14, machine.Transitions.Count, "two arrows out of every state, x ones included");
        }

        /// <summary>Thirty-two states reachable, sixteen drawn, and the machine says so.</summary>
        [Test]
        public void PastSixteenStates_TheRestAreNotExplored_AndTheMachineSaysSo()
        {
            StateMachine machine = Explored(Written(
                "module count(input logic clk, input logic rst, input logic en, output logic [4:0] q);\n" +
                "    always_ff @(posedge clk) if (rst) q <= 5'd0; else if (en) q <= q + 5'd1;\n" +
                "endmodule\n"), out string why);
            Assert.IsNotNull(machine, why);
            Assert.IsTrue(machine.Cut);
            Assert.AreEqual(StateExplorer.MostStates, machine.States.Count);
        }

        /// <summary>
        /// An input called clk is the clock even where the stimulus clocks another input: the registers' edges are written against
        /// it. The 15g-6 review found the stimulus's clocked en taken for the clock, and clk an arrow's input.
        /// </summary>
        [Test]
        public void AnInputCalledClk_IsTheClock_WhateverElseTheStimulusClocks()
        {
            Project project = Written(
                "module enabled(input logic clk, input logic en, input logic d, output logic q);\n" +
                "    always_ff @(posedge clk) if (en) q <= d;\n" +
                "endmodule\n");
            project = project with { Stimulus = new BitSorter.Board.Stimulus { Clocks = new[] { new ClockTrack { Port = "en", Period = 10 } }, End = 100 } };
            StateMachine machine = Explored(project, out string why);
            Assert.IsNotNull(machine, why);
            CollectionAssert.AreEqual(new[] { "en", "d" }, machine.InputBits.ToArray(), "clk is no arrow's input");
            Assert.IsTrue(machine.States.Contains("0") && machine.States.Contains("1"), string.Join(", ", machine.States));
        }

        [Test]
        public void ACircuitWithoutRegisters_HasNoStates_AndSaysWhy()
        {
            Assert.IsNull(Explored(WorkbenchSamples.FullAdder(), out string why));
            StringAssert.Contains("registers", why);
        }

        [Test]
        public void TheCounterSample_CountsThroughItsStates()
        {
            StateMachine machine = Explored(WorkbenchSamples.Counter(), out string why);
            Assert.IsNotNull(machine, why);
            Assert.AreEqual(8, machine.States.Count, "a 3-bit count goes through all eight: " + string.Join(", ", machine.States));
            Assert.AreEqual("000", machine.States[0], "from its reset");
            Assert.AreEqual("001", machine.States[1], "and a state reads as a bus does, high bit first");
            CollectionAssert.AreEqual(new[] { "count[2]", "count[1]", "count[0]" }.Select(b => b.Substring(b.IndexOf('['))).ToArray(),
                machine.StateBits.Select(b => b.Substring(b.IndexOf('['))).ToArray(), "the register's bits, high first");
            Assert.IsTrue(machine.Moore, "count is its state");
            Assert.AreEqual(0, machine.InputBits.Count, "the clock and the reset are not an arrow's inputs");
            for (int s = 0; s < machine.States.Count; s++)
                Assert.AreEqual(machine.States[(s + 1) % 8], machine.States[machine.Transitions.Single(t => t.From == s).To], "each count one on");
        }
    }
}
