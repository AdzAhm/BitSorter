using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Builds a free-play <see cref="LevelDefinition"/> from a <see cref="SandboxConfig"/>.
    /// </summary>
    /// <remarks>
    /// Pure and static, for the same reason <see cref="CircuitBuilder"/> is: this is the step the
    /// tests care about most, and burying it in a panel would mean standing up a Canvas to check
    /// where a source lands.
    ///
    /// Free play is built here rather than authored as JSON in Resources/Levels, and that is a
    /// decision rather than a convenience. <see cref="LevelLoader.Validate"/> refuses a level with no
    /// expectations, and refuses a sink nothing grades -- both correct for a taught level and both
    /// fatal to free play. CurriculumTests then parses every file in that folder and demands a hint
    /// and a goal from each. Building in code leaves every one of those rules exactly as strict as it
    /// was, instead of carving an exception through the middle of them.
    /// </remarks>
    public static class SandboxLevel
    {
        /// <summary>
        /// The name free play is saved under, and the one <see cref="GameAnalytics"/> ignores.
        /// </summary>
        /// <remarks>
        /// Not a file name -- nothing by this name exists in Resources/Levels. It is only a key: the
        /// progress store records boards against it exactly as it does for a real level, which is all
        /// a sandbox needs to survive a restart.
        /// </remarks>
        public const string Key = "sandbox";

        public const int DefaultVectors = 4;
        public const int DefaultSources = 2;
        public const int DefaultSinks = 2;

        private static readonly GateKind[] EveryKind =
        {
            GateKind.Not, GateKind.And, GateKind.Or, GateKind.Xor, GateKind.Nand, GateKind.Nor,
        };

        /// <summary>
        /// How many fixtures fit in one column, which is how many rows the board has.
        /// </summary>
        /// <remarks>
        /// Sources take the left column and sinks the right, so this caps each independently. The
        /// panel enforces it rather than letting a config ask for a seventh source on a five-row
        /// board and lose it silently.
        /// </remarks>
        public static int Capacity(Vector2Int halfExtents) => halfExtents.y * 2 + 1;

        /// <summary>The config free play opens with the first time anyone visits it.</summary>
        public static SandboxConfig Default(Vector2Int halfExtents)
        {
            int capacity = Capacity(halfExtents);

            var config = new SandboxConfig
            {
                vectors = DefaultVectors,
                sinks = Mathf.Min(DefaultSinks, capacity),
                sources = new string[Mathf.Min(DefaultSources, capacity)],
            };

            // The two-input truth table, which is what most circuits worth trying want fed into them.
            string[] opening = { "0101", "0011" };

            for (int i = 0; i < config.sources.Length; i++)
                config.sources[i] = i < opening.Length ? opening[i] : string.Empty;

            config.Normalise(capacity, capacity);
            return config;
        }

        /// <summary>
        /// Turns a config into a level. Mutates <paramref name="config"/> only by normalising it.
        /// </summary>
        public static LevelDefinition Build(SandboxConfig config, Vector2Int halfExtents)
        {
            int capacity = Capacity(halfExtents);

            if (config == null)
                config = Default(halfExtents);

            config.Normalise(capacity, capacity);

            var fixtures = new List<LevelFixture>(config.sources.Length + config.sinks);

            for (int i = 0; i < config.sources.Length; i++)
            {
                fixtures.Add(new LevelFixture(
                    SourceId(i),
                    FixtureKind.Source,
                    Cell(-halfExtents.x, i, config.sources.Length, halfExtents),
                    ToBits(config.sources[i])));
            }

            for (int i = 0; i < config.sinks; i++)
            {
                fixtures.Add(new LevelFixture(
                    SinkId(i),
                    FixtureKind.Sink,
                    Cell(halfExtents.x, i, config.sinks, halfExtents),
                    System.Array.Empty<Bit>()));
            }

            var budget = new List<LevelBudgetEntry>(EveryKind.Length);

            foreach (GateKind kind in EveryKind)
                budget.Add(new LevelBudgetEntry(kind, LevelDefinition.UnlimitedBudget));

            return new LevelDefinition(
                name: "Sandbox",
                hint: "Nothing here is graded. Wire whatever you like and watch what comes out.",
                tickLimit: LevelLoader.DefaultTickLimit,
                vectorCount: config.vectors,
                fixtures: fixtures,
                budget: budget,
                // Empty rather than absent. A sink with no expectation is exactly what free play
                // means, and the grader is never asked in the first place.
                expectations: System.Array.Empty<LevelExpectation>(),
                maxWireDelay: LevelDefinition.DefaultMaxWireDelay,
                delayBudget: 0,
                maxLatency: 0,
                order: 0,
                goal: "Free play. Build anything; nothing passes or fails.",
                isGraded: false);
        }

        /// <summary>Source ids run A, B, C so they read like the inputs of an authored level.</summary>
        public static string SourceId(int index) => ((char)('A' + index)).ToString();

        public static string SinkId(int index) => $"OUT {index + 1}";

        /// <summary>
        /// Where the nth of <paramref name="count"/> fixtures sits in its column, centred vertically
        /// so a lone source is level with the middle of the board rather than pinned to the top.
        /// </summary>
        private static Vector2Int Cell(int x, int index, int count, Vector2Int halfExtents)
        {
            int capacity = Capacity(halfExtents);
            int offset = (capacity - count) / 2;

            return new Vector2Int(x, halfExtents.y - offset - index);
        }

        private static Bit[] ToBits(string stream)
        {
            var bits = new Bit[stream.Length];

            for (int i = 0; i < stream.Length; i++)
                bits[i] = stream[i] == '1' ? Bit.One : Bit.Zero;

            return bits;
        }
    }
}
