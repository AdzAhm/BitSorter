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
    /// **Bit by bit, as the timing graph is.** Each register's process is followed in the order it is written: what each
    /// bit it writes takes its next value from, through any blocking temporary as it stands at that point, so a shift
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
    /// writing 1 or 0 is one stage (the 16d sweep). A condition that reads a register makes its process's writes
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

            // What every register writes, for whether a condition reads one; and every clock, first, for none is a condition.
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
                follows[register] = Follow(register.Process, design, first, build, registerBits, clocks);
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

            bool sequenced = false, mixed = false, unfollowed = false;
            (int, EdgeKind)? seen = null;
            foreach (KeyValuePair<TimingRegister, Followed> each in follows)
            {
                if (!Touches(each.Key, on))
                    continue;
                sequenced |= each.Value.Sequenced;
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
            public bool Sequenced;
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
        /// One register's process, in order: an arc of one register into each bit it writes from each bit its value takes,
        /// a blocking temporary followed to what it holds at that point; its conditions, through a junction, into every bit
        /// it writes, as no stage.
        /// </summary>
        private static Followed Follow(ProcessInfo process, Design design, int[] first, Builder build, HashSet<int> registerBits,
            HashSet<int> clocks)
        {
            Instruction[] code = process.Code;
            var followed = new Followed { Clock = (-1, EdgeKind.Any) };

            // A statement is on some ways only when a jump passes over it -- but the jump back to the event control, which
            // is the always itself going round.
            var waits = new HashSet<int>();
            for (int i = 0; i < code.Length; i++)
                if (code[i].Op == OpCode.WaitEvent)
                    waits.Add(i);
            var sometimes = new bool[code.Length];
            for (int i = 0; i < code.Length; i++)
            {
                OpCode op = code[i].Op;
                if (op != OpCode.Jump && op != OpCode.JumpIf && op != OpCode.JumpUnless && op != OpCode.JumpIfMatch)
                    continue;
                if (waits.Contains(code[i].Jump))
                    continue;
                int from = Math.Min(i, code[i].Jump), to = Math.Max(i, code[i].Jump);
                for (int k = Math.Max(0, from + 1); k < Math.Min(code.Length, to); k++)
                    sometimes[k] = true;
            }

            // What each blocking-written bit holds now: the bits it was worked out from. Absent, it holds its own last value.
            var holds = new Dictionary<int, HashSet<int>>();
            var written = new HashSet<int>();
            var decisions = new HashSet<int>();
            var decidingSignals = new HashSet<int>();
            var reads = new List<int>();
            var gates = new List<int>();

            for (int i = 0; i < code.Length; i++)
            {
                Instruction ins = code[i];
                switch (ins.Op)
                {
                    case OpCode.JumpIf:
                    case OpCode.JumpUnless:
                    case OpCode.JumpIfMatch:
                    case OpCode.WaitUntil:
                        reads.Clear();
                        ins.Value?.CollectReads(reads);
                        ins.Other?.CollectReads(reads);
                        foreach (int s in reads)
                        {
                            decidingSignals.Add(s);
                            if (clocks.Contains(s) || s < 0 || s >= design.Signals.Count)
                                continue;
                            for (int b = first[s]; b < first[s + 1]; b++)
                                decisions.UnionWith(Now(b, holds));
                        }
                        break;

                    case OpCode.Assign:
                    case OpCode.AssignLater:
                        if (ins.Target == null || ins.Value == null)
                            break;
                        gates.Clear();
                        followed.Unfollowed |= Write(ins, sometimes[i], design, first, build, holds, written, gates);
                        // An enable written through ?: decides as an if does (the M2 review): its condition a way, not data.
                        foreach (int s in gates)
                        {
                            decidingSignals.Add(s);
                            if (clocks.Contains(s) || s < 0 || s >= design.Signals.Count)
                                continue;
                            for (int b = first[s]; b < first[s + 1]; b++)
                                decisions.UnionWith(Now(b, holds));
                        }
                        break;
                }
            }

            // Its clock: the first edge it waits on that it does not also test -- an asynchronous reset is tested.
            foreach (EventItem item in Edges(process))
            {
                var read = new List<int>();
                item.Value?.CollectReads(read);
                if (read.Count == 0)
                    continue;
                if (followed.Clock.Item1 < 0 || (decidingSignals.Contains(followed.Clock.Item1) && !decidingSignals.Contains(read[0])))
                    followed.Clock = (read[0], item.Edge);
            }

            // A blocking variable keeps what it holds at the end to the next edge: a register of its own.
            foreach (KeyValuePair<int, HashSet<int>> held in holds)
                foreach (int source in held.Value)
                    if (source != Kept)
                        build.Data(source, held.Key, 1);

            if (decisions.Count > 0 && written.Count > 0)
            {
                int conditions = build.Junction();
                foreach (int d in decisions)
                {
                    build.Condition(d, conditions);
                    followed.Sequenced |= registerBits.Contains(d);
                }
                // Into what it decides, through the register it is written in: what a way of decisions alone counts.
                foreach (int w in written)
                    build.Condition(conditions, w, 1);
            }
            return followed;
        }

        /// <summary>Stands for a bit's own last value where it is kept rather than worked out: no arc.</summary>
        private const int Kept = -1;

        /// <summary>What <paramref name="bit"/> holds at this point of the process.</summary>
        private static IEnumerable<int> Now(int bit, Dictionary<int, HashSet<int>> holds)
        {
            if (holds.TryGetValue(bit, out HashSet<int> sources))
            {
                foreach (int s in sources)
                    yield return s == Kept ? bit : s;
            }
            else
            {
                yield return bit;
            }
        }

        /// <summary>One assignment's bits landed; whether any is at a place found as it runs.</summary>
        private static bool Write(Instruction ins, bool sometimes, Design design, int[] first, Builder build,
            Dictionary<int, HashSet<int>> holds, HashSet<int> written, ICollection<int> gates)
        {
            bool blocking = ins.Op == OpCode.Assign;
            // A register written whole at the edge: a ?: arm giving it back is its hold, an enable, never a way round.
            int held = !blocking && ins.Target != null && ins.Target.Length == 1 ? WholeSignal(ins.Target[0], design, first) : -1;
            BitDeps value = held >= 0 ? ins.Value.DependsHolding(first, held, gates) : ins.Value.Depends(first);
            bool unfollowed = false;
            int offset = ins.TargetWidth;
            foreach (LPiece piece in ins.Target)
            {
                offset -= piece.Width;
                if (piece.Signal < 0 || piece.Signal >= design.Signals.Count)
                    continue;

                if (piece.Offset == null && piece.Word == null)
                {
                    int lowest = first[piece.Signal] + piece.Lowest;
                    if (!blocking && value.IsUniform && (long)value.Union.Count * piece.Width > MostArcsApart)
                    {
                        // Every bit from the same many: through a junction, as the timing graph spreads one.
                        HashSet<int> all = Expanded(value.Union, holds);
                        int junction = build.Junction();
                        foreach (int s in all)
                            build.Data(s, junction, 0);
                        for (int i = 0; i < piece.Width; i++)
                            if (lowest + i < first[piece.Signal + 1])
                            {
                                written.Add(lowest + i);
                                build.Data(junction, lowest + i, 1);
                            }
                        continue;
                    }

                    for (int i = 0; i < piece.Width; i++)
                    {
                        int target = lowest + i;
                        if (target < first[piece.Signal] || target >= first[piece.Signal + 1])
                            continue;
                        Land(target, Expanded(value.Of(offset + i), holds), blocking, sometimes, value.Routing, build, holds, written);
                    }
                    continue;
                }

                // Its place found as it runs: any bit of any word it may land in, from any bit of the value or the place --
                // enough to find a loop or an output reached, too much to count stages by.
                unfollowed = true;
                HashSet<int> any = Expanded(value.Union, holds);
                var where = new List<int>();
                piece.Offset?.CollectReads(where);
                piece.Word?.CollectReads(where);
                foreach (int s in where)
                    if (s >= 0 && s < design.Signals.Count)
                        for (int b = first[s]; b < first[s + 1]; b++)
                            any.UnionWith(Now(b, holds));
                int words = piece.Word != null ? Math.Max(1, piece.Depth) : 1;
                int into = blocking ? -1 : build.Junction();
                if (into >= 0)
                    foreach (int s in any)
                        build.Data(s, into, 0);
                for (int w = 0; w < words && piece.Signal + w < design.Signals.Count; w++)
                    for (int b = first[piece.Signal + w]; b < first[piece.Signal + w + 1]; b++)
                    {
                        written.Add(b);
                        if (into >= 0)
                            build.Data(into, b, 1);
                        else
                            Land(b, any, blocking: true, sometimes: true, routing: false, build, holds, written);
                    }
            }
            return unfollowed;
        }

        /// <summary>The signal <paramref name="piece"/> writes, when it writes all of it at a place fixed in the code; -1 otherwise.</summary>
        private static int WholeSignal(LPiece piece, Design design, int[] first) =>
            piece.Offset == null && piece.Word == null && piece.Signal >= 0 && piece.Signal < design.Signals.Count && piece.Lowest == 0
            && piece.Width == first[piece.Signal + 1] - first[piece.Signal] ? piece.Signal : -1;

        private static HashSet<int> Expanded(SourceSet from, Dictionary<int, HashSet<int>> holds)
        {
            var sources = new HashSet<int>();
            for (int k = 0; k < from.Count; k++)
                sources.UnionWith(Now(from[k].Node, holds));
            return sources;
        }

        /// <summary>
        /// <paramref name="target"/> written from <paramref name="sources"/>: a blocking variable now holds them -- with its
        /// last value too where it is written only on some ways -- and a nonblocking one takes them at the edge, through one
        /// register. A bit given its own value and nothing else is kept, as an enable's register keeps it.
        /// </summary>
        private static void Land(int target, HashSet<int> sources, bool blocking, bool sometimes, bool routing, Builder build,
            Dictionary<int, HashSet<int>> holds, HashSet<int> written)
        {
            written.Add(target);
            if (blocking)
            {
                var now = new HashSet<int>(sources);
                if (sometimes)
                {
                    if (holds.TryGetValue(target, out HashSet<int> before))
                        now.UnionWith(before);
                    else
                        now.Add(Kept);
                }
                holds[target] = now;
                return;
            }

            if (routing && sources.Count == 1 && sources.Contains(target))
                return;
            foreach (int s in sources)
                build.Data(s, target, 1);
        }

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
