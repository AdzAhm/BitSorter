using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Answering an exercise in code (Part 15e): the top written -- from the drawing as it prints, or its ports -- and graded
    /// as a drawing is: held to the library's cells with no operator, at its line, and to the stock's counts, which a
    /// drawing's parts never saw in code; a block the exercise gives counted whole wherever it is placed; and a register
    /// starting where the exercise says only when the code gives it that start.
    /// </summary>
    public class ExerciseCodeTests
    {
        private static Project Opened(Exercise exercise)
        {
            var problems = new List<string>();
            Project project = exercise.ToProject(problems);
            Assert.IsNotNull(project, string.Join("\n", problems));
            return project;
        }

        /// <summary><paramref name="exercise"/> answered by <paramref name="source"/>, its top written, through the editor and its lock.</summary>
        private static Project Answered(Exercise exercise, string source)
        {
            var editor = new BoardEditor(Opened(exercise));
            Module written = exercise.Written(editor.Project);
            EditResult wrote = editor.ReplaceModule(written with { Source = written.Source.Substring(0, written.Source.IndexOf("module ")) + source });
            Assert.IsTrue(wrote.Done, wrote.Refusal);
            return editor.Project;
        }

        private static string Why(Grade grade) =>
            string.Join("\n", grade.Problems.Concat(grade.Faults).Concat(grade.Failures.Select(f => f.ToString())));

        private const string HalfAdder = @"module half_adder (input logic a, input logic b, output logic sum, output logic carry);
    xor #1 g1 (sum, a, b);
    and #1 g2 (carry, a, b);
endmodule
";

        [Test]
        public void AWrittenAnswer_IsGradedAsADrawingIs()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            Grade grade = ExerciseGrader.Grade(exercise, Answered(exercise, HalfAdder));
            Assert.IsTrue(grade.Solved, Why(grade));
            Assert.AreEqual(2, grade.Gates, "its gates counted, though no part is drawn");
            Assert.AreEqual(1, grade.Parts["xor"]);
            Assert.AreEqual(1, grade.Parts["and"]);
        }

        private const string WithAnOperator = @"module half_adder (input logic a, input logic b, output logic sum, output logic carry);
    assign sum = a ^ b;
    and #1 g2 (carry, a, b);
