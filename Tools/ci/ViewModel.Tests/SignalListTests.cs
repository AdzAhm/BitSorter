using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Finding a signal and arranging the rows (Part 9b-2b): <see cref="SignalIndex"/>'s tree, keys,
    /// names and search, and <see cref="WaveList"/>'s rows -- adding, grouping, selecting, renaming,
    /// radix, and finding every row again in a design built again.
    /// </summary>
    public class SignalListTests
    {
        private static WorkbenchModel FullAdder() => WorkbenchModel.Of(WorkbenchSamples.FullAdder());

        private static int Find(SignalIndex index, string key)
        {
            Assert.IsTrue(index.TryFind(key, out int signal), $"the index has no '{key}'");
            return signal;
        }

        private static List<string> Keys(SignalIndex index, string query, bool cells = false)
        {
            var hits = new List<SignalHit>();
            index.Search(query, hits, cells);
            return hits.Select(h => h.Key).ToList();
        }

        private const string Adder = @"
module half_adder (input logic a, b, output logic s, c);
    assign s = a ^ b;
    assign c = a & b;
endmodule

module fa (input logic a, b, cin, output logic s, cout);
    logic s1, c1, c2;
    half_adder h1 (.a(a), .b(b), .s(s1), .c(c1));
    half_adder h2 (.a(s1), .b(cin), .s(s), .c(c2));
    assign cout = c1 | c2;
endmodule

module TB;
    logic a, b, cin, s, cout;
    integer count;
    fa dut (.a(a), .b(b), .cin(cin), .s(s), .cout(cout));
    initial begin
        for (int i = 0; i < 8; i++) begin
            {a, b, cin} = i;
            #5;
        end
        $finish;
    end
endmodule
";

        private static SignalIndex IndexOf(string source, string top)
        {
            SyntaxTree tree = SyntaxReader.ReadText(source, "test.sv", null);
            Assert.IsEmpty(tree.Diagnostics, string.Join("; ", tree.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(tree, top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));
            return SignalIndex.Of(elaboration.Design, top + ".dut", null);
        }

        // -----------------------------------------------------------------
        // The index
        // -----------------------------------------------------------------

        [Test]
        public void TheTree_RunsFromTheTestbench_ThroughTheDesign_ToItsBlocksAndCells()
        {
            SignalIndex index = FullAdder().Signals;
            ScopeNode tb = index.Root;
            Assert.AreEqual(ScopeNodeKind.Testbench, tb.Kind);

            ScopeNode dut = tb.Children.Single();
            Assert.AreEqual(ScopeNodeKind.Design, dut.Kind);
            Assert.AreEqual(index.DesignPath, dut.Path);

            CollectionAssert.AreEqual(new[] { "h1", "h2", "g1" }, dut.Children.Select(c => c.Name));
            Assert.AreEqual(ScopeNodeKind.Block, dut.Children[0].Kind, "a half adder is a block of the board's own");
            Assert.AreEqual("half_adder", dut.Children[0].Module);
            Assert.AreEqual(ScopeNodeKind.Cell, dut.Children[2].Kind, "the OR prints as a library cell");
            Assert.IsTrue(dut.Children[0].Children.All(c => c.Kind == ScopeNodeKind.Cell), "and so do the half adder's gates");
            Assert.IsTrue(dut.Children[0].Children[0].InCell);
            Assert.IsFalse(dut.Children[0].InCell);

            CollectionAssert.AreEqual(new[] { "a", "b", "s", "c" }, dut.Children[0].Signals.Select(index.KeyOf).Select(k => k.Substring(3)),
                "h1 declares its four ports, in order");
        }

        [Test]
        public void AKey_IsWhereTheSignalIsBelowTheDesign_AndATestbenchsOwnIsUnderTb()
        {
            SignalIndex index = FullAdder().Signals;
            int inner = Find(index, "h1.s");
            Assert.AreEqual(index.DesignPath + ".h1.s", FullAdder().Session.Design.Signals[inner].Name);
            Assert.AreEqual("h1.s", index.ShortName(inner));
            Assert.AreEqual("/" + index.DesignPath.Replace('.', '/') + "/h1/s", index.FullName(inner), "as ModelSim writes it");

            int tbCin = Find(index, SignalIndex.TestbenchPrefix + "cin");
            Assert.AreEqual(index.Root.Path + ".cin", FullAdder().Session.Design.Signals[tbCin].Name);
            Assert.IsFalse(index.TryFind("nothing.here", out _));
        }

        [Test]
        public void ABusName_CarriesItsRangeAsDeclared()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            SignalIndex index = model.Signals;
            Assert.AreEqual("s[3:0]", index.ShortName(Find(index, "s")));
            Assert.AreEqual("a", index.ShortName(Find(index, "a")));
            StringAssert.EndsWith("/dut/out[3:0]", index.FullName(Find(index, "out")));
        }

        [Test]
        public void ACellsPins_AreListedOnlyWhenAskedFor()
        {
            SignalIndex index = FullAdder().Signals;
            int pin = Find(index, "h1.g1.Z");
            Assert.IsFalse(index.IsListed(pin), "a cell's pin is hidden");
            Assert.IsTrue(index.IsListed(pin, includeCells: true));
            CollectionAssert.DoesNotContain(Keys(index, "Z"), "h1.g1.Z");
            CollectionAssert.Contains(Keys(index, "Z", cells: true), "h1.g1.Z");
        }

        [Test]
        public void ASearch_RanksALastNameThatIsTheTermFirst_ThenTheShallower()
        {
            SignalIndex index = FullAdder().Signals;
            List<string> s = Keys(index, "s");

            // Every key that is s, then those whose last name starts with s, then any holding an s.
            CollectionAssert.AreEqual(new[] { "s", "h1.s", "h2.s", "tb.s" }, s.Take(4), "the s's first, the shallower first");
            Assert.IsTrue(s.Contains("h1_s"), "h1_s holds an s");
            Assert.Greater(s.IndexOf("h1_s"), 3);

            CollectionAssert.AreEqual(new[] { "h1.a", "h1.b", "h1.s", "h1.c" }, Keys(index, "h1.").OrderBy(k => k == "h1.a" ? 0 : k == "h1.b" ? 1 : k == "h1.s" ? 2 : 3),
                "every signal of h1, and nothing of its cells");
            Assert.AreEqual(4, Keys(index, "h1.").Count);
        }

        [Test]
        public void ASearch_TakesEveryTerm_IgnoresCase_AndReadsAStarAsAnyRun()
        {
            SignalIndex index = FullAdder().Signals;
            CollectionAssert.AreEquivalent(new[] { "h1.c", "h2.c" }, Keys(index, "H* c").Where(k => k.Contains('.')).ToList(), "two terms, both kept, case ignored");
            CollectionAssert.AreEquivalent(new[] { "h1.s", "h2.s" }, Keys(index, "h?.s".Replace('?', '*')));
            CollectionAssert.AreEquivalent(new[] { "cout", "tb.cout" }, Keys(index, "*out"));
            Assert.IsEmpty(Keys(index, "nothing"));
            Assert.AreEqual(Keys(index, "").Count, index.Count - Enumerable.Range(0, index.Count).Count(i => !index.IsListed(i)),
                "no terms, every listed signal");

            Assert.IsTrue(SignalIndex.Glob("a*b*c", "aXbYc"));
            Assert.IsTrue(SignalIndex.Glob("*", ""));
            Assert.IsFalse(SignalIndex.Glob("a*b", "acd"));
            Assert.IsTrue(SignalIndex.Glob("**x", "abx"));
        }

        [Test]
        public void AWorkingVariable_AndAMemorysWords_AreNeverListed()
        {
            SignalIndex index = IndexOf(Adder, "TB");
            Assert.IsFalse(Keys(index, "").Any(k => k.EndsWith(".i") || k == "tb.i"), "a for loop's int i is a working variable");
            Assert.IsTrue(index.TryFind("tb.count", out _), "but a variable the testbench declares is a signal");
        }

        // -----------------------------------------------------------------
        // The list
        // -----------------------------------------------------------------

        private static WaveList Ports(WorkbenchModel model) =>
            new WaveList(model.Signals, model.Layout.Rows.Select(r => model.Signals.KeyOf(model.Session.Design.TryFind(r.Signal, out SignalId id) ? id.Index : -1)));

        [Test]
        public void TheDefaultList_IsTheLayoutsPorts_NamedAsTheLayoutNamesThem()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            WaveList list = Ports(model);
            CollectionAssert.AreEqual(model.Layout.Rows.Select(r => r.Label), list.Lines.Select(list.LabelOf),
                "the short names are the rows' own labels, ranges and all");
            Assert.IsTrue(list.Lines.All(l => !l.IsHeader && l.Signal.Signal >= 0));

            var signals = new List<int>();
            var radixes = new List<WaveRadix>();
            list.Rows(signals, radixes, model.DefaultRadix);
            CollectionAssert.AreEqual(Enumerable.Range(0, model.RowCount).Select(model.RowSignal), signals, "the model's default rows, one for one");
        }

        [Test]
        public void TheRows_FollowThePorts_APortPlacedGainsOne_AndOneTakenAwayLosesIt()
        {
            WorkbenchModel model = FullAdder();
            WaveList list = Ports(model);
            string[] ports = list.AllSignals.Select(s => s.Key).ToArray();
            int inner = Find(model.Signals, "h1.s");
            list.Add(inner);
            list.ClearSelection();
            list.Select(0);
            int version = list.Version;

            // cin taken away, and a port called x placed: the inner row the student added stays.
            string[] now = ports.Where(k => k != "cin").Concat(new[] { "x" }).ToArray();
            Assert.IsTrue(list.FollowPorts(ports, now));

            string[] keys = list.AllSignals.Select(s => s.Key).ToArray();
            CollectionAssert.DoesNotContain(keys, "cin");
            Assert.AreEqual("x", keys.Last(), "the new port's row at the end");
            CollectionAssert.Contains(keys, model.Signals.KeyOf(inner));
            Assert.AreEqual(-1, list.AllSignals.Last().Signal, "x is no signal of this run until its replay takes over");
            Assert.IsTrue(list.Lines[0].Selected, "a row selected stays selected");
            Assert.Greater(list.Version, version);
            Assert.IsFalse(list.FollowPorts(now, now), "no change, no change");
        }

        [Test]
        public void AddingASignal_PutsItAtTheEnd_AndAddingItAgainSelectsItWhereItIs()
        {
            WorkbenchModel model = FullAdder();
            WaveList list = Ports(model);
            int count = list.Lines.Count;
            int inner = Find(model.Signals, "h1.s");

            int line = list.Add(inner);
            Assert.AreEqual(count, line);
            Assert.AreEqual("h1.s", list.LabelOf(list.Lines[line]));
            Assert.IsTrue(list.Lines[line].Selected, "selected alone");
            Assert.AreEqual(1, list.Lines.Count(l => l.Selected));

            int a = Find(model.Signals, "a");
            Assert.AreEqual(0, list.Add(a), "a is the first row already");
            Assert.AreEqual(count + 1, list.Lines.Count, "and is not added twice");
            Assert.IsTrue(list.Lines[0].Selected && !list.Lines[line].Selected);

            Assert.AreEqual(-1, list.Add(-1));
        }

        [Test]
        public void AGroup_TakesTheSelected_AShutOneHidesItsRows_AndUngroupingPutsThemBack()
        {
            WorkbenchModel model = FullAdder();
            WaveList list = Ports(model);
            list.Add(Find(model.Signals, "h1.s"));
            list.Add(Find(model.Signals, "h1.c"));
            list.Add(Find(model.Signals, "h2.c"));
            int first = list.Lines.ToList().FindIndex(l => l.Signal?.Key == "h1.s");
            list.Select(first);
            list.Select(first + 2, extend: true);
            Assert.AreEqual(3, list.SelectedSignals().Count);

            int version = list.Version;
            WaveGroup group = list.GroupSelected("carries");
            Assert.Greater(list.Version, version);
            Assert.AreEqual("carries", group.Name);
            CollectionAssert.AreEqual(new[] { "h1.s", "h1.c", "h2.c" }, group.Signals.Select(s => s.Key));

            int header = list.Lines.ToList().FindIndex(l => l.IsHeader);
            Assert.AreEqual(first, header, "the group stands where the first of them was");
            Assert.AreEqual("carries", list.LabelOf(list.Lines[header]));
            Assert.IsTrue(list.Lines.Skip(header).Take(4).All(l => l.Selected), "the group is selected, its rows with it");

            list.Toggle(group);
            Assert.AreEqual(header + 1, list.Lines.Count, "shut, its rows leave the lines");
            var signals = new List<int>();
            var radixes = new List<WaveRadix>();
            list.Rows(signals, radixes, model.DefaultRadix);
            Assert.AreEqual(-1, signals[header], "a header is a row with no signal");

            list.Toggle(group);
            list.Ungroup(group);
            Assert.IsFalse(list.Lines.Any(l => l.IsHeader));
            CollectionAssert.AreEqual(new[] { "h1.s", "h1.c", "h2.c" }, list.Lines.Skip(first).Select(l => l.Signal.Key));
        }

        [Test]
        public void Selecting_Alone_ExtendingAndToggling()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            WaveList list = Ports(model);
            list.Select(1);
            list.Select(4, extend: true);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, Selected(list));
            list.Select(2, toggle: true);
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, Selected(list));
            list.Select(6);
            CollectionAssert.AreEqual(new[] { 6 }, Selected(list));
            list.ClearSelection();
            Assert.IsFalse(list.AnySelected);
        }

        private static int[] Selected(WaveList list) => Enumerable.Range(0, list.Lines.Count).Where(i => list.Lines[i].Selected).ToArray();

        [Test]
        public void ARadix_IsSetOnTheSelected_AndAGroupsHeaderStandsForAllOfIt()
        {
            WorkbenchModel model = FullAdder();
            WaveList list = Ports(model);
            list.Select(0);
            list.SetRadix(WaveRadix.Unsigned);
            Assert.AreEqual(WaveRadix.Unsigned, list.Lines[0].Signal.Radix);
            Assert.IsNull(list.Lines[1].Signal.Radix, "the rest keep their own");

            list.Select(1);
            list.Select(2, extend: true);
            WaveGroup group = list.GroupSelected("in");
            list.SetRadix(WaveRadix.Signed);
            Assert.IsTrue(group.Signals.All(s => s.Radix == WaveRadix.Signed), "the header selected, the radix is all of it");

            var signals = new List<int>();
            var radixes = new List<WaveRadix>();
            list.Rows(signals, radixes, model.DefaultRadix);
            Assert.AreEqual(WaveRadix.Unsigned, radixes[0]);
            Assert.AreEqual(WaveRadix.Signed, radixes[2]);

            list.SetRadix(null);
            Assert.IsTrue(group.Signals.All(s => s.Radix == null), "null puts each back to its default");
        }

        [Test]
        public void Renaming_ARowAndAGroup_AndFullNames()
        {
            WorkbenchModel model = FullAdder();
            WaveList list = Ports(model);
            list.Rename(0, "  first  ");
            Assert.AreEqual("first", list.LabelOf(list.Lines[0]), "a name of its own, trimmed");
            list.Rename(0, "");
            Assert.AreEqual("a", list.LabelOf(list.Lines[0]), "empty puts its own back");

            list.Names = WaveNames.Full;
            StringAssert.EndsWith("/dut/a", list.LabelOf(list.Lines[0]));
            StringAssert.StartsWith("/", list.LabelOf(list.Lines[0]));

            list.Select(0);
            WaveGroup group = list.GroupSelected("");
            Assert.AreEqual("group", group.Name, "a group with no name is called group");
            list.Rename(0, "inputs");
            Assert.AreEqual("inputs", group.Name);
            list.Rename(0, " ");
            Assert.AreEqual("inputs", group.Name, "a group always has a name");
        }

        [Test]
        public void Removing_TakesTheSelected_AndAGroupWithItsRows()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            WaveList list = Ports(model);
            int count = list.Lines.Count;
            Assert.IsFalse(list.RemoveSelected(), "nothing selected, nothing goes");

            list.Select(0);
            list.Select(1, extend: true);
            list.GroupSelected("pair");
            list.Select(3, toggle: true);
            Assert.IsTrue(list.RemoveSelected());
            Assert.AreEqual(count - 3, list.Lines.Count, "the group, its two rows, and the one toggled in");
            Assert.IsFalse(list.Lines.Any(l => l.IsHeader));
        }

        [Test]
        public void Moving_ARowWithinItsLevel()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            WaveList list = Ports(model);
            string[] before = list.Lines.Select(l => l.Signal.Key).ToArray();
            list.Move(0, 2);
            CollectionAssert.AreEqual(new[] { before[1], before[2], before[0] }, list.Lines.Take(3).Select(l => l.Signal.Key));
            list.Move(2, 0);
            CollectionAssert.AreEqual(before, list.Lines.Select(l => l.Signal.Key));
        }

        [Test]
        public void EveryRow_IsFoundAgain_InADesignBuiltAgain_UnderAnotherTestbench()
        {
            SignalIndex first = IndexOf(Adder, "TB");
            string renamed = Adder.Replace("module TB;", "module Bench;");
            SignalIndex second = IndexOf(renamed, "Bench");
            Assert.AreNotEqual(first.DesignPath, second.DesignPath, "sanity: the design path moved");

            var list = new WaveList(first, new[] { "a", "h1.s", "tb.cin" });
            Assert.IsTrue(list.AllSignals.All(s => s.Signal >= 0));

            list.Resolve(second);
            foreach (WaveSignal row in list.AllSignals)
            {
                Assert.GreaterOrEqual(row.Signal, 0, $"{row.Key} was found again");
                Assert.AreEqual(row.Key, second.KeyOf(row.Signal));
            }

            // A design without half adders: h1.s has gone, and its row stays, showing nothing.
            string flat = Adder.Replace("half_adder h1 (.a(a), .b(b), .s(s1), .c(c1));", "assign s1 = a ^ b; assign c1 = a & b;");
            list.Resolve(IndexOf(flat, "TB"));
            WaveSignal gone = list.Find("h1.s");
            Assert.AreEqual(-1, gone.Signal);
            Assert.AreEqual(1, list.LineOf(gone), "kept in its place");
            Assert.AreEqual("h1.s", list.LabelOf(list.Lines[1]), "named by its key");
        }
    }
}
