using System.Linq;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>Nets worked out from wires, and the names they print under.</summary>
    public class NetlistTests
    {
        [Test]
        public void AHalfAdder_HasANetForEachPort_NamedAfterIt()
        {
            Bench b = Bench.HalfAdder();
            Netlist nets = b.Nets;

            CollectionAssert.AreEquivalent(new[] { "a", "b", "s", "c" }, nets.Nets.Select(n => n.Name));
            Assert.IsTrue(nets.Nets.All(n => n.Naming == NetNaming.Port));
            Assert.AreEqual(3, nets.Named("a").Pins.Count, "the port and both gates' a");
            Assert.AreEqual(1, nets.Named("s").Drivers.Count);
        }

        [Test]
        public void ANetWithNoPort_IsNamedAfterItsDriver_AndKeepsTheNameAsTheBoardChanges()
        {
            var b = new Bench();
            int a = b.Input("a"), y = b.Output("y");
            int g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not);
            b.Wire(a, "out", g1, "a");
            b.Wire(g1, "y", g2, "a");
            b.Wire(g2, "y", y, "in");

            Assert.AreEqual("g1_y", b.NetName(g1, "y"));

            int g3 = b.Gate(GateKind.Not, name: "g0");
            b.Wire(g1, "y", g3, "a");
            Bench.Must(b.Editor.Remove(b.Module, g3));
            Assert.AreEqual("g1_y", b.NetName(g1, "y"), "parts added and taken away elsewhere do not rename it");
        }

        [Test]
        public void APinWithNothingOnIt_IsANetOfItsOwn_NamedAfterThePin()
        {
            var b = new Bench();
            int g = b.Gate(GateKind.And);

            Assert.AreEqual("g1_a", b.NetName(g, "a"));
            Assert.AreEqual("g1_b", b.NetName(g, "b"));
            Assert.AreEqual(0, b.Nets.NetOf(new PinRef(g, "a")).Drivers.Count);
        }

        [Test]
        public void ANameGiven_IsTheNetsName_AndAMadeNameNeverTakesIt()
        {
            var b = new Bench();
            int g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not);
            int w = b.Wire(g1, "y", g2, "a");

            // The player calls g1's output "g2_y" -- which is what g2's own output would have been called.
            Bench.Must(b.Editor.NameWire(b.Module, w, "g2_y"));

            Assert.AreEqual("g2_y", b.NetName(g1, "y"));
            Assert.AreEqual(NetNaming.Given, b.Nets.NetOf(new PinRef(g1, "y")).Naming);
            Assert.AreEqual("g2_y_2", b.NetName(g2, "y"), "the made name steps aside");
        }

        [Test]
        public void ATap_JoinsTheWireItIsOn()
        {
            var b = new Bench();
            int g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not), g3 = b.Gate(GateKind.Not);
            int w = b.Wire(g1, "y", g2, "a");
            Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g3, "a"), Endpoint.OnWire(w)));

            Netlist nets = b.Nets;
            Net net = nets.NetOf(new PinRef(g3, "a"));
            Assert.AreSame(nets.NetOf(new PinRef(g1, "y")), net);
            Assert.AreEqual(2, net.Wires.Count);
        }

        [Test]
        public void AnInputWiredStraightToAnOutput_IsTheInputsNet_AndTheOutputComesSecond()
        {
            var b = new Bench();
            int a = b.Input("a"), y = b.Output("y");
            b.Wire(a, "out", y, "in");

            Net net = b.Nets.NetOf(new PinRef(y, "in"));
            Assert.AreEqual("a", net.Name);
            CollectionAssert.AreEqual(new[] { "a", "y" }, net.Ports.Select(p => p.Name), "y is fed from a by an assign");
        }
    }
}
