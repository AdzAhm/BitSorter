using System;
using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>A validated level, or the reason the file was refused.</summary>
    public readonly struct LevelLoadResult
    {
        public readonly LevelDefinition Level;

        /// <summary>Null when the load succeeded, otherwise one line naming what is wrong.</summary>
        public readonly string Error;

        private LevelLoadResult(LevelDefinition level, string error)
        {
            Level = level;
            Error = error;
        }

        public bool IsValid => Level != null;

        public static LevelLoadResult Accept(LevelDefinition level) =>
            new LevelLoadResult(level, null);

        public static LevelLoadResult Reject(string error) =>
            new LevelLoadResult(null, error ?? "invalid level");

        public override string ToString() => IsValid ? Level.ToString() : $"invalid: {Error}";
    }

    /// <summary>
    /// Turns a level JSON file into a <see cref="LevelDefinition"/>, refusing anything malformed with
    /// a reason instead of throwing.
    /// </summary>
    /// <remarks>
    /// Split into three layers so only the outermost needs the engine:
    /// <see cref="Load"/> reads from Resources, <see cref="Parse"/> deserializes a string, and
    /// <see cref="Validate"/> is pure. The tests drive Parse and Validate with inline JSON, so the
    /// whole rule matrix is exercised without touching the asset database.
    ///
    /// JsonUtility gives no validation whatsoever -- unknown keys are dropped and missing ones become
    /// default values -- so every rule in Validate is load-bearing. A level that skipped validation
    /// would not fail loudly; it would build a subtly wrong circuit and grade the player against it.
    /// </remarks>
    public static class LevelLoader
    {
        /// <summary>
        /// Used when a file omits tickLimit. Generous: the board is 9 by 5 cells, so no honestly built
        /// circuit comes near it. It exists only to stop an oscillator -- a gate fed by its own output,
        /// which WiringRules deliberately allows -- from hanging a run forever.
        /// </summary>
        /// <remarks>
        /// Worth setting per level rather than leaning on this. The limit is spent in real time at the
        /// runner's tick interval, so it is also how long a player stares at a circuit that is never
        /// going to finish: 100 ticks at the default half-second tick is nearly a minute. Both shipped
        /// levels settle within 6 ticks and cap themselves at 40. R interrupts a run at any point, so
        /// this is a backstop rather than the only way out.
        /// </remarks>
        public const int DefaultTickLimit = 100;

        /// <summary>Where level files live, relative to a Resources folder.</summary>
        public const string ResourcePath = "Levels";

        /// <summary>
        /// Loads a level by file name without extension, e.g. "route-the-bit".
        /// </summary>
        public static LevelLoadResult Load(string levelName, Vector2Int halfExtents)
        {
            if (string.IsNullOrWhiteSpace(levelName))
                return LevelLoadResult.Reject("no level name given");

            var asset = Resources.Load<TextAsset>($"{ResourcePath}/{levelName}");

            if (asset == null)
            {
                return LevelLoadResult.Reject(
                    $"no level named '{levelName}' in Assets/Resources/{ResourcePath}/");
            }

            LevelLoadResult result = Parse(asset.text, halfExtents);

            return result.IsValid
                ? result
                : LevelLoadResult.Reject($"level '{levelName}': {result.Error}");
        }

        /// <summary>
        /// Deserializes and validates JSON text. Malformed JSON comes back as a rejection rather than
        /// an exception, because a bad level file is authoring feedback, not a crash.
        /// </summary>
        public static LevelLoadResult Parse(string json, Vector2Int halfExtents)
        {
            if (string.IsNullOrWhiteSpace(json))
                return LevelLoadResult.Reject("the file is empty");

            LevelFile file;

            try
            {
                file = JsonUtility.FromJson<LevelFile>(json);
            }
            catch (ArgumentException exception)
            {
                // JsonUtility's only failure mode, and its message is the sole clue about where.
                return LevelLoadResult.Reject($"malformed JSON -- {exception.Message}");
            }

            return Validate(file, halfExtents);
        }

        // -----------------------------------------------------------------
        // Validation
        // -----------------------------------------------------------------

        /// <summary>
        /// The whole rule set, pure and engine-free apart from Vector2Int. Returns on the first
        /// problem: a level author fixes one thing at a time, and a list of cascading complaints
        /// from a single missing field helps nobody.
        /// </summary>
        public static LevelLoadResult Validate(LevelFile file, Vector2Int halfExtents)
        {
            if (file == null)
                return LevelLoadResult.Reject("the file did not deserialize to a level");

            if (string.IsNullOrWhiteSpace(file.name))
                return LevelLoadResult.Reject("no name");

            if (file.fixtures == null || file.fixtures.Length == 0)
                return LevelLoadResult.Reject("no fixtures -- a level needs at least one source and one sink");

            if (file.expected == null || file.expected.Length == 0)
                return LevelLoadResult.Reject("no expectations -- nothing would be graded");

            var fixtures = new List<LevelFixture>(file.fixtures.Length);
            var takenCells = new HashSet<Vector2Int>();
            var takenIds = new HashSet<string>();
            int vectorCount = -1;

            for (int i = 0; i < file.fixtures.Length; i++)
            {
                LevelFixtureFile raw = file.fixtures[i];

                if (raw == null)
                    return LevelLoadResult.Reject($"fixture {i} is empty");

                if (string.IsNullOrWhiteSpace(raw.id))
                    return LevelLoadResult.Reject($"fixture {i} has no id");

                string id = raw.id.Trim();

                if (!takenIds.Add(id))
                    return LevelLoadResult.Reject($"two fixtures share the id '{id}'");

                if (!TryParseFixtureKind(raw.kind, out FixtureKind kind))
                {
                    return LevelLoadResult.Reject(
                        $"fixture '{id}' has kind '{raw.kind}'; expected Source or Sink");
                }

                var cell = new Vector2Int(raw.cell.x, raw.cell.y);

                if (Mathf.Abs(cell.x) > halfExtents.x || Mathf.Abs(cell.y) > halfExtents.y)
                {
                    return LevelLoadResult.Reject(
                        $"fixture '{id}' sits at {cell}, outside the board " +
                        $"(x within {halfExtents.x}, y within {halfExtents.y})");
                }

                if (!takenCells.Add(cell))
                    return LevelLoadResult.Reject($"two fixtures share the cell {cell}");

                IReadOnlyList<Bit> stream = Array.Empty<Bit>();

                if (kind == FixtureKind.Source)
                {
                    if (!TryParseStream(raw.stream, out Bit[] bits, out string streamError))
                        return LevelLoadResult.Reject($"source '{id}': {streamError}");

                    if (vectorCount >= 0 && bits.Length != vectorCount)
                    {
                        return LevelLoadResult.Reject(
                            $"source '{id}' has {bits.Length} vectors but an earlier source has " +
                            $"{vectorCount}; every stream must be the same length");
                    }

                    vectorCount = bits.Length;
                    stream = bits;
                }
                else if (!string.IsNullOrEmpty(raw.stream))
                {
                    // Almost always a copy-pasted source. Silently ignoring it would leave the
                    // author believing the sink emits something.
                    return LevelLoadResult.Reject(
                        $"sink '{id}' has a stream; only sources emit bits");
                }

                fixtures.Add(new LevelFixture(id, kind, cell, stream));
            }

            if (vectorCount < 0)
                return LevelLoadResult.Reject("no sources -- nothing would ever be emitted");

            if (!TryBuildBudget(file.budget, out List<LevelBudgetEntry> budget, out string budgetError))
                return LevelLoadResult.Reject(budgetError);

            if (!TryBuildExpectations(file.expected, fixtures, vectorCount,
                    out List<LevelExpectation> expectations, out string expectationError))
            {
                return LevelLoadResult.Reject(expectationError);
            }

            // Every sink must be spoken for. The grader already treats an unmentioned sink as
            // "expects nothing", but relying on that silently is how a level ships with a bin nobody
            // checks -- which the player then passes by wiring into it. Demanding the explicit "-"
            // makes the intent readable in the file.
            for (int i = 0; i < fixtures.Count; i++)
            {
                LevelFixture fixture = fixtures[i];

                if (fixture.Kind != FixtureKind.Sink)
                    continue;

                if (!HasExpectationFor(expectations, fixture.Id))
                {
                    return LevelLoadResult.Reject(
                        $"sink '{fixture.Id}' has no expectation; use \"-\" for every vector if it " +
                        "is meant to stay empty");
                }
            }

            int tickLimit = file.tickLimit > 0 ? file.tickLimit : DefaultTickLimit;

            string hint = string.IsNullOrWhiteSpace(file.hint) ? string.Empty : file.hint.Trim();

            return LevelLoadResult.Accept(new LevelDefinition(
                file.name.Trim(), hint, tickLimit, vectorCount, fixtures, budget, expectations));
        }

        private static bool TryBuildBudget(
            LevelBudgetFile[] raw, out List<LevelBudgetEntry> budget, out string error)
        {
            budget = new List<LevelBudgetEntry>(raw?.Length ?? 0);
            error = null;

            // An absent or empty budget is legal: it means a level solvable with wires alone.
            if (raw == null)
                return true;

            var takenKinds = new HashSet<GateKind>();

            for (int i = 0; i < raw.Length; i++)
            {
                LevelBudgetFile entry = raw[i];

                if (entry == null)
                {
                    error = $"budget entry {i} is empty";
                    return false;
                }

                if (!TryParseGateKind(entry.kind, out GateKind kind))
                {
                    error = $"budget entry {i} has kind '{entry.kind}'; expected one of " +
                            "Not, And, Or, Xor, Nand, Nor";
                    return false;
                }

                if (!takenKinds.Add(kind))
                {
                    error = $"the budget lists {GatePalette.Label(kind)} twice";
                    return false;
                }

                if (entry.count < 1)
                {
                    // Zero would be indistinguishable from leaving the kind out, which is already
                    // how a level says "you may not place this".
                    error = $"budget for {GatePalette.Label(kind)} is {entry.count}; " +
                            "omit the kind entirely to forbid it";
                    return false;
                }

                budget.Add(new LevelBudgetEntry(kind, entry.count));
            }

            return true;
        }

        private static bool TryBuildExpectations(
            LevelExpectationFile[] raw,
            List<LevelFixture> fixtures,
            int vectorCount,
            out List<LevelExpectation> expectations,
            out string error)
        {
            expectations = new List<LevelExpectation>(raw.Length);
            error = null;

            var takenSinks = new HashSet<string>();

            for (int i = 0; i < raw.Length; i++)
            {
                LevelExpectationFile entry = raw[i];

                if (entry == null)
                {
                    error = $"expectation {i} is empty";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.sink))
                {
                    error = $"expectation {i} names no sink";
                    return false;
                }

                string sinkId = entry.sink.Trim();
                LevelFixture target = FindFixture(fixtures, sinkId);

                if (target == null)
                {
                    error = $"expectation names '{sinkId}', which is not a fixture in this level";
                    return false;
                }

                if (target.Kind != FixtureKind.Sink)
                {
                    error = $"expectation names '{sinkId}', which is a source; only sinks receive bits";
                    return false;
                }

                if (!takenSinks.Add(sinkId))
                {
                    error = $"two expectations grade the sink '{sinkId}'";
                    return false;
                }

                if (string.IsNullOrEmpty(entry.values))
                {
                    error = $"expectation for '{sinkId}' has no values; use \"-\" per vector for an " +
                            "empty sink";
                    return false;
                }

                string values = entry.values.Trim();

                if (values.Length != vectorCount)
                {
                    error = $"expectation for '{sinkId}' has {values.Length} vectors but the sources " +
                            $"supply {vectorCount}";
                    return false;
                }

                var expected = new List<ExpectedBit>(values.Length);

                for (int vector = 0; vector < values.Length; vector++)
                {
                    char c = values[vector];

                    switch (c)
                    {
                        case '0': expected.Add(new ExpectedBit(Bit.Zero, vector)); break;
                        case '1': expected.Add(new ExpectedBit(Bit.One, vector)); break;
                        case '-': break;   // this vector produces nothing here
                        default:
                            error = $"expectation for '{sinkId}' has '{c}' at vector {vector}; " +
                                    "expected 0, 1 or -";
                            return false;
                    }
                }

                expectations.Add(new LevelExpectation(sinkId, values, expected));
            }

            return true;
        }

        /// <summary>
        /// Sources are dense: '0' and '1' only. A SourceNode emits one bit per tick from tick 0 with
        /// no way to skip a tick, so a '-' here has no meaning that could be honoured. Saying so is
        /// better than accepting it and emitting something else.
        /// </summary>
        private static bool TryParseStream(string stream, out Bit[] bits, out string error)
        {
            bits = Array.Empty<Bit>();
            error = null;

            if (string.IsNullOrWhiteSpace(stream))
            {
                error = "no stream; a source needs one character per test vector";
                return false;
            }

            string trimmed = stream.Trim();
            var parsed = new Bit[trimmed.Length];

            for (int i = 0; i < trimmed.Length; i++)
            {
                switch (trimmed[i])
                {
                    case '0': parsed[i] = Bit.Zero; break;
                    case '1': parsed[i] = Bit.One; break;
                    case '-':
                        error = $"'-' at vector {i}; a source emits every tick and cannot skip one, " +
                                "so sparse streams are not supported";
                        return false;
                    default:
                        error = $"'{trimmed[i]}' at vector {i}; expected 0 or 1";
                        return false;
                }
            }

            bits = parsed;
            return true;
        }

        private static bool TryParseFixtureKind(string text, out FixtureKind kind)
        {
            kind = FixtureKind.Source;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            switch (text.Trim().ToLowerInvariant())
            {
                case "source": kind = FixtureKind.Source; return true;
                case "sink": kind = FixtureKind.Sink; return true;
                default: return false;
            }
        }

        private static bool TryParseGateKind(string text, out GateKind kind)
        {
            kind = GateKind.Not;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            switch (text.Trim().ToLowerInvariant())
            {
                case "not": kind = GateKind.Not; return true;
                case "and": kind = GateKind.And; return true;
                case "or": kind = GateKind.Or; return true;
                case "xor": kind = GateKind.Xor; return true;
                case "nand": kind = GateKind.Nand; return true;
                case "nor": kind = GateKind.Nor; return true;
                default: return false;
            }
        }

        private static LevelFixture FindFixture(List<LevelFixture> fixtures, string id)
        {
            for (int i = 0; i < fixtures.Count; i++)
            {
                if (fixtures[i].Id == id)
                    return fixtures[i];
            }

            return null;
        }

        private static bool HasExpectationFor(List<LevelExpectation> expectations, string sinkId)
        {
            for (int i = 0; i < expectations.Count; i++)
            {
                if (expectations[i].SinkId == sinkId)
                    return true;
            }

            return false;
        }
    }
}
