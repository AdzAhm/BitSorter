using System;
using System.Collections.Generic;

namespace BitSorter.Sim
{
    /// <summary>
    /// How many registers a design's paths from its inputs to its outputs cross (Part 15i-1, asked 2026-11-02 beside the
    /// course read): what its latency is in clock periods. A design whose every such path crosses the same number K is a
    /// K-stage pipeline -- the course's K-pipe -- and a result leaves K periods after its inputs came in, one a period after
    /// that. Anything else is said for what it is, and never claimed to be a pipeline: a loop on such a path, as a state
    /// machine's; a register loaded only when another register says so, as a multicycle machine's; registers taking
    /// different clocks or edges; a register written at a place found as it runs, which is not followed; paths of
    /// different depths.
    /// </summary>
    /// <remarks>
    /// **Bit by bit, as the timing graph is.** Each register's process is followed along the ways its code runs: what each
    /// bit it writes takes its next value from, through any blocking temporary as it stands at that point -- what every way
    /// there wrote it, where ways meet (the 16d sweep: followed in the order written, an if's and its else's both joined the
    /// temporary's last value, and a 1-stage design read as 1 to 2) -- so a shift
    /// register is a chain of stages and an accumulator kept in a blocking variable a loop. A bit written only on some ways
    /// through keeps its value on the others, which is no loop, and so does one given its own value back (<c>q &lt;= q</c>):
    /// an enable's register is a stage. So is one kept through <c>en ? d : q</c> -- a ?: arm that is the register whole is
    /// its hold (<see cref="Expr.DependsHolding"/>; it read as a loop until 2026-11-02), its condition a decision as an if's
    /// is -- where <c>en ? d : q + 1</c> goes round.
    ///
    /// **A condition is no stage, and is still a way.** An <c>if</c>'s or a <c>case</c>'s condition decides what is
    /// written: counted towards a loop and towards reaching an output -- a state machine written in one process is a
    /// loop -- but never as a register crossed, so a pipeline's enable or reset leaves it a pipeline. An output no input's
    /// value reaches is counted along its decisions instead, each crossing the register it decides: a registered comparator
    /// writing 1 or 0 is one stage (the 16d sweep). A condition worked out from a register -- read itself, or through
    /// logic between, as <c>assign tick = (cnt == 3)</c> decodes a count (the 16d sweep) -- makes its process's writes
    /// <see cref="Sequenced"/>.
    ///
    /// A clock -- what an edge waits on -- starts no path and is no condition. The first review of this found each of
    /// these wrong (2026-11-02).
    /// </remarks>
    public sealed class PipelineDepth
    {
        private PipelineDepth(bool hasOutputs, bool reaches, int fewest, int most, bool loop, bool sequenced, bool mixedClocks,
            bool unfollowed)
        {
            HasOutputs = hasOutputs;
            Reaches = reaches;
            Fewest = fewest;
            Most = most;
            Loop = loop;
            Sequenced = sequenced;
            MixedClocks = mixedClocks;
            Unfollowed = unfollowed;
        }

        /// <summary>Whether the design has an output at all.</summary>
        public bool HasOutputs { get; }

        /// <summary>Whether any input reaches any output, by its value or by deciding what is written.</summary>
        public bool Reaches { get; }

        /// <summary>
        /// The fewest registers a path from an input's value to an output crosses -- or, to an output no input's value
        /// reaches, a way of the decisions that write it, each crossing the register it decides.
        /// </summary>
        public int Fewest { get; }

        /// <summary>The most, counted as <see cref="Fewest"/> is.</summary>
        public int Most { get; }

        /// <summary>Whether a way from an input to an output runs round a loop -- a state machine's, or logic fed back.</summary>
        public bool Loop { get; }

        /// <summary>Whether a register on such a way is loaded only when another register's value says so: a multicycle machine.</summary>
        public bool Sequenced { get; }

        /// <summary>Whether the registers on such ways take different clocks, or different edges of one.</summary>
        public bool MixedClocks { get; }

        /// <summary>Whether a register on such a way is written at a place found as it runs -- <c>r[i] &lt;= ...</c> -- which is not followed.</summary>
        public bool Unfollowed { get; }

        /// <summary>K for a K-stage pipeline: every path from an input to an output crossing K registers; null for none.</summary>
        public int? Stages => HasOutputs && Reaches && !Unfollowed && !Loop && !Sequenced && !MixedClocks && Fewest == Most
            ? Fewest : (int?)null;

