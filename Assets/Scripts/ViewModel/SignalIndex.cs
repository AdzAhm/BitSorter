using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Sim;

namespace BitSorter.ViewModel
{
    /// <summary>What a scope of the design is to someone looking for a signal in it.</summary>
    public enum ScopeNodeKind
    {
        /// <summary>The testbench around the design.</summary>
        Testbench,

        /// <summary>The design itself: the board's top module.</summary>
        Design,

        /// <summary>An instance of a module of the board's own -- a block.</summary>
        Block,

        /// <summary>An instance of a library cell: a gate with delay, printed as one. Hidden unless asked for.</summary>
        Cell,

        /// <summary>A generate block.</summary>
        Generate,

        /// <summary>A named block in a process.</summary>
        Named,
    }

    /// <summary>One scope of the design, its scopes inside it and the signals it declares.</summary>
    public sealed class ScopeNode
    {
        internal ScopeNode(string name, string path, ScopeNodeKind kind, string module, ScopeNode parent)
        {
            Name = name;
            Path = path;
            Kind = kind;
            Module = module;
            Parent = parent;
        }

        /// <summary>Its own name in its parent: <c>dut</c>, <c>h1</c>, <c>slice[3]</c>.</summary>
        public string Name { get; }

        /// <summary>The prefix its signals' names have.</summary>
        public string Path { get; }

        public ScopeNodeKind Kind { get; }

        /// <summary>The module it is an instance of; null for a generate or a named block.</summary>
        public string Module { get; }

        public ScopeNode Parent { get; }

        /// <summary>The scopes inside it, in the order the design made them.</summary>
        public IReadOnlyList<ScopeNode> Children => _children;

        /// <summary>The signals it declares that a waveform can show, by index in the design, in declaration order.</summary>
        public IReadOnlyList<int> Signals => _signals;

        internal readonly List<ScopeNode> _children = new List<ScopeNode>();
        internal readonly List<int> _signals = new List<int>();

        /// <summary>Whether it is, or is inside, a library cell.</summary>
        public bool InCell
        {
            get
            {
                for (ScopeNode s = this; s != null; s = s.Parent)
                    if (s.Kind == ScopeNodeKind.Cell)
                        return true;
                return false;
            }
        }

        public override string ToString() => $"{Kind} {Path}";
    }

    /// <summary>A signal a search found, and how well it matched: lower is better.</summary>
    public readonly struct SignalHit
    {
        public SignalHit(int signal, string key, int score, int depth)
        {
            Signal = signal;
            Key = key;
            Score = score;
            Depth = depth;
        }

        /// <summary>The signal, by index in the design.</summary>
        public int Signal { get; }

        /// <summary>Its key (<see cref="SignalIndex.KeyOf"/>).</summary>
        public string Key { get; }

        public int Score { get; }

        /// <summary>How deep it is: the dots in its key.</summary>
        public int Depth { get; }

        public override string ToString() => $"{Key} ({Score})";
    }

    /// <summary>
    /// Every signal of a design a waveform can show, as the design is built and as a list to search:
    /// the testbench, the design and every block in it, each with what it declares.
    /// </summary>
    /// <remarks>
    /// **Keyed by where the signal is in the design, not by its full name** (<see cref="KeyOf"/>):
    /// <c>h1.s</c> rather than <c>full_adder_tb.dut.h1.s</c>, and a testbench's own signal under
    /// <c>tb.</c>. A row keeps its key, so renaming the board's top module, which renames the design path
    /// every name begins with, leaves every row where it was (<see cref="WaveList.Resolve"/>).
    ///
    /// **Left out:** a process's working variables (<see cref="SignalInfo.Local"/>), <c>$time</c>, a
    /// memory's words (Part 12 or 13 shows memories), and, unless asked for, the inside of a library cell
    /// -- every gate with delay prints as one, so a full adder of two half adders would list three pins
    /// for each of its five gates beside the signals anyone means.
    ///
    /// **Search** (<see cref="Search"/>) takes terms apart on spaces, and a signal matches when every
    /// term is in its key, ignoring case; a term with <c>*</c> in it is a pattern instead, matched
    /// against the whole key or its last name. The best come first: a last name that is the term, then
    /// one that starts with it, a key that starts with it, any key holding it -- then the shallower, then
    /// in order.
    /// </remarks>
    public sealed class SignalIndex
    {
        /// <summary>What a testbench's own signal is keyed under, so it never reads as one of the design's.</summary>
        public const string TestbenchPrefix = "tb.";

        private readonly Design _design;
        private readonly string[] _keys;
        private readonly bool[] _listed;
        private readonly ScopeNode[] _scopeOf;
        private readonly Dictionary<string, int> _byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly string _testbench;

