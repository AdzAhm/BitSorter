using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>What a name means where it is used: a signal, a constant, a genvar or a type.</summary>
    internal abstract class Entry
    {
        public SourceLocation Declared;
    }

    /// <summary>A signal of the flattened design, with the range it was declared with.</summary>
    internal sealed class SignalEntry : Entry
    {
        public SignalId Signal;
        public string FullName;
        public int Width;

        /// <summary>The declared range, <c>[Msb:Lsb]</c>, which a select is counted against.</summary>
        public int Msb, Lsb;

        public bool Signed;
        public SignalKind Kind;

        /// <summary>The port's direction, when the signal is a port of its module.</summary>
        public PortDirection? Port;
    }

    /// <summary>A parameter, a localparam or an enum's name: a value known before the run.</summary>
    internal sealed class ConstantEntry : Entry
    {
        public LogicVec Value;
    }

    /// <summary>A genvar: a constant inside a generate loop, and nothing outside one.</summary>
    internal sealed class GenvarEntry : Entry
    {
        public LogicVec? Value;
    }

    /// <summary>An instance's or a generate block's name: taken, but not a value.</summary>
    internal sealed class InstanceEntry : Entry
    {
    }

    /// <summary>A typedef's type: its width, signedness and range.</summary>
    internal sealed class TypeEntry : Entry
    {
        public int Width;
        public bool Signed;
        public int Msb, Lsb;
    }

    /// <summary>
    /// The names visible at one place in a module: the module's own, and inside a generate block the
    /// block's own over them. A name is looked up from the innermost block outward to the module,
    /// never into the instance that contains the module.
    /// </summary>
    internal sealed class Scope
    {
        private readonly Dictionary<string, Entry> _names = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public Scope(string path, Scope parent, ModuleDeclaration module)
        {
            Path = path;
            Parent = parent;
            Module = module;
        }

        /// <summary>The hierarchical name things declared here get as their prefix: <c>alu64.slice[3]</c>.</summary>
        public string Path { get; }

        /// <summary>The enclosing block's scope; null for the module's own.</summary>
        public Scope Parent { get; }

        public ModuleDeclaration Module { get; }

        /// <summary>How many generate constructs this scope has met, for naming unnamed ones genblk1, genblk2...</summary>
        public int GenerateConstructs;

        public bool TryDeclare(string name, Entry entry)
        {
            if (_names.ContainsKey(name))
                return false;
            _names[name] = entry;
            return true;
        }

        /// <summary>Replaces what a name means here: a genvar's value as a loop goes round.</summary>
        public void Set(string name, Entry entry) => _names[name] = entry;

        public Entry Lookup(string name)
        {
            for (Scope s = this; s != null; s = s.Parent)
                if (s._names.TryGetValue(name, out Entry entry))
                    return entry;
            return null;
        }

        /// <summary>A name declared in this scope itself, not an enclosing one.</summary>
        public Entry Own(string name) => _names.TryGetValue(name, out Entry entry) ? entry : null;
    }
}
