using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The one rule left of the 4.x board migration: how a name becomes one SystemVerilog takes. The migration itself, and
    /// the tests that held it to every shipped level, went with the 4.x game (Part 16b).
    /// </summary>
    public class MigratorTests
    {
        [TestCase("sum", "sum")]
        [TestCase("and", "and_")]
        [TestCase("3rd", "in_3rd")]
        [TestCase("a-b", "a_b")]
        [TestCase("half-adder", "half_adder")]
        [TestCase("", "in")]
        public void A4xName_BecomesOneSystemVerilogTakes(string id, string expected) =>
            Assert.AreEqual(expected, Migrator.NameFrom(id, "in"));
    }
}
