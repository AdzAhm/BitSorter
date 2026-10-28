using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The INPUTS tab's table (Part 15g): what the testbench gives the inputs as a vector table -- a column per input, a row
    /// per moment something changes -- and every edit of it a stimulus to commit. Ahmad asked for input sequences and vectors
    /// without code, for buses as well as bits (2026-10-28).
    /// </summary>
    public class InputTableTests
    {
        private static LogicVec V(string binary) => Literal.Parse(binary.Length + "'b" + binary).Value;

        /// <summary>A top with an input a, a 4-bit input s and an output y, s changing at 0 and 20 and a at 0 and 10.</summary>
        private static WorkbenchSession Session()
        {
            var editor = new BoardEditor(Project.New("top"));
            int Must(EditResult r) { Assert.IsTrue(r.Done, r.Refusal); return r.Id; }
            int a = Must(editor.Add("top", new PortPart { Name = "a" }, new Placement(1, 1)));
            Must(editor.Add("top", new PortPart { Name = "s", Width = 4 }, new Placement(1, -1)));
            int y = Must(editor.Add("top", new PortPart { Name = "y", Direction = PortDirection.Output }, new Placement(6, 1)));
            Must(editor.Connect("top", Endpoint.At(a, "out"), Endpoint.At(y, "in")));
            Must(editor.SetStimulus(new Stimulus
            {
                Tracks = new[]
                {
                    new Track { Port = "a", Steps = new[] { new Step(0, V("0")), new Step(10, V("1")) } },
                    new Track { Port = "s", Steps = new[] { new Step(0, V("0000")), new Step(20, V("1010")) } },
                },
                End = 40,
            }));
            var session = new WorkbenchSession(editor.Project, () => 0);
            session.Finish();
            return session;
        }

        private static void Commit(WorkbenchSession session, bool done, Stimulus edited, long from, string refusal)
        {
            Assert.IsTrue(done, refusal);
            EditResult made = session.SetStimulus(edited, from);
            Assert.IsTrue(made.Done, made.Refusal);
        }

        [Test]
        public void TheTable_IsAColumnPerInput_AndARowPerMomentSomethingChanges()
        {
            InputTable table = InputTable.Of(Session().Project);

            CollectionAssert.AreEqual(new[] { "a", "s[3:0]" }, table.Columns.Select(c => c.Name).ToArray(), "the inputs, not the output");
            CollectionAssert.AreEqual(new long[] { 0, 10, 20 }, table.Rows.Select(r => r.Time).ToArray());
            CollectionAssert.AreEqual(new[] { "0", "0000" }, table.Rows[0].Cells.ToArray());
            CollectionAssert.AreEqual(new[] { "1", "" }, table.Rows[1].Cells.ToArray(), "s does not change at 10: its cell is empty");
            CollectionAssert.AreEqual(new[] { "", "1010" }, table.Rows[2].Cells.ToArray());
            Assert.IsFalse(table.ReadOnly);
            Assert.AreEqual(40, table.End);
        }

        [Test]
        public void ABusValue_IsTypedInItsColumnsRadix_OrAsAnyLiteral_AndUndoneInOneStep()
        {
            WorkbenchSession session = Session();
            InputTable table = InputTable.Of(session.Project).NextRadix(1);
            Assert.AreEqual(Radix.Hex, table.Columns[1].Radix);
            Assert.AreEqual("a", table.Rows[2].Cells[1], "1010 written in hex");

            bool done = table.TrySetCell(1, 1, "C", out Stimulus edited, out long from, out string refusal);
            Commit(session, done, edited, from, refusal);
            table = InputTable.Of(session.Project, table);
            Assert.AreEqual("c", table.Rows[1].Cells[1], "C in a hex column is 1100, at 10");
            Assert.AreEqual(10, from, "the run changed from the row's moment");

            done = table.TrySetCell(0, 1, "4'b0011", out edited, out from, out refusal);
            Commit(session, done, edited, from, refusal);
            Assert.AreEqual("3", InputTable.Of(session.Project, table).Rows[0].Cells[1], "a literal, whatever the column's radix");

            Assert.IsFalse(table.TrySetCell(0, 1, "1G", out _, out _, out string bad), "no hex digit");
            Assert.IsNotNull(bad);
            Assert.IsFalse(table.TrySetCell(0, 1, "1F", out _, out _, out string wide), "five bits in four");
            StringAssert.Contains("does not fit", wide);

            Assert.IsTrue(session.Undo());
            Assert.AreEqual("c", InputTable.Of(session.Project, table).Rows[1].Cells[1], "one step back: 0011 at 0 undone, C at 10 kept");
        }

        [Test]
        public void ARowAdded_IsTheTablesUntilItIsGivenAValue_AndEmptyingACell_TakesItsStepAway()
        {
            WorkbenchSession session = Session();
            Stimulus before = session.Project.Stimulus;
            Assert.IsTrue(InputTable.Of(session.Project).TryAddRow(out InputTable table, out string added), added);
            Assert.AreEqual(30, table.Rows.Last().Time, "a step after the last");
            Assert.IsTrue(table.Rows.Last().Pending);
            Assert.AreSame(before, session.Project.Stimulus, "nothing committed");

            Assert.IsTrue(table.TrySetStep("5", out InputTable fives, out _));
            Assert.IsTrue(fives.TryAddRow(out InputTable more, out _));
            Assert.AreEqual(35, more.Rows.Last().Time, "STEP 5: a row five after the last");

            bool done = table.TrySetCell(3, 0, "0", out Stimulus edited, out long from, out string refusal);
            Commit(session, done, edited, from, refusal);
            table = InputTable.Of(session.Project, table);
            Assert.AreEqual(30, table.Rows.Last().Time);
            Assert.IsFalse(table.Rows.Last().Pending, "given a value, the document's own");

            done = table.TrySetCell(1, 0, "", out edited, out from, out refusal);
            Commit(session, done, edited, from, refusal);
            table = InputTable.Of(session.Project, table);
            CollectionAssert.AreEqual(new long[] { 0, 20 }, table.Rows.Select(r => r.Time).ToArray(),
                "a's 1 at 10 emptied -- and its 0 at 30 with it, the value a then already held");
        }

        [Test]
        public void ARow_IsMovedByItsTime_AndTakenAway()
        {
            WorkbenchSession session = Session();
            InputTable table = InputTable.Of(session.Project);
            bool done = table.TrySetTime(1, "25", out Stimulus edited, out long from, out _, out string refusal);
            Commit(session, done, edited, from, refusal);
            table = InputTable.Of(session.Project, table);
            CollectionAssert.AreEqual(new long[] { 0, 20, 25 }, table.Rows.Select(r => r.Time).ToArray(), "10 moved past 20");
            Assert.AreEqual(10, from, "the run changed from the earlier of the two");

            Assert.IsFalse(table.TrySetTime(2, "-4", out _, out _, out _, out string never));
            Assert.IsNotNull(never);

            done = table.TryRemoveRow(2, out edited, out from, out _, out refusal);
            Commit(session, done, edited, from, refusal);
            CollectionAssert.AreEqual(new long[] { 0, 20 }, InputTable.Of(session.Project, table).Rows.Select(r => r.Time).ToArray());
        }

        [Test]
        public void AllCombinations_Count_AndClock_WorkOnTheChosenInputs()
        {
            WorkbenchSession session = Session();
            InputTable table = InputTable.Of(session.Project);
            Assert.IsFalse(table.TryAllCombinations(out _, out string none), "nothing chosen");
            StringAssert.Contains("Choose", none);

            table = table.Choose(0).Choose(1);
            Assert.IsTrue(table.Columns.All(c => c.Chosen));
            bool done = table.TryAllCombinations(out Stimulus edited, out string refusal);
            Commit(session, done, edited, 0, refusal);
            table = InputTable.Of(session.Project, table);
            Assert.AreEqual(32, table.Rows.Count, "five bits, every combination");
            Assert.AreEqual(320, table.End, "and the run lasts for them");

            Assert.IsFalse(table.TryCount(out _, out string two), "two chosen");
            StringAssert.Contains("Choose one", two);
            table = table.Choose(0);
            done = table.TryCount(out edited, out refusal);
            Commit(session, done, edited, 0, refusal);
            Assert.AreEqual("1111", InputTable.Of(session.Project, table).Rows[15].Cells[1], "s counts 0 to 15");

            table = InputTable.Of(session.Project, table).Choose(1).Choose(0);
            done = table.TryClock(out edited, out InputTable letGo, out refusal);
            Commit(session, done, edited, 0, refusal);
            Assert.IsFalse(letGo.Columns[0].Chosen, "a made a clock and let go of, so it is not in the next fill's way");
            table = InputTable.Of(session.Project, letGo);
            Assert.AreEqual(20, table.Columns[0].ClockPeriod, "a clock that changes every step");
            Assert.AreEqual("CLOCK 20", table.Columns[0].Kind);
            Assert.IsFalse(table.TrySetCell(0, 0, "1", out _, out _, out string ticking));
            StringAssert.Contains("clock", ticking);

            done = table.Choose(0).TryClock(out edited, out _, out refusal);
            Commit(session, done, edited, 0, refusal);
            Assert.IsFalse(InputTable.Of(session.Project, table).Columns[0].IsClock, "and back");
        }

        [Test]
        public void TheEnd_IsTyped()
        {
            WorkbenchSession session = Session();
            InputTable table = InputTable.Of(session.Project);
            bool done = table.TrySetEnd("90", out Stimulus edited, out string refusal);
            Commit(session, done, edited, 0, refusal);
            Assert.AreEqual(90, InputTable.Of(session.Project, table).End);
            Assert.IsFalse(table.TrySetEnd("soon", out _, out _));
        }

        /// <summary>
        /// In an exercise the table is read only and shows the exercise's own test: its rows, and in a column of each output it
        /// checks, the value it wants. An edit is refused in the document's words, as SET is.
        /// </summary>
        [Test]
        public void InAnExercise_TheTableShowsItsTest_AndRefusesAnEdit()
        {
            Exercise exercise = ExerciseFiles.InOrder().Single(e => e.Id == "half-adder");
            Project project = exercise.ToProject(new List<string>());
            InputTable table = InputTable.Of(project);

            Assert.IsTrue(table.ReadOnly);
            Assert.AreEqual(BoardEditor.ExerciseDrivesTheInputs, table.Why);
            Assert.IsTrue(table.Columns.Any(c => !c.IsInput && c.Kind == "EXPECTED"), "an output its test checks");
            Assert.IsTrue(table.Rows.Any(r => r.Cells.Where((_, c) => !table.Columns[c].IsInput).Any(cell => cell.Length > 0)),
                "and the values it wants");
            Assert.IsFalse(table.TrySetCell(0, 0, "1", out _, out _, out string refusal));
            Assert.AreEqual(BoardEditor.ExerciseDrivesTheInputs, refusal);

            var session = new WorkbenchSession(project, () => 0);
            session.Finish();
            EditResult made = session.SetStimulus(project.Stimulus with { End = project.Stimulus.End + 1 }, 0);
            Assert.IsFalse(made.Done, "and the document refuses it too");
            Assert.AreEqual(BoardEditor.ExerciseDrivesTheInputs, made.Refusal);
        }

        private static Project Imported(params (string Path, string Text)[] files)
        {
            ProjectImport import = ProjectImporter.Import(files.Select(f => new SourceFile(f.Path, f.Text)).ToList());
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            return import.Project;
        }

        /// <summary>
        /// Under a written testbench (Part 15h) the table is what the testbench applies, read from a run of it as CONVERT would
        /// record it -- read only, and an edit refused in the same words as ever. It was the column heads over nothing, and Ahmad
        /// "couldn't do anything in inputs" (2026-10-28).
        /// </summary>
        [Test]
        public void UnderATestbench_TheTableIsWhatItApplies_ReadOnly()
        {
            Project project = Imported(
                ("pick.sv", "module pick (input logic a, input logic b, input logic s, output logic y);\n" +
                            "    assign y = s ? b : a;\n" +
                            "endmodule\n"),
                ("pick_tb.sv", "module pick_tb;\n" +
                               "    logic a, b, s, y;\n" +
                               "    pick dut (.a(a), .b(b), .s(s), .y(y));\n" +
                               "    initial begin\n" +
                               "        a = 0; b = 1; s = 0;\n" +
                               "        #10 s = 1;\n" +
                               "        #10 a = 1;\n" +
                               "        #10 $stop;\n" +
                               "    end\n" +
                               "endmodule\n"));
            Assert.AreEqual("pick_tb", project.Bench, "sanity: imported with its testbench");

            InputTable table = InputTable.Of(project);
            Assert.AreEqual("pick_tb", table.AppliedBy);
            Assert.IsNull(table.CannotConvert);
            Assert.IsTrue(table.ReadOnly);
            Assert.AreEqual(BoardRules.BenchDrivesTheInputs(project), table.Why, "an edit refused in the same words as ever");
            CollectionAssert.AreEqual(new[] { "a", "b", "s" }, table.Columns.Select(c => c.Port).ToArray());
            CollectionAssert.AreEqual(new long[] { 0, 10, 20 }, table.Rows.Select(r => r.Time).ToArray());
            CollectionAssert.AreEqual(new[] { "0", "1", "0" }, table.Rows[0].Cells.ToArray(), "what it sets at 0");
            CollectionAssert.AreEqual(new[] { "", "", "1" }, table.Rows[1].Cells.ToArray(), "s at 10, and nothing else");
            CollectionAssert.AreEqual(new[] { "1", "", "" }, table.Rows[2].Cells.ToArray(), "a at 20");
            Assert.IsFalse(table.TrySetCell(1, 0, "1", out _, out _, out string refused), "a cell typed in");
            Assert.AreEqual(table.Why, refused);
        }

        /// <summary>A testbench CONVERT cannot turn into a stimulus has no rows to show, and says why.</summary>
        [Test]
        public void UnderATestbenchThatCannotBeConverted_TheTableSaysWhy()
        {
            Project project = Imported(
                ("wide.sv", "module wide #(parameter W = 1) (input logic [W-1:0] a, output logic [W-1:0] y);\n" +
                            "    assign y = a;\n" +
                            "endmodule\n"),
                ("wide_tb.sv", "module wide_tb;\n" +
                               "    logic a, y;\n" +
                               "    wide #(.W(1)) dut (.a(a), .y(y));\n" +
                               "    initial begin a = 0; #5 a = 1; #5 $stop; end\n" +
                               "endmodule\n"));
            Assert.AreEqual("wide_tb", project.Bench, "sanity: imported with its testbench");

            InputTable table = InputTable.Of(project);
            Assert.IsNull(table.AppliedBy);
            StringAssert.Contains("parameters", table.CannotConvert);
            Assert.IsTrue(table.ReadOnly);
            CollectionAssert.IsEmpty(table.Rows);
        }

        /// <summary>The inputs nothing drives -- no step, start or clock -- which an import without its testbench names.</summary>
        [Test]
        public void TheInputsNothingDrives_AreNamed()
        {
            WorkbenchSession session = Session();
            CollectionAssert.IsEmpty(InputTable.Undriven(session.Project), "a and s both have steps");

            EditResult emptied = session.SetStimulus(session.Project.Stimulus with
            {
                Tracks = session.Project.Stimulus.Tracks.Where(t => t.Port != "s").ToArray(),
            }, 0);
            Assert.IsTrue(emptied.Done, emptied.Refusal);
            CollectionAssert.AreEqual(new[] { "s" }, InputTable.Undriven(session.Project).ToArray());

            Assert.IsTrue(InputTable.Of(session.Project).Choose(0).TryClock(out Stimulus clocked, out _, out string refusal), refusal);
            Assert.IsTrue(session.SetStimulus(clocked with { Tracks = new Track[0] }, 0).Done);
            CollectionAssert.AreEqual(new[] { "s" }, InputTable.Undriven(session.Project).ToArray(), "a clock is driven");
        }

        /// <summary>
        /// An edit that changes nothing commits nothing (the 15g-4 review): a value the input already holds says so, an empty
        /// cell emptied is quiet, and the end typed as it is is no undo step. And no moment past the table's last.
        /// </summary>
        [Test]
        public void AnEditThatChangesNothing_IsNoUndoStep_AndNoMomentGoesPastTheLast()
        {
            WorkbenchSession session = Session();
            InputTable table = InputTable.Of(session.Project);

            Assert.IsFalse(table.TrySetCell(1, 1, "0000", out _, out _, out string held), "s is 0000 at 10 already");
            StringAssert.Contains("already holds 0000", held);
            Assert.IsTrue(table.TrySetCell(1, 1, "", out Stimulus emptied, out _, out _), "s has no step at 10 to empty");
            Assert.IsNull(emptied);
            Assert.IsTrue(table.TrySetEnd("040", out Stimulus same, out _));
            Assert.IsNull(same, "the end it has");

            Assert.IsFalse(table.TrySetEnd("9223372036854775807", out _, out string forever));
            StringAssert.Contains("as far as", forever);
            Assert.IsFalse(table.TrySetTime(1, (StimulusEdits.MaxTime + 1).ToString(), out _, out _, out _, out _));
            Assert.IsTrue(table.TrySetStep(StimulusEdits.MaxTime.ToString(), out InputTable wide, out _));
            Assert.IsFalse(wide.TryAddRow(out _, out string past), "a step that would put the row past the last moment");
            StringAssert.Contains("as far as", past);
            Assert.IsFalse(wide.Choose(0).TryAllCombinations(out _, out string fill));
            StringAssert.Contains("smaller step", fill);
        }

        [Test]
        public void ARefusal_SaysWhatWasTyped_AndHexTakes0x()
        {
            Assert.IsFalse(InputTable.TryRead("1FF", 8, Radix.Hex, out _, out string error));
            StringAssert.Contains("1FF", error);
            StringAssert.DoesNotContain("'h", error, "the text typed, not the literal it was made into");
            Assert.IsTrue(InputTable.TryRead("0x1F", 8, Radix.Hex, out LogicVec value, out error), error);
            Assert.AreEqual("00011111", value.Format(Radix.Binary));
        }

        [TestCase("A", 4, Radix.Hex, "1010")]
        [TestCase("1010", 4, Radix.Binary, "1010")]
        [TestCase("10", 4, Radix.Unsigned, "1010")]
        [TestCase("'hA", 4, Radix.Binary, "1010")]
        [TestCase("x", 4, Radix.Hex, "xxxx")]
        [TestCase("z", 1, Radix.Binary, "z")]
        [TestCase("1_0", 2, Radix.Binary, "10")]
        public void WhatIsTyped_IsReadInTheColumnsRadix_UnlessItIsALiteral(string text, int width, Radix radix, string binary)
        {
            Assert.IsTrue(InputTable.TryRead(text, width, radix, out LogicVec value, out string error), error);
            Assert.AreEqual(binary, value.Format(Radix.Binary));
        }
    }
}
