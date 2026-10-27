using System.Linq;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Inout pins on blocks of written devices (Part 14g, Ahmad's choice): a block of a written module with an inout has a
    /// pin of both ways, drawn on the right under its outputs; a wire it is on is a net, printed tri; several inout pins and
    /// tri-state gates on one wire are a bus; and a wire is refused with an inout pin and a driver that cannot let go, or an
    /// input of the module, which carries a value in only.
    /// </summary>
    public class InoutPinTests
    {
        /// <summary>A board of two devices whose inout pins are on one wire, and a top output on it.</summary>
        private static (Bench Bench, int U1, int U2, int Y) TwoDevices()
        {
            var b = new Bench("top");
            Bench.Must(b.Editor.AddWrittenModule("dev", SampleBoards.Device));
            int u1 = b.Add(new BlockPart { Module = "dev" }), u2 = b.Add(new BlockPart { Module = "dev" });
            int y = b.Output("y");
            b.Wire(u1, "io", y, "in");
            b.Wire(u2, "io", y, "in");
            return (b, u1, u2, y);
        }

        [Test]
        public void TwoDevicesInoutPins_OnOneWire_AreABus_PrintedTri()
        {
            (Bench b, int u1, int u2, int _) = TwoDevices();
            Assert.IsFalse(b.Editor.Diagnostics.Any(d => d.IsError), string.Join("\n", b.Editor.Diagnostics));

            Netlist nets = Netlist.Of(b.Project, b.Current);
            Net bus = nets.NetOf(new PinRef(u1, "io"));
            CollectionAssert.AreEquivalent(new[] { new PinRef(u1, "io"), new PinRef(u2, "io") }, bus.InOuts);
            Assert.IsTrue(BoardRules.IsBus(bus, b.Current));

            CompiledProject compiled = BoardCompiler.Compile(b.Project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            StringAssert.Contains("output tri logic y", BoardPrinter.Print(compiled).Single(p => p.Path == "top.sv").Text, "the wire is a net, as the inouts join it");
        }

        [Test]
        public void AnInoutPin_OnAWireWithADriverThatCannotLetGo_IsRefused()
        {
            (Bench b, int u1, int _, int _) = TwoDevices();
            int and = b.Gate(GateKind.And);
            EditResult refused = b.Editor.Connect("top", Endpoint.At(and, "y"), Endpoint.At(u1, "io"));
            Assert.IsFalse(refused.Done);
            StringAssert.Contains("blocks' inout pins, each letting go while it does not drive", refused.Refusal);
        }

        [Test]
        public void AnInoutPin_OnAWireWithAnInputOfTheModule_IsRefused_WithTheWayOut()
        {
            var b = new Bench("top");
            Bench.Must(b.Editor.AddWrittenModule("dev", SampleBoards.Device));
            int u1 = b.Add(new BlockPart { Module = "dev" });
            int a = b.Input("a");
            EditResult refused = b.Editor.Connect("top", Endpoint.At(a, "out"), Endpoint.At(u1, "io"));
            Assert.IsFalse(refused.Done);
            StringAssert.Contains("a is an input, on one wire with", refused.Refusal);
            StringAssert.Contains("through a BUFIF1", refused.Refusal);
        }

        /// <summary>An inout pin is drawn on the block's right, under its outputs, and a wire may end on it like any pin.</summary>
        [Test]
        public void AnInoutPin_IsDrawnOnTheRight_UnderTheOutputs()
        {
            (Bench b, int u1, int _, int _) = TwoDevices();
            Part block = b.Current.FindPart(u1);
            PartOutline outline = PartGeometry.Placed(b.Project, block, b.Current.Layout.PlacementOf(u1));
            var right = outline.Pins.Where(p => p.At.X > (outline.Left + outline.Right) / 2).OrderByDescending(p => p.At.Y).Select(p => p.Pin).ToArray();
            CollectionAssert.AreEqual(new[] { "seen", "io" }, right, "the output, then the inout under it");
        }
    }
}
