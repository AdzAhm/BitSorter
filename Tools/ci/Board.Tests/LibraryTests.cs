using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Gate-library mode (Part 14d): the course's preset is BitSorter's own cells; under a library the rules refuse a gate it
    /// has no cell for, a gate off its cell's delays, a MUX and -- where it takes none -- a constant, each in words; setting
    /// it gives every gate its cell's delays in one step; a design over its budget is never refused; and it is saved.
    /// </summary>
    public class LibraryTests
    {
        private static BoardEditor Under(Project project, GateLibrary library)
        {
            var editor = new BoardEditor(project);
            EditResult set = editor.SetLibrary(library);
            Assert.IsTrue(set.Done, set.Refusal);
            return editor;
        }

        [Test]
        public void TheCoursePreset_IsEveryCellBitSorterHas_AtTheDelayAsked()
        {
            GateLibrary course = GateLibrary.CourseCells(3);
            foreach (GateKind kind in System.Enum.GetValues(typeof(GateKind)).Cast<GateKind>())
                for (int inputs = 1; inputs <= GatePart.MaxInputs; inputs++)
                {
                    bool has = CellLibrary.CellFor(kind, inputs) != null;
                    LibraryCell cell = course.CellFor(kind, inputs);
                    Assert.AreEqual(has, cell != null, $"{kind} of {inputs}");
                    if (cell != null)
                        Assert.AreEqual((3L, 3L), (cell.Rise, cell.Fall));
                }
            Assert.AreEqual(GateLibrary.CourseCellsName, course.Name);
            Assert.AreEqual(20, course.Cells.Count, "INV, BUF, and six gates of two to four inputs");
        }

        /// <summary>The full adder's gates rise in 1 and fall in 2; under the course's cells at 3 every one is 3 and 3, and undo puts them back in one step.</summary>
        [Test]
        public void SettingALibrary_GivesEveryGateItsCellsDelays_InOneStep()
        {
            BoardEditor editor = Under(SampleBoards.Build("full_adder"), GateLibrary.CourseCells(3));
            IEnumerable<GatePart> Gates() => editor.Project.Modules.SelectMany(m => m.Parts).OfType<GatePart>();
            Assert.That(Gates().Select(g => (g.Rise, g.Fall)), Is.All.EqualTo((3L, 3L)));

            // A delay changed in the table is every gate of the cell changed.
            GateLibrary library = editor.Project.Settings.Library;
            var cells = library.Cells.Select(c => c.Kind == GateKind.Xor && c.Inputs == 2 ? c with { Rise = 5, Fall = 4 } : c).ToList();
            Assert.IsTrue(editor.SetLibrary(library with { Name = GateLibrary.CustomName, Cells = cells }).Done);
            Assert.That(Gates().Where(g => g.Kind == GateKind.Xor).Select(g => (g.Rise, g.Fall)), Is.All.EqualTo((5L, 4L)));
            Assert.That(Gates().Where(g => g.Kind != GateKind.Xor).Select(g => (g.Rise, g.Fall)), Is.All.EqualTo((3L, 3L)));

            Assert.IsTrue(editor.Undo());
            Assert.IsTrue(editor.Undo());
            Assert.IsNull(editor.Project.Settings.Library);
            Assert.That(Gates().Where(g => g.Kind != GateKind.Or).Select(g => (g.Rise, g.Fall)), Is.All.EqualTo((1L, 2L)), "back as drawn");
        }

        /// <summary>A library with no XOR for a full adder built of XORs: refused, naming the gate and what the library has.</summary>
        [Test]
        public void ALibraryWithoutAGateTheDesignUses_IsRefused_InWords()
        {
            var editor = new BoardEditor(SampleBoards.Build("full_adder"));
            GateLibrary noXor = GateLibrary.CourseCells(1) with { Cells = GateLibrary.AllCells(1).Where(c => c.Kind != GateKind.Xor).ToList() };
            EditResult set = editor.SetLibrary(noXor);
            Assert.IsFalse(set.Done);
            StringAssert.Contains("is a XOR2, which the library has no cell for: it has INV, BUF, AND2", set.Refusal);
        }

        [Test]
        public void UnderALibrary_WhatItRefuses_IsRefused_InItsWords()
        {
            BoardEditor editor = Under(SampleBoards.Build("full_adder"), GateLibrary.CourseCells(1));
            string top = editor.Project.Top;

            EditResult tri = editor.Add(top, new GatePart { Kind = GateKind.Bufif1, Inputs = 2 }, new Placement(30, 30));
            StringAssert.Contains("is a BUFIF1, which the library has no cell for", tri.Refusal, "named by its kind, no count of inputs");
            EditResult wide = editor.Add(top, new GatePart { Kind = GateKind.And, Inputs = 2, Rise = 1, Fall = 1, Width = 4 }, new Placement(30, 33));
            Assert.IsTrue(wide.Done, "a wide gate of a cell is that cell a bit");

            GatePart or = editor.Project.TopModule.Parts.OfType<GatePart>().First(g => g.Kind == GateKind.Or);
            EditResult retimed = editor.Change(top, or with { Rise = 7 });
            StringAssert.Contains("a gate keeps its cell's delays", retimed.Refusal);

            StringAssert.Contains("is a MUX, which is no cell of the library", editor.Add(top, new MuxPart(), new Placement(30, 36)).Refusal);
            Assert.IsTrue(editor.Add(top, new ConstantPart { Value = SystemVerilog.LogicVec.FromUInt64(1, 1) }, new Placement(30, 39)).Done, "constants are allowed");

            BoardEditor strict = Under(SampleBoards.Build("full_adder"), GateLibrary.CourseCells(1) with { NoConstants = true });
            StringAssert.Contains("the library takes none", strict.Add(strict.Project.Top, new ConstantPart { Value = SystemVerilog.LogicVec.FromUInt64(1, 1) }, new Placement(30, 30)).Refusal);
        }

        [Test]
        public void AKindTakenOut_IsEveryCellOfIt_AndPutBack_AtTheDelayAsked()
        {
            GateLibrary course = GateLibrary.CourseCells(2);
            GateLibrary noNand = course.Toggled(GateKind.Nand, 5);
            Assert.IsFalse(noNand.Allows(GateKind.Nand));
            Assert.AreEqual(17, noNand.Cells.Count);
            Assert.AreEqual(GateLibrary.CustomName, noNand.Name);

            GateLibrary back = noNand.Toggled(GateKind.Nand, 5);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, back.Cells.Where(c => c.Kind == GateKind.Nand).Select(c => c.Inputs));
            Assert.That(back.Cells.Where(c => c.Kind == GateKind.Nand).Select(c => (c.Rise, c.Fall)), Is.All.EqualTo((5L, 5L)));
        }

        [Test]
        public void ALibrary_IsRefused_WhenWhatIsDrawnBreaksIt()
        {
            var editor = new BoardEditor(SampleBoards.Build("mux4"));
            EditResult set = editor.SetLibrary(GateLibrary.CourseCells(1));
            Assert.IsFalse(set.Done);
            StringAssert.Contains("is a MUX", set.Refusal);
            Assert.IsNull(editor.Project.Settings.Library, "and nothing changed");
        }

        /// <summary>
        /// The full adder is five gates: over a budget of 4 it is never refused -- gates come one at a time on the way to
        /// fewer -- and the rules say nothing of it: the TIMING tab's gate count is the one count, written code's gates and
        /// all (the 14d review found the rules counting drawn gates alone).
        /// </summary>
        [Test]
        public void ADesignOverItsBudget_IsNeverRefused()
        {
            BoardEditor editor = Under(SampleBoards.Build("full_adder"), GateLibrary.CourseCells(1) with { GateBudget = 4 });
            Assert.IsFalse(BoardRules.Check(editor.Project).Any(d => d.Message.Contains("budget")));
            Assert.IsTrue(editor.Add(editor.Project.Top, new GatePart { Kind = GateKind.And, Inputs = 2 }, new Placement(30, 30)).Done, "a sixth gate");
        }

        [Test]
        public void ALibrary_IsSaved_AndAnOlderFileHasNone()
        {
            Project project = Under(SampleBoards.Build("full_adder"), GateLibrary.CourseCells(2) with { NoConstants = true, GateBudget = 9 }).Project;
            var fields = new JsonSerializerOptions { IncludeFields = true };
            var problems = new List<string>();
            string json = JsonSerializer.Serialize(ProjectRecord.From(project), fields);
            GateLibrary again = JsonSerializer.Deserialize<ProjectRecord>(json, fields).ToProject(problems).Settings.Library;

            Assert.IsEmpty(problems);
            CollectionAssert.AreEqual(project.Settings.Library.Cells, again.Cells);
            Assert.AreEqual((true, 9, GateLibrary.CourseCellsName), (again.NoConstants, again.GateBudget, again.Name));

            string older = json.Replace("\"hasLibrary\":true", "\"hasLibrary\":false");
            Assert.IsNull(JsonSerializer.Deserialize<ProjectRecord>(older, fields).ToProject(problems).Settings.Library);
        }

        [Test]
        public void ASavedCellBitSorterHasNot_IsLeftOut_AndSaidSo()
        {
            var record = new LibraryRecord { cells = new[] { new CellRecord { kind = "Nand", inputs = 2, rise = 1, fall = 1 }, new CellRecord { kind = "Nand", inputs = 9 } } };
            var problems = new List<string>();
            GateLibrary library = record.ToLibrary(problems);
            Assert.AreEqual(1, library.Cells.Count);
            StringAssert.Contains("no cell BitSorter has", problems.Single());
        }

        /// <summary>
        /// A library may hold a tri-state gate (Part 15c): the course's cells have none, and a bus needs one -- share the wire
        /// is built of BUFIF1s. It is held to its delays as any cell is, and saved.
        /// </summary>
        [Test]
        public void ALibrary_MayHoldATriStateGate_AndKeepsItThroughASave()
        {
            Assert.IsTrue(GateLibrary.CanHold(GateKind.Bufif1, 2));
            Assert.IsTrue(GateLibrary.CanHold(GateKind.Notif0, 2));
            Assert.IsFalse(GateLibrary.CanHold(GateKind.Bufif1, 3), "a tri-state gate has a data input and an enable");
            Assert.IsTrue(GateLibrary.CanHold(GateKind.Nand, 3), "and every cell BitSorter has");

            Project held = Project.New("bus") with
            {
                Settings = new ProjectSettings { Library = new GateLibrary { Cells = new[] { new LibraryCell(GateKind.Bufif1, 1, 1, 1) } } },
            };
            StringAssert.Contains("a tri-state gate has a data input and an enable",
                string.Join("\n", BoardRules.Check(held).Select(d => d.Message)), "a BUFIF1 of one input is refused, saying why");

            GateLibrary bus = new GateLibrary { Cells = new[] { new LibraryCell(GateKind.Bufif1, 2, 2, 2) } };
            var problems = new List<string>();
            GateLibrary back = LibraryRecord.From(bus).ToLibrary(problems);
            Assert.IsEmpty(problems);
            Assert.AreEqual("BUFIF1", back.Cells.Single().Name);

            var editor = new BoardEditor(Project.New("bus"));
            Assert.IsTrue(editor.SetLibrary(bus).Done);
            EditResult placed = editor.Add("bus", new GatePart { Kind = GateKind.Bufif1, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(4, 0));
            Assert.IsTrue(placed.Done, placed.Refusal);
            GatePart gate = (GatePart)editor.Project.TopModule.FindPart(placed.Id);
            Assert.AreEqual((2L, 2L), (gate.Rise, gate.Fall), "placed at its cell's delays, as any gate under a library is");
        }
    }
}
