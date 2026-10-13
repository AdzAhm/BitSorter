using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>Where a net's name came from.</summary>
    public enum NetNaming
    {
        /// <summary>A port is on it, and the net is the port.</summary>
        Port,

        /// <summary>The player named one of its wires.</summary>
        Given,

        /// <summary>Made from the pin that drives it, or the first that reads it: <c>g1_y</c>.</summary>
        Made,
    }

    /// <summary>
    /// A net: everything joined by wires, which is one signal in the printed SystemVerilog. Worked out
    /// from the document every time, never stored (ADR-7).
    /// </summary>
    public sealed class Net
    {
        internal Net(int index) => Index = index;

        public int Index { get; }

        /// <summary>The pins on it, in the order of the module's parts and then of each part's pins.</summary>
        public IReadOnlyList<PinRef> Pins => _pins;

        /// <summary>The wires that make it.</summary>
        public IReadOnlyList<int> Wires => _wires;

        /// <summary>The pins that drive it. More than one is an error; none leaves it x.</summary>
        public IReadOnlyList<PinRef> Drivers => _drivers;

        /// <summary>The ports on it: the first is the one it is named after; an output after that is fed by an assign.</summary>
        public IReadOnlyList<PortPart> Ports => _ports;

        /// <summary>Its name in the printed SystemVerilog, unique in the module.</summary>
        public string Name { get; internal set; }

        public NetNaming Naming { get; internal set; }

        /// <summary>The width of its pins; when they disagree, the widest, and <see cref="WidthsAgree"/> is false.</summary>
        public int Width { get; internal set; }

        public bool WidthsAgree { get; internal set; } = true;

        internal readonly List<PinRef> _pins = new List<PinRef>();
        internal readonly List<int> _wires = new List<int>();
        internal readonly List<PinRef> _drivers = new List<PinRef>();
        internal readonly List<PortPart> _ports = new List<PortPart>();

        public override string ToString() => Name;
    }

    /// <summary>
    /// A module's nets, worked out from its wires: every pin of every part is on exactly one net, alone
    /// if nothing is wired to it.
    /// </summary>
    /// <remarks>
    /// **Naming**, in three passes so that no made-up name can take one somebody chose: first every net
    /// with a port on it takes the port's name (an input's over an output's); then every net the player
    /// named takes that name; then the rest are named after the pin that drives them -- <c>g1_y</c> --
    /// or, with no driver, the first pin that reads them. A name made from a pin stays put when other
    /// parts are added or taken away, so a signal picked out in the waveform keeps its name across
    /// edits. Should a made name already be in use it takes <c>_2</c>, <c>_3</c> ...
    ///
    /// **Problems** -- a wire ending on a pin or wire that is not there -- are collected rather than
    /// thrown, so a document that is wrong in some way can still be drawn and explained.
    /// </remarks>
    public sealed class Netlist
    {
        private readonly Dictionary<PinRef, Net> _byPin = new Dictionary<PinRef, Net>();
        private readonly Dictionary<int, Net> _byWire = new Dictionary<int, Net>();
        private readonly List<Net> _nets = new List<Net>();
        private readonly List<string> _problems = new List<string>();

        public IReadOnlyList<Net> Nets => _nets;

        /// <summary>Wires that end on a pin or a wire that does not exist, in words.</summary>
        public IReadOnlyList<string> Problems => _problems;

        public Net NetOf(PinRef pin) => _byPin.TryGetValue(pin, out Net net) ? net : null;
        public Net NetOfWire(int wire) => _byWire.TryGetValue(wire, out Net net) ? net : null;

        public Net Named(string name)
        {
            foreach (Net net in _nets)
                if (string.Equals(net.Name, name, StringComparison.Ordinal))
                    return net;
            return null;
        }

        public static Netlist Of(Project project, Module module)
        {
            var netlist = new Netlist();
            netlist.Build(project, module);
            return netlist;
        }

        private void Build(Project project, Module module)
        {
            // Union-find over every pin and every wire.
            var pins = new List<(PinRef Ref, Pin Pin, Part Part)>();
            var pinIndex = new Dictionary<PinRef, int>();

            foreach (Part part in module.Parts)
            {
                foreach (Pin pin in part.Pins(project))
                {
                    var key = new PinRef(part.Id, pin.Name);
                    if (pinIndex.ContainsKey(key))
                        continue;
                    pinIndex[key] = pins.Count;
                    pins.Add((key, pin, part));
                }
            }

            var wireIndex = new Dictionary<int, int>();
            foreach (Wire wire in module.Wires)
                wireIndex[wire.Id] = pins.Count + wireIndex.Count;

            var parent = new int[pins.Count + wireIndex.Count];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a != b)
                    parent[Math.Max(a, b)] = Math.Min(a, b);
            }

            foreach (Wire wire in module.Wires)
            {
                int self = wireIndex[wire.Id];
                foreach (Endpoint end in new[] { wire.From, wire.To })
                {
                    if (end.IsTap)
                    {
                        if (end.TapOf == wire.Id)
                            _problems.Add($"Wire {wire.Id} taps itself.");
                        else if (wireIndex.TryGetValue(end.TapOf, out int tapped))
                            Union(self, tapped);
                        else
                            _problems.Add($"Wire {wire.Id} taps wire {end.TapOf}, which is not there.");
                    }
                    else if (pinIndex.TryGetValue(end.Pin, out int pin))
                    {
                        Union(self, pin);
                    }
                    else
                    {
                        Part part = module.FindPart(end.Pin.Part);
                        _problems.Add(part == null
                            ? $"Wire {wire.Id} ends on part {end.Pin.Part}, which is not there."
                            : $"Wire {wire.Id} ends on {part.Name}.{end.Pin.Pin}, which {part.Name} does not have.");
                    }
                }
            }

            // One net per root, in the order their first pin (or wire) comes.
            var byRoot = new Dictionary<int, Net>();
            Net NetFor(int node)
            {
                int root = Find(node);
                if (!byRoot.TryGetValue(root, out Net net))
                {
                    net = new Net(_nets.Count);
                    byRoot[root] = net;
                    _nets.Add(net);
                }

                return net;
            }

            for (int i = 0; i < pins.Count; i++)
            {
                (PinRef key, Pin pin, Part part) = pins[i];
                Net net = NetFor(i);
                net._pins.Add(key);
                _byPin[key] = net;

                if (net._pins.Count == 1)
                    net.Width = pin.Width;
                else if (pin.Width != net.Width)
                {
                    net.WidthsAgree = false;
                    net.Width = Math.Max(net.Width, pin.Width);
                }

                if (pin.Direction == PinDirection.Out)
                    net._drivers.Add(key);
                if (part is PortPart port)
                    net._ports.Add(port);
            }

            foreach (Wire wire in module.Wires)
            {
                Net net = NetFor(wireIndex[wire.Id]);
                net._wires.Add(wire.Id);
                _byWire[wire.Id] = net;
                if (net._pins.Count == 0 && net.Width == 0)
                    net.Width = 1;
            }

            Name(module);
        }

        private void Name(Module module)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (Part part in module.Parts)
                if (!(part is PortPart) && part.Name != null)
                    taken.Add(part.Name);

            // 1. Ports: an input's name over an output's, so `assign y = a;` feeds an output from an input.
            foreach (Net net in _nets)
            {
                if (net._ports.Count == 0)
                    continue;

                PortPart chosen = net._ports.Find(p => p.Direction == PortDirection.Input) ?? net._ports[0];
                net._ports.Remove(chosen);
                net._ports.Insert(0, chosen);
                Give(net, chosen.Name, NetNaming.Port, taken);
            }

            // 2. Names the player gave, the lowest wire's first.
            var wireNames = new Dictionary<int, string>();
            foreach (Wire wire in module.Wires)
                if (!string.IsNullOrEmpty(wire.NetName))
                    wireNames[wire.Id] = wire.NetName;

            foreach (Net net in _nets)
            {
                if (net.Name != null)
                    continue;

                string given = null;
                int lowest = int.MaxValue;
                foreach (int wire in net._wires)
                {
                    if (wireNames.TryGetValue(wire, out string name) && wire < lowest)
                    {
                        given = name;
                        lowest = wire;
                    }
                }

                if (given != null)
                    Give(net, given, NetNaming.Given, taken);
            }

            // 3. Made from a pin: the driver's, or the first reader's.
            var parts = new Dictionary<int, Part>();
            foreach (Part part in module.Parts)
                parts[part.Id] = part;

            foreach (Net net in _nets)
            {
                if (net.Name != null)
                    continue;

                PinRef from = net._drivers.Count > 0 ? net._drivers[0] : net._pins.Count > 0 ? net._pins[0] : default;
                parts.TryGetValue(from.Pin == null ? -1 : from.Part, out Part part);
                string made = part != null ? part.Name + "_" + from.Pin : "w" + (net._wires.Count > 0 ? net._wires[0] : net.Index);
                Give(net, made, NetNaming.Made, taken);
            }
        }

        private static void Give(Net net, string name, NetNaming naming, HashSet<string> taken)
        {
            string unique = name;
            for (int n = 2; taken.Contains(unique); n++)
                unique = name + "_" + n;

            taken.Add(unique);
            net.Name = unique;
            net.Naming = naming;
        }
    }
}
