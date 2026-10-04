using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>A gate the player placed, as the save file stores it.</summary>
    [Serializable]
    public sealed class SavedPlacement
    {
        public int x;
        public int y;

        /// <summary>A GateKind name. Enums deserialize from integers only, so kinds are strings.</summary>
        public string kind;
    }

    /// <summary>
    /// A wire, as the save file stores it.
    /// </summary>
    /// <remarks>
    /// Only the port indices are kept, not which side is an input. WiringRules already guarantees
    /// From is the output end and To the input end, so storing it would be storing a fact that is
    /// always the same -- and a fact that can be wrong in a file is a fact that will be.
    /// </remarks>
    [Serializable]
    public sealed class SavedWire
    {
        public int fromX;
        public int fromY;
        public int fromPort;

        public int toX;
        public int toY;
        public int toPort;

        public int delay;
    }

    /// <summary>
    /// Everything remembered about one level: what was built on it, and the best it has been solved.
    /// </summary>
    /// <remarks>
    /// Plain ints rather than Vector2Int, matching how <see cref="LevelFile"/> stores cells and for
    /// the same reason: Unity's built-in structs back x and y with m_X and m_Y, and a save someone
    /// opens and edits would read as (0, 0) with no complaint.
    ///
    /// Zero means "no record yet" for both bests, which is safe here in a way it usually is not --
    /// a solved circuit always has at least one gate and a latency of at least one tick, so zero is
    /// unreachable rather than merely unlikely.
    /// </remarks>
    [Serializable]
    public sealed class SavedBoard
    {
        public string level;

        public SavedPlacement[] placements;
        public SavedWire[] wires;

        public int bestGates;
        public int bestLatency;

        /// <summary>
        /// Free play's sources and sinks. Null for every authored level, whose fixtures come from its
        /// file and are not the player's to change.
        /// </summary>
        /// <remarks>
        /// A new key in an existing file, which is safe in both directions: JsonUtility reads a
        /// missing key as null, so saves written before this field load unchanged, and a build
        /// without it ignores the key rather than choking on it.
        ///
        /// This has to be applied before the placements are restored, not after.
        /// <see cref="BoardSerializer.Restore"/> checks every gate against
        /// <see cref="LevelDefinition.FixtureAt"/>, so restoring gates into a sandbox whose sources
        /// do not exist yet would drop everything wired to one.
        /// </remarks>
        public SandboxConfig sandbox;

        /// <summary>The blocks on the board, each with a copy of what it is.</summary>
        /// <remarks>
        /// A new key, so a file from before blocks reads it as null and loads unchanged. The copy is
        /// what keeps a free-play board whole after its block is deleted from the library or made
        /// again differently under the same name. A level's board is restored from the level's own
        /// block of that name instead, which is current where the copy might not be.
        /// </remarks>
        public SavedBlockPlacement[] blocks;
    }

    /// <summary>A block on a board: its top cell, and a copy of the block.</summary>
    [Serializable]
    public sealed class SavedBlockPlacement
    {
        public int x;
        public int y;
        public SavedBlock block;
    }

    /// <summary>A block, as the save file stores it: on a board, and in free play's library.</summary>
    [Serializable]
    public sealed class SavedBlock
    {
        public string name;
        public SavedBlockPort[] inputs;
        public SavedBlockPort[] outputs;
        public SavedPlacement[] gates;
        public SavedWire[] wires;
    }

    /// <summary>One port of a saved block.</summary>
    [Serializable]
    public sealed class SavedBlockPort
    {
        public string id;
        public int x;
        public int y;
    }

    /// <summary>
    /// Converts between a <see cref="CircuitBlueprint"/> and its saved form.
    /// </summary>
    /// <remarks>
    /// Restoring validates rather than trusts, which is the whole reason this is separate from the
    /// store. A board saved before its level was edited can name a cell that now holds a fixture, a
    /// gate the budget no longer stocks, a port that no longer exists, or more delay than the budget
    /// now pays for -- and restoring any of those blindly would put the player on a board they could
    /// not have built. The grader never looks at budgets, so an overspent board would also pass.
    /// Anything that no longer resolves is dropped, and the rest is restored.
    /// </remarks>
    public static class BoardSerializer
    {
        public static SavedBoard ToSaved(string level, CircuitBlueprint blueprint)
        {
            var saved = new SavedBoard
            {
                level = level,
                placements = new SavedPlacement[blueprint.Placements.Count],
                wires = new SavedWire[blueprint.Wires.Count],
            };

            for (int i = 0; i < blueprint.Placements.Count; i++)
            {
                GatePlacement placement = blueprint.Placements[i];

                saved.placements[i] = new SavedPlacement
                {
                    x = placement.Cell.x,
                    y = placement.Cell.y,
                    kind = GatePalette.Label(placement.Kind),
                };
            }

            for (int i = 0; i < blueprint.Wires.Count; i++)
                saved.wires[i] = ToSaved(blueprint.Wires[i]);

            saved.blocks = new SavedBlockPlacement[blueprint.Blocks.Count];

            for (int i = 0; i < blueprint.Blocks.Count; i++)
            {
                BlockPlacement block = blueprint.Blocks[i];

                saved.blocks[i] = new SavedBlockPlacement
                {
                    x = block.Cell.x,
                    y = block.Cell.y,
                    block = ToSaved(block.Block),
                };
            }

            return saved;
        }

        private static SavedWire ToSaved(BlueprintWire wire) => new SavedWire
        {
            fromX = wire.From.Cell.x,
            fromY = wire.From.Cell.y,
            fromPort = wire.From.Index,
            toX = wire.To.Cell.x,
            toY = wire.To.Cell.y,
            toPort = wire.To.Index,
            delay = wire.Delay,
        };

        /// <summary>A block as the file stores it, in full.</summary>
        public static SavedBlock ToSaved(BlockDefinition block)
        {
            var saved = new SavedBlock
            {
                name = block.Name,
                inputs = ToSaved(block.Inputs),
                outputs = ToSaved(block.Outputs),
                gates = new SavedPlacement[block.Gates.Count],
                wires = new SavedWire[block.Wires.Count],
            };

            for (int i = 0; i < block.Gates.Count; i++)
            {
                saved.gates[i] = new SavedPlacement
                {
                    x = block.Gates[i].Cell.x,
                    y = block.Gates[i].Cell.y,
                    kind = GatePalette.Label(block.Gates[i].Kind),
                };
            }

            for (int i = 0; i < block.Wires.Count; i++)
                saved.wires[i] = ToSaved(block.Wires[i]);

            return saved;
        }

        private static SavedBlockPort[] ToSaved(IReadOnlyList<BlockPort> ports)
        {
            var saved = new SavedBlockPort[ports.Count];

            for (int i = 0; i < saved.Length; i++)
                saved[i] = new SavedBlockPort { id = ports[i].Id, x = ports[i].Cell.x, y = ports[i].Cell.y };

            return saved;
        }

        /// <summary>
        /// A saved block, read back and held to <see cref="BlockRules"/> again: a file is not trusted
        /// to still describe a block, any more than it is trusted to describe a board.
        /// </summary>
        public static bool TryFromSaved(SavedBlock saved, out BlockDefinition block)
        {
            block = null;

            if (saved == null)
                return false;

            var gates = new List<GatePlacement>(saved.gates?.Length ?? 0);

            foreach (SavedPlacement gate in saved.gates ?? Array.Empty<SavedPlacement>())
            {
                if (gate == null || !GatePalette.TryParse(gate.kind, out GateKind kind))
                    return false;

                gates.Add(new GatePlacement(new Vector2Int(gate.x, gate.y), kind));
            }

            var wires = new List<BlueprintWire>(saved.wires?.Length ?? 0);

            foreach (SavedWire wire in saved.wires ?? Array.Empty<SavedWire>())
            {
                if (wire == null)
                    return false;

                wires.Add(new BlueprintWire(
                    new CellPort(new Vector2Int(wire.fromX, wire.fromY), false, wire.fromPort),
                    new CellPort(new Vector2Int(wire.toX, wire.toY), true, wire.toPort),
                    wire.delay));
            }

            return BlockRules.TryDefine(
                saved.name, FromSaved(saved.inputs), FromSaved(saved.outputs), gates, wires, out block, out string _);
        }

        private static BlockPort[] FromSaved(SavedBlockPort[] saved)
        {
            var ports = new BlockPort[saved?.Length ?? 0];

            for (int i = 0; i < ports.Length; i++)
            {
                ports[i] = saved[i] == null
                    ? new BlockPort(null, default)
                    : new BlockPort(saved[i].id, new Vector2Int(saved[i].x, saved[i].y));
            }

            return ports;
        }

        /// <summary>
        /// Rebuilds <paramref name="blueprint"/> from <paramref name="saved"/>, keeping only what the
        /// level still permits. Returns how many entries were dropped.
        /// </summary>
        public static int Restore(
            SavedBoard saved, LevelDefinition level, CircuitBlueprint blueprint,
            Vector2Int halfExtents)
        {
            blueprint.Clear();

            if (saved == null || level == null)
                return 0;

            int dropped = 0;

            dropped += RestorePlacements(saved, level, blueprint, halfExtents);
            dropped += RestoreBlocks(saved, level, blueprint, halfExtents);
            dropped += RestoreWires(saved, level, blueprint);

            return dropped;
        }

        /// <summary>
        /// Puts back the blocks a level still stocks, or in free play any block whose copy still makes
        /// a block, on cells still free.
        /// </summary>
        /// <remarks>
        /// A level's board takes the level's own block of that name rather than the saved copy, the way
        /// it takes the level's current budget: a block changed in a later version reaches the board.
        /// Free play keeps the copy, which is the point of keeping one.
        /// </remarks>
        private static int RestoreBlocks(
            SavedBoard saved, LevelDefinition level, CircuitBlueprint blueprint, Vector2Int halfExtents)
        {
            if (saved.blocks == null)
                return 0;

            int dropped = 0;

            foreach (SavedBlockPlacement placement in saved.blocks)
            {
                BlockDefinition block = null;

                if (placement?.block != null)
                {
                    if (level.AnyBlock)
                        TryFromSaved(placement.block, out block);
                    else
                        block = level.BlockNamed(placement.block.name);
                }

                if (block == null || !Fits(block, new Vector2Int(placement.x, placement.y), level, blueprint, halfExtents))
                {
                    dropped++;
                    continue;
                }

                blueprint.PlaceBlock(new Vector2Int(placement.x, placement.y), block);
            }

            return dropped;
        }

        /// <summary>Whether a block may stand here as the board now is: every cell, and the budget.</summary>
        private static bool Fits(
            BlockDefinition block, Vector2Int cell, LevelDefinition level, CircuitBlueprint blueprint,
            Vector2Int halfExtents)
        {
            int budgeted = level.BlockBudgetFor(block.Name);

            if (budgeted == 0 ||
                (budgeted != LevelDefinition.UnlimitedBudget && blueprint.CountOfBlock(block.Name) >= budgeted))
            {
                return false;
            }

            for (int i = 0; i < block.Height; i++)
            {
                var covered = new Vector2Int(cell.x, cell.y - i);

                bool free =
                    Mathf.Abs(covered.x) <= halfExtents.x &&
                    Mathf.Abs(covered.y) <= halfExtents.y &&
                    level.FixtureAt(covered) == null &&
                    !level.IsReserved(covered) &&
                    !blueprint.HasPlacementAt(covered);

                if (!free)
                    return false;
            }

            return true;
        }

        private static int RestorePlacements(
            SavedBoard saved, LevelDefinition level, CircuitBlueprint blueprint,
            Vector2Int halfExtents)
        {
            if (saved.placements == null)
                return 0;

            int dropped = 0;

            foreach (SavedPlacement placement in saved.placements)
            {
                if (placement == null || !GatePalette.TryParse(placement.kind, out GateKind kind))
                {
                    dropped++;
                    continue;
                }

                var cell = new Vector2Int(placement.x, placement.y);

                bool withinBudget = level.IsUnlimited(kind) ||
                                    blueprint.CountOf(kind) < level.BudgetFor(kind);

                bool legal =
                    Mathf.Abs(cell.x) <= halfExtents.x &&
                    Mathf.Abs(cell.y) <= halfExtents.y &&
                    level.FixtureAt(cell) == null &&
                    !level.IsReserved(cell) &&
                    !blueprint.HasPlacementAt(cell) &&
                    withinBudget;

                if (!legal)
                {
                    dropped++;
                    continue;
                }

                blueprint.Place(cell, kind);
            }

            return dropped;
        }

        private static int RestoreWires(
            SavedBoard saved, LevelDefinition level, CircuitBlueprint blueprint)
        {
            if (saved.wires == null)
                return 0;

            int dropped = 0;

            foreach (SavedWire wire in saved.wires)
            {
                if (wire == null)
                {
                    dropped++;
                    continue;
                }

                var fromCell = new Vector2Int(wire.fromX, wire.fromY);
                var toCell = new Vector2Int(wire.toX, wire.toY);

                // Wires are kept in file order, so the ones that fit the budget first stay -- the
                // same answer editing gives, where the re-time that overspends is the one refused.
                bool affordable = !level.HasDelayBudget ||
                                  blueprint.ExtraDelay() + (wire.delay - 1) <= level.DelayBudget;

                bool legal =
                    wire.delay >= 1 &&
                    wire.delay <= level.MaxWireDelay &&
                    affordable &&
                    wire.fromPort >= 0 && wire.fromPort < OutputsAt(fromCell, level, blueprint) &&
                    wire.toPort >= 0 && wire.toPort < InputsAt(toCell, level, blueprint) &&
                    !blueprint.HasWire(
                        new CellPort(fromCell, false, wire.fromPort),
                        new CellPort(toCell, true, wire.toPort));

                if (!legal)
                {
                    dropped++;
                    continue;
                }

                blueprint.AddWire(new BlueprintWire(
                    new CellPort(fromCell, false, wire.fromPort),
                    new CellPort(toCell, true, wire.toPort),
                    wire.delay));
            }

            return dropped;
        }

        /// <summary>
        /// Output ports on whatever occupies a cell, or zero for an empty one.
        /// </summary>
        /// <remarks>
        /// A reserved slot counts as the fixture it is kept for, even while empty. That is what lets
        /// a wire outlive the source it was drawn from: free play's counts go down as well as up,
        /// and a wire dropped on the way down would not come back on the way up. It stays in the
        /// blueprint, drawn by nothing and simulated by nothing, until the fixture returns.
        /// </remarks>
        internal static int OutputsAt(Vector2Int cell, LevelDefinition level, CircuitBlueprint blueprint)
        {
            LevelFixture fixture = level.FixtureAt(cell);

            if (fixture != null)
                return fixture.Kind == FixtureKind.Source ? 1 : 0;

            if (level.TryReservedKind(cell, out FixtureKind reserved))
                return reserved == FixtureKind.Source ? 1 : 0;

            // A block's wires are stored against its top cell alone.
            if (blueprint.TryGetBlock(cell, out BlockPlacement block))
                return block.Cell == cell ? block.Block.Outputs.Count : 0;

            return blueprint.TryGetPlacement(cell, out GateKind kind) ? GatePalette.OutputsOf(kind) : 0;
        }

        /// <inheritdoc cref="OutputsAt"/>
        internal static int InputsAt(Vector2Int cell, LevelDefinition level, CircuitBlueprint blueprint)
        {
            LevelFixture fixture = level.FixtureAt(cell);

            if (fixture != null)
                return fixture.Kind == FixtureKind.Sink ? 1 : 0;

            if (level.TryReservedKind(cell, out FixtureKind reserved))
                return reserved == FixtureKind.Sink ? 1 : 0;

            if (blueprint.TryGetBlock(cell, out BlockPlacement block))
                return block.Cell == cell ? block.Block.Inputs.Count : 0;

            return blueprint.TryGetPlacement(cell, out GateKind kind)
                ? GatePalette.InputsOf(kind)
                : 0;
        }
    }
}
