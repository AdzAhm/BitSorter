using BitSorter.Board;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// PARTS in an exercise (Part 15d): a register placed starts where the document says, an exercise's start or x; only
    /// what it stocks is offered, no port, and each counted part says how many are left.
    /// </summary>
    public class ExercisePartsTests
    {
        [Test]
        public void AnExercise_OffersWhatItStocks_NoPort_AndHowManyAreLeft()
        {
            Exercise exercise = BitSorter.Board.Tests.ExerciseFiles.Load("half-adder");
            var editor = new BoardEditor(exercise.ToProject(new System.Collections.Generic.List<string>()));
            string top = editor.Project.Top;
            var offered = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(PartsCatalog.For(editor.Project, top), e => e.Key));
            CollectionAssert.Contains(offered, "xor");
            CollectionAssert.Contains(offered, "and");
            CollectionAssert.DoesNotContain(offered, "or", "not stocked");
            CollectionAssert.DoesNotContain(offered, "register", "no register stocked");
            CollectionAssert.DoesNotContain(offered, "input", "its ports are its test's");

            CatalogEntry xor = PartsCatalog.Find(editor.Project, top, "xor");
            Assert.AreEqual(1, PartsCatalog.Left(editor.Project, xor));
            Assert.AreEqual("XOR 1", PartsCatalog.Caption(editor.Project, xor));
            Assert.IsTrue(PartsCatalog.Place(editor, top, xor, new Placement(8, 0)).Done);
            Assert.AreEqual("XOR 0", PartsCatalog.Caption(editor.Project, xor), "none left once it is placed");

            Project own = Project.New("mine");
            CatalogEntry free = PartsCatalog.Find(own, "mine", "xor");
            Assert.IsNull(PartsCatalog.Left(own, free), "no library, no count");
            Assert.AreEqual("XOR", PartsCatalog.Caption(own, free));
            Assert.IsNotNull(PartsCatalog.Find(own, "mine", "input"), "a circuit of the student's own has its ports to place");
        }

        [Test]
        public void ARegisterPlaced_StartsWhereTheDocumentSays()
        {
            Project project = Project.New("late");
            CatalogEntry register = PartsCatalog.Find(project, "late", "register");
            Assert.IsNotNull(register, "sanity: PARTS has a register");

            Assert.IsNull(((RegisterPart)PartsCatalog.Make(project, "late", register)).Initial, "x, in a circuit of the student's own");

            Project exercise = project with { Settings = project.Settings with { RegisterStart = LogicVec.FromBinary("0") } };
            Assert.AreEqual("0", ((RegisterPart)PartsCatalog.Make(exercise, "late", register)).Initial?.Format(Radix.Binary),
                "an exercise's start, as every 4.x register started at 0");
        }
    }
}
