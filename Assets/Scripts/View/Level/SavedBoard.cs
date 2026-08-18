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
    }

    /// <summary>
    /// Converts between a <see cref="CircuitBlueprint"/> and its saved form.
    /// </summary>
    /// <remarks>
    /// Restoring validates rather than trusts, which is the whole reason this is separate from the
    /// store. A board saved before its level was edited can name a cell that now holds a fixture, a
    /// gate the budget no longer stocks, or a port that no longer exists -- and restoring any of
    /// those blindly would put the player on a board they could not have built and cannot fix.
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
            {
                BlueprintWire wire = blueprint.Wires[i];

                saved.wires[i] = new SavedWire
                {
                    fromX = wire.From.Cell.x,
                    fromY = wire.From.Cell.y,
                    fromPort = wire.From.Index,
                    toX = wire.To.Cell.x,
                    toY = wire.To.Cell.y,
                    toPort = wire.To.Index,
                    delay = wire.Delay,
                };
            }

            return saved;
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
            dropped += RestoreWires(saved, level, blueprint);

            return dropped;
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

                bool legal =
                    Mathf.Abs(cell.x) <= halfExtents.x &&
                    Mathf.Abs(cell.y) <= halfExtents.y &&
                    level.FixtureAt(cell) == null &&
                    !blueprint.HasPlacementAt(cell) &&
                    blueprint.CountOf(kind) < level.BudgetFor(kind);

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

                bool legal =
                    wire.delay >= 1 &&
                    wire.delay <= level.MaxWireDelay &&
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

        /// <summary>Output ports on whatever occupies a cell, or zero for an empty one.</summary>
        private static int OutputsAt(Vector2Int cell, LevelDefinition level, CircuitBlueprint blueprint)
        {
            LevelFixture fixture = level.FixtureAt(cell);

            if (fixture != null)
                return fixture.Kind == FixtureKind.Source ? 1 : 0;

            return blueprint.TryGetPlacement(cell, out GateKind _) ? 1 : 0;
        }

        /// <inheritdoc cref="OutputsAt"/>
        private static int InputsAt(Vector2Int cell, LevelDefinition level, CircuitBlueprint blueprint)
        {
            LevelFixture fixture = level.FixtureAt(cell);

            if (fixture != null)
                return fixture.Kind == FixtureKind.Sink ? 1 : 0;

            return blueprint.TryGetPlacement(cell, out GateKind kind)
                ? GatePalette.InputsOf(kind)
                : 0;
        }
    }
}
