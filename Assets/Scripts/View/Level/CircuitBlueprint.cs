using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// A port addressed by the cell its node sits on, rather than by node id.
    /// </summary>
    /// <remarks>
    /// The whole reason the blueprint survives a rebuild. Node ids are nothing but Simulation.Add
    /// call order, so they shift whenever the graph is rebuilt after a deletion; a cell does not
    /// move, and only one node can ever occupy one. Addressing wires by cell means a rebuild needs no
    /// id remapping and no tombstones.
    /// </remarks>
    public readonly struct CellPort : IEquatable<CellPort>
    {
        public readonly Vector2Int Cell;
        public readonly bool IsInput;
        public readonly int Index;

        public CellPort(Vector2Int cell, bool isInput, int index)
        {
            Cell = cell;
            IsInput = isInput;
            Index = index;
        }

        public bool Equals(CellPort other) =>
            Cell == other.Cell && IsInput == other.IsInput && Index == other.Index;

        public override bool Equals(object obj) => obj is CellPort other && Equals(other);

        public override int GetHashCode()
        {
            int hash = Cell.GetHashCode();
            hash = (hash * 397) ^ Index;
            return (hash * 397) ^ (IsInput ? 1 : 0);
        }

        public static bool operator ==(CellPort a, CellPort b) => a.Equals(b);
        public static bool operator !=(CellPort a, CellPort b) => !a.Equals(b);

        public override string ToString() => $"{Cell}{(IsInput ? "in" : "out")}{Index}";
    }

    /// <summary>A wire the player drew, from an output port to an input port.</summary>
    public readonly struct BlueprintWire : IEquatable<BlueprintWire>
    {
        public readonly CellPort From;
        public readonly CellPort To;
        public readonly int Delay;

        public BlueprintWire(CellPort from, CellPort to, int delay)
        {
            From = from;
            To = to;
            Delay = delay;
        }

        /// <summary>True if either end sits on this cell, so removing that node must drop this wire.</summary>
        public bool Touches(Vector2Int cell) => From.Cell == cell || To.Cell == cell;

        public bool Equals(BlueprintWire other) =>
            From == other.From && To == other.To && Delay == other.Delay;

        public override bool Equals(object obj) => obj is BlueprintWire other && Equals(other);

        public override int GetHashCode()
        {
            int hash = From.GetHashCode();
            hash = (hash * 397) ^ To.GetHashCode();
            return (hash * 397) ^ Delay;
        }

        public static bool operator ==(BlueprintWire a, BlueprintWire b) => a.Equals(b);
        public static bool operator !=(BlueprintWire a, BlueprintWire b) => !a.Equals(b);

        public override string ToString() => $"{From} -> {To} (delay {Delay})";
    }

    /// <summary>A gate the player put on a cell.</summary>
    public readonly struct GatePlacement
    {
        public readonly Vector2Int Cell;
        public readonly GateKind Kind;

        public GatePlacement(Vector2Int cell, GateKind kind)
        {
            Cell = cell;
            Kind = kind;
        }

        public override string ToString() => $"{GatePalette.Label(Kind)} at {Cell}";
    }

    /// <summary>
    /// Everything the player built, as data. The single authority on the circuit: the Simulation is a
    /// derived artifact rebuilt from this on Run, on Reset, and on every edit.
    /// </summary>
    /// <remarks>
    /// This is what makes Reset trivial. Nothing snapshots the Simulation -- no node state, no port
    /// contents, no in-transit bits, and no preserved ids. Reset simply rebuilds from here, which is
    /// the same call Run makes. Cloning a Simulation instead would mean every future node type,
    /// RegisterNode above all, had to implement deep-copy correctly forever.
    ///
    /// Order is part of the contract. Placements and wires are lists, iterated by index, because a
    /// rebuild assigns node ids by Add order -- so iterating anything unordered here would shuffle
    /// ids between rebuilds and silently desync the layout table.
    /// </remarks>
    public sealed class CircuitBlueprint
    {
        private readonly List<GatePlacement> _placements = new List<GatePlacement>();
        private readonly List<BlueprintWire> _wires = new List<BlueprintWire>();

        /// <summary>In placement order, which is the order they are added on a rebuild.</summary>
        public IReadOnlyList<GatePlacement> Placements => _placements;

        /// <summary>In creation order, which is the order they are connected on a rebuild.</summary>
        public IReadOnlyList<BlueprintWire> Wires => _wires;

        public bool IsEmpty => _placements.Count == 0 && _wires.Count == 0;

        // -----------------------------------------------------------------
        // Placements
        // -----------------------------------------------------------------

        /// <summary>True if the player has already put a gate on this cell.</summary>
        public bool HasPlacementAt(Vector2Int cell) => IndexOfPlacement(cell) >= 0;

        public bool TryGetPlacement(Vector2Int cell, out GateKind kind)
        {
            int index = IndexOfPlacement(cell);

            if (index < 0)
            {
                kind = default;
                return false;
            }

            kind = _placements[index].Kind;
            return true;
        }

        /// <summary>
        /// Records a gate on a cell. The caller has already checked legality through
        /// <see cref="LevelRules"/>; this only guards the invariant that one cell holds one node.
        /// </summary>
        public void Place(Vector2Int cell, GateKind kind)
        {
            if (HasPlacementAt(cell))
                throw new InvalidOperationException($"A gate is already placed at {cell}.");

            _placements.Add(new GatePlacement(cell, kind));
        }

        /// <summary>
        /// Removes the gate on a cell along with every wire touching it, mirroring
        /// Simulation.Remove. Returns false if the cell held nothing.
        /// </summary>
        public bool RemoveAt(Vector2Int cell)
        {
            int index = IndexOfPlacement(cell);

            if (index < 0)
                return false;

            _placements.RemoveAt(index);

            // Backwards, so removing one does not skip the next.
            for (int i = _wires.Count - 1; i >= 0; i--)
            {
                if (_wires[i].Touches(cell))
                    _wires.RemoveAt(i);
            }

            return true;
        }

        /// <summary>How many of a kind are placed. The budget's "used" figure, always computed.</summary>
        public int CountOf(GateKind kind)
        {
            int count = 0;

            for (int i = 0; i < _placements.Count; i++)
            {
                if (_placements[i].Kind == kind)
                    count++;
            }

            return count;
        }

        // -----------------------------------------------------------------
        // Wires
        // -----------------------------------------------------------------

        public void AddWire(BlueprintWire wire) => _wires.Add(wire);

        public bool RemoveWire(BlueprintWire wire) => _wires.Remove(wire);

        /// <summary>
        /// True if this exact output-to-input pair is already wired. Duplicate detection also happens
        /// against the simulation in <see cref="WiringRules"/>; this exists for callers that are
        /// editing the blueprint without a built graph to compare against.
        /// </summary>
        public bool HasWire(CellPort from, CellPort to)
        {
            for (int i = 0; i < _wires.Count; i++)
            {
                if (_wires[i].From == from && _wires[i].To == to)
                    return true;
            }

            return false;
        }

        /// <summary>Discards everything the player built. Used when a level is loaded or restarted.</summary>
        public void Clear()
        {
            _placements.Clear();
            _wires.Clear();
        }

        private int IndexOfPlacement(Vector2Int cell)
        {
            for (int i = 0; i < _placements.Count; i++)
            {
                if (_placements[i].Cell == cell)
                    return i;
            }

            return -1;
        }

        public override string ToString() =>
            $"{_placements.Count} gates, {_wires.Count} wires";
    }
}
