using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A controller's reading held to the engine in the tests (Part 15h-6e): the module elaborated as the top, its netlist
    /// worked out by <see cref="ControllerCheck"/> -- the same check the workbench makes before it shows a reading. Shared with
    /// the view-model's tests, which hold the start page's samples to it.
    /// </summary>
    public static class ControllerEngine
    {
        public static void Hold(SyntaxTree tree, string module, ControllerReading reading)
        {
            Elaboration elaboration = Elaborator.Elaborate(tree, module);
            if (elaboration.HasErrors)
                return;   // a module only its parent's parameters make whole: held where it is drawn, inside it
            Design design = elaboration.Design;
            ScopeInfo top = design.Scopes.First(s => s.Kind == ScopeKind.Top);
            Assert.IsNull(ControllerCheck.Why(reading, design, RtlExtractor.Of(design, top.Path), top.Path), module);
        }
    }
}
