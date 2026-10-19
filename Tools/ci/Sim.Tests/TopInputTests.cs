using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// A top module's inputs. An input port is a net (IEEE 1800, 23.2.2.3), so one nothing drives reads
    /// z, as Icarus shows it and as the course's ModelSim draws it, a high-impedance line. BitSorter
    /// declares the top's inputs as variables, so a stimulus can write them; until one does, they read
    /// z as well.
    /// </summary>
    public class TopInputTests
    {
        private const string Gate = @"
module gate (input logic a, input logic [1:0] b, output logic y);
    assign y = a & b[0];
endmodule";

        private static Design Elaborate()
        {
            SyntaxTree tree = SyntaxReader.ReadText(Gate, "gate.sv");
            Assert.IsEmpty(tree.Diagnostics);
            Elaboration e = Elaborator.Elaborate(tree, "gate");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            return e.Design;
        }

        private static string Now(Simulator sim, string name) => sim.Value(sim.Design.Find("gate." + name)).Format(Radix.Binary);

        [Test]
        public void AnInputNothingDrives_ReadsZ_AndTheGateItFeeds_ReadsX()
        {
            var sim = new Simulator(Elaborate(), null, null);
            sim.RunTo(10);

            Assert.AreEqual("z", Now(sim, "a"));
            Assert.AreEqual("zz", Now(sim, "b"));
            Assert.AreEqual("x", Now(sim, "y"), "a z into an AND reads as x");
        }

        [Test]
        public void AnInputAStimulusWrites_TakesItsValue_FromTheStart()
        {
            Design design = Elaborate();
            var stimulus = new Stimulus()
                .Set(0, design.Find("gate.a"), Logic4.One)
                .Set(0, design.Find("gate.b"), LogicVec.FromUInt64(1, 2));
            var sim = new Simulator(design, stimulus, null);
            sim.RunTo(10);

            Assert.AreEqual("1", Now(sim, "a"));
            Assert.AreEqual("1", Now(sim, "y"));
            CollectionAssert.AreEqual(new[] { "0 1" }, Probes.Settled(sim.History, design.Find("gate.a")),
                "settled, time 0 holds only the value written then");
        }
    }
}
