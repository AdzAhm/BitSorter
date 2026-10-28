using BitSorter.Board;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>A register placed from PARTS starts where its document says (Part 15d): an exercise's start, or x.</summary>
    public class ExercisePartsTests
    {
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
