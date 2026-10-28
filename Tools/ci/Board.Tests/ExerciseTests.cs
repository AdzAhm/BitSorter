using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The Exercise model and its grader (Part 15b): a file read under dotnet as Unity reads it, what is wrong with one said
    /// in words, the document it opens as -- ports, test, library, start circuit and blocks -- and a design graded at it:
    /// right, wrong at the moments it is wrong, over its stock, too slow, and clocked.
    /// </summary>
    public class ExerciseTests
    {
        private const string HalfAdderJson = @"{
            ""id"": ""half-adder"", ""title"": ""Half adder"", ""order"": 80, ""chapter"": 6,
            ""goal"": ""Add two bits."", ""hint"": ""two questions about one pair of bits."",
            ""inputs"":  [ { ""name"": ""a"", ""values"": ""0011"" }, { ""name"": ""b"", ""values"": ""0101"" } ],
            ""outputs"": [ { ""name"": ""sum"", ""values"": ""0110"" }, { ""name"": ""carry"", ""values"": ""0001"" } ],
            ""parts"":   [ { ""kind"": ""Xor"", ""count"": 1 }, { ""kind"": ""And"", ""count"": 1 } ]
        }";

        private static readonly JsonSerializerOptions Fields = new JsonSerializerOptions { IncludeFields = true };

        private static ExerciseRecord Record(string json) => JsonSerializer.Deserialize<ExerciseRecord>(json, Fields);

        private static Exercise Make(string json)
        {
            var problems = new List<string>();
            Exercise exercise = Exercise.From(Record(json), problems);
            Assert.IsNotNull(exercise, string.Join("\n", problems));
            return exercise;
        }

        private static Project Open(Exercise exercise)
        {
            var problems = new List<string>();
            Project project = exercise.ToProject(problems);
            Assert.IsNotNull(project, string.Join("\n", problems));
            return project;
        }

        private static int Port(BoardEditor editor, string name) => editor.Project.TopModule.Ports.Single(p => p.Name == name).Id;

        private static int Gate(BoardEditor editor, GateKind kind, double x, double y, long delay = 1)
        {
            EditResult added = editor.Add(editor.Project.Top, new GatePart { Kind = kind, Inputs = kind == GateKind.Not ? 1 : 2, Rise = delay, Fall = delay },
                new Placement(x, y));
            Assert.IsTrue(added.Done, added.Refusal);
            return added.Id;
        }

        private static void Wire(BoardEditor editor, int from, string fromPin, int to, string toPin)
        {
            EditResult wired = editor.Connect(editor.Project.Top, Endpoint.At(from, fromPin), Endpoint.At(to, toPin));
            Assert.IsTrue(wired.Done, wired.Refusal);
        }

        /// <summary>The half adder, its sum of <paramref name="sum"/> and its carry an AND.</summary>
        private static Project HalfAdder(Exercise exercise, GateKind sum)
        {
            var editor = new BoardEditor(Open(exercise));
            int a = Port(editor, "a"), b = Port(editor, "b");
            int s = Gate(editor, sum, 8, 0), c = Gate(editor, GateKind.And, 8, 4);
            foreach (int gate in new[] { s, c })
            {
                Wire(editor, a, "out", gate, "a");
                Wire(editor, b, "out", gate, "b");
            }
            Wire(editor, s, "y", Port(editor, "sum"), "in");
            Wire(editor, c, "y", Port(editor, "carry"), "in");
            return editor.Project;
        }

        [Test]
        public void AFile_IsReadAsUnityReadsIt_IntoAnExercise()
        {
            Exercise exercise = Make(HalfAdderJson);
            Assert.AreEqual(("half-adder", "Half adder", 80, 6), (exercise.Id, exercise.Title, exercise.Order, exercise.Chapter));
            Assert.AreEqual("half_adder", exercise.Module, "its module, the id as SystemVerilog takes a name");
            Assert.AreEqual(4, exercise.Vectors);
            CollectionAssert.AreEqual(new[] { "xor", "and" }, exercise.Stock.Select(s => s.Part));
            Assert.AreEqual("1 XOR", Exercise.Said(exercise.Stock[0]));
            Assert.AreEqual(10, exercise.Cycle, "worked out: twice through its stock, at least ten");
            Assert.IsFalse(exercise.Constants, "no constants unless it says");
        }

        [Test]
        public void WhatIsWrongWithAFile_IsSaid_InWords()
        {
            var problems = new List<string>();
            Assert.IsNull(Exercise.From(Record(@"{ ""title"": ""t"",
                ""inputs"": [ { ""name"": ""a"", ""values"": ""01q"" }, { ""name"": ""a"", ""values"": ""01"" } ],
                ""outputs"": [ { ""name"": ""y"", ""width"": 2, ""values"": ""01 1"" } ],
                ""parts"": [ { ""kind"": ""Frob"", ""count"": 1 }, { ""kind"": ""Not"", ""inputs"": 3 } ],
                ""cycle"": 7 }"), problems));
            string all = string.Join("\n", problems);
            StringAssert.Contains("an exercise needs an id", all);
            StringAssert.Contains("'q' is no value of 1 bit", all);
            StringAssert.Contains("two signals called a", all);
            StringAssert.Contains("'1' is no value of 2 bits", all);
            StringAssert.Contains("'Frob' is no gate", all);
            StringAssert.Contains("no NOT of 3 inputs", all);
            StringAssert.Contains("a cycle of 7 is no cycle", all);
        }

        [Test]
        public void ItOpens_AsItsPorts_ItsTest_AndItsLibrary()
        {
            Exercise exercise = Make(HalfAdderJson);
            Project project = Open(exercise);

            Assert.AreEqual("half_adder", project.Top);
            CollectionAssert.AreEqual(new[] { "a", "b", "sum", "carry" }, project.TopModule.Ports.Select(p => p.Name));

            Stimulus test = project.Stimulus;
            Assert.AreEqual(40, test.End);
            CollectionAssert.AreEqual(new long[] { 0, 20 }, test.Tracks.Single(t => t.Port == "a").Steps.Select(s => s.Time), "a step only where it changes");
            CollectionAssert.AreEqual(new long[] { 4, 4, 14, 14, 24, 24, 34, 34 }, test.Checks.Select(c => c.Time), "read just before each vector's half-way");

            GateLibrary library = project.Settings.Library;
            Assert.AreEqual(Exercise.LibraryName, library.Name);
            CollectionAssert.AreEqual(new[] { "XOR2", "AND2" }, library.Cells.Select(c => c.Name));
            Assert.IsTrue(library.NoConstants);
            Assert.AreEqual(1, library.MostOf("xor"));
        }

        [Test]
        public void ARightDesign_IsSolved_WithWhatItTook()
        {
            Exercise exercise = Make(HalfAdderJson);
            Grade grade = ExerciseGrader.Grade(exercise, HalfAdder(exercise, GateKind.Xor));
            Assert.IsTrue(grade.Solved, string.Join("\n", grade.Problems.Concat(grade.Faults).Concat(grade.Failures.Select(f => f.ToString()))));
            Assert.AreEqual(8, grade.Checks);
            Assert.AreEqual(2, grade.Gates);
            Assert.AreEqual(1L, grade.Settle, "one gate of delay 1 from each input to each output");
        }

        [Test]
        public void AWrongDesign_FailsAtTheMomentsItIsWrong()
        {
            Exercise exercise = Make(HalfAdderJson.Replace(@"{ ""kind"": ""And"", ""count"": 1 }", @"{ ""kind"": ""And"", ""count"": 2 }"));
            Grade grade = ExerciseGrader.Grade(exercise, HalfAdder(exercise, GateKind.And));
            Assert.IsFalse(grade.Solved);
            Assert.IsEmpty(grade.Faults, "two ANDs are within the stock");
            CollectionAssert.AreEqual(new[] { "sum at 14: 0, wanted 1", "sum at 24: 0, wanted 1", "sum at 34: 1, wanted 0" },
                grade.Failures.Select(f => f.ToString()), "what the testbench printed, check by check");
        }

        [Test]
        public void ADesignPastItsStock_IsAFault_EvenWhenItsDocumentLostTheLibrary()
        {
            Exercise exercise = Make(HalfAdderJson);
            Project right = HalfAdder(exercise, GateKind.Xor);
            var editor = new BoardEditor(right with { Settings = right.Settings with { Library = null } });
            Gate(editor, GateKind.Xor, 12, 8);

            Grade grade = ExerciseGrader.Grade(exercise, editor.Project);
            Assert.IsFalse(grade.Solved);
            StringAssert.Contains("The exercise stocks 1 XOR, and the design has 2.", string.Join("\n", grade.Faults));
            Assert.AreEqual(2, grade.Parts["xor"]);
        }

        [Test]
        public void ATimingExercise_IsMetByAFastEnoughDesign_AndNotByASlowOne()
        {
            Exercise quick = Make(HalfAdderJson.Replace(@"""chapter"": 6,", @"""chapter"": 6, ""settleBy"": 1,"));
            Assert.IsTrue(ExerciseGrader.Grade(quick, HalfAdder(quick, GateKind.Xor)).Solved);

            Exercise slow = Make(HalfAdderJson.Replace(@"""chapter"": 6,", @"""chapter"": 6, ""settleBy"": 1, ""delay"": 2,"));
            Grade late = ExerciseGrader.Grade(slow, HalfAdder(slow, GateKind.Xor));
            Assert.IsEmpty(late.Failures, "right at every check");
            Assert.AreEqual(2L, late.Settle);
            Assert.IsFalse(late.SettleMet);
            Assert.IsFalse(late.Solved, "but slower than the exercise allows");
        }

        /// <summary>One clock late: q reads, at each vector, what d was the vector before -- a register on the clock.</summary>
        [Test]
        public void AClockedExercise_HasItsClock_AndARegisterSolvesIt()
        {
            Exercise late = Make(@"{ ""id"": ""one-clock-late"", ""title"": ""One clock late"", ""clock"": true,
                ""inputs"": [ { ""name"": ""d"", ""values"": ""10110"" } ],
                ""outputs"": [ { ""name"": ""q"", ""values"": ""x1011"" } ],
                ""parts"": [ { ""kind"": ""Register"", ""count"": 1 } ] }");
            Project project = Open(late);
            Assert.AreEqual(Exercise.ClockName, project.TopModule.Ports[0].Name, "the clock first");
            Assert.AreEqual(late.Cycle, project.Stimulus.Clocks.Single().Period);

            var editor = new BoardEditor(project);
            EditResult register = editor.Add(project.Top, new RegisterPart(), new Placement(8, 2));
            Assert.IsTrue(register.Done, register.Refusal);
            Wire(editor, Port(editor, "d"), "out", register.Id, "d");
            Wire(editor, Port(editor, Exercise.ClockName), "out", register.Id, "clk");
            Wire(editor, register.Id, "q", Port(editor, "q"), "in");

            Grade grade = ExerciseGrader.Grade(late, editor.Project);
            Assert.IsTrue(grade.Solved, string.Join("\n", grade.Problems.Concat(grade.Faults).Concat(grade.Failures.Select(f => f.ToString()))));
            Assert.AreEqual(4, grade.Checks, "the first vector's x asks nothing");
        }

        [Test]
        public void ItsRegisters_StartWhereTheFileSays_AndItIsTimedInNanoseconds()
        {
            string Clocked(string start) => @"{ ""id"": ""late"", ""title"": ""Late"", ""clock"": true, ""registerStart"": """ + start + @""",
                ""inputs"": [ { ""name"": ""d"", ""values"": ""10"" } ],
                ""outputs"": [ { ""name"": ""q"", ""values"": ""x1"" } ],
                ""parts"": [ { ""kind"": ""Register"", ""count"": 1 } ] }";

            Assert.AreEqual("0", Make(Clocked("0")).RegisterStart?.Format(Radix.Binary));
            Assert.AreEqual("1", Make(Clocked("1")).RegisterStart?.Format(Radix.Binary));
            Assert.IsNull(Make(Clocked("")).RegisterStart, "empty: x, until a reset");
            foreach (string junk in new[] { "x", "2" })
            {
                var problems = new List<string>();
                Assert.IsNull(Exercise.From(Record(Clocked(junk)), problems), junk);
                StringAssert.Contains($"a register cannot start at '{junk}'", string.Join("\n", problems));
            }

            Assert.AreEqual("ns", Open(Make(HalfAdderJson)).Settings.TimeUnit, "its goals and its limits are in ns");
        }

        private const string HalfAdderBlock = "module ha (input logic a, input logic b, output logic s, output logic c);\n" +
            "    xor g1 (s, a, b);\n    and g2 (c, a, b);\nendmodule\n";

        [Test]
        public void ItsBlocks_AreModules_CountedWhole_AndAStartCircuitIsDrawn()
        {
            string json = @"{ ""id"": ""full-adder"", ""title"": ""Full adder"",
                ""inputs"": [ { ""name"": ""a"", ""values"": ""00001111"" }, { ""name"": ""b"", ""values"": ""00110011"" }, { ""name"": ""cin"", ""values"": ""01010101"" } ],
                ""outputs"": [ { ""name"": ""s"", ""values"": ""01101001"" }, { ""name"": ""cout"", ""values"": ""00010111"" } ],
                ""parts"": [ { ""block"": ""ha"", ""count"": 2 }, { ""kind"": ""Or"", ""count"": 1 } ],
                ""blocks"": " + JsonSerializer.Serialize(HalfAdderBlock) + " }";
            Exercise exercise = Make(json);
            Project blank = Open(exercise);
            Assert.IsNotNull(blank.Find("ha"), "the block, drawn as a module of its own");

            var editor = new BoardEditor(blank);
            string top = blank.Top;
            int h1 = editor.Add(top, new BlockPart { Module = "ha" }, new Placement(6, 0)).Id;
            int h2 = editor.Add(top, new BlockPart { Module = "ha" }, new Placement(10, 2)).Id;
            int or = Gate(editor, GateKind.Or, 14, 6);
            Wire(editor, Port(editor, "a"), "out", h1, "a");
            Wire(editor, Port(editor, "b"), "out", h1, "b");
            Wire(editor, h1, "s", h2, "a");
            Wire(editor, Port(editor, "cin"), "out", h2, "b");
            Wire(editor, h2, "s", Port(editor, "s"), "in");
            Wire(editor, h1, "c", or, "a");
            Wire(editor, h2, "c", or, "b");
            Wire(editor, or, "y", Port(editor, "cout"), "in");
            Assert.IsFalse(editor.Add(top, new BlockPart { Module = "ha" }, new Placement(6, 10)).Done, "a third: the exercise stocks two");

            Grade grade = ExerciseGrader.Grade(exercise, editor.Project);
            Assert.IsTrue(grade.Solved, string.Join("\n", grade.Problems.Concat(grade.Faults).Concat(grade.Failures.Select(f => f.ToString()))));
            Assert.AreEqual(2, grade.Parts["ha"]);
            Assert.AreEqual(1, grade.Gates, "the OR: the half adders' gates are theirs");

            // The same, opening on a start circuit: the half adders placed, the OR left to the student.
            string start = "module full_adder (input logic a, input logic b, input logic cin, output logic s, output logic cout);\n" +
                "    logic s1, c1, c2;\n    ha h1 (.a(a), .b(b), .s(s1), .c(c1));\n    ha h2 (.a(s1), .b(cin), .s(s), .c(c2));\nendmodule\n";
            Exercise started = Make(json.Replace(@"""parts"":", @"""start"": " + JsonSerializer.Serialize(start) + @", ""parts"":"));
            Project opened = Open(started);
            Assert.AreEqual(2, opened.TopModule.Parts.OfType<BlockPart>().Count(), "it opens on its circuit");
            Grade unfinished = ExerciseGrader.Grade(started, opened);
            Assert.IsFalse(unfinished.Solved);
            Assert.That(unfinished.Failures.Select(f => f.Port), Has.All.EqualTo("cout"), "its sum is right; its carry is not built yet");
        }

        [Test]
        public void AStartCircuit_IsHeldToTheRules_AndToTheTestsPorts()
        {
            var problems = new List<string>();
            Exercise wrongPorts = Make(HalfAdderJson.Replace(@"""parts"":", @"""start"": ""module half_adder (input logic a, input logic b, output logic sum); endmodule"", ""parts"":"));
            Assert.IsNull(wrongPorts.ToProject(problems));
            StringAssert.Contains("the circuit has no port carry", string.Join("\n", problems));

            problems.Clear();
            Exercise overStock = Make(HalfAdderJson.Replace(@"""parts"":", @"""start"": ""module half_adder (input logic a, input logic b, output logic sum, output logic carry); xor g1 (sum, a, b); xor g2 (carry, a, b); endmodule"", ""parts"":"));
            Assert.IsNull(overStock.ToProject(problems), "a start circuit over its own stock");
            StringAssert.Contains("stocks 1 XOR", string.Join("\n", problems));
        }

        /// <summary>
        /// A run that stops before the test is through is no pass (the #85 review): here a ring of a NAND and two NOTs at no
        /// delay, which once a is 1 has three inversions and never settles, so the engine stops it long before the last check --
        /// which, never made, could never fail.
        /// </summary>
        [Test]
        public void ARunThatStopsBeforeTheTestIsThrough_IsNotSolved()
        {
            Exercise invert = Make(@"{ ""id"": ""invert"", ""title"": ""Invert"", ""delay"": 0,
                ""inputs"": [ { ""name"": ""a"", ""values"": ""0101"" } ], ""outputs"": [ { ""name"": ""y"", ""values"": ""1010"" } ],
                ""parts"": [ { ""kind"": ""Not"" }, { ""kind"": ""Nand"" } ] }");
            var editor = new BoardEditor(Open(invert));
            int not = Gate(editor, GateKind.Not, 8, 0, delay: 0);
            Wire(editor, Port(editor, "a"), "out", not, "a");
            Wire(editor, not, "y", Port(editor, "y"), "in");
            Assert.IsTrue(ExerciseGrader.Grade(invert, editor.Project).Solved, "sanity: the answer alone is right");

            int g1 = Gate(editor, GateKind.Nand, 8, 8, 0), g2 = Gate(editor, GateKind.Not, 12, 8, 0), g3 = Gate(editor, GateKind.Not, 16, 8, 0);
            Wire(editor, Port(editor, "a"), "out", g1, "a");
            Wire(editor, g1, "y", g2, "a");
            Wire(editor, g2, "y", g3, "a");
            Wire(editor, g3, "y", g1, "b");

            Grade grade = ExerciseGrader.Grade(invert, editor.Project);
            Assert.IsFalse(grade.Solved);
            StringAssert.Contains("The test stopped before it had made every check", string.Join("\n", grade.Problems));
        }

        [Test]
        public void ATestThatChecksNothing_OrAnOutputShorterThanTheInputs_IsRefused()
        {
            var problems = new List<string>();
            Assert.IsNull(Exercise.From(Record(HalfAdderJson.Replace(@"""values"": ""0110""", @"""value"": ""0110""")
                .Replace(@"""values"": ""0001""", @"""values"": ""xxxx""")), problems), "a misspelt field leaves the test checking nothing");
            StringAssert.Contains("its test checks nothing", string.Join("\n", problems));

            problems.Clear();
            Assert.IsNull(Exercise.From(Record(HalfAdderJson.Replace(@"""values"": ""0110""", @"""values"": ""011""")), problems));
            StringAssert.Contains("the output carry has 4 values, and sum 3", string.Join("\n", problems));
            StringAssert.Contains("its outputs have 3 values for 4 vectors of inputs", string.Join("\n", problems));
        }

        [Test]
        public void ACycle_LeavesAMomentToSpare_BeforeTheOutputsAreRead()
        {
            var problems = new List<string>();
            Assert.IsNull(Exercise.From(Record(HalfAdderJson.Replace(@"""chapter"": 6,", @"""chapter"": 6, ""cycle"": 4,")), problems));
            StringAssert.Contains("an even length of at least 6", string.Join("\n", problems), "a gate of delay 1 answers at 1; 4 reads at 1");

            // A design that only just meets its limit is read settled: two NOTs in a row, settling at 2, against 2.
            Exercise twice = Make(@"{ ""id"": ""twice"", ""title"": ""Twice"", ""settleBy"": 2,
                ""inputs"": [ { ""name"": ""a"", ""values"": ""0101"" } ], ""outputs"": [ { ""name"": ""y"", ""values"": ""0101"" } ],
                ""parts"": [ { ""kind"": ""Not"", ""count"": 2 } ] }");
            var editor = new BoardEditor(Open(twice));
            int n1 = Gate(editor, GateKind.Not, 6, 0), n2 = Gate(editor, GateKind.Not, 10, 0);
            Wire(editor, Port(editor, "a"), "out", n1, "a");
            Wire(editor, n1, "y", n2, "a");
            Wire(editor, n2, "y", Port(editor, "y"), "in");
            Grade grade = ExerciseGrader.Grade(twice, editor.Project);
            Assert.AreEqual(2L, grade.Settle);
            Assert.IsTrue(grade.Solved, string.Join("\n", grade.Failures.Select(f => f.ToString())));
        }

        [Test]
        public void ABlockStockedWithNoCount_IsCountedWhole_AndSoAreTheModulesInsideIt()
        {
            string fullAdderBlock = HalfAdderBlock + "module fa (input logic a, input logic b, input logic cin, output logic s, output logic cout);\n" +
                "    logic s1, c1, c2;\n    ha h1 (.a(a), .b(b), .s(s1), .c(c1));\n    ha h2 (.a(s1), .b(cin), .s(s), .c(c2));\n" +
                "    or g3 (cout, c1, c2);\nendmodule\n";
            Exercise any = Make(@"{ ""id"": ""any"", ""title"": ""Any"",
                ""inputs"": [ { ""name"": ""a"", ""values"": ""01"" } ], ""outputs"": [ { ""name"": ""y"", ""values"": ""01"" } ],
                ""parts"": [ { ""block"": ""ha"" }, { ""kind"": ""Or"", ""count"": 1 } ],
                ""blocks"": " + JsonSerializer.Serialize(HalfAdderBlock) + " }");
            Open(any);

            Exercise nested = Make(@"{ ""id"": ""nested"", ""title"": ""Nested"",
                ""inputs"": [ { ""name"": ""a"", ""values"": ""01"" } ], ""outputs"": [ { ""name"": ""y"", ""values"": ""01"" } ],
                ""parts"": [ { ""block"": ""fa"", ""count"": 2 } ],
                ""blocks"": " + JsonSerializer.Serialize(fullAdderBlock) + " }");
            Project project = Open(nested);
            CollectionAssert.AreEquivalent(new[] { "fa", "ha" }, BoardRules.StockedInsides(project, project.Settings.Library.Counted()),
                "the half adder is the full adder's, inside it");
        }

        [Test]
        public void WrittenCode_IsAFault_WhereTheExerciseIsAnsweredByDrawing()
        {
            Exercise exercise = Make(HalfAdderJson);
            var editor = new BoardEditor(HalfAdder(exercise, GateKind.Xor));
            Assert.IsTrue(editor.AddWrittenModule("w", "module w (input logic a, output logic y);\n    assign y = a;\nendmodule\n").Done);

            Grade grade = ExerciseGrader.Grade(exercise, editor.Project);
            Assert.IsFalse(grade.Solved);
            StringAssert.Contains("w is written, and this exercise is answered by drawing it.", string.Join("\n", grade.Faults));
        }

        [Test]
        public void TheStock_IsValidated()
        {
            var problems = new List<string>();
            Assert.IsNull(Exercise.From(Record(HalfAdderJson.Replace(@"{ ""kind"": ""And"", ""count"": 1 }",
                @"{ ""kind"": ""And"", ""count"": 1 }, { ""kind"": ""and"", ""inputs"": 3, ""count"": 1 }, { ""kind"": ""1"" }, { ""kind"": ""and,or"" }")), problems));
            string all = string.Join("\n", problems);
            StringAssert.Contains("it stocks and twice", all);
            StringAssert.Contains("'1' is no gate", all);
            StringAssert.Contains("'and,or' is no gate", all);

            problems.Clear();
            Assert.IsNotNull(Exercise.From(Record(HalfAdderJson.Replace(@"{ ""kind"": ""And"", ""count"": 1 }",
                @"{ ""kind"": ""And"", ""count"": 1 }, { ""kind"": ""Bufif1"", ""count"": 2 }")), problems), "a tri-state gate is stocked: " + string.Join("\n", problems));
            Assert.IsNull(Exercise.From(Record(HalfAdderJson.Replace(@"{ ""kind"": ""And"", ""count"": 1 }",
                @"{ ""kind"": ""And"", ""count"": 1 }, { ""kind"": ""Bufif1"", ""inputs"": 3 }")), problems));
            StringAssert.Contains("BitSorter has no BUFIF1 of 3 inputs", string.Join("\n", problems));

            problems.Clear();
            Exercise missing = Make(HalfAdderJson.Replace(@"{ ""kind"": ""And"", ""count"": 1 }", @"{ ""kind"": ""And"", ""count"": 1 }, { ""block"": ""nowhere"" }"));
            Assert.IsNull(missing.ToProject(problems));
            StringAssert.Contains("it stocks the block nowhere, which its blocks do not have", string.Join("\n", problems));

            problems.Clear();
            Exercise itself = Make(HalfAdderJson.Replace(@"{ ""kind"": ""And"", ""count"": 1 }", @"{ ""kind"": ""And"", ""count"": 1 }, { ""block"": ""half_adder"" }"));
            Assert.IsNull(itself.ToProject(problems));
            StringAssert.Contains("the top, half_adder, cannot be a block it stocks", string.Join("\n", problems));
        }

        [Test]
        public void AStartCircuit_OfPlainPrimitives_TakesItsCellsDelays()
        {
            Exercise exercise = Make(HalfAdderJson.Replace(@"""parts"":",
                @"""delay"": 3, ""start"": ""module half_adder (input logic a, input logic b, output logic sum, output logic carry); xor g1 (sum, a, b); and g2 (carry, a, b); endmodule"", ""parts"":"));
            Project project = Open(exercise);
            Assert.That(project.TopModule.Parts.OfType<GatePart>().Select(g => (g.Rise, g.Fall)), Is.All.EqualTo((3L, 3L)));
            Assert.IsTrue(ExerciseGrader.Grade(exercise, project).Solved, "the start circuit here is already the answer");
        }

        [Test]
        public void AnExercisesTest_PrintsAsASelfCheckingTestbench()
        {
            Exercise exercise = Make(HalfAdderJson);
            CompiledProject compiled = BoardCompiler.Compile(HalfAdder(exercise, GateKind.Xor));
            string bench = BoardPrinter.Print(compiled).Single(f => f.Path.Contains("_tb")).Text;
            StringAssert.Contains("$display(\"%0d of 8 checks failed\", errors);", bench);
            StringAssert.Contains("if ((sum ==? 1'b1) !== 1'b1)", bench);
        }

        /// <summary>
        /// The two timing exercises' limits can be missed (Part 15g, decision 4): their stock builds a slow answer that is
        /// right at every check and late, and CHECK fails it on time. Before, the parts they gave could only meet them.
        /// </summary>
        [TestCase("the-slow-lane", @"module the_slow_lane (input logic a, input logic b, input logic c, output logic out);
    logic g1_y, g2_y, g3_y;
    xor #1 g1 (g1_y, a, b);
    xor #1 g2 (g2_y, g1_y, c);
    and #1 g3 (g3_y, g2_y, a);
    or #1 g4 (out, g3_y, b);
endmodule", 4L)]
        [TestCase("balance-the-paths", @"module balance_the_paths (input logic a, input logic b, output logic out);
    logic g1_y, g3_y;
    xor #1 g1 (g1_y, a, b);
    buf #1 g3 (g3_y, g1_y);
    and #1 g2 (out, g3_y, b);
endmodule", 3L)]
        public void ASlowAnswer_IsRightButLate_AndFailsOnTime(string id, string slow, long settles)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            Grade grade = ExerciseGrader.Grade(exercise, ExerciseFiles.Drawn(exercise, slow));
            Assert.IsEmpty(grade.Failures, "right at every check");
            Assert.AreEqual(settles, grade.Settle, "and late");
            Assert.IsFalse(grade.SettleMet);
            Assert.IsFalse(grade.Solved, "so CHECK fails it on time");
        }

        /// <summary>Balance the paths without its buffer still settles in time: the buffer balances, it is not needed to pass.</summary>
        [Test]
        public void BalanceThePaths_WithoutItsBuffer_StillPasses()
        {
            Exercise exercise = ExerciseFiles.Load("balance-the-paths");
            Grade grade = ExerciseGrader.Grade(exercise, ExerciseFiles.Drawn(exercise, @"module balance_the_paths (input logic a, input logic b, output logic out);
    logic g1_y;
    xor #1 g1 (g1_y, a, b);
    and #1 g2 (out, g1_y, b);
endmodule"));
            Assert.IsTrue(grade.Solved);
        }
    }
}