        /// <summary>Past this many arcs into one write's bits, they meet at a junction rather than pairing every bit.</summary>
        private const long MostArcsApart = 1 << 16;

        public static PipelineDepth Of(TimingGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            Design design = graph.Design;
            var first = new int[design.Signals.Count + 1];
            for (int s = 0; s < design.Signals.Count; s++)
                first[s + 1] = first[s] + design.Signals[s].Width;

            var build = new Builder(graph.NodeCount);
            foreach (TimingArc arc in graph.Arcs)
                build.Data(arc.From, arc.To, 0);

            // What every register writes, for whether a condition is worked out from one; and every clock, first, for none is
            // a condition.
            var registerBits = new HashSet<int>();
            var clocks = new HashSet<int>();
            foreach (TimingRegister register in graph.Registers)
            {
                foreach (int q in register.Launches)
                    registerBits.Add(q);
                if (register.Process != null)
                    foreach (EventItem item in Edges(register.Process))
                        item.Value?.CollectReads(clocks);
            }

            var follows = new Dictionary<TimingRegister, Followed>();
            foreach (TimingRegister register in graph.Registers)
            {
                if (register.Process == null)
                {
                    // Nothing to follow: what it captures, through one register, to what it launches.
                    int junction = build.Junction();
                    foreach (int d in register.Captures)
                        build.Data(d, junction, 0);
                    foreach (int q in register.Launches)
                        build.Data(junction, q, 1);
                    continue;
                }
                follows[register] = Follow(register.Process, design, first, build, clocks);
            }

            // A clock starts no path: its edge is when a stage is taken, not a value passed on.
            var inputs = new List<int>();
            foreach (int input in graph.Inputs)
                if (!IsClock(input, clocks, first))
                    inputs.Add(input);

            int n = build.Count;
            List<(int To, int Weight)>[] all = build.Adjacency(n, data: false), data = build.Adjacency(n, data: true);

            // Every way, conditions included: a loop on one, and the registers on them.
            bool[] on = Between(n, inputs, graph.Outputs, all);
            bool loop = !InOrder(n, on, all, out List<int> wayOrder);

            // A condition worked out from a register, through any logic between -- assign tick = (cnt == 3) -- says when its
            // writes are loaded (the 16d sweep: only a condition that was a register's bit itself was asked).
            bool[] fromRegister = Reach(n, new List<int>(registerBits), v => ThroughLogic(data[v]));

            bool sequenced = false, mixed = false, unfollowed = false;
            (int, EdgeKind)? seen = null;
            foreach (KeyValuePair<TimingRegister, Followed> each in follows)
            {
                if (!Touches(each.Key, on))
                    continue;
                foreach (int d in each.Value.Decisions)
                    sequenced |= d >= 0 && d < n && fromRegister[d];
                unfollowed |= each.Value.Unfollowed;
                if (seen == null)
                    seen = each.Value.Clock;
                else if (!seen.Value.Equals(each.Value.Clock))
                    mixed = true;
            }

            // The registers counted along values alone, where an input's value reaches an output: an enable or a reset deciding
            // a stage is no stage of its own.
            bool[] onData = Between(n, inputs, graph.Outputs, data);
            bool any = false;
            int least = int.MaxValue, greatest = 0;
            bool[] byValue = null;
            if (InOrder(n, onData, data, out List<int> order))
            {
                byValue = Count(n, inputs, onData, data, order, out int[] fewest, out int[] most);
                foreach (int output in graph.Outputs)
                {
                    if (output < 0 || output >= n || !byValue[output] || !onData[output])
                        continue;
                    any = true;
                    least = Math.Min(least, fewest[output]);
                    greatest = Math.Max(greatest, most[output]);
                }
            }
            else
            {
                loop = true;   // a loop of values alone is on a way of every kind too
            }

            // An output no input's value reaches, but one decides -- a registered comparator writing 1 or 0, a decoder writing
            // constants -- is counted along every way, a decision crossing the register it decides (the 16d sweep: counted
            // by values alone it was none, and said to be within the period, beside the flip-flop it is).
            if (!loop && byValue != null)
            {
                bool[] byWay = Count(n, inputs, on, all, wayOrder, out int[] fewest, out int[] most);
                foreach (int output in graph.Outputs)
                {
                    if (output < 0 || output >= n || !on[output] || !byWay[output] || (byValue[output] && onData[output]))
                        continue;
                    any = true;
                    least = Math.Min(least, fewest[output]);
                    greatest = Math.Max(greatest, most[output]);
                }
            }
            if (!any)
                least = 0;

            // Reaching an output by deciding alone -- a Moore machine's input -- is reaching it too.
            bool reaches = any || Array.IndexOf(on, true) >= 0;
            return new PipelineDepth(graph.Outputs.Count > 0, reaches, least, greatest, loop, sequenced, mixed, unfollowed);
        }

