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

        /// <summary>
        /// A gate on a wire whose only other pin is an inout drives something: the device reads the wire as well as driving
        /// it (the 14g-4 review). The gate is placed first, so it is the net's first driver, the one the warning names.
        /// </summary>
        [Test]
        public void AGate_OnAWireWithOnlyAnInoutPin_DrivesSomething_ForTheDeviceReadsIt()
        {
            var b = new Bench("top");
            Bench.Must(b.Editor.AddWrittenModule("dev", SampleBoards.Device));
            int g = b.Gate(GateKind.Bufif1);
            int u1 = b.Add(new BlockPart { Module = "dev" });
            b.Wire(g, "y", u1, "io");

            Assert.AreEqual(g, Netlist.Of(b.Project, b.Current).NetOf(new PinRef(u1, "io")).Drivers[0].Part, "sanity: the gate is named first");
            Assert.IsFalse(b.Editor.Diagnostics.Any(d => d.Message.Contains("drives nothing")), string.Join("\n", b.Editor.Diagnostics));
        }

        /// <summary>
        /// A wire one inout pin is on is printed tri though it is no bus (the 14g-4 review): the elaborator joins an inout to
        /// a whole net only, and a logic variable there would be refused. A wire inside, read by a gate.
        /// </summary>
        [Test]
        public void AWireWithOneInoutPin_IsATriNet_ThoughNoBus()
        {
            var b = new Bench("top");
            Bench.Must(b.Editor.AddWrittenModule("dev", SampleBoards.Device));
            int u1 = b.Add(new BlockPart { Module = "dev" });
            int not = b.Gate(GateKind.Not);
            int y = b.Output("y");
            b.Wire(u1, "io", not, "a");
            b.Wire(not, "y", y, "in");

            Net io = Netlist.Of(b.Project, b.Current).NetOf(new PinRef(u1, "io"));
            Assert.IsFalse(BoardRules.IsBus(io, b.Current), "one driver: no bus");
            Assert.IsTrue(BoardRules.IsTri(io, b.Current));

            CompiledProject compiled = BoardCompiler.Compile(b.Project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            string top = BoardPrinter.Print(compiled).Single(p => p.Path == "top.sv").Text;
            StringAssert.Contains("tri logic " + io.Name + ";", top);
            StringAssert.Contains("output logic y", top, "the gate's output, no inout on it, stays a variable");
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
        }

        /// <summary>
        /// An output port one inout pin is on, four bits wide, is a tri port, and the testbench's end of it a net (the 14g-4
        /// review); a device's inout left open is left open.
        /// </summary>
        [Test]
        public void AnOutputWithOneInoutPin_IsATriPort_AndTheTestbenchsEndANet()
        {
            const string wide =
                "module wide (\n" +
                "    input logic en,\n" +
                "    input logic [3:0] d,\n" +
                "    inout wire [3:0] io\n" +
                ");\n" +
                "    assign io = en ? d : 4'bz;\n" +
                "endmodule\n";
            var b = new Bench("top");
            Bench.Must(b.Editor.AddWrittenModule("wide", wide));
            int en = b.Input("en"), d = b.Input("d", 4), y = b.Output("y", 4);
            int u1 = b.Add(new BlockPart { Module = "wide" }), u2 = b.Add(new BlockPart { Module = "wide" });
            b.Wire(en, "out", u1, "en");
            b.Wire(d, "out", u1, "d");
            b.Wire(u1, "io", y, "in");
            b.Wire(en, "out", u2, "en");
            b.Wire(d, "out", u2, "d");
            Assert.IsTrue(b.Editor.SetStimulus(new Stimulus { End = 10 }).Done, "a testbench to drive it");

            CompiledProject compiled = BoardCompiler.Compile(b.Project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            string top = BoardPrinter.Print(compiled).Single(p => p.Path == "top.sv").Text;
            StringAssert.Contains("output tri logic [3:0] y", top);
            StringAssert.Contains(".io()", top, "u2's inout, left open");
            StringAssert.Contains("wire logic [3:0] y;", BoardPrinter.Print(compiled).Single(p => p.Path == compiled.Testbench + ".sv").Text,
                "the testbench's end of it a net");
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
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
