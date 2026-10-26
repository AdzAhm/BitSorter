using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Course templates (Part 13e): a homework file imported is remembered as its module's template, kept as code with its
    /// markers, and EXPORT FOR SUBMISSION hands it back as given with the student's code between them -- refused, saying
    /// why, when the ports, the markers or the code outside them are no longer the template's. Every template here is
    /// written for these tests in the course's style; none is the course's own.
    /// </summary>
    public class SubmissionTests
    {
        internal const string MuxTemplate =
            "// Homework: a 2-to-1 multiplexer. Written for BitSorter's tests, in the course's style.\n" +
            "module mux2 (\n" +
            "    input  logic a, b, s,\n" +
            "    output logic y\n" +
            ");\n" +
            "    // Put your code here\n" +
            "\n" +
            "    // End of your code\n" +
            "endmodule\n";

        internal const string StudentsCode = "    assign y = s ? b : a;\n";

        private static readonly DateTime When = new DateTime(2026, 10, 26, 18, 0, 0);

        private static BoardEditor Imported(params (string Path, string Text)[] files)
        {
            ProjectImport import = ProjectImporter.Import(files.Select(f => new SourceFile(f.Path, f.Text)).ToList());
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            return new BoardEditor(import.Project);
        }

        /// <summary>The module's text with <paramref name="code"/> between its markers, as a student would write it.</summary>
        private static string Written(Module module, string code)
        {
            Assert.IsTrue(CourseTemplate.TryRead(module.Source, out CourseTemplate template));
            return module.Source.Substring(0, template.RegionStart) + code + module.Source.Substring(template.RegionEnd);
        }

        private static Dictionary<string, string> Unzipped(byte[] zip)
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            return archive.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open(), Encoding.UTF8).ReadToEnd());
        }

        [Test]
        public void AnImportedTemplate_IsRemembered_AndKeptAsCodeWithItsMarkers()
        {
            Module mux = Imported(("mux2.sv", MuxTemplate)).Project.Find("mux2");

            Assert.IsTrue(mux.IsWritten, "written, though an empty body could be drawn: the student's code goes between the markers");
            Assert.AreEqual("mux2.sv", mux.Template.File);
            Assert.AreEqual(MuxTemplate, mux.Template.Text, "every character as given");
            Assert.IsTrue(CourseTemplate.TryRead(mux.Source, out _), "its text keeps the markers");
        }

        [Test]
        public void ASubmission_IsTheTemplateAsGiven_WithTheStudentsCodeBetweenItsMarkers()
        {
            BoardEditor editor = Imported(("mux2.sv", MuxTemplate));
            Assert.IsTrue(editor.SetSource("mux2", Written(editor.Project.Find("mux2"), StudentsCode)).Done);

            Submission submission = SubmissionExport.Build(editor.Project, When);

            Assert.IsTrue(submission.Succeeded, submission.Refusal);
            CollectionAssert.AreEqual(new[] { "mux2.sv" }, submission.Files);
            Assert.AreEqual(MuxTemplate.Replace("    // Put your code here\n\n", "    // Put your code here\n" + StudentsCode),
                Unzipped(submission.Zip)["mux2-submission/mux2.sv"]);
        }

        [Test]
        public void PortsThatAreNoLongerTheTemplates_AreRefused_SayingHow()
        {
            BoardEditor editor = Imported(("mux2.sv", MuxTemplate));
            string widened = Written(editor.Project.Find("mux2"), StudentsCode).Replace("output logic y", "output logic [1:0] y");
            Assert.IsTrue(editor.SetSource("mux2", widened).Done);

            Submission submission = SubmissionExport.Build(editor.Project, When);

            Assert.IsFalse(submission.Succeeded);
            StringAssert.Contains("ports are no longer mux2.sv's", submission.Refusal);
            StringAssert.Contains("output [1:0] y", submission.Refusal);
        }

        [Test]
        public void ACodeWithoutTheMarkers_OrChangedOutsideThem_IsRefused()
        {
            BoardEditor editor = Imported(("mux2.sv", MuxTemplate));
            string unmarked = "module mux2 (\n    input  logic a, b, s,\n    output logic y\n);\n" + StudentsCode + "endmodule\n";
            Assert.IsTrue(editor.SetSource("mux2", unmarked).Done);
            StringAssert.Contains("lost the template's markers", SubmissionExport.Build(editor.Project, When).Refusal);

            editor.Undo();
            string outside = Written(editor.Project.Find("mux2"), StudentsCode).Replace(");\n    // Put your code here", ");\n    logic spare;\n    // Put your code here");
            Assert.IsTrue(editor.SetSource("mux2", outside).Done);
            StringAssert.Contains("changed outside the template's markers", SubmissionExport.Build(editor.Project, When).Refusal);
        }

        [Test]
        public void ACommentChangedOutsideTheMarkers_IsRefused_RatherThanLostFromTheSubmission()
        {
            BoardEditor editor = Imported(("mux2.sv", MuxTemplate));
            string noted = Written(editor.Project.Find("mux2"), StudentsCode).Replace("    input  logic a, b, s,", "    input  logic a, b, s,   // a when s is 0");
            Assert.IsTrue(editor.SetSource("mux2", noted).Done);

            StringAssert.Contains("changed outside the template's markers, its code or its comments", SubmissionExport.Build(editor.Project, When).Refusal);

            editor.Undo();
            string respaced = Written(editor.Project.Find("mux2"), StudentsCode).Replace("    input  logic a, b, s,\n", "    input logic a, b, s,\r\n");
            Assert.IsTrue(editor.SetSource("mux2", respaced).Done);
            Assert.IsTrue(SubmissionExport.Build(editor.Project, When).Succeeded, "spacing and line endings are not a change");
        }

        /// <summary>
        /// A template that includes a file of parameters above its module, as three of the course's do (none uses them in its
        /// header, which the import refuses): the import moves the include into the module, and the module untouched outside
        /// the markers is still the template's -- handed in with the include where the course had it.
        /// </summary>
        [Test]
        public void ATemplateThatIncludesItsParameters_IsHandedInWithItsIncludeWhereTheCourseHadIt()
        {
            const string widths = "parameter W = 4;\n";
            const string adder =
                "`include \"widths.inc\"\n" +
                "// Homework: add two 4-bit numbers, W bits each. Written for BitSorter's tests, in the course's style.\n" +
                "module add4 (\n    input  logic [3:0] a, b,\n    output logic [4:0] s\n);\n" +
                "    // Put your code here\n\n    // End of your code\nendmodule\n";
            const string code = "    logic [W:0] sum;\n    assign sum = a + b;\n    assign s = sum;\n";
            BoardEditor editor = Imported(("add4.sv", adder), ("widths.inc", widths));
            Module add4 = editor.Project.Find("add4");
            Assert.AreNotEqual(add4.Template.Text, add4.Template.Given, "sanity: the import moved the include, so the module is not the file");
            Assert.IsTrue(editor.SetSource("add4", Written(add4, code)).Done);

            Submission submission = SubmissionExport.Build(editor.Project, When);

            Assert.IsTrue(submission.Succeeded, submission.Refusal);
            Assert.AreEqual(adder.Replace("    // Put your code here\n\n", "    // Put your code here\n" + code),
                Unzipped(submission.Zip)["add4-submission/add4.sv"]);
        }

        /// <summary>
        /// The other three of the course's that include a file have no end marker, and the import moves the include into the
        /// module's region there: it is handed in once, where the course had it, not again among the student's code.
        /// </summary>
        [Test]
        public void ATemplateWithNoEndThatIncludesItsParameters_HandsInTheIncludeOnce()
        {
            const string widths = "parameter W = 4;\n";
            const string adder =
                "`include \"widths.inc\"\n" +
                "module add4 (input logic [3:0] a, b, output logic [4:0] s);\n" +
                "    // put your code here\n\nendmodule\n";
            BoardEditor editor = Imported(("add4.sv", adder), ("widths.inc", widths));
            StringAssert.Contains("`include \"widths.inc\"", CourseTemplate.RegionIn(editor.Project.Find("add4").Source),
                "sanity: the import put the include where the student's code goes");
            Assert.IsTrue(editor.SetSource("add4", Written(editor.Project.Find("add4"), "    assign s = a + b;\n")).Done);

            Submission submission = SubmissionExport.Build(editor.Project, When);

            Assert.IsTrue(submission.Succeeded, submission.Refusal);
            Assert.AreEqual(adder.Replace("    // put your code here\n\n", "    // put your code here\n    assign s = a + b;\n"),
                Unzipped(submission.Zip)["add4-submission/add4.sv"]);
        }

        /// <summary>Every one of the course's files ends its lines in CR LF: the student's lines are ended the same, not in a bare LF among them.</summary>
        [Test]
        public void ATemplateInCrLf_IsHandedInWithEveryLineEndedInCrLf()
        {
            BoardEditor editor = Imported(("mux2.sv", MuxTemplate.Replace("\n", "\r\n")));
            Assert.IsTrue(editor.SetSource("mux2", Written(editor.Project.Find("mux2"), StudentsCode)).Done);

            string handedIn = Unzipped(SubmissionExport.Build(editor.Project, When).Zip)["mux2-submission/mux2.sv"];

            Assert.AreEqual(MuxTemplate.Replace("    // Put your code here\n\n", "    // Put your code here\n" + StudentsCode).Replace("\n", "\r\n"), handedIn);
        }

        [Test]
        public void TwoModulesFromOneFile_OrTwoFilesOfOneName_AreRefused_RatherThanOneOfThemLost()
        {
            const string both = "module a (input logic x, output logic y);\n    // Put your code here\n    // End of your code\nendmodule\n" +
                "module b (input logic x, output logic y);\n    // Put your code here\n    // End of your code\nendmodule\n";
            StringAssert.Contains("both.sv holds two modules to write, a and b", SubmissionExport.Build(Imported(("both.sv", both)).Project, When).Refusal);

            const string first = "module first (input logic x, output logic y);\n    // Put your code here\n    // End of your code\nendmodule\n";
            Project twoHomeworks = Imported(("hw1/top.sv", first), ("hw2/top.sv", first.Replace("first", "second"))).Project;
            StringAssert.Contains("first and second both came from a file named top.sv", SubmissionExport.Build(twoHomeworks, When).Refusal);
        }

        [Test]
        public void AModuleRenamed_IsRefused_SayingTheNameItIsGradedBy()
        {
            BoardEditor editor = Imported(("mux2.sv", MuxTemplate));
            Assert.IsTrue(editor.SetSource("mux2", Written(editor.Project.Find("mux2"), StudentsCode)).Done);
            Assert.IsTrue(editor.RenameModule("mux2", "mux3").Done);

            StringAssert.Contains("mux3 is mux2 in mux2.sv, and is graded by that name: rename it back to mux2", SubmissionExport.Build(editor.Project, When).Refusal);
        }

        [Test]
        public void AModuleTheCodeUses_ThatIsNotHandedIn_IsNamed()
        {
            BoardEditor editor = Imported(("mux2.sv", MuxTemplate));
            Assert.IsTrue(editor.AddWrittenModule("pick", "module pick (input logic a, b, s, output logic y);\n    assign y = s ? b : a;\nendmodule\n").Done);
            Assert.IsTrue(editor.SetSource("mux2", Written(editor.Project.Find("mux2"), "    pick p (.a(a), .b(b), .s(s), .y(y));\n")).Done);

            Submission submission = SubmissionExport.Build(editor.Project, When);

            Assert.IsTrue(submission.Succeeded, submission.Refusal);
            CollectionAssert.AreEqual(new[] { "pick" }, submission.NotHandedIn, "pick is not in the zip, and the student is told");
        }

        [Test]
        public void ATemplatesFileName_FromAHandEditedSave_IsHeldToAFilesName()
        {
            Project project = Imported(("mux2.sv", MuxTemplate)).Project;
            Module mux = project.Find("mux2");
            Project edited = project.WithModule(mux with { Template = mux.Template with { File = "../../elsewhere.sv" } });

            Submission submission = SubmissionExport.Build(edited, When);

            Assert.IsFalse(submission.Succeeded, "never a path out of the zip's folder");
            StringAssert.Contains("has no name a file can have", submission.Refusal);
        }

        [Test]
        public void ATemplateWithALowercaseOpenerAndNoEnd_IsFilledUpToEndmodule()
        {
            const string parity = "module parity (input logic [7:0] d, output logic odd);\n    // put your code here\n\nendmodule\n";
            BoardEditor editor = Imported(("parity.sv", parity));
            Assert.IsTrue(editor.SetSource("parity", Written(editor.Project.Find("parity"), "    assign odd = ^d;\n")).Done);

            Submission submission = SubmissionExport.Build(editor.Project, When);

            Assert.IsTrue(submission.Succeeded, submission.Refusal);
            Assert.AreEqual("module parity (input logic [7:0] d, output logic odd);\n    // put your code here\n    assign odd = ^d;\nendmodule\n",
                Unzipped(submission.Zip)["parity-submission/parity.sv"]);
        }

        [Test]
        public void AModulesTemplate_IsSaved_AndReadBack()
        {
            Project project = Imported(("mux2.sv", MuxTemplate)).Project;
            var fields = new JsonSerializerOptions { IncludeFields = true };
            var problems = new List<string>();
            Project again = JsonSerializer.Deserialize<ProjectRecord>(JsonSerializer.Serialize(ProjectRecord.From(project), fields), fields).ToProject(problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(project.Find("mux2").Template, again.Find("mux2").Template);
        }

        [Test]
        public void WithNoTemplate_ThereIsNothingToHandIn()
        {
            Submission submission = SubmissionExport.Build(SampleBoards.Build("half_adder"), When);

            Assert.IsFalse(submission.Succeeded);
            StringAssert.Contains("No module here came from a course template", submission.Refusal);
        }
    }
}
