using System;
using System.IO;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Part 6a: memories, their files, the transcript, <c>$time</c>, and names through the hierarchy.
    /// The oracle runs the corpus's printing and memory designs against Icarus line by line; these
    /// hold what Icarus cannot judge -- what happens when a file is missing or wrong, what is written
    /// out -- and each feature on its own.
    /// </summary>
    public class SystemTaskTests
    {
        private static Elaboration Elaborate(string text, string top = null)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics, "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, top);
        }

        private static Design Clean(string text, string top = null)
        {
            Elaboration e = Elaborate(text, top);
            Assert.IsEmpty(e.Diagnostics, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            return e.Design;
        }

        private static Simulator Run(Design design, ISimFiles files = null, long until = 1000)
        {
            var sim = new Simulator(design, null, new SimOptions { Files = files });
            sim.RunTo(until);
            return sim;
        }

        /// <summary>What the run printed, without its own closing line.</summary>
        private static string[] Printed(Simulator sim, Severity? severity = null) =>
            sim.History.Messages
                .Where(m => !m.Text.EndsWith(": the run ends here.", StringComparison.Ordinal) && (severity == null || m.Severity == severity))
                .Select(m => m.Text)
                .ToArray();

        private static string Now(Simulator sim, string name) =>
            sim.Value(sim.Design.Find(sim.Design.Name + "." + name)).Format(Radix.Binary);

        // -----------------------------------------------------------------
        // Memories
        // -----------------------------------------------------------------

        [Test]
        public void AMemory_IsAWordASignal_NamedByItsIndex_WhicheverWayItIsDeclared()
        {
            Design design = Clean(@"
module t;
    logic [7:0] up [0:3];
    logic [3:0] down [3:0];
    logic signed [7:0] sized [4];
endmodule");

            Assert.AreEqual(3, design.Memories.Count);
            MemoryInfo down = design.Memories[1];
            Assert.AreEqual("t.down", down.Name);
            Assert.AreEqual(4, down.Depth);
            Assert.AreEqual(4, down.Width);
            Assert.AreEqual("t.down[2]", design[down.Word(2)].Name);
            Assert.AreSame(down, design[down.Word(2)].Memory);
            Assert.IsTrue(design.Memories[2].Signed);
            Assert.AreEqual(0, design.Memories[2].Lowest);
            Assert.AreEqual(3, design.Memories[2].Highest);
        }

        [Test]
        public void Words_AreReadAndWritten_ByAConstantIndex_OrOneWorkedOutAsTheDesignRuns()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic [7:0] mem [4:7];
    logic [7:0] fixed, picked, outside, unknown;
    logic [2:0] i;
    assign fixed = mem[5];
    assign picked = mem[i];
    assign outside = mem[3];
    initial begin
        mem[5] = 8'h55;
        i = 3'd6;
        mem[i] <= 8'h66;
        #1 mem[i][3:0] = 4'hf;          // a part of a word, the word picked as it runs
        #1 i = 3'bx1x; mem[i] = 8'h00;  // an unknown word: nothing is written
        #1 i = 3'd2; mem[i] = 8'h00;    // below the memory: nothing is written
        #1 unknown = mem[i];
        i = 3'd6;
    end
endmodule"));

            Assert.AreEqual("01010101", Now(sim, "fixed"));
            Assert.AreEqual("01101111", Now(sim, "picked"), "mem[6]: 66, then its low nibble f");
            Assert.AreEqual("xxxxxxxx", Now(sim, "outside"), "a constant index outside reads x");
            Assert.AreEqual("xxxxxxxx", Now(sim, "unknown"), "and so does one worked out outside");
        }

        [Test]
        public void AContinuousRead_OfAWordPickedAsItRuns_FollowsWritesToAnyWord()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic clk = 0, we = 0;
    logic [1:0] addr = 0;
    logic [3:0] d = 0, q;
    logic [3:0] mem [0:3];
    always_ff @(posedge clk) if (we) mem[addr] <= d;
    assign q = mem[2];
    always #5 clk = ~clk;
    initial begin
        @(negedge clk) begin we = 1; addr = 2; d = 4'd9; end
        @(negedge clk) begin addr = 1; d = 4'd3; end
        @(negedge clk) we = 0;
    end
endmodule"), until: 100);

            CollectionAssert.AreEqual(new[] { "0 xxxx", "15 1001" }, Probes.Settled(sim.History, sim.Design.Find("t.q")));
        }

        // -----------------------------------------------------------------
        // Memory files
        // -----------------------------------------------------------------

        private const string Loader = @"
