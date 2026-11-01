using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A controller's reading held to the engine (Part 15h-6e): the module's netlist worked out (<see cref="RtlEvaluator"/>)
    /// for every state and every value of every input and held register of eight bits or fewer -- the next state is one the
    /// diagram has an arrow to, every arrow is taken, and every constant a box, an arrow or a table shows is what the circuit
    /// outputs. Shared with the view-model's tests, which hold the start page's samples to it.
    /// </summary>
    public static class ControllerEngine
    {
        /// <summary>The reading against the module's netlist, worked out for every state and every value of every narrow input.</summary>
        public static void Hold(SyntaxTree tree, string module, ControllerReading reading)
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
    }
}