        /// <summary>What following one register's process found.</summary>
        private sealed class Followed
        {
            public (int, EdgeKind) Clock;

            /// <summary>The bits its conditions read, worked out through its blocking temporaries.</summary>
            public IReadOnlyCollection<int> Decisions = Array.Empty<int>();

            public bool Unfollowed;
        }

        private static IEnumerable<EventItem> Edges(ProcessInfo process)
        {
            foreach (Instruction ins in process.Code)
                if (ins.Op == OpCode.WaitEvent && ins.Wait != null)
                    foreach (EventItem item in ins.Wait.Items)
                        if (item.Edge != EdgeKind.Any)
                            yield return item;
        }

        /// <summary>
        /// One register's process, as it runs: an arc of one register into each bit it writes from each bit its value takes,
        /// a blocking temporary followed to what it holds at that point -- on every way there, where ways meet; its conditions,
        /// through a junction, into every bit it writes, as no stage.
        /// </summary>
        /// <remarks>
        /// **What each temporary holds is worked out along the code's own ways** (<see cref="CodeFlow"/>), until nothing more
        /// is learned: a bit written on every way into a point holds what those ways wrote, and one written on some only its
        /// last value too. It was worked out in the order written, a statement any jump passed over taken as on some ways only,
        /// so a temporary written in an if and in its else was kept from the edge before as well (the 16d sweep).
        /// </remarks>
        private static Followed Follow(ProcessInfo process, Design design, int[] first, Builder build, HashSet<int> clocks)
        {
            Instruction[] code = process.Code;
            var run = new FollowRun(code, design, first, build, clocks);
            var flow = new CodeFlow(code);

            // What each blocking-written bit holds where each stretch of code begins. A wait on the clock is the always going
            // round: past it, every bit holds its own last value.
            var at = new Dictionary<int, HashSet<int>>[flow.Count];
            var todo = new Stack<int>();
            var queued = new bool[flow.Count];
            if (flow.Count > 0)
            {
                at[0] = new Dictionary<int, HashSet<int>>();
                todo.Push(0);
                queued[0] = true;
            }
            while (todo.Count > 0)
            {
                int block = todo.Pop();
                queued[block] = false;
                Dictionary<int, HashSet<int>> state = run.Through(flow, block, new Dictionary<int, HashSet<int>>(at[block]), emit: false);
                foreach (int next in flow.Next[block])
                {
                    bool learned;
                    if (at[next] == null)
                    {
                        at[next] = flow.IsWait[next] ? new Dictionary<int, HashSet<int>>() : new Dictionary<int, HashSet<int>>(state);
                        learned = true;
                    }
                    else
                    {
                        learned = !flow.IsWait[next] && Merge(at[next], state);
                    }
                    if (learned && !queued[next])
                    {
                        queued[next] = true;
                        todo.Push(next);
                    }
                }
            }

            // Each stretch reached, once, from what it begins with: the arcs, and what the process holds at its edge.
            Dictionary<int, HashSet<int>> end = null;
            for (int block = 0; block < flow.Count; block++)
            {
                if (at[block] == null)
                    continue;
                Dictionary<int, HashSet<int>> state = run.Through(flow, block, new Dictionary<int, HashSet<int>>(at[block]), emit: true);
                if (!flow.Ends[block])
                    continue;
                if (end == null)
                    end = new Dictionary<int, HashSet<int>>(state);
                else
                    Merge(end, state);
            }

            Followed followed = run.Followed;

            // Its clock: the first edge it waits on that it does not also test -- an asynchronous reset is tested.
            foreach (EventItem item in Edges(process))
            {
                var read = new List<int>();
                item.Value?.CollectReads(read);
                if (read.Count == 0)
                    continue;
                if (followed.Clock.Item1 < 0 || (run.DecidingSignals.Contains(followed.Clock.Item1) && !run.DecidingSignals.Contains(read[0])))
                    followed.Clock = (read[0], item.Edge);
            }

            // A blocking variable keeps what it holds at the edge to the next: a register of its own. So does a memory written
            // with = at a word found as it runs, from what its write's junction was given.
            if (end != null)
                foreach (KeyValuePair<int, HashSet<int>> held in end)
                    foreach (int source in held.Value)
                        if (source != Kept)
                            build.Data(source, held.Key, 1);
            foreach (KeyValuePair<int, List<int>> word in run.Memory)
                foreach (int junction in word.Value)
                    build.Data(junction, word.Key, 1);

            if (run.Decisions.Count > 0 && run.Written.Count > 0)
            {
                int conditions = build.Junction();
                foreach (int d in run.Decisions)
                    build.Condition(d, conditions);
                followed.Decisions = run.Decisions;
                // Into what it decides, through the register it is written in: what a way of decisions alone counts.
                foreach (int w in run.Written)
                    build.Condition(conditions, w, 1);
            }
            return followed;
        }