module t;
    logic [7:0] mem [0:3];
    initial $readmemh(""image.hex"", mem);
endmodule";

        [Test]
        public void ReadMemh_LoadsWordsFromTheRunsFiles()
        {
            Simulator sim = Run(Clean(Loader), new MemorySimFiles().Add("image.hex", "// four words\n01 23\n45 67\n"));

            Assert.IsEmpty(Printed(sim));
            Assert.AreEqual("00000001", sim.Value(sim.Design.Memories[0].Word(0)).Format(Radix.Binary));
            Assert.AreEqual("01100111", sim.Value(sim.Design.Memories[0].Word(3)).Format(Radix.Binary));
        }

        [TestCase(null, "there is no file called image.hex")]
        [TestCase("01 2g", "line 1: 2g is not a hex word")]
        [TestCase("@9 00", "line 1: @9 is outside the words being loaded")]
        [TestCase("/* never closed", "a /* comment is never closed")]
        public void AMissingOrBrokenFile_IsAnErrorInTheTranscript_AndTheRunGoesOn(string text, string said)
        {
            var files = new MemorySimFiles();
            if (text != null)
                files.Add("image.hex", text);

            Simulator sim = Run(Clean(Loader), files);

            StringAssert.Contains(said, Printed(sim, Severity.Error).Single());
            Assert.IsFalse(sim.Stopped, "a file is the testbench's business: the run goes on");
            Assert.AreEqual("xxxxxxxx", sim.Value(sim.Design.Memories[0].Word(0)).Format(Radix.Binary), "nothing loaded");
        }

        [Test]
        public void MoreWordsThanRoom_AndFewerThanWords_AreWarnings()
        {
            Simulator more = Run(Clean(Loader), new MemorySimFiles().Add("image.hex", "1 2 3 4 5 6"));
            StringAssert.Contains("more words than t.mem has room for", Printed(more, Severity.Warning).Single());

            Simulator fewer = Run(Clean(Loader), new MemorySimFiles().Add("image.hex", "1 2"));
            StringAssert.Contains("the file has 2 words for the 4 of t.mem", Printed(fewer, Severity.Warning).Single());
        }

        [Test]
        public void ARange_LoadsFromItsFirstWord_EvenDownwards()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic [3:0] mem [0:7];
    initial $readmemb(""bits.bin"", mem, 5, 3);
