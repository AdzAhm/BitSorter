using System;
using BitSorter.Sim;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Which signals of a design are one electrical node: a net, the ports of every instance it is
    /// connected to, and the cell outputs that drive it, down through any depth of blocks. What a
    /// waveform row needs to show a glitch swallowed on its way to any of them.
    /// </summary>
    /// <remarks>
    /// **Joined where the elaborator joins them.** A port connected to a net is a zero-delay
    /// assignment that reads the one whole net (<see cref="Expr.TryRead"/>): into an instance for an
    /// input, out of it for an output. Every such assignment puts its two ends in one node, so
    /// <c>dut.h1.g1.Z</c> (the XOR cell's output), <c>dut.h1.s</c>, the parent's <c>dut.h1_s</c> and
    /// <c>dut.h2.a</c> are one: a change swallowed on the cell's output was on its way to all of them.
    ///
    /// It generalises the rule <see cref="DocumentSchematic"/> wrote for the top module alone -- a net
    /// and the cell output driving it -- which gave a row of a block's inside no glitch marks at all.
    /// An assignment that takes time, or computes anything (<c>~a</c>, a bit of a bus, a concatenation),
    /// joins nothing: what it drives is a node of its own.
    /// </remarks>
    public sealed class SignalAliases
    {
        private readonly int[] _node;

        private SignalAliases(int[] node) => _node = node;

        /// <summary>Every signal of <paramref name="design"/> in its node.</summary>
        public static SignalAliases Of(Design design)
        {
            if (design == null)
                throw new ArgumentNullException(nameof(design));

            int count = design.Signals.Count;
            var parent = new int[count];
            for (int i = 0; i < count; i++)
                parent[i] = i;

            int Root(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }

                return i;
            }

            foreach (DriverInfo driver in design.Drivers)
            {
                if (!(driver is AssignDriver assign) || !assign.Delay.IsZero || !assign.Value.TryRead(out SignalId read))
                    continue;

                int target = assign.Target.Index;
                int width = design.Signals[target].Width;
                if (assign.TargetLowest != 0 || assign.TargetWidth != width || design.Signals[read.Index].Width != width)
                    continue;

                // The smaller index stands for the node, so a node is named the same however it was joined.
                int a = Root(read.Index), b = Root(target);
                if (a != b)
                    parent[Math.Max(a, b)] = Math.Min(a, b);
            }

            var node = new int[count];
            for (int i = 0; i < count; i++)
                node[i] = Root(i);
            return new SignalAliases(node);
        }

        /// <summary>How many signals it knows: the design's.</summary>
        public int Count => _node.Length;

        /// <summary>The node <paramref name="signal"/> is in, named by its lowest signal index; -1 for none (a signal the design does not have).</summary>
        public int NodeOf(int signal) => signal >= 0 && signal < _node.Length ? _node[signal] : -1;

        /// <summary>Whether two signals are one node; never for a signal the design does not have.</summary>
        public bool SameNode(int a, int b)
        {
            int node = NodeOf(a);
            return node >= 0 && node == NodeOf(b);
        }
    }
}
