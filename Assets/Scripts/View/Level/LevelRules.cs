using UnityEngine;

namespace BitSorter.View
{
    public enum LevelOutcome
    {
        Valid,

        /// <summary>A run is in progress or has finished; the board is read-only until Reset.</summary>
        NotEditing,

        /// <summary>The cell lies outside the playfield.</summary>
        OffBoard,

        /// <summary>Something is already on the cell -- a fixture, or another gate.</summary>
        CellTaken,

        /// <summary>This level does not offer that gate at all.</summary>
        NotInBudget,

        /// <summary>The level offers that gate, but every one is already placed.</summary>
        BudgetSpent,

        /// <summary>A source or sink the level pinned down. Not the player's to remove.</summary>
        Fixed,

        /// <summary>Nothing on that cell to remove. Silent: a right click means "delete a wire" next.</summary>
        NothingThere,

        /// <summary>A wire cannot be shorter than one tick.</summary>
        DelayAtMinimum,

        /// <summary>The level caps how many ticks one wire may carry.</summary>
        DelayAtMaximum,

        /// <summary>The level caps total added delay, and it is all spent.</summary>
        DelayBudgetSpent,

        /// <summary>No wire under the cursor. Silent.</summary>
        NoWire,
    }

    /// <summary>The result of asking whether an edit may happen.</summary>
    public readonly struct LevelVerdict
    {
        public readonly LevelOutcome Outcome;

        /// <summary>Player-facing reason, or null when the rejection should be silent.</summary>
        public readonly string Reason;

        private LevelVerdict(LevelOutcome outcome, string reason)
        {
            Outcome = outcome;
            Reason = reason;
        }

        public bool IsValid => Outcome == LevelOutcome.Valid;

        public static LevelVerdict Accept() => new LevelVerdict(LevelOutcome.Valid, null);

        public static LevelVerdict Reject(LevelOutcome outcome, string reason) =>
            new LevelVerdict(outcome, reason);

        public override string ToString() => IsValid ? "valid" : $"{Outcome}: {Reason ?? "(silent)"}";
    }

    /// <summary>
    /// Whether a level permits an edit: the budget, the fixed parts, the board edge, and the run
    /// state. Pure and static, so the whole matrix is testable without a scene.
    /// </summary>
    /// <remarks>
    /// Deliberately the same shape as <see cref="WiringRules"/> -- an outcome enum plus a nullable
    /// player-facing reason -- so both kinds of refusal reach the HUD through one channel,
    /// SimulationRunner.RejectEdit.
    ///
    /// Note the division of labour. This answers "may this edit happen", using only the level and the
    /// blueprint. Whether the resulting circuit is *correct* is a different question with a different
    /// answer path: that one runs the real simulation, in <see cref="LevelGrader"/>. Nothing here
    /// simulates anything.
    ///
    /// The budget's remaining count is always computed as budgeted minus placed, never stored, so it
    /// cannot drift out of step with the blueprint -- and removing a gate returns it to the pool with
    /// no bookkeeping at all.
    /// </remarks>
    public static class LevelRules
    {
        /// <summary>
        /// The shared gate every edit passes through first. Editing a running graph would mean adding
        /// and removing nodes mid-stream, so it is refused rather than queued.
        /// </summary>
        public static LevelVerdict CanEdit(RunState state)
        {
            if (state == RunState.Editing)
                return LevelVerdict.Accept();

            return LevelVerdict.Reject(LevelOutcome.NotEditing,
                state == RunState.Running
                    ? "press R to reset before editing"
                    : "press R to reset and edit");
        }

        /// <summary>Whether a gate of this kind may go on this cell.</summary>
        public static LevelVerdict CanPlace(
            LevelDefinition level,
            CircuitBlueprint blueprint,
            RunState state,
            GateKind kind,
            Vector2Int cell,
            Vector2Int halfExtents)
        {
            LevelVerdict gate = CanEdit(state);
            if (!gate.IsValid)
                return gate;

            if (Mathf.Abs(cell.x) > halfExtents.x || Mathf.Abs(cell.y) > halfExtents.y)
                return LevelVerdict.Reject(LevelOutcome.OffBoard, "that is off the board");

            LevelFixture fixedNode = level.FixtureAt(cell);
            if (fixedNode != null)
                return LevelVerdict.Reject(LevelOutcome.CellTaken, $"'{fixedNode.Id}' is there");

            if (blueprint.HasPlacementAt(cell))
                return LevelVerdict.Reject(LevelOutcome.CellTaken, "that cell is taken");

            int budgeted = level.BudgetFor(kind);
            string label = GatePalette.Label(kind);

            // Absent from the budget and exhausted are different messages, because they need
            // different reactions: one means "never in this level", the other "remove one first".
            if (budgeted <= 0)
                return LevelVerdict.Reject(LevelOutcome.NotInBudget, $"this level has no {label} gates");

            int placed = blueprint.CountOf(kind);

            if (placed >= budgeted)
            {
                return LevelVerdict.Reject(LevelOutcome.BudgetSpent,
                    $"no {label} left ({budgeted} of {budgeted} used)");
            }

            return LevelVerdict.Accept();
        }

