using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// 4.x boards made into documents, held to what migration promises (section 8): every output's
    /// settled value after each vector, read just before the vector's clock edge.
    /// </summary>
    public class MigratorTests
    {
        /// <summary>What the migrated board's bins disagree on with what the circuit expected.</summary>
        public static List<string> Check(MigrationResult migrated, LegacyCircuit circuit) => migrated.Mismatches(circuit.expected);

        private static LegacyFixture Source(string id, int x, int y, string stream) => new LegacyFixture { id = id, source = true, x = x, y = y, stream = stream };
        private static LegacyFixture Sink(string id, int x, int y) => new LegacyFixture { id = id, x = x, y = y };
        private static LegacyGate Gate(string kind, int x, int y) => new LegacyGate { kind = kind, x = x, y = y };

        private static LegacyWire Wire(int fx, int fy, int tx, int ty, int toPort = 0, int delay = 1, int fromPort = 0) =>
            new LegacyWire { fromX = fx, fromY = fy, fromPort = fromPort, toX = tx, toY = ty, toPort = toPort, delay = delay };

        /// <summary>The half adder of the 3.0.2 save that WholeSaveMigrationTests reads.</summary>
        public static LegacyCircuit HalfAdder() => new LegacyCircuit
        {
            name = "half-adder",
            fixtures = new[] { Source("a", -3, 1, "0011"), Source("b", -3, -1, "0101"), Sink("sum", 3, 1), Sink("carry", 3, -1) },
            gates = new[] { Gate("Xor", 0, 1), Gate("And", 0, -1) },
            wires = new[]
            {
                Wire(-3, 1, 0, 1, 0), Wire(-3, -1, 0, 1, 1), Wire(-3, 1, 0, -1, 0), Wire(-3, -1, 0, -1, 1),
                Wire(0, 1, 3, 1), Wire(0, -1, 3, -1),
            },
            expected = new[] { new LegacyExpectation { sink = "sum", values = "0110" }, new LegacyExpectation { sink = "carry", values = "0001" } },
        };

        [Test]
        public void AHalfAdder_ComesAcrossAsPortsAndGates_AndAddsAsItDid()
        {
            LegacyCircuit circuit = HalfAdder();
            MigrationResult migrated = Migrator.Migrate(circuit);

            Module top = migrated.Project.TopModule;
            Assert.AreEqual("half_adder", top.Name, "the level's file name, made a name SystemVerilog takes");
            CollectionAssert.AreEqual(new[] { "a", "b", "sum", "carry" }, top.Ports.Select(p => p.Name));
            Assert.AreEqual(new Placement(-3, 1), top.Layout.PlacementOf(top.FindPart("a").Id), "a cell is a position, one to one");

            GatePart[] gates = top.Parts.OfType<GatePart>().ToArray();
            CollectionAssert.AreEqual(new[] { GateKind.Xor, GateKind.And }, gates.Select(g => g.Kind));
            Assert.IsTrue(gates.All(g => g.Rise == Migrator.GateDelay && g.Fall == Migrator.GateDelay), "a gate takes the cell's 1");
            Assert.IsEmpty(BoardRules.Check(migrated.Project).Where(d => d.IsError));

            CollectionAssert.IsEmpty(Check(migrated, circuit));
        }

        [Test]
        public void AWireLongerThanATick_BecomesABufferOfTheRest()
        {
            var circuit = new LegacyCircuit
            {
                name = "slow",
                fixtures = new[] { Source("a", -2, 0, "0110"), Sink("y", 2, 0) },
                gates = new[] { Gate("Not", 0, 0) },
                wires = new[] { Wire(-2, 0, 0, 0, delay: 4), Wire(0, 0, 2, 0) },
                expected = new[] { new LegacyExpectation { sink = "y", values = "1001" } },
            };

            MigrationResult migrated = Migrator.Migrate(circuit);
            GatePart buffer = migrated.Project.TopModule.Parts.OfType<GatePart>().Single(g => g.Kind == GateKind.Buf);

            Assert.AreEqual(3, buffer.Rise, "a wire of 4 is a buffer of 3: the gate's own 1 is the rest");
            Assert.AreEqual(new Placement(-1, 0), migrated.Project.TopModule.Layout.PlacementOf(buffer.Id), "half-way along the wire");
            CollectionAssert.IsEmpty(Check(migrated, circuit));
        }

        [Test]
        public void ARegister_IsAFlipFlopFromZero_OnAClock_HandingOnEachBitAVectorLate()
        {
            // A toggle: the register holds q, and q XOR t goes back into it -- flip on a one.
            var circuit = new LegacyCircuit
            {
                name = "flip-on-one",
                clockPeriod = 3,
                fixtures = new[] { Source("t", -3, 0, "01101"), Sink("q", 3, 0) },
                gates = new[] { Gate("Xor", -1, 0), Gate("Register", 1, 0) },
                wires = new[] { Wire(-3, 0, -1, 0, 0), Wire(1, 0, -1, 0, 1), Wire(-1, 0, 1, 0), Wire(1, 0, 3, 0) },
                expected = new[] { new LegacyExpectation { sink = "q", values = "001001" } },
            };

            MigrationResult migrated = Migrator.Migrate(circuit);
            Module top = migrated.Project.TopModule;
            RegisterPart register = top.Parts.OfType<RegisterPart>().Single();

            Assert.AreEqual("0", register.Initial?.Format(Radix.Binary), "every 4.x register started at 0");
            Assert.AreEqual("clk", migrated.Project.Stimulus.Clocks.Single().Port);
            Assert.AreEqual(migrated.Cycle, migrated.Project.Stimulus.Clocks.Single().Period, "one clock edge a vector");
            Assert.AreEqual(6, migrated.Vectors, "the bin's expectation runs one past the stream");
            CollectionAssert.IsEmpty(Check(migrated, circuit));
        }

        [Test]
        public void ABlock_IsAModule_MadeOnceHoweverOftenItIsPlaced()
        {
            var inverter = new LegacyBlock
            {
                name = "OR2",
                inputs = new[] { new LegacyPort { id = "in", x = -2, y = 0 } },
                outputs = new[] { new LegacyPort { id = "out", x = 2, y = 0 } },
                gates = new[] { Gate("Not", 0, 0) },
                wires = new[] { Wire(-2, 0, 0, 0, delay: 2), Wire(0, 0, 2, 0) },
            };

            var circuit = new LegacyCircuit
            {
                name = "twice",
                fixtures = new[] { Source("a", -4, 0, "01"), Sink("y", 4, 0) },
                blocks = new[] { new LegacyBlockPlacement { x = -1, y = 0, block = inverter }, new LegacyBlockPlacement { x = 1, y = 0, block = inverter } },
                wires = new[] { Wire(-4, 0, -1, 0), Wire(-1, 0, 1, 0), Wire(1, 0, 4, 0) },
                expected = new[] { new LegacyExpectation { sink = "y", values = "01" } },
            };

            MigrationResult migrated = Migrator.Migrate(circuit);

            Assert.AreEqual(2, migrated.Project.Modules.Count, "the board and one module for the block");
            Module block = migrated.Project.Modules.Single(m => m.Name != "twice");
            Assert.AreEqual("OR2_2", block.Name, "OR2 is one of the cells a printed board uses, so the block steps aside");
            CollectionAssert.AreEqual(new[] { "in", "out" }, block.Ports.Select(p => p.Name), "the block's own port names");
            Assert.AreEqual(2, migrated.Project.TopModule.Parts.OfType<BlockPart>().Count());
            CollectionAssert.IsEmpty(Check(migrated, circuit));
        }

        [TestCase("sum", "sum")]
        [TestCase("and", "and_")]
        [TestCase("3rd", "in_3rd")]
        [TestCase("a-b", "a_b")]
        public void A4xName_BecomesOneSystemVerilogTakes(string id, string expected) =>
            Assert.AreEqual(expected, Migrator.NameFrom(id, "in"));

        [Test]
        public void TwoWiresIntoOneInput_ComeAcross_AsTheErrorTheyNowAre()
        {
            var circuit = new LegacyCircuit
            {
                name = "collide",
                fixtures = new[] { Source("a", -2, 1, "0"), Source("b", -2, -1, "1"), Sink("y", 2, 0) },
                gates = new[] { Gate("Not", 0, 0) },
                wires = new[] { Wire(-2, 1, 0, 0), Wire(-2, -1, 0, 0), Wire(0, 0, 2, 0) },
            };

            MigrationResult migrated = Migrator.Migrate(circuit);

            BoardDiagnostic error = BoardRules.Check(migrated.Project).Single(d => d.IsError);
            StringAssert.Contains("a.out and b.out both drive one wire", error.Message, "it loads, and says what is wrong");
        }
    }
}
