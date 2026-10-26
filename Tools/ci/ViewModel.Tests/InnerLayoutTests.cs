using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A module inside the design drawn as one of its instances (Part 12a's ground): the full adder's half adder laid
    /// out on its own, every signal it draws read under h1's path -- so stepping inside a block shows that block's values.
    /// </summary>
    public class InnerLayoutTests
    {
        [Test]
        public void AnInnerModule_IsLaidOut_WithItsSignalsUnderTheInstancesPath()
        {
            Project project = WorkbenchSamples.FullAdder();
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
            Design design = Elaborator.Elaborate(compiled.Tree, compiled.Top).Design;

            string h1 = compiled.DesignPath + ".h1";
            SchematicLayout inner = DocumentSchematic.Build(project, compiled, "half_adder", h1);
            Assert.IsNotNull(inner);
            Assert.AreEqual(2, inner.Parts.Count(p => p.Symbol != PartSymbol.Block), "the half adder's two gates");
            CollectionAssert.AreEquivalent(new[] { "a", "b", "s", "c" }, inner.Ports.Select(p => p.Name), "its four ports");

            foreach (PortLayout port in inner.Ports)
            {
                StringAssert.StartsWith(h1 + ".", port.Signal, "read under h1");
                Assert.IsTrue(design.TryFind(port.Signal, out _), $"{port.Signal} is a signal of the design");
            }

            foreach (NetLayout net in inner.Nets)
                Assert.IsTrue(design.TryFind(net.Signal, out _), $"the net {net.Name} reads {net.Signal}, which the design has");

            Assert.IsNull(DocumentSchematic.Build(project, compiled, "no_such_module"), "a module the project has not");
            Assert.AreEqual(DocumentSchematic.Build(project, compiled).Parts.Count, DocumentSchematic.Build(project, compiled, project.Top).Parts.Count,
                "and the top by name is the top");
        }
    }
}