        /// <summary>
        /// Whether whatever occupies this cell may be removed.
        /// </summary>
        /// <remarks>
        /// <see cref="LevelOutcome.NothingThere"/> is silent on purpose. A right click on an empty
        /// cell falls through to deleting the nearest wire, so scolding the player here would fire
        /// every single time they delete a wire. A fixture, by contrast, gets a message, and the
        /// caller must not fall through -- the click was aimed at something real.
        /// </remarks>
        public static LevelVerdict CanRemove(
            LevelDefinition level,
            CircuitBlueprint blueprint,
            RunState state,
            Vector2Int cell)
        {
            LevelVerdict gate = CanEdit(state);
            if (!gate.IsValid)
                return gate;

            LevelFixture fixedNode = level.FixtureAt(cell);
            if (fixedNode != null)
            {
                return LevelVerdict.Reject(LevelOutcome.Fixed,
                    $"'{fixedNode.Id}' is fixed -- it cannot be moved or removed");
            }

            if (!blueprint.HasPlacementAt(cell))
                return LevelVerdict.Reject(LevelOutcome.NothingThere, null);

            return LevelVerdict.Accept();
        }

        /// <summary>
        /// How many of a kind are still available. Negative is impossible: placement is gated on
        /// <see cref="CanPlace"/>.
        /// </summary>
        public static int RemainingFor(LevelDefinition level, CircuitBlueprint blueprint, GateKind kind) =>
            level.BudgetFor(kind) - blueprint.CountOf(kind);

        // -----------------------------------------------------------------
        // Wire delay
        // -----------------------------------------------------------------

        /// <summary>
        /// Whether a wire may be re-timed to <paramref name="targetDelay"/>.
        /// </summary>
        /// <remarks>
        /// The floor gets a message rather than staying silent. That a wire cannot be shorter than one
        /// tick is a real rule of the simulator -- a zero-delay edge would let one node see another's
        /// output inside a single tick, which is what makes evaluation order irrelevant -- and not an
        /// interface limitation. Scrolling into the floor is exactly where a player wonders why.
        ///
        /// The cap and the budget are separate refusals because they need different reactions: one
        /// means "not on this wire", the other "take it off another wire first".
        /// </remarks>
        public static LevelVerdict CanSetDelay(
            LevelDefinition level,
            CircuitBlueprint blueprint,
            RunState state,
            int currentDelay,
            int targetDelay)
        {
            LevelVerdict gate = CanEdit(state);
            if (!gate.IsValid)
                return gate;

            if (targetDelay == currentDelay)
                return LevelVerdict.Reject(LevelOutcome.NoWire, null);   // scrolled, nothing to do

            if (targetDelay < 1)
                return LevelVerdict.Reject(LevelOutcome.DelayAtMinimum, "1 is the shortest a wire can be");

            if (targetDelay > level.MaxWireDelay)
            {
                return level.MaxWireDelay <= 1
                    ? LevelVerdict.Reject(LevelOutcome.DelayAtMaximum, "this level has fixed wiring")
                    : LevelVerdict.Reject(LevelOutcome.DelayAtMaximum,
                        $"this level caps wires at {level.MaxWireDelay}");
            }

            // Shortening always fits: it can only give budget back.
            if (targetDelay < currentDelay || !level.HasDelayBudget)
                return LevelVerdict.Accept();

            int spentAfter = blueprint.ExtraDelay() + (targetDelay - currentDelay);

            if (spentAfter > level.DelayBudget)
            {
                return LevelVerdict.Reject(LevelOutcome.DelayBudgetSpent,
                    $"no delay budget left ({blueprint.ExtraDelay()} of {level.DelayBudget} used)");
            }

            return LevelVerdict.Accept();
        }

        /// <summary>
        /// Ticks of delay the player may still add, or -1 when the level sets no budget.
        /// </summary>
        public static int RemainingDelay(LevelDefinition level, CircuitBlueprint blueprint) =>
            level.HasDelayBudget ? level.DelayBudget - blueprint.ExtraDelay() : -1;
    }
}
