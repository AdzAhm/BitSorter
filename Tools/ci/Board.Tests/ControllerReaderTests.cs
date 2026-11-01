using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A controller read as the textbook draws it (Part 15h-6e): the three processors' controllers -- the multicycle one's
    /// eleven states, the single-cycle and pipelined ones' tables -- and the traffic light read as written; every corpus and
    /// exercise module read or saying why; and every reading held to the engine: for every state and every input, the next
    /// state the circuit works out is one the diagram has an arrow to, every arrow is taken, and every constant the diagram
    /// or a table shows is what the circuit outputs.
    /// </summary>
    public class ControllerReaderTests
    {
        [Test]
        public void TheMulticycleController_IsElevenStates_FromFetch_AndItsAluDecoder()
        {
            ControllerReading reading = Read("processor", "mcpu.sv", "mcpu_control");
            MachineReading machine = reading.Machine;
            CollectionAssert.AreEqual(new[] { "FETCH", "DECODE", "EXECUTE", "ALU_WB", "JUMP", "JUMP_REG", "BRANCH", "MEM_ADDR", "MEM_READ", "MEM_WB", "MEM_WRITE" },
                machine.States);
            Assert.AreEqual("state", machine.Register);
            Assert.AreEqual("state_next", machine.Next);
            Assert.AreEqual("FETCH", machine.States[machine.Start]);
            Assert.IsTrue(reading.Whole, "nothing in it the reading leaves out");

            CollectionAssert.AreEqual(new[] { "DECODE" }, Out(machine, "FETCH").Select(a => a.To).ToArray(), "FETCH always to DECODE");
            Assert.IsNull(Out(machine, "FETCH").Single().Condition);
            CollectionAssert.AreEqual(new[]
            {
                "EXECUTE: opcode is OP_REG, OP_IMM or OP_LUI", "ALU_WB: opcode is OP_AUIPC", "JUMP: opcode is OP_JAL",
                "JUMP_REG: opcode is OP_JALR", "BRANCH: opcode is OP_BRANCH", "MEM_ADDR: opcode is OP_LOAD or OP_STORE", "FETCH: otherwise",
            }, Out(machine, "DECODE").Select(a => a.To + ": " + a.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { "MEM_READ: opcode == OP_LOAD", "MEM_WRITE: otherwise" },
                Out(machine, "MEM_ADDR").Select(a => a.To + ": " + a.Condition).ToArray());

            CollectionAssert.AreEqual(new[] { "pc_we = 1", "ir_we = 1", "alu_a_sel = 1", "alu_b_sel = 2" }, Settings(machine, "FETCH"));
            CollectionAssert.AreEqual(new[] { "pc_we = taken", "pc_src = 1" }, Settings(machine, "BRANCH"));
            CollectionAssert.AreEqual(new[] { "alu_a_sel = 2", "alu_b_sel = 1" }, Settings(machine, "DECODE"));

            TableReading alu = reading.Tables.Single();
            Assert.AreEqual("funct3", alu.Subject);
            CollectionAssert.AreEqual(new[] { "alu_function" }, alu.Columns);
            CollectionAssert.AreEqual(new[] { "3'b000", "3'b001", "3'b010", "3'b011", "3'b100", "3'b101", "3'b110", "otherwise" }, alu.Rows.Select(r => r.Label).ToArray());
            Assert.AreEqual("ALU_SLL", alu.Rows[1].Cells[0].Text);
            Assert.AreEqual(1, alu.RowOf(LogicVec.FromUInt64(1, 3)), "funct3 = 1 picks the second row");
            Assert.AreEqual(7, alu.RowOf(LogicVec.FromUInt64(7, 3)), "and 7 the default's");
        }

        [TestCase("scpu.sv", "scpu_control")]
        [TestCase("pcpu.sv", "pcpu_control")]
        public void ACombinationalController_IsItsTwoTables(string file, string module)
        {
            ControllerReading reading = Read("processor", file, module);
            Assert.IsNull(reading.Machine);
            Assert.IsTrue(reading.Whole);
            CollectionAssert.AreEqual(new[] { "funct3", "opcode" }, reading.Tables.Select(t => t.Subject).ToArray());
            TableReading main = reading.Tables[1];
            CollectionAssert.AreEqual(new[] { "OP_REG", "OP_IMM", "OP_LUI", "OP_AUIPC", "OP_JAL", "OP_JALR", "OP_BRANCH", "OP_LOAD", "OP_STORE", "otherwise" },
                main.Rows.Select(r => r.Label).ToArray());
            TableRow lui = main.Rows[2];
            Assert.AreEqual("ALU_PASSB", lui.Cells[main.Columns.ToList().IndexOf("alu_op")].Text);
            Assert.IsFalse(lui.Cells[main.Columns.ToList().IndexOf("alu_op")].FromDefault);
            TableRow reg = main.Rows[0];
            Assert.IsTrue(reg.Cells[main.Columns.ToList().IndexOf("alu_b_imm")].FromDefault, "left to what is set before the case");
            Assert.AreEqual(2, main.RowOf(LogicVec.FromUInt64(0b0110111, 7)), "OP_LUI's code picks its row");
        }

        [Test]
        public void TheTrafficLight_IsItsFourStates_ItsCounterLeftToTheDrawing()
        {
            ControllerReading reading = Read("behavioural", "traffic_light.sv", "traffic_light");
            MachineReading machine = reading.Machine;
            CollectionAssert.AreEqual(new[] { "RED", "RED_AMBER", "GREEN", "AMBER" }, machine.States);
            Assert.AreEqual(0, machine.Start);
            CollectionAssert.AreEqual(new[] { "RED_AMBER: !walk", "RED: otherwise" }, Out(machine, "RED").Select(a => a.To + ": " + a.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { "RED" }, Out(machine, "AMBER").Select(a => a.To).ToArray(), "AMBER is the default arm");
            CollectionAssert.AreEqual(new[] { "red = 1", "amber = 1" }, Settings(machine, "RED_AMBER"));
            Assert.IsFalse(reading.Whole, "its counter is no part of the diagram");
        }

        [Test]
        public void TheMultipliersControl_IsAMealyMachine_ItsOutputsOnTheArrowsThatSetThem()
        {
            ControllerReading reading = Read("behavioural", "multiplier.sv", "mult_control");
            MachineReading machine = reading.Machine;
            CollectionAssert.AreEqual(new[] { "IDLE", "RUN" }, machine.States);
            CollectionAssert.AreEqual(new[] { "RUN: start", "IDLE: otherwise" }, Out(machine, "IDLE").Select(a => a.To + ": " + a.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { "count_next = 0", "load = 1" },
                machine.Arrows.Single(a => a.From == 0 && a.To == 1).Outputs.Select(o => o.ToString()).ToArray(),
                "start / load = 1, as the textbook writes a Mealy output, in the order the code sets them before the case");
            CollectionAssert.IsEmpty(Settings(machine, "IDLE"), "nothing IDLE sets on every way out of it");
            CollectionAssert.AreEqual(new[] { "count_next = count + 4'd1", "step = 1" }, Settings(machine, "RUN"));
            Assert.IsTrue(reading.Whole, "its counter is kept by the state's own process");
        }

        [Test]
        public void ADatapath_IsNoController()
        {
            Assert.IsNull(ReadOrNull("processor", "scpu.sv", "scpu_datapath", out string why));
            Assert.IsNotEmpty(why);
        }

        private static IEnumerable<TestCaseData> Designs()
        {
            foreach (string id in ExerciseFiles.Ids())
                yield return new TestCaseData("exercise", id).SetName("exercise " + id);
            foreach (string folder in new[] { "structural", "behavioural", "processor", "system" })
                foreach (string path in Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder), "*.sv").OrderBy(p => p, StringComparer.Ordinal))
                    yield return new TestCaseData(folder, Path.GetFileName(path)).SetName(folder + " " + Path.GetFileName(path));
        }

        /// <summary>Every module read or saying why, never throwing; and every reading held to the engine.</summary>
        [TestCaseSource(nameof(Designs))]
        public void EveryModule_IsReadOrSaysWhy_AndEveryReadingIsWhatTheEngineMakes(string kind, string name)
        {
            SyntaxTree tree = Tree(kind, name);
            foreach (ModuleDeclaration module in tree.Modules)
            {
                ControllerReading reading = ControllerReader.Read(tree, module, out string why);
                if (reading == null)
                {
                    Assert.IsNotEmpty(why, $"{module.Name}: no reading says why");
                    continue;
                }
                TestContext.WriteLine($"{name} {module.Name}: {(reading.Machine != null ? reading.Machine.States.Count + " states, " : "")}{reading.Tables.Count} tables{(reading.Whole ? ", whole" : "")}");
                HoldToTheEngine(tree, module.Name, reading);
            }
        }

        /// <summary>The reading against the module's netlist, worked out for every state and every value of every narrow input.</summary>
        private static void HoldToTheEngine(SyntaxTree tree, string module, ControllerReading reading)
        {
            Elaboration elaboration = Elaborator.Elaborate(tree, module);
            if (elaboration.HasErrors)
                return;   // a module only its parent's parameters make whole: drawn only inside it
            Design design = elaboration.Design;
            int top = design.Scopes.ToList().FindIndex(s => s.Kind == ScopeKind.Top);
            RtlNetlist netlist = RtlExtractor.Of(design, design.Scopes[top].Path);
            var random = new Random(module.Length * 7919 + module[0]);
            int Signal(string name) => design.Signals.ToList().FindIndex(s => s.Scope == top && (s.Name == name || s.Name.EndsWith("." + name, StringComparison.Ordinal)));
            // What the logic reads that nothing in it works out: the inputs, and what the registers hold -- a counter a
            // condition compares, say -- but the state's own register, which each trial sets.
            var held = new HashSet<int>(netlist.Cells.Where(c => c.Kind == RtlKind.Register && c.Signal >= 0).Select(c => c.Signal));
            if (reading.Machine != null)
                held.Remove(Signal(reading.Machine.Register));
            int[] inputs = Enumerable.Range(0, design.Signals.Count)
                .Where(i => design.Signals[i].Scope == top && (design.Signals[i].Port == PortDirection.Input || held.Contains(i))).ToArray();

            // Every value of each of those of eight bits or fewer, the rest random; a wider one sixteen random values.
            IEnumerable<LogicVec[]> Trials(Action<LogicVec[]> fix)
            {
                foreach (int input in inputs)
                {
                    int width = design.Signals[input].Width;
                    int count = width <= 8 ? 1 << width : 16;
                    for (int v = 0; v < count; v++)
                    {
                        var values = Randomised(design, random);
                        values[input] = width <= 8 ? LogicVec.FromUInt64((ulong)v, width) : Random(random, width);
                        fix(values);
                        yield return values;
                    }
                }
            }

            if (reading.Machine is MachineReading machine)
            {
                int register = Signal(machine.Register), next = Signal(machine.Next);
                Assert.GreaterOrEqual(register, 0, machine.Register);
                int nextNet = machine.Next != machine.Register ? NetOf(netlist, next)
                    : netlist.Cells.First(c => c.Kind == RtlKind.Register && c.Signal == register).Inputs[0];
                Assert.GreaterOrEqual(nextNet, 0, $"{module}: the next state's net");
                var taken = new HashSet<(int, int)>();
                for (int s = 0; s < machine.States.Count; s++)
                {
                    int state = s;
                    foreach (LogicVec[] values in Trials(v => v[register] = machine.Codes[state].Resize(design.Signals[register].Width)))
                    {
                        LogicVec[] nets = RtlEvaluator.Evaluate(netlist, values);
                        int to = machine.StateOf(nets[nextNet]);
                        Assert.IsTrue(machine.Arrows.Any(a => a.From == state && a.To == to),
                            $"{module}: from {machine.States[state]} the circuit goes to {(to >= 0 ? machine.States[to] : nets[nextNet].ToString())}, where the diagram has no arrow");
                        taken.Add((state, to));
                        foreach (ReadValue set in machine.Arrows.First(a => a.From == state && a.To == to).Outputs.Concat(machine.Settings[state]))
                            if (set.Constant is LogicVec constant && NetOf(netlist, Signal(set.Signal)) is int net && net >= 0)
                                Assert.IsTrue(Same(nets[net], constant), $"{module}: in {machine.States[state]}, {set.Signal} is {nets[net]}, the diagram says {set.Text}");
                    }
                }
                foreach (MachineArrow arrow in machine.Arrows)
                    Assert.IsTrue(taken.Contains((arrow.From, arrow.To)),
                        $"{module}: no input takes {machine.States[arrow.From]} to {machine.States[arrow.To]} ({arrow.Condition})");
            }

            foreach (TableReading table in reading.Tables)
            {
                int subject = Signal(table.Subject);
                if (subject < 0)
                    continue;   // a case on an expression: its rows are held only through the machine's settings
                for (int r = 0; r < table.Rows.Count; r++)
                    foreach (LogicVec match in table.Rows[r].Matches)
                    {
                        if (table.RowOf(match) != r)
                            continue;   // a label an earlier row already takes
                        for (int trial = 0; trial < 8; trial++)
                        {
                            LogicVec[] values = Randomised(design, random);
                            values[subject] = match.Resize(design.Signals[subject].Width);
                            LogicVec[] nets = RtlEvaluator.Evaluate(netlist, values);
                            for (int c = 0; c < table.Columns.Count; c++)
                                if (table.Rows[r].Cells[c].Constant is LogicVec constant && NetOf(netlist, Signal(table.Columns[c])) is int net && net >= 0)
                                    Assert.IsTrue(Same(nets[net], constant),
                                        $"{module}: {table.Subject} = {table.Rows[r].Label} gives {table.Columns[c]} = {nets[net]}, the table says {table.Rows[r].Cells[c].Text}");
                        }
                    }
            }
        }

        private static LogicVec[] Randomised(Design design, Random random)
        {
            var values = new LogicVec[design.Signals.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = Random(random, design.Signals[i].Width);
            return values;
        }

        private static LogicVec Random(Random random, int width)
        {
            ulong bits = ((ulong)(uint)random.Next() << 32) | (uint)random.Next();
            return LogicVec.FromUInt64(width >= 64 ? bits : bits & ((1UL << width) - 1), width);
        }

        /// <summary>The net a signal's whole value is on, the one something drives where there are several; -1 for none.</summary>
        private static int NetOf(RtlNetlist netlist, int signal)
        {
            if (signal < 0)
                return -1;
            int found = -1;
            foreach (RtlNet net in netlist.Nets)
                if (net.Signal == signal)
                {
                    if (net.Driver >= 0)
                        return net.Id;
                    found = net.Id;
                }
            return found;
        }

        private static bool Same(LogicVec value, LogicVec constant) =>
            value.TryGetUInt64(out ulong v) && constant.Resize(value.Width).TryGetUInt64(out ulong c) && v == c;

        /// <summary>The arrows out of <paramref name="state"/>, each to a state by its name.</summary>
        private static List<(string To, string Condition)> Out(MachineReading machine, string state)
        {
            int from = machine.States.ToList().IndexOf(state);
            return machine.Arrows.Where(a => a.From == from).Select(a => (machine.States[a.To], a.Condition)).ToList();
        }

        private static string[] Settings(MachineReading machine, string state) =>
            machine.Settings[machine.States.ToList().IndexOf(state)].Select(s => s.ToString()).ToArray();

        private static ControllerReading Read(string kind, string file, string module)
        {
            ControllerReading reading = ReadOrNull(kind, file, module, out string why);
            Assert.IsNotNull(reading, why);
            return reading;
        }

        private static ControllerReading ReadOrNull(string kind, string file, string module, out string why)
        {
            SyntaxTree tree = Tree(kind, file);
            return ControllerReader.Read(tree, tree.Modules.Single(m => m.Name == module), out why);
        }

        private static SyntaxTree Tree(string kind, string name)
        {
            if (kind == "exercise")
            {
                Project solution = ExerciseFiles.Solution(ExerciseFiles.Load(name));
                return BoardCompiler.Compile(solution).Tree;
            }
            string path = RepoRoot.Combine("Tools/ci/Oracle/corpus/" + kind + "/" + name);
            var files = new MemorySourceFiles();
            foreach (string other in Directory.GetFiles(Path.GetDirectoryName(path)))
                files.Add(Path.GetFileName(other), File.ReadAllText(other));
            return SyntaxReader.ReadText(File.ReadAllText(path), name, files);
        }
    }
}
