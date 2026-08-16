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
        public string hint;

        /// <summary>Zero means "unspecified"; the validator substitutes the default.</summary>
        public int tickLimit;

        public LevelFixtureFile[] fixtures;
        public LevelBudgetFile[] budget;
        public LevelExpectationFile[] expected;
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
        /// <summary>A GateKind name: Not, And, Or, Xor, Nand or Nor.</summary>
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

    /// <summary>A grid cell in the JSON's own coordinates, converted to Vector2Int on validation.</summary>
    [Serializable]
    public struct LevelCellFile
    {
        public int x;
        public int y;
    }
}
