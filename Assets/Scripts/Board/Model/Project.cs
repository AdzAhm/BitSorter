using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// A drawn module: its parts, the wires between them, and beside them how they are drawn. Its ports
    /// are its <see cref="PortPart"/>s, inputs first, as the course writes a header. Or a written one
    /// (Part 12d): its <see cref="Source"/>, and nothing drawn.
    /// </summary>
    public sealed record Module
    {
        public string Name { get; init; }
        public string Comment { get; init; }

        /// <summary>
        /// The module written as SystemVerilog, or null for a drawn one (Part 12d). A written module has no parts and no
        /// wires: a block of it takes its pins from its header (<see cref="WrittenModules"/>), and it is compiled as written.
        /// </summary>
        public string Source { get; init; }

        public bool IsWritten => Source != null;
        public IReadOnlyList<Part> Parts { get; init; } = Array.Empty<Part>();
        public IReadOnlyList<Wire> Wires { get; init; } = Array.Empty<Wire>();
        public Layout Layout { get; init; } = Layout.Empty;

        /// <summary>The next id to give a part or a wire: they share one count, and an id is never given twice.</summary>
        public int NextId { get; init; } = 1;

        public Part FindPart(int id)
        {
            foreach (Part part in Parts)
                if (part.Id == id)
                    return part;
            return null;
        }

        public Part FindPart(string name)
        {
            foreach (Part part in Parts)
                if (string.Equals(part.Name, name, StringComparison.Ordinal))
                    return part;
            return null;
        }

        public Wire FindWire(int id)
        {
            foreach (Wire wire in Wires)
                if (wire.Id == id)
                    return wire;
            return null;
        }

        /// <summary>The ports, inputs first and then outputs, each in the order they were placed.</summary>
        public IReadOnlyList<PortPart> Ports
        {
            get
            {
                var ports = new List<PortPart>();
                foreach (PortDirection direction in new[] { PortDirection.Input, PortDirection.Output })
                    foreach (Part part in Parts)
                        if (part is PortPart port && port.Direction == direction)
                            ports.Add(port);
                return ports;
            }
        }
    }

    public sealed record ProjectSettings
    {
        /// <summary>
        /// The unit time is counted in, one of <see cref="SystemVerilog.TimeUnits.All"/>; time itself is a
        /// whole number of them (invariant 7), so changing it relabels time and rescales nothing.
        /// </summary>
        public string TimeUnit { get; init; } = SystemVerilog.TimeUnits.Default;

        /// <summary>The rise and fall a newly placed gate takes: a library cell's, as the course's cells default to.</summary>
        public long DefaultGateDelay { get; init; } = 1;
    }

    /// <summary>
    /// A whole document: its modules, which one is the design, and the stimulus its testbench is made
    /// from. Immutable, like everything under it: <see cref="BoardEditor"/> makes a new one for every
    /// edit, sharing whatever the edit did not touch.
    /// </summary>
    public sealed record Project
    {
        public string Name { get; init; }
        public IReadOnlyList<Module> Modules { get; init; } = Array.Empty<Module>();

        /// <summary>The module that is the design; the testbench, when there is a stimulus, instantiates it.</summary>
        public string Top { get; init; }

        /// <summary>What drives the top module's inputs, or null for none.</summary>
        public Stimulus Stimulus { get; init; }

        public ProjectSettings Settings { get; init; } = new ProjectSettings();

        public Module Find(string name)
        {
            foreach (Module module in Modules)
                if (string.Equals(module.Name, name, StringComparison.Ordinal))
                    return module;
            return null;
        }

        public Module TopModule => Find(Top);

        /// <summary>The project with <paramref name="module"/> in place of the module of the same name, or added at the end.</summary>
        public Project WithModule(Module module)
        {
            var modules = new List<Module>(Modules.Count + 1);
            bool replaced = false;
            foreach (Module m in Modules)
            {
                if (!replaced && string.Equals(m.Name, module.Name, StringComparison.Ordinal))
                {
                    modules.Add(module);
                    replaced = true;
                }
                else
                {
                    modules.Add(m);
                }
            }

            if (!replaced)
                modules.Add(module);

            return this with { Modules = modules };
        }

        /// <summary>A new project: one empty module of the same name, which is the top.</summary>
        public static Project New(string name) => new Project
        {
            Name = name,
            Modules = new[] { new Module { Name = name } },
            Top = name,
        };
    }
}
