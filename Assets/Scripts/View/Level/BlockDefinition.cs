using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// One of a block's ports: the name it is labelled with, and the cell its source or sink had on
    /// the board the block was made from.
    /// </summary>
    public readonly struct BlockPort : IEquatable<BlockPort>
    {
        public readonly string Id;
        public readonly Vector2Int Cell;

        public BlockPort(string id, Vector2Int cell)
        {
            Id = id;
            Cell = cell;
        }

        public bool Equals(BlockPort other) => string.Equals(Id, other.Id, StringComparison.Ordinal) && Cell == other.Cell;

        public override bool Equals(object obj) => obj is BlockPort other && Equals(other);

        public override int GetHashCode() => ((Id?.GetHashCode() ?? 0) * 397) ^ Cell.GetHashCode();

        public override string ToString() => $"{Id} at {Cell}";
    }

    /// <summary>
    /// A circuit in a box: a small board whose sources became the box's inputs and whose sinks became
    /// its outputs. Placed and wired like a gate.
    /// </summary>
    /// <remarks>
    /// Immutable, and checked before it exists: <see cref="BlockRules.TryDefine"/> is the only way to
    /// make one, as <see cref="LevelLoader.Validate"/> is for a level. A block that broke a rule would
    /// break the splice that builds it, so the rules are not a courtesy.
    ///
    /// The cells are those of the board it was made on, and mean nothing on the board it is placed on.
    /// They say only which wire joins what inside, in the same shapes a board uses, so a free-play
    /// board becomes a block by being copied.
    ///
    /// Held by reference in <see cref="BlockPlacement"/>, so a board carries the blocks on it. Nothing
    /// can change one, so a snapshot that shares it with the live board is still a copy.
    /// </remarks>
    public sealed class BlockDefinition
    {
        private readonly BlockPort[] _inputs;
        private readonly BlockPort[] _outputs;
        private readonly GatePlacement[] _gates;
        private readonly BlueprintWire[] _wires;

        internal BlockDefinition(
            string name, BlockPort[] inputs, BlockPort[] outputs, GatePlacement[] gates, BlueprintWire[] wires)
        {
            Name = name;
            _inputs = inputs;
            _outputs = outputs;
            _gates = gates;
            _wires = wires;
        }

        /// <summary>What the box is labelled. Short: see <see cref="BlockRules.MaxNameLength"/>.</summary>
        public string Name { get; }

        /// <summary>The input ports, top to bottom on the box's left face.</summary>
        public IReadOnlyList<BlockPort> Inputs => _inputs;

        /// <summary>The output ports, top to bottom on the box's right face.</summary>
        public IReadOnlyList<BlockPort> Outputs => _outputs;

        /// <summary>The gates inside, in the order they are added on a build.</summary>
        public IReadOnlyList<GatePlacement> Gates => _gates;

        /// <summary>The wires inside, in the order they are connected on a build.</summary>
        public IReadOnlyList<BlueprintWire> Wires => _wires;

        /// <summary>
        /// Cells tall on the board: two ports a cell on the busier face, and never less than one.
        /// </summary>
        /// <remarks>
        /// One column wide whatever it holds, so it sits in a column the way a gate does. Ports are
        /// spread over its whole height, which at two a cell keeps them as far apart as the two inputs
        /// of a gate are.
        /// </remarks>
        public int Height => Mathf.Max(1, (Mathf.Max(_inputs.Length, _outputs.Length) + 1) / 2);

        /// <summary>
        /// Whether two definitions describe the same block: the same name, ports, gates and wires, in
        /// the same order.
        /// </summary>
        /// <remarks>
        /// Content rather than reference. A board restored from a save holds a copy of its block, read
        /// back from the file, and it is still the block the player put there.
        /// </remarks>
        public bool Matches(BlockDefinition other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other == null || !string.Equals(Name, other.Name, StringComparison.Ordinal))
                return false;

            return Same(_inputs, other._inputs) && Same(_outputs, other._outputs)
                   && SameGates(_gates, other._gates) && Same(_wires, other._wires);
        }

        private static bool Same<T>(T[] a, T[] b) where T : IEquatable<T>
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }

            return true;
        }

        private static bool SameGates(GatePlacement[] a, GatePlacement[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Cell != b[i].Cell || a[i].Kind != b[i].Kind)
                    return false;
            }

            return true;
        }

        public override string ToString() =>
            $"{Name} ({_inputs.Length} in, {_outputs.Length} out, {_gates.Length} gates)";
    }
}
