using System;
using System.Collections.Generic;

namespace BitSorter.Sim
{
    /// <summary>One bit a bit of an expression's value depends on, and how a change of it passes (Part 14a).</summary>
    internal readonly struct BitSource
    {
        public BitSource(int node, Sense sense)
        {
            Node = node;
            Sense = sense;
        }

        /// <summary>The bit, as the timing graph numbers it: a signal's first node and the bit's place in it.</summary>
        public readonly int Node;

        public readonly Sense Sense;
    }

    /// <summary>
    /// The bits one bit of a value depends on, each once: a bit reached two ways, the same way round both times, keeps
    /// its sense; reached both ways round, it is <see cref="Sense.Either"/>. Built once and then only read, so one set can
    /// stand for many bits.
    /// </summary>
    internal sealed class SourceSet
    {
        public static readonly SourceSet Empty = new SourceSet();

        private readonly List<BitSource> _items = new List<BitSource>();
        private Dictionary<int, int> _index;

        public int Count => _items.Count;

        public BitSource this[int i] => _items[i];

        /// <summary>A set of the one bit <paramref name="node"/>, passed the same way round.</summary>
        public static SourceSet Of(int node)
        {
            var set = new SourceSet();
            set.Add(node, Sense.Positive);
            return set;
        }

        /// <summary>The <paramref name="count"/> bits from <paramref name="node"/> on, each through <paramref name="sense"/>: a signal whole.</summary>
        public static SourceSet Range(int node, int count, Sense sense)
        {
            var set = new SourceSet();
            for (int b = 0; b < count; b++)
                set.Add(node + b, sense);
            return set;
        }

        /// <summary>
        /// The bits of every one of <paramref name="parts"/>, each passed on through what its sense says: a set through
        /// <see cref="Sense.Negative"/> is every bit the other way round.
        /// </summary>
        public static SourceSet Union(params (SourceSet Set, Sense Through)[] parts)
        {
            // One part through Positive is that part: shared, never copied.
            int nonEmpty = 0;
            SourceSet only = Empty;
            foreach ((SourceSet set, Sense through) in parts)
                if (set.Count > 0)
                {
                    nonEmpty++;
                    only = through == Sense.Positive ? set : null;
                }
            if (nonEmpty == 0)
                return Empty;
            if (nonEmpty == 1 && only != null)
                return only;

            var union = new SourceSet();
            foreach ((SourceSet set, Sense through) in parts)
                for (int i = 0; i < set.Count; i++)
                    union.Add(set[i].Node, Compose(through, set[i].Sense));
            return union;
        }

        /// <summary>Every bit of this set, passed on through <paramref name="through"/>.</summary>
        public SourceSet Through(Sense through) => through == Sense.Positive ? this : Union((this, through));

        private void Add(int node, Sense sense)
        {
            // A short set is searched; a long one is indexed, so a memory's thousands of bits are not searched for each.
            if (_index == null && _items.Count >= 16)
            {
                _index = new Dictionary<int, int>(_items.Count * 2);
                for (int i = 0; i < _items.Count; i++)
                    _index[_items[i].Node] = i;
            }

            int at = -1;
            if (_index != null)
            {
                if (!_index.TryGetValue(node, out at))
                    at = -1;
            }
            else
            {
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].Node == node)
                    {
                        at = i;
                        break;
                    }
            }

