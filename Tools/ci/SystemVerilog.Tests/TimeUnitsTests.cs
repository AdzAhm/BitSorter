using System;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.SystemVerilog.Tests
{
    /// <summary>The time units, as SystemVerilog writes them and as a project counts in them (Part 9b-3).</summary>
    public class TimeUnitsTests
    {
        [Test]
        public void TheUnits_AreSystemVerilogs_SToFs_AndPsIsTheDefault_AsInTheCoursesModelSim()
        {
            CollectionAssert.AreEqual(new[] { "s", "ms", "us", "ns", "ps", "fs" }, TimeUnits.All.ToArray());
            Assert.AreEqual("ps", TimeUnits.Default);
            Assert.IsTrue(TimeUnits.IsUnit(TimeUnits.Default));
        }

        [TestCase("ns", true)]
        [TestCase("ps", true)]
        [TestCase("s", true)]
        [TestCase("NS", false)]
        [TestCase("ns ", false)]
        [TestCase("sec", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void AUnit_IsOneOnlyAsSystemVerilogWritesIt(string name, bool unit) => Assert.AreEqual(unit, TimeUnits.IsUnit(name));

        [Test]
        public void ATimescale_IsOneWholeUnit_ItsOwnPrecision()
        {
            Assert.AreEqual("1ps/1ps", TimeUnits.Timescale("ps"));
            Assert.AreEqual("1us/1us", TimeUnits.Timescale("us"));
            Assert.Throws<ArgumentException>(() => TimeUnits.Timescale("weeks"));
        }

        [TestCase("1ns/1ps", "ns")]
        [TestCase("1ps/1ps", "ps")]
        [TestCase(" 1 us / 1 ns ", "us")]
        [TestCase("1s/1ms", "s")]
        [TestCase("1ns", "ns")]
        public void TheUnitOfATimescale_IsItsFirstHalf(string timescale, string unit)
        {
            Assert.IsTrue(TimeUnits.TryUnitOf(timescale, out string read), timescale);
            Assert.AreEqual(unit, read);
        }

        [TestCase("10ns/1ns")]
        [TestCase("100ps/1ps")]
        [TestCase("1weeks/1s")]
        [TestCase("ns/ps")]
        [TestCase("")]
        [TestCase(null)]
        public void ATimescaleOfTensOfAUnit_OrNoneAtAll_HasNoUnitAProjectCanCountIn(string timescale) =>
            Assert.IsFalse(TimeUnits.TryUnitOf(timescale, out _), timescale ?? "null");

        [Test]
        public void ADelayWrittenWithAUnit_IsReadAsItsNumber_AndTheWarningNamesNoUnitOfItsOwn()
        {
            SyntaxTree tree = SyntaxReader.ReadText("module m(output logic y, input logic a);\n  assign #10ns y = a;\nendmodule\n", "test.sv", null);

            Diagnostic warning = tree.Diagnostics.Single();
            Assert.AreEqual(DiagnosticCodes.Unsupported, warning.Code);
            StringAssert.Contains("#10ns is ignored", warning.Message);
            StringAssert.Contains("the design's own time unit", warning.Message, "the unit is the design's, which need not be ns");
            StringAssert.DoesNotContain("(ns)", warning.Message, "no unit is hard-coded");
        }
    }
}
