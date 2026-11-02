using System.Collections.Generic;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The classic slips of a first SystemVerilog course (Part 15i-2), each written for these tests: what is typed, the line
    /// and the words BitSorter answers with, and the code as it should have been. <c>FriendlyErrorTests</c> holds the words
    /// and the mended code to BitSorter; <c>FriendlyErrorOracleTests</c> holds both to Icarus -- every slip refused there
    /// too, but the procedural assign, which the language has: BitSorter runs it, as Icarus does, and warns (M4).
    /// </summary>
    public sealed class FriendlyErrorCase
    {
        public string Name;

        /// <summary>The module <c>m</c> as typed.</summary>
        public string Typed;

        /// <summary>Line of <see cref="Typed"/> the answer is on.</summary>
        public int Line;

        /// <summary>Words the answer has.</summary>
        public string Says;

        /// <summary>Whether the answer is a warning -- the code read and run -- rather than an error.</summary>
        public bool Warning;

        /// <summary>Whether the slip is a look-alike character, read as the one it looks like, which the export straightens.</summary>
        public bool Lookalike;

        /// <summary><c>m</c> mended.</summary>
        public string Mended;

        /// <summary>Other modules beside <c>m</c>, typed right.</summary>
        public string[] Beside = new string[0];

        /// <summary>Whether Icarus takes <see cref="Typed"/> as it is.</summary>
        public bool IcarusTakesIt;

        public override string ToString() => Name;
    }

    public static class FriendlyErrorCases
    {
        private const string AndTwo =
            "module and2 #(parameter D = 0) (input logic a, b, output logic y);\n" +
            "    assign y = a & b;\n" +
            "endmodule\n";

        public static IEnumerable<FriendlyErrorCase> All()
        {
            yield return new FriendlyErrorCase
            {
                Name = "curly quote",
                Typed = "module m (input logic a, output logic y);\n    assign y = a & 1’b1;\nendmodule\n",
                Line = 2, Says = "curly quote, not an apostrophe", Warning = true, Lookalike = true,
                Mended = "module m (input logic a, output logic y);\n    assign y = a & 1'b1;\nendmodule\n",
            };
            yield return new FriendlyErrorCase
            {
                Name = "dash",
                Typed = "module m (input logic [1:0] a, b, output logic [1:0] y);\n    assign y = a – b;\nendmodule\n",
                Line = 2, Says = "is a dash, not a minus sign", Warning = true, Lookalike = true,
                Mended = "module m (input logic [1:0] a, b, output logic [1:0] y);\n    assign y = a - b;\nendmodule\n",
            };
            yield return new FriendlyErrorCase
            {
                Name = "an array used as a bus",
                Typed = "module m (input logic a, b, output logic [1:0] y);\n" +
                        "    logic s[1:0];\n" +
                        "    assign s[0] = a;\n" +
                        "    assign s[1] = b;\n" +
                        "    assign y = s + 2'd1;\n" +
                        "endmodule\n",
                Line = 5, Says = "for a 2-bit bus, put the range before the name: logic [1:0] s.",
                Mended = "module m (input logic a, b, output logic [1:0] y);\n" +
                         "    logic [1:0] s;\n" +
                         "    assign s[0] = a;\n" +
                         "    assign s[1] = b;\n" +
                         "    assign y = s + 2'd1;\n" +
                         "endmodule\n",
            };
            yield return new FriendlyErrorCase
            {
                Name = "an array written whole",
                Typed = "module m (input logic a, b, output logic y);\n" +
                        "    logic s[1:0];\n" +
                        "    always_comb s = {a, b};\n" +
                        "    assign y = s[0];\n" +
                        "endmodule\n",
                Line = 3, Says = "s is a memory: write it a word at a time",
                Mended = "module m (input logic a, b, output logic y);\n" +
                         "    logic [1:0] s;\n" +
                         "    always_comb s = {a, b};\n" +
                         "    assign y = s[0];\n" +
                         "endmodule\n",
            };
            yield return new FriendlyErrorCase
            {
                Name = "an if on a signal outside an always",
                Typed = "module m (input logic a, sel, output logic y);\n" +
                        "    if (sel)\n" +
                        "        assign y = a;\n" +
                        "    else\n" +
                        "        assign y = ~a;\n" +
                        "endmodule\n",
                Line = 2, Says = "an if outside an always is chosen once, as the design is built",
                Mended = "module m (input logic a, sel, output logic y);\n" +
                         "    always_comb\n" +
                         "        if (sel)\n" +
                         "            y = a;\n" +
                         "        else\n" +
                         "            y = ~a;\n" +
                         "endmodule\n",
            };
            yield return new FriendlyErrorCase
            {
                Name = "a for on a signal outside an always",
                Typed = "module m (input logic [3:0] a, input logic [1:0] n, output logic [3:0] y);\n" +
                        "    for (genvar i = 0; i < n; i++)\n" +
                        "        assign y[i] = a[i];\n" +
                        "endmodule\n",
                Line = 2, Says = "its bounds must be numbers or parameters",
                Mended = "module m #(parameter N = 4) (input logic [3:0] a, input logic [1:0] n, output logic [3:0] y);\n" +
                         "    for (genvar i = 0; i < N; i++)\n" +
                         "        assign y[i] = a[i];\n" +
                         "endmodule\n",
            };
            yield return new FriendlyErrorCase
            {
                Name = "a case outside an always",
                Typed = "module m (input logic a, sel, output logic y);\n" +
                        "    case (sel)\n" +
                        "        1'b0: assign y = a;\n" +
                        "        default: assign y = ~a;\n" +
                        "    endcase\n" +
                        "endmodule\n",
                Line = 2, Says = "To choose while it runs, put the case in an always_comb",
                Mended = "module m (input logic a, sel, output logic y);\n" +
                         "    always_comb\n" +
                         "        case (sel)\n" +
                         "            1'b0: y = a;\n" +
                         "            default: y = ~a;\n" +
                         "        endcase\n" +
                         "endmodule\n",
            };
            yield return new FriendlyErrorCase
            {
                Name = "an instance with no name",
                Typed = "module m (input logic a, b, output logic y);\n    and2 (.a(a), .b(b), .y(y));\nendmodule\n",
                Line = 2, Says = "expected an instance name -- 'and2 u1 (...);'",
                Mended = "module m (input logic a, b, output logic y);\n    and2 u1 (.a(a), .b(b), .y(y));\nendmodule\n",
                Beside = new[] { AndTwo },
            };
            yield return new FriendlyErrorCase
            {
                Name = "an instance with parameters and no name",
                Typed = "module m (input logic a, b, output logic y);\n    and2 #(1) (.a(a), .b(b), .y(y));\nendmodule\n",
                Line = 2, Says = "After and2 and its parameters, expected an instance name",
                Mended = "module m (input logic a, b, output logic y);\n    and2 #(1) u1 (.a(a), .b(b), .y(y));\nendmodule\n",
                Beside = new[] { AndTwo },
            };
            yield return new FriendlyErrorCase
            {
                Name = "always_comb with an event control",
                Typed = "module m (input logic a, b, output logic y);\n    always_comb @(a or b)\n        y = a & b;\nendmodule\n",
                Line = 2, Says = "to wait on a list of your own, write always @(...)",
                Mended = "module m (input logic a, b, output logic y);\n    always_comb\n        y = a & b;\nendmodule\n",
            };
            yield return new FriendlyErrorCase
            {
                Name = "an assign inside an always",
                Typed = "module m (input logic a, b, output logic y);\n    always_comb begin\n        assign y = a & b;\n    end\nendmodule\n",
                Line = 3, Says = "a procedural continuous assignment: from the first time it runs, y follows a & b", Warning = true,
                Mended = "module m (input logic a, b, output logic y);\n    always_comb begin\n        y = a & b;\n    end\nendmodule\n",
                IcarusTakesIt = true,
            };
        }
    }
}
