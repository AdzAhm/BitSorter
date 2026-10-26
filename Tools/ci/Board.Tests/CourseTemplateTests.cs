using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A course's homework file (Part 13e): where the student's code goes, read in any case and with or without an end
    /// marker, and the file filled with it, everything given kept. Every template here is written for these tests in the
    /// course's style; none is the course's own.
    /// </summary>
    public class CourseTemplateTests
    {
        private const string Marked =
            "// Homework: a 2-to-1 multiplexer. Written for BitSorter's tests.\n" +
            "module mux2 (\n" +
            "    input  logic a, b, s,\n" +
            "    output logic y\n" +
            ");\n" +
            "    // Put your code here\n" +
            "\n" +
            "    // End of your code\n" +
            "endmodule\n";

        private const string Given =
            "module counter (input logic clk, rst, output logic [3:0] q);\n" +
            "    logic [3:0] next;          // given: the next count\n" +
            "    assign next = q + 4'd1;\n" +
            "    // Put your code here: the register\n" +
            "    // End of your code\n" +
            "endmodule\n";

        private const string OpenEnded =
            "module parity (input logic [7:0] d, output logic odd);\n" +
            "    // put your code here\n" +
            "\n" +
            "endmodule\n";

        [Test]
        public void TheMarkers_AreFound_AndTheFileIsFilledBetweenThem()
        {
            Assert.IsTrue(CourseTemplate.TryRead(Marked, out CourseTemplate template));
            Assert.IsTrue(template.HasEndMarker);
            Assert.AreEqual("    ", template.Indent);

            string filled = template.Fill("assign y = s ? b : a;");

            Assert.AreEqual(Marked.Replace("    // Put your code here\n\n", "    // Put your code here\n    assign y = s ? b : a;\n"), filled,
                "the code between the markers, given the marker's indent, and every other character as given");
        }

        [Test]
        public void WhatTheFileGives_IsKept_AroundTheStudentsCode()
        {
            Assert.IsTrue(CourseTemplate.TryRead(Given, out CourseTemplate template));

            string filled = template.Fill("always_ff @(posedge clk) q <= rst ? 4'd0 : next;");

            StringAssert.StartsWith("module counter (input logic clk, rst, output logic [3:0] q);\n    logic [3:0] next;", filled);
            StringAssert.Contains("    // Put your code here: the register\n    always_ff @(posedge clk) q <= rst ? 4'd0 : next;\n    // End of your code\n", filled);
        }

        [Test]
        public void AnOpenerInLowerCase_WithNoEnd_IsFilledUpToEndmodule()
        {
            Assert.IsTrue(CourseTemplate.TryRead(OpenEnded, out CourseTemplate template));
            Assert.IsFalse(template.HasEndMarker);

            string filled = template.Fill("assign odd = ^d;");

            Assert.AreEqual("module parity (input logic [7:0] d, output logic odd);\n    // put your code here\n    assign odd = ^d;\nendmodule\n", filled);
        }

        [Test]
        public void CodeAlreadyIndented_IsKeptAsWritten()
        {
            Assert.IsTrue(CourseTemplate.TryRead(Marked, out CourseTemplate template));

            string filled = template.Fill("    always_comb begin\n        y = s ? b : a;\n    end\n");

            StringAssert.Contains("    // Put your code here\n    always_comb begin\n        y = s ? b : a;\n    end\n    // End of your code\n", filled);
        }

        [Test]
        public void TheStudentsCode_IsReadBackFromAFileThatKeepsTheMarkers()
        {
            Assert.IsTrue(CourseTemplate.TryRead(Marked, out CourseTemplate template));
            string filled = template.Fill("assign y = s ? b : a;");

            Assert.AreEqual("    assign y = s ? b : a;\n", CourseTemplate.RegionIn(filled));
            Assert.IsNull(CourseTemplate.RegionIn("module m; endmodule\n"), "no markers, no region");
        }

        /// <summary>A comment that quotes the markers is not one: a marker is a comment on a line of its own, as the course's all are.</summary>
        [Test]
        public void ABannerThatQuotesTheMarkers_IsNotTakenForThem()
        {
            string banner = "// Write between '// Put your code here' and '// End of your code', and nowhere else.\n" + Marked;

            Assert.IsTrue(CourseTemplate.TryRead(banner, out CourseTemplate template));

            Assert.AreEqual("\n", template.Region, "the region is the module's, not the lines between the banner's quotes");
            Assert.AreEqual("    ", template.Indent);
        }

        [Test]
        public void AFileWithNoMarker_IsNoTemplate()
        {
            Assert.IsFalse(CourseTemplate.TryRead("module m (input logic a, output logic y);\n    assign y = a;\nendmodule\n", out _));
            Assert.IsFalse(CourseTemplate.TryRead("", out _));
        }
    }
}
