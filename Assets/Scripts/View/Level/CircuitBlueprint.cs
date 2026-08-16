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

        public bool RemoveWireAt(int index)
        {
            if (index < 0 || index >= _wires.Count)
                return false;

            _wires.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// The wire joining these two ports, or -1. The pair is unique:
        /// <see cref="WiringRules"/> refuses an exact duplicate, so delay never has to be part of
        /// the search.
        /// </summary>
        public int IndexOfWire(CellPort from, CellPort to)
        {
            for (int i = 0; i < _wires.Count; i++)
            {
                if (_wires[i].From == from && _wires[i].To == to)
                    return i;
            }

            return -1;
        }

        /// <inheritdoc cref="IndexOfWire"/>
        public bool HasWire(CellPort from, CellPort to) => IndexOfWire(from, to) >= 0;

        /// <summary>
        /// Re-times a wire, keeping it at the same position in the list.
        /// </summary>
        /// <remarks>
        /// In place, and that matters. Node and edge ids come from Add and Connect call order, so
        /// replacing the entry rather than removing and re-appending it means a rebuild issues the
        /// same edge ids in the same order. The delay interaction depends on that: the player scrolls
        /// a wire they are hovering, which rebuilds the graph underneath them, and the hover is
        /// remembered by edge id. Re-appending would renumber the edges and slide the highlight onto
        /// a different wire mid-scroll.
        ///
        /// BlueprintWire is a readonly struct, so this replaces the element rather than mutating it.
        /// </remarks>
        public void SetDelayAt(int index, int delay)
        {
            if (index < 0 || index >= _wires.Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, "No wire at that index.");

            if (delay < 1)
            {
                // The same floor Edge enforces, checked here so an illegal blueprint cannot exist even
                // briefly. Below 1 would let a node observe another's output within one tick, which is
                // what makes evaluation order irrelevant.
                throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be at least 1.");
            }

            BlueprintWire wire = _wires[index];
            _wires[index] = new BlueprintWire(wire.From, wire.To, delay);
        }

        /// <summary>
        /// Ticks spent above the default across every wire -- the sum of delay minus one.
        /// </summary>
        /// <remarks>
        /// What a level's delay budget is measured against. Counting only the excess means drawing a
        /// wire never quietly costs budget, and lowering a wire refunds immediately, because this is
        /// computed from the wires rather than tallied as the player spends.
        /// </remarks>
        public int ExtraDelay()
        {
            int extra = 0;

            for (int i = 0; i < _wires.Count; i++)
                extra += _wires[i].Delay - 1;

            return extra;
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
