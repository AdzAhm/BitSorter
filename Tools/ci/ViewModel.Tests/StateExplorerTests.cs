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

            StateTransition[] found = machine.Transitions.Where(t => t.Output == "1").ToArray();
            Assert.AreEqual(1, found.Length, "1, 0, 1 found on one arrow alone");
            Assert.AreEqual("1", found[0].Input);
            int sawOneZero = found[0].From;
            Assert.IsTrue(machine.Transitions.Any(t => t.To == sawOneZero && t.Input == "0" && machine.Transitions.Any(u => u.To == t.From && u.Input == "1")),
                "reached on a 0 from the state a 1 reaches");
        }

        [Test]
        public void OneClockBehind_ReadsAsAMooreMachine()
        {
            StateMachine machine = Exercise("one-clock-behind");
            Assert.IsTrue(machine.Moore, "its output depends on its state alone, as its goal says");
            Assert.GreaterOrEqual(machine.States.Count, 2);
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
