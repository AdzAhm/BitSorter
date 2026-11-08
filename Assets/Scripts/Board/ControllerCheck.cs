using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.Board
{
    /// <summary>
    /// A controller's reading held to the engine before it is shown (Part 15h-6e): the instance's netlist worked out
    /// (<see cref="RtlEvaluator"/>) for every state and every value of each input and held register of eight bits or fewer,
    /// the others random -- the next state must be one the diagram has an arrow to, every arrow must be taken, and every
    /// constant a box, an arrow or a table shows must be what the circuit gives. A reading that fails is not shown: the
    /// module is drawn as its code builds instead, so a diagram never shows a circuit that is not there.
    /// </summary>
    /// <remarks>
    /// A machine of one process -- the <c>case</c> in the <c>always_ff</c> -- is tried with its reset let go
    /// (<see cref="MachineReading.LetGo"/>), and what its arms set held to what each register is about to take, its D.
    ///
    /// A table's rows are tried at their labels' values, and its default at values no label takes; each trial holds the
    /// constants of the row the circuit's own subject picks, which for a subject the module works out itself is whichever
    /// the inputs make it. Not tried: a case on an expression (<c>case (instr[6:0])</c>), a row whose labels are worked out
    /// rather than written, and a row the circuit never comes to -- nothing the circuit does bears those out or not.
    ///
    /// What it does not hold is the words: an arrow's condition and a cell's expression are the code's own, printed as written.
    /// The tests hold every corpus, exercise and sample controller to it (<c>ControllerEngine</c>).
    /// </remarks>
    public static class ControllerCheck
    {
        /// <summary>How many values a wide input is tried at, and how many each table row is tried with.</summary>
        public const int WideTrials = 16, RowTrials = 8;

        /// <summary>
        /// Null where <paramref name="reading"/> is what <paramref name="netlist"/> -- the netlist of the instance at
        /// <paramref name="path"/> in <paramref name="design"/> -- does; else the first thing it does otherwise.
        /// </summary>
        public static string Why(ControllerReading reading, Design design, RtlNetlist netlist, string path)
        {
            if (reading == null || design == null || netlist == null)
                throw new ArgumentNullException(reading == null ? nameof(reading) : design == null ? nameof(design) : nameof(netlist));
            int scope = -1;
            for (int i = 0; i < design.Scopes.Count; i++)
                if (design.Scopes[i].Path == path)
                    scope = i;
            if (scope < 0)
                return "There is no instance " + path + " to hold it to.";

            int Signal(string name)
            {
                for (int i = 0; i < design.Signals.Count; i++)
                    if (design.Signals[i].Scope == scope && (design.Signals[i].Name == name || design.Signals[i].Name.EndsWith("." + name, StringComparison.Ordinal)))
                        return i;
                return -1;
            }

            // Only what the netlist reads is given a value, in one array used for every trial: its nets' signals, what its
            // registers hold, and a memory's words, which a word cell reads straight off the signals -- as DrawnValues reads
            // them (the 16d sweep: left with none, the first word read threw).
            var read = new HashSet<int>();
            foreach (RtlNet net in netlist.Nets)
                if (net.Signal >= 0)
                    read.Add(net.Signal);
            var held = new HashSet<int>();
            foreach (RtlCell cell in netlist.Cells)
            {
                if (cell.Kind == RtlKind.Register && cell.Signal >= 0)
                {
                    read.Add(cell.Signal);
                    held.Add(cell.Signal);
                }
                if (cell.Kind == RtlKind.Word && cell.Memory != null)
                    for (int word = 0; word < cell.Memory.Depth; word++)
                        read.Add(cell.Memory.First.Index + word);
            }
            var values = new LogicVec[design.Signals.Count];
            var random = new Random(7919);
            void Randomise()
            {
                foreach (int s in read)
                    values[s] = Random(random, design.Signals[s].Width);
            }

            // What the logic reads that nothing in it works out: the inputs, and what registers hold, but the state's.
            int register = reading.Machine != null ? Signal(reading.Machine.Register) : -1;
            held.Remove(register);
            var inputs = new List<int>();
            for (int i = 0; i < design.Signals.Count; i++)
                if (design.Signals[i].Scope == scope && (design.Signals[i].Port == BitSorter.SystemVerilog.Syntax.PortDirection.Input || held.Contains(i)))
                    inputs.Add(i);

            if (reading.Machine is MachineReading machine)
            {
                if (register < 0)
                    return "Its state register " + machine.Register + " is not in the instance.";
                // One process works out the next state and holds it (the case in the always_ff): its next state is the
                // register's D, which the reset is folded into, so the arms are tried with the reset let go -- as two processes'
                // next state never sees it -- and what an arm sets is what the register it sets is about to take, its D, not
                // what it holds (the 16d sweep: no such machine with a reset or an output was ever shown).
                bool oneProcess = machine.Next == machine.Register;
                int letGo = machine.LetGo is ReadValue go ? Signal(go.Signal) : -1;
                if (letGo >= 0)
                    inputs.Remove(letGo);
                int Set(string name)
                {
                    int signal = Signal(name);
                    return oneProcess && RegisterInput(netlist, signal) is int d && d >= 0 ? d : NetOf(netlist, signal);
                }

                int nextNet = !oneProcess ? NetOf(netlist, Signal(machine.Next)) : RegisterInput(netlist, register);
                if (nextNet < 0)
                    return "Its next state " + machine.Next + " is not in the netlist.";
                var taken = new HashSet<(int, int)>();
                for (int s = 0; s < machine.States.Count; s++)
                    foreach (int input in inputs)
                    {
                        int width = design.Signals[input].Width;
                        int count = width <= 8 ? 1 << width : WideTrials;
                        for (int v = 0; v < count; v++)
                        {
                            Randomise();
                            values[input] = width <= 8 ? LogicVec.FromUInt64((ulong)v, width) : Random(random, width);
                            values[register] = machine.Codes[s].Resize(design.Signals[register].Width);
                            if (letGo >= 0)
                                values[letGo] = machine.LetGo.Value.Constant.Value.Resize(design.Signals[letGo].Width);
                            LogicVec[] nets = RtlEvaluator.Evaluate(netlist, values);
                            int to = machine.StateOf(nets[nextNet]);
                            MachineArrow? arrow = null;
                            foreach (MachineArrow a in machine.Arrows)
                                if (a.From == s && a.To == to)
                                    arrow = a;
                            if (!(arrow is MachineArrow way))
                                return $"From {machine.States[s]} the circuit goes to {(to >= 0 ? machine.States[to] : nets[nextNet].ToString())}, where the diagram has no arrow.";
                            taken.Add((s, to));
                            foreach (ReadValue set in way.Outputs)
                                if (Differs(set, nets, Set) is string why)
                                    return $"From {machine.States[s]} to {machine.States[to]}, {why}";
                            foreach (ReadValue set in machine.Settings[s])
                                if (Differs(set, nets, Set) is string why)
                                    return $"In {machine.States[s]}, {why}";
                        }
                    }
                foreach (MachineArrow arrow in machine.Arrows)
                    if (!taken.Contains((arrow.From, arrow.To)))
                        return $"No input takes {machine.States[arrow.From]} to {machine.States[arrow.To]}.";
            }

            foreach (TableReading table in reading.Tables)
            {
                int subject = Signal(table.Subject);
                if (subject < 0)
                    continue;   // a case on an expression: its rows are not tried
                int subjectNet = NetOf(netlist, subject);
                for (int r = 0; r < table.Rows.Count; r++)
                    foreach (LogicVec match in Tried(table, r, design.Signals[subject].Width, random))
                    {
                        for (int trial = 0; trial < RowTrials; trial++)
                        {
                            Randomise();
                            values[subject] = match.Resize(design.Signals[subject].Width);
                            LogicVec[] nets = RtlEvaluator.Evaluate(netlist, values);

                            // The row the circuit's own subject picks: the one forced where nothing in the module works the
                            // subject out, else whichever the inputs make it -- the netlist works a driven subject out again
                            // from them, whatever it is given (the 16d sweep: a decoder on another's output was never shown).
                            int row = subjectNet >= 0 ? table.RowOf(nets[subjectNet]) : r;
                            if (row < 0)
                                continue;   // a value no row takes, nor a default
                            for (int c = 0; c < table.Columns.Count; c++)
                                if (table.Rows[row].Cells[c].Constant is LogicVec constant && NetOf(netlist, Signal(table.Columns[c])) is int net && net >= 0
                                    && !Same(nets[net], constant))
                                    return $"{table.Subject} = {table.Rows[row].Label} gives {table.Columns[c]} = {nets[net]}, where the table says {table.Rows[row].Cells[c].Text}.";
                        }
                    }
            }
            return null;
        }

        /// <summary>
        /// The values of a table's subject row <paramref name="r"/> is tried at: its labels' that no earlier row takes; for the
        /// default row, which has none, values no label takes -- every one of a subject of eight bits or fewer, spread to
        /// <see cref="WideTrials"/> of them, or as many random ones that fall to it (the 16d sweep: the default was never tried).
        /// A row whose labels are worked out, not written, is not tried.
        /// </summary>
        private static List<LogicVec> Tried(TableReading table, int r, int width, Random random)
        {
            var tried = new List<LogicVec>();
            TableRow row = table.Rows[r];
            foreach (LogicVec match in row.Matches)
                if (table.RowOf(match) == r)
                    tried.Add(match);
            if (!row.Otherwise)
                return tried;

            var falling = new List<LogicVec>();
            if (width <= 8)
            {
                for (int v = 0; v < 1 << width; v++)
                {
                    LogicVec value = LogicVec.FromUInt64((ulong)v, width);
                    if (table.RowOf(value) == r)
                        falling.Add(value);
                }
                for (int i = 0; i < Math.Min(WideTrials, falling.Count); i++)
                    tried.Add(falling[i * falling.Count / Math.Min(WideTrials, falling.Count)]);
            }
            else
                for (int attempt = 0; attempt < 4 * WideTrials && tried.Count < WideTrials; attempt++)
                {
                    LogicVec value = Random(random, width);
                    if (table.RowOf(value) == r)
                        tried.Add(value);
                }
            return tried;
        }

        /// <summary>Why <paramref name="set"/>'s constant is not what the net <paramref name="netOf"/> gives its signal holds; null where it is, or is no constant.</summary>
        private static string Differs(ReadValue set, LogicVec[] nets, Func<string, int> netOf)
        {
            if (!(set.Constant is LogicVec constant))
                return null;
            int net = netOf(set.Signal);
            return net >= 0 && !Same(nets[net], constant) ? $"{set.Signal} is {nets[net]}, where it says {set.Text}." : null;
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

        /// <summary>A register's D: the first input of the register cell holding the signal; -1 for none.</summary>
        private static int RegisterInput(RtlNetlist netlist, int signal)
        {
            foreach (RtlCell cell in netlist.Cells)
                if (cell.Kind == RtlKind.Register && cell.Signal == signal && cell.Inputs.Count > 0)
                    return cell.Inputs[0];
            return -1;
        }

        private static bool Same(LogicVec value, LogicVec constant) =>
            value.TryGetUInt64(out ulong v) && constant.Resize(value.Width).TryGetUInt64(out ulong c) && v == c;
    }
}
