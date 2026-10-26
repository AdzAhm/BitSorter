using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The workbench's samples (<see cref="WorkbenchSamples"/>): each a board the editor accepts
    /// without a word, compiled, run and laid out; the full adder adding and the counter counting,
    /// read the way the workbench reads them; and every one laid out to be read.
    /// </summary>
    /// <remarks>
    /// **The layout is held here, not left to the eye**, from the board's own geometry
    /// (<see cref="PartGeometry"/>, <see cref="WireGeometry"/>) -- the same that
    /// <see cref="DocumentSchematic"/> draws from. The words round a part are measured as the view
    /// sets them, in Micro's 12 units a line at 48 a cell: a line 0.3 of a cell tall and a character
    /// about a seventh of one wide. That is an estimate of the font, so the boxes are held clear of
    /// wires and parts rather than to the pixel. The reference circuit is held to every rule too,
    /// which is what says the rules are not drawn tighter than a good layout.
    /// </remarks>
    public class SampleTests
    {
        private static IEnumerable<string> Names => WorkbenchSamples.All.Select(s => s.Name);

        private static Project Build(string name) => WorkbenchSamples.All.Single(s => s.Name == name).Build();

        private static int Row(WorkbenchModel model, string label) => model.Layout.Rows.ToList().FindIndex(r => r.Label == label);

        /// <summary>A row's value at <paramref name="time"/>, as a number; fails on x or z.</summary>
        private static ulong Value(WorkbenchModel model, int row, double time)
        {
            model.Seek(time);
            LogicVec value = model.RowValue(row, out string text);
            Assert.IsTrue(value.TryGetUInt64(out ulong number), $"{model.Layout.Rows[row].Label} is {text} at {time}");
            return number;
        }

        // -----------------------------------------------------------------
        // What is offered
        // -----------------------------------------------------------------

        [Test]
        public void TheChip_OffersTheReference_ThenTheFullAdder_ThenTheCounter()
        {
            CollectionAssert.AreEqual(new[] { "Reference", "Full adder", "Counter", "Four half adders" }, Names);

            Project reference = WorkbenchSamples.All[0].Build();
            Assert.AreEqual(ReferenceCircuit.Document().Top, reference.Top);
            Assert.AreEqual(ReferenceCircuit.EndTime, reference.Stimulus.End, "the reference is the reference circuit's own document");
            Assert.AreEqual("full_adder", WorkbenchSamples.All[1].Build().Top);
            Assert.AreEqual("counter", WorkbenchSamples.All[2].Build().Top);
        }

        [TestCaseSource(nameof(Names))]
        public void ASample_IsABoardTheEditorHasNothingToSayAbout(string name)
        {
            CollectionAssert.IsEmpty(BoardRules.Check(Build(name)), "not an error, and not a warning either: a sample is a board drawn as it should be");
        }

        [TestCaseSource(nameof(Names))]
        public void ASample_Compiles_Elaborates_LaysOut_AndRunsToWhereItsTestbenchStops(string name)
        {
            Project project = Build(name);

            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));

            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));
            Design design = elaboration.Design;

            SchematicLayout layout = DocumentSchematic.Build(project, compiled);
            Assert.IsNotEmpty(layout.Parts);
            Assert.IsNotEmpty(layout.Nets);
            Assert.AreEqual(project.TopModule.Ports.Count, layout.Rows.Count, "a waveform row for every port");
            foreach (NetLayout net in layout.Nets)
                Assert.IsTrue(design.TryFind(net.Signal, out _), $"the net {net.Name} reads {net.Signal}, which the design has");
            foreach (WaveRow row in layout.Rows)
                Assert.IsTrue(design.TryFind(row.Signal, out _), $"the row {row.Label} reads {row.Signal}, which the design has");

            long stop = project.Stimulus.StopTime;
            var session = new SimSession(design, null);
            RunResult run = session.RunTo(stop);

            Assert.AreNotEqual(RunStatus.Stopped, run.Status, string.Join("; ", session.History.Messages));
            Assert.AreEqual(stop, session.Reached, "the run goes to where the testbench stops, and no further");
            CollectionAssert.IsEmpty(session.History.Messages.Where(m => m.Severity != Severity.Info), "nothing in the transcript but the testbench stopping");
        }

        // -----------------------------------------------------------------
        // The full adder
        // -----------------------------------------------------------------

        /// <summary>
        /// Every combination, from 4 ns after it is set -- the two XORs, the slowest way through --
        /// until the next: s the inputs' parity and cout their majority, read off the waveform's rows.
        /// </summary>
        [Test]
        public void TheFullAdder_AddsEveryCombination_WithinFourNanoseconds()
        {
            WorkbenchModel model = WorkbenchModel.Of(WorkbenchSamples.FullAdder());
            int s = Row(model, "s"), cout = Row(model, "cout");

            for (int v = 0; v < 8; v++)
            {
                int a = v >> 2 & 1, b = v >> 1 & 1, cin = v & 1;
                ulong sum = (ulong)(a ^ b ^ cin), carry = (ulong)(a + b + cin >= 2 ? 1 : 0);
                long set = v * WorkbenchSamples.FullAdderStep;

                foreach (double time in new[] { set + 4.0, set + 5.0, set + WorkbenchSamples.FullAdderStep - 0.01 })
                {
                    Assert.AreEqual(sum, Value(model, s, time), $"s for {a}{b}{cin} at {time}");
                    Assert.AreEqual(carry, Value(model, cout, time), $"cout for {a}{b}{cin} at {time}");
                }
            }
        }

        /// <summary>
        /// The four half adders (Part 12c): each pair of inputs, from 2 ns after it is set -- the XOR, the slower way -- until
        /// the next, s is a XOR b and c is a AND b, bit by bit: copy i works on bit i.
        /// </summary>
        [Test]
        public void TheFourHalfAdders_AddEachBitPair_CopyIOnBitI()
        {
            Project project = WorkbenchSamples.FourHalfAdders();
            Assert.AreEqual(4, project.TopModule.Parts.OfType<BlockPart>().Single().Copies, "one block, four copies");
            WorkbenchModel model = WorkbenchModel.Of(project);
            int s = Row(model, "s[3:0]"), c = Row(model, "c[3:0]");
            Assert.GreaterOrEqual(s, 0, "a row for s, all four bits");
            Track a = project.Stimulus.Tracks.Single(t => t.Port == "a"), b = project.Stimulus.Tracks.Single(t => t.Port == "b");

            for (int v = 0; v < a.Steps.Count; v++)
            {
                Assert.IsTrue(a.Steps[v].Value.TryGetUInt64(out ulong x));
                Assert.IsTrue(b.Steps[v].Value.TryGetUInt64(out ulong y));
                long set = a.Steps[v].Time;
                foreach (double time in new[] { set + 2.0, set + WorkbenchSamples.FourHalfAddersStep - 0.01 })
                {
                    Assert.AreEqual(x ^ y, Value(model, s, time), $"s for {x:X} and {y:X} at {time}");
                    Assert.AreEqual(x & y, Value(model, c, time), $"c for {x:X} and {y:X} at {time}");
                }
            }
        }

        /// <summary>
        /// The glitch the second XOR passes when cin and the first half's sum reach it 2 ns apart, as
        /// <see cref="WorkbenchSamples.FullAdder"/> says: it is the engine's answer, so it is pinned
        /// here rather than left to be noticed.
        /// </summary>
        [Test]
        public void TheFullAdder_GlitchesOnS_WhereCinAndTheFirstSumArrive2nsApart()
        {
            WorkbenchModel model = WorkbenchModel.Of(WorkbenchSamples.FullAdder());
            int s = Row(model, "s");

            Assert.AreEqual(1UL, Value(model, s, 13.5));
            Assert.AreEqual(0UL, Value(model, s, 14.5), "001 to 010: cin falls at 12 and the XOR answers it at 14");
            Assert.AreEqual(1UL, Value(model, s, 16.5), "and h1's sum rises at 14, answered at 16");

            Assert.AreEqual(0UL, Value(model, s, 37.5));
            Assert.AreEqual(1UL, Value(model, s, 38.5), "101 to 110: cin falls at 36");
            Assert.AreEqual(0UL, Value(model, s, 40.5), "and h1's sum falls at 38");
        }

        [Test]
        public void TheFullAdder_IsTwoHalfAdderBlocks_EachAScopeOfItsOwn()
        {
            Project project = WorkbenchSamples.FullAdder();
            CompiledProject compiled = BoardCompiler.Compile(project);
            Design design = Elaborator.Elaborate(compiled.Tree, compiled.Top).Design;
            string path = compiled.DesignPath;

            foreach (string block in new[] { "h1", "h2" })
            {
                string scope = path + "." + block + ".";
                Assert.Greater(design.Signals.Count(signal => signal.Name.StartsWith(scope, StringComparison.Ordinal)), 4,
                    $"{block}'s ports and its gates are signals under {scope}");
                foreach (string port in new[] { "a", "b", "s", "c" })
                    Assert.IsTrue(design.TryFind(scope + port, out _), $"{scope}{port}");
            }

            SchematicLayout layout = DocumentSchematic.Build(project, compiled);
            PartLayout[] blocks = layout.Parts.Where(p => p.Symbol == PartSymbol.Block).ToArray();
            CollectionAssert.AreEqual(new[] { "h1", "h2" }, blocks.Select(b => b.Part.Name), "drawn as two boxes, the instances' names in the document");
            CollectionAssert.AreEqual(new[] { "half_adder", "half_adder" }, blocks.Select(b => b.Name), "each labelled with its module");
        }

        [Test]
        public void TheFullAdders_Rows_ReadAsItsTruthTable()
        {
            WorkbenchModel model = WorkbenchModel.Of(WorkbenchSamples.FullAdder());
            CollectionAssert.AreEqual(new[] { "a", "b", "cin", "s", "cout" }, model.Layout.Rows.Select(r => r.Label));
        }

        // -----------------------------------------------------------------
        // The counter
        // -----------------------------------------------------------------

        /// <summary>
        /// The count just after every rising edge, and just before it: held at 0 through the edge that
        /// comes while reset is high, then one more each edge, 7 wrapping to 0 -- and changing at no
        /// other moment.
        /// </summary>
        [Test]
        public void TheCounter_CountsOnEveryRisingEdge_OnceItsResetHasFallen()
        {
            WorkbenchModel model = WorkbenchModel.Of(WorkbenchSamples.Counter());
            int count = Row(model, "count[2:0]");
            const long period = WorkbenchSamples.CounterPeriod;
            ulong wrap = 1UL << WorkbenchSamples.CounterBits;

            Assert.AreEqual(0UL, Value(model, count, 0), "reset rises at 0, and the register is 0 from the start");

            ulong expected = 0;
            var seen = new List<ulong>();
            for (long edge = period / 2; edge < WorkbenchSamples.CounterEnd; edge += period)
            {
                Assert.AreEqual(expected, Value(model, count, edge - 0.5), $"before the edge at {edge}");
                if (edge > WorkbenchSamples.CounterResetFalls)
                    expected = (expected + 1) % wrap;
                Assert.AreEqual(expected, Value(model, count, edge + 0.5), edge < WorkbenchSamples.CounterResetFalls
                    ? $"the edge at {edge} comes while reset is held, and changes nothing"
                    : $"after the edge at {edge}");
                seen.Add(expected);
            }

            CollectionAssert.AreEqual(new ulong[] { 0, 1, 2, 3, 4, 5, 6, 7, 0 }, seen, "through every value and round to 0");

            var segments = new List<WaveSegment>();
            model.RowSegments(count, segments, model.End);
            foreach (WaveSegment segment in segments)
                Assert.IsTrue(segment.From == 0 || (segment.From - period / 2) % period == 0, $"the count changed at {segment.From}, which is no rising edge");
        }

        /// <summary>
        /// The register's next value is ready before every edge: each change of it comes 1 to 3 ns
        /// after an edge -- the NOT and the AND's 1, the first XOR's 2, the AND and the second XOR's 3
        /// -- so it is settled a nanosecond before the edge that takes it.
        /// </summary>
        [Test]
        public void TheCounter_SettlesInsideEveryPeriod_WithANanosecondToSpare()
        {
            WorkbenchModel model = WorkbenchModel.Of(WorkbenchSamples.Counter());
            const long period = WorkbenchSamples.CounterPeriod;
            Assert.IsTrue(model.Session.Design.TryFind(model.Layout.Nets[model.Layout.NetNamed("next")].Signal, out SignalId next));

            List<ValueChange> changes = model.Session.History.SettledChanges(next);
            Assert.Greater(changes.Count, 8, "sanity: it changes with every count");
            foreach (ValueChange change in changes)
            {
                if (change.Time <= WorkbenchSamples.CounterResetFalls)
                    continue;
                long after = (change.Time - period / 2) % period;
                Assert.That(after, Is.InRange(1, period - 1), $"next changed at {change.Time}, {after} ns after a rising edge");
            }

            int count = Row(model, "count[2:0]");
            int nextNet = model.Layout.NetNamed("next");
            for (long edge = period / 2 + period; edge < WorkbenchSamples.CounterEnd; edge += period)
            {
                ulong now = Value(model, count, edge - 1);
                model.Seek(edge - 1);
                Assert.IsTrue(model.NetValue(nextNet).TryGetUInt64(out ulong ready));
                Assert.AreEqual((now + 1) % (1UL << WorkbenchSamples.CounterBits), ready, $"a nanosecond before the edge at {edge}, next is the count plus one");
            }
        }

        /// <summary>
        /// The second XOR's answer to bit 2 moving at the edge is cancelled when the carry follows a
        /// nanosecond later, at 19 and at 35, and the schematic shows each dying on d2's wire.
        /// </summary>
        [Test]
        public void TheCounter_SwallowsTwoChanges_OnTheWayOutOfItsSecondXor()
        {
            WorkbenchModel model = WorkbenchModel.Of(WorkbenchSamples.Counter());
            History history = model.Session.History;

            var swallowed = new List<long>();
            for (int i = 0; i < history.TransitionCount; i++)
                if (history.TransitionAt(i).Outcome == TransitionOutcome.Swallowed)
                    swallowed.Add(history.TransitionAt(i).EndedAt);
            CollectionAssert.AreEqual(new long[] { 19, 35 }, swallowed);

            int d2 = model.Layout.NetNamed("d2");
            foreach (double time in new[] { 19.5, 35.5 })
            {
                model.Seek(time);
                Assert.IsTrue(model.Markers.Any(m => m.Net == d2 && m.Swallowed), $"a change dying on d2 at {time}");
            }
        }

        [Test]
        public void TheCounters_Rows_AreItsPortsTopToBottom()
        {
            WorkbenchModel model = WorkbenchModel.Of(WorkbenchSamples.Counter());
            CollectionAssert.AreEqual(new[] { "count[2:0]", "clk", "rst" }, model.Layout.Rows.Select(r => r.Label));
        }

        // -----------------------------------------------------------------
        // Laid out to be read
        // -----------------------------------------------------------------

        /// <summary>Where the one wire crossing another in each sample is: see <see cref="WorkbenchSamples"/>.</summary>
        private static readonly Dictionary<string, int> Crossings = new Dictionary<string, int>
        {
            ["Reference"] = 0,
            ["Full adder"] = 1,   // h1's carry, on its way down to the OR, over cin on its way to h2
            ["Counter"] = 1,      // q0, on its way up to the AND, over q1 on its way to its XOR
            ["Four half adders"] = 0,
        };

        [TestCaseSource(nameof(Names))]
        public void ASample_HasItsInputsOnTheLeft_AndItsOutputsOnTheRight(string name)
        {
            var drawn = new Drawn(Build(name));
            double inputs = double.MinValue, outputs = double.MaxValue, left = double.MaxValue, right = double.MinValue;

            foreach (Part part in drawn.Module.Parts)
            {
                PartOutline outline = drawn.Outlines[part.Id];
                if (part is PortPart port)
                {
                    if (port.Direction == SystemVerilog.Syntax.PortDirection.Input)
                        inputs = Math.Max(inputs, outline.Right);
                    else
                        outputs = Math.Min(outputs, outline.Left);
                    continue;
                }

                left = Math.Min(left, outline.Left);
                right = Math.Max(right, outline.Right);
            }

            Assert.Less(inputs, left, "every input is left of every part");
            Assert.Greater(outputs, right, "every output is right of every part");
        }

        [TestCaseSource(nameof(Names))]
        public void ASample_RunsEveryWireAtRightAngles(string name)
        {
            var drawn = new Drawn(Build(name));
            foreach ((int wire, BoardPoint a, BoardPoint b) in drawn.Segments)
                Assert.IsTrue(Math.Abs(a.X - b.X) < Epsilon || Math.Abs(a.Y - b.Y) < Epsilon, $"wire {wire} runs from {a} to {b} on the slant");
        }

        [TestCaseSource(nameof(Names))]
        public void NoWire_RunsThroughAPart_OrTheWordsRoundOne(string name)
        {
            var drawn = new Drawn(Build(name));

            foreach ((int wire, BoardPoint a, BoardPoint b) in drawn.Segments)
            {
                Wire w = drawn.Module.FindWire(wire);
                foreach (Part part in drawn.Module.Parts)
                {
                    // Clear of a part by a tenth of a cell; one the wire ends on, at its pin, only clear of its inside.
                    bool own = w.Touches(part.Id);
                    Box outline = Box.Of(drawn.Outlines[part.Id]).Grown(own ? -Epsilon : Clearance);
                    Assert.IsFalse(outline.Meets(a, b), $"wire {wire}, from {a} to {b}, runs through {part.Name}");
                }

                foreach ((string what, Box box) in drawn.Words)
                    Assert.IsFalse(box.Meets(a, b), $"wire {wire}, from {a} to {b}, runs through {what}");
            }
        }

        [TestCaseSource(nameof(Names))]
        public void NoTwoParts_NorTheWordsRoundThem_Overlap(string name)
        {
            var drawn = new Drawn(Build(name));
            Part[] parts = drawn.Module.Parts.ToArray();

            for (int i = 0; i < parts.Length; i++)
                for (int j = i + 1; j < parts.Length; j++)
                    Assert.IsFalse(Box.Of(drawn.Outlines[parts[i].Id]).Overlaps(Box.Of(drawn.Outlines[parts[j].Id])), $"{parts[i].Name} and {parts[j].Name} overlap");

            for (int i = 0; i < drawn.Words.Count; i++)
            {
                (string what, Box box) = drawn.Words[i];
                foreach (Part part in parts)
                    if (drawn.WordsOf[i] != part.Id)
                        Assert.IsFalse(box.Overlaps(Box.Of(drawn.Outlines[part.Id])), $"{what} runs into {part.Name}");
                for (int j = i + 1; j < drawn.Words.Count; j++)
                    Assert.IsFalse(box.Overlaps(drawn.Words[j].Box), $"{what} runs into {drawn.Words[j].What}");
            }
        }

        /// <summary>
        /// Two nets' wires are never nearer than a quarter of a cell -- running along one another, or
        /// one ending on the other as if joined -- unless they cross square, away from either's ends,
        /// which reads as a crossing and nothing else. Each sample crosses only where it must.
        /// </summary>
        [TestCaseSource(nameof(Names))]
        public void NoTwoNets_RunAlongOrTouchEachOther_AndTheyCrossOnlyWhereTheyMust(string name)
        {
            var drawn = new Drawn(Build(name));
            var segments = drawn.Segments.ToArray();
            int crossings = 0;

            for (int i = 0; i < segments.Length; i++)
            {
                for (int j = i + 1; j < segments.Length; j++)
                {
                    (int wi, BoardPoint a, BoardPoint b) = segments[i];
                    (int wj, BoardPoint c, BoardPoint d) = segments[j];
                    if (drawn.NetOfWire[wi] == drawn.NetOfWire[wj])
                        continue;

                    string what = $"wire {wi} ({a} to {b}) and wire {wj} ({c} to {d})";
                    if (Crosses(a, b, c, d))
                    {
                        crossings++;
                        continue;
                    }

                    Assert.GreaterOrEqual(Distance(a, b, c, d), MinimumGap - Epsilon, $"{what} come within a quarter of a cell");
                }
            }

            Assert.AreEqual(Crossings[name], crossings, "wires crossing each other");
        }

        [TestCaseSource(nameof(Names))]
        public void ASample_IsNoTallerThanTheReference_AndNotAQuarterWider(string name)
        {
            Vec2 reference = WorkbenchModel.Of(ReferenceCircuit.Document()).Layout.Size;
            Vec2 size = WorkbenchModel.Of(Build(name)).Layout.Size;

            Assert.LessOrEqual(size.y, reference.y, "a taller stage scrolls the page on a short window");
            Assert.LessOrEqual(size.x, 1.25f * reference.x, "a wider one draws its words smaller on a narrow window");
        }

        // -----------------------------------------------------------------
        // Geometry
        // -----------------------------------------------------------------

        private const double Epsilon = 1e-6;

        /// <summary>How far a wire keeps from a part it does not end on.</summary>
        private const double Clearance = 0.1;

        /// <summary>The nearest two nets' wires come, unless they cross.</summary>
        private const double MinimumGap = 0.25;

        /// <summary>A Micro line, at 12 units in 48 a cell: its height, and a character's width.</summary>
        private const double LineHeight = 0.3, CharacterWidth = 0.14;

        /// <summary>How far above a part's outline the view centres its name: 9 units.</summary>
        private const double NameLift = 9.0 / 48;

        /// <summary>A rectangle in cells, y up.</summary>
        private readonly struct Box
        {
            public Box(double left, double bottom, double right, double top)
            {
                Left = left;
                Bottom = bottom;
                Right = right;
                Top = top;
            }

            public readonly double Left, Bottom, Right, Top;

            public static Box Of(PartOutline outline) => new Box(outline.Left, outline.Bottom, outline.Right, outline.Top);

            public static Box Around(double x, double y, double halfWidth, double halfHeight) => new Box(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight);

            public Box Grown(double by) => new Box(Left - by, Bottom - by, Right + by, Top + by);

            public bool Overlaps(Box other) => Left < other.Right && other.Left < Right && Bottom < other.Top && other.Bottom < Top;

            /// <summary>Whether a segment at right angles gets inside: its edge alone does not count.</summary>
            public bool Meets(BoardPoint a, BoardPoint b)
            {
                double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
                if (x1 - x0 < Epsilon)
                    return Left < x0 && x0 < Right && Math.Max(y0, Bottom) < Math.Min(y1, Top);
                if (y1 - y0 < Epsilon)
                    return Bottom < y0 && y0 < Top && Math.Max(x0, Left) < Math.Min(x1, Right);
                throw new ArgumentException($"{a} to {b} is on the slant");
            }

            public override string ToString() => $"[{Left}, {Bottom}] to [{Right}, {Top}]";
        }

        /// <summary>A sample's top module as the board's geometry has it: outlines, wires cut into segments, each wire's net, and the words round each part.</summary>
        private sealed class Drawn
        {
            public Drawn(Project project)
            {
                Module = project.TopModule;
                foreach (Part part in Module.Parts)
                    Outlines[part.Id] = PartGeometry.Placed(project, part, Module.Layout.PlacementOf(part.Id));

                foreach (KeyValuePair<int, IReadOnlyList<BoardPoint>> wire in WireGeometry.Wires(project, Module))
                    for (int i = 1; i < wire.Value.Count; i++)
                        Segments.Add((wire.Key, wire.Value[i - 1], wire.Value[i]));

                Netlist netlist = Netlist.Of(project, Module);
                foreach (Net net in netlist.Nets)
                    foreach (int wire in net.Wires)
                        NetOfWire[wire] = net;

                foreach (Part part in Module.Parts)
                {
                    if (part is PortPart)
                        continue;
                    Placement at = Module.Layout.PlacementOf(part.Id);
                    PartOutline outline = Outlines[part.Id];
                    PartSymbol symbol = DocumentSchematic.SymbolOf(part);

                    // A name over the part: over a triangle's back, which is lower than its outline;
                    // otherwise just over the outline, both as WorkbenchPanel places it.
                    if (PartLayout.NameAbove(symbol))
                    {
                        string name = DocumentSchematic.NameOf(part);
                        bool triangle = symbol == PartSymbol.Not || symbol == PartSymbol.Buffer || symbol == PartSymbol.TriBuffer || symbol == PartSymbol.TriInverter;
                        BoardPoint over = triangle
                            ? PartGeometry.Transform(at, new BoardPoint(-0.2, 0.32))
                            : new BoardPoint((outline.Left + outline.Right) / 2, outline.Top);
                        Word(part, "the name " + name, Box.Around(over.X, over.Y + NameLift, name.Length * CharacterWidth / 2, LineHeight / 2));
                    }

                    // A delay just past the output, over the wire leaving it, running away from the part.
                    string delay = DocumentSchematic.DelayOf(part);
                    if (delay.Length > 0)
                    {
                        BoardPoint from = PartGeometry.Transform(at, new BoardPoint(0.55, 0.3));
                        bool leftward = PartGeometry.Transform(at, new BoardPoint(1, 0)).X - PartGeometry.Transform(at, new BoardPoint(0, 0)).X < -0.5;
                        double width = delay.Length * CharacterWidth;
                        Word(part, $"{part.Name}'s delay", leftward
                            ? new Box(from.X - width, from.Y - LineHeight / 2, from.X, from.Y + LineHeight / 2)
                            : new Box(from.X, from.Y - LineHeight / 2, from.X + width, from.Y + LineHeight / 2));
                    }
                }
            }

            public Module Module { get; }
            public Dictionary<int, PartOutline> Outlines { get; } = new Dictionary<int, PartOutline>();
            public List<(int Wire, BoardPoint A, BoardPoint B)> Segments { get; } = new List<(int, BoardPoint, BoardPoint)>();
            public Dictionary<int, Net> NetOfWire { get; } = new Dictionary<int, Net>();

            /// <summary>The words round the parts, and which part each is round.</summary>
            public List<(string What, Box Box)> Words { get; } = new List<(string, Box)>();

            /// <inheritdoc cref="Words"/>
            public List<int> WordsOf { get; } = new List<int>();

            private void Word(Part part, string what, Box box)
            {
                Words.Add((what, box));
                WordsOf.Add(part.Id);
            }
        }

        /// <summary>Whether a horizontal and a vertical segment cross square, at least a quarter of a cell from each one's ends.</summary>
        private static bool Crosses(BoardPoint a, BoardPoint b, BoardPoint c, BoardPoint d)
        {
            bool firstLevel = Math.Abs(a.Y - b.Y) < Epsilon, secondLevel = Math.Abs(c.Y - d.Y) < Epsilon;
            if (firstLevel == secondLevel)
                return false;

            (BoardPoint h0, BoardPoint h1, BoardPoint v0, BoardPoint v1) = firstLevel ? (a, b, c, d) : (c, d, a, b);
            double x = v0.X, y = h0.Y;
            bool Inside(double value, double from, double to) => Math.Min(from, to) + MinimumGap - Epsilon <= value && value <= Math.Max(from, to) - MinimumGap + Epsilon;
            return Inside(x, h0.X, h1.X) && Inside(y, v0.Y, v1.Y);
        }

        /// <summary>The least distance between two segments at right angles to the grid.</summary>
        private static double Distance(BoardPoint a, BoardPoint b, BoardPoint c, BoardPoint d)
        {
            double Gap(double p0, double p1, double q0, double q1) =>
                Math.Max(0, Math.Max(Math.Min(p0, p1), Math.Min(q0, q1)) - Math.Min(Math.Max(p0, p1), Math.Max(q0, q1)));

            double dx = Gap(a.X, b.X, c.X, d.X), dy = Gap(a.Y, b.Y, c.Y, d.Y);
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