        /// <summary>Stands for a bit's own last value where it is kept rather than worked out: no arc.</summary>
        private const int Kept = -1;

        /// <summary>
        /// <paramref name="into"/> as it stands once <paramref name="from"/> meets it, two ways joining: each bit what either
        /// holds, and a bit written on one way only its own last value as well. Whether anything was learned. A set once held
        /// is never changed, so one may stand for many bits and many points.
        /// </summary>
        private static bool Merge(Dictionary<int, HashSet<int>> into, Dictionary<int, HashSet<int>> from)
        {
            bool learned = false;
            List<int> keptToo = null;
            foreach (KeyValuePair<int, HashSet<int>> there in into)
                if (!from.ContainsKey(there.Key) && !there.Value.Contains(Kept))
                    (keptToo ??= new List<int>()).Add(there.Key);
            if (keptToo != null)
            {
                foreach (int bit in keptToo)
                    into[bit] = new HashSet<int>(into[bit]) { Kept };
                learned = true;
            }

            foreach (KeyValuePair<int, HashSet<int>> here in from)
            {
                if (into.TryGetValue(here.Key, out HashSet<int> there))
                {
                    if (ReferenceEquals(there, here.Value) || there.IsSupersetOf(here.Value))
                        continue;
                    var both = new HashSet<int>(there);
                    both.UnionWith(here.Value);
                    into[here.Key] = both;
                }
                else
                {
                    into[here.Key] = here.Value.Contains(Kept) ? here.Value : new HashSet<int>(here.Value) { Kept };
                }
                learned = true;
            }
            return learned;
        }

        /// <summary>
        /// A process's code cut into stretches run straight through, and where each goes next: a jump's target and, for one
        /// that may not jump, the next instruction. A wait on an event is a stretch of its own.
        /// </summary>
        private sealed class CodeFlow
        {
            public CodeFlow(Instruction[] code)
            {
                int n = code.Length;
                var leader = new bool[n + 1];
                leader[0] = true;
                for (int i = 0; i < n; i++)
                {
                    OpCode op = code[i].Op;
                    if (IsJump(op))
                    {
                        if (code[i].Jump >= 0 && code[i].Jump < n)
                            leader[code[i].Jump] = true;
                        leader[i + 1] = true;
                    }
                    else if (op == OpCode.WaitEvent)
                    {
                        leader[i] = true;
                        leader[i + 1] = true;
                    }
                    else if (op == OpCode.End)
                    {
                        leader[i + 1] = true;
                    }
                }

                var starts = new List<int>();
                var blockOf = new int[n];
                for (int i = 0; i < n; i++)
                {
                    if (leader[i])
                        starts.Add(i);
                    blockOf[i] = starts.Count - 1;
                }

                Count = starts.Count;
                Start = starts.ToArray();
                Stop = new int[Count];
                Next = new List<int>[Count];
                IsWait = new bool[Count];
                Ends = new bool[Count];
                for (int b = 0; b < Count; b++)
                {
                    Stop[b] = b + 1 < Count ? Start[b + 1] : n;
                    int last = Stop[b] - 1;
                    OpCode op = code[last].Op;
                    IsWait[b] = op == OpCode.WaitEvent;
                    var next = new List<int>(2);
                    bool leaves = false;
                    if (IsJump(op))
                    {
                        int target = code[last].Jump;
                        if (target >= 0 && target < n)
                            next.Add(blockOf[target]);
                        else
                            leaves = true;
                    }
                    if (op != OpCode.Jump && op != OpCode.End)
                    {
                        if (last + 1 < n)
                            next.Add(blockOf[last + 1]);
                        else
                            leaves = true;
                    }
                    if (op == OpCode.End)
                        leaves = true;
                    Next[b] = next;
                    Ends[b] = leaves;
                }

                // What flows into a wait is what the process holds at its edge, as is what leaves its code.
                for (int b = 0; b < Count; b++)
                    foreach (int next in Next[b])
                        if (code[Start[next]].Op == OpCode.WaitEvent)
                            Ends[b] = true;
            }

