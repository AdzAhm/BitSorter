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
    /// A state with an arrow not yet taken after that is looked at again before the arrow is said never taken: a wide input at
    /// the constants the logic compares with, each pair of inputs at every pair of their values where they are few enough, and
    /// every input at once at random among those -- an arrow two inputs take together, or a 16-bit compare's, was missed.
    ///
    /// A machine of one process -- the <c>case</c> in the <c>always_ff</c> -- is tried with its reset let go
    /// (<see cref="MachineReading.LetGo"/>), and what its arms set held to what each register is about to take, its D.
    ///
    /// A table's rows are tried at their labels' values, and its default at values no label takes, the subject held there --
    /// on its own net too, so a subject the module works out itself, a decoder on another's output, is tried at every row,
    /// however seldom or never the inputs bring it there. Not tried: a case on an expression (<c>case (instr[6:0])</c>) and a
    /// row whose labels are worked out rather than written, which nothing the check can hold the subject at bears out or not.
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
            var nets = new LogicVec[netlist.Nets.Count];   // every trial's, written over: none of it is kept past the trial
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

                // One trial in state s, its inputs given already: where the circuit goes must be an arrow, and what it sets what
                // the diagram says; null where it is.
                string Trial(int s)
                {
                    values[register] = machine.Codes[s].Resize(design.Signals[register].Width);
                    if (letGo >= 0)
                        values[letGo] = machine.LetGo.Value.Constant.Value.Resize(design.Signals[letGo].Width);
                    RtlEvaluator.Evaluate(netlist, values, nets);
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
                    return null;
                }

                bool AllTaken(int s)
                {
                    foreach (MachineArrow a in machine.Arrows)
                        if (a.From == s && !taken.Contains((a.From, a.To)))
                            return false;
                    return true;
                }

                for (int s = 0; s < machine.States.Count; s++)
                {
                    foreach (int input in inputs)
                    {
                        int width = design.Signals[input].Width;
                        int count = width <= 8 ? 1 << width : WideTrials;
                        for (int v = 0; v < count; v++)
                        {
                            Randomise();
                            values[input] = width <= 8 ? LogicVec.FromUInt64((ulong)v, width) : Random(random, width);
                            if (Trial(s) is string why)
                                return why;
                        }
                    }
                    if (!AllTaken(s) && SecondLook(s, inputs, Constants(netlist), Trial, AllTaken) is string missed)
                        return missed;
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
                // The subject held at the row's value, on its own net as well as its signal: a subject the module works out
                // itself -- a decoder on another's output -- is worked out again from the inputs by the netlist, whatever its
                // signal is given (the 16d sweep: such a table was never shown), and taking whichever row the inputs made it
                // pick left a row they seldom bring it to all but untried (the review of the 16d fixes).
                int width = design.Signals[subject].Width;
                int subjectNet = NetOf(netlist, subject);
                int heldNet = subjectNet >= 0 && netlist.Nets[subjectNet].Width == width ? subjectNet : -1;
                for (int r = 0; r < table.Rows.Count; r++)
                    foreach (LogicVec match in Tried(table, r, width, random))
                    {
                        LogicVec at = match.Resize(width);
                        for (int trial = 0; trial < RowTrials; trial++)
                        {
                            Randomise();
                            values[subject] = at;
                            RtlEvaluator.Evaluate(netlist, values, nets, heldNet, at);
                            for (int c = 0; c < table.Columns.Count; c++)
                                if (table.Rows[r].Cells[c].Constant is LogicVec constant && NetOf(netlist, Signal(table.Columns[c])) is int net && net >= 0
                                    && !Same(nets[net], constant))
                                    return $"{table.Subject} = {table.Rows[r].Label} gives {table.Columns[c]} = {nets[net]}, where the table says {table.Rows[r].Cells[c].Text}.";
                        }
                    }
            }
            return null;

            // A second look before an arrow out of state s is said never taken (the 16d sweep): one input at a time, the rest
            // at random, misses an arrow two inputs take together -- data == 200 && valid, about half the time by the seed --
            // and sixteen random tries of a wide input all but never meet the one value it is compared with. So each input is
            // tried at the values the logic compares it with too -- its constants, for a wide one -- each pair of inputs at
            // every pair of their values where they are few enough, and every input at once at random among them.
            string SecondLook(int s, List<int> tried, List<ulong> constants, Func<int, string> trial, Func<int, bool> allTaken)
            {
                int left = SecondLookTrials;
                var candidates = new List<LogicVec>[tried.Count];
                for (int i = 0; i < tried.Count; i++)
                    candidates[i] = Candidates(design.Signals[tried[i]].Width, constants);

                for (int i = 0; i < tried.Count; i++)
                    if (design.Signals[tried[i]].Width > 8)
                        foreach (LogicVec value in candidates[i])
                            for (int again = 0; again < 2; again++)
                            {
                                Randomise();
                                values[tried[i]] = value;
                                if (trial(s) is string why)
                                    return why;
                                if (allTaken(s) || --left == 0)
                                    return null;
                            }

                for (int i = 0; i < tried.Count; i++)
                    for (int j = i + 1; j < tried.Count; j++)
                        if (candidates[i].Count * candidates[j].Count <= PairTrials)
                            foreach (LogicVec a in candidates[i])
                                foreach (LogicVec b in candidates[j])
                                {
                                    Randomise();
                                    values[tried[i]] = a;
                                    values[tried[j]] = b;
                                    if (trial(s) is string why)
                                        return why;
                                    if (allTaken(s) || --left == 0)
                                        return null;
                                }

                for (int t = 0; t < LooseTrials; t++)
                {
                    Randomise();
                    for (int i = 0; i < tried.Count; i++)
                        values[tried[i]] = candidates[i][random.Next(candidates[i].Count)];
                    if (trial(s) is string why)
                        return why;
                    if (allTaken(s) || --left == 0)
                        return null;
                }
                return null;
            }
        }

        /// <summary>
        /// How many pairs of values two inputs are tried at together, how many tries every input at once has, and the most trials
        /// a second look at a state makes in all -- a reading worked out once, as its module opens, in a frame.
        /// </summary>
        public const int PairTrials = 1024, LooseTrials = 256, SecondLookTrials = 16384;

        /// <summary>Every value written as a constant in <paramref name="netlist"/>, as a number, each once: what its logic compares with.</summary>
        private static List<ulong> Constants(RtlNetlist netlist)
        {
            var constants = new List<ulong>();
            foreach (RtlCell cell in netlist.Cells)
                if (cell.Kind == RtlKind.Constant && cell.Value.Width > 0 && cell.Value.TryGetUInt64(out ulong c) && !constants.Contains(c))
                    constants.Add(c);
            return constants;
        }

        /// <summary>
        /// The values an input of <paramref name="width"/> bits is tried at in a second look: every one at eight bits or fewer;
        /// wider, every one of <paramref name="constants"/> it can hold, and 0.
        /// </summary>
        private static List<LogicVec> Candidates(int width, List<ulong> constants)
        {
            var candidates = new List<LogicVec>();
            if (width <= 8)
            {
                for (int v = 0; v < 1 << width; v++)
                    candidates.Add(LogicVec.FromUInt64((ulong)v, width));
                return candidates;
            }
            ulong mask = width >= 64 ? ulong.MaxValue : (1UL << width) - 1;
            var seen = new HashSet<ulong> { 0 };
            candidates.Add(LogicVec.FromUInt64(0, width));
            foreach (ulong c in constants)
                if ((c & ~mask) == 0 && seen.Add(c))
                    candidates.Add(LogicVec.FromUInt64(c, width));
            return candidates;
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

        /// <summary>
        /// Whether the circuit gives <paramref name="value"/> where the reading shows <paramref name="constant"/>: the same number,
        /// or, for a constant the code writes with an x or a z in it -- a decoder's <c>default: a = 2'bxx;</c>, leaving it free
        /// -- the same bits, x for x (the review of the 16d fixes: once the default row was tried, an x never matched).
        /// </summary>
        private static bool Same(LogicVec value, LogicVec constant)
        {
            LogicVec wanted = constant.Resize(value.Width);
            if (!wanted.IsKnown)
                return LogicVec.CaseEqual(value, wanted).IsTrue;
            return value.TryGetUInt64(out ulong v) && wanted.TryGetUInt64(out ulong c) && v == c;
        }
    }
}
