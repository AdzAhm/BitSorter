using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// A process opened into cells (Part 15h-2): its statements run symbolically, from its wait to the next time it waits, every
    /// path at once. A path keeps the value each signal has on it; two paths meeting again are joined by a mux on the condition
    /// that parted them; a loop whose condition is a constant is simply run, so one with constant bounds unrolls; and at the end
    /// an <c>always_comb</c>'s writes are its outputs, an <c>always_ff</c>'s the D of a register each.
    /// </summary>
    /// <remarks>
    /// **The engine's own tests decide every branch**: an <c>if</c> takes its branch when <see cref="LogicVec.Truth"/> is 1, as
    /// <see cref="Simulator"/> jumps, so an x or a z goes the else way here as it does there; a <c>case</c> label matches by
    /// <see cref="Matching.CaseMatches"/>. A write is spliced into its signal and held as the signal holds it, as the engine
    /// stores one. So the circuit a process opens into computes what the process does, path for path.
    ///
    /// **What is not hardware stays a box**, with why: an <c>initial</c>, a delay or a wait inside the body, a write into a
    /// memory at a place worked out as it runs, a loop that runs past <see cref="MostSteps"/> steps, a signal written both
    /// blocking and nonblocking, <c>$time</c>; and a process on levels that reads what it does not wait on, or reads what it
    /// writes before writing it, either of which leaves it seeing an old value no circuit holds. A <c>$display</c> is left out,
    /// as a synthesis tool leaves it out: it prints, and builds nothing.
    /// </remarks>
    internal sealed partial class RtlBuilder
    {
        /// <summary>How many instructions opening one process may run, every path counted: a loop past it is no fixed circuit to draw.</summary>
        internal const int MostSteps = 200_000;

        /// <summary>How many paths may be open at once.</summary>
        internal const int MostPaths = 1024;

        // The steps run opening one process or one call in an assignment, every path counted. While a process is opened: every
        // signal its cells read from outside it, which its wait must cover; those read in an expression rather than kept as
        // they were, which it must not also write; the memories it reads; and the bits of each signal it writes.
        private int _steps;
        private HashSet<int> _readHere;
        private HashSet<int> _readInExpr;
        private HashSet<MemoryInfo> _memoriesRead;
        private Dictionary<int, (int Lowest, int Highest)> _writtenBits;
        private bool _keeping;

        // The values made on the path being followed, by expression, until it writes something blocking: a case's subject is
        // one cell however many labels it is matched against (the review of #106: case (a + b) was three adders).
        private Dictionary<Expr, int> _pathMade;

        /// <summary>A process found to be no hardware, part-way through: unwinds the run to its box.</summary>
        private sealed class NotHardware : Exception
        {
            public NotHardware(string why) : base(why) { }
        }

        /// <summary>One path through a process: where it is, the splits it went through and which way, and every signal's value on it.</summary>
        private sealed class Path
        {
            public int Pc;
            public List<(int Condition, bool Taken)> Splits = new List<(int, bool)>();

            /// <summary>Each signal written so far, blocking, by its value's net: what a read on this path sees.</summary>
            public Dictionary<int, int> Now = new Dictionary<int, int>();

            /// <summary>Each signal written so far, nonblocking, by the value it will take: what no read on this path sees yet.</summary>
            public Dictionary<int, int> Later = new Dictionary<int, int>();

            /// <summary>What expressions have made on it since it last wrote anything blocking.</summary>
            public Dictionary<Expr, int> Made = new Dictionary<Expr, int>();

            public Path Fork(int pc, int condition, bool taken)
            {
                var p = new Path
                {
                    Pc = pc,
                    Splits = new List<(int, bool)>(Splits) { (condition, taken) },
                    Now = new Dictionary<int, int>(Now),
                    Later = new Dictionary<int, int>(Later),
                    Made = new Dictionary<Expr, int>(Made),
                };
                return p;
            }
        }

        /// <summary>
        /// A process opened: what it waits on when that is edges, every signal it read from outside, and for each signal it
        /// writes, the value it gives it and the bits it writes -- what <see cref="DriveOpened"/> makes its registers or joins of.
        /// </summary>
        private sealed class Opened
        {
            public ProcessInfo Process;
            public EventItem[] Edges;
            public HashSet<int> Reads;
            public readonly List<(int Signal, int Value, int Lowest, int Width)> Writes = new List<(int, int, int, int)>();
        }

        /// <summary>
        /// <paramref name="process"/> opened into cells, the values it gives what it writes; null, and why, for one that is no
        /// hardware -- a box is drawn for that instead.
        /// </summary>
        private Opened OpenProcess(ProcessInfo process, out string why)
        {
            Instruction[] code = process.Code;
            int n = code.Length;
            why = null;

            // The shapes the elaborator compiles a process to: an always_comb's body, its wait, and the jump back; an always's
            // wait, its body, and the jump back.
            if (process.Kind == "initial")
            {
                why = "An initial block runs once, as the simulation starts: behaviour, not a circuit.";
                return null;
            }
            foreach (Instruction ins in code)
                if (ins.Op == OpCode.Delay)
                {
                    why = "A delay, #d, makes it a clock or a timer for a testbench: timing, not a circuit.";
                    return null;
                }
            if (n < 2 || code[n - 1].Op != OpCode.Jump || code[n - 1].Jump != 0)
            {
                why = "It does not run over and over from one wait, as a circuit does.";
                return null;
            }

            int start, end;
            EventItem[] edges = Array.Empty<EventItem>();
            EventItem[] waited;
            if ((process.Kind == "always_comb" || process.Kind == "always_latch") && code[n - 2].Op == OpCode.WaitEvent)
            {
                start = 0;
                end = n - 2;
                waited = code[n - 2].Wait?.Items ?? Array.Empty<EventItem>();
            }
            else if (code[0].Op == OpCode.WaitEvent)
            {
                start = 1;
                end = n - 1;
                EventItem[] items = code[0].Wait?.Items ?? Array.Empty<EventItem>();
                waited = items;
                int edged = 0;
                foreach (EventItem item in items)
                    if (item.Edge != EdgeKind.Any)
                        edged++;
                if (edged > 0 && edged < items.Length)
                {
                    why = "It waits on an edge and a level at once, which no flip-flop does.";
                    return null;
                }
                if (edged > 0)
                    edges = items;
            }
            else
            {
                why = "It does not start with what it waits for.";
                return null;
            }

            for (int i = start; i < end; i++)
            {
                switch (code[i].Op)
                {
                    case OpCode.Assign:
                    case OpCode.Jump:
                    case OpCode.JumpUnless:
                    case OpCode.JumpIf:
                    case OpCode.JumpIfMatch:
                        break;
                    case OpCode.Print:
                        _unshown.Add("the $display in " + Relative(process.Name));
                        break;
                    case OpCode.AssignLater:
                        if (code[i].Other != null)
                        {
                            why = "A nonblocking write after a delay, <= #d, is timing, not a circuit.";
                            return null;
                        }
                        break;
                    case OpCode.WaitEvent:
                    case OpCode.WaitUntil:
                        why = "It waits inside its body, which no circuit does.";
                        return null;
                    default:
                        why = "It does something a circuit does not: " + code[i].Op + ".";
                        return null;
                }
            }

            Dictionary<int, int> now, later;
            var reads = new HashSet<int>();
            var inExpressions = new HashSet<int>();
            var memories = new HashSet<MemoryInfo>();
            var bits = new Dictionary<int, (int Lowest, int Highest)>();
            _steps = 0;
            _readHere = reads;
            _readInExpr = inExpressions;
            _memoriesRead = memories;
            _writtenBits = bits;
            try
            {
                Run(code, start, end, out now, out later, null, new Path { Pc = start });
            }
            catch (NotHardware e)
            {
                why = e.Message;
                return null;
            }
            finally
            {
                _readHere = null;
                _readInExpr = null;
                _memoriesRead = null;
                _writtenBits = null;
            }

            // What it writes: an always_comb's outputs, an always_ff's registers.
            var writes = new SortedSet<int>(process.Writes);
            foreach (int signal in writes)
            {
                SignalInfo info = _design.Signals[signal];
                if (info.Local)
                    continue;
                if (now.ContainsKey(signal) && later.ContainsKey(signal))
                {
                    why = info.Name + " is written both blocking and nonblocking.";
                    return null;
                }
            }

            if (edges.Length == 0 && !Follows(waited, writes, reads, inExpressions, memories, out why))
                return null;

            var opened = new Opened { Process = process, Edges = edges, Reads = reads };
            foreach (int signal in writes)
            {
                SignalInfo info = _design.Signals[signal];
                if (info.Local)
                    continue;
                int value = later.TryGetValue(signal, out int l) ? l : now.TryGetValue(signal, out int c) ? c : -1;
                if (value < 0 || !bits.TryGetValue(signal, out (int Lowest, int Highest) written))
                    continue;
                opened.Writes.Add((signal, value, written.Lowest, written.Highest - written.Lowest + 1));
            }
            return opened;
        }

        /// <summary>
        /// Whether a process on levels computes what a circuit would: it waits on everything it reads -- else it is left as it
        /// was when only that changes, an <c>@(a)</c> reading b, an <c>@*</c> calling a function that reads one -- and it reads
        /// nothing it writes before writing it, which would see what its last run left. False, and why, otherwise.
        /// </summary>
        private bool Follows(EventItem[] waited, SortedSet<int> writes, HashSet<int> reads, HashSet<int> inExpressions,
            HashSet<MemoryInfo> memories, out string why)
        {
            why = null;
            var covered = new HashSet<int>();
            foreach (EventItem item in waited)
                if (item.Value.TryRead(out SignalId s))
                    covered.Add(s.Index);

            foreach (int signal in new SortedSet<int>(reads))
            {
                SignalInfo info = _design.Signals[signal];
                if (info.Local || writes.Contains(signal) || covered.Contains(signal))
                    continue;
                why = Stale(NameOf(signal));
                return false;
            }

            foreach (MemoryInfo memory in memories)
            {
                bool waitsOnIt = false, writesIt = false;
                for (int word = 0; word < memory.Depth; word++)
                {
                    waitsOnIt |= covered.Contains(memory.First.Index + word);
                    writesIt |= writes.Contains(memory.First.Index + word);
                }
                if (writesIt)
                {
                    why = Early(ShortMemoryName(memory));
                    return false;
                }
                if (!waitsOnIt)
                {
                    why = Stale(ShortMemoryName(memory));
                    return false;
                }
            }

            foreach (int signal in new SortedSet<int>(inExpressions))
            {
                if (!writes.Contains(signal))
                    continue;
                why = Early(NameOf(signal));
                return false;
            }
            return true;

            static string Stale(string name) =>
                "It reads " + name + " but does not wait on it, so the simulation leaves it as it was when only " + name +
                " changes, which no circuit does. always_comb waits on everything it reads, the functions it calls included.";

            static string Early(string name) =>
                "It reads " + name + " before it writes it, so it sees what its last run left there, which no circuit holds.";
        }

        private string ShortMemoryName(MemoryInfo memory) =>
            memory.Name.StartsWith(_prefix, StringComparison.Ordinal) ? memory.Name.Substring(_prefix.Length) : memory.Name;

        /// <summary>
        /// What the opened processes drive. A signal one process writes whole is one register, or one join; where several each
        /// write some of its bits -- a generate loop's <c>q[i] &lt;= d[i]</c>, two <c>always_comb</c> on <c>y[0]</c> and
        /// <c>y[1]</c> -- each is its own register or value on those bits, joined into the signal by their places as continuous
        /// drivers are (the review of #106: all but the first were dropped).
        /// </summary>
        private void DriveOpened(List<Opened> opened)
        {
            var pieces = new SortedDictionary<int, List<(Opened Process, int Value, int Lowest, int Width)>>();
            foreach (Opened o in opened)
                foreach ((int signal, int value, int lowest, int width) in o.Writes)
                {
                    if (!pieces.TryGetValue(signal, out List<(Opened, int, int, int)> list))
                        pieces[signal] = list = new List<(Opened, int, int, int)>();
                    list.Add((o, value, lowest, width));
                }

            foreach (KeyValuePair<int, List<(Opened Process, int Value, int Lowest, int Width)>> entry in pieces)
            {
                int signal = entry.Key;
                SignalInfo info = _design.Signals[signal];
                List<(Opened Process, int Value, int Lowest, int Width)> list = entry.Value;
                int net = Read(signal, info.Width);
                bool taken = _nets[net].Driver >= 0;
                if (!taken && list.Count == 1 && list[0].Lowest == 0 && list[0].Width == info.Width)
                {
                    Drives(list[0].Process, signal, list[0].Value, 0, info.Width, net);
                    continue;
                }

                var inputs = new int[list.Count];
                var lowests = new int[list.Count];
                var widths = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    (Opened process, int value, int lowest, int width) = list[i];
                    int part = lowest == 0 && width == _nets[value].Width ? value : Slice(value, lowest, width);
                    inputs[i] = Drives(process, signal, part, lowest, width, -1);
                    lowests[i] = lowest;
                    widths[i] = width;
                }

                int target = taken ? NewNet(info.Width, NameOf(signal) + " (written here)", -1) : net;
                RtlCell join = NewCellDriving(RtlKind.Join, NameOf(signal), inputs, target);
                join.Signal = signal;
                join.Lowests = lowests;
                join.PieceWidths = widths;
                join.Signed = info.Signed;
                join.TwoStateTarget = info.TwoState;
            }
        }

        /// <summary>
        /// A process's value for bits <paramref name="lowest"/> up of a signal, <paramref name="width"/> of them: a register's
        /// output for one on edges, else the value itself -- into <paramref name="net"/> when it is given, through a join or as
        /// the register's own output.
        /// </summary>
        private int Drives(Opened o, int signal, int value, int lowest, int width, int net)
        {
            SignalInfo info = _design.Signals[signal];
            if (o.Edges.Length == 0)
            {
                if (net < 0)
                    return value;
                RtlCell join = NewCellDriving(RtlKind.Join, NameOf(signal), new[] { value }, net);
                join.Signal = signal;
                join.Lowests = new[] { 0 };
                join.PieceWidths = new[] { info.Width };
                join.Signed = info.Signed;
                join.TwoStateTarget = info.TwoState;
                return net;
            }

            string name = width == info.Width ? NameOf(signal)
                : width == 1 ? NameOf(signal) + "[" + lowest + "]"
                : NameOf(signal) + "[" + (lowest + width - 1) + ":" + lowest + "]";
            int output = net >= 0 ? net : NewNet(width, name, -1);
            var inputs = new List<int> { value };
            var names = new List<string> { "D" };
            var kinds = new List<EdgeKind>();
            foreach (EventItem item in o.Edges)
            {
                inputs.Add(item.Value.ToRtl(this));
                names.Add(item.Value.TryRead(out SignalId clock) ? NameOf(clock.Index) : "clock");
                kinds.Add(item.Edge);
            }
            RtlCell register = NewCellDriving(RtlKind.Register, name, inputs.ToArray(), output);
            register.Signal = signal;
            register.Lowest = lowest;
            register.Width = width;
            register.InputNames = names.ToArray();
            register.Edges = kinds.ToArray();

            // The clock is the edge the body never tests; an asynchronous reset is one it does, if (!rst_n).
            int untested = 0;
            for (int e = 0; e < o.Edges.Length; e++)
                if (!(o.Edges[e].Value.TryRead(out SignalId edge) && o.Reads.Contains(edge.Index)))
                {
                    untested++;
                    register.Clock = e + 1;
                }
            if (untested != 1)
                register.Clock = -1;
            return output;
        }

        /// <summary>
        /// Runs the body from <paramref name="first"/>'s place to <paramref name="end"/>, every path, and joins them where they
        /// end -- a process's, or, given <paramref name="function"/>, a function's, which writes only its own signals and ends
        /// on anything else.
        /// </summary>
        private void Run(Instruction[] code, int start, int end, out Dictionary<int, int> now, out Dictionary<int, int> later,
            FunctionInfo function, Path first)
        {
            first.Pc = start;
            var paths = new List<Path> { first };
            while (true)
            {
                if (++_steps > MostSteps)
                    throw new NotHardware("Its loops run too long to draw: more than " + MostSteps.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " steps.");
                if (paths.Count > MostPaths)
                    throw new NotHardware("It has too many paths through it to draw.");

                // The path furthest behind goes first, so paths meet where they join again; two at one place are joined.
                int pc = int.MaxValue;
                foreach (Path p in paths)
                    pc = Math.Min(pc, Math.Min(p.Pc, end));
                List<Path> here = paths.FindAll(p => Math.Min(p.Pc, end) == pc);
                if (here.Count > 1)
                {
                    foreach (Path p in here)
                        paths.Remove(p);
                    paths.Add(JoinAll(here));
                    continue;
                }

                Path path = here[0];
                if (pc >= end)
                {
                    now = path.Now;
                    later = path.Later;
                    return;
                }

                Instruction ins = code[pc];
                if (function != null && ins.Op != OpCode.Assign && ins.Op != OpCode.Jump && ins.Op != OpCode.JumpUnless
                    && ins.Op != OpCode.JumpIf && ins.Op != OpCode.JumpIfMatch)
                {
                    path.Pc = end;   // the end of the call, as the engine ends one on anything else
                    continue;
                }

                switch (ins.Op)
                {
                    case OpCode.Assign:
                        Store(path, path.Now, ins.Target, ins.TargetWidth, Value(ins.Value, path), function);
                        path.Pc++;
                        break;
                    case OpCode.AssignLater:
                        Store(path, path.Later, ins.Target, ins.TargetWidth, Value(ins.Value, path), null);
                        path.Pc++;
                        break;
                    case OpCode.Print:
                        path.Pc++;
                        break;
                    case OpCode.Jump:
                        path.Pc = ins.Jump;
                        break;
                    case OpCode.JumpUnless:
                        Branch(paths, path, Truth(Value(ins.Value, path)), whenTrue: pc + 1, whenFalse: ins.Jump);
                        break;
                    case OpCode.JumpIf:
                        Branch(paths, path, Truth(Value(ins.Value, path)), whenTrue: ins.Jump, whenFalse: pc + 1);
                        break;
                    case OpCode.JumpIfMatch:
                    {
                        int subject = Value(ins.Value, path);
                        Branch(paths, path, Match(subject, Value(ins.Other, path), ins.Case), whenTrue: ins.Jump, whenFalse: pc + 1);
                        break;
                    }
                    default:
                        throw new NotHardware("It does something a circuit does not: " + ins.Op + ".");
                }
            }
        }

        /// <summary>A path at a branch: on one way when its condition is a constant, else parted into two, one each way.</summary>
        private void Branch(List<Path> paths, Path path, int condition, int whenTrue, int whenFalse)
        {
            if (TryConstant(condition, out LogicVec known))
            {
                path.Pc = known[0] == Logic4.One ? whenTrue : whenFalse;
                return;
            }
            paths.Remove(path);
            paths.Add(path.Fork(whenTrue, condition, true));
            paths.Add(path.Fork(whenFalse, condition, false));
        }

        /// <summary>An expression's net on a path: a signal written on it read as written, a value made on it already used again.</summary>
        private int Value(Expr e, Path path)
        {
            Dictionary<int, int> saved = _overlay;
            Dictionary<Expr, int> savedMade = _pathMade;
            _overlay = path.Now;
            _pathMade = path.Made;
            try
            {
                return Of(e);
            }
            finally
            {
                _overlay = saved;
                _pathMade = savedMade;
            }
        }

        /// <summary>
        /// A write's value spliced into each signal it names, as the engine stores one: most significant piece first, each piece
        /// clipped to its signal, and a place worked out as it runs taken from a constant -- a loop's counter, unrolled. A
        /// function's write lands only in its own signals, as the engine's does.
        /// </summary>
        private void Store(Path path, Dictionary<int, int> into, LPiece[] target, int total, int value, FunctionInfo function)
        {
            if (_nets[value].Width != total)
                value = Resize(value, total, false);
            if (into == path.Now)
                path.Made.Clear();   // what it read until now may read otherwise after

            int offset = total;
            foreach (LPiece piece in target)
            {
                offset -= piece.Width;
                if (function != null && (function.InfoOf(piece.Signal) == null || piece.Word != null))
                    continue;
                if (piece.Word != null)
                    throw new NotHardware("It writes into a memory at a place worked out as it runs, which is drawn as a box.");

                int lowest = piece.Lowest;
                int width = _design.Signals[piece.Signal].Width;
                if (piece.Offset != null)
                {
                    int place = Value(piece.Offset, path);
                    if (!TryConstant(place, out LogicVec known))
                    {
                        // A place worked out as it runs: a demultiplexer, putting the part where the place says.
                        int written = target.Length == 1 ? value : Slice(value, offset, piece.Width);
                        into[piece.Signal] = Put(Current(into, piece.Signal), written, place, piece.Signal);
                        Wrote(piece.Signal, 0, width - 1, function);
                        continue;
                    }
                    if (!OffsetOf(known, out lowest))
                        continue;   // an unknown place: the engine writes nothing
                }

                int partWidth = piece.Width;
                long top = (long)lowest + partWidth;
                if (lowest >= width || top <= 0)
                    continue;
                int part = target.Length == 1 ? value : Slice(value, offset, piece.Width);
                if (lowest < 0)
                {
                    part = Slice(part, -lowest, (int)top);
                    partWidth = (int)top;
                    lowest = 0;
                }
                if (lowest + partWidth > width)
                {
                    part = Slice(part, 0, width - lowest);
                    partWidth = width - lowest;
                }

                bool whole = lowest == 0 && partWidth == width;
                into[piece.Signal] = Splice(whole ? -1 : Current(into, piece.Signal), part, lowest, piece.Signal);
                Wrote(piece.Signal, lowest, lowest + partWidth - 1, function);
            }
            if (into == path.Now)
                path.Made.Clear();
        }

        /// <summary>Notes that the process being opened writes bits <paramref name="lowest"/> to <paramref name="highest"/> of a signal.</summary>
        private void Wrote(int signal, int lowest, int highest, FunctionInfo function)
        {
            if (function != null || _writtenBits == null)
                return;
            _writtenBits[signal] = _writtenBits.TryGetValue(signal, out (int Lowest, int Highest) before)
                ? (Math.Min(before.Lowest, lowest), Math.Max(before.Highest, highest))
                : (lowest, highest);
        }

        /// <summary>A signal's value on a path: what the path has written into it so far, or the signal as it was, kept.</summary>
        private int Current(Dictionary<int, int> into, int signal) =>
            into.TryGetValue(signal, out int written) ? written : Kept(signal);

        /// <summary>
        /// A signal as it was, where a path leaves it so: its bits a write does not touch, or its value where one way writes it
        /// and the other does not. A latch keeps its value so; it is no read in an expression.
        /// </summary>
        private int Kept(int signal)
        {
            bool saved = _keeping;
            _keeping = true;
            try
            {
                return Read(signal, _design.Signals[signal].Width);
            }
            finally
            {
                _keeping = saved;
            }
        }

        /// <summary>Where a value puts a write, as <c>Simulator.TryOffset</c> has it; false when it is unknown or too large.</summary>
        internal static bool OffsetOf(LogicVec value, out int lowest)
        {
            lowest = 0;
            if (!value.IsKnown)
                return false;
            if (value.IsSigned)
            {
                if (!value.TryGetInt64(out long signedValue) || signedValue < int.MinValue || signedValue > int.MaxValue)
                    return false;
                lowest = (int)signedValue;
                return true;
            }
            if (!value.TryGetUInt64(out ulong unsigned) || unsigned > int.MaxValue)
                return false;
            lowest = (int)unsigned;
            return true;
        }

        /// <summary>
        /// Paths that have reached one place, joined into one, two at a time: the two that parted last first, each pair by a mux
        /// on the condition that parted them -- so an <c>if</c> is one mux a signal, and a <c>case</c> a chain of them. In the
        /// order of their splits, the two that went through the most alike are always neighbours, so each join looks along the
        /// list once rather than at every pair (the review of #106: a case of 1,000 labels took 55 seconds).
        /// </summary>
        private Path JoinAll(List<Path> here)
        {
            var open = new List<Path>(here);
            open.Sort(BySplits);
            while (open.Count > 1)
            {
                int best = 0, bestShared = -1;
                for (int i = 0; i + 1 < open.Count; i++)
                {
                    int shared = Shared(open[i], open[i + 1]);
                    if (shared > bestShared)
                    {
                        bestShared = shared;
                        best = i;
                    }
                }

                Path joined = Join(open[best], open[best + 1], bestShared);
                open.RemoveAt(best + 1);
                open[best] = joined;
            }
            return open[0];
        }

        /// <summary>Paths in the order of their splits: condition by condition, the way not taken first.</summary>
        private static int BySplits(Path a, Path b)
        {
            for (int i = 0; i < a.Splits.Count && i < b.Splits.Count; i++)
            {
                int c = a.Splits[i].Condition.CompareTo(b.Splits[i].Condition);
                if (c != 0)
                    return c;
                c = a.Splits[i].Taken.CompareTo(b.Splits[i].Taken);
                if (c != 0)
                    return c;
            }
            return a.Splits.Count.CompareTo(b.Splits.Count);
        }

        /// <summary>How many splits two paths went through alike, from the start.</summary>
        private static int Shared(Path a, Path b)
        {
            int n = 0;
            while (n < a.Splits.Count && n < b.Splits.Count && a.Splits[n] == b.Splits[n])
                n++;
            return n;
        }

        /// <summary>
        /// Two paths joined. Two that parted at one split, one each way, are a mux on its condition, and together are all their
        /// shared splits hold. Any other two are a mux on whichever went through splits of its own past the shared ones -- no
        /// run takes both paths, so where its splits hold the other's do not -- and the joined path keeps that it is the one or
        /// the other, not everything the shared splits hold: a third path under them, ahead after a break or a return, is
        /// joined to it later as to anything else (the review of #106: kept as only the shared splits, a case's label list
        /// chose the wrong arm, and a path with none of its own threw the other away).
        /// </summary>
        private Path Join(Path a, Path b, int shared)
        {
            var joined = new Path { Pc = a.Pc, Splits = a.Splits.GetRange(0, shared) };
            if (a.Splits.Count == shared + 1 && b.Splits.Count == shared + 1 && a.Splits[shared].Condition == b.Splits[shared].Condition)
            {
                (int condition, bool bTaken) = b.Splits[shared];
                joined.Now = JoinValues(a.Now, b.Now, condition, bTaken);
                joined.Later = JoinValues(a.Later, b.Later, condition, bTaken);
                return joined;
            }

            int tailA = Tail(a, shared), tailB = Tail(b, shared);
            if (tailA < 0 && tailB < 0)
                return b;
            Path otherwise = a, chosen = b;
            int select = tailB;
            if (tailB < 0)
            {
                otherwise = b;
                chosen = a;
                select = tailA;
            }
            if (tailA >= 0 && tailB >= 0)
                joined.Splits.Add((Binary(BinaryOp.LogicalOr, tailA, tailB, 1), true));
            joined.Now = JoinValues(otherwise.Now, chosen.Now, select, true);
            joined.Later = JoinValues(otherwise.Later, chosen.Later, select, true);
            return joined;
        }

        /// <summary>Whether a path's own splits past the first <paramref name="shared"/> all hold, as one bit; -1 where it has none.</summary>
        private int Tail(Path path, int shared)
        {
            int condition = -1;
            for (int i = shared; i < path.Splits.Count; i++)
            {
                (int c, bool taken) = path.Splits[i];
                int holds = taken ? c : Unary(UnaryOp.LogicalNot, c, 1);
                condition = condition < 0 ? holds : Binary(BinaryOp.LogicalAnd, condition, holds, 1);
            }
            return condition;
        }

        private Dictionary<int, int> JoinValues(Dictionary<int, int> a, Dictionary<int, int> b, int condition, bool bTaken)
        {
            var joined = new Dictionary<int, int>();
            var signals = new SortedSet<int>(a.Keys);
            signals.UnionWith(b.Keys);
            foreach (int signal in signals)
            {
                int width = _design.Signals[signal].Width;
                if (_unbound != null && _unbound.Contains(signal) && !(a.ContainsKey(signal) && b.ContainsKey(signal)))
                    continue;   // what one way wrote and the other left: read after, it is the last call's
                int va = a.TryGetValue(signal, out int x) ? x : Kept(signal);
                int vb = b.TryGetValue(signal, out int y) ? y : Kept(signal);
                joined[signal] = va == vb ? va : bTaken ? Mux(condition, vb, va, width) : Mux(condition, va, vb, width);
            }
            return joined;
        }

        // -----------------------------------------------------------------
        // The cells a process adds
        // -----------------------------------------------------------------

        /// <summary>Whether a value is true as an <c>if</c> takes it: 1 when <see cref="LogicVec.Truth"/> is, else 0 -- x and z included.</summary>
        public int Truth(int value)
        {
            if (TryConstant(value, out LogicVec v))
                return Constant(LogicVec.FromUInt64(LogicVec.Truth(v) == Logic4.One ? 1UL : 0UL, 1));
            RtlKind? already = DriverOf(value)?.Kind;
            if (already == RtlKind.Truth || already == RtlKind.Match)
                return value;   // a 1 or a 0 already
            return NewCell(RtlKind.Truth, "", new[] { value }, 1).Output;
        }

        /// <summary>Whether a <c>case</c>'s subject matches a label, 1 or 0, as <see cref="Matching.CaseMatches"/> has it.</summary>
        public int Match(int subject, int label, CaseKind kind)
        {
            if (TryConstant(subject, out LogicVec s) && TryConstant(label, out LogicVec l))
                return Constant(LogicVec.FromUInt64(Matching.CaseMatches(s, l, kind) ? 1UL : 0UL, 1));
            RtlCell cell = NewCell(RtlKind.Match, "", new[] { subject, label }, 1);
            cell.Case = kind;
            return cell.Output;
        }

        /// <summary><paramref name="part"/> put into <paramref name="old"/> where <paramref name="place"/> says, as the engine writes <c>y[i] = v</c>.</summary>
        public int Put(int old, int part, int place, int signal)
        {
            SignalInfo info = _design.Signals[signal];
            RtlCell cell = NewCell(RtlKind.Put, NameOf(signal), new[] { old, part, place }, info.Width);
            cell.Signal = signal;
            cell.Signed = info.Signed;
            cell.TwoStateTarget = info.TwoState;
            return cell.Output;
        }

        /// <summary>
        /// <paramref name="part"/> put into <paramref name="old"/> from bit <paramref name="lowest"/>, held as
        /// <paramref name="signal"/> holds it, as <c>Simulator.Splice</c> and <c>Shape</c> store a write: the old value's bits
        /// either side of it and the part between, joined -- so a signal written a bit at a time, a loop's <c>y[i]</c> or a
        /// ripple's <c>c[i + 1]</c>, is the bits each write gave it, and a read of one bit is the net that bit was given.
        /// </summary>
        public int Splice(int old, int part, int lowest, int signal)
        {
            SignalInfo info = _design.Signals[signal];
            int width = info.Width, partWidth = _nets[part].Width;
            int value = part;
            if (lowest > 0)
                value = Concat(value, Slice(old, 0, lowest), lowest + partWidth);
            if (lowest + partWidth < width)
                value = Concat(Slice(old, lowest + partWidth, width - lowest - partWidth), value, width);
            int typed = Signedness(value, info.Signed, width);
            return info.TwoState ? TwoState(typed, width) : typed;
        }
    }
}