            public int Count { get; }

            /// <summary>Each stretch's first instruction, and one past its last.</summary>
            public int[] Start { get; }
            public int[] Stop { get; }

            public List<int>[] Next { get; }

            /// <summary>Whether a stretch is a wait on an event: past it, every bit holds its own last value.</summary>
            public bool[] IsWait { get; }

            /// <summary>Whether what a stretch leaves with is what the process holds at an edge: it goes into a wait, or out of the code.</summary>
            public bool[] Ends { get; }

            private static bool IsJump(OpCode op) => op == OpCode.Jump || op == OpCode.JumpIf || op == OpCode.JumpUnless || op == OpCode.JumpIfMatch;
        }

        /// <summary>
        /// Following one process: what it reads to decide, what it writes, and -- once <see cref="Through"/> is asked to
        /// emit -- the arcs of what it writes.
        /// </summary>
        private sealed class FollowRun
        {
            private readonly Instruction[] _code;
            private readonly Design _design;
            private readonly int[] _first;
            private readonly Builder _build;
            private readonly HashSet<int> _clocks;
            private readonly Dictionary<int, int> _junctionOf = new Dictionary<int, int>();
            private readonly Dictionary<int, BitDeps> _blocking = new Dictionary<int, BitDeps>();
            private readonly List<int> _reads = new List<int>();
            private readonly List<int> _gates = new List<int>();

            public FollowRun(Instruction[] code, Design design, int[] first, Builder build, HashSet<int> clocks)
            {
                _code = code;
                _design = design;
                _first = first;
                _build = build;
                _clocks = clocks;

                // A word written with = at a place found as it runs: each such write meets at a junction of its own, which every
                // bit it may land in may hold from then on, wherever in the process it is read -- a word found as it runs is
                // followed only enough to find a loop or an output. A list shared by every bit with no other, so a memory of
                // thousands of words is not a copy for each of its bits (the 16d sweep: 939 MB for 4,096 words).
                for (int i = 0; i < code.Length; i++)
                {
                    Instruction ins = code[i];
                    if (ins.Op != OpCode.Assign || ins.Target == null || ins.Value == null)
                        continue;
                    List<int> only = null;
                    foreach (LPiece piece in ins.Target)
                    {
                        if ((piece.Offset == null && piece.Word == null) || piece.Signal < 0 || piece.Signal >= design.Signals.Count)
                            continue;
                        if (only == null)
                        {
                            _junctionOf[i] = build.Junction();
                            only = new List<int> { _junctionOf[i] };
                        }
                        int words = piece.Word != null ? Math.Max(1, piece.Depth) : 1;
                        for (int w = 0; w < words && piece.Signal + w < design.Signals.Count; w++)
                            for (int b = first[piece.Signal + w]; b < first[piece.Signal + w + 1]; b++)
                                Memory[b] = Memory.TryGetValue(b, out List<int> before) && before != only ? new List<int>(before) { _junctionOf[i] } : only;
                    }
                }
            }

            public Followed Followed { get; } = new Followed { Clock = (-1, EdgeKind.Any) };

            /// <summary>Every bit a memory written with = at a word found as it runs may hold, by the junctions of those writes.</summary>
            public Dictionary<int, List<int>> Memory { get; } = new Dictionary<int, List<int>>();

            public HashSet<int> Written { get; } = new HashSet<int>();
            public HashSet<int> Decisions { get; } = new HashSet<int>();
            public HashSet<int> DecidingSignals { get; } = new HashSet<int>();

