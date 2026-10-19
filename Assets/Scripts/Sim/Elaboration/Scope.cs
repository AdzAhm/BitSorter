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

        /// <summary>The enum it was declared with, for its methods (<c>s.name()</c>); null for any other type.</summary>
        public EnumInfo Enum;

        /// <summary>Whether its type holds only 0 and 1: what an enum's <c>next()</c> gives for a value that is no member.</summary>
        public bool TwoState;

        /// <summary>
        /// What its bits are (Part L4): a packed array's elements, or a struct's members, which a select or
        /// a member access finds its way through. Never null; a plain vector is <c>[Msb:Lsb]</c> of bits.
        /// </summary>
        public PackedType Type;

        /// <summary>
        /// A named event (Part L5): a bit every trigger turns over, which <c>@(e)</c> waits on and nothing may
        /// read as a value, and <see cref="Triggered"/>, the time of its last trigger plus one.
        /// </summary>
        public bool IsEvent;

        /// <inheritdoc cref="IsEvent"/>
        public SignalId Triggered;
    }

    /// <summary>
    /// A function or a task (Part L2), declared where it was written. Its own signals -- arguments, result
    /// and locals -- are made once, the first time a call needs them (<see cref="Frame"/>), and every
    /// call uses the same ones: static storage, as the standard's default is, with an automatic one
    /// starting each call afresh.
    /// </summary>
    internal sealed class SubroutineEntry : Entry
    {
        public SubroutineDeclaration Declaration;

        /// <summary>Where it was declared: its body's names are looked up from here.</summary>
        public Scope Scope;

        /// <summary>A function's result, as its return type gives it; width 1 for a task or a void function.</summary>
        public int Width = 1;
        public bool Signed;

        public bool IsTask => Declaration.IsTask;
        public bool IsVoid => Declaration.IsTask || Declaration.IsVoid;

        /// <summary>Its own signals' scope, once made: the arguments, the result (named as the function) and the locals.</summary>
        public Scope Frame;

        /// <summary>The arguments, in order, with their directions and defaults.</summary>
        public List<(SignalEntry Signal, PortDirection Direction, Expression Default)> Arguments;

        /// <summary>The result's signal; null for a task or a void function.</summary>
        public SignalEntry Result;

        /// <summary>Every signal of its own, the arguments and the result among them.</summary>
        public List<SignalEntry> Own;

        /// <summary>Compiled to run inside an expression, once a call there has asked for it.</summary>
        public FunctionInfo Compiled;

        /// <summary>Whether it is being compiled now: a call met then is a call back into itself.</summary>
        public bool Compiling;

        /// <summary>Whether its signals or its statements could not be made, which has been said once already.</summary>
        public bool Failed;

        /// <summary>Compiles it for an expression's call, the elaborator's own work: null if it cannot be.</summary>
        public Func<SubroutineEntry, FunctionInfo> Compile;

        /// <summary>Every place a call of it is written into a process, for <c>disable</c> (Part L5).</summary>
        public readonly List<BlockRange> Inlined = new List<BlockRange>();
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

        /// <summary>For a named block or fork in a process, where it is, for <c>disable</c> (Part L5); null for an instance or a generate block.</summary>
        public List<BlockRange> Ranges;
    }

    /// <summary>A memory, <c>logic [7:0] mem [0:255]</c>: its words, and the range each word is declared with.</summary>
    internal sealed class MemoryEntry : Entry
    {
        public MemoryInfo Memory;
        public string FullName;

        /// <summary>A word's declared range, <c>[Msb:Lsb]</c>, which a select inside a word counts against.</summary>
        public int Msb, Lsb;

        /// <summary>What a word's bits are, as <see cref="SignalEntry.Type"/> says for a signal.</summary>
        public PackedType Type;

        /// <summary>
        /// The unpacked dimensions, the first the outermost (Part L4): <c>mem [0:3][0:7]</c> is four rows of
        /// eight words, kept as thirty-two words row by row. One dimension for a memory of one.
        /// </summary>
        public (int Left, int Right)[] Dims;
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

        /// <summary>What its bits are: a struct's members, a packed array's elements, or a plain vector.</summary>
        public PackedType Type;
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

        /// <summary>
        /// The function or task a call names, past anything else of that name in between: inside a
        /// function its own name is its result, and a call of it is still a call.
        /// </summary>
        public SubroutineEntry LookupSubroutine(string name)
        {
            for (Scope s = this; s != null; s = s.Parent)
                if (s._names.TryGetValue(name, out Entry entry) && entry is SubroutineEntry subroutine)
                    return subroutine;
            return null;
        }

        /// <summary>A name declared in this scope itself, not an enclosing one.</summary>
        public Entry Own(string name) => _names.TryGetValue(name, out Entry entry) ? entry : null;
    }
}
