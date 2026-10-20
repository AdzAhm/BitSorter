using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using EngineGate = BitSorter.Sim.GateKind;

namespace BitSorter.ViewModel
{
    /// <summary>What a catalogue entry places.</summary>
    public enum CatalogKind { Input, Output, Clock, Reset, Constant, Gate, Mux, Register, Splitter, Joiner, Lamp, Block }

    /// <summary>One entry of the parts strip: what it is called there, and the part it places.</summary>
    public sealed class CatalogEntry
    {
        internal CatalogEntry(string key, string label, CatalogKind kind, EngineGate gate = EngineGate.And, string module = null)
        {
            Key = key;
            Label = label;
            Kind = kind;
            Gate = gate;
            Module = module;
        }

        /// <summary>A name for it that does not change: what a test, or a key, asks for.</summary>
        public string Key { get; }

        /// <summary>What the strip says.</summary>
        public string Label { get; }

        public CatalogKind Kind { get; }

        /// <summary>For a gate, which.</summary>
        public EngineGate Gate { get; }

        /// <summary>For a block, the module it is an instance of.</summary>
        public string Module { get; }

        public override string ToString() => Key;
    }

    /// <summary>
    /// The parts a module can be built from, as the parts strip offers them (Part 10a), and what placing one
    /// does to the document: logic here, so Part 11's panel offers the same entries and places them the same way.
    /// </summary>
    /// <remarks>
    /// - **An input of the top module is given a value at once**: 0 from the start, so the waveform can change
    ///   it straight away (Part 9b-3) and nothing reads z for want of a testbench step.
    /// - **A clock and a reset are inputs**, as the course's testbenches drive them: a clock is given a
    ///   <see cref="ClockTrack"/> of <see cref="ClockPeriod"/>, starting low; a reset is held high from the start
    ///   until <see cref="ResetUntil"/>, the clock's first falling edge, when the course changes an input. Each
    ///   is named clk and rst while those are free.
    /// - **A gate takes the project's delay** (<see cref="ProjectSettings.DefaultGateDelay"/>), rise and fall, as
    ///   a library cell does; two inputs, one for NOT and BUF.
    /// - **Every module of the project but the one being built is a block**, placed as an instance of it.
    /// - Placing is one step for undo, made in full or not at all (<see cref="BoardEditor.Group"/>).
    /// </remarks>
    public static class PartsCatalog
    {
        /// <summary>The period a clock is placed with, in the project's units.</summary>
        public const long ClockPeriod = 10;

        /// <summary>When a reset placed falls: the clock's first falling edge.</summary>
        public const long ResetUntil = ClockPeriod;

        private static readonly CatalogEntry[] Fixed =
        {
            new CatalogEntry("input", "INPUT", CatalogKind.Input),
            new CatalogEntry("output", "OUTPUT", CatalogKind.Output),
            new CatalogEntry("clock", "CLOCK", CatalogKind.Clock),
            new CatalogEntry("reset", "RESET", CatalogKind.Reset),
            new CatalogEntry("constant", "CONST", CatalogKind.Constant),
            new CatalogEntry("and", "AND", CatalogKind.Gate, EngineGate.And),
            new CatalogEntry("or", "OR", CatalogKind.Gate, EngineGate.Or),
            new CatalogEntry("xor", "XOR", CatalogKind.Gate, EngineGate.Xor),
            new CatalogEntry("nand", "NAND", CatalogKind.Gate, EngineGate.Nand),
            new CatalogEntry("nor", "NOR", CatalogKind.Gate, EngineGate.Nor),
            new CatalogEntry("xnor", "XNOR", CatalogKind.Gate, EngineGate.Xnor),
            new CatalogEntry("not", "NOT", CatalogKind.Gate, EngineGate.Not),
            new CatalogEntry("buf", "BUF", CatalogKind.Gate, EngineGate.Buf),
            new CatalogEntry("bufif1", "BUFIF1", CatalogKind.Gate, EngineGate.Bufif1),
            new CatalogEntry("mux", "MUX", CatalogKind.Mux),
            new CatalogEntry("register", "REG", CatalogKind.Register),
            new CatalogEntry("splitter", "SPLIT", CatalogKind.Splitter),
            new CatalogEntry("joiner", "JOIN", CatalogKind.Joiner),
            new CatalogEntry("lamp", "LAMP", CatalogKind.Lamp),
        };

