using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using BoardPoint = BitSorter.Board.Point;
using EngineGate = BitSorter.Sim.GateKind;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The inspector (Part 10c), Unity-free: the fields each kind of part and a wire has, what was typed read back
    /// with words, the document's refusals in its own words, several fields as one step, and the double-click and
    /// Enter that open it.
    /// </summary>
    public class InspectorTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        private static int Must(EditResult r)
        {
            Assert.IsTrue(r.Done, r.Refusal);
            return r.Id;
        }

        /// <summary>a and b into an AND and a NOT, the AND's output to y, and a clock and a reset as the strip places them.</summary>
        private sealed class Bench
        {
            public Bench()
            {
                var editor = new BoardEditor(Project.New("bench"));
                CatalogEntry Entry(string key) => PartsCatalog.Find(editor.Project, "bench", key);
                A = Must(PartsCatalog.Place(editor, "bench", Entry("input"), new Placement(1, 1)));
                B = Must(PartsCatalog.Place(editor, "bench", Entry("input"), new Placement(1, -1)));
                And = Must(editor.Add("bench", new GatePart { Kind = EngineGate.And, Rise = 1, Fall = 1 }, new Placement(4, 0)));
                Not = Must(editor.Add("bench", new GatePart { Kind = EngineGate.Not, Inputs = 1 }, new Placement(4, -3)));
                Y = Must(editor.Add("bench", new PortPart { Name = "y", Direction = PortDirection.Output }, new Placement(7, 0)));
                Clock = Must(PartsCatalog.Place(editor, "bench", Entry("clock"), new Placement(1, -4)));
                Reset = Must(PartsCatalog.Place(editor, "bench", Entry("reset"), new Placement(1, -6)));
                Must(editor.Connect("bench", Endpoint.At(A, "out"), Endpoint.At(And, "a")));
                Must(editor.Connect("bench", Endpoint.At(B, "out"), Endpoint.At(And, "b")));
                ToY = Must(editor.Connect("bench", Endpoint.At(And, "y"), Endpoint.At(Y, "in")));
                Session = new WorkbenchSession(editor.Project, Ticking(0.01));
                Session.Finish();
            }

            public int A, B, And, Not, Y, Clock, Reset, ToY;
            public WorkbenchSession Session;
            public Module Module => Session.Project.TopModule;

            public Inspection Of(int part) => Inspector.Of(Session.Project, Session.Module, new CanvasTarget(CanvasTargetKind.Part, part: part));

            public EditResult Apply(Inspection inspection, params (string Key, string Text)[] typed) =>
                Session.Inspect(inspection, typed.ToDictionary(t => t.Key, t => t.Text));

            public T Part<T>(int id) where T : Part => (T)Module.FindPart(id);
        }

        [Test]
        public void EachKindOfPart_ShowsTheFieldsItHas_AsTheyWouldBeTyped()
        {
            var b = new Bench();
            Inspection and = b.Of(b.And);
            CollectionAssert.AreEqual(new[] { "name", "inputs", "rise", "fall", "width" }, and.Fields.Select(f => f.Key));
            StringAssert.StartsWith("AND ", and.Title);
            Assert.AreEqual("1 ps", and.Field("rise").Text, "a time, in the document's unit");
            Assert.AreEqual("2", and.Field("inputs").Text);

            CollectionAssert.AreEqual(new[] { "name", "rise", "fall", "width" }, b.Of(b.Not).Fields.Select(f => f.Key), "a NOT has one input, and no field for it");
            CollectionAssert.AreEqual(new[] { "name", "width", "comment" }, b.Of(b.Y).Fields.Select(f => f.Key), "an output");
            CollectionAssert.AreEqual(new[] { "name", "width", "comment" }, b.Of(b.A).Fields.Select(f => f.Key), "an input given steps");

            Inspection clock = b.Of(b.Clock);
            StringAssert.StartsWith("CLOCK ", clock.Title);
            Assert.AreEqual("10 ps", clock.Field("period").Text);
            Inspection reset = b.Of(b.Reset);
            StringAssert.StartsWith("RESET ", reset.Title);
            Assert.IsNotNull(reset.Field("release"), "a reset's release");

            Inspection pin = Inspector.Of(b.Session.Project, b.Session.Module, new CanvasTarget(CanvasTargetKind.Pin, part: b.And, pin: "y"));
            Assert.AreEqual(and.Title, pin.Title, "a pin is its part's");
            Assert.IsNull(Inspector.Of(b.Session.Project, b.Session.Module, CanvasTarget.Nothing));
        }

        [Test]
        public void EachKind_HasItsQuickFields_AtHand_EachOneOfItsFields()
        {
            var b = new Bench();
            string[] Quick(Inspection i) => i.Quick.Select(f => f.Key).ToArray();

            CollectionAssert.AreEqual(new[] { "rise", "fall" }, Quick(b.Of(b.And)), "a gate: its delays");
            CollectionAssert.AreEqual(new[] { "rise", "fall" }, Quick(b.Of(b.Not)));
            CollectionAssert.AreEqual(new[] { "name", "width" }, Quick(b.Of(b.Y)), "a port: its name and width");
            CollectionAssert.AreEqual(new[] { "name", "width", "period" }, Quick(b.Of(b.Clock)), "a clock: its period too");
            CollectionAssert.AreEqual(new[] { "name", "width", "release" }, Quick(b.Of(b.Reset)), "a reset: its release too");
            Inspection wire = Inspector.Of(b.Session.Project, b.Session.Module, new CanvasTarget(CanvasTargetKind.Wire, wire: b.ToY));
            CollectionAssert.AreEqual(new[] { "name" }, Quick(wire), "a wire: its net's name");

            foreach (Inspection i in new[] { b.Of(b.And), b.Of(b.Clock), wire })
                foreach (InspectorField quick in i.Quick)
                    Assert.AreSame(i.Field(quick.Key), quick, "a quick field is one of its fields, the same one");
        }

        [Test]
        public void ADelayChanged_IsMadeInOneStep_AndTheRunIsPlayedAgainWithIt()
        {
            var b = new Bench();
            EditResult made = b.Apply(b.Of(b.And), ("rise", "3"), ("fall", "2 ps"));
            Assert.IsTrue(made.Done, made.Refusal);
            Assert.AreEqual((3L, 2L), (b.Part<GatePart>(b.And).Rise, b.Part<GatePart>(b.And).Fall));
            Assert.IsTrue(b.Session.Replaying, "a delay changes what the circuit does: replayed");

            Assert.IsTrue(b.Session.Undo());
            Assert.AreEqual((1L, 1L), (b.Part<GatePart>(b.And).Rise, b.Part<GatePart>(b.And).Fall), "both fields, one step");
        }

        /// <summary>
        /// An operator part's fields: its operator as written, its width, a shift's amount width and a comparison's sign only
        /// where they are; a token both kinds have keeps the part's own; a width of nothing is refused; and its symbol is its token.
        /// </summary>
        [Test]
        public void AnOperatorPart_IsInspected_AsItsOperatorIsWritten()
        {
            var editor = new BoardEditor(Project.New("ops"));
            int shift = Must(editor.Add("ops", new OperatorPart { Op = OperatorKind.ShiftLeft, Width = 4, BWidth = 2 }, new Placement(2, 0)));
            int reduce = Must(editor.Add("ops", new OperatorPart { Op = OperatorKind.ReduceAnd, Width = 4 }, new Placement(2, -4)));
            var session = new WorkbenchSession(editor.Project, Ticking(0.01));
            session.Finish();
            Inspection Of(int part) => Inspector.Of(session.Project, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: part));
            EditResult Apply(int part, params (string Key, string Text)[] typed) => session.Inspect(Of(part), typed.ToDictionary(t => t.Key, t => t.Text));
            OperatorPart Part(int id) => (OperatorPart)session.Project.TopModule.FindPart(id);

            CollectionAssert.AreEqual(new[] { "name", "op", "width", "bwidth" }, Of(shift).Fields.Select(f => f.Key).ToArray(), "a shift: no sign");
            Assert.AreEqual("<<", Of(shift).Fields[1].Text);

            Must(Apply(shift, ("op", "<")));
            Assert.AreEqual(OperatorKind.Less, Part(shift).Op);
            Assert.AreEqual(0, Part(shift).BWidth, "a comparison's b is as wide as a");
            CollectionAssert.AreEqual(new[] { "name", "op", "width", "signed" }, Of(shift).Fields.Select(f => f.Key).ToArray());
            Must(Apply(shift, ("signed", "yes")));
            Must(Apply(shift, ("op", "+")));
            Assert.IsFalse(Part(shift).Signed, "an adder takes no sign");

            Must(Apply(reduce, ("op", "^")));
            Assert.AreEqual(OperatorKind.ReduceXor, Part(reduce).Op, "^ before a stays a reduction");
            Assert.IsFalse(Apply(reduce, ("op", "?")).Done);

            Must(Apply(shift, ("op", ">>")));
            StringAssert.Contains("a bit or more", Apply(shift, ("bwidth", "0")).Refusal);

            Assert.AreEqual(PartSymbol.Operator, DocumentSchematic.SymbolOf(Part(shift)));
            Assert.AreEqual(">>", DocumentSchematic.NameOf(Part(shift)));
        }

        [Test]
        public void WhatIsNoNumberOrTime_IsRefusedWithWords_AndWhatTheDocumentForbids_InItsOwn()
        {
            var b = new Bench();
            Project before = b.Session.Project;

            EditResult soon = b.Apply(b.Of(b.And), ("rise", "soon"));
            Assert.IsFalse(soon.Done);
            StringAssert.StartsWith("The rise:", soon.Refusal);

            EditResult two = b.Apply(b.Of(b.And), ("width", "two"));
            StringAssert.Contains("whole number", two.Refusal);

            EditResult wide = b.Apply(b.Of(b.And), ("width", "2000"));
            Assert.IsFalse(wide.Done);
            StringAssert.Contains("1 to 1024", wide.Refusal, "the document's own words");

            EditResult nine = b.Apply(b.Of(b.And), ("inputs", "9"));
            StringAssert.Contains("2 to 8", nine.Refusal);

            EditResult keyword = b.Apply(b.Of(b.And), ("name", "module"));
            StringAssert.Contains("keyword", keyword.Refusal);

            Assert.AreSame(before, b.Session.Project, "and nothing changed");
            Assert.IsFalse(b.Session.Replaying);
        }

        /// <summary>
        /// A count typed that no part can have -- INPUTS or WIDTH below none, or a million -- is refused in the document's words,
        /// never thrown, and makes no pin for each of it (the 16d sweep: -1 in an AND's INPUTS threw out of its pins, and a
        /// splitter's WIDTH of 100,000,000 made that many before the rule was reached).
        /// </summary>
        [Test]
        public void ACountTypedThatNoPartCanHave_IsRefusedInWords_NeverThrown()
        {
            var b = new Bench();
            int mux = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "mux"), new Placement(9, -4)));
            int split = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "splitter"), new Placement(9, -8)));
            int join = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "joiner"), new Placement(9, -12)));
            Project before = b.Session.Project;

            void Refused(int part, string key, string typed, string said)
            {
                EditResult result = default;
                long made = GC.GetAllocatedBytesForCurrentThread();
                Assert.DoesNotThrow(() => result = b.Apply(b.Of(part), (key, typed)), $"{key} {typed}");
                made = GC.GetAllocatedBytesForCurrentThread() - made;

                Assert.IsFalse(result.Done, $"{key} {typed}");
                StringAssert.Contains(said, result.Refusal, $"{key} {typed}: in the document's words");
                Assert.Less(made, 16_000_000, $"{key} {typed}: no pin made for each of the count");
            }

            foreach (string n in new[] { "-1", "-2", "1000000" })
            {
                Refused(b.And, "inputs", n, "takes 2 to 8");
                Refused(mux, "inputs", n, "has 2, 4 or 8");
                Refused(split, "width", n, "takes 2 to 64");
                Refused(join, "width", n, "takes 2 to 64");
            }

            Assert.AreSame(before, b.Session.Project, "and nothing changed");
        }

        /// <summary>
        /// A count or a width a part's pins are made by, typed out of its range -- a gate's or a mux's inputs, a splitter's or a
        /// joiner's width -- is refused in the document's words like any other (the 16d sweep): the rules said why, and then
        /// made the module's netlist anyway, which threw for a count below 0 and made a pin array of a huge one.
        /// </summary>
        [Test]
        public void ACountOrAWidthAPartsPinsAreMadeBy_OutOfRange_IsRefusedInTheDocumentsWords()
        {
            var b = new Bench();
            CatalogEntry Entry(string key) => PartsCatalog.Find(b.Session.Project, b.Session.Module, key);
            int mux = Must(b.Session.Place(Entry("mux"), new Placement(9, -9)));
            int split = Must(b.Session.Place(Entry("splitter"), new Placement(12, -9)));
            int join = Must(b.Session.Place(Entry("joiner"), new Placement(15, -9)));
            Project before = b.Session.Project;

            foreach ((int part, string field, string typed, string words) in new[]
            {
                (b.And, "inputs", "-1", "2 to 8"), (b.And, "inputs", "-2", "2 to 8"), (mux, "inputs", "-3", "2, 4 or 8"),
                (split, "width", "-2", "2 to 64"), (join, "width", (1 << 28).ToString(), "2 to 64"),   // 268 million: a pin array that size
            })
            {
                EditResult refused = default;
                Assert.DoesNotThrow(() => refused = b.Apply(b.Of(part), (field, typed)), field + " = " + typed);
                Assert.IsFalse(refused.Done, field + " = " + typed);
                StringAssert.Contains(words, refused.Refusal, "in the document's own words");
            }
            Assert.AreSame(before, b.Session.Project, "and nothing changed");
        }

        /// <summary>
        /// A register's width typed out of range while it has a reset value and a starting value, which are read again at its
        /// new width, is refused in the document's words (the 16d sweep): a width below 1, or past what a value can be, threw
        /// out of reading them.
        /// </summary>
        [Test]
        public void ARegistersWidth_OutOfRange_WithItsValues_IsRefusedInTheDocumentsWords()
        {
            var b = new Bench();
            int register = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "register"), new Placement(9, -4)));
            Assert.IsTrue(b.Apply(b.Of(register), ("reset", "yes"), ("resetvalue", "1'b0"), ("start", "1'b1")).Done);
            Project before = b.Session.Project;

            foreach (string width in new[] { "0", "-1", "2000000" })
            {
                EditResult refused = default;
                Assert.DoesNotThrow(() => refused = b.Apply(b.Of(register), ("width", width)), "WIDTH " + width);
                Assert.IsFalse(refused.Done, "WIDTH " + width);
                StringAssert.Contains("1 to " + BoardRules.MaxWidth, refused.Refusal, "in the document's own words");
            }
            Assert.AreSame(before, b.Session.Project, "and nothing changed");
        }

        [Test]
        public void NothingChanged_IsDone_WithNothingMade()
        {
            var b = new Bench();
            Project before = b.Session.Project;
            Assert.IsTrue(b.Apply(b.Of(b.And)).Done);
            Assert.IsTrue(b.Apply(b.Of(b.And), ("rise", "1 ps"), ("name", b.Part<GatePart>(b.And).Name)).Done);
            Assert.AreSame(before, b.Session.Project);
            Assert.IsFalse(b.Session.Replaying, "nothing to play again");
        }

        /// <summary>
        /// A clock's period or a reset's release typed again as the time it is, in other words -- 10 where it shows 10 ps --
        /// changes nothing, as the same text does: no step to undo, nothing played again (the 16d sweep: the field's text was
        /// compared, and the stimulus made again with the same time).
        /// </summary>
        [Test]
        public void APeriodOrAReleaseTypedAgainInOtherWords_ChangesNothing()
        {
            var b = new Bench();
            Project before = b.Session.Project;
            Inspection clock = b.Of(b.Clock), reset = b.Of(b.Reset);
            StringAssert.EndsWith(" ps", clock.Field("period").Text, "sanity: shown in its unit");

            Assert.IsTrue(b.Apply(clock, ("period", clock.Field("period").Text.Split(' ')[0])).Done);
            Assert.IsTrue(b.Apply(reset, ("release", reset.Field("release").Text.Split(' ')[0])).Done);
            Assert.AreSame(before, b.Session.Project, "nothing made");
            Assert.IsFalse(b.Session.Editor.CanUndo, "no step to undo");
            Assert.IsFalse(b.Session.Replaying, "nothing to play again");
        }

        [Test]
        public void AClockRenamed_AndItsPeriodChanged_GoTogether_AsOneStep()
        {
            var b = new Bench();
            string was = b.Part<PortPart>(b.Clock).Name;
            EditResult made = b.Apply(b.Of(b.Clock), ("name", "fast"), ("period", "4"));
            Assert.IsTrue(made.Done, made.Refusal);

            ClockTrack clock = b.Session.Project.Stimulus.Clocks.Single();
            Assert.AreEqual(("fast", 4L), (clock.Port, clock.Period), "the clock follows the port's new name, at its new period");
            Assert.AreEqual("fast", b.Part<PortPart>(b.Clock).Name);

            Assert.IsTrue(b.Session.Undo());
            clock = b.Session.Project.Stimulus.Clocks.Single();
            Assert.AreEqual((was, 10L), (clock.Port, clock.Period), "one step back");

            EditResult slow = b.Apply(b.Of(b.Clock), ("period", "1"));
            StringAssert.Contains("at least 2", slow.Refusal);
        }

        [Test]
        public void AResetsRelease_IsMoved()
        {
            var b = new Bench();
            string name = b.Part<PortPart>(b.Reset).Name;
            Assert.IsTrue(b.Apply(b.Of(b.Reset), ("release", "12")).Done);
            Track track = b.Session.Project.Stimulus.Tracks.Single(t => t.Port == name);
            Assert.AreEqual(new[] { 0L, 12L }, track.Steps.Select(s => s.Time).ToArray());
            Assert.AreEqual("1", track.Steps[0].Value.Format(Radix.Binary), "held at 1 from the start, as it was");
        }

        [Test]
        public void AWiresNet_IsNamed_AndAnEmptyNameGivesItItsOwnBack()
        {
            var b = new Bench();
            Inspection wire = Inspector.Of(b.Session.Project, b.Session.Module, new CanvasTarget(CanvasTargetKind.Wire, wire: b.ToY));
            Assert.AreEqual("y", wire.Field("name").Text == "" ? "y" : wire.Field("name").Text);
            StringAssert.Contains("port", wire.Says, "the AND's output to y is y's net, which keeps y's name");

            // A wire between two gates: on a net no port names.
            int buf = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "buf"), new Placement(7, -3)));
            int toAnd = Must(b.Session.Connect(Endpoint.At(b.Not, "y"), Endpoint.At(buf, "a")));
            Inspection inner = Inspector.Of(b.Session.Project, b.Session.Module, new CanvasTarget(CanvasTargetKind.Wire, wire: toAnd));
            Assert.IsTrue(b.Apply(inner, ("name", "left")).Done);
            Assert.AreEqual("left", Netlist.Of(b.Session.Project, b.Module).NetOfWire(toAnd).Name);

            inner = Inspector.Of(b.Session.Project, b.Session.Module, new CanvasTarget(CanvasTargetKind.Wire, wire: toAnd));
            Assert.AreEqual("left", inner.Field("name").Text);
            Assert.IsTrue(b.Apply(inner, ("name", "")).Done);
            Assert.IsNull(b.Module.FindWire(toAnd).NetName, "its own name back");
        }

        [Test]
        public void ARegisterTakesItsValuesAtItsNewWidth_AndAConstantItsWidthFromItsValue()
        {
            var b = new Bench();
            int register = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "register"), new Placement(9, -4)));
            int constant = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "constant"), new Placement(9, -7)));

            Assert.IsTrue(b.Apply(b.Of(register), ("width", "4"), ("reset", "yes"), ("resetvalue", "4'b0101"), ("start", "")).Done);
            var r = b.Part<RegisterPart>(register);
            Assert.AreEqual((4, true), (r.Width, r.HasReset));
            Assert.AreEqual("0101", r.ResetValue.Value.Format(Radix.Binary));
            Assert.IsNull(r.Initial);

            EditResult maybe = b.Apply(b.Of(register), ("reset", "maybe"));
            StringAssert.Contains("yes or no", maybe.Refusal);
            EditResult tooBig = b.Apply(b.Of(register), ("resetvalue", "8'hFF"));
            StringAssert.Contains("does not fit", tooBig.Refusal);

            Assert.IsTrue(b.Apply(b.Of(constant), ("value", "8'hA5")).Done);
            Assert.AreEqual(8, b.Part<ConstantPart>(constant).Value.Width, "four bits, from 8'hA5's 8");
            StringAssert.StartsWith("The value:", b.Apply(b.Of(constant), ("value", "8'hZQ")).Refusal);
        }

        /// <summary>
        /// A register given a reset value or a start, and then a WIDTH no register can have -- 0, -3, a hundred million -- is
        /// refused with the width's own words, as 2000 is (the 16d sweep: its values were read again at the new width, which
        /// threw for a width below a bit, and for one past the widest value).
        /// </summary>
        [Test]
        public void ARegistersWidth_ThatNoRegisterCanHave_IsRefusedInWords_WithItsValuesSet()
        {
            var b = new Bench();
            int register = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "register"), new Placement(9, -4)));
            Assert.IsTrue(b.Apply(b.Of(register), ("width", "4"), ("reset", "yes"), ("resetvalue", "4'b0101"), ("start", "4'b0011")).Done);
            Project before = b.Session.Project;

            foreach (string width in new[] { "0", "-3", "100000000", "2000" })
            {
                EditResult result = default;
                Assert.DoesNotThrow(() => result = b.Apply(b.Of(register), ("width", width)), width);
                Assert.IsFalse(result.Done, width);
                StringAssert.Contains("1 to 1024", result.Refusal, $"{width}: the width's own words");
            }

            Assert.AreSame(before, b.Session.Project, "and nothing changed");
        }

        /// <summary>
        /// An input placed from the strip, given its steps, is made a bus by typing its WIDTH (the 16d sweep: it was refused every
        /// time, its steps still a bit wide).
        /// </summary>
        [Test]
        public void AnInputPlacedFromTheStrip_IsMadeABus_ByItsWidth()
        {
            var b = new Bench();
            int c = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "input"), new Placement(1, -9)));
            string name = b.Part<PortPart>(c).Name;
            Assert.IsTrue(b.Session.Project.Stimulus.Tracks.Any(t => t.Port == name), "sanity: the strip gives it steps");

            EditResult made = b.Apply(b.Of(c), ("width", "4"));
            Assert.IsTrue(made.Done, made.Refusal);
            Assert.AreEqual(4, b.Part<PortPart>(c).Width);
            Assert.IsTrue(b.Session.Project.Stimulus.Tracks.Single(t => t.Port == name).Steps.All(s => s.Value.Width == 4), "its steps with it");
        }

        /// <summary>A register's clock to Q and setup (Part 14b): at hand beside its values, set together as one step, never below 0.</summary>
        [Test]
        public void ARegistersClockToQAndSetup_AreAtHand_AndSetInOneStep()
        {
            var b = new Bench();
            int register = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "register"), new Placement(9, -4)));
            CollectionAssert.AreEqual(new[] { "resetvalue", "start", "tcq", "tsetup" }, b.Of(register).Quick.Select(f => f.Key));

            EditResult made = b.Apply(b.Of(register), ("tcq", "2"), ("tsetup", "3 ps"));
            Assert.IsTrue(made.Done, made.Refusal);
            Assert.AreEqual((2L, 3L), (b.Part<RegisterPart>(register).Tcq, b.Part<RegisterPart>(register).Tsetup));
            Assert.IsTrue(b.Session.Undo());
            Assert.AreEqual((0L, 0L), (b.Part<RegisterPart>(register).Tcq, b.Part<RegisterPart>(register).Tsetup), "both, one step");

            StringAssert.StartsWith("The clock to Q:", b.Apply(b.Of(register), ("tcq", "-1")).Refusal, "refused, in words");
        }

        [Test]
        public void ANetRenamed_KeepsItsRowInTheWaveform_UnderItsNewName_WithItsRadix()
        {
            var b = new Bench();
            int buf = Must(b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "buf"), new Placement(7, -3)));
            int wire = Must(b.Session.Connect(Endpoint.At(b.Not, "y"), Endpoint.At(buf, "a")));
            b.Session.Finish();
            WorkbenchModel model = b.Session.Run.Model;
            string old = Netlist.Of(b.Session.Project, b.Module).NetOfWire(wire).Name;
            string yKey = "y";

            var list = new WaveList(model.Signals, new[] { yKey, old });
            list.Select(1);
            list.SetRadix(WaveRadix.Hex);
            Assert.GreaterOrEqual(list.Lines[1].Signal.Signal, 0, "sanity: the net's row is found");

            Project before = b.Session.Project;
            Inspection inspection = Inspector.Of(b.Session.Project, b.Session.Module, new CanvasTarget(CanvasTargetKind.Wire, wire: wire));
            Assert.IsTrue(b.Apply(inspection, ("name", "inverted")).Done);
            foreach ((string from, string to) in Inspector.NetsRenamed(before, b.Session.Project, b.Session.Module))
                list.Rekey(from, to);
            b.Session.Finish();
            list.Resolve(b.Session.Run.Model.Signals);

            Assert.AreEqual("inverted", list.Lines[1].Signal.Key, "the row is under the net's new name");
            Assert.GreaterOrEqual(list.Lines[1].Signal.Signal, 0, "and the run played again finds it");
            Assert.AreEqual(WaveRadix.Hex, list.Lines[1].Signal.Radix, "in the radix it was in, at the place it was");

            Project renamedFrom = b.Session.Project;
            Assert.IsTrue(b.Apply(b.Of(b.And), ("name", "both")).Done);
            CollectionAssert.IsEmpty(Inspector.NetsRenamed(renamedFrom, b.Session.Project, b.Session.Module),
                "the AND drives y's net, which keeps the port's name: renaming the AND renames no net");
        }

        // -----------------------------------------------------------------
        // Opening it
        // -----------------------------------------------------------------

        private static CanvasPress At(CanvasTarget t, double time, bool drafting = false, bool hand = false, bool alt = false, float x = 10) =>
            new CanvasPress { Button = 0, Window = new Vec2(x, 10), Board = new BoardPoint(4, 0), Target = t, Time = time, Drafting = drafting, InHand = hand, Alt = alt };

        /// <summary>Presses, each let go where it was, as the commands the last one made.</summary>
        private static string Clicks(params CanvasPress[] presses)
        {
            var pointer = new CanvasPointer();
            var commands = new List<CanvasCommand>();
            foreach (CanvasPress press in presses)
            {
                commands.Clear();
                pointer.Press(press, commands);
                pointer.Release(press.Board, press.Target, press.Alt, commands);
            }

            return string.Join(", ", commands.Where(c => c.Gesture != CanvasGesture.Select));
        }

        [Test]
        public void ADoubleClickOnAPartOrAWire_Inspects_EvenWithTheWireItsFirstClickBegan()
        {
            var part = new CanvasTarget(CanvasTargetKind.Part, part: 3);
            var pin = new CanvasTarget(CanvasTargetKind.Pin, part: 3, pin: "y");
            var wire = new CanvasTarget(CanvasTargetKind.Wire, wire: 7);

            Assert.AreEqual("Inspect part 3", Clicks(At(part, 0), At(part, 0.2)));
            Assert.AreEqual("Inspect pin 3.y", Clicks(At(pin, 0), At(pin, 0.2, drafting: true)), "the first click on the pin began a wire");
            Assert.AreEqual("Inspect wire 7 segment -1", Clicks(At(wire, 0), At(wire, 0.2)));
            Assert.AreEqual("", Clicks(At(CanvasTarget.Nothing, 0), At(CanvasTarget.Nothing, 0.2)), "on nothing, nothing to inspect");
        }

        [Test]
        public void ADoubleClick_IsTwoPlainClicks_SoonAndNear_AndNeverAPlaceOrAModifiedClickAndThenAnother()
        {
            var part = new CanvasTarget(CanvasTargetKind.Part, part: 3);
            Assert.AreEqual("", Clicks(At(part, 0), At(part, 0.6)), "too slow");
            Assert.AreEqual("", Clicks(At(part, 0), At(part, 0.2, x: 30)), "too far apart");
            Assert.AreEqual("", Clicks(At(part, 0, hand: true), At(part, 0.2)), "a part placed, then pressed to drag it");
            Assert.AreEqual("", Clicks(At(part, 0, alt: true), At(part, 0.2)), "Alt and a click, then a click");
            Assert.AreEqual("", Clicks(At(part, 0), At(part, 0.2), At(part, 0.3)), "a third click starts again");
            Assert.AreEqual("Place 4,0", Clicks(At(part, 0), At(part, 0.2, hand: true)), "with a part in hand, the second places it");

            // A press dragged a cell away and let go is a drag, not the first of two clicks.
            var pointer = new CanvasPointer();
            var commands = new List<CanvasCommand>();
            pointer.Press(At(part, 0), commands);
            pointer.Move(new Vec2(60, 10), new BoardPoint(5, 0), false, commands);
            pointer.Release(new BoardPoint(5, 0), part, false, commands);
            commands.Clear();
            pointer.Press(At(part, 0.2), commands);
            Assert.IsFalse(commands.Any(c => c.Gesture == CanvasGesture.Inspect), "a drag, then a press");
        }

        [Test]
        public void Inspecting_SelectsWhatIsInspected_Alone_AndLetsTheWireBeingDrawnGo_AndEnterHasTheOneThingSelected()
        {
            var b = new Bench();
            var editor = new CanvasEditor(b.Session);
            editor.Do(new CanvasCommand(CanvasGesture.BeginWire, target: new CanvasTarget(CanvasTargetKind.Pin, part: b.And, pin: "y")));
            Assert.IsTrue(editor.Drafting);

            editor.Do(new CanvasCommand(CanvasGesture.Inspect, target: new CanvasTarget(CanvasTargetKind.Pin, part: b.And, pin: "y")));
            Assert.IsFalse(editor.Drafting);
            CollectionAssert.AreEqual(new[] { b.And }, editor.Selection.Parts);
            Assert.AreEqual(new CanvasTarget(CanvasTargetKind.Part, part: b.And), editor.Single);

            editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: b.Y), extend: true));
            Assert.AreEqual(CanvasTargetKind.Nothing, editor.Single.Kind, "two selected: Enter inspects nothing");
            Assert.IsNull(WorkbenchKeys.WhyRefused(WorkbenchKeys.For(WorkbenchCommand.Inspect)), "Enter is the workbench's to take");
        }
    }
}
