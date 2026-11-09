using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using EngineStimulus = BitSorter.Sim.Stimulus;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A module the importer draws means what its code means (Part 16d's sweep): each case is read, drawn where the
    /// importer draws it, and run beside its code as written on the same inputs, and every output must change as the
    /// code's does. Where a board cannot say what the code says, the importer refuses to draw it, and the module comes in
    /// as written code -- which is run as written, so it means what it says.
    /// </summary>
    public class DrawnAsWrittenTests
    {
        /// <summary>
        /// <paramref name="top"/> of <paramref name="code"/> drawn and run beside the code as written, both given the same
        /// inputs every ten units for sixty-four steps, every output's changes compared. The import, drawn or refused.
        /// </summary>
        internal static ImportResult AssertDrawnRunsAsWritten(string code, string top, bool mustDraw = false, params string[] cells)
        {
            SyntaxTree tree = SyntaxReader.ReadText(code, top + ".sv");
            Assert.IsFalse(tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), string.Join("\n", tree.Diagnostics));

            // The code as written, with the library's cells it uses, as a project compiles them.
            SyntaxTree withCells = SyntaxReader.ReadText(code, top + ".sv");
            foreach (string cell in cells)
                withCells.Modules.Add(CellLibrary.Build(cell));
            Elaboration written = Elaborator.Elaborate(withCells, top);
            Assert.IsFalse(written.HasErrors, "sanity: the code itself runs\n" + string.Join("\n", written.Diagnostics));

            ImportResult imported = BoardImporter.Import(tree, top);
            if (!imported.Succeeded)
            {
                Assert.IsFalse(mustDraw, "it should be drawn:\n" + string.Join("\n", imported.Problems));
                return imported;
            }

            CompiledProject compiled = BoardCompiler.Compile(imported.Project);
            Assert.IsTrue(compiled.Succeeded, "the drawing compiles");
            Elaboration drawn = Elaborator.Elaborate(compiled.Tree, top);
            Assert.IsFalse(drawn.HasErrors, "the drawing elaborates\n" + string.Join("\n", drawn.Diagnostics));

            ModuleDeclaration module = tree.Modules.Single(m => m.Name == top);
            const int Steps = 64;
            var random = new Random(16);
            var inputs = new List<(string Name, LogicVec[] Values)>();
            foreach (PortDeclaration port in module.Ports.Where(p => p.Direction == PortDirection.Input))
            {
                int width = written.Design[written.Design.Find(top + "." + port.Name)].Width;
                var values = new LogicVec[Steps];
                for (int i = 0; i < Steps; i++)
                    values[i] = LogicVec.FromUInt64(((ulong)random.Next() << 32 | (uint)random.Next()) & (width >= 64 ? ulong.MaxValue : (1UL << width) - 1), width);
                inputs.Add((port.Name, values));
            }

            Simulator Run(Design design)
            {
                // Each value at the width the design gives the input, so a drawing that has it narrower still runs, and differs.
                var stimulus = new EngineStimulus();
                foreach ((string name, LogicVec[] values) in inputs)
                {
                    SignalId signal = design.Find(top + "." + name);
                    for (int i = 0; i < Steps; i++)
                        stimulus.Set(i * 10L, signal, values[i].Extend(design[signal].Width, false));
                }
                var sim = new Simulator(design, stimulus);
                sim.RunTo(Steps * 10L + 50);
                return sim;
            }

            Simulator a = Run(written.Design), b = Run(drawn.Design);
            foreach (PortDeclaration port in module.Ports.Where(p => p.Direction == PortDirection.Output))
            {
                string name = top + "." + port.Name;
                string[] Changes(Simulator sim) =>
                    sim.History.SettledChanges(sim.Design.Find(name)).Select(c => $"{c.Time} {c.Value.Format(Radix.Binary)}").ToArray();
                CollectionAssert.AreEqual(Changes(a), Changes(b), $"{name}, drawn, changes as the code says it does");
            }

            return imported;
        }

        // -----------------------------------------------------------------
        // '0, '1, 'x and 'z fill their context (import-export-1)
        // -----------------------------------------------------------------

        [Test]
        public void AFillLiteral_FillsTheWholeNet_InAnAssign()
        {
            AssertDrawnRunsAsWritten(
                "module fills (output logic [3:0] ones, output logic [3:0] unknown, output logic [3:0] zeros, output logic [3:0] off);\n" +
                "    assign ones = '1;\n" +
                "    assign unknown = 'x;\n" +
                "    assign zeros = '0;\n" +
                "    assign off = 'z;\n" +
                "endmodule\n", "fills", mustDraw: true);
        }

        [Test]
        public void AFillLiteral_FillsTheWholeRegister_AsItsResetAndItsStart()
        {
            AssertDrawnRunsAsWritten(
                "module filled (input logic clk, input logic rst, input logic [3:0] d, output logic [3:0] q, output logic [3:0] s);\n" +
                "    logic [3:0] held = '1;\n" +
                "    always_ff @(posedge clk, posedge rst)\n" +
                "        if (rst) q <= '1;\n" +
                "        else q <= d;\n" +
                "    always_ff @(posedge clk) held <= d;\n" +
                "    assign s = held;\n" +
                "endmodule\n", "filled", mustDraw: true);
        }

        [Test]
        public void ASignedLiteral_IsSignExtended_ToTheNetItDrives()
        {
            // 4'sb1000 is -8: on eight bits, 11111000 (IEEE 1800, 11.8.2), as the code's own run extends it.
            AssertDrawnRunsAsWritten(
                "module widened (output logic [7:0] y);\n" +
                "    assign y = 4'sb1000;\n" +
                "endmodule\n", "widened", mustDraw: true);
        }

        // -----------------------------------------------------------------
        // What a declaration says of its net (import-export-3)
        // -----------------------------------------------------------------

        [Test]
        public void ASupplyNet_IsDrivenWithItsLevel()
        {
            AssertDrawnRunsAsWritten(
                "module supplied (input logic a, output logic y, output logic z);\n" +
                "    supply1 vdd;\n" +
                "    supply0 gnd;\n" +
                "    and g1 (y, a, vdd);\n" +
                "    or g2 (z, a, gnd);\n" +
                "endmodule\n", "supplied", mustDraw: true);
        }

        [Test]
        public void ANetsDeclarationAssignment_DrivesIt()
        {
            // wire one = 1'b1; is a continuous assignment (IEEE 1800, 10.3.1), not a starting value.
            AssertDrawnRunsAsWritten(
                "module declared (input logic a, output logic y, output logic [3:0] w);\n" +
                "    wire one = 1'b1;\n" +
                "    wire [3:0] five = 4'd5;\n" +
                "    and g1 (y, a, one);\n" +
                "    assign w = five;\n" +
                "endmodule\n", "declared", mustDraw: true);
        }

        [Test]
        public void AVariablesStartingValue_HoldsWhereNothingElseWritesIt()
        {
            AssertDrawnRunsAsWritten(
                "module started (input logic a, output logic y);\n" +
                "    logic k = 1'b1;\n" +
                "    and g1 (y, a, k);\n" +
                "endmodule\n", "started", mustDraw: true);
        }

        [Test]
        public void ANetsDeclarationAssignment_WithADelay_IsNotDrawnWithout()
        {
            AssertDrawnRunsAsWritten(
                "module late (input logic a, output logic y);\n" +
                "    wire #2 one = 1'b1;\n" +
                "    and g1 (y, a, one);\n" +
                "endmodule\n", "late");
        }

        [TestCase("wire #3 n;", "a delay")]
        [TestCase("tri0 n;", "tri0")]
        [TestCase("wand n;", "wand")]
        [TestCase("trireg n;", "trireg")]
        public void ANetTheBoardCannotMean_IsNotDrawn(string declaration, string said)
        {
            // The engine refuses these too (Elaborator.CheckNetType, and a net delay off an assign); a drawing silently
            // without them would be a circuit the code is not.
            ImportResult imported = BoardImporter.Import(SyntaxReader.ReadText(
                "module netty (input logic a, input logic b, output logic y);\n" +
                "    " + declaration + "\n" +
                "    and g1 (n, a, b);\n" +
                "    assign y = n;\n" +
                "endmodule\n", "netty.sv"), "netty");

            Assert.IsFalse(imported.Succeeded, "drawn without what its declaration says");
            StringAssert.Contains(said, string.Join("\n", imported.Problems));
            StringAssert.Contains("netty.sv:2", string.Join("\n", imported.Problems), "and where");
        }

        // -----------------------------------------------------------------
        // A type a board's nets are not (import-export-4)
        // -----------------------------------------------------------------

        [TestCase("typedef logic [7:0] word_t;\n", "word_t")]
        [TestCase("", "byte")]
        [TestCase("", "int")]
        [TestCase("typedef enum logic [1:0] { IDLE, RUN, DONE } state_t;\n", "state_t")]
        [TestCase("typedef struct packed { logic [3:0] hi; logic [3:0] lo; } pair_t;\n", "pair_t")]
        public void APortOfAType_IsDrawnAtItsWidth_OrNotAtAll(string unit, string type)
        {
            AssertDrawnRunsAsWritten(unit +
                "module typed (input logic clk, input " + type + " d, output " + type + " q, output " + type + " w);\n" +
                "    always_ff @(posedge clk) q <= d;\n" +
                "    assign w = d;\n" +
                "endmodule\n", "typed");
        }

        // -----------------------------------------------------------------
        // A bit is named by its range (import-export-6)
        // -----------------------------------------------------------------

        [Test]
        public void ABitSelect_TakesTheBitItsRangeNames()
        {
            // a[2] of [4:1] is the second bit up, offset 1; b[0] of [0:3] is the top bit (IEEE 1800, 7.4.5).
            AssertDrawnRunsAsWritten(
                "module bits (input logic [4:1] a, input logic [0:3] b, output logic y, output logic z);\n" +
                "    assign y = a[2];\n" +
                "    assign z = b[0];\n" +
                "endmodule\n", "bits", mustDraw: true);
        }

        [Test]
        public void AMuxsSelect_IsReadByItsRange()
        {
            AssertDrawnRunsAsWritten(
                "module chooser (input logic [0:1] s, input logic d0, input logic d1, input logic d2, input logic d3, output logic y);\n" +
                "    assign y = s[1] ? (s[0] ? d3 : d2) : (s[0] ? d1 : d0);\n" +
                "endmodule\n", "chooser");
        }

        [Test]
        public void AnArrayOfBlocks_TakesTheBitsItsRangeNames()
        {
            // Copy i takes a[i] and gives y[i]: a[0] of [0:3] is its top bit, y[0] of [3:0] its bottom one.
            AssertDrawnRunsAsWritten(
                "module flip (input logic x, output logic y);\n" +
                "    assign y = ~x;\n" +
                "endmodule\n" +
                "module flips (input logic [0:3] a, output logic [3:0] y);\n" +
                "    genvar i;\n" +
                "    for (i = 0; i < 4; i++) begin : u\n" +
                "        flip f (.x(a[i]), .y(y[i]));\n" +
                "    end\n" +
                "endmodule\n", "flips");
        }

        // -----------------------------------------------------------------
        // A cell's connections (import-export-14)
        // -----------------------------------------------------------------

        [TestCase("NAND2 g1 (.A, .B, .Z);")]
        [TestCase("NAND2 g1 (.*);")]
        public void ACellsConnectionsByShorthand_AreMadeOrNotDrawn(string cell)
        {
            // .A is .A(A), and .* every port to the net of its name (IEEE 1800, 23.3.2.3 and 23.3.2.4).
            AssertDrawnRunsAsWritten(
                "module celled (input logic A, input logic B, output logic Z);\n" +
                "    " + cell + "\n" +
                "endmodule\n", "celled", false, "NAND2");
        }

        [TestCase("NAND2 g1 (.A(a), .B(b), .Q(y));", ".Q")]
        [TestCase("NAND2 g1 (a, b, y, y2);", "four")]
        public void ACellConnectedToPortsItHasNot_IsNotDrawn(string cell, string said)
        {
            ImportResult imported = BoardImporter.Import(SyntaxReader.ReadText(
                "module celled (input logic a, input logic b, output logic y, output logic y2);\n" +
                "    " + cell + "\n" +
                "endmodule\n", "celled.sv"), "celled");

            Assert.IsFalse(imported.Succeeded, "drawn with a connection the cell has no port for");
            StringAssert.Contains(said, string.Join("\n", imported.Problems));
            StringAssert.Contains("celled.sv:2", string.Join("\n", imported.Problems), "and where");
        }

        // -----------------------------------------------------------------
        // A width past what a board holds (import-export-2)
        // -----------------------------------------------------------------

        [TestCase("[9223372036854775808:0]")]   // 2^63: Math.Abs(long.MinValue) threw
        [TestCase("[4294967296:0]")]           // 2^32 + 1 bits, cast to an int of 1
        [TestCase("[2000000:0]")]              // past LogicVec.MaxWidth: its starting value's Extend threw
        public void AWidthPastWhatABoardHolds_IsRefused_NeverThrown(string range)
        {
            string code = "module huge (input logic " + range + " a, output logic y);\n    logic " + range + " w = 1;\n    assign y = a[0];\nendmodule\n";

            ImportResult imported = null;
            Assert.DoesNotThrow(() => imported = BoardImporter.Import(SyntaxReader.ReadText(code, "huge.sv"), "huge"));
            Assert.IsFalse(imported.Succeeded, "drawn with a width it has not");
            StringAssert.Contains("huge.sv:1", string.Join("\n", imported.Problems), "said where");

            // And as a project: it comes in written, or is refused, saying why -- never an exception out of FILE, IMPORT.
            Assert.DoesNotThrow(() => ProjectImporter.Import(new[] { new SourceFile("huge.sv", code) }));
        }

        [TestCase("input bit a", "bit")]
        [TestCase("input logic a [1:0]", "array")]
        [TestCase("input integer a", "integer")]
        public void APortABoardsNetIsNot_IsNotDrawn(string port, string said)
        {
            // bit is 2-state, an x read as 0; an unpacked port is an array; neither is a net of a board, a logic vector.
            ImportResult imported = BoardImporter.Import(SyntaxReader.ReadText(
                "module odd (\n    " + port + ",\n    output logic y);\n    assign y = 1'b0;\nendmodule\n", "odd.sv"), "odd");

            Assert.IsFalse(imported.Succeeded, "drawn as a net it is not");
            StringAssert.Contains(said, string.Join("\n", imported.Problems));
            StringAssert.Contains("odd.sv:2", string.Join("\n", imported.Problems), "and where");
        }
    }
}