            /// <summary>
            /// Stretch <paramref name="block"/> run from <paramref name="state"/>, which it leaves as it stands after it: every
            /// blocking write landed. With <paramref name="emit"/>, its decisions read and its writes' arcs made too.
            /// </summary>
            public Dictionary<int, HashSet<int>> Through(CodeFlow flow, int block, Dictionary<int, HashSet<int>> state, bool emit)
            {
                if (flow.IsWait[block])
                    return new Dictionary<int, HashSet<int>>();
                for (int i = flow.Start[block]; i < flow.Stop[block]; i++)
                {
                    Instruction ins = _code[i];
                    switch (ins.Op)
                    {
                        case OpCode.JumpIf:
                        case OpCode.JumpUnless:
                        case OpCode.JumpIfMatch:
                        case OpCode.WaitUntil:
                            if (!emit)
                                break;
                            _reads.Clear();
                            ins.Value?.CollectReads(_reads);
                            ins.Other?.CollectReads(_reads);
                            Decide(_reads, state);
                            break;

                        case OpCode.Assign:
                        case OpCode.AssignLater:
                            if (ins.Target == null || ins.Value == null || (!emit && ins.Op != OpCode.Assign))
                                break;
                            _gates.Clear();
                            Followed.Unfollowed |= Write(i, ins, state, emit);
                            // An enable written through ?: decides as an if does (the M2 review): its condition a way, not data.
                            if (emit)
                                Decide(_gates, state);
                            break;
                    }
                }
                return state;
            }

            private void Decide(List<int> signals, Dictionary<int, HashSet<int>> state)
            {
                foreach (int s in signals)
                {
                    DecidingSignals.Add(s);
                    if (_clocks.Contains(s) || s < 0 || s >= _design.Signals.Count)
                        continue;
                    for (int b = _first[s]; b < _first[s + 1]; b++)
                        Decisions.UnionWith(Now(b, state));
                }
            }

            /// <summary>What <paramref name="bit"/> holds at this point of the process.</summary>
            private IEnumerable<int> Now(int bit, Dictionary<int, HashSet<int>> state)
            {
                if (state.TryGetValue(bit, out HashSet<int> sources))
                {
                    foreach (int s in sources)
                        yield return s == Kept ? bit : s;
                }
                else
                {
                    yield return bit;
                }
                if (Memory.Count > 0 && Memory.TryGetValue(bit, out List<int> written))
                    foreach (int junction in written)
                        yield return junction;
            }

            private HashSet<int> Expanded(SourceSet from, Dictionary<int, HashSet<int>> state)
            {
                var sources = new HashSet<int>();
                for (int k = 0; k < from.Count; k++)
                    sources.UnionWith(Now(from[k].Node, state));
                return sources;
            }

