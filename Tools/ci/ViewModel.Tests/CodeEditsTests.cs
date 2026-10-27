using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The code editor's edits of whole lines (Part 14g): Tab and Shift+Tab indent and outdent by four spaces the lines a
    /// selection touches, and Ctrl+/ comments them out and back in -- as the browser's editor does -- each keeping the
    /// selection on the same characters.
    /// </summary>
    public class CodeEditsTests
    {
        private const string Code = "module m;\nassign y = a;\n\nassign z = b;\nendmodule\n";

        [Test]
        public void Tab_IndentsTheCaretsLine_ByFourSpaces_AndKeepsTheCaretOnItsCharacter()
        {
            int caret = Code.IndexOf("y =");
            CodeEdit edit = CodeEdits.Indent(Code, caret, caret);
            Assert.AreEqual("module m;\n    assign y = a;\n\nassign z = b;\nendmodule\n", edit.Text);
            Assert.AreEqual(caret + 4, edit.Start);
            Assert.AreEqual('y', edit.Text[edit.End], "the caret is still before the y");
        }

        [Test]
        public void Tab_IndentsEveryLineTheSelectionTouches_ButNotABlankOne_NorTheLineItEndsAtTheStartOf()
        {
            int start = Code.IndexOf("assign y"), end = Code.IndexOf("endmodule");
            CodeEdit edit = CodeEdits.Indent(Code, start, end);
            Assert.AreEqual("module m;\n    assign y = a;\n\n    assign z = b;\nendmodule\n", edit.Text);
            Assert.AreEqual("assign y = a;\n\n    assign z = b;\n", edit.Text.Substring(edit.Start, edit.End - edit.Start),
                "the selection still runs from the first line's text to the start of endmodule");
        }

        [Test]
        public void ShiftTab_OutdentsByUpToFourSpaces_OrOneTab_AndTabThenShiftTabIsWhereItStarted()
        {
            CodeEdit outdented = CodeEdits.Outdent("      x;\n  y;\n\tz;\nw;", 0, 20);
            Assert.AreEqual("  x;\ny;\nz;\nw;", outdented.Text);

            int caret = Code.IndexOf("z =");
            CodeEdit there = CodeEdits.Indent(Code, caret, caret);
            CodeEdit back = CodeEdits.Outdent(there.Text, there.Start, there.End);
            Assert.AreEqual(Code, back.Text);
            Assert.AreEqual(caret, back.Start);
        }

        [Test]
        public void ShiftTab_WithTheCaretInTheIndentation_PutsItAtTheLinesStart_NotBeforeIt()
        {
            string text = "a;\n    b;\n";
            CodeEdit edit = CodeEdits.Outdent(text, 5, 5);   // two spaces into the indentation
            Assert.AreEqual("a;\nb;\n", edit.Text);
            Assert.AreEqual(3, edit.Start, "at the line's start, never on the line before");
        }

        [Test]
        public void CtrlSlash_CommentsTheLinesOut_AtTheShallowestIndentation_AndBackIn()
        {
            string text = "    if (a)\n        y = b;\n\n    z = c;\n";
            CodeEdit commented = CodeEdits.ToggleComment(text, 0, text.Length);
            Assert.AreEqual("    // if (a)\n    //     y = b;\n\n    // z = c;\n", commented.Text, "lined up, the blank line left");

            CodeEdit back = CodeEdits.ToggleComment(commented.Text, commented.Start, commented.End);
            Assert.AreEqual(text, back.Text);
        }

        [Test]
        public void CtrlSlash_OnAMixOfCommentedAndNot_CommentsThemAll_AndALoneCaretTakesItsLine()
        {
            string text = "// a;\nb;\n";
            Assert.AreEqual("// // a;\n// b;\n", CodeEdits.ToggleComment(text, 0, text.Length).Text);

            int caret = text.IndexOf('b');
            CodeEdit one = CodeEdits.ToggleComment(text, caret, caret);
            Assert.AreEqual("// a;\n// b;\n", one.Text);
            Assert.AreEqual('b', one.Text[one.Start], "the caret stays before the b, past what was put in front of it");
            Assert.AreEqual("a;\nb;\n", CodeEdits.ToggleComment(text, 0, 0).Text, "the first line uncommented");
        }

        /// <summary>
        /// Tab with a caret on an empty line indents it -- Enter does not indent, so a fresh line is where Tab is pressed -- but
        /// in a selection of several, a blank line, or one of spaces, is left as it is (the 14g review).
        /// </summary>
        [Test]
        public void Tab_OnAnEmptyLine_IndentsIt_ButBlankLinesInASelectionAreLeft()
        {
            string text = "module m;\n\nendmodule\n";
            CodeEdit edit = CodeEdits.Indent(text, 10, 10);
            Assert.AreEqual("module m;\n    \nendmodule\n", edit.Text);
            Assert.AreEqual(14, edit.Start, "the caret after the indent, where the code goes");

            Assert.AreEqual("    a;\n  \n    b;\n", CodeEdits.Indent("a;\n  \nb;\n", 0, 9).Text);
        }

        /// <summary>
        /// A Windows line's carriage return, which a paste keeps, is no content: a blank CRLF line is not commented, and a
        /// commented block with one in it comes back in (the 14g review found it could never be uncommented).
        /// </summary>
        [Test]
        public void CRLFLines_CommentAndComeBackIn_AsLFLinesDo()
        {
            string text = "a;\r\n\r\nb;\r\n";
            CodeEdit commented = CodeEdits.ToggleComment(text, 0, text.Length);
            Assert.AreEqual("// a;\r\n\r\n// b;\r\n", commented.Text);
            Assert.AreEqual(text, CodeEdits.ToggleComment(commented.Text, 0, commented.Text.Length).Text);
            Assert.AreEqual("    a;\r\n\r\n    b;\r\n", CodeEdits.Indent(text, 0, text.Length).Text);
        }

        [Test]
        public void TheEdits_TakeAnyRangeOrder_AndOutOfRangeEnds_WithoutThrowing()
        {
            Assert.AreEqual(CodeEdits.Indent(Code, 3, 20).Text, CodeEdits.Indent(Code, 20, 3).Text);
            Assert.DoesNotThrow(() => CodeEdits.Indent(Code, -5, 999));
            Assert.AreEqual("", CodeEdits.ToggleComment("", 0, 0).Text);
            Assert.AreEqual("\n\n", CodeEdits.ToggleComment("\n\n", 0, 2).Text, "only blank lines: nothing to do");
        }
    }
}
