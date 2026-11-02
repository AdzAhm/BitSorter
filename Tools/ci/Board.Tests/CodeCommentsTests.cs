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
        }

        [Test]
        public void AModulesComment_IsOverItsModule_AndAFilesHeaderAboveABlankLineIsNot()
        {
            Assert.AreEqual("The ALU: adds, subtracts and compares a and b as op says.", CodeComments.OverModule(Code));
            Assert.IsNull(CodeComments.OverModule("// a header\n\nmodule m;\nendmodule\n"), "a blank line between: the file's, not the module's");
            Assert.IsNull(CodeComments.OverModule("module m;\nendmodule\n"));
            Assert.AreEqual("Two lines over it.", CodeComments.OverModule("// Two lines\n// over it.\nmodule m;\nendmodule\n"));
        }
    }
}
