using System.Collections.Generic;
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

        /// <summary>
        /// A count no part can have -- inputs or bits below none, or a million -- is refused in the document's own words, as nine
        /// inputs are, and makes no pin for each of it (the 16d sweep: -1 threw out of the gate's pins before the rule could say
        /// so, and a splitter of 100,000,000 bits made that many pins first).
        /// </summary>
        [Test]
        public void ACountNoPartCanHave_IsRefusedInWords_WithoutAPinMadeForEachOfIt()
        {
            var b = new Bench();
            int g = b.Gate(GateKind.And), mux = b.Add(new MuxPart()), split = b.Add(new SplitterPart()), join = b.Add(new JoinerPart());
            Project before = b.Project;

            void Refused(Part changed, string said)
            {
                EditResult result = default;
                long made = System.GC.GetAllocatedBytesForCurrentThread();
                Assert.DoesNotThrow(() => result = b.Editor.Change(b.Module, changed), changed.ToString());
                made = System.GC.GetAllocatedBytesForCurrentThread() - made;

                Assert.IsFalse(result.Done, changed.ToString());
                StringAssert.Contains(said, result.Refusal, changed.ToString());
                Assert.Less(made, 4_000_000, $"{changed}: no pin made for each of the count");
            }

            foreach (int n in new[] { -1, -2, 1_000_000 })
            {
                Refused((GatePart)b.Current.FindPart(g) with { Inputs = n }, "takes 2 to 8");
                Refused((MuxPart)b.Current.FindPart(mux) with { Inputs = n }, "has 2, 4 or 8");
                Refused((SplitterPart)b.Current.FindPart(split) with { Width = n }, "takes 2 to 64");
                Refused((JoinerPart)b.Current.FindPart(join) with { Width = n }, "takes 2 to 64");
            }

            Assert.AreSame(before, b.Project, "nothing changed");
        }

        // -----------------------------------------------------------------
        // Wiring
        // -----------------------------------------------------------------

        [Test]
        public void AWireBetweenDifferentWidths_IsRefused_NamingBoth_AndWhatTakesABusApart()
        {
            var b = new Bench();
            int a = b.Input("a", width: 4);
            int g = b.Gate(GateKind.Not);

            EditResult result = b.TryWire(a, "out", g, "a");

            Assert.IsFalse(result.Done);
            StringAssert.Contains("a.out is 4 bits", result.Refusal);
            StringAssert.Contains("g1.a is 1 bit", result.Refusal);
            StringAssert.Contains("Take a bus apart with a splitter", result.Refusal, "and says what would join them (Part 10b)");
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
            StringAssert.Contains("as a bus, only tri-state gates", result.Refusal, "and says what a bus is driven by (Part 14f)");
        }

        /// <summary>
        /// Two tri-state gates on one wire are a bus (Part 14f), allowed and printed as a tri net; a third driver that is no
        /// tri-state gate makes it two drivers again, refused.
        /// </summary>
        [Test]
        public void TriStateGatesOnOneWire_AreABus_AndAnyOtherDriverIsRefused()
        {
            var b = new Bench();
            int g1 = b.Gate(GateKind.Bufif1), g2 = b.Gate(GateKind.Notif0), not = b.Gate(GateKind.Not);

            EditResult bus = b.TryWire(g1, "y", g2, "y");
            Assert.IsTrue(bus.Done, bus.Refusal);
            Assert.IsFalse(b.Editor.Diagnostics.Any(d => d.IsError), "a bus is no fault");

            EditResult third = b.TryWire(not, "y", g1, "y");
            Assert.IsFalse(third.Done);
            StringAssert.Contains("both drive one wire", third.Refusal);
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

        /// <summary>A gate whose output goes nowhere is said, as vlog says a signal set and never read (Part 14e); never refused.</summary>
        [Test]
        public void AGateThatDrivesNothing_IsAWarning_NotARefusal()
        {
            var b = new Bench();
            int g = b.Gate(GateKind.And);

            BoardDiagnostic warning = b.Editor.Diagnostics.Single(d => d.Part == g && d.Message.Contains("drives nothing"));
            Assert.IsFalse(warning.IsError);
            Assert.AreEqual("g1.y drives nothing, so what it works out is never used.", warning.Message);
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

        /// <summary>
        /// A module nothing uses can be taken out, its code with it, in one step undo puts back (the 14g report: deleting
        /// code1's block left code1 in CODE and PARTS, with nothing in the workbench to take it out).
        /// </summary>
        [Test]
        public void AModuleNothingUses_IsTakenOut_InOneStepUndoPutsBack()
        {
            var b = new Bench("top");
            Bench.Must(b.Editor.AddWrittenModule("code1", WrittenModules.Template("code1")));
            int u = b.Add(new BlockPart { Module = "code1" });
            Bench.Must(b.Editor.Remove("top", u));
            Assert.IsNotNull(b.Project.Find("code1"), "sanity: deleting its block leaves the module, which is the player's code");

            Assert.IsNull(b.Editor.WhyNotRemoveModule("code1"));
            Bench.Must(b.Editor.RemoveModule("code1"));
            Assert.IsNull(b.Project.Find("code1"));

            Assert.IsTrue(b.Editor.Undo());
            Assert.AreEqual(WrittenModules.Template("code1"), b.Project.Find("code1").Source, "undo puts the code back as it was");
        }

        /// <summary>A module in use is refused, saying where it is used, so the player knows what to delete first; so is the top.</summary>
        [Test]
        public void AModuleInUse_OrTheTop_IsNotTakenOut_AndSaysWhere()
        {
            var b = new Bench("top");
            Bench.Must(b.Editor.AddWrittenModule("code1", WrittenModules.Template("code1")));
            int u = b.Add(new BlockPart { Module = "code1" });
            string block = b.Current.FindPart(u).Name;

            EditResult placed = b.Editor.RemoveModule("code1");
            Assert.IsFalse(placed.Done);
            StringAssert.Contains($"top has a block of it, {block}", placed.Refusal);
            Assert.AreEqual(placed.Refusal, b.Editor.WhyNotRemoveModule("code1"), "asked before and refused after alike");

            Bench.Must(b.Editor.Remove("top", u));
            Bench.Must(b.Editor.AddWrittenModule("code2",
                "module code2 (\n    input  logic a,\n    output logic y\n);\n    code1 inner (.a(a), .y(y));\nendmodule\n"));
            EditResult written = b.Editor.RemoveModule("code1");
            Assert.IsFalse(written.Done);
            StringAssert.Contains("code2's code makes a code1", written.Refusal);

            StringAssert.Contains("is the top module", b.Editor.RemoveModule("top").Refusal);
            Assert.IsNotNull(b.Project.Find("code1"));
        }

        [Test]
        public void AStimulusForAPortThatIsNotThere_IsRefused()
        {
            Bench b = Bench.HalfAdder();

            EditResult result = b.Editor.SetStimulus(new Stimulus { Clocks = new[] { new ClockTrack { Port = "clk" } } });

            Assert.IsFalse(result.Done);
            StringAssert.Contains("The clock clk is not an input of half_adder", result.Refusal);
        }

        /// <summary>
        /// An input of the top made wider or narrower takes its steps with it, each value widened with 0s or cut to its low bits,
        /// in the same step -- as a rename takes its track's name (the 16d sweep: the steps kept their width, the rules said the
        /// input was given the wrong one, and no input placed from the strip could ever be made a bus).
        /// </summary>
        [Test]
        public void AnInputMadeWider_OrNarrower_TakesItsStepsWithIt_InOneStep()
        {
            Bench b = Bench.HalfAdder();
            var a = (PortPart)b.Current.Parts.First(p => p.Name == "a");
            Bench.Must(b.Editor.SetStimulus(new Stimulus
            {
                Tracks = new[]
                {
                    new Track { Port = "a", Start = LogicVec.FromUInt64(0, 1), Steps = new[] { new Step(0, LogicVec.FromUInt64(1, 1)), new Step(10, LogicVec.FromUInt64(0, 1)) } },
                    new Track { Port = "b", Steps = new[] { new Step(0, LogicVec.FromUInt64(1, 1)) } },
                },
            }));
            Bench.Must(b.Editor.RemoveParts(b.Module, b.Current.Parts.Where(p => p is GatePart).Select(p => p.Id).ToArray()));
            Project before = b.Project;

            EditResult wider = b.Editor.Change(b.Module, a with { Width = 4 });
            Assert.IsTrue(wider.Done, wider.Refusal);
            Track track = b.Project.Stimulus.Tracks.Single(t => t.Port == "a");
            CollectionAssert.AreEqual(new[] { "0001", "0000" }, track.Steps.Select(s => s.Value.Format(Radix.Binary)), "each value widened with 0s");
            CollectionAssert.AreEqual(new[] { 0L, 10L }, track.Steps.Select(s => s.Time), "at the times they were");
            Assert.AreEqual("0000", track.Start.Value.Format(Radix.Binary), "and what it holds from the start");
            Assert.AreEqual(1, b.Project.Stimulus.Tracks.Single(t => t.Port == "b").Steps[0].Value.Width, "another input's steps as they were");

            Bench.Must(b.Editor.SetStimulus(b.Project.Stimulus with
            {
                Tracks = new[] { track with { Steps = new[] { new Step(0, LogicVec.FromUInt64(0b1101, 4)) } }, b.Project.Stimulus.Tracks[1] },
            }));
            EditResult narrower = b.Editor.Change(b.Module, (PortPart)b.Current.FindPart(a.Id) with { Width = 2 });
            Assert.IsTrue(narrower.Done, narrower.Refusal);
            Assert.AreEqual("01", b.Project.Stimulus.Tracks.Single(t => t.Port == "a").Steps[0].Value.Format(Radix.Binary), "cut to its low bits");

            Assert.IsTrue(b.Editor.Undo());
            Assert.IsTrue(b.Editor.Undo());
            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(before, b.Project, "each width with its steps one step back");

            EditResult none = b.Editor.Change(b.Module, a with { Width = 0 });
            Assert.IsFalse(none.Done);
            StringAssert.Contains("1 to 1024", none.Refusal, "a width no input can have is the width's refusal, its steps untouched");
        }

        // -----------------------------------------------------------------
        // Undo
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // Several edits as one (Part 10)
        // -----------------------------------------------------------------

        [Test]
        public void ASelectionMoved_IsOneStep_AndADragOfItIsOneStepToo()
        {
            var b = Bench.HalfAdder();
            Project start = b.Project;
            int x = b.Current.FindPart("g1").Id, n = b.Current.FindPart("g2").Id;

            for (int step = 1; step <= 3; step++)
                Bench.Must(b.Editor.MoveParts(b.Module, new Dictionary<int, Placement>
                {
                    [x] = new Placement(10 + step, 2),
                    [n] = new Placement(new Point(10 + step, -2), 1, true),
                }, continuing: true));

            Assert.AreEqual(new Placement(new Point(13, -2), 1, true), b.Current.Layout.PlacementOf(n), "turned and mirrored as it moved");
            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(start, b.Project, "three moves of the selection undo as one");

            Assert.IsFalse(b.Editor.MoveParts(b.Module, new Dictionary<int, Placement> { [99] = new Placement(0, 0) }).Done);
            Assert.AreSame(start, b.Project, "a part that is not there refuses the whole move");
        }

        [Test]
        public void ASelectionTakenAway_TakesItsWiresAndTheirTaps_InOneStep()
        {
            var b = Bench.HalfAdder();
            Project start = b.Project;
            int x = b.Current.FindPart("g1").Id, s = b.Current.FindPart("s").Id;
            int tapped = b.Current.Wires.First(w => w.Touches(b.Current.FindPart("g2").Id)).Id;
            int tap = Bench.Must(b.Editor.Connect(b.Module, Endpoint.OnWire(tapped), Endpoint.At(b.Output("t"), "in")));
            Project withTap = b.Project;

            Bench.Must(b.Editor.RemoveParts(b.Module, new[] { x, s }, new[] { tapped }));

            Assert.IsNull(b.Current.FindPart(x));
            Assert.IsNull(b.Current.FindPart(s));
            Assert.IsNull(b.Current.FindWire(tapped));
            Assert.IsNull(b.Current.FindWire(tap), "a wire tapping one taken goes with it");
            Assert.IsFalse(b.Current.Wires.Any(w => w.Touches(x) || w.Touches(s)), "every wire on a part taken goes with it");
            Assert.IsFalse(b.Current.Layout.Parts.ContainsKey(x));

            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(withTap, b.Project, "one step");
            Assert.IsFalse(b.Editor.RemoveParts(b.Module, new int[0]).Done, "nothing to take away");
            Assert.AreNotSame(start, withTap);
        }

        [Test]
        public void AnInputTakenAway_TakesWhatTheStimulusGaveIt()
        {
            var b = new Bench();
            int a = b.Input("a"), clk = b.Input("clk"), keep = b.Input("k");
            Bench.Must(b.Editor.SetStimulus(new Stimulus
            {
                Tracks = new[] { new Track { Port = "a", Steps = new[] { new Step(0, LogicVec.FromUInt64(1, 1)) } }, new Track { Port = "k" } },
                Clocks = new[] { new ClockTrack { Port = "clk", Period = 4 } },
            }));

            Bench.Must(b.Editor.Remove(b.Module, a));
            CollectionAssert.AreEqual(new[] { "k" }, b.Project.Stimulus.Tracks.Select(t => t.Port), "a's track goes with it, and was no refusal");

            Bench.Must(b.Editor.RemoveParts(b.Module, new[] { clk }));
            Assert.IsEmpty(b.Project.Stimulus.Clocks, "and a clock's with the clock");
            Assert.IsNotNull(b.Current.FindPart(keep));
        }

        [Test]
        public void AGroup_IsOneStep_AndARefusalInsideItTakesBackEveryEditItMade()
        {
            var b = new Bench();
            Project start = b.Project;
            int changes = 0;
            b.Editor.Changed += () => changes++;

            EditResult made = b.Editor.Group(() =>
            {
                int clk = Bench.Must(b.Editor.Add(b.Module, new PortPart { Name = "clk" }, new Placement(0, 0)));
                Bench.Must(b.Editor.SetStimulus(new Stimulus { Clocks = new[] { new ClockTrack { Port = "clk", Period = 4 } } }));
                return EditResult.Ok(clk);
            });

            Assert.IsTrue(made.Done);
            Assert.AreEqual("clk", b.Current.FindPart(made.Id).Name, "the group's answer is its body's");
            Assert.AreEqual(1, changes, "said once, at the end");
            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(start, b.Project, "an input and its clock undo as one");

            Assert.IsTrue(b.Editor.Redo());
            Project before = b.Project;
            EditResult refused = b.Editor.Group(() =>
            {
                Bench.Must(b.Editor.Add(b.Module, new GatePart { Kind = GateKind.And }, new Placement(3, 0)));
                return b.Editor.Add(b.Module, new GatePart { Kind = GateKind.And, Name = "g1" }, new Placement(5, 0));
            });

            Assert.IsFalse(refused.Done);
            StringAssert.Contains("g1", refused.Refusal);
            Assert.AreSame(before, b.Project, "the gate placed before the refusal is taken back with it");
            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(start, b.Project, "and the refused group left no step behind");
        }

        /// <summary>
        /// A group asks the rules once, of the document it ends on, and an error it adds refuses it all with that error; inside
        /// it, a wire between ends already joined is refused -- by a wire the group made, a tap included -- as one outside is
        /// (the 16d sweep: each edit of a group asked the rules of the whole project, and each wire worked its netlist out again).
        /// </summary>
        [Test]
        public void AGroup_AsksTheRulesOnceAtItsEnd_AndKnowsWhatItHasJoined()
        {
            var b = new Bench();
            int a = b.Input("a"), y = b.Output("y");
            Project before = b.Project;

            EditResult joined = b.Editor.Group(() =>
            {
                int g = Bench.Must(b.Editor.Add(b.Module, new GatePart { Kind = GateKind.Not, Inputs = 1 }, new Placement(3, 0)));
                int w = Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(a, "out"), Endpoint.At(g, "a")));
                Assert.AreEqual("Those are already joined.", b.Editor.Connect(b.Module, Endpoint.At(g, "a"), Endpoint.OnWire(w)).Refusal,
                    "a tap on the wire just made, to a pin it is on");
                Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g, "y"), Endpoint.At(y, "in")));
                return b.Editor.Connect(b.Module, Endpoint.At(y, "in"), Endpoint.At(g, "y"));
            });
            Assert.AreEqual("Those are already joined.", joined.Refusal);
            Assert.AreSame(before, b.Project, "and the group taken back");

            EditResult twoDrivers = b.Editor.Group(() =>
            {
                int g = Bench.Must(b.Editor.Add(b.Module, new GatePart { Kind = GateKind.Not, Inputs = 1 }, new Placement(3, 0)));
                int h = Bench.Must(b.Editor.Add(b.Module, new GatePart { Kind = GateKind.Not, Inputs = 1 }, new Placement(3, 2)));
                Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g, "y"), Endpoint.At(y, "in")));
                Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(h, "y"), Endpoint.At(y, "in")));
                return EditResult.Ok();
            });
            Assert.IsFalse(twoDrivers.Done);
            StringAssert.Contains("both drive one wire", twoDrivers.Refusal, "the error the whole group adds, in the rules' words");
            Assert.AreSame(before, b.Project);
            Assert.IsTrue(b.Editor.Undo());
            Assert.IsNull(b.Current.Parts.FirstOrDefault(p => p.Name == "y"), "no step left behind by either: one undo takes y away");
        }

        /// <summary>
        /// A group is held to the rules as the one step it is, the document it ends on: an error one edit in it makes -- a second
        /// driver on y -- and a later one mends is no refusal, and the group is one step for undo. Its refusal, where the end
        /// still has the error, is that error, as <see cref="AGroup_AsksTheRulesOnceAtItsEnd_AndKnowsWhatItHasJoined"/> holds
        /// (the review of the 16d fixes: since b3b8c3aa a group is no longer refused an edit at a time, and the architecture
        /// says so).
        /// </summary>
        [Test]
        public void AGroup_IsCheckedAsTheDocumentItEndsOn_SoAnErrorItMendsIsNoRefusal()
        {
            var b = new Bench();
            int a = b.Input("a"), y = b.Output("y");
            Project before = b.Project;

            EditResult mended = b.Editor.Group(() =>
            {
                int g = Bench.Must(b.Editor.Add(b.Module, new GatePart { Kind = GateKind.Not, Inputs = 1 }, new Placement(3, 0)));
                int h = Bench.Must(b.Editor.Add(b.Module, new GatePart { Kind = GateKind.Not, Inputs = 1 }, new Placement(3, 2)));
                Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(a, "out"), Endpoint.At(g, "a")));
                Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g, "y"), Endpoint.At(y, "in")));
                Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(h, "y"), Endpoint.At(y, "in")));   // two drive y: an error
                return b.Editor.RemoveParts(b.Module, new[] { h });                                   // and mended
            });

            Assert.IsTrue(mended.Done, mended.Refusal);
            Assert.AreEqual(1, b.Current.Parts.OfType<GatePart>().Count(), "sanity: the group's own end, one gate driving y");
            Assert.IsFalse(b.Editor.Diagnostics.Any(d => d.IsError), string.Join("\n", b.Editor.Diagnostics.Select(d => d.Message)));

            int extra = Bench.Must(b.Editor.Add(b.Module, new GatePart { Kind = GateKind.Not, Inputs = 1 }, new Placement(3, 4)));
            StringAssert.Contains("both drive one wire", b.Editor.Connect(b.Module, Endpoint.At(extra, "y"), Endpoint.At(y, "in")).Refusal,
                "sanity: the second driver, alone, is an error the rules refuse");
            Assert.IsTrue(b.Editor.Undo(), "the extra gate");
            Assert.IsTrue(b.Editor.Undo(), "the group");
            Assert.AreSame(before, b.Project, "one step");
        }

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

        /// <summary>
        /// An edit no run can be made of, taken back by the workbench's session (the 16d sweep), as if it had never been made --
        /// as an edit the rules refuse leaves no step: the document before it, the steps to undo, and the step undone before it
        /// still there to redo (the review of the sweep's fixes: the edit had cleared it, and the take-back left it cleared). A
        /// drag's last move alone, the drag before it still one step; a group's edits together; and at the history's cap, the
        /// oldest step the edit pushed out back again.
        /// </summary>
        [Test]
        public void TakeBack_PutsTheHistoryBackAsItWasBeforeTheEdit()
        {
            Bench b = Bench.HalfAdder();
            Project before = b.Project;
            b.Gate(GateKind.Nor);
            Project middle = b.Project;
            b.Gate(GateKind.And);
            Project undone = b.Project;
            Assert.IsTrue(b.Editor.Undo());
            b.Gate(GateKind.Or);

            Assert.IsTrue(b.Editor.TakeBack());
            Assert.AreSame(middle, b.Project);
            Assert.IsTrue(b.Editor.CanRedo, "the step undone before it is still there to redo");
            Assert.AreSame(undone, b.Editor.RedoTarget);
            Assert.AreSame(before, b.Editor.UndoTarget, "and the steps before it as they were");
            Assert.IsFalse(b.Editor.TakeBack(), "taken back once, and nothing more");
            Assert.IsTrue(b.Editor.Redo());
            Assert.AreSame(undone, b.Project);

            // A drag: its last move taken back alone, and the next move still the same step.
            int g = b.Gate(GateKind.Buf);
            Project placed = b.Project;
            Bench.Must(b.Editor.Move(b.Module, g, new Placement(1, 0), continuing: true));
            Project moved = b.Project;
            Bench.Must(b.Editor.Move(b.Module, g, new Placement(2, 0), continuing: true));
            Assert.IsTrue(b.Editor.TakeBack());
            Assert.AreSame(moved, b.Project, "the drag's last move taken back alone");
            Bench.Must(b.Editor.Move(b.Module, g, new Placement(3, 0), continuing: true));
            Assert.IsTrue(b.Editor.Undo());
            Assert.AreSame(placed, b.Project, "and the drag still one step");

            // A group, after an undo: taken back whole, the step undone still to redo.
            Project grouped = b.Project;
            Assert.IsTrue(b.Editor.Undo());
            Project ungrouped = b.Project;
            Bench.Must(b.Editor.Group(() =>
            {
                b.Gate(GateKind.Xor);
                b.Gate(GateKind.Xnor);
                return EditResult.Ok();
            }));
            Assert.IsTrue(b.Editor.TakeBack());
            Assert.AreSame(ungrouped, b.Project, "the group's edits together");
            Assert.AreSame(grouped, b.Editor.RedoTarget);

            // At the cap: the oldest step, pushed out by the edit, back.
            var capped = new Bench();
            Project first = capped.Project;
            for (int i = 0; i < BoardEditor.HistoryLimit; i++)
                capped.Gate(GateKind.Buf);
            capped.Gate(GateKind.Buf);
            Assert.IsTrue(capped.Editor.TakeBack());
            int steps = 0;
            while (capped.Editor.Undo())
                steps++;
            Assert.AreEqual(BoardEditor.HistoryLimit, steps);
            Assert.AreSame(first, capped.Project, "the oldest step back again");

            Assert.IsFalse(new BoardEditor(Project.New("top")).TakeBack(), "nothing to take back");
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
