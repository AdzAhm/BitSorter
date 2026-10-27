using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BitSorter.Sim
{
    /// <summary>
    /// One path a change can take (Part 14a): the arcs from one signal to another, and how long the change takes along them
    /// when it starts as a rise and when it starts as a fall. Identical paths -- the same gates, at the same delays, in the
    /// same order -- are one, counted.
    /// </summary>
    public sealed class TimingPath
    {
        internal TimingPath(int from, int to, IReadOnlyList<TimingArc> arcs, long rising, long falling)
        {
            From = from;
            To = to;
            Arcs = arcs;
            Rising = rising;
            Falling = falling;
            _alike.Add((from, to));
        }

        private readonly List<(int From, int To)> _alike = new List<(int, int)>();

        /// <summary>Where it starts, by signal index.</summary>
        public int From { get; }

        /// <summary>Where it ends.</summary>
        public int To { get; }

        /// <summary>Every arc along it, a connection's included.</summary>
        public IReadOnlyList<TimingArc> Arcs { get; }

        /// <summary>
        /// How long after its start rises its end can change -- the latest, where a gate passes a change either way -- or
        /// <see cref="PathDelays.Never"/> when no rise at its start reaches its end.
        /// </summary>
        public long Rising { get; }

        /// <summary>The same, when its start falls.</summary>
        public long Falling { get; }

        public long Worst => Math.Max(Rising, Falling);

        /// <summary>How many paths are this one: the same gates in the same order, from other ends.</summary>
        public int Count => _alike.Count;

        /// <summary>Each identical path's ends, this one's first.</summary>
        public IReadOnlyList<(int From, int To)> Alike => _alike;

        internal void AddAlike(int from, int to) => _alike.Add((from, to));

        /// <summary>The arcs that are gates or logic, a connection left out: what a path is said to pass through.</summary>
        public IEnumerable<TimingArc> Steps
        {
            get
            {
                foreach (TimingArc arc in Arcs)
                    if (!arc.IsWire)
                        yield return arc;
            }
        }
    }

    /// <summary>Every path from a design's inputs to its outputs, identical ones together, the worst first (Part 14a).</summary>
    public sealed class PathReport
    {
        internal PathReport(IReadOnlyList<TimingPath> paths, int found, bool capped)
        {
            Paths = paths;
            Found = found;
            Capped = capped;
        }

        public IReadOnlyList<TimingPath> Paths { get; }

        /// <summary>How many paths there were before identical ones were put together.</summary>
        public int Found { get; }

        /// <summary>Whether there were more than <see cref="PathDelays.MaxPaths"/>, and only those were reckoned.</summary>
        public bool Capped { get; }

        /// <summary>The slowest path; null when no input reaches an output.</summary>
        public TimingPath Worst => Paths.Count > 0 ? Paths[0] : null;
    }

    /// <summary>
    /// The course's path delays (Part 14a): every path from an input to an output, each gate adding its <c>Tpdlh</c> where
    /// its output rises and its <c>Tpdhl</c> where it falls, a change flipping at an inverting gate -- NOT, NAND, NOR -- and
    /// taken either way where a gate can pass it either way, XOR and XNOR, so a path's delay is the latest it can be.
    /// </summary>
    /// <remarks>
    /// This is the path as drawn: whether some input values make the change travel it at all is not asked here. A path no
    /// step ever fires is found by trying the steps (Part 14c), and marked there.
    /// </remarks>
    public static class PathDelays
    {
        /// <summary>The most paths reckoned: a design wide enough to have more says so rather than taking forever.</summary>
        public const int MaxPaths = 10_000;

        /// <summary>A change that cannot arrive.</summary>
        public const long Never = -1;

        public static PathReport Find(TimingGraph graph, int max = MaxPaths)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var outputs = new HashSet<int>(graph.Outputs);
            var onPath = new bool[graph.Design.Signals.Count];
            var arcs = new List<TimingArc>();
            var found = new List<TimingPath>();
            bool capped = false;

            foreach (int start in graph.Inputs)
            {
                // Depth first, without recursion: each frame is a signal and the next arc to try from it.
                var frames = new Stack<(int Node, int Next)>();
                frames.Push((start, 0));
                onPath[start] = true;
                while (frames.Count > 0 && !capped)
                {
                    (int node, int next) = frames.Pop();
                    if (next == 0 && node != start && outputs.Contains(node))
                    {
                        if (found.Count >= max)
                        {
                            capped = true;
                            break;
                        }

                        found.Add(Reckon(start, node, arcs));
                    }

                    IReadOnlyList<int> leaving = graph.Leaving(node);
                    bool descended = false;
                    for (int k = next; k < leaving.Count; k++)
                    {
                        TimingArc arc = graph.Arcs[leaving[k]];
                        if (onPath[arc.To])
                            continue;
                        frames.Push((node, k + 1));
                        frames.Push((arc.To, 0));
                        onPath[arc.To] = true;
                        arcs.Add(arc);
                        descended = true;
                        break;
                    }

                    if (descended)
                        continue;

                    onPath[node] = false;
                    if (arcs.Count > 0 && frames.Count > 0)
                        arcs.RemoveAt(arcs.Count - 1);
                }

                Array.Clear(onPath, 0, onPath.Length);
                arcs.Clear();
                if (capped)
                    break;
            }

            return new PathReport(Group(graph, found), found.Count, capped);
        }

        /// <summary>A path's delays, from its start rising and from it falling.</summary>
        internal static TimingPath Reckon(int from, int to, List<TimingArc> arcs)
        {
            var copy = arcs.ToArray();
            return new TimingPath(from, to, copy, Along(copy, startRises: true), Along(copy, startRises: false));
        }

        /// <summary>
        /// The latest a change reaches the end of <paramref name="arcs"/>: each arc's delay added for the way its output goes,
        /// both ways followed where it can go either.
        /// </summary>
        internal static long Along(IReadOnlyList<TimingArc> arcs, bool startRises)
        {
            long rise = startRises ? 0 : Never, fall = startRises ? Never : 0;
            foreach (TimingArc arc in arcs)
            {
                (long toRise, long toFall) = Through(arc.Sense, rise, fall);
                rise = toRise == Never ? Never : toRise + arc.Rise;
                fall = toFall == Never ? Never : toFall + arc.Fall;
            }

            return Math.Max(rise, fall);
        }

        /// <summary>Which change reaches an arc's output, by when, from a rise or a fall at its input.</summary>
        internal static (long Rise, long Fall) Through(Sense sense, long rise, long fall)
        {
            switch (sense)
            {
                case Sense.Positive:
                    return (rise, fall);
                case Sense.Negative:
                    return (fall, rise);
                default:
                    long either = Math.Max(rise, fall);
                    return (either, either);
            }
        }

        private static IReadOnlyList<TimingPath> Group(TimingGraph graph, List<TimingPath> found)
        {
            var groups = new Dictionary<string, TimingPath>(StringComparer.Ordinal);
            var order = new List<TimingPath>();
            foreach (TimingPath path in found)
            {
                string key = Signature(path);
                if (groups.TryGetValue(key, out TimingPath same))
                {
                    same.AddAlike(path.From, path.To);
                    continue;
                }

                groups[key] = path;
                order.Add(path);
            }

            order.Sort((a, b) =>
            {
                int byWorst = b.Worst.CompareTo(a.Worst);
                if (byWorst != 0)
                    return byWorst;
                int byFrom = string.CompareOrdinal(graph.Design.Signals[a.From].Name, graph.Design.Signals[b.From].Name);
                return byFrom != 0 ? byFrom : string.CompareOrdinal(graph.Design.Signals[a.To].Name, graph.Design.Signals[b.To].Name);
            });
            return order;
        }

        /// <summary>What makes two paths the same: their gates' kinds and delays in order, and so their delays.</summary>
        private static string Signature(TimingPath path)
        {
            var key = new StringBuilder();
            foreach (TimingArc arc in path.Steps)
            {
                key.Append(arc.Gate?.ToString() ?? "logic").Append(arc.Inputs.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(arc.Rise.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(arc.Fall.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append((int)arc.Sense).Append('>');
            }

            return key.Append(path.Rising.ToString(CultureInfo.InvariantCulture)).Append('|').Append(path.Falling.ToString(CultureInfo.InvariantCulture)).ToString();
        }
    }
}
