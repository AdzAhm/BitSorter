using System;
using System.Collections.Generic;

namespace BitSorter.Sim
{
    /// <summary>
    /// How many registers a design's paths from its inputs to its outputs cross (Part 15i-1, asked 2026-11-02 beside the
    /// course read): what its latency is in clock periods. A design whose every such path crosses the same number K is a
    /// K-stage pipeline -- the course's K-pipe -- and a result leaves K periods after its inputs came in, one a period after
    /// that. A register on a loop is a state machine's, whose output follows no input a fixed number of periods later; and
    /// paths of different depths are no pipeline at all.
    /// </summary>
    public sealed class PipelineDepth
    {
        private PipelineDepth(bool reaches, int fewest, int most, bool loop)
        {
            Reaches = reaches;
            Fewest = fewest;
            Most = most;
            Loop = loop;
        }

        /// <summary>Whether any input reaches any output.</summary>
        public bool Reaches { get; }

        /// <summary>The fewest registers a path from an input to an output crosses.</summary>
        public int Fewest { get; }

        /// <summary>The most a path from an input to an output crosses.</summary>
        public int Most { get; }

        /// <summary>Whether a path runs round a loop -- through a register, as a state machine's does, or of logic alone.</summary>
        public bool Loop { get; }

        /// <summary>K for a K-stage pipeline: every path from an input to an output crossing K registers; null for none.</summary>
        public int? Stages => Reaches && !Loop && Fewest == Most ? Fewest : (int?)null;

        public static PipelineDepth Of(TimingGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            // Every arc of logic, and every register: the bits each value it writes reads -- what its own assignment reads,
            // through any blocking temporary -- a junction, and the bits it writes, one register crossed. A process is one
            // register to the timing graph, and a pipeline written as one always_ff of stages would be one loop to it.
            var edges = new List<(int From, int To, int Weight)>();
            int n = graph.NodeCount;
            foreach (TimingArc arc in graph.Arcs)
                edges.Add((arc.From, arc.To, 0));

            foreach (TimingRegister register in graph.Registers)
            {
                // By the process itself, never its name: a drawn module's registers are all always_ff@0.
                Dictionary<int, HashSet<int>> flow = register.Process != null ? FlowOf(register.Process) : null;
                if (flow == null || flow.Count == 0)
                {
                    int junction = n++;
                    foreach (int d in register.Captures)
                        edges.Add((d, junction, 0));
                    foreach (int q in register.Launches)
                        edges.Add((junction, q, 1));
                    continue;
                }
                foreach (KeyValuePair<int, HashSet<int>> write in flow)
                {
                    if (graph.Design.Signals[write.Key].Local)
                        continue;
                    int junction = n++;
                    foreach (int read in write.Value)
                        if (!graph.Design.Signals[read].Local)
                            for (int b = 0; b < graph.Design.Signals[read].Width; b++)
                                edges.Add((graph.NodeOf(read, b), junction, 0));
                    for (int b = 0; b < graph.Design.Signals[write.Key].Width; b++)
                        edges.Add((junction, graph.NodeOf(write.Key, b), 1));
                }
            }

            var next = new List<(int To, int Weight)>[n];
            var back = new List<int>[n];
            foreach ((int from, int to, int weight) in edges)
            {
                if (from < 0 || to < 0 || from >= n || to >= n)
                    continue;
                (next[from] ??= new List<(int, int)>()).Add((to, weight));
                (back[to] ??= new List<int>()).Add(from);
            }

            // Only what lies on a way from an input to an output counts: a counter beside the pipeline loops, and the
            // pipeline is a pipeline all the same.
            bool[] fromInput = Reach(n, graph.Inputs, v => next[v] == null ? null : Targets(next[v]));
            bool[] toOutput = Reach(n, graph.Outputs, v => back[v]);
            var on = new bool[n];
            for (int v = 0; v < n; v++)
                on[v] = fromInput[v] && toOutput[v];

            // In order, so each bit's counts are whole before it passes them on; what is left unordered is on a loop.
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
            var order = new List<int>(count);
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
            if (order.Count < count)
                return new PipelineDepth(true, 0, 0, loop: true);

            var fewest = new int[n];
            var most = new int[n];
            var reached = new bool[n];
            foreach (int input in graph.Inputs)
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

            bool any = false;
            int least = int.MaxValue, greatest = 0;
            foreach (int output in graph.Outputs)
            {
                if (output < 0 || output >= n || !reached[output])
                    continue;
                any = true;
                least = Math.Min(least, fewest[output]);
                greatest = Math.Max(greatest, most[output]);
            }
            return new PipelineDepth(any, any ? least : 0, greatest, loop: false);
        }

        /// <summary>
        /// What each signal an edge-triggered process writes takes its next value from: the signals its assignments' values
        /// read, a blocking temporary's own reads passed through, since its value is used within the same edge. Its ifs and
        /// cases are left out -- an enable or a reset gates a stage, and is no stage of its own, as the course counts one.
        /// </summary>
        private static Dictionary<int, HashSet<int>> FlowOf(ProcessInfo process)
        {
            var direct = new Dictionary<int, HashSet<int>>();
            var blocking = new HashSet<int>();
            foreach (Instruction ins in process.Code)
            {
                if ((ins.Op != OpCode.Assign && ins.Op != OpCode.AssignLater) || ins.Target == null)
                    continue;
                var reads = new HashSet<int>();
                ins.Value?.CollectReads(reads);
                foreach (LPiece piece in ins.Target)
                {
                    piece.Offset?.CollectReads(reads);
                    piece.Word?.CollectReads(reads);
                }
                foreach (LPiece piece in ins.Target)
                {
                    if (!direct.TryGetValue(piece.Signal, out HashSet<int> into))
                        direct[piece.Signal] = into = new HashSet<int>();
                    into.UnionWith(reads);
                    if (ins.Op == OpCode.Assign)
                        blocking.Add(piece.Signal);
                }
            }

            var flow = new Dictionary<int, HashSet<int>>();
            foreach (int written in direct.Keys)
            {
                var reads = new HashSet<int>();
                var seen = new HashSet<int> { written };
                var stack = new Stack<int>(direct[written]);
                while (stack.Count > 0)
                {
                    int read = stack.Pop();
                    if (blocking.Contains(read) && read != written && direct.TryGetValue(read, out HashSet<int> through))
                    {
                        if (seen.Add(read))
                            foreach (int r in through)
                                stack.Push(r);
                    }
                    else
                    {
                        reads.Add(read);
                    }
                }
                flow[written] = reads;
            }
            return flow;
        }

        private static IEnumerable<int> Targets(List<(int To, int Weight)> edges)
        {
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
    }
}
