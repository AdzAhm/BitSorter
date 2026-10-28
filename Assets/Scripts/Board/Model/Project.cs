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

        /// <summary>
        /// The course's homework file the module was imported from (Part 13e), when it said where the student's code goes:
        /// EXPORT FOR SUBMISSION hands it back as given, the student's code between its markers. Null for none.
        /// </summary>
        public ModuleTemplate Template { get; init; }
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

    /// <summary>A course template a module came from (Part 13e): the file's bare name, and every character of it as given.</summary>
    public sealed record ModuleTemplate
    {
        public string File { get; init; }
        public string Text { get; init; } = "";

        /// <summary>
        /// The module's own text as the import first made it of the file (<see cref="Module.Source"/>), the student's code
        /// checked against outside the markers: the import moves an include and drops a <c>`timescale</c>, as it does again
        /// nowhere, so the file's own text is not what the module was given.
        /// </summary>
        public string Given { get; init; } = "";
    }

    /// <summary>One of a project's other files: an include or a memory image, by a bare name, and its text.</summary>
    public sealed record ProjectFile
    {
        public string Name { get; init; }
        public string Text { get; init; } = "";
    }

    public sealed record ProjectSettings
    {
        /// <summary>
        /// The unit time is counted in, one of <see cref="SystemVerilog.TimeUnits.All"/>; time itself is a
        /// whole number of them (invariant 7), so changing it relabels time and rescales nothing.
        /// </summary>
        public string TimeUnit { get; init; } = SystemVerilog.TimeUnits.Default;

        /// <summary>
        /// The rise and fall a newly placed gate takes, as the course's cells default to -- under a gate library
        /// (<see cref="Library"/>), its cell's instead -- and what COURSE CELLS starts every cell at.
        /// </summary>
        public long DefaultGateDelay { get; init; } = 1;

        /// <summary>
        /// The gate library the design is held to (Part 14d): the gates it may be built of and their delays, constants or
        /// none, and a budget of gates. Null for none: any gate, at any delay.
        /// </summary>
        public GateLibrary Library { get; init; }

        /// <summary>
        /// The exercise this document answers, by its id, or null for a circuit of the student's own (Part 15d). Its test,
        /// its library, its top and the blocks it stocks are then the exercise's, and no edit changes them
        /// (<see cref="BoardEditor"/>).
        /// </summary>
        public string Exercise { get; init; }

        /// <summary>What a register placed starts at: an exercise's <c>registerStart</c>, or null for x (Part 15d).</summary>
        public SystemVerilog.LogicVec? RegisterStart { get; init; }
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

        /// <summary>
        /// A written module that is the testbench (Part 13b): the course's own, imported, run as written instead of one
        /// made from <see cref="Stimulus"/>. It instantiates <see cref="Top"/>; null when the stimulus drives the design.
        /// While there is one, the stimulus holds only its <see cref="Stimulus.End"/> -- where the run is shown to -- and
        /// no tracks: the bench drives the inputs.
        /// </summary>
        public string Bench { get; init; }

        public ProjectSettings Settings { get; init; } = new ProjectSettings();

        /// <summary>
        /// The project's other files (Part 13b): what a written module <c>`include</c>s and the memory images
        /// <c>$readmemh</c> reads, each by a bare name, as the course keeps them beside its sources.
        /// </summary>
        public IReadOnlyList<ProjectFile> Files { get; init; } = Array.Empty<ProjectFile>();

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
