using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Part L6: packages, strings and files. <c>corpus/behavioural/library.sv</c> holds what Icarus runs as
    /// well; these hold, by the standard, what Icarus 12 cannot run (Docs/oracle-notes.md) -- a string's
    /// toupper, getc, compare and putc, a task called as <c>pkg::t</c> -- and BitSorter's own limits and refusals.
    /// </summary>
    public class LibraryTests
    {
        private static Elaboration Elaborate(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, null);
        }

        private static Simulator Run(string text, MemorySimFiles files = null, long until = 100)
        {
            Elaboration e = Elaborate(text);
            Assert.IsFalse(e.HasErrors, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, new SimOptions { Files = files });
            sim.RunTo(until);
            return sim;
        }

        private static string[] Printed(Simulator sim, Severity severity = Severity.Info) =>
            sim.History.Messages.Where(m => m.Severity == severity && !m.Text.StartsWith("$")).Select(m => m.Text).ToArray();

        // -----------------------------------------------------------------
        // Strings (IEEE 1800, 6.16)
        // -----------------------------------------------------------------

        [Test]
        public void AStringsMethods_ThatIcarusLacks_WorkAsTheStandardSays()
        {
            CollectionAssert.AreEqual(new[] { "ADDER_1 adder_1 100 100", "-1 0 1 0", "+dder_1 255 5", "ff 377 11111111" }, Printed(Run(@"
module t;
    string s = ""Adder_1"", u;
    initial begin
        $display(""%s %s %0d %0d"", s.toupper(), s.tolower(), s.getc(1), s.getc(2));
        $display(""%0d %0d %0d %0d"", s.compare(""B""), s.icompare(""ADDER_1""), s.compare(""Adder""), s.getc(99));
        s.putc(0, ""+"");
        u = ""ff"";
        $display(""%s %0d %0d"", s, u.atohex(), u.len() + 3);
        u.hextoa(255);
        s.octtoa(255);
        $display(""%s %s %s"", u, s, $sformatf(""%b"", 8'hFF));
    end
endmodule")));
        }

        [Test]
        public void StringsCompareAsText_DropZeroBytes_AndKeepTheirFirstCharacters_PastTheRoom()
        {
            CollectionAssert.AreEqual(new[] { "1 1 0", "[A] 1", "256 a z" }, Printed(Run(@"
module t;
    string a = ""ab"", b = ""b"", s, t;
    logic [15:0] packed_a = 16'h4100;
    initial begin
        $display(""%0d %0d %0d"", a < b, ""abc"" < ""abd"", a == b);
        s = packed_a;
        $display(""[%s] %0d"", s, s.len());
        t = ""a"";
        repeat (299) t = {t, ""z""};
        $display(""%0d %c %c"", t.len(), t.getc(0), t.getc(255));
    end
endmodule")));
        }

        [Test]
        public void AFunctionGivesAString_AndAMemoryHoldsThem_AndSwriteFillsOne()
        {
            CollectionAssert.AreEqual(new[] { "#3 #4", "tag #3 and           4" }, Printed(Run(@"
module t;
    string names [2];
    string joined;
    function string tag(input int n);
        return $sformatf(""#%0d"", n);
    endfunction
    initial begin
        names[0] = tag(3);
        names[1] = tag(4);
        $display(""%s %s"", names[0], names[1]);
        $swrite(joined, ""tag "", names[0], "" and "", 4);
        $display(""%s"", joined);
    end
endmodule")));
        }

        // -----------------------------------------------------------------
        // Packages (IEEE 1800, 26)
        // -----------------------------------------------------------------

        [Test]
        public void ATaskInAPackage_IsCalledByItsPackagesName_AndALocalNameWinsOverAnImport()
        {
            CollectionAssert.AreEqual(new[] { "said 5", "local 1" }, Printed(Run(@"
package talk;
    parameter int LIMIT = 9;
    task automatic say(input int n);
        $display(""said %0d"", n);
    endtask
endpackage

module t;
    import talk::*;
    localparam int LIMIT = 1;
    initial begin
        talk::say(5);
        $display(""local %0d"", LIMIT);
    end
endmodule")));
        }

        [TestCase("package p; parameter int A = 1; endpackage\npackage q; import p::*; endpackage\nmodule t; int y; initial y = q::A; endmodule", ElaborationCodes.UnknownName)]
        [TestCase("module t; import nowhere::*; endmodule", ElaborationCodes.UnknownName)]
        [TestCase("package p; parameter int A = 1; endpackage\nmodule t; import p::B; endmodule", ElaborationCodes.UnknownName)]
        [TestCase("package p; logic shared; endpackage\nmodule t; import p::*; endmodule", ElaborationCodes.NotYet)]
        [TestCase("package p; parameter int A = 1; endpackage\nmodule t; int A; import p::A; endmodule", ElaborationCodes.Duplicate)]
        [TestCase("package p; import q::*; endpackage\npackage q; import p::*; endpackage\nmodule t; import p::*; endmodule", ElaborationCodes.Recursion)]
        public void APackageNameThatLeadsNowhere_IsRefused_WithWhy(string text, string code)
        {
            Elaboration e = Elaborate(text);
            CollectionAssert.Contains(e.Diagnostics.Select(d => d.Code).ToArray(), code, string.Join("\n", e.Diagnostics));
        }

        // -----------------------------------------------------------------
        // Files (IEEE 1800, 21.3)
        // -----------------------------------------------------------------

        [Test]
        public void AFile_IsWrittenOutAsTheRunHandsBack_AndStandardOutputIsTheTranscript()
        {
            var files = new MemorySimFiles();
            Simulator sim = Run(@"
module t;
    integer fd;
    initial begin
        fd = $fopen(""log.txt"", ""w"");
        $fdisplay(fd, ""first"");
        $fdisplay(32'h8000_0001, ""to standard output"");
        #5 $fdisplay(fd, ""second"");
    end
endmodule", files, until: 2);

            Assert.IsTrue(files.TryRead("log.txt", out string text));
            Assert.AreEqual("first\n", text, "written as far as the run has got");
            CollectionAssert.AreEqual(new[] { "to standard output" }, Printed(sim));

            sim.RunTo(10);
            files.TryRead("log.txt", out text);
            Assert.AreEqual("first\nsecond\n", text);
        }

        [Test]
        public void OpeningAFileToRead_GivesNothing_AndSaysWhy()
        {
            Simulator sim = Run(@"
module t;
    integer fd;
    initial begin
        fd = $fopen(""data.txt"", ""r"");
        $display(""%0d"", fd);
    end
endmodule", new MemorySimFiles().Add("data.txt", "1\n"));

            CollectionAssert.AreEqual(new[] { "0" }, Printed(sim));
            StringAssert.Contains("reading one is not supported yet", sim.History.Messages.Single(m => m.Severity == Severity.Error).Text);
        }

        [TestCase("integer fd; assign fd = $fopen(\"x.txt\", \"w\");", ElaborationCodes.NotYet)]
        [TestCase("integer fd; initial $fmonitor(fd, \"%d\", fd);", ElaborationCodes.NotYet)]
        [TestCase("initial $fclose;", ElaborationCodes.BadCall)]
        [TestCase("string s; initial s = $sformatf(s);", ElaborationCodes.BadCall)]
        [TestCase("string s; initial s.putc(1);", ElaborationCodes.BadCall)]
        [TestCase("string s; int n; initial n = s.reverse();", ElaborationCodes.UnknownName)]
        [TestCase("logic [7:0] v; initial v.itoa(3);", ElaborationCodes.BadCall)]
        public void WhatLibraryCallsCannotDo_IsRefused_WithWhy(string items, string code)
        {
            Elaboration e = Elaborate("module t;\n    " + items + "\nendmodule");
            CollectionAssert.Contains(e.Diagnostics.Select(d => d.Code).ToArray(), code, string.Join("\n", e.Diagnostics));
        }
    }
}
