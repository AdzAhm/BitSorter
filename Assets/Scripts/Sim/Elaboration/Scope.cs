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

    /// <summary>An instance's or a generate block's name: not a value, but a scope a hierarchical name can reach into.</summary>
    internal sealed class InstanceEntry : Entry
    {
        /// <summary>The names inside it, once it has been elaborated; null before, or for a named block in a process.</summary>
        public Scope Scope;
    }

    /// <summary>A memory, <c>logic [7:0] mem [0:255]</c>: its words, and the range each word is declared with.</summary>
    internal sealed class MemoryEntry : Entry
    {
        public MemoryInfo Memory;
        public string FullName;

        /// <summary>A word's declared range, <c>[Msb:Lsb]</c>, which a select inside a word counts against.</summary>
        public int Msb, Lsb;
    }

    /// <summary>A typedef's type: its width, signedness and range, and its enum when it is one.</summary>
    internal sealed class TypeEntry : Entry
    {
        public int Width;
        public bool Signed;
        public int Msb, Lsb;

        /// <summary>
        /// The enum, for <c>typedef enum logic [1:0] {IDLE, RUN} state_t;</c>: carried here so a signal
        /// declared <c>state_t s;</c> gets it, as one declared with the enum in place does. Null otherwise.
        /// </summary>
        public EnumInfo Enum;

        /// <summary>Whether the type holds only 0 and 1, as <c>typedef bit [3:0] nibble_t;</c> or an enum on no base does.</summary>
        public bool TwoState;
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

        /// <summary>
        /// Its index in <see cref="Design.Scopes"/>, when it is a scope of the hierarchy: a module's
        /// instance, a generate block, a named block in a process. -1 for one that only gathers names --
        /// a <c>for</c> loop's, an unnamed <c>begin</c> with declarations, a generate loop's genvar -- whose
        /// path is its parent's, and for the compilation unit.
        /// </summary>
        public int Index = -1;

        /// <summary>
        /// The scope of the hierarchy what is declared here belongs to: this one, or the nearest around it
        /// that is one. Its index in <see cref="Design.Scopes"/>, or -1 in the compilation unit.
        /// </summary>
        public int Recorded
        {
            get
            {
                for (Scope s = this; s != null; s = s.Parent)
                    if (s.Index >= 0)
                        return s.Index;
                return -1;
            }
        }

        /// <summary>On the outermost scope: where <c>$time</c> comes from, and the top module's scope, which a full hierarchical name starts at.</summary>
        public Func<Expr> TimeSource;

        /// <inheritdoc cref="TimeSource"/>
        public Scope Top;

        /// <summary>The outermost scope, which every scope leads up to.</summary>
        public Scope Root
        {
            get
            {
                Scope s = this;
                while (s.Parent != null)
                    s = s.Parent;
                return s;
            }
        }

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
