using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// What written code says of its own lines (Ahmad, 2026-11-02: every part of the processors described): a line's comment
    /// is at its end, else in the comment lines right above it, and a module's is over its <c>module</c>.
    /// </summary>
    public class CodeCommentsTests
    {
        private const string Code =
            "// The file's own header, which no part takes.\n" +
            "\n" +
            "// The ALU: adds, subtracts and compares a and b\n" +
            "// as op says.\n" +
            "module alu (\n" +
            "    input  logic [31:0] a,    // the first operand\n" +
            "    input  logic [31:0] b,\n" +
            "    output logic [31:0] y     // \"the result\" -- // and all\n" +
            ");\n" +
            "    // ----------------------------------\n" +
            "    assign y = a + b;\n" +
            "\n" +
            "    // The next PC: the target when taken,\n" +
            "    // else PC + 4.\n" +
            "    assign n = t ? x : \"// not a comment\";\n" +
            "    logic z; assign z = a == b;\n" +
            "endmodule\n";

        [Test]
        public void ALinesComment_IsAtItsEnd_ElseRightAboveIt_AndADividerIsNone()
        {
            Assert.AreEqual("the first operand", CodeComments.For(Code, 6), "at the end of the line");
            Assert.AreEqual("\"the result\" -- // and all", CodeComments.For(Code, 8), "the first // is where the comment starts");
            Assert.IsNull(CodeComments.For(Code, 7), "b says nothing: the line above is code");
            Assert.IsNull(CodeComments.For(Code, 11), "a line of rule characters is a divider, not a comment");
            Assert.AreEqual("The next PC: the target when taken, else PC + 4.", CodeComments.For(Code, 15),
                "the comment lines right above, joined, and a // in a string is no comment");
            Assert.IsNull(CodeComments.For(Code, 16), "code above ends the lines that may speak for it");
            Assert.IsNull(CodeComments.For(Code, 0));
            Assert.IsNull(CodeComments.For(Code, 99));
            Assert.IsNull(CodeComments.For(null, 1));

            const string Cut = "    // The PC.\n    `include \"rv32i.svh\"\n\n    always_ff @(posedge clk) pc <= next;\n";
            Assert.AreEqual("The PC.", CodeComments.For(Cut, 4),
                "an include an import put at the head of a module's body, and the blank line after it, are passed over");
            Assert.IsNull(CodeComments.For("    // Not this.\n\n    assign y = a;\n", 3), "a blank line of the code's own still ends it");
            Assert.AreEqual("About y.", CodeComments.For("// The module's.\n`include \"defs.svh\"\n\n// About y.\nassign y = a;\n", 5),
                "an include of the code's own, above the comment, ends it: only one right over the line is passed over (the M3 review)");
            Assert.AreEqual("The PC.", CodeComments.For("// The PC.\r\n`include \"rv32i.svh\"\r\n\r\nalways_ff @(posedge clk) pc <= n;\r\n", 4),
                "written with CRLF");
        }

        [Test]
        public void WhatIsNotAComment_IsNotTaken()   // the M3 review
        {
            Assert.IsNull(CodeComments.For("// assign y = b;\nassign y = a;\n", 2), "a line of code commented out");
            Assert.AreEqual("A read is immediate; a write waits.", CodeComments.For("// A read is immediate;\n// a write waits.\nassign y = a;\n", 3),
                "a line of prose may end in a semicolon");
            Assert.IsNull(CodeComments.For("// ===== Fetch stage =====\nassign y = a;\n", 2), "a banner is a divider");
            Assert.AreEqual("the pc", CodeComments.For("// ---- Fetch ----\n// the pc\nassign y = a;\n", 3), "and ends what is above it");
            Assert.AreEqual("doc", CodeComments.For("/// doc\nassign y = a;\n", 2), "a /// comment's third slash is not a word");
            Assert.IsNull(CodeComments.For("assign y = a; /* see http://foo.org */\n", 1), "a // inside a /* */ comment");
            Assert.AreEqual("real", CodeComments.For("assign y = a; /* x */ // real\n", 1));
            Assert.AreEqual("real", CodeComments.For("initial $display(\"a\\\\\"); // real\n", 1), "an escaped backslash ends no string");
            Assert.AreEqual("real", CodeComments.For("initial $display(\"say \\\"//\\\"\"); // real\n", 1), "an escaped quote ends none either");
        }

        [Test]
        public void AOneLineDescription_TakesTheCommentsFirstSentence()
        {
            Assert.AreEqual("The PC: the address of the instruction.", CodeComments.FirstSentence("The PC: the address of the instruction. It steps on by four."));
            Assert.AreEqual("one sentence, x0 and 4 KiB", CodeComments.FirstSentence("one sentence, x0 and 4 KiB"));
            Assert.AreEqual("A choice, e.g. Fetch or Decode, made here.", CodeComments.FirstSentence("A choice, e.g. Fetch or Decode, made here. Then more."),
                "e.g. ends no sentence");
            Assert.AreEqual("Reset sends the PC back to 0.", CodeComments.FirstSentence("Reset sends the PC back to 0."));
        }

        [Test]
        public void AModulesComment_IsOverItsModule_AndAFilesHeaderAboveABlankLineIsNot()
        {
            Assert.AreEqual("The ALU: adds, subtracts and compares a and b as op says.", CodeComments.OverModule(Code));
            Assert.IsNull(CodeComments.OverModule("// a header\n\nmodule m;\nendmodule\n"), "a blank line between: the file's, not the module's");
            Assert.IsNull(CodeComments.OverModule("module m;\nendmodule\n"));
            Assert.AreEqual("Two lines over it.", CodeComments.OverModule("// Two lines\n// over it.\nmodule m;\nendmodule\n"));
            Assert.AreEqual("at its end", CodeComments.OverModule("module m (input a);   // at its end\nendmodule\n"),
                "else one at the end of its module line");
        }
    }
}