        /// <summary>The entries for building <paramref name="module"/> of <paramref name="project"/>: the fixed ones, then a block of every other module.</summary>
        public static IReadOnlyList<CatalogEntry> For(Project project, string module)
        {
            var entries = new List<CatalogEntry>(Fixed);
            foreach (Module m in project.Modules)
                if (!string.Equals(m.Name, module, StringComparison.Ordinal))
                    entries.Add(new CatalogEntry("block:" + m.Name, m.Name, CatalogKind.Block, module: m.Name));
            return entries;
        }

        /// <summary>The entry of this key among <see cref="For"/>'s, or null.</summary>
        public static CatalogEntry Find(Project project, string module, string key)
        {
            foreach (CatalogEntry entry in For(project, module))
                if (entry.Key == key)
                    return entry;
            return null;
        }

        /// <summary>The part an entry places, before the document gives it an id and, when it has none, a name.</summary>
        public static Part Make(Project project, string module, CatalogEntry entry)
        {
            long delay = project.Settings?.DefaultGateDelay ?? 1;
            Module m = project.Find(module);

            switch (entry.Kind)
            {
                case CatalogKind.Input: return new PortPart { Direction = PortDirection.Input };
                case CatalogKind.Output: return new PortPart { Direction = PortDirection.Output };
                case CatalogKind.Clock: return new PortPart { Direction = PortDirection.Input, Name = Free(m, "clk") };
                case CatalogKind.Reset: return new PortPart { Direction = PortDirection.Input, Name = Free(m, "rst") };
                case CatalogKind.Constant: return new ConstantPart { Value = LogicVec.FromUInt64(0, 1) };
                case CatalogKind.Gate:
                    int inputs = entry.Gate == EngineGate.Not || entry.Gate == EngineGate.Buf ? 1 : 2;
                    return new GatePart { Kind = entry.Gate, Inputs = inputs, Rise = delay, Fall = delay };
                case CatalogKind.Mux: return new MuxPart();
                case CatalogKind.Register: return new RegisterPart();
                case CatalogKind.Splitter: return new SplitterPart();
                case CatalogKind.Joiner: return new JoinerPart();
                case CatalogKind.Lamp: return new LampPart();
                default: return new BlockPart { Module = entry.Module };
            }
        }

        /// <summary>
        /// Places <paramref name="entry"/> in <paramref name="module"/> at <paramref name="at"/>, and -- for an input
        /// of the top module -- what drives it: 0, a clock, or a reset's pulse. One step, or refused as a whole.
        /// </summary>
        public static EditResult Place(BoardEditor editor, string module, CatalogEntry entry, Placement at)
        {
            Project project = editor.Project;
            return editor.Group(() =>
            {
                EditResult placed = editor.Add(module, Make(project, module, entry), at);
                if (!placed.Done)
                    return placed;

                bool topInput = module == editor.Project.Top &&
                                (entry.Kind == CatalogKind.Input || entry.Kind == CatalogKind.Clock || entry.Kind == CatalogKind.Reset);
                if (!topInput)
                    return placed;

                string port = editor.Project.Find(module).FindPart(placed.Id).Name;
                Stimulus stimulus = editor.Project.Stimulus ?? new Stimulus();
                if (entry.Kind == CatalogKind.Clock)
                {
                    stimulus = stimulus with { Clocks = With(stimulus.Clocks, new ClockTrack { Port = port, Period = ClockPeriod }) };
                }
                else
                {
                    Step[] steps = entry.Kind == CatalogKind.Reset
                        ? new[] { new Step(0, LogicVec.FromUInt64(1, 1)), new Step(ResetUntil, LogicVec.FromUInt64(0, 1)) }
                        : new[] { new Step(0, LogicVec.FromUInt64(0, 1)) };
                    stimulus = stimulus with { Tracks = With(stimulus.Tracks, new Track { Port = port, Steps = steps }) };
                }

                EditResult driven = editor.SetStimulus(stimulus);
                return driven.Done ? placed : driven;
            });
        }

        /// <summary>The name asked for, when nothing in the module has it; null for the document's own naming.</summary>
        private static string Free(Module module, string name)
        {
            if (module == null)
                return name;
            if (module.FindPart(name) != null)
                return null;
            foreach (Wire wire in module.Wires)
                if (wire.NetName == name)
                    return null;
            return name;
        }

        private static IReadOnlyList<T> With<T>(IReadOnlyList<T> list, T item)
        {
            var copy = new List<T>(list.Count + 1);
            copy.AddRange(list);
            copy.Add(item);
            return copy;
        }
    }
}
