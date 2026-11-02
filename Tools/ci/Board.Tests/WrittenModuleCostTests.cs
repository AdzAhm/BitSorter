using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A written module read again in the same document costs nothing again (Part 15h-6d's browser check). Every read was kept
    /// under a key made of the whole project's text -- the module's, every other module's, each file they include or read --
    /// and the key was made again on every read, kept or not. Opening the pipelined processor read its seventeen modules some
    /// two thousand times in one frame and made 1.6 GB of keys; a browser frees memory only between frames, and the page ran
    /// out of its 2 GB.
    /// </summary>
    public class WrittenModuleCostTests
    {
        /// <summary>
        /// <paramref name="count"/> written stages in a chain, each including a header of definitions, under a top whose
        /// testbench reads a memory image of some 20 KB: a processor's shape.
        /// </summary>
        private static Project Chain(int count)
        {
            var files = new List<SourceFile>();
            var defs = new StringBuilder("`ifndef DEFS_SVH\n`define DEFS_SVH\n");
            for (int i = 0; i < 40; i++)
                defs.Append("`define OP_").Append(i).Append(" 7'd").Append(i).Append('\n');
            defs.Append("`endif\n");
            files.Add(new SourceFile("defs.svh", defs.ToString()));

            var image = new StringBuilder();
            for (int i = 0; i < 2048; i++)
                image.Append((i * 2654435761u).ToString("x8")).Append('\n');
            files.Add(new SourceFile("image.hex", image.ToString()));

            for (int i = 0; i < count; i++)
                files.Add(new SourceFile($"stage{i}.sv",
                    "`include \"defs.svh\"\n" +
                    $"module stage{i} (input logic [7:0] a, output logic [7:0] y);\n" +
                    $"    assign y = a + 8'd{i + 1};\n" +
                    "endmodule\n"));

            var top = new StringBuilder("module chain (input logic [7:0] a, output logic [7:0] y);\n");
            for (int i = 0; i <= count; i++)
                top.Append("    logic [7:0] w").Append(i).Append(";\n");
            top.Append("    assign w0 = a;\n");
            for (int i = 0; i < count; i++)
                top.Append($"    stage{i} s{i} (.a(w{i}), .y(w{i + 1}));\n");
            top.Append($"    assign y = w{count};\nendmodule\n");
            files.Add(new SourceFile("chain.sv", top.ToString()));

            files.Add(new SourceFile("chain_tb.sv",
                "module chain_tb;\n" +
                "    logic [7:0] a, y;\n" +
                "    logic [31:0] rom [0:2047];\n" +
                "    chain dut (.a(a), .y(y));\n" +
                "    initial begin\n" +
                "        $readmemh(\"image.hex\", rom);\n" +
                "        a = rom[0][7:0];\n" +
                "        #10 $display(\"%h\", y);\n" +
                "    end\n" +
                "endmodule\n"));

            ProjectImport import = ProjectImporter.Import(files);
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            Assert.AreEqual(count + 1, import.Project.Modules.Count(m => m.IsWritten), "sanity: every stage written, and the testbench; the chain itself is drawn");
            return import.Project;
        }

        [Test]
        public void ReadingAgain_InTheSameDocument_MakesNoKeyAgain()
        {
            Project project = Chain(16);
            List<Module> written = project.Modules.Where(m => m.IsWritten).ToList();
            // Read, and kept: twice, since a cache full from the tests before empties itself once on the way.
            for (int pass = 0; pass < 2; pass++)
                foreach (Module module in written)
                    Assert.IsNotNull(WrittenModules.Read(project, module), module.Name);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < 10; pass++)
                foreach (Module module in written)
                    WrittenModules.Read(project, module);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

            TestContext.WriteLine($"{bytes} bytes for {10 * written.Count} reads kept");
            Assert.Less(bytes, 16 * 1024, "a read kept for this document costs nothing again -- no key made of the project's text");
        }

        [Test]
        public void WhatAModuleUses_AskedAgain_ParsesNoKeyAgain()
        {
            Project project = Chain(16);
            List<Module> written = project.Modules.Where(m => m.IsWritten).ToList();
            for (int pass = 0; pass < 2; pass++)
                foreach (Module module in written)
                    WrittenModules.UsesOf(project, module).ToList();

            long before = GC.GetAllocatedBytesForCurrentThread();
            int asked = 0;
            for (int pass = 0; pass < 10; pass++)
                foreach (Module module in written)
                {
                    foreach (Module used in WrittenModules.UsesOf(project, module))
                        asked += used.Name.Length > 0 ? 1 : 0;
                }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            int calls = 10 * written.Count;

            TestContext.WriteLine($"{bytes} bytes for {calls} asks, {bytes / calls} an ask");
            Assert.AreEqual(10, asked, "sanity: the testbench uses the chain");
            Assert.Less(bytes / calls, 2 * 1024, "the text parsed for this document is kept for it: no key made of its text and its files");
        }
    }
}