endmodule
";

        [Test]
        public void AnOperator_IsRefused_SayingWhy_AndGradedAtItsLine()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            var editor = new BoardEditor(Opened(exercise));
            Module written = exercise.Written(editor.Project);
            string header = written.Source.Substring(0, written.Source.IndexOf("module "));
            EditResult refused = editor.ReplaceModule(written with { Source = header + WithAnOperator });
            Assert.IsFalse(refused.Done, "the edit adds an error, so it is refused");
            StringAssert.Contains("an operator", refused.Refusal);

            Project kept = editor.Project.WithModule(written with { Source = header + WithAnOperator });
            Grade grade = ExerciseGrader.Grade(exercise, kept);
            Assert.IsFalse(grade.Solved);
            int line = header.Split('\n').Length + 1;
            Assert.IsTrue(grade.Faults.Any(f => f.StartsWith("half_adder, line " + line + ":") && f.Contains("an operator")), Why(grade));
        }

        [Test]
        public void OneGateTooMany_IsAFault_InCodeAsInADrawing()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            Grade grade = ExerciseGrader.Grade(exercise, Answered(exercise, @"module half_adder (input logic a, input logic b, output logic sum, output logic carry);
    logic t;
    xor #1 g1 (t, a, b);
    xor #1 g3 (sum, t, 1'b0);
    and #1 g2 (carry, a, b);
endmodule
".Replace("1'b0", "b")));
            Assert.IsFalse(grade.Solved);
            CollectionAssert.Contains(grade.Faults, "The exercise stocks 1 XOR, and the design has 2.", Why(grade));
        }

        [Test]
        public void AWrittenRegister_StartsWhereTheCodeSaysItDoes()
        {
            Exercise exercise = ExerciseFiles.Load("one-clock-late");
            string module = Opened(exercise).Top;
            string Register(string declared) => "module " + module + " (input logic clk, input logic in, output logic out);\n" +
                "    logic r" + declared + ";\n    always_ff @(posedge clk)\n        r <= in;\n    assign out = r;\nendmodule\n";

            Grade started = ExerciseGrader.Grade(exercise, Answered(exercise, Register(" = 1'b0")));
            Assert.IsTrue(started.Solved, Why(started));
            Assert.AreEqual(1, started.Parts[PartCount.Register], "its register counted");

            Grade unknown = ExerciseGrader.Grade(exercise, Answered(exercise, Register("")));
            Assert.IsFalse(unknown.Solved, "a register with no start is x until something says");
            StringAssert.Contains("x", Why(unknown));
            StringAssert.Contains("starts at 0 here: logic q = 1'b0;", exercise.Written(Opened(exercise)).Source, "the header says so");
        }

        [Test]
        public void ABlockTheExerciseGives_CountsWhole_InCode()
        {
            Exercise exercise = ExerciseFiles.Load("four-at-once");
            Project solution = ExerciseFiles.Solution(exercise);
            Module written = exercise.Written(solution);
            var editor = new BoardEditor(Opened(exercise));
            Assert.IsTrue(editor.ReplaceModule(written).Done, "the drawn solution, as code");
            Assert.IsTrue(editor.Project.TopModule.IsWritten);

            Grade grade = ExerciseGrader.Grade(exercise, editor.Project);
            Assert.IsTrue(grade.Solved, Why(grade));
            Assert.AreEqual(4, grade.Parts["FA"], "four full adders, each counted once");
            Assert.IsFalse(grade.Parts.ContainsKey("xor") && grade.Parts["xor"] > 0, "and none of the gates inside them");
        }

        [Test]
        public void EverySolution_WrittenAsCode_StillSolvesItsExercise()
        {
            foreach (string id in ExerciseFiles.Ids())
            {
                Exercise exercise = ExerciseFiles.Load(id);
                if (!exercise.Code)
                    continue;
                Project solution = ExerciseFiles.Solution(exercise);
                Project written = solution.WithModule(exercise.Written(solution));
                Grade grade = ExerciseGrader.Grade(exercise, written);
                Assert.IsTrue(grade.Solved, $"{id}, written: {Why(grade)}");
            }
        }

        [Test]
        public void AWrittenAnswer_KeptAndOpenedAgain_IsAdopted_NotMovedAside()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            Project kept = ProjectRecord.From(Answered(exercise, HalfAdder)).ToProject(new List<string>());
            var problems = new List<string>();
            Project adopted = exercise.Adopt(kept, problems);
            Assert.IsNotNull(adopted, "its ports are its header's: " + string.Join("\n", problems));
            Assert.IsTrue(adopted.TopModule.IsWritten, "the code kept");
            Assert.IsTrue(ExerciseGrader.Grade(exercise, adopted).Solved);
        }

        [Test]
        public void ARegisterWrittenABitAtATime_CountsABitEach()
        {
            Exercise late = Exercise.From(System.Text.Json.JsonSerializer.Deserialize<ExerciseRecord>(@"{
                ""id"": ""late4"", ""title"": ""Late by four"", ""clock"": true, ""code"": true,
                ""inputs"": [ { ""name"": ""d"", ""width"": 4, ""values"": ""0011 0101 1111"" } ],
                ""outputs"": [ { ""name"": ""q"", ""width"": 4, ""values"": ""xxxx 0011 0101 1111"" } ],
                ""parts"": [ { ""kind"": ""Register"", ""count"": 4 } ] }",
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true }), new List<string>());
            Assert.IsNotNull(late, "sanity: the exercise reads");
            Grade grade = ExerciseGrader.Grade(late, Answered(late, @"module late4 (input logic clk, input logic [3:0] d, output logic [3:0] q);
    logic [3:0] r;
    genvar i;
    for (i = 0; i < 4; i++) begin : each
        always_ff @(posedge clk)
            r[i] <= d[i];
    end
    assign q = r;
endmodule
"));
            Assert.AreEqual(4, grade.Parts[PartCount.Register], Why(grade));
            Assert.IsTrue(grade.Solved, Why(grade));
        }

        [Test]
        public void CodeTooBigToCount_IsAFault_NotAPass()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            Grade grade = ExerciseGrader.Grade(exercise, Answered(exercise, @"module half_adder (input logic a, input logic b, output logic sum, output logic carry);
    logic [1048575:0] big;
    xor #1 g1 (sum, a, b);
    and #1 g2 (carry, a, b);
endmodule
"));
            Assert.IsFalse(grade.Solved, "not passed uncounted");
            Assert.IsTrue(grade.Faults.Any(f => f.StartsWith("Its gates could not be counted")), Why(grade));
        }

        [Test]
        public void DrawnAgain_ItIsTheCodeDrawn_CountingItsIdsOn()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            Project project = Answered(exercise, HalfAdder);
            var problems = new List<string>();
            var notDrawn = new List<string>();
            Module drawn = exercise.Drawn(project, problems, notDrawn);
            Assert.IsNotNull(drawn, string.Join("\n", problems));
            CollectionAssert.IsEmpty(notDrawn, "the code is drawn");
            Assert.IsFalse(drawn.IsWritten);
            CollectionAssert.AreEquivalent(Opened(exercise).TopModule.Ports.Select(p => p.Name), drawn.Ports.Select(p => p.Name));
            CollectionAssert.AreEquivalent(new[] { BitSorter.Sim.GateKind.Xor, BitSorter.Sim.GateKind.And }, drawn.Parts.OfType<GatePart>().Select(g => g.Kind),
                "its gates, as the code has them (Part 15g: it drew the exercise as it starts)");
            Assert.GreaterOrEqual(drawn.NextId, project.TopModule.NextId, "no id given twice");

            var editor = new BoardEditor(project);
            Assert.IsTrue(editor.ReplaceModule(drawn).Done);
            Assert.IsFalse(editor.Project.TopModule.IsWritten);
            Assert.IsTrue(ExerciseGrader.Grade(exercise, editor.Project).Solved, "drawn, it still solves the exercise");
        }

        [Test]
        public void CodeThatCannotBeDrawn_OpensTheExerciseAsItStarts_SayingWhy()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            // Gates only, so it is an answer, but in a generate loop, which an import does not draw.
            Project project = Answered(exercise, "module half_adder (input logic a, input logic b, output logic sum, output logic carry);\n" +
                "    genvar i;\n    for (i = 0; i < 1; i++) begin : one\n        xor #1 g1 (sum, a, b);\n    end\n" +
                "    and #1 g2 (carry, a, b);\nendmodule\n");
            var problems = new List<string>();
            var notDrawn = new List<string>();
            Module drawn = exercise.Drawn(project, problems, notDrawn);
            Assert.IsNotNull(drawn, string.Join("\n", problems));
            CollectionAssert.IsNotEmpty(notDrawn, "it says why the code was not drawn");
            Assert.IsFalse(drawn.IsWritten);
            Assert.IsEmpty(drawn.Parts.OfType<GatePart>(), "the exercise as it starts: its ports, no gates");
        }

        [Test]
        public void EverySolution_WrittenThenDrawn_IsItsCodeDrawn_AndStillSolves()
        {
            foreach (string id in ExerciseFiles.Ids())
            {
                Exercise exercise = ExerciseFiles.Load(id);
                if (!exercise.Code)
                    continue;
                Project solution = ExerciseFiles.Solution(exercise);
                Grade solved = ExerciseGrader.Grade(exercise, solution);
                var editor = new BoardEditor(Opened(exercise));
                EditResult wrote = editor.ReplaceModule(exercise.Written(solution));
                Assert.IsTrue(wrote.Done, $"{id}: {wrote.Refusal}");

                var problems = new List<string>();
                var notDrawn = new List<string>();
                Module drawn = exercise.Drawn(editor.Project, problems, notDrawn);
                Assert.IsNotNull(drawn, $"{id}: {string.Join("\n", problems)}");
                CollectionAssert.IsEmpty(notDrawn, $"{id}: its code is drawn");
                EditResult replaced = editor.ReplaceModule(drawn);
                Assert.IsTrue(replaced.Done, $"{id}: {replaced.Refusal}");

                Grade grade = ExerciseGrader.Grade(exercise, editor.Project);
                Assert.IsTrue(grade.Solved, $"{id}, written and drawn again: {Why(grade)}");
                CollectionAssert.AreEquivalent(solved.Parts, grade.Parts, $"{id}: the same parts");
            }
        }

        [Test]
        public void ABlockTheExerciseGives_IsNotReplaced()
        {
            Exercise exercise = ExerciseFiles.Load("four-at-once");
            var editor = new BoardEditor(Opened(exercise));
            Module fa = editor.Project.Find("FA");
            EditResult replaced = editor.ReplaceModule(fa with { Source = "module FA (input logic a, input logic b, input logic cin, output logic s, output logic cout);\nendmodule\n", Parts = new Part[0] });
            Assert.IsFalse(replaced.Done);
            StringAssert.Contains("FA is a block the exercise gives", replaced.Refusal);
        }
    }
}
