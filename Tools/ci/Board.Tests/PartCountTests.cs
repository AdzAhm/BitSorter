using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The parts an exercise stocks (Part 15b, <see cref="GateLibrary.Counts"/>): a gate past its kind's count is refused, in
    /// words, where the gate budget only warns; a block with a count is counted by its copies and not by its insides, and one
    /// without is counted through; and the counts are saved.
    /// </summary>
    public class PartCountTests
    {
        private static GateLibrary Stocking(params PartCount[] counts) => GateLibrary.CourseCells(1) with { Counts = counts };

        [Test]
        public void AGatePastItsCount_IsRefused_InWords_AndOneUpToItIsNot()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            Assert.IsTrue(editor.SetLibrary(Stocking(new PartCount("xor", 1), new PartCount("and", 2))).Done, "the half adder has one of each");
            string top = editor.Project.Top;

            EditResult second = editor.Add(top, new GatePart { Kind = GateKind.And, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(40, 0));
            Assert.IsTrue(second.Done, "a second AND: the exercise stocks two");
            EditResult third = editor.Add(top, new GatePart { Kind = GateKind.And, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(40, 8));
            Assert.IsFalse(third.Done);
            StringAssert.Contains("The exercise stocks 2 AND, and the design has 3.", third.Refusal);

            EditResult xor = editor.Add(top, new GatePart { Kind = GateKind.Xor, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(40, 16));
            StringAssert.Contains("stocks 1 XOR", xor.Refusal);
            EditResult or = editor.Add(top, new GatePart { Kind = GateKind.Or, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(40, 24));
            Assert.IsTrue(or.Done, "a kind with no count has none");
        }

        [Test]
        public void ACountOfNone_IsSaidSo()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            EditResult set = editor.SetLibrary(Stocking(new PartCount("xor", 0)));
            Assert.IsFalse(set.Done);
            StringAssert.Contains("The exercise stocks no XOR, and the design has 1.", set.Refusal);
        }

        /// <summary>The full adder: two half-adder blocks and an OR, each half adder an XOR and an AND.</summary>
        [Test]
        public void ABlock_IsCountedByItsCopies_ItsInsidesOnlyWhenItHasNoCount()
        {
            Project fullAdder = SampleBoards.Build("full_adder");
            Dictionary<string, int> through = PartCounts.Of(fullAdder);
            Assert.AreEqual(2, through["half_adder"]);
            Assert.AreEqual(2, through["xor"], "counted through each instance");
            Assert.AreEqual(2, through["and"]);
            Assert.AreEqual(1, through["or"]);

            Dictionary<string, int> whole = PartCounts.Of(fullAdder, new HashSet<string> { "half_adder" });
            Assert.AreEqual(2, whole["half_adder"]);
            Assert.IsFalse(whole.ContainsKey("xor"), "a block with a count is the part: its insides are its own");

            Dictionary<string, int> four = PartCounts.Of(SampleBoards.Build("half_adders"));
            Assert.AreEqual(4, four["half_adder"], "an array of four is four");
            Assert.AreEqual(4, four["xor"]);
        }

        [Test]
        public void ABlockPastItsCount_IsRefused()
        {
            var editor = new BoardEditor(SampleBoards.Build("full_adder"));
            Assert.IsTrue(editor.SetLibrary(Stocking(new PartCount("half_adder", 2), new PartCount("xor", 0), new PartCount("and", 0))).Done,
                "the half adders' gates are theirs, and not counted against the stock");
            EditResult third = editor.Add(editor.Project.Top, new BlockPart { Module = "half_adder" }, new Placement(40, 0));
            Assert.IsFalse(third.Done);
            StringAssert.Contains("The exercise stocks 2 half_adder, and the design has 3.", third.Refusal);
        }

        [Test]
        public void TheCounts_AreSaved_AndAnOlderFileHasNone()
        {
            GateLibrary stocked = Stocking(new PartCount("xor", 1), new PartCount("fa", 4));
            var problems = new List<string>();
            GateLibrary back = LibraryRecord.From(stocked).ToLibrary(problems);
            Assert.IsEmpty(problems);
            Assert.IsTrue(GateLibrary.Alike(stocked, back), "the same library, counts and all");
            Assert.AreEqual(4, back.MostOf("fa"));
            Assert.AreEqual(-1, back.MostOf("or"), "no count");

            LibraryRecord older = JsonSerializer.Deserialize<LibraryRecord>("{\"name\":\"CUSTOM\",\"cells\":[]}", new JsonSerializerOptions { IncludeFields = true });
            Assert.IsEmpty(older.ToLibrary(problems).Counts);
            Assert.IsFalse(GateLibrary.Alike(stocked, stocked with { Counts = new[] { new PartCount("xor", 2) } }), "a count changed is another library");
        }
    }
}