            /// <summary>
            /// One assignment landed: a blocking one's bits now hold what its value reads, worked out before any of them is
            /// written; a nonblocking one's take it at the edge, through one register, when <paramref name="emit"/>. Whether any
            /// bit is at a place found as it runs.
            /// </summary>
            private bool Write(int at, Instruction ins, Dictionary<int, HashSet<int>> state, bool emit)
            {
                bool blocking = ins.Op == OpCode.Assign;
                // A register written whole at the edge: a ?: arm giving it back is its hold, an enable, never a way round.
                int held = !blocking && ins.Target.Length == 1 ? WholeSignal(ins.Target[0], _design, _first) : -1;
                BitDeps value;
                if (held >= 0)
                    value = ins.Value.DependsHolding(_first, held, _gates);
                else if (!blocking)
                    value = ins.Value.Depends(_first);
                else if (!_blocking.TryGetValue(at, out value))
                    _blocking[at] = value = ins.Value.Depends(_first);   // a blocking write is run each time its stretch is
                bool unfollowed = false;
                List<(int Bit, HashSet<int> Sources)> landed = null;
                HashSet<int> whole = null;
                int offset = ins.TargetWidth;
                foreach (LPiece piece in ins.Target)
                {
                    offset -= piece.Width;
                    if (piece.Signal < 0 || piece.Signal >= _design.Signals.Count)
                        continue;

                    if (piece.Offset == null && piece.Word == null)
                    {
                        int lowest = _first[piece.Signal] + piece.Lowest;
                        if (!blocking && value.IsUniform && (long)value.Union.Count * piece.Width > MostArcsApart)
                        {
                            // Every bit from the same many: through a junction, as the timing graph spreads one.
                            HashSet<int> all = Expanded(value.Union, state);
                            int junction = _build.Junction();
                            foreach (int s in all)
                                _build.Data(s, junction, 0);
                            for (int i = 0; i < piece.Width; i++)
                                if (lowest + i < _first[piece.Signal + 1])
                                {
                                    Written.Add(lowest + i);
                                    _build.Data(junction, lowest + i, 1);
                                }
                            continue;
                        }

                        for (int i = 0; i < piece.Width; i++)
                        {
                            int target = lowest + i;
                            if (target < _first[piece.Signal] || target >= _first[piece.Signal + 1])
                                continue;
                            // A value every bit of which reads the same: one set for them all.
                            HashSet<int> sources = value.IsUniform ? (whole ??= Expanded(value.Union, state)) : Expanded(value.Of(offset + i), state);
                            if (emit)
                                Written.Add(target);
                            if (blocking)
                                (landed ??= new List<(int, HashSet<int>)>()).Add((target, sources));
                            else if (!(value.Routing && sources.Count == 1 && sources.Contains(target)))   // q <= q is kept
                                foreach (int s in sources)
                                    _build.Data(s, target, 1);
                        }
                        continue;
                    }

                    // Its place found as it runs: any bit of any word it may land in, from any bit of the value or the place --
                    // enough to find a loop or an output reached, too much to count stages by.
                    unfollowed = true;
                    if (!emit)
                        continue;
                    HashSet<int> any = Expanded(value.Union, state);
                    _reads.Clear();
                    piece.Offset?.CollectReads(_reads);
                    piece.Word?.CollectReads(_reads);
                    foreach (int s in _reads)
                        if (s >= 0 && s < _design.Signals.Count)
                            for (int b = _first[s]; b < _first[s + 1]; b++)
                                any.UnionWith(Now(b, state));
                    int into = blocking ? _junctionOf[at] : _build.Junction();
                    foreach (int s in any)
                        _build.Data(s, into, 0);
                    int words = piece.Word != null ? Math.Max(1, piece.Depth) : 1;
                    for (int w = 0; w < words && piece.Signal + w < _design.Signals.Count; w++)
                        for (int b = _first[piece.Signal + w]; b < _first[piece.Signal + w + 1]; b++)
                        {
                            Written.Add(b);
                            if (!blocking)
                                _build.Data(into, b, 1);
                        }
                }

                if (landed != null)
                    foreach ((int bit, HashSet<int> sources) in landed)
                        state[bit] = sources;
                return unfollowed;
            }
        }

        /// <summary>The signal <paramref name="piece"/> writes, when it writes all of it at a place fixed in the code; -1 otherwise.</summary>
        private static int WholeSignal(LPiece piece, Design design, int[] first) =>
            piece.Offset == null && piece.Word == null && piece.Signal >= 0 && piece.Signal < design.Signals.Count && piece.Lowest == 0
            && piece.Width == first[piece.Signal + 1] - first[piece.Signal] ? piece.Signal : -1;

        private static bool IsClock(int node, HashSet<int> clocks, int[] first)
        {
            foreach (int c in clocks)
                if (c >= 0 && c + 1 < first.Length && node >= first[c] && node < first[c + 1])
                    return true;
            return false;
        }

        private static bool Touches(TimingRegister register, bool[] on)
        {
            foreach (int q in register.Launches)
                if (q >= 0 && q < on.Length && on[q])
                    return true;
            return false;
        }

        /// <summary>
        /// The fewest and the most registers on a way from <paramref name="inputs"/> to each node <paramref name="on"/>, along
        /// <paramref name="next"/> in <paramref name="order"/>; which nodes a way reaches.
        /// </summary>
        private static bool[] Count(int n, List<int> inputs, bool[] on, List<(int To, int Weight)>[] next, List<int> order,
            out int[] fewest, out int[] most)
        {
            fewest = new int[n];
            most = new int[n];
            var reached = new bool[n];
            foreach (int input in inputs)
                if (input >= 0 && input < n)
                    reached[input] = true;
            foreach (int v in order)
            {
                if (!reached[v] || next[v] == null)
                    continue;
                foreach ((int to, int weight) in next[v])
                {
                    if (!on[to])
                        continue;
                    int low = fewest[v] + weight, high = most[v] + weight;
                    if (!reached[to])
                    {
                        reached[to] = true;
                        fewest[to] = low;
                        most[to] = high;
                    }
                    else
                    {
                        fewest[to] = Math.Min(fewest[to], low);
                        most[to] = Math.Max(most[to], high);
                    }
                }
            }
            return reached;
        }

