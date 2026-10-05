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
        /// Used when a file omits tickLimit. Generous: the largest board is 13 by 7 cells, and free
        /// play's longest input -- eight vectors at a clock of six -- has played its last bit by tick
        /// 42, so no honestly built circuit comes near it. It exists only to stop an oscillator -- a
        /// gate fed by its own output, which WiringRules deliberately allows -- from hanging a run
        /// forever.
        /// </summary>
        /// <remarks>
        /// Worth setting per level rather than leaning on this. The limit is spent in real time at the
        /// runner's tick interval, so it is also how long a player stares at a circuit that is never
        /// going to finish: 100 ticks at the default half-second tick is nearly a minute. Every shipped
        /// level sets its own, at 40, 60 or 80; free play and the tutorial are what fall back to this. R
        /// interrupts a run at any point, so this is a backstop rather than the only way out.
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

            // The level's own board wins; the one passed in is only the board of a level that
            // does not say, which is every level written before a level could.
            if (!TryBoard(file.board, halfExtents, out halfExtents, out string boardError))
                return LevelLoadResult.Reject(boardError);

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

            if (!TryBuildBlocks(file.blocks, out List<BlockDefinition> blocks, out string blocksError))
                return LevelLoadResult.Reject(blocksError);

            if (!TryBuildBudget(file.budget, blocks, out List<LevelBudgetEntry> budget,
                    out List<LevelBlockBudgetEntry> blockBudget, out string budgetError))
            {
                return LevelLoadResult.Reject(budgetError);
            }

            if (!TryBuildExpectations(file.expected, fixtures, vectorCount, TailRoom(budget),
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

            // Negative is a mistake worth naming. Zero is not: JsonUtility yields 0 for a missing key,
            // so zero has to keep meaning "unspecified".
            if (file.maxWireDelay < 0)
            {
                return LevelLoadResult.Reject(
                    $"maxWireDelay is {file.maxWireDelay}; use 1 to forbid re-timing, or leave it out");
            }

            if (file.delayBudget < 0)
            {
                return LevelLoadResult.Reject(
                    $"delayBudget is {file.delayBudget}; leave it out for no limit, or set " +
                    "maxWireDelay to 1 to forbid re-timing");
            }

            if (file.maxLatency < 0)
            {
                return LevelLoadResult.Reject(
                    $"maxLatency is {file.maxLatency}; leave it out for no limit, or give the " +
                    "critical path of the intended solution in ticks");
            }

            if (file.clockPeriod < 0)
            {
                return LevelLoadResult.Reject(
                    $"clockPeriod is {file.clockPeriod}; leave it out for a vector every tick, or " +
                    "give the ticks between vectors");
            }

            // Only the shape is checkable here. Whether this value is unique across the level set is
            // LevelCatalog's call -- one file has no way to see another.
            if (file.order < 0)
            {
                return LevelLoadResult.Reject(
                    $"order is {file.order}; use a positive place in the run, or leave it out to " +
                    "sort to the end");
            }

            int maxWireDelay = file.maxWireDelay > 0
                ? file.maxWireDelay
                : LevelDefinition.DefaultMaxWireDelay;

            string hint = string.IsNullOrWhiteSpace(file.hint) ? string.Empty : file.hint.Trim();
            string goal = string.IsNullOrWhiteSpace(file.goal) ? string.Empty : file.goal.Trim();

            LevelDefinition Define(BlueprintSnapshot start) => new LevelDefinition(
                file.name.Trim(), hint, tickLimit, vectorCount, fixtures, budget, expectations,
                maxWireDelay, file.delayBudget, file.maxLatency, file.order, goal,
                clockPeriod: file.clockPeriod, boardHalfExtents: halfExtents, start: start,
                blocks: blocks, blockBudget: blockBudget);

            // A start is checked against the level it belongs to -- its board, fixtures, budget and
            // wire limits -- so the level is defined once without it to be asked, then again with it.
            LevelDefinition level = Define(null);

            if (!TryBuildStart(file.start, level, halfExtents, out BlueprintSnapshot starting, out string startError))
                return LevelLoadResult.Reject(startError);

            return LevelLoadResult.Accept(starting == null ? level : Define(starting));
        }

        /// <summary>
        /// A level's starting circuit, checked as a board the player could have built, or null when
        /// the level opens on an empty one.
        /// </summary>
        /// <remarks>
        /// Every check runs before the part or wire goes onto the scratch board, because the board
        /// itself guards less than this does: <see cref="CircuitBlueprint.Place"/> throws on a taken
        /// cell and <see cref="CircuitBlueprint.AddWire"/> checks no delay, so a bad file would throw
        /// here, or later inside the level load, rather than be refused with a reason. The port counts
        /// are the save path's own (<see cref="BoardSerializer"/>), so a start that loads is one a
        /// save of it restores.
        ///
        /// Only a fixture's cell is off limits: the reserved edge columns belong to free play alone.
        /// And a second wire into one input is refused although the game allows it, because in a
        /// file it is far likelier to be a forgotten <c>toPort</c>, read as 0, than a design.
        /// </remarks>
        private static bool TryBuildStart(
            LevelStartFile raw, LevelDefinition level, Vector2Int halfExtents,
            out BlueprintSnapshot start, out string error)
        {
            start = null;
            error = null;

            int gateCount = raw?.gates?.Length ?? 0;
            int wireCount = raw?.wires?.Length ?? 0;

            // JsonUtility may hand back an empty object for a missing one, as it does for board.
            if (gateCount == 0 && wireCount == 0)
                return true;

            var board = new CircuitBlueprint();

            for (int i = 0; i < gateCount; i++)
            {
                LevelStartGateFile gate = raw.gates[i];

                if (gate == null)
                {
                    error = $"start gate {i} is empty";
                    return false;
                }

                if (!TryParseGateKind(gate.kind, out GateKind kind))
                {
                    error = $"start gate {i} has kind '{gate.kind}'; expected one of " +
                            string.Join(", ", Enum.GetNames(typeof(GateKind)));
                    return false;
                }

                var cell = new Vector2Int(gate.cell.x, gate.cell.y);
                string label = GatePalette.Label(kind);

                if (Mathf.Abs(cell.x) > halfExtents.x || Mathf.Abs(cell.y) > halfExtents.y)
                {
                    error = $"the start's {label} sits at {cell}, outside the board " +
                            $"(x within {halfExtents.x}, y within {halfExtents.y})";
                    return false;
                }

                LevelFixture under = level.FixtureAt(cell);

                if (under != null || level.IsReserved(cell))
                {
                    error = $"the start's {label} sits at {cell}, on the fixture '{under?.Id}'";
                    return false;
                }

                if (board.HasPlacementAt(cell))
                {
                    error = $"two of the start's gates share the cell {cell}";
                    return false;
                }

                int budgeted = level.BudgetFor(kind);

                if (budgeted == 0)
                {
                    error = $"the start places a {label} but the budget stocks none; the budget counts " +
                            "the start's parts as well as the spares";
                    return false;
                }

                if (!level.IsUnlimited(kind) && board.CountOf(kind) >= budgeted)
                {
                    error = $"the start places more than the budget's {budgeted} {label}; the budget " +
                            "counts the start's parts as well as the spares";
                    return false;
                }

                board.Place(cell, kind);
            }

            for (int i = 0; i < wireCount; i++)
            {
                LevelStartWireFile wire = raw.wires[i];

                if (wire == null)
                {
                    error = $"start wire {i} is empty";
                    return false;
                }

                var fromCell = new Vector2Int(wire.from.x, wire.from.y);
                var toCell = new Vector2Int(wire.to.x, wire.to.y);

                int outputs = BoardSerializer.OutputsAt(fromCell, level, board);

                if (outputs == 0)
                {
                    error = $"start wire {i} runs from {fromCell}, where there is no output -- " +
                            "nothing there, or a sink";
                    return false;
                }

                if (wire.fromPort < 0 || wire.fromPort >= outputs)
                {
                    error = $"start wire {i} leaves output {wire.fromPort} at {fromCell}, which has {outputs}";
                    return false;
                }

                int inputs = BoardSerializer.InputsAt(toCell, level, board);

                if (inputs == 0)
                {
                    error = $"start wire {i} runs to {toCell}, where there is no input -- " +
                            "nothing there, or a source";
                    return false;
                }

                if (wire.toPort < 0 || wire.toPort >= inputs)
                {
                    error = $"start wire {i} enters input {wire.toPort} at {toCell}, which has {inputs}, " +
                            "counted from 0";
                    return false;
                }

                int delay = wire.delay == 0 ? 1 : wire.delay;

                if (delay < 1 || delay > level.MaxWireDelay)
                {
                    error = $"start wire {i} has delay {wire.delay}; it may be from 1 to {level.MaxWireDelay}, " +
                            "or left out for 1";
                    return false;
                }

                var from = new CellPort(fromCell, false, wire.fromPort);
                var to = new CellPort(toCell, true, wire.toPort);

                if (board.HasWire(from, to))
                {
                    error = $"start wire {i} repeats an earlier one";
                    return false;
                }

                for (int j = 0; j < board.Wires.Count; j++)
                {
                    if (board.Wires[j].To.Equals(to))
                    {
                        error = $"start wire {i} is a second wire into input {wire.toPort} at {toCell}; " +
                                "check its toPort, which reads as 0 when left out";
                        return false;
                    }
                }

                // A wire from a part back into itself is allowed: a register's feedback is one.
                board.AddWire(new BlueprintWire(from, to, delay));
            }

            if (level.HasDelayBudget && board.ExtraDelay() > level.DelayBudget)
            {
                error = $"the start's wires add {board.ExtraDelay()} ticks of delay, more than the " +
                        $"delayBudget of {level.DelayBudget}";
                return false;
            }

            start = board.Snapshot();
            return true;
        }

        /// <summary>The smallest board a level may name: the standard one.</summary>
        public const int MinColumns = 9;

        /// <inheritdoc cref="MinColumns"/>
        public const int MinRows = 5;

        /// <summary>
        /// The largest board a level may name. Past it a cell is drawn too small to read, since the
        /// board is fitted to the screen and never panned.
        /// </summary>
        public const int MaxColumns = 13;

        /// <inheritdoc cref="MaxColumns"/>
        public const int MaxRows = 7;

        /// <summary>
        /// The board a level is played on: its own, if it names one, or the fallback.
        /// </summary>
        private static bool TryBoard(
            LevelBoardFile board, Vector2Int fallback, out Vector2Int halfExtents, out string error)
        {
            halfExtents = fallback;
            error = null;

            if (board == null || (board.columns == 0 && board.rows == 0))
                return true;

            if (board.columns <= 0 || board.rows <= 0)
            {
                error = $"board is {board.columns} by {board.rows}; give both columns and rows, or leave it out";
                return false;
            }

            if (board.columns % 2 == 0 || board.rows % 2 == 0)
            {
                error = $"board is {board.columns} by {board.rows}; both must be odd, because the grid is " +
                        "centred on the middle cell";
                return false;
            }

            if (board.columns < MinColumns || board.columns > MaxColumns ||
                board.rows < MinRows || board.rows > MaxRows)
            {
                error = $"board is {board.columns} by {board.rows}; it may be from {MinColumns} by {MinRows} " +
                        $"to {MaxColumns} by {MaxRows}";
                return false;
            }

            halfExtents = new Vector2Int((board.columns - 1) / 2, (board.rows - 1) / 2);
            return true;
        }

        /// <summary>
        /// A level's own blocks, each held to <see cref="BlockRules"/> as a free-play block is.
        /// </summary>
        /// <remarks>
        /// Written as a start is -- gates and wires by cell -- with named inputs and outputs standing
        /// where a board's sources and sinks would. A wire's delay left out reads as 1, as a start's
        /// does.
        /// </remarks>
        private static bool TryBuildBlocks(LevelBlockFile[] raw, out List<BlockDefinition> blocks, out string error)
        {
            blocks = new List<BlockDefinition>(raw?.Length ?? 0);
            error = null;

            if (raw == null)
                return true;

            var names = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < raw.Length; i++)
            {
                LevelBlockFile entry = raw[i];

                if (entry == null)
                {
                    error = $"block {i} is empty";
                    return false;
                }

                string label = string.IsNullOrWhiteSpace(entry.name) ? $"block {i}" : $"block '{entry.name.Trim()}'";
                var gates = new List<GatePlacement>(entry.gates?.Length ?? 0);

                for (int g = 0; g < (entry.gates?.Length ?? 0); g++)
                {
                    LevelStartGateFile gate = entry.gates[g];

                    if (gate == null || !TryParseGateKind(gate.kind, out GateKind kind))
                    {
                        error = $"{label}: gate {g} has kind '{gate?.kind}'; expected one of " +
                                string.Join(", ", Enum.GetNames(typeof(GateKind)));
                        return false;
                    }

                    gates.Add(new GatePlacement(new Vector2Int(gate.cell.x, gate.cell.y), kind));
                }

                var wires = new List<BlueprintWire>(entry.wires?.Length ?? 0);

                for (int w = 0; w < (entry.wires?.Length ?? 0); w++)
                {
                    LevelStartWireFile wire = entry.wires[w];

                    if (wire == null)
                    {
                        error = $"{label}: wire {w} is empty";
                        return false;
                    }

                    wires.Add(new BlueprintWire(
                        new CellPort(new Vector2Int(wire.from.x, wire.from.y), false, wire.fromPort),
                        new CellPort(new Vector2Int(wire.to.x, wire.to.y), true, wire.toPort),
                        wire.delay == 0 ? 1 : wire.delay));
                }

                if (!BlockRules.TryDefine(entry.name, BlockPorts(entry.inputs), BlockPorts(entry.outputs), gates, wires,
                        out BlockDefinition block, out string refusal))
                {
                    error = $"{label}: {refusal}";
                    return false;
                }

                if (!names.Add(block.Name))
                {
                    error = $"two blocks are called '{block.Name}'";
                    return false;
                }

                blocks.Add(block);
            }

            return true;
        }

        private static BlockPort[] BlockPorts(LevelBlockPortFile[] raw)
        {
            var ports = new BlockPort[raw?.Length ?? 0];

            for (int i = 0; i < ports.Length; i++)
            {
                LevelBlockPortFile port = raw[i];
                ports[i] = port == null
                    ? new BlockPort(null, default)
                    : new BlockPort(port.id?.Trim(), new Vector2Int(port.cell.x, port.cell.y));
            }

            return ports;
        }

        private static bool TryBuildBudget(
            LevelBudgetFile[] raw, List<BlockDefinition> blocks, out List<LevelBudgetEntry> budget,
            out List<LevelBlockBudgetEntry> blockBudget, out string error)
        {
            budget = new List<LevelBudgetEntry>(raw?.Length ?? 0);
            blockBudget = new List<LevelBlockBudgetEntry>();
            error = null;

            var takenBlocks = new HashSet<string>(StringComparer.Ordinal);

            // An absent or empty budget is legal: it means a level solvable with wires alone.
            for (int i = 0; i < (raw?.Length ?? 0); i++)
            {
                LevelBudgetFile entry = raw[i];

                if (entry != null && !string.IsNullOrWhiteSpace(entry.block))
                {
                    string name = entry.block.Trim();

                    if (!string.IsNullOrWhiteSpace(entry.kind))
                    {
                        error = $"budget entry {i} names both the kind '{entry.kind}' and the block '{name}'; " +
                                "an entry is one or the other";
                        return false;
                    }

                    if (blocks.Find(b => b.Name == name) == null)
                    {
                        error = $"budget entry {i} stocks the block '{name}', which the level does not define";
                        return false;
                    }

                    if (!takenBlocks.Add(name))
                    {
                        error = $"the budget lists the block {name} twice";
                        return false;
                    }

                    if (entry.count < 1)
                    {
                        error = $"budget for the block {name} is {entry.count}; omit it entirely to forbid it";
                        return false;
                    }

                    blockBudget.Add(new LevelBlockBudgetEntry(name, entry.count));
                    continue;
                }

                if (!TryBuildGateEntry(entry, i, budget, out error))
                    return false;
            }

            if (budget.Count + blockBudget.Count > LevelDefinition.MaxPartsRows)
            {
                error = $"the budget lists {budget.Count + blockBudget.Count} parts, and the parts list " +
                        $"shows at most {LevelDefinition.MaxPartsRows}";
                return false;
            }

            // A block defined and never stocked is a block the player can never place: a typo in the
            // budget, almost always, rather than a design.
            for (int b = 0; b < blocks.Count; b++)
            {
                if (!takenBlocks.Contains(blocks[b].Name))
                {
                    error = $"the level defines the block '{blocks[b].Name}' but its budget never stocks it";
                    return false;
                }
            }

            return true;
        }

        /// <summary>One gate's row of the budget, refused if it repeats a kind already listed.</summary>
        private static bool TryBuildGateEntry(
            LevelBudgetFile entry, int i, List<LevelBudgetEntry> budget, out string error)
        {
            error = null;

            if (entry == null)
            {
                error = $"budget entry {i} is empty";
                return false;
            }

            if (!GatePalette.TryParse(entry.kind, out GateKind kind))
            {
                error = $"budget entry {i} has kind '{entry.kind}'; expected one of " +
                        string.Join(", ", System.Enum.GetNames(typeof(GateKind)));
                return false;
            }

            for (int k = 0; k < budget.Count; k++)
            {
                if (budget[k].Kind == kind)
                {
                    error = $"the budget lists {GatePalette.Label(kind)} twice";
                    return false;
                }
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
            return true;
        }

        /// <summary>
        /// How many cycles an expectation may run past the last vector.
        /// </summary>
        /// <remarks>
        /// Only a register can put a bit out after the streams have finished, and it shifts by one
        /// clock -- so a chain of every register the level stocks is the longest tail that can ever
        /// be satisfied. Anything past that is asking for a bit nothing could produce, which is a
        /// typo in a level file rather than a design.
        ///
        /// An unlimited count means no bound can be worked out, so none is imposed. No shipped
        /// level stocks registers that way; free play is built in code and never comes through here.
        /// </remarks>
        private static int TailRoom(List<LevelBudgetEntry> budget)
        {
            int registers = 0;

            for (int i = 0; i < budget.Count; i++)
            {
                if (budget[i].Kind != GateKind.Register)
                    continue;

                if (budget[i].Count < 0)
                    return int.MaxValue;

                registers += budget[i].Count;
            }

            return registers;
        }

        private static bool TryBuildExpectations(
            LevelExpectationFile[] raw,
            List<LevelFixture> fixtures,
            int vectorCount,
            int tailRoom,
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

                if (values.Length < vectorCount)
                {
                    error = $"expectation for '{sinkId}' has {values.Length} vectors but the sources " +
                            $"supply {vectorCount}";
                    return false;
                }

                // Longer than the vector count is allowed, and it is what a register makes happen: a
                // sink fed through one receives the bit it started out holding before the first
                // vector's, so one more bit arrives than there were vectors. Those trailing
                // characters are the clock cycles after the last input.
                //
                // The tail cannot be silent. A '-' is dropped rather than taking a slot, so "silence
                // after the end" is what an expectation of exactly the vector count already says.
                if (values.Length > vectorCount && values.IndexOf('-', vectorCount) >= 0)
                {
                    error = $"expectation for '{sinkId}' has '-' after the last vector; leave those " +
                            "characters off instead";
                    return false;
                }

                // And no longer than the registers could shift it. A tail is a register handing on
                // what it was holding, so a chain of every register the level stocks is the most
                // any sink can receive after the streams stop. Longer is a bit nothing on the board
                // could produce, and without this it loaded happily and failed at run time with
                // MissingOutput -- a level file's typo reported as the player's mistake.
                if (tailRoom != int.MaxValue && values.Length > vectorCount + tailRoom)
                {
                    error = $"expectation for '{sinkId}' has {values.Length} values for " +
                            $"{vectorCount} vectors, and the budget stocks {tailRoom} register(s) " +
                            $"-- at most {vectorCount + tailRoom} can ever arrive";
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

                        // 'x' and '-' are opposites. A don't-care still expects a bit and so keeps
                        // its slot; a silent vector expects none and is dropped. Only the second
                        // shortens the list, which is why only the second can shift what follows it.
                        case 'x': expected.Add(ExpectedBit.Any(vector)); break;
                        case '-': break;   // this vector produces nothing here

                        default:
                            error = $"expectation for '{sinkId}' has '{c}' at vector {vector}; " +
                                    "expected 0, 1, x or -";
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
                        // A source can stay quiet on a tick, but a level says so with clockPeriod
                        // rather than by hand: every source has to keep the same beat, and a gap
                        // written into one stream would silently put that source out of step with
                        // the others. The vector count is the length of the stream either way.
                        error = $"'-' at vector {i}; a stream is the vectors themselves, " +
                                "and clockPeriod is what spaces them out";
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

        /// <summary>
        /// The shape of this file's other parsers, for the parts of a starting circuit. It is
        /// <see cref="GatePalette.TryParse"/>, which is the one place a kind name becomes a
        /// <see cref="GateKind"/>.
        /// </summary>
        /// <remarks>
        /// This used to be a second switch listing the six gates, beside the one in GatePalette
        /// that says in its own remarks that it is the only one. They drifted the moment a seventh
        /// part existed: the register was placeable, saveable and testable, and the one thing it
        /// could not be was written into a level file.
        /// </remarks>
        private static bool TryParseGateKind(string text, out GateKind kind)
        {
            return GatePalette.TryParse(text, out kind);
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