            if (at < 0)
            {
                _index?.Add(node, _items.Count);
                _items.Add(new BitSource(node, sense));
            }
            else if (_items[at].Sense != sense)
            {
                _items[at] = new BitSource(node, Sense.Either);
            }
        }

        /// <summary>A change passed through <paramref name="outer"/> after <paramref name="inner"/>: two inversions cancel.</summary>
        public static Sense Compose(Sense outer, Sense inner)
        {
            if (outer == Sense.Either || inner == Sense.Either)
                return Sense.Either;
            return outer == inner ? Sense.Positive : Sense.Negative;
        }
    }

    /// <summary>
    /// What each bit of an expression's value depends on (Part 14a): the bits of signals it reads, and whether a change
    /// passes the same way round, the other, or either -- worked out from the expression's operators, so <c>v[1]</c> read
    /// into <c>v[2]</c> is one bit to another, never a vector to itself, and <c>~a</c> inverts where <c>a &amp; b</c> does
    /// not. What no rule here knows is every bit to every bit, either way.
    /// </summary>
    /// <remarks>
    /// A value wider than <see cref="MostBitsApart"/> is not followed bit by bit: every bit of it depends on everything
    /// any bit does. A select, a part-select, a concatenation and a widening only move bits: a value made of nothing else
    /// is <see cref="Routing"/>, and a zero-delay assignment of one joins names of the same wires rather than being logic.
    /// </remarks>
    internal sealed class BitDeps
    {
        /// <summary>The widest value followed bit by bit.</summary>
        public const int MostBitsApart = 4096;

        private readonly SourceSet[] _bits;
        private readonly SourceSet _all;
        private SourceSet _union;

        private BitDeps(int width, SourceSet[] bits, SourceSet all, bool routing)
        {
            Width = width;
            _bits = bits;
            _all = all;
            Routing = routing;
        }

        public int Width { get; }

        /// <summary>Whether the value is the bits it reads moved about and nothing more, no constant's bit set by logic.</summary>
        public bool Routing { get; }

        /// <summary>Whether every bit depends on the same set: a value too wide to follow, or one made by an operator that mixes all its bits.</summary>
        public bool IsUniform => _bits == null;

        /// <summary>What bit <paramref name="bit"/> depends on; nothing past the value's width.</summary>
        public SourceSet Of(int bit)
        {
            if (bit < 0 || bit >= Width)
                return SourceSet.Empty;
            return _bits == null ? _all : _bits[bit] ?? SourceSet.Empty;
        }

        /// <summary>What any bit depends on.</summary>
        public SourceSet Union
        {
            get
            {
                if (_union != null)
                    return _union;
                if (_bits == null)
                    return _union = _all;

                var parts = new List<(SourceSet, Sense)>();
                SourceSet last = null;
                foreach (SourceSet bit in _bits)
                    if (bit != null && !ReferenceEquals(bit, last))
                    {
                        parts.Add((bit, Sense.Positive));
                        last = bit;
                    }
                return _union = SourceSet.Union(parts.ToArray());
            }
        }

        /// <summary>A value of <paramref name="width"/> bits, bit <c>i</c> depending on <paramref name="bit"/>(<c>i</c>).</summary>
        public static BitDeps Bits(int width, bool routing, Func<int, SourceSet> bit)
        {
            if (width > MostBitsApart)
            {
                // Too wide to follow: each bit on everything, which the bits taken one by one would have unioned to.
                var parts = new List<(SourceSet, Sense)>();
                SourceSet last = null;
                for (int i = 0; i < width; i++)
                {
                    SourceSet set = bit(i);
                    if (set.Count > 0 && !ReferenceEquals(set, last))
                        parts.Add((set, Sense.Positive));
                    last = set;
                }
                return new BitDeps(width, null, SourceSet.Union(parts.ToArray()), routing: false);
            }

            var bits = new SourceSet[Math.Max(0, width)];
            for (int i = 0; i < bits.Length; i++)
                bits[i] = bit(i);
            return new BitDeps(width, bits, null, routing);
        }

        /// <summary>A value every bit of which depends on <paramref name="all"/>: an operator that mixes its operands' bits.</summary>
        public static BitDeps Uniform(int width, SourceSet all) => new BitDeps(Math.Max(0, width), null, all ?? SourceSet.Empty, routing: false);

        /// <summary>
        /// A value whose bit <c>i</c> depends on bits 0 to <c>i</c> of <paramref name="operands"/>, either way: a sum's, as a
        /// carry runs from the bottom up.
        /// </summary>
        public static BitDeps Upward(int width, params BitDeps[] operands)
        {
            if (width > MostBitsApart)
                return Uniform(width, Mixed(operands));

            var bits = new SourceSet[Math.Max(0, width)];
            SourceSet below = SourceSet.Empty;
            for (int i = 0; i < bits.Length; i++)
            {
                var parts = new (SourceSet, Sense)[operands.Length + 1];
                parts[0] = (below, Sense.Positive);
                for (int k = 0; k < operands.Length; k++)
                    parts[k + 1] = (operands[k].Of(i), Sense.Either);
                bits[i] = below = SourceSet.Union(parts);
            }
            return new BitDeps(width, bits, null, routing: false);
        }

        /// <summary>Every bit of every one of <paramref name="operands"/>, either way.</summary>
        public static SourceSet Mixed(params BitDeps[] operands)
        {
            var parts = new (SourceSet, Sense)[operands.Length];
            for (int k = 0; k < operands.Length; k++)
                parts[k] = (operands[k].Union, Sense.Either);
            return SourceSet.Union(parts);
        }

        /// <summary>
        /// What the reads of <paramref name="e"/> are, every bit of every signal it reads to every bit of its value, either
        /// way: what an expression no rule knows depends on.
        /// </summary>
        public static BitDeps Unknown(Expr e, int[] first)
        {
            var reads = new HashSet<int>();
            e.CollectReads(reads);
            return Uniform(e.Width, Everything(reads, first, Sense.Either));
        }

        /// <summary>Every bit of every signal in <paramref name="signals"/>, through <paramref name="sense"/>.</summary>
        public static SourceSet Everything(IEnumerable<int> signals, int[] first, Sense sense)
        {
            var parts = new List<(SourceSet, Sense)>();
            foreach (int s in signals)
                parts.Add((SourceSet.Range(first[s], first[s + 1] - first[s], sense), Sense.Positive));
            return SourceSet.Union(parts.ToArray());
        }
    }
}
