using System.Linq;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The student's circuits (Part 15a, <see cref="Documents"/>): which documents of the save are circuits, what each is
    /// called, the names a circuit may have, and the order OPEN lists them in.
    /// </summary>
    public class DocumentsTests
    {
        private static readonly string[] Saved =
        {
            WorkbenchSamples.MyCircuitKey, "half-adder", "doc/adder", "free-play/My board", "doc/alu",
        };

        [Test]
        public void TheCircuits_AreMyCircuit_OnesMadeOrImported_AndOldFreePlayBoards_NotALevelsBoard()
        {
            CollectionAssert.AreEqual(new[] { true, false, true, true, true }, Saved.Select(Documents.IsDocument));
            Assert.IsFalse(Documents.IsDocument("doc/"), "no name, no circuit");
            Assert.IsFalse(Documents.IsOwn(WorkbenchSamples.MyCircuitKey), "MY CIRCUIT is neither renamed nor deleted");
            Assert.IsTrue(Documents.IsOwn("free-play/My board"));
            CollectionAssert.AreEqual(new[] { "my circuit", "adder", "My board" },
                new[] { WorkbenchSamples.MyCircuitKey, "doc/adder", "free-play/My board" }.Select(Documents.NameOf));
        }

        [Test]
        public void AName_IsSomethingAndNotTooLong_OfTheLettersANameTakes_AndNoOtherCircuits()
        {
            Assert.IsNull(Documents.WhyNotName("my ALU-2_v1.0", Saved));
            StringAssert.Contains("needs a name", Documents.WhyNotName("   ", Saved));
            StringAssert.Contains("at most 32", Documents.WhyNotName(new string('a', 33), Saved));
            StringAssert.Contains("not '/'", Documents.WhyNotName("a/b", Saved));
            StringAssert.Contains("called adder already", Documents.WhyNotName("ADDER", Saved), "by any case");
            StringAssert.Contains("called my circuit already", Documents.WhyNotName("My Circuit", Saved));
            StringAssert.Contains("called My board already", Documents.WhyNotName("my board", Saved), "an old free-play board's name is taken too");
            Assert.IsNull(Documents.WhyNotName("Adder", Saved, except: "doc/adder"), "renamed to itself in another case");
            Assert.IsNull(Documents.WhyNotName("half-adder", Saved), "a level's board is no circuit, so its name is free");
        }

        [Test]
        public void AFreeName_IsCircuitAndANumber_OrAnImportsOwnNameWithANumberAfterIt()
        {
            Assert.AreEqual("circuit1", Documents.FreeName(Saved));
            Assert.AreEqual("circuit2", Documents.FreeName(Saved.Append("doc/circuit1")));
            Assert.AreEqual("cpu", Documents.FreeName(Saved, "cpu"));
            Assert.AreEqual("alu 2", Documents.FreeName(Saved, "alu"));
            Assert.AreEqual("alu 3", Documents.FreeName(Saved.Append("doc/alu 2"), "alu"));
            Assert.AreEqual("mycpu", Documents.FreeName(Saved, "my/cpu!"), "what a name may not hold is left out");
            Assert.AreEqual("circuit1", Documents.FreeName(Saved, "///"), "nothing left: a new circuit's name");
            Assert.AreEqual(32, Documents.FreeName(new[] { "doc/" + new string('a', 32) }, new string('a', 40)).Length, "cut to length, number and all");
        }

        [Test]
        public void OpenListsTheMostRecentFirst_ThenTheRestAsKept_AndRecentStaysShort()
        {
            string[] recent = { "doc/alu", "gone", WorkbenchSamples.MyCircuitKey };
            CollectionAssert.AreEqual(new[] { "doc/alu", WorkbenchSamples.MyCircuitKey, "doc/adder", "free-play/My board" },
                Documents.InOrder(Saved, recent), "a key no longer kept is left out, and a level's board is no circuit");

            var touched = Documents.Touched(recent, WorkbenchSamples.MyCircuitKey);
            CollectionAssert.AreEqual(new[] { WorkbenchSamples.MyCircuitKey, "doc/alu", "gone" }, touched);
            var full = Enumerable.Range(0, 20).Select(i => "doc/c" + i).ToList();
            Assert.AreEqual(Documents.MostRecent, Documents.Touched(full, "doc/new").Count);
        }

        [Test]
        public void ANewCircuitsModule_IsItsNameAsSystemVerilogTakesOne()
        {
            Assert.AreEqual("my_ALU_2", Documents.ModuleNameFor("my ALU 2"));
            Assert.AreEqual("circuit_4bit", Documents.ModuleNameFor("4bit"));
        }
    }
}