endmodule"), new MemorySimFiles().Add("bits.bin", "0001 0010 0011"));

            MemoryInfo mem = sim.Design.Memories[0];
            Assert.AreEqual("0001", sim.Value(mem.Word(5)).Format(Radix.Binary));
            Assert.AreEqual("0010", sim.Value(mem.Word(4)).Format(Radix.Binary));
            Assert.AreEqual("0011", sim.Value(mem.Word(3)).Format(Radix.Binary));
            Assert.AreEqual("xxxx", sim.Value(mem.Word(6)).Format(Radix.Binary));
        }

        [Test]
        public void WriteMemh_WritesAWordALine_IntoTheRunsFiles()
        {
            var files = new MemorySimFiles();
            Run(Clean(@"
module t;
    logic [7:0] mem [0:3];
    initial begin
        mem[0] = 8'h0a; mem[1] = 8'hb0; mem[2] = 8'b1111_xxxx;
        $writememh(""out.hex"", mem);
        $writememb(""out.bin"", mem, 1, 2);
    end
endmodule"), files);

            Assert.IsTrue(files.TryRead("out.hex", out string hex));
            Assert.AreEqual("0a\nb0\nfx\nxx\n", hex);
            Assert.IsTrue(files.TryRead("out.bin", out string bin));
            Assert.AreEqual("10110000\n1111xxxx\n", bin);
        }

        [Test]
        public void AString_IsEightBitsACharacter_SoAParameterCanNameAMemorysFile()
        {
            // Part 6b. The course's memories take their image as a parameter, so the file name is a
            // value: the first character in the top byte, padded with zeros on the left and cut from
            // the left, as any unsigned value is (IEEE 1800, 5.9).
            Simulator sim = Run(Clean(@"
module rom #(parameter IMAGE = ""none.hex"") (output logic [7:0] first);
    logic [7:0] mem [0:1];
    initial $readmemh(IMAGE, mem);
    assign first = mem[0];
endmodule

// Seven characters in 60 bits: four zeros on top that are not a character of their own.
module odd_rom #(parameter [59:0] IMAGE = ""x.hex"") (output logic [7:0] first);
    logic [7:0] mem [0:1];
    initial $readmemh(IMAGE, mem);
    assign first = mem[1];
endmodule

module t;
    logic [15:0] exact = ""AB"";
    logic [19:0] wider = ""AB"";
    logic [7:0] narrower = ""AB"";
    logic [7:0] first, second;
    rom #(.IMAGE(""rom.hex"")) r (.first(first));
    odd_rom #(.IMAGE(""rom.hex"")) o (.first(second));
endmodule", "t"), new MemorySimFiles().Add("rom.hex", "a5 5a"));

            Assert.AreEqual("0100000101000010", Now(sim, "exact"), "A is 41 and B 42");
            Assert.AreEqual("00000100000101000010", Now(sim, "wider"));
            Assert.AreEqual("01000010", Now(sim, "narrower"), "the A cut away");
            Assert.AreEqual("10100101", Now(sim, "first"), "rom.hex, named by the instance's parameter, was read");
            Assert.AreEqual("01011010", Now(sim, "second"), "and named by one wider than its name");
            Assert.IsEmpty(Printed(sim));
        }

        // -----------------------------------------------------------------
        // The transcript
        // -----------------------------------------------------------------

        [Test]
        public void InfoWarningAndError_AreMarkedSo_AndFatalEndsTheRun()
        {
            Design design = Clean(@"
module t;
    initial begin
        $info(""starting %0d"", 3);
        $warning(""odd"");
        $error;
        #5 $fatal(1, ""stopping at %0t"", $time);
        #5 $display(""never"");
    end
endmodule");

            var sim = new Simulator(design);
            RunResult result = sim.RunTo(100);

            Assert.AreEqual(RunStatus.Finished, result.Status);
            Assert.AreEqual(5, result.Time);
            CollectionAssert.AreEqual(new[] { "starting 3" }, Printed(sim, Severity.Info));
            CollectionAssert.AreEqual(new[] { "odd" }, Printed(sim, Severity.Warning));
            CollectionAssert.AreEqual(new[] { "$error", "stopping at 5" }, Printed(sim, Severity.Error));
        }

        [Test]
        public void Time_IsReadAsTheRunMoves_ButAnAssignOfItIsNotWokenByIt()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic [63:0] seen, held;
    assign held = $time;
    initial begin #7 seen = $time; #3 $display(""%0t %0d"", $time, held); end
endmodule"));

            Assert.AreEqual(LogicVec.FromUInt64(7, 64).Format(Radix.Binary), Now(sim, "seen"));
            CollectionAssert.AreEqual(new[] { "10 0" }, Printed(sim), "the assign ran at time 0 and nothing woke it since, as the standard has it");
        }

        [Test]
        public void M_IsTheScopeThePrintIsWrittenIn()
        {
            Simulator sim = Run(Clean(@"
module leaf; initial $display(""%m""); endmodule
module mid; leaf l (); endmodule
module top; mid m (); initial begin : named $display(""%m""); end endmodule"));

            CollectionAssert.AreEquivalent(new[] { "top.m.l", "top.named" }, Printed(sim), "a named block is a scope of its own");
        }

        // -----------------------------------------------------------------
        // Names through the hierarchy
        // -----------------------------------------------------------------

        [Test]
        public void AHierarchicalName_IsReadAndWritten_FromAProcessWrittenBeforeTheInstance()
        {
            Simulator sim = Run(Clean(@"
module counter (input logic clk);
    logic [3:0] count = 0;
    always_ff @(posedge clk) count <= count + 4'd1;
endmodule

module top;
    logic clk = 0;
    logic [3:0] seen, shown;
    initial begin
        #12 seen = c.count;
        c.count = 4'd9;
    end
    assign shown = c.count;          // an assign naming it, written before the instance too
    always #5 clk = ~clk;
    counter c (.clk(clk));
endmodule"), until: 20);

            Assert.AreEqual("0001", Now(sim, "seen"), "by 12 the counter has seen one rising edge, at 5");
            Assert.AreEqual("1010", Now(sim, "c.count"), "the testbench's 9, counted on at 15");
            Assert.AreEqual("1010", Now(sim, "shown"), "and the assign written before the instance follows it");
        }

        [Test]
        public void AGenerateBlocksSignal_IsReachedByItsIndex()
        {
            Simulator sim = Run(Clean(@"
module top;
    logic [7:0] seen;
    for (genvar i = 0; i < 4; i++) begin : lane
        logic [7:0] value;
        assign value = 8'd10 * i;
    end
    initial #1 seen = lane[3].value;
endmodule"));

            Assert.AreEqual(LogicVec.FromUInt64(30, 8).Format(Radix.Binary), Now(sim, "seen"));
        }

        [Test]
        public void ALocalparamFile_IncludedOutsideEveryModule_IsSeenByThemAll()
        {
            var files = new MemorySourceFiles().Add("consts.svh", "localparam WIDTH = 6;\nlocalparam [WIDTH-1:0] ONES = '1;\n");
            SyntaxTree tree = SyntaxReader.ReadText("`include \"consts.svh\"\nmodule t; logic [WIDTH-1:0] y; initial y = ONES; endmodule\n", "test.sv", files);
            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));

            Elaboration e = Elaborator.Elaborate(tree);
            Assert.IsEmpty(e.Diagnostics, string.Join("\n", e.Diagnostics));
            Assert.AreEqual("111111", Now(Run(e.Design), "y"));
        }

        // -----------------------------------------------------------------
        // What is refused
        // -----------------------------------------------------------------

        [TestCase("module t; logic [7:0] m [4]; logic [7:0] y; assign y = m; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module t; logic [7:0] m [4]; logic [1:0] i; assign m[i] = 8'd0; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module t; logic [7:0] m [4]; initial m = 8'd0; endmodule", ElaborationCodes.BadLValue)]
        [TestCase("module t; wire [7:0] m [4]; endmodule", ElaborationCodes.NotYet)]
        [TestCase("module t; initial begin logic [7:0] m [4]; end endmodule", ElaborationCodes.NotYet)]
        [TestCase("module t; logic [7:0] m [4]; initial $readmemh(m, m); endmodule", ElaborationCodes.BadFormat)]
        [TestCase("module t; logic [7:0] y; initial $readmemh(\"f.hex\", y); endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module t; initial $display(\"%f\", 1); endmodule", ElaborationCodes.NotYet)]
        [TestCase("module t; logic y; initial y = nowhere.x; endmodule", ElaborationCodes.UnknownName)]
        [TestCase("module c; endmodule\nmodule t; logic y; c i (); initial y = i.missing; endmodule", ElaborationCodes.UnknownName)]
        public void AnError_IsReported_AndThereIsNoDesign(string text, string code)
        {
            Elaboration e = Elaborate(text);
            CollectionAssert.Contains(e.Diagnostics.Select(d => d.Code).ToArray(), code, string.Join("\n", e.Diagnostics));
            Assert.IsNull(e.Design);
        }

        [Test]
        public void AFormatWithTooFewValues_IsAWarning_AndPrintsWhatItCan()
        {
            Elaboration e = Elaborate("module t; initial $display(\"a=%0d b=%0d\", 1); endmodule");

            CollectionAssert.AreEqual(new[] { ElaborationCodes.BadFormat }, e.Diagnostics.Select(d => d.Code));
            CollectionAssert.AreEqual(new[] { "a=1 b=" }, Printed(Run(e.Design)));
        }

        [Test]
        public void AVcd_LeavesAMemorysWordsOut_AsIcarusDoes()
        {
            Design design = Clean("module t; logic [7:0] mem [0:3]; logic [7:0] y; assign y = mem[1]; endmodule");
            var vcd = new StringWriter();
            VcdWriter.Write(vcd, design, Run(design).History, 1);

            StringAssert.DoesNotContain("mem", vcd.ToString());
            StringAssert.Contains(" y ", vcd.ToString());
        }
    }
}
