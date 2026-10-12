using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// The oracle harness works: Icarus can be found, answers without hanging, and simulates a design
    /// the way the plan says BitSorter's own engine must.
    /// </summary>
    /// <remarks>
    /// The second test is also the first fact the oracle pins down, before BitSorter has an engine to
    /// compare: a gate with different rise and fall delays reads x until its first output, takes the
    /// fall delay to settle at 0 and the rise delay to go to 1. Docs/architecture-5.md's delay table
    /// promises exactly that, and the engine's own tests will be held to it.
    /// </remarks>
    public class IcarusSmokeTests
    {
        [Test]
        public void Icarus_AnswersWithItsVersion_WithoutWaitingForInput()
        {
            Icarus icarus = Icarus.Require();

            ProcessResult result = icarus.Version();

            Assert.IsFalse(result.TimedOut,
                "iverilog -V hung: it waits on standard input when given no source files, and the harness " +
                "must close it");
            StringAssert.Contains("Icarus Verilog version", result.StandardOutput + result.StandardError);
        }

        [Test]
        public void AGateWithRiseAndFallDelays_ReadsXThenFallsThenRises()
        {
            Icarus icarus = Icarus.Require();

            using IcarusRun run = icarus.Simulate(new[] { Corpus.File("smoke/xor_rise_fall.sv") });

            Assert.IsTrue(run.Succeeded, run.Explain());

            // The testbench prints "<time> <value>" whenever the output changes; vvp adds a line of its
            // own when $finish is called, which is not one of them.
            string[] changes = Regex.Split(run.Simulate.StandardOutput, "\r?\n")
                .Where(line => Regex.IsMatch(line, @"^\d+ [01xz]$"))
                .ToArray();

            Assert.That(changes, Is.EqualTo(new[]
            {
                "0 x",   // nothing has reached the output yet
                "3 0",   // inputs 0 and 0 at time 0: a fall, which takes 3
                "12 1",  // a rises at 10: the output rises, which takes 2
                "23 0",  // a falls at 20: the output falls, which takes 3
            }));
        }
    }
}