        private SignalIndex(Design design, string designPath, ScopeNode root, string testbench, ScopeNode[] scopeOf)
        {
            _design = design;
            DesignPath = designPath;
            Root = root;
            _testbench = testbench;
            _scopeOf = scopeOf;
            _keys = new string[design.Signals.Count];
            _listed = new bool[design.Signals.Count];

            for (int i = 0; i < design.Signals.Count; i++)
            {
                SignalInfo info = design.Signals[i];
                string key = Key(info.Name);
                _keys[i] = key;
                _listed[i] = key != null && !info.Local && info.Memory == null && !(design.Time.HasValue && design.Time.Value.Index == i)
                    && _scopeOf[i] != null;
                if (_listed[i] && !_byKey.ContainsKey(key))
                    _byKey.Add(key, i);
            }
        }

        /// <summary>
        /// The index of <paramref name="design"/>, whose own module's signals are under
        /// <paramref name="designPath"/> (<see cref="Board.CompiledProject.DesignPath"/>), with the scopes
        /// that are instances of <paramref name="cellModules"/> (<see cref="Board.CompiledProject.Cells"/>) as cells.
        /// </summary>
        public static SignalIndex Of(Design design, string designPath, IEnumerable<string> cellModules)
        {
            if (design == null)
                throw new ArgumentNullException(nameof(design));
            if (string.IsNullOrEmpty(designPath))
                throw new ArgumentException("A design path names where the design's own signals are.", nameof(designPath));

            var cells = new HashSet<string>(cellModules ?? Array.Empty<string>(), StringComparer.Ordinal);
            var nodes = new ScopeNode[design.Scopes.Count];
            ScopeNode root = null;
            string testbench = null;

            for (int i = 0; i < design.Scopes.Count; i++)
            {
                ScopeInfo scope = design.Scopes[i];
                ScopeNode parent = scope.Parent >= 0 ? nodes[scope.Parent] : null;
                ScopeNodeKind kind;
                switch (scope.Kind)
                {
                    case ScopeKind.Top:
                        kind = scope.Path == designPath ? ScopeNodeKind.Design : ScopeNodeKind.Testbench;
                        if (kind == ScopeNodeKind.Testbench)
                            testbench = scope.Path;
                        break;
                    case ScopeKind.Instance:
                        kind = scope.Path == designPath ? ScopeNodeKind.Design
                            : scope.Module != null && cells.Contains(scope.Module) ? ScopeNodeKind.Cell
                            : ScopeNodeKind.Block;
                        break;
                    case ScopeKind.Generate:
                        kind = ScopeNodeKind.Generate;
                        break;
                    default:
                        kind = ScopeNodeKind.Named;
                        break;
                }

                var node = new ScopeNode(scope.Name, scope.Path, kind, scope.Module, parent);
                nodes[i] = node;
                if (parent != null)
                    parent._children.Add(node);
                else if (root == null)
                    root = node;
            }

            var scopeOf = new ScopeNode[design.Signals.Count];
            for (int i = 0; i < design.Signals.Count; i++)
            {
                int scope = design.Signals[i].Scope;
                scopeOf[i] = scope >= 0 && scope < nodes.Length ? nodes[scope] : null;
            }

            var index = new SignalIndex(design, designPath, root, testbench, scopeOf);
            for (int i = 0; i < design.Signals.Count; i++)
                if (index._listed[i])
                    scopeOf[i]._signals.Add(i);
            return index;
        }

        /// <summary>Where the design's own signals are: every key is relative to it.</summary>
        public string DesignPath { get; }

        /// <summary>The top of the hierarchy: the testbench, or the design run alone. Null for a design with no scopes.</summary>
        public ScopeNode Root { get; }

        /// <summary>How many signals the design has: the range a signal index is in.</summary>
        public int Count => _keys.Length;

        /// <summary>
        /// A signal's key: its name below the design path -- <c>a</c>, <c>h1.s</c> -- or, for one of the
        /// testbench's own, <c>tb.</c> and its name below the testbench. Null for a signal under neither.
        /// </summary>
        public string KeyOf(int signal) => signal >= 0 && signal < _keys.Length ? _keys[signal] : null;

        /// <summary>The signal a key names, as this design has it; false for one it does not list.</summary>
        public bool TryFind(string key, out int signal)
        {
            signal = -1;
            return key != null && _byKey.TryGetValue(key, out signal);
        }

        /// <summary>Whether a search or the add list offers the signal: never a working variable, $time or a memory's word, and a cell's pins only when <paramref name="includeCells"/>.</summary>
        public bool IsListed(int signal, bool includeCells = false) =>
            signal >= 0 && signal < _listed.Length && _listed[signal] && (includeCells || !_scopeOf[signal].InCell);

