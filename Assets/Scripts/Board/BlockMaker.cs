using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// A block made from a selection (Part 12b): the parts chosen, and the wires between them, moved into a new module,
    /// and one instance of it put where they were -- the circuit doing exactly what it did. Unity-free, and checked by
    /// running the circuit before and after.
    /// </summary>
    /// <remarks>
    /// - **Every net that crosses the selection's edge becomes a port** of the new module: an input where the net is driven
    ///   outside the selection, an output where it is driven inside. Named for the net where its name can be a port's,
    ///   else <c>in1</c>, <c>out1</c>... A net driven on both sides -- a tri-state bus, say -- cannot be one port's, and
    ///   is refused with its name.
    /// - **Inside**, the parts keep their names and their places; a net wholly inside keeps its wires as drawn, and a net
    ///   that crosses is wired from its port to each of its pins inside. The ports stand left (inputs) and right
    ///   (outputs) of the parts, in the order the nets were found.
    /// - **Outside**, the selection is taken away, with every wire on it, and the instance stands at the middle of where it
    ///   was; each crossing net is joined to the instance's pin of its port again, once for each piece of it that taking
    ///   the selection away left apart.
    /// - **One step** for undo (<see cref="BoardEditor.Group"/>): made in full, or, refused anywhere on the way, not at all.
    /// - The module's own ports cannot be moved into a block -- they are what the module is -- and are refused.
    /// </remarks>
    public static class BlockMaker
    {
        /// <summary>The name a block made from a selection is given when none is: <c>block1</c>, <c>block2</c>...</summary>
        public static string FreeName(Project project)
        {
            for (int i = 1; ; i++)
            {
                string name = "block" + i;
                if (project.Find(name) == null)
                    return name;
            }
        }

        /// <summary>
        /// Moves <paramref name="parts"/> of <paramref name="module"/> into a new module called <paramref name="name"/> (or a
        /// free name), and puts an instance of it in their place, as one step; the result's id is the instance's.
        /// </summary>
        public static EditResult Make(BoardEditor editor, string module, IReadOnlyCollection<int> parts, string name = null)
        {
            if (editor == null)
                throw new ArgumentNullException(nameof(editor));
            Module m = editor.Project.Find(module);
            if (m == null)
                return EditResult.Refused($"There is no module called {module}.");
            if (parts == null || parts.Count == 0)
                return EditResult.Refused("Select the parts to make a block of.");

            var chosen = new HashSet<int>(parts);
            foreach (int id in chosen)
            {
                Part part = m.FindPart(id);
                if (part == null)
                    return EditResult.Refused("A part selected is no longer there.");
                if (part is PortPart)
                    return EditResult.Refused($"{part.Name} is a port of {module}: it stays where it is. Select the parts between the ports.");
            }

            name = string.IsNullOrWhiteSpace(name) ? FreeName(editor.Project) : name.Trim();
            Netlist nets = Netlist.Of(editor.Project, m);

            // The nets the selection touches: wholly inside, or crossing its edge.
            var crossing = new List<Crossing>();
            var inside = new HashSet<int>();   // wires of nets wholly inside
            var used = new HashSet<string>(StringComparer.Ordinal);   // names taken in the new module: its parts', then its ports'
            foreach (Part part in m.Parts)
                if (chosen.Contains(part.Id) && part.Name != null)
                    used.Add(part.Name);
            int ins = 0, outs = 0;
            foreach (Net net in nets.Nets)
            {
                bool touches = false, outsidePin = false;
                foreach (PinRef pin in net.Pins)
                {
                    if (chosen.Contains(pin.Part))
                        touches = true;
                    else
                        outsidePin = true;
                }

                if (!touches)
                    continue;

                if (!outsidePin)
                {
                    foreach (int wire in net.Wires)
                        inside.Add(wire);
                    continue;
                }

                bool drivenInside = false, drivenOutside = false;
                foreach (PinRef driver in net.Drivers)
                {
                    if (chosen.Contains(driver.Part))
                        drivenInside = true;
                    else
                        drivenOutside = true;
                }

                if (drivenInside && drivenOutside)
                    return EditResult.Refused($"The net {net.Name} is driven inside the selection and outside it: one port cannot carry it both ways.");

                PortDirection direction = drivenInside ? PortDirection.Output : PortDirection.Input;
                string port = BoardRules.IsValidName(net.Name) && used.Add(net.Name) ? net.Name : null;
                if (port == null)
                {
                    do port = direction == PortDirection.Input ? "in" + ++ins : "out" + ++outs;
                    while (!used.Add(port));
                }

                crossing.Add(new Crossing(net, port, direction));
            }

            return editor.Group(() =>
            {
                EditResult made = editor.AddModule(name);
                if (!made.Done)
                    return made;

                // Inside: the parts where they stood, their own wires, and a port for each crossing net.
                var ids = new Dictionary<int, int>();
                double left = double.MaxValue, right = double.MinValue, top = double.MinValue, bottom = double.MaxValue;
                foreach (Part part in m.Parts)
                {
                    if (!chosen.Contains(part.Id))
                        continue;
                    Placement at = m.Layout.PlacementOf(part.Id);
                    EditResult added = editor.Add(name, part with { Id = 0 }, at);
                    if (!added.Done)
                        return added;
                    ids[part.Id] = added.Id;
                    left = Math.Min(left, at.Position.X);
                    right = Math.Max(right, at.Position.X);
                    top = Math.Max(top, at.Position.Y);
                    bottom = Math.Min(bottom, at.Position.Y);
                }

                var wires = new Dictionary<int, int>();
                foreach (Wire wire in m.Wires)
                {
                    if (!inside.Contains(wire.Id))
                        continue;
                    EditResult joined = editor.Connect(name, Moved(wire.From, ids, wires), Moved(wire.To, ids, wires));
                    if (!joined.Done)
                        return joined;
                    wires[wire.Id] = joined.Id;
                }

                int inputs = 0, outputs = 0;
                foreach (Crossing c in crossing)
                {
                    bool input = c.Direction == PortDirection.Input;
                    double y = top - (input ? inputs++ : outputs++);
                    var at = new Placement(input ? left - 3 : right + 3, y);
                    EditResult port = editor.Add(name, new PortPart { Name = c.Port, Direction = c.Direction, Width = Math.Max(1, c.Net.Width) }, at);
                    if (!port.Done)
                        return port;

                    string pin = input ? "out" : "in";
                    foreach (PinRef p in c.Net.Pins)
                    {
                        if (!chosen.Contains(p.Part))
                            continue;
                        EditResult joined = editor.Connect(name, Endpoint.At(port.Id, pin), Endpoint.At(ids[p.Part], p.Pin));
                        if (!joined.Done)
                            return joined;
                    }
                }

                // Outside: the selection taken away, the instance where it was, and each crossing net joined to it again.
                EditResult removed = editor.RemoveParts(module, chosen);
                if (!removed.Done)
                    return removed;

                var middle = new Placement(Math.Round((left + right) * 0.5), Math.Round((top + bottom) * 0.5));
                EditResult block = editor.Add(module, new BlockPart { Module = name }, middle);
                if (!block.Done)
                    return block;

                foreach (Crossing c in crossing)
                {
                    var pin = new PinRef(block.Id, c.Port);
                    foreach (PinRef p in c.Net.Pins)
                    {
                        if (chosen.Contains(p.Part))
                            continue;

                        // A piece already joined to the instance's pin -- through another pin of it -- is joined once.
                        Netlist now = Netlist.Of(editor.Project, editor.Project.Find(module));
                        Net here = now.NetOf(p), there = now.NetOf(pin);
                        if (here != null && ReferenceEquals(here, there))
                            continue;

                        EditResult joined = editor.Connect(module, Endpoint.At(pin), Endpoint.At(p));
                        if (!joined.Done)
                            return joined;
                    }
                }

                return EditResult.Ok(block.Id);
            });
        }

        /// <summary>A wire's end as it is in the new module: its part's or its tapped wire's new id.</summary>
        private static Endpoint Moved(Endpoint end, Dictionary<int, int> parts, Dictionary<int, int> wires) =>
            end.IsTap ? Endpoint.OnWire(wires[end.TapOf]) : Endpoint.At(parts[end.Pin.Part], end.Pin.Pin);

        private readonly struct Crossing
        {
            public Crossing(Net net, string port, PortDirection direction)
            {
                Net = net;
                Port = port;
                Direction = direction;
            }

            public Net Net { get; }
            public string Port { get; }
            public PortDirection Direction { get; }
        }
    }
}
