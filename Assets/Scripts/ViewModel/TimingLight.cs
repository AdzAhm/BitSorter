using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// What of a timing path a drawing lights (Part 14b): the parts it passes through, the branch of each wire it takes, and
    /// the ports it starts and ends at -- of the module drawn, whichever instance of it, from the path's signals named from
    /// the design's top (<see cref="TimingLine.Signals"/>).
    /// </summary>
    /// <remarks>
    /// A path is a run of signals; the drawing knows nets, each driven by one pin and read by the pins its branches end on.
    /// The signals it has a net for are the path through this drawing -- a bit of a bus is the bus, a library cell's output
    /// the net it drives -- and between two of them the part that reads the one and drives the other is the step taken, and
    /// the branch into that part the wire. So a path through a block, drawn from outside, lights the block; drawn from inside,
    /// it lights the gates between the block's own ports. A signal a drawing has no net for is passed over.
    /// </remarks>
    public sealed class TimingLight
    {
        private TimingLight(HashSet<int> parts, List<(int Net, int Branch)> branches, List<int> ports)
        {
            Parts = parts;
            Branches = branches;
            Ports = ports;
        }

        /// <summary>Nothing lit.</summary>
        public static readonly TimingLight None = new TimingLight(new HashSet<int>(), new List<(int, int)>(), new List<int>());

        /// <summary>The parts lit, by id.</summary>
        public IReadOnlyCollection<int> Parts { get; }

        /// <summary>Each wire lit: a net of the layout and one of its branches, by index.</summary>
        public IReadOnlyList<(int Net, int Branch)> Branches { get; }

        /// <summary>The ports lit, by their index in the layout.</summary>
        public IReadOnlyList<int> Ports { get; }

        public bool IsEmpty => Parts.Count == 0 && Branches.Count == 0 && Ports.Count == 0;

        /// <summary>What <paramref name="layout"/> lights of the path through <paramref name="signals"/>.</summary>
        public static TimingLight Of(SchematicLayout layout, IReadOnlyList<string> signals)
        {
            if (layout == null || signals == null || signals.Count == 0 || layout.DesignPath == null)
                return None;

            var netOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int n = 0; n < layout.Nets.Count; n++)
            {
                netOf[layout.Nets[n].Signal] = n;
                foreach (string alias in layout.Nets[n].Aliases)
                    netOf[alias] = n;
            }

            var drawn = new HashSet<int>();
            foreach (PartLayout part in layout.Parts)
                drawn.Add(part.Part.Id);

            // The path's signals as the drawing names them, and the nets it has for them, in order, each once in a row.
            var names = new HashSet<string>(StringComparer.Ordinal);
            var nets = new List<int>();
            foreach (string signal in signals)
            {
                string full = layout.DesignPath + "." + WithoutBit(signal);
                names.Add(full);
                if (netOf.TryGetValue(full, out int net) && (nets.Count == 0 || nets[nets.Count - 1] != net))
                    nets.Add(net);
            }

            var parts = new HashSet<int>();
            var branches = new List<(int, int)>();
            var ports = new List<int>();
            for (int p = 0; p < layout.Ports.Count; p++)
                if (names.Contains(layout.Ports[p].Signal))
                    ports.Add(p);

            if (nets.Count > 0)
            {
                // A path that starts at a part -- a register launching it -- starts lit there.
                int start = layout.Nets[nets[0]].Source.Part;
                if (drawn.Contains(start))
                    parts.Add(start);
            }

            for (int k = 0; k + 1 < nets.Count; k++)
            {
                NetLayout from = layout.Nets[nets[k]];
                int step = layout.Nets[nets[k + 1]].Source.Part;
                if (!drawn.Contains(step))
                    continue;
                bool read = false;
                for (int b = 0; b < from.Loads.Count; b++)
                    if (from.Loads[b].Part == step)
                    {
                        branches.Add((nets[k], b));
                        read = true;
                    }
                if (read)
                    parts.Add(step);
            }

            if (nets.Count > 0)
            {
                // Its end: the branches into an output port, or into the register that captures it.
                int last = nets[nets.Count - 1];
                NetLayout end = layout.Nets[last];
                for (int b = 0; b < end.Loads.Count; b++)
                {
                    int load = end.Loads[b].Part;
                    if (!drawn.Contains(load) ? ports.Count > 0 : IsRegister(layout, load))
                    {
                        branches.Add((last, b));
                        if (drawn.Contains(load))
                            parts.Add(load);
                    }
                }
            }

            return new TimingLight(parts, branches, ports);
        }

        private static bool IsRegister(SchematicLayout layout, int part)
        {
            foreach (PartLayout p in layout.Parts)
                if (p.Part.Id == part)
                    return p.Symbol == PartSymbol.Register;
            return false;
        }

        /// <summary><c>c[1]</c> as the bus it is a bit of, <c>c</c>: a drawing has a net for a bus, not one a bit.</summary>
        private static string WithoutBit(string signal)
        {
            if (signal.Length == 0 || signal[signal.Length - 1] != ']')
                return signal;
            int open = signal.LastIndexOf('[');
            return open > 0 ? signal.Substring(0, open) : signal;
        }
    }
}
