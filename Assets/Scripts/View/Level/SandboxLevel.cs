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

        private const string FreePlay = "Free play. Build anything; nothing passes or fails.";

        /// <summary>
        /// Why this setup cannot do anything, or null when it can.
        /// </summary>
        /// <remarks>
        /// A sandbox with no sources settles the instant it is run, and one with no sinks has
        /// nowhere for a bit to end up. Both are reachable -- the panel allows zero of either -- and
        /// both otherwise present as a run that ends immediately with nothing to show, which reads
        /// as the game being broken rather than as the board being empty.
        ///
        /// One wording, used twice: <see cref="GoalFor"/> puts it in the status banner for a player
        /// looking at the board, and the sandbox panel shows the same string for a player looking at
        /// the setup. The panel covers the banner, so both are needed; a second wording would not be.
        /// </remarks>
        public static string Warning(SandboxConfig config)
        {
            if (config == null)
                return null;

            if (config.sources == null || config.sources.Length == 0)
                return "No sources, so nothing is emitted. Add one to see anything happen.";

            if (config.sinks <= 0)
                return "No sinks, so bits have nowhere to land. Add one to catch them.";

            return null;
        }

        /// <summary>What the status banner says: the trouble if there is any, else what free play is.</summary>
        public static string GoalFor(SandboxConfig config) => Warning(config) ?? FreePlay;

        /// <summary>
        /// Every part there is, taken from the enum rather than written out: free play stocks all
        /// of them, so a part added later should appear here without anyone remembering to.
        /// </summary>
        private static readonly GateKind[] EveryKind =
            (GateKind[])System.Enum.GetValues(typeof(GateKind));

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
                layout = SandboxConfig.CurrentLayout,
                sinks = Mathf.Min(DefaultSinks, capacity),
                sources = new string[Mathf.Min(DefaultSources, capacity)],
            };

            // The two-input truth table, which is what most circuits worth trying want fed into them,
            // written the way the game writes every other one: A is the most significant bit.
            string[] opening = SandboxRules.Table(DefaultSources);

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
                    Cell(-halfExtents.x, i, halfExtents),
                    ToBits(config.sources[i], config.vectors)));
            }

            for (int i = 0; i < config.sinks; i++)
            {
                fixtures.Add(new LevelFixture(
                    SinkId(i),
                    FixtureKind.Sink,
                    Cell(halfExtents.x, i, halfExtents),
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
                goal: GoalFor(config),
                isGraded: false,
                reservedSlots: Slots(halfExtents),
                // Free play's own clock. It matters here for the same reason it matters in the
                // levels: a loop through a register cannot keep up with a vector every tick, so a
                // state machine built at a clock of 1 destroys bits however carefully it is wired.
                clockPeriod: config.Clock);
        }

        /// <summary>
        /// Every cell in the two edge columns, tagged with the kind that column takes.
        /// </summary>
        /// <remarks>
        /// The whole column, not merely the slots beyond the current count: the count is the
        /// player's to raise at any moment, and a rule that changes shape as they step it would have
        /// them place a gate legally and then watch it vanish.
        /// </remarks>
        private static LevelSlot[] Slots(Vector2Int halfExtents)
        {
            int capacity = Capacity(halfExtents);
            var slots = new LevelSlot[capacity * 2];

            for (int i = 0; i < capacity; i++)
            {
                slots[i] = new LevelSlot(Cell(-halfExtents.x, i, halfExtents), FixtureKind.Source);
                slots[capacity + i] = new LevelSlot(Cell(halfExtents.x, i, halfExtents), FixtureKind.Sink);
            }

            return slots;
        }

        /// <summary>Source ids run A, B, C so they read like the inputs of an authored level.</summary>
        public static string SourceId(int index) => ((char)('A' + index)).ToString();

        public static string SinkId(int index) => $"OUT {index + 1}";

        /// <summary>
        /// Where the nth fixture of a column sits: its own slot, counted from the top, whatever the
        /// count.
        /// </summary>
        /// <remarks>
        /// Fixtures used to be centred in the column, so a lone source sat level with the middle of
        /// the board. That moved every fixture whenever the count changed, and wires are stored by
        /// cell: adding a second source put B where A had been, and every wire drawn from A came
        /// from B from then on, with nothing on screen to say so.
        /// </remarks>
        private static Vector2Int Cell(int x, int index, Vector2Int halfExtents) =>
            new Vector2Int(x, halfExtents.y - index);

        /// <summary>
        /// Moves a board saved while fixtures were centred onto the slots they have now. True if the
        /// board needed it.
        /// </summary>
        /// <remarks>
        /// Wire ends are moved from each fixture's old cell to its new one, by index, so a circuit
        /// wired to A is still wired to A. The old cells are worked out from the saved counts, which
        /// is all the old layout depended on. Only a wire's output end can be on a source and only
        /// its input end on a sink, so each end is looked up against one column's moves and never
        /// moved twice.
        ///
        /// Gates are left where they were. One in an edge column may now share a cell with a
        /// fixture, and the restore drops it as it drops anything that no longer fits.
        /// </remarks>
        public static bool MigrateLegacyBoard(SavedBoard board, Vector2Int halfExtents)
        {
            SandboxConfig config = board?.sandbox;

            if (config == null || config.layout >= SandboxConfig.CurrentLayout)
                return false;

            int capacity = Capacity(halfExtents);
            config.Normalise(capacity, capacity);

            Dictionary<Vector2Int, Vector2Int> sources =
                LegacyMoves(-halfExtents.x, config.sources.Length, halfExtents);
            Dictionary<Vector2Int, Vector2Int> sinks =
                LegacyMoves(halfExtents.x, config.sinks, halfExtents);

            if (board.wires != null)
            {
                foreach (SavedWire wire in board.wires)
                {
                    if (wire == null)
                        continue;

                    if (sources.TryGetValue(new Vector2Int(wire.fromX, wire.fromY), out Vector2Int from))
                    {
                        wire.fromX = from.x;
                        wire.fromY = from.y;
                    }

                    if (sinks.TryGetValue(new Vector2Int(wire.toX, wire.toY), out Vector2Int to))
                    {
                        wire.toX = to.x;
                        wire.toY = to.y;
                    }
                }
            }

            config.layout = SandboxConfig.CurrentLayout;
            return true;
        }

        /// <summary>Each fixture's cell under the centred layout, to its cell now.</summary>
        private static Dictionary<Vector2Int, Vector2Int> LegacyMoves(
            int x, int count, Vector2Int halfExtents)
        {
            var moves = new Dictionary<Vector2Int, Vector2Int>(count);
            int offset = (Capacity(halfExtents) - count) / 2;

            for (int i = 0; i < count; i++)
                moves[new Vector2Int(x, halfExtents.y - offset - i)] = Cell(x, i, halfExtents);

            return moves;
        }

        /// <summary>
        /// The first <paramref name="vectors"/> bits of a stream.
        /// </summary>
        /// <remarks>
        /// The config keeps every stream at its full length, so the vector count decides how much of
        /// one is emitted rather than how much of it survives. Lowering it and raising it again gets
        /// the same pattern back.
        /// </remarks>
        private static Bit[] ToBits(string stream, int vectors)
        {
            int length = Mathf.Clamp(vectors, 0, stream != null ? stream.Length : 0);
            var bits = new Bit[length];

            for (int i = 0; i < length; i++)
                bits[i] = stream[i] == '1' ? Bit.One : Bit.Zero;

            return bits;
        }
    }
}
