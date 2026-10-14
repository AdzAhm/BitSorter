using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Every change goes through the editor: what it refuses and why, what it takes with it, and undo
    /// that gives back exactly the document before.
    /// </summary>
    public class BoardEditorTests
    {
        // -----------------------------------------------------------------
        // Placing and naming
        // -----------------------------------------------------------------

        [Test]
        public void APlacedPart_GetsTheNextId_AndANameOfItsKind()
        {
            var b = new Bench();
            int a = b.Input(null), c = b.Input(null), y = b.Output(null);
            int g1 = b.Gate(GateKind.And), g2 = b.Gate(GateKind.Or), r = b.Register();

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, new[] { a, c, y, g1, g2, r });
            CollectionAssert.AreEqual(new[] { "a", "b", "y", "g1", "g2", "r1" }, b.Current.Parts.Select(p => p.Name));
            Assert.AreEqual(new Placement(3, 0), b.Current.Layout.PlacementOf(g1));
        }

        [TestCase("module", "keyword")]
        [TestCase("3x", "start with a letter")]
        [TestCase("", "cannot be empty")]
        public void ANameSystemVerilogWouldRefuse_IsRefused(string name, string said)
        {
            var b = new Bench();
            int g = b.Gate(GateKind.And);

            EditResult result = b.Editor.Change(b.Module, b.Current.FindPart(g) with { Name = name });

            Assert.IsFalse(result.Done);
            StringAssert.Contains(said, result.Refusal);
            Assert.AreEqual("g1", b.Current.FindPart(g).Name, "nothing changed");
        }

        [Test]
        public void TwoPartsOfOneName_AreRefused()
        {
            var b = new Bench();
            b.Gate(GateKind.And);
            int g2 = b.Gate(GateKind.And);

            EditResult result = b.Editor.Change(b.Module, b.Current.FindPart(g2) with { Name = "g1" });

            Assert.IsFalse(result.Done);
            StringAssert.Contains("Two parts are called g1", result.Refusal);
        }

        [Test]
        public void ATriStateGate_TakesItsDataAndItsControl_OneBitEach()
        {
            var b = new Bench();
            int g = b.Gate(GateKind.Bufif1);

            CollectionAssert.AreEqual(new[] { "a", "en", "y" }, b.Current.FindPart(g).Pins(b.Project).Select(p => p.Name),
                "the second input is the control, named as the primitive's is");

            var gate = (GatePart)b.Current.FindPart(g);
            EditResult three = b.Editor.Change(b.Module, gate with { Inputs = 3 });
            Assert.IsFalse(three.Done);
            StringAssert.Contains("bufif1 gate, which has two inputs, the data and the control, not 3", three.Refusal);

            EditResult wide = b.Editor.Change(b.Module, gate with { Width = 4 });
            Assert.IsFalse(wide.Done);
            StringAssert.Contains("bufif1 gate, which is one bit wide", wide.Refusal);
        }

        // -----------------------------------------------------------------
        // Wiring
        // -----------------------------------------------------------------

        [Test]
        public void AWireBetweenDifferentWidths_IsRefused_NamingBoth()
        {
            var b = new Bench();
            int a = b.Input("a", width: 4);
            int g = b.Gate(GateKind.Not);

            EditResult result = b.TryWire(a, "out", g, "a");

            Assert.IsFalse(result.Done);
            StringAssert.Contains("a.out is 4 bits", result.Refusal);
            StringAssert.Contains("g1.a is 1 bit", result.Refusal);
            Assert.IsEmpty(b.Current.Wires);
        }

        [Test]
        public void TwoDriversOnOneWire_AreRefused_NamingBoth()
        {
            var b = new Bench();
            int g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not);

            EditResult result = b.TryWire(g1, "y", g2, "y");

            Assert.IsFalse(result.Done);
            StringAssert.Contains("g1.y and g2.y both drive one wire", result.Refusal);
        }

        [Test]
        public void JoiningWhatIsAlreadyJoined_IsRefused()
        {
            var b = new Bench();
            int a = b.Input("a"), g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not);
            b.Wire(a, "out", g1, "a");
            b.Wire(a, "out", g2, "a");

            EditResult result = b.TryWire(g1, "a", g2, "a");

            Assert.IsFalse(result.Done);
            StringAssert.Contains("already joined", result.Refusal);
        }

        [Test]
        public void AnInputLeftUnconnected_IsAWarning_NotARefusal()
        {
            var b = new Bench();
            int g = b.Gate(GateKind.And);

            BoardDiagnostic warning = b.Editor.Diagnostics.Single(d => d.Part == g && d.Message.Contains("g1.a"));
            Assert.IsFalse(warning.IsError);
            StringAssert.Contains("reads x", warning.Message);
        }

        [Test]
        public void AVectorGateWithDifferentRiseAndFall_IsAWarning_ThatSimulatorsPartOnIt()
        {
            var b = new Bench();
            int one = b.Gate(GateKind.And, rise: 2, fall: 3);
            int four = b.Gate(GateKind.And, rise: 2, fall: 3, width: 4);

            Assert.IsFalse(b.Editor.Diagnostics.Any(d => d.Part == one && d.Message.Contains("simulators part")), "one bit: no question");
            BoardDiagnostic warning = b.Editor.Diagnostics.Single(d => d.Part == four && d.Message.Contains("simulators part"));
            Assert.IsFalse(warning.IsError);
        }

        // -----------------------------------------------------------------
        // Taking away
        // -----------------------------------------------------------------

        [Test]
        public void TakingAPartAway_TakesItsWires_AndTheWiresTappingThem()
        {
            var b = new Bench();
            int g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not), g3 = b.Gate(GateKind.Not), g4 = b.Gate(GateKind.Not);
            int w = b.Wire(g1, "y", g2, "a");
            int tap = Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g3, "a"), Endpoint.OnWire(w)));
            int onTap = Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g4, "a"), Endpoint.OnWire(tap)));

            Bench.Must(b.Editor.Remove(b.Module, g1));

            CollectionAssert.DoesNotContain(b.Current.Wires.Select(x => x.Id), w);
            CollectionAssert.DoesNotContain(b.Current.Wires.Select(x => x.Id), tap);
            CollectionAssert.DoesNotContain(b.Current.Wires.Select(x => x.Id), onTap);
            Assert.IsNull(b.Current.FindPart(g1));
            Assert.IsFalse(b.Current.Layout.Parts.ContainsKey(g1));
        }

        [Test]
        public void FewerInputs_WhileAWireIsOnTheOneGoing_IsRefused()
        {
            var b = new Bench();
            int a = b.Input("a");
            int g = b.Gate(GateKind.And, inputs: 3);
            b.Wire(a, "out", g, "c");

            EditResult result = b.Editor.Change(b.Module, (GatePart)b.Current.FindPart(g) with { Inputs = 2 });

            Assert.IsFalse(result.Done);
            StringAssert.Contains("g1.c, which g1 does not have", result.Refusal);
        }

        // -----------------------------------------------------------------
        // Modules, blocks and renaming
        // -----------------------------------------------------------------

        [Test]
        public void RenamingAPort_RenamesItOnEveryBlock_AndInTheStimulus()
        {
            Bench b = Bench.HalfAdder(module: "half_adder");
            Bench.Must(b.Editor.AddModule("top"));
            b.Module = "top";
            int x = b.Input("x"), s = b.Output("sum");
            int ha = b.Add(new BlockPart { Module = "half_adder" });
            b.Wire(x, "out", ha, "a");
            b.Wire(ha, "s", s, "in");
            Bench.Must(b.Editor.SetTop("half_adder"));
            Bench.Must(b.Editor.SetStimulus(new Stimulus
            {
                Tracks = new[] { new Track { Port = "a", Steps = new[] { new Step(0, LogicVec.FromUInt64(1, 1)) } } },
            }));

            Module half = b.Project.Find("half_adder");
            Bench.Must(b.Editor.Change("half_adder", half.FindPart("a") with { Name = "x_in" }));

            Module top = b.Project.Find("top");
            Assert.IsTrue(top.Wires.Any(w => w.To.Pin.Part == ha && w.To.Pin.Pin == "x_in"), "the block's wire follows the port");
            Assert.AreEqual("x_in", b.Project.Stimulus.Tracks[0].Port);
            Assert.IsFalse(b.Editor.Diagnostics.Any(d => d.IsError), string.Join("\n", b.Editor.Diagnostics));
        }

        [Test]
        public void AModuleThatContainsItself_IsRefused()
        {
            var b = new Bench("outer");
            Bench.Must(b.Editor.AddModule("inner"));
            b.Add(new BlockPart { Module = "inner" });
            b.Module = "inner";

            EditResult result = b.Editor.Add("inner", new BlockPart { Module = "outer" }, new Placement(0, 0));

            Assert.IsFalse(result.Done);
            StringAssert.Contains("a module cannot contain itself", result.Refusal);
        }

        [Test]
        public void RenamingAModule_RenamesItsBlocks_AndTheTop()
        {
            var b = new Bench("outer");
            Bench.Must(b.Editor.AddModule("inner"));
            int u = b.Add(new BlockPart { Module = "inner" });

            Bench.Must(b.Editor.RenameModule("inner", "core"));
            Bench.Must(b.Editor.RenameModule("outer", "chip"));

            Assert.AreEqual("core", ((BlockPart)b.Project.Find("chip").FindPart(u)).Module);
            Assert.AreEqual("chip", b.Project.Top);
        }

        [Test]
        public void AStimulusForAPortThatIsNotThere_IsRefused()
        {
            Bench b = Bench.HalfAdder();

            EditResult result = b.Editor.SetStimulus(new Stimulus { Clocks = new[] { new ClockTrack { Port = "clk" } } });

            Assert.IsFalse(result.Done);
            StringAssert.Contains("The clock clk is not an input of half_adder", result.Refusal);
        }

        // -----------------------------------------------------------------
        // Undo
        // -----------------------------------------------------------------

        [Test]
        public void Undo_GivesBackTheVeryDocumentBefore_AndRedoTheOneAfter()
        {
            Bench b = Bench.HalfAdder();
            Project before = b.Project;

            b.Gate(GateKind.Nor);
            Project after = b.Project;

            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(before, b.Project);
            Assert.IsTrue(b.Editor.Redo());
            Assert.AreSame(after, b.Project);
        }

        [Test]
        public void ADrag_IsOneStep_AndARefusedEdit_IsNone()
        {
            var b = new Bench();
            int g = b.Gate(GateKind.And);
            Project placed = b.Project;

            for (int x = 1; x <= 10; x++)
                Bench.Must(b.Editor.Move(b.Module, g, new Placement(x, 0), continuing: true));
            Assert.IsFalse(b.TryWire(g, "y", g, "y").Done);

            Assert.AreEqual(new Placement(10, 0), b.Current.Layout.PlacementOf(g));
            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(placed, b.Project, "ten moves in a row undo together, and the refused wire left nothing");
        }

        [Test]
        public void HistoryIsCapped()
        {
            var b = new Bench();
            for (int i = 0; i < BoardEditor.HistoryLimit + 20; i++)
                b.Gate(GateKind.Buf);

            int undone = 0;
            while (b.Editor.Undo())
                undone++;

            Assert.AreEqual(BoardEditor.HistoryLimit, undone);
        }
    }
}