        /// <summary>Every node on some way from <paramref name="starts"/> to <paramref name="ends"/>.</summary>
        private static bool[] Between(int n, IReadOnlyList<int> starts, IReadOnlyList<int> ends, List<(int To, int Weight)>[] next)
        {
            var back = new List<int>[n];
            for (int v = 0; v < n; v++)
                if (next[v] != null)
                    foreach ((int to, int _) in next[v])
                        (back[to] ??= new List<int>()).Add(v);

            bool[] forward = Reach(n, starts, v => Targets(next[v]));
            bool[] backward = Reach(n, ends, v => back[v]);
            var on = new bool[n];
            for (int v = 0; v < n; v++)
                on[v] = forward[v] && backward[v];
            return on;
        }

        /// <summary>The nodes <paramref name="on"/> in an order every arc goes forward in; false when some are on a loop.</summary>
        private static bool InOrder(int n, bool[] on, List<(int To, int Weight)>[] next, out List<int> order)
        {
            var into = new int[n];
            int count = 0;
            for (int v = 0; v < n; v++)
            {
                if (!on[v])
                    continue;
                count++;
                if (next[v] != null)
                    foreach ((int to, int _) in next[v])
                        if (on[to])
                            into[to]++;
            }
            order = new List<int>(count);
            var queue = new Queue<int>();
            for (int v = 0; v < n; v++)
                if (on[v] && into[v] == 0)
                    queue.Enqueue(v);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                order.Add(v);
                if (next[v] != null)
                    foreach ((int to, int _) in next[v])
                        if (on[to] && --into[to] == 0)
                            queue.Enqueue(to);
            }
            return order.Count == count;
        }

        private static IEnumerable<int> Targets(List<(int To, int Weight)> edges)
        {
            if (edges == null)
                yield break;
            foreach ((int to, int _) in edges)
                yield return to;
        }

        /// <summary>Where <paramref name="edges"/> go through logic alone: the arcs that cross no register.</summary>
        private static IEnumerable<int> ThroughLogic(List<(int To, int Weight)> edges)
        {
            if (edges == null)
                yield break;
            foreach ((int to, int weight) in edges)
                if (weight == 0)
                    yield return to;
        }

        /// <summary>Every bit reached from <paramref name="starts"/> along <paramref name="step"/>, the starts included.</summary>
        private static bool[] Reach(int n, IReadOnlyList<int> starts, Func<int, IEnumerable<int>> step)
        {
            var reached = new bool[n];
            var stack = new Stack<int>();
            foreach (int start in starts)
                if (start >= 0 && start < n && !reached[start])
                {
                    reached[start] = true;
                    stack.Push(start);
                }
            while (stack.Count > 0)
            {
                IEnumerable<int> onward = step(stack.Pop());
                if (onward == null)
                    continue;
                foreach (int to in onward)
                    if (!reached[to])
                    {
                        reached[to] = true;
                        stack.Push(to);
                    }
            }
            return reached;
        }

        /// <summary>The arcs: of values, weighted by the registers they cross, and of conditions, which cross none.</summary>
        private sealed class Builder
        {
            private readonly List<(int From, int To, int Weight, bool Data)> _arcs = new List<(int, int, int, bool)>();

            public Builder(int nodes) => Count = nodes;

            public int Count { get; private set; }

            public int Junction() => Count++;

            public void Data(int from, int to, int weight) => _arcs.Add((from, to, weight, true));

            public void Condition(int from, int to, int weight = 0) => _arcs.Add((from, to, weight, false));

            /// <summary>
            /// Each node's arcs out: of values alone, or of values and conditions -- a condition crossing the register it
            /// decides, which only an output no value reaches is counted by.
            /// </summary>
            public List<(int To, int Weight)>[] Adjacency(int n, bool data)
            {
                var next = new List<(int To, int Weight)>[n];
                foreach ((int from, int to, int weight, bool isData) in _arcs)
                {
                    if (data && !isData)
                        continue;
                    if (from < 0 || to < 0 || from >= n || to >= n)
                        continue;
                    (next[from] ??= new List<(int, int)>()).Add((to, weight));
                }
                return next;
            }
        }
    }
}
