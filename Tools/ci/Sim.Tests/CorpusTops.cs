using System.Collections.Generic;
using System.Linq;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Every design a corpus file is: each of its modules that no other module of it instantiates, elaborated as the top. A
    /// file with two testbenches (<c>processor/mcpu.sv</c>) or a library of cells (<c>structural/cells.sv</c>) has no one
    /// top, so a test that elaborated it naming none stopped at SV0301 with no design, and a lint over it passed on nothing
    /// (the Part 16d sweep). A test over the corpus walks these instead.
    /// </summary>
    internal static class CorpusTops
    {
        /// <summary>The modules of <paramref name="tree"/> that nothing in it instantiates, in the order written.</summary>
        public static List<string> Of(SyntaxTree tree)
        {
            var instantiated = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ModuleDeclaration module in tree.Modules)
                Instantiated(module.Items, instantiated);

            return tree.Modules.Select(m => m.Name).Where(name => !instantiated.Contains(name)).Distinct().ToList();
        }

        /// <summary>Each top of <paramref name="tree"/>, elaborated: its name and what came of it.</summary>
        public static IEnumerable<(string Top, Elaboration Elaboration)> Elaborated(SyntaxTree tree)
        {
            foreach (string top in Of(tree))
                yield return (top, Elaborator.Elaborate(tree, top));
        }

        private static void Instantiated(List<ModuleItem> items, ISet<string> into)
        {
            foreach (ModuleItem item in items)
            {
                switch (item)
                {
                    case ModuleInstantiation mi:
                        into.Add(mi.Module);
                        break;
                    case GenerateRegion region:
                        Instantiated(region.Items, into);
                        break;
                    case GenerateBlock block:
                        Instantiated(block.Items, into);
                        break;
                    case LoopGenerate loop when loop.Body != null:
                        Instantiated(loop.Body.Items, into);
                        break;
                    case IfGenerate g:
                        if (g.Then != null) Instantiated(g.Then.Items, into);
                        if (g.Else != null) Instantiated(g.Else.Items, into);
                        break;
                }
            }
        }
    }
}
