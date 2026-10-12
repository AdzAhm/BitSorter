using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.SystemVerilog.Tests.Syntax
{
    /// <summary>Shared helpers for the reader's tests.</summary>
    internal static class SyntaxKit
    {
        /// <summary>Reads text, and fails the test if the read said anything at all.</summary>
        public static SyntaxTree Clean(string text, ISourceFiles files = null)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv", files);
            Assert.IsEmpty(tree.Diagnostics, "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return tree;
        }

        /// <summary>Reads text that is expected to draw diagnostics.</summary>
        public static SyntaxTree Read(string text, ISourceFiles files = null) => SyntaxReader.ReadText(text, "test.sv", files);

        public static ModuleDeclaration OnlyModule(string text) => Clean(text).Modules.Single();

        /// <summary>The items of a module whose body is <paramref name="body"/>.</summary>
        public static List<ModuleItem> Items(string body) => OnlyModule("module m;\n" + body + "\nendmodule\n").Items;

        /// <summary>One expression, read as the right side of an assign.</summary>
        public static Expression Expr(string text) =>
            ((ContinuousAssign)Items("assign y = " + text + ";").Single()).Assignments.Single().Value;

        /// <summary>The statements of an initial block's begin-end.</summary>
        public static List<Statement> Statements(string body)
        {
            var process = (ProceduralBlock)Items("initial begin\n" + body + "\nend").Single();
            return ((BlockStatement)process.Body).Statements;
        }

        /// <summary>Codes of every diagnostic, in order.</summary>
        public static string[] Codes(SyntaxTree tree) => tree.Diagnostics.Select(d => d.Code).ToArray();

        public static string CorpusFolder(string name) => RepoRoot.Combine(Path.Combine("Tools", "ci", "Oracle", "corpus", name));

        /// <summary>Every .sv file of the oracle corpus, as paths relative to the corpus folder.</summary>
        public static IEnumerable<string> CorpusFiles()
        {
            string root = RepoRoot.Combine(Path.Combine("Tools", "ci", "Oracle", "corpus"));
            return Directory.GetFiles(root, "*.sv", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                .OrderBy(p => p);
        }

        /// <summary>The corpus as files a read can include from, keyed by their path under the corpus folder.</summary>
        public static MemorySourceFiles CorpusSources()
        {
            string root = RepoRoot.Combine(Path.Combine("Tools", "ci", "Oracle", "corpus"));
            var files = new MemorySourceFiles();
            foreach (string relative in CorpusFiles())
                files.Add(relative, File.ReadAllText(Path.Combine(root, relative)));
            return files;
        }
    }
}
