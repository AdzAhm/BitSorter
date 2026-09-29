using System;

namespace BitSorter.View
{
    /// <summary>
    /// The raw shape of a level JSON file, exactly as JsonUtility deserializes it. Nothing here is
    /// trusted; <see cref="LevelDefinition"/> is the checked projection that the rest of the game
    /// uses.
    /// </summary>
    /// <remarks>
    /// Field names are lower case because JsonUtility maps JSON keys onto field names verbatim, and
    /// every discriminator is a string rather than an enum. Three of the serializer's limits shape
    /// this file:
    ///
    /// Enums deserialize from integers only, never from names. A "kind": "Source" read into a
    /// GateKind field would silently become 0, so kinds arrive as strings and are parsed by hand,
    /// which yields a usable error message as well.
    ///
    /// Missing and unknown keys are ignored in silence, so a typo produces a default value instead
    /// of a failure. Nothing here is optional by accident -- every field is checked downstream.
    ///
    /// Cells are a local type rather than Vector2Int. Unity's built-in structs back their x and y
    /// with m_X and m_Y, and trusting the serializer to bridge that would risk a cell quietly
    /// reading as (0, 0), the exact silent failure this two-layer split exists to prevent.
    /// </remarks>
    [Serializable]
    public sealed class LevelFile
    {
        public string name;

        /// <summary>
        /// What the player is being asked to build, stated plainly. Unlike <see cref="hint"/> this is
        /// allowed to name gates and give the function outright -- it is the objective, not a clue.
        /// </summary>
        public string goal;

        /// <summary>
        /// A nudge towards the objective, never a statement of it. Held to the no-giveaway rules in
        /// CurriculumTests, which deliberately do not apply to <see cref="goal"/>.
        /// </summary>
        public string hint;

        /// <summary>Zero means "unspecified"; the validator substitutes the default.</summary>
        public int tickLimit;

        /// <summary>
        /// Most ticks the player may put on one wire. Zero means unspecified. Set it to 1 to forbid
        /// re-timing altogether -- there is no way to say that with delayBudget, because JsonUtility
        /// cannot tell a missing key from an explicit 0.
        /// </summary>
        public int maxWireDelay;

        /// <summary>
        /// Total ticks the player may add across all wires, counting only what is above the default
        /// of 1. Zero or absent means unlimited.
        /// </summary>
        public int delayBudget;

        /// <summary>
        /// Most ticks a bit may take from leaving a source to reaching a sink. Zero or absent means
        /// the level does not grade on time at all, which is how every level written before this
        /// field behaves.
        /// </summary>
        public int maxLatency;

        /// <summary>
        /// Ticks between one test vector and the next: the level's clock. Absent or zero means 1,
        /// a vector every tick, which is every level written before this field.
        /// </summary>
        /// <remarks>
        /// A level with feedback needs one. The shortest loop is two wires, so the state cannot get
        /// back to the logic before the next vector lands, and the inputs pile up into a collision.
        /// Spacing the vectors is the clock period, and the rule it teaches is the real one:
        /// everything must settle inside one period.
        /// </remarks>
        public int clockPeriod;

        /// <summary>
        /// Where this level sits in the run. Zero or absent leaves it unplaced, and unplaced levels
        /// sort to the end by file name -- which is how every level behaved before this field.
        /// </summary>
        /// <remarks>
        /// Authored in tens, so a level can be inserted between two others without renumbering the
        /// rest. Uniqueness is not checkable here: one file cannot see another, so
        /// <see cref="LevelCatalog"/> enforces it across the set.
        /// </remarks>
        public int order;

        /// <summary>
        /// The board this level is played on, in cells: <c>"board": { "columns": 11, "rows": 7 }</c>.
        /// Absent, or zero, is the 9 by 5 board every level had before this field.
        /// </summary>
        /// <remarks>
        /// Odd both ways, because the grid is centred on the origin, and from 9 by 5 up to 13 by 7:
        /// past that a cell is drawn too small to read on one screen, and the board is never
        /// panned or zoomed.
        /// </remarks>
        public LevelBoardFile board;

        public LevelFixtureFile[] fixtures;
        public LevelBudgetFile[] budget;
        public LevelExpectationFile[] expected;

        /// <summary>
        /// A circuit already on the board when the level opens, for a level about finding what is
        /// wrong with one. Absent or empty is an empty board, which is every level written before
        /// this field.
        /// </summary>
        /// <remarks>
        /// Its parts count against <see cref="budget"/> like any the player places, so the budget
        /// is the start's parts plus whatever spares the level offers, and its wires spend
        /// <see cref="delayBudget"/> from the moment the level opens. The loader holds it to every
        /// rule a player's board is held to.
        /// </remarks>
        public LevelStartFile start;
    }

    /// <summary>A level's starting circuit: its parts, and the wires between them.</summary>
    [Serializable]
    public sealed class LevelStartFile
    {
        public LevelStartGateFile[] gates;
        public LevelStartWireFile[] wires;
    }

    /// <summary>One part of a starting circuit.</summary>
    [Serializable]
    public sealed class LevelStartGateFile
    {
        /// <summary>A GateKind name, as in the budget.</summary>
        public string kind;

        public LevelCellFile cell;
    }

    /// <summary>
    /// One wire of a starting circuit, from an output to an input, each named by its cell and its
    /// port there.
    /// </summary>
    /// <remarks>
    /// JsonUtility reads a missing key as zero, which decides what zero means here. Every part and
    /// every source has one output, so <see cref="fromPort"/> is 0 and may be left out. A missing
    /// <see cref="toPort"/> is input 0, counted from the top -- so one forgotten on a two-input gate
    /// shows up as a second wire into input 0, which the loader refuses and names. A missing
    /// <see cref="delay"/> is 1, the length of a wire nobody has scrolled.
    /// </remarks>
    [Serializable]
    public sealed class LevelStartWireFile
    {
        public LevelCellFile from;
        public int fromPort;
        public LevelCellFile to;
        public int toPort;
        public int delay;
    }

    /// <summary>A node the player can neither move nor delete.</summary>
    [Serializable]
    public sealed class LevelFixtureFile
    {
        public string id;

        /// <summary>"Source" or "Sink".</summary>
        public string kind;

        public LevelCellFile cell;

        /// <summary>
        /// Sources only: one character per test vector, each '0' or '1'. Unused by sinks.
        /// </summary>
        public string stream;
    }

    /// <summary>How many of one gate kind the player may place.</summary>
    [Serializable]
    public sealed class LevelBudgetFile
    {
        /// <summary>A GateKind name: Not, And, Or, Xor, Nand, Nor or Register.</summary>
        public string kind;

        public int count;
    }

    /// <summary>What one sink must receive, one character per test vector.</summary>
    [Serializable]
    public sealed class LevelExpectationFile
    {
        /// <summary>The id of a Sink fixture.</summary>
        public string sink;

        /// <summary>Each character '0', '1', or '-' for "this vector produces nothing here".</summary>
        public string values;
    }

    /// <summary>A level's own board size, in cells. See <see cref="LevelFile.board"/>.</summary>
    [Serializable]
    public sealed class LevelBoardFile
    {
        public int columns;
        public int rows;
    }

    /// <summary>A grid cell in the JSON's own coordinates, converted to Vector2Int on validation.</summary>
    [Serializable]
    public struct LevelCellFile
    {
        public int x;
        public int y;
    }
}
