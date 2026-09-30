using System;
using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>A wire, by the two ports it joins: the one identity that survives a rebuild.</summary>
    public readonly struct WireKey : IEquatable<WireKey>
    {
        public readonly CellPort From;
        public readonly CellPort To;

        public WireKey(CellPort from, CellPort to)
        {
            From = from;
            To = to;
        }

        public bool Equals(WireKey other) => From == other.From && To == other.To;
        public override bool Equals(object obj) => obj is WireKey other && Equals(other);
        public override int GetHashCode() => (From.GetHashCode() * 397) ^ To.GetHashCode();
        public override string ToString() => $"{From} -> {To}";
    }

    /// <summary>What happened to an Alt+click on a wire.</summary>
    public enum ProbeToggle
    {
        Added,
        Removed,

        /// <summary>Four wires are in the diagram already, and this was not one of them.</summary>
        Full,
    }

    /// <summary>
    /// The wires the player has put in the timing diagram: up to four, each in a numbered slot.
    /// </summary>
    /// <remarks>
    /// Held by the two ports a wire joins, never by its edge id. Every edit and every RUN rebuilds the
    /// graph, and edge ids follow the order the wires were built in, so removing one wire renumbers
    /// every wire after it. After each rebuild <see cref="Resolve"/> finds the edge now carrying each
    /// wire; a wire that is gone -- deleted, cleared, or a free-play fixture counted away -- takes its
    /// slot with it, and undo does not bring it back.
    ///
    /// Slots stay put: taking W1 out leaves W2 as W2, so the tag on the board and the row in the
    /// diagram do not renumber under the player.
    ///
    /// Pure, so the rules are testable without a scene.
    /// </remarks>
    public sealed class WireProbes
    {
        /// <summary>How many wires the diagram takes.</summary>
        public const int Slots = 4;

        private readonly WireKey?[] _keys = new WireKey?[Slots];
        private readonly int[] _edgeIds = { -1, -1, -1, -1 };

        /// <summary>Moves on whenever a slot changes, so views know when to relabel.</summary>
        public int Revision { get; private set; }

        /// <summary>The edge carrying the wire in a slot, or -1 for an empty slot.</summary>
        public int EdgeIdAt(int slot) => slot >= 0 && slot < Slots ? _edgeIds[slot] : -1;

        /// <summary>Whether a slot holds a wire.</summary>
        public bool IsUsed(int slot) => slot >= 0 && slot < Slots && _keys[slot].HasValue;

        /// <summary>The wire in a slot.</summary>
        public bool TryKeyAt(int slot, out WireKey key)
        {
            key = default;

            if (!IsUsed(slot))
                return false;

            key = _keys[slot].Value;
            return true;
        }

        /// <summary>The slot a wire is in, or -1.</summary>
        public int SlotOf(WireKey key)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (_keys[slot].HasValue && _keys[slot].Value.Equals(key))
                    return slot;
            }

            return -1;
        }

        /// <summary>
        /// Puts a wire in the first free slot, or takes it out if it is in one. <paramref name="edgeId"/>
        /// is the edge carrying it in the graph as it is now.
        /// </summary>
        public ProbeToggle Toggle(WireKey key, int edgeId)
        {
            int existing = SlotOf(key);

            if (existing >= 0)
            {
                _keys[existing] = null;
                _edgeIds[existing] = -1;
                Revision++;
                return ProbeToggle.Removed;
            }

            for (int slot = 0; slot < Slots; slot++)
            {
                if (_keys[slot].HasValue)
                    continue;

                _keys[slot] = key;
                _edgeIds[slot] = edgeId;
                Revision++;
                return ProbeToggle.Added;
            }

            return ProbeToggle.Full;
        }

        /// <summary>Empties every slot: a new level starts with no wires in the diagram.</summary>
        public void Clear()
        {
            bool any = false;

            for (int slot = 0; slot < Slots; slot++)
            {
                any |= _keys[slot].HasValue;
                _keys[slot] = null;
                _edgeIds[slot] = -1;
            }

            if (any)
                Revision++;
        }

        /// <summary>
        /// After a rebuild: finds the edge now carrying each wire, and drops any wire the new graph
        /// does not have.
        /// </summary>
        public void Resolve(SimulationView view, IReadOnlyDictionary<int, Vector2Int> cells)
        {
            bool changed = false;

            for (int slot = 0; slot < Slots; slot++)
            {
                if (!_keys[slot].HasValue)
                    continue;

                int id = EdgeIdOf(view, cells, _keys[slot].Value);

                if (id < 0)
                {
                    _keys[slot] = null;
                    changed = true;
                }

                if (_edgeIds[slot] != id)
                {
                    _edgeIds[slot] = id;
                    changed = true;
                }
            }

            if (changed)
                Revision++;
        }

        /// <summary>The wire an edge carries, by the ports it joins; false for an edge off the layout.</summary>
        public static bool TryKeyOf(Edge edge, IReadOnlyDictionary<int, Vector2Int> cells, out WireKey key)
        {
            key = default;

            if (edge == null || cells == null
                || !cells.TryGetValue(edge.Source.Owner.Id, out Vector2Int from)
                || !cells.TryGetValue(edge.Target.Owner.Id, out Vector2Int to))
            {
                return false;
            }

            key = new WireKey(new CellPort(from, false, edge.Source.Index), new CellPort(to, true, edge.Target.Index));
            return true;
        }

        /// <summary>The edge carrying a wire, or -1.</summary>
        public static int EdgeIdOf(SimulationView view, IReadOnlyDictionary<int, Vector2Int> cells, WireKey key)
        {
            for (int id = 0; id < view.EdgeCount; id++)
            {
                if (TryKeyOf(view.GetEdge(id), cells, out WireKey found) && found.Equals(key))
                    return id;
            }

            return -1;
        }
    }
}