        /// <summary>Whether a signal is a port of the scope that declares it.</summary>
        public bool IsPort(int signal) => signal >= 0 && signal < _keys.Length && _design.Signals[signal].Port != null;

        /// <summary>The scope a signal was declared in, or null.</summary>
        public ScopeNode ScopeOf(int signal) => signal >= 0 && signal < _scopeOf.Length ? _scopeOf[signal] : null;

        /// <summary>A signal's short name: its key, and a bus's range as declared -- <c>h1.s</c>, <c>s[3:0]</c>.</summary>
        public string ShortName(int signal) => KeyOf(signal) + Range(signal);

        /// <summary>
        /// A signal's full name as ModelSim writes it, slashes between the scopes: <c>/full_adder_tb/dut/h1/s</c>,
        /// and a bus's range.
        /// </summary>
        public string FullName(int signal)
        {
            if (signal < 0 || signal >= _keys.Length)
                return null;
            return "/" + _design.Signals[signal].Name.Replace('.', '/') + Range(signal);
        }

        private string Range(int signal)
        {
            SignalInfo info = _design.Signals[signal];
            return info.Width > 1 || info.Msb != info.Lsb
                ? "[" + info.Msb.ToString(CultureInfo.InvariantCulture) + ":" + info.Lsb.ToString(CultureInfo.InvariantCulture) + "]"
                : "";
        }

        /// <summary>
        /// Every listed signal matching <paramref name="query"/>, best first, added to
        /// <paramref name="into"/> (which it does not clear). An empty query matches every listed signal,
        /// in order.
        /// </summary>
        public void Search(string query, List<SignalHit> into, bool includeCells = false)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            string[] terms = (query ?? "").ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = new List<SignalHit>();

            for (int i = 0; i < _keys.Length; i++)
            {
                if (!IsListed(i, includeCells))
                    continue;

                string key = _keys[i].ToLowerInvariant();
                string leaf = key.Substring(key.LastIndexOf('.') + 1);
                int score = 0;
                bool all = true;

                foreach (string term in terms)
                {
                    int s = Score(term, key, leaf);
                    if (s < 0)
                    {
                        all = false;
                        break;
                    }

                    score += s;
                }

                if (all)
                    hits.Add(new SignalHit(i, _keys[i], score, Depth(_keys[i])));
            }

            hits.Sort((a, b) =>
            {
                int byScore = a.Score.CompareTo(b.Score);
                if (byScore != 0) return byScore;
                int byDepth = a.Depth.CompareTo(b.Depth);
                return byDepth != 0 ? byDepth : string.CompareOrdinal(a.Key, b.Key);
            });

            into.AddRange(hits);
        }

        /// <summary>How well one term matches: 0 a last name that is it, 1 a last name starting with it, 2 a key starting with it, 3 a key holding it; -1 no match.</summary>
        private static int Score(string term, string key, string leaf)
        {
            if (term.IndexOf('*') >= 0)
            {
                if (Glob(term, leaf)) return 1;
                return Glob(term, key) ? 2 : -1;
            }

            if (leaf == term) return 0;
            if (leaf.StartsWith(term, StringComparison.Ordinal)) return 1;
            if (key.StartsWith(term, StringComparison.Ordinal)) return 2;
            return key.IndexOf(term, StringComparison.Ordinal) >= 0 ? 3 : -1;
        }

        /// <summary>Whether <paramref name="text"/> is the whole of <paramref name="pattern"/>, each <c>*</c> standing for any run of characters.</summary>
        public static bool Glob(string pattern, string text)
        {
            int p = 0, t = 0, star = -1, mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && pattern[p] != '*' && pattern[p] == text[t])
                {
                    p++;
                    t++;
                }
                else if (p < pattern.Length && pattern[p] == '*')
                {
                    star = p++;
                    mark = t;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    t = ++mark;
                }
                else
                {
                    return false;
                }
            }

            while (p < pattern.Length && pattern[p] == '*')
                p++;
            return p == pattern.Length;
        }

        private static int Depth(string key)
        {
            int dots = 0;
            foreach (char c in key)
                if (c == '.')
                    dots++;
            return dots;
        }

        private string Key(string name)
        {
            if (name.Length > DesignPath.Length && name[DesignPath.Length] == '.' && name.StartsWith(DesignPath, StringComparison.Ordinal))
                return name.Substring(DesignPath.Length + 1);
            if (_testbench != null && name.Length > _testbench.Length && name[_testbench.Length] == '.' && name.StartsWith(_testbench, StringComparison.Ordinal))
                return TestbenchPrefix + name.Substring(_testbench.Length + 1);
            return null;
        }
    }
}
