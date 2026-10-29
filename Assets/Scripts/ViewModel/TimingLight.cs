using System;
using System.Collections.Generic;
using BitSorter.Board;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// What of a timing path a drawing lights (Part 14b): the parts it passes through, the branch of each wire it takes, and
    /// the ports it starts and ends at -- of the module drawn, whichever instance of it, from the path's signals named from
    /// the design's top (<see cref="TimingLine.Signals"/>).
    /// </summary>
    /// <remarks>
    /// A path is a run of signals; the drawing knows nets, each driven by one pin and read by the pins its branches end on,
    /// and blocks. The signals it has a net for, and those inside one of its blocks, are the path through this drawing -- a
    /// bit of a bus is the bus, a library cell's output the net it drives, anything under <c>u.</c> the block u -- and
    /// between two of them the part that reads the one and drives the other is the step taken, and the branch into that
    /// part the wire. So a path through a block, drawn from outside, lights the block, as one that ends in it or never
    /// leaves it does; drawn from inside, it lights the gates between the block's own ports. An input port is lit where the
    /// path comes in and an output port where it goes out, never one it only passes -- a register's output that is also a
    /// port is where a path starts, not one it reaches. A signal a drawing has neither a net nor a block for is passed over.
    ///
    /// **On the drawing of code a step is often several parts** (Ahmad, 2026-10-29): one assign is one step of the path,
    /// from a signal it reads to the one it drives, and its drawing is the operators it is made of, joined by wires no signal
    /// holds -- named <c>$</c> and a number, which no signal of the language can be. Where no one part joins the two, the
    /// light follows those wires from the one to the other, through anything but a block or a register, and takes the
    /// longest way, the most parts, as a path through gates is the one through the most of them; into a block through the
    /// slice that takes its bits out of a bus, and out of one through the join that puts them back, the same.
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
        public static TimingLight Of(SchematicLayout layout, IReadOnlyList<string> signals) =>
            Of(layout, signals == null ? null : new[] { signals });

        /// <summary>
        /// What <paramref name="layout"/> lights of every one of <paramref name="paths"/> together: identical paths from other
        /// ends, all lit as one line of the TIMING tab is.
        /// </summary>
        public static TimingLight Of(SchematicLayout layout, IReadOnlyList<IReadOnlyList<string>> paths)
        {
            if (layout == null || paths == null || paths.Count == 0 || layout.DesignPath == null)
                return None;

            var netOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int n = 0; n < layout.Nets.Count; n++)
            {
                netOf[layout.Nets[n].Signal] = n;
                foreach (string alias in layout.Nets[n].Aliases)
                    netOf[alias] = n;
            }

            var drawn = new Dictionary<int, PartLayout>();
            var blocks = new List<(string Prefix, int Part)>();
            string here = layout.InstancePath ?? layout.DesignPath;
            foreach (PartLayout part in layout.Parts)
            {
                drawn[part.Part.Id] = part;
                if (part.Part is BlockPart)
                    blocks.Add((here + "." + part.Part.Name, part.Part.Id));
            }

            var portOfPart = new Dictionary<int, int>();
            for (int p = 0; p < layout.Ports.Count; p++)
                if (layout.Ports[p].Part >= 0)
                    portOfPart[layout.Ports[p].Part] = p;

            // What each part drives, for the way between two of the path's signals through wires no signal holds.
            var drives = new Dictionary<int, List<int>>();
            for (int n = 0; n < layout.Nets.Count; n++)
                foreach (PinRef driver in layout.Nets[n].Drivers)
                {
                    if (!drives.TryGetValue(driver.Part, out List<int> nets))
                        drives[driver.Part] = nets = new List<int>();
                    if (!nets.Contains(n))
                        nets.Add(n);
                }

            var parts = new HashSet<int>();
            var branches = new List<(int, int)>();
            var branchSeen = new HashSet<(int, int)>();
            var ports = new List<int>();
            var portSeen = new HashSet<int>();

            void Branch(int net, int branch)
            {
                if (branchSeen.Add((net, branch)))
                    branches.Add((net, branch));
            }

            void Port(int port)
            {
                if (portSeen.Add(port))
                    ports.Add(port);
            }

            // The longest way from the nets <paramref name="starts"/> to one of the parts <paramref name="targets"/>, every
            // net after the first one no signal holds, lit -- its branches and the parts they go into. False for none.
            bool LightTheWay(IEnumerable<int> starts, ICollection<int> targets, int atLeast)
            {
                List<(int Net, int Branch, int Part)> best = null;
                var known = new Dictionary<int, List<(int, int, int)>>();
                var walking = new HashSet<int>();
                foreach (int start in starts)
                    if (Way(start, targets, known, walking) is List<(int, int, int)> way && (best == null || way.Count > best.Count))
                        best = way;
                if (best == null || best.Count < atLeast)
                    return false;
                foreach ((int net, int branch, int part) in best)
                {
                    Branch(net, branch);
                    parts.Add(part);
                }
                return true;
            }

            List<(int, int, int)> Way(int net, ICollection<int> targets, Dictionary<int, List<(int, int, int)>> known, HashSet<int> walking)
            {
                if (known.TryGetValue(net, out List<(int, int, int)> found))
                    return found;
                if (!walking.Add(net))
                    return null;   // round a loop: no way
                List<(int, int, int)> best = null;
                IReadOnlyList<PinRef> loads = layout.Nets[net].Loads;
                for (int b = 0; b < loads.Count; b++)
                {
                    int load = loads[b].Part;
                    if (!drawn.TryGetValue(load, out PartLayout part))
                        continue;
                    List<(int, int, int)> way = null;
                    if (targets.Contains(load))
                        way = new List<(int, int, int)> { (net, b, load) };
                    else if (!(part.Part is BlockPart) && part.Symbol != PartSymbol.Register && drives.TryGetValue(load, out List<int> outs))
                        foreach (int next in outs)
                            if (IsMadeUp(layout.Nets[next]) && Way(next, targets, known, walking) is List<(int, int, int)> rest
                                && (way == null || rest.Count + 1 > way.Count))
                            {
                                way = new List<(int, int, int)>(rest.Count + 1) { (net, b, load) };
                                way.AddRange(rest);
                            }
                    if (way != null && (best == null || way.Count > best.Count))
                        best = way;
                }
                walking.Remove(net);
                known[net] = best;
                return best;
            }

            // The nets no signal holds that the block <paramref name="block"/> drives: the way out of it to a join.
            List<int> MadeUpOutOf(int block)
            {
                var outs = new List<int>();
                if (drives.TryGetValue(block, out List<int> nets))
                    foreach (int net in nets)
                        if (IsMadeUp(layout.Nets[net]))
                            outs.Add(net);
                return outs;
            }

            foreach (IReadOnlyList<string> signals in paths)
            {
                if (signals == null || signals.Count == 0)
                    continue;

                // The path as this drawing has it: a run of nets and blocks, each once in a row.
                var stops = new List<(bool Block, int Index)>();
                var names = new List<string>(signals.Count);
                foreach (string signal in signals)
                {
                    string full = layout.DesignPath + "." + WithoutBit(signal);
                    names.Add(full);
                    (bool, int) stop;
                    if (netOf.TryGetValue(full, out int net))
                        stop = (false, net);
                    else if (BlockOf(blocks, full) is int block)
                        stop = (true, block);
                    else
                        continue;
                    if (stops.Count == 0 || stops[stops.Count - 1] != stop)
                        stops.Add(stop);
                }

                // An input where it comes in, an output where it goes out.
                for (int p = 0; p < layout.Ports.Count; p++)
                {
                    PortLayout port = layout.Ports[p];
                    int at = names.IndexOf(port.Signal), latest = names.LastIndexOf(port.Signal);
                    if (at >= 0 && (port.Output ? latest > 0 : at < names.Count - 1))
                        Port(p);
                }

                if (stops.Count == 0)
                    continue;

                // A path that starts at a part -- a register launching it -- or in a block starts lit there.
                (bool startBlock, int start) = stops[0];
                if (startBlock)
                    parts.Add(start);
                else if (drawn.ContainsKey(layout.Nets[start].Source.Part))
                    parts.Add(layout.Nets[start].Source.Part);

                for (int k = 0; k + 1 < stops.Count; k++)
                {
                    (bool fromBlock, int from) = stops[k];
                    (bool toBlock, int to) = stops[k + 1];
                    ICollection<int> into = toBlock ? new[] { to } : DriversOf(layout.Nets[to]);
                    if (fromBlock)
                    {
                        // Out of a block: lit already; the net it drives is the next step's -- through the join that
                        // takes its bits into a bus, on a drawing of code.
                        if (!into.Contains(from))
                            LightTheWay(MadeUpOutOf(from), into, 1);
                        if (toBlock)
                            parts.Add(to);
                        continue;
                    }

                    // On a drawing of code, through the parts of an assign, or the slice into a block.
                    if (LightTheWay(new[] { from }, into, 2))
                        continue;

                    // Into the part that reads this net and drives the next, or into the block the path goes inside.
                    int step = toBlock ? to : DriverReading(layout.Nets[to], layout.Nets[from]);
                    if (!drawn.ContainsKey(step))
                        continue;
                    NetLayout net = layout.Nets[from];
                    bool read = false;
                    for (int b = 0; b < net.Loads.Count; b++)
                        if (net.Loads[b].Part == step)
                        {
                            Branch(from, b);
                            read = true;
                        }
                    if (read)
                        parts.Add(step);
                }

                // Its end: the branches into an output port it goes out of, or into the register that captures it.
                (bool endBlock, int end) = stops[stops.Count - 1];
                if (endBlock)
                {
                    parts.Add(end);
                    continue;
                }

                NetLayout last = layout.Nets[end];
                for (int b = 0; b < last.Loads.Count; b++)
                {
                    int load = last.Loads[b].Part;
                    bool intoPort = portOfPart.TryGetValue(load, out int port) && portSeen.Contains(port) && layout.Ports[port].Output;
                    bool intoRegister = drawn.TryGetValue(load, out PartLayout part) && part.Symbol == PartSymbol.Register;
                    if (intoPort || intoRegister)
                    {
                        Branch(end, b);
                        if (intoRegister)
                            parts.Add(load);
                    }
                }
            }

            return new TimingLight(parts, branches, ports);
        }

        /// <summary>The block whose inside <paramref name="signal"/> is, by its part's id: <c>u.q</c> is u's, <c>ha[2].s</c> ha's.</summary>
        private static int? BlockOf(List<(string Prefix, int Part)> blocks, string signal)
        {
            foreach ((string prefix, int part) in blocks)
                if (signal.Length > prefix.Length && signal.StartsWith(prefix, StringComparison.Ordinal)
                    && (signal[prefix.Length] == '.' || signal[prefix.Length] == '['))
                    return part;
            return null;
        }

        /// <summary>
        /// Which part driving <paramref name="to"/> reads <paramref name="from"/>: its one driver, or on a bus (Part 14f) the
        /// tri-state gate the path goes through, which need not be the one the drawing roots the bus at.
        /// </summary>
        private static int DriverReading(NetLayout to, NetLayout from)
        {
            foreach (PinRef driver in to.Drivers)
                foreach (PinRef load in from.Loads)
                    if (load.Part == driver.Part)
                        return driver.Part;
            return to.Source.Part;
        }

        /// <summary>The parts that drive <paramref name="net"/>: its one driver, or a bus's every one.</summary>
        private static HashSet<int> DriversOf(NetLayout net)
        {
            var drivers = new HashSet<int>();
            foreach (PinRef driver in net.Drivers)
                drivers.Add(driver.Part);
            return drivers;
        }

        /// <summary>
        /// A net no signal holds: a value inside an expression on the drawing of code, named <c>$</c> and its number
        /// (<see cref="RtlDrawing"/>) -- no signal of the language starts so.
        /// </summary>
        private static bool IsMadeUp(NetLayout net) => net.Name != null && net.Name.Length > 0 && net.Name[0] == '$';

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
