using System.Collections.Generic;
using BitSorter.LogicCore;

namespace BitSorter.View
{
    /// <summary>Where a level attempt currently stands.</summary>
    public enum RunState
    {
        /// <summary>Clock held at tick 0, edits allowed.</summary>
        Editing,

        /// <summary>Streaming the test vectors. Edits refused.</summary>
        Running,

        Passed,
        Failed,
    }

    public enum RunOutcome
    {
        Pass,

        /// <summary>The tick limit ran out with bits still moving -- almost always a feedback loop.</summary>
        NeverSettled,

        /// <summary>Bits collided at an input port and were destroyed.</summary>
        Corrupted,

        /// <summary>A sink received fewer bits than the level expects.</summary>
        MissingOutput,

        /// <summary>A sink received the right number of bits, but one has the wrong value.</summary>
        WrongOutput,

        /// <summary>A sink received bits it should not have -- including any bit at all in an empty bin.</summary>
        ExtraOutput,
    }

    /// <summary>How a run ended, with one sentence the player can act on.</summary>
    public readonly struct RunVerdict
    {
        public readonly RunOutcome Outcome;

        /// <summary>Always populated, pass or fail.</summary>
        public readonly string Reason;

        /// <summary>The test vector at fault, or -1 when the failure cannot be pinned to one.</summary>
        public readonly int Vector;

        /// <summary>The sink at fault, or null for a whole-run failure.</summary>
        public readonly string SinkId;

        private RunVerdict(RunOutcome outcome, string reason, int vector, string sinkId)
        {
            Outcome = outcome;
            Reason = reason;
            Vector = vector;
            SinkId = sinkId;
        }

        public bool IsPass => Outcome == RunOutcome.Pass;

        public static RunVerdict Pass(string reason) =>
            new RunVerdict(RunOutcome.Pass, reason, -1, null);

        public static RunVerdict Fail(RunOutcome outcome, string reason, int vector = -1, string sinkId = null) =>
            new RunVerdict(outcome, reason, vector, sinkId);

        public override string ToString() => $"{Outcome}: {Reason}";
    }

    /// <summary>
    /// Decides whether a finished run satisfies its level.
    /// </summary>
    /// <remarks>
    /// Reads the results of the real simulation -- the same one the player just watched. There is no
    /// second model of the circuit anywhere in the game, deliberately: the entire subject here is
    /// consume semantics, collision poisoning and delay arithmetic, and a separate oracle would have
    /// to reimplement all three. Any divergence between oracle and simulator is a bug the player
    /// experiences as the game lying to them.
    ///
    /// The pass rule, in full:
    ///
    ///   the run went idle within the tick limit,
    ///   and CorruptedCount is zero,
    ///   and every sink's received value sequence equals its expected sequence.
    ///
    /// Two decisions inside that are worth knowing.
    ///
    /// Ticks are ignored, values and order are not. A Reception carries the tick it landed on, but
    /// absolute arrival time depends on how long a path the player routed, and that is the player's
    /// choice. Grading on exact ticks would fail a correct circuit for taking the scenic route. Order
    /// and count still carry the timing information that matters: a path too slow to keep up drops or
    /// reorders bits, and that fails here.
    ///
    /// Every sink is graded, including the ones expected to stay empty. Without that, this level's
    /// routing puzzle is passable by wiring the source into *both* bins -- the right bin gets its bit
    /// and nobody checks the other one. A sink with no expectation is held to the empty sequence.
    /// </remarks>
    public static class LevelGrader
    {
        /// <summary>
        /// True once nothing can ever happen again: no bits are in transit and no source has anything
        /// left to emit. The signal that a run has finished and may be graded.
        /// </summary>
        /// <remarks>
        /// Deliberately does not require the input ports to be empty. With unequal path delays the
        /// faster stream runs out first, stranding the slower stream's last bit in a port whose partner
        /// will never arrive. That is a terminal state, not a busy one -- a node with every port filled
        /// would already have fired during the last evaluate phase, so if nothing is in flight, nothing
        /// can become ready. Requiring empty ports would hang the run on that stranded bit, and the
        /// grader would never get to report the far more useful truth that a bit went missing.
        /// </remarks>
        public static bool IsSettled(SimulationView view)
        {
            for (int id = 0; id < view.EdgeCount; id++)
            {
                Edge edge = view.GetEdge(id);   // null for a removed id
                if (edge != null && edge.InTransitCount > 0)
                    return false;
            }

            // The 'is' pattern already yields false for a null, so removed ids fall through safely.
            for (int id = 0; id < view.NodeCount; id++)
            {
                if (view.GetNode(id) is SourceNode source && !source.IsExhausted)
                    return false;
            }

            return true;
        }

        /// <summary>Whether the run has used up its tick budget without settling.</summary>
        public static bool HasTimedOut(SimulationView view, LevelDefinition level) =>
            view.CurrentTick >= level.TickLimit;

        /// <summary>
        /// Ticks until the run settles or the tick limit is reached, then grades it. The game does not
        /// use this -- it ticks on a wall clock and polls the two predicates above between frames --
        /// but the rules are shared, so the two drivers cannot disagree about when a run is over.
        /// </summary>
        public static RunVerdict RunToCompletion(
            Simulation simulation,
            LevelDefinition level,
            IReadOnlyDictionary<string, int> sinkNodeIds)
        {
            while (!IsSettled(simulation.View) && !HasTimedOut(simulation.View, level))
                simulation.Tick();

            return Grade(simulation.View, level, sinkNodeIds, IsSettled(simulation.View));
        }

        /// <summary>
        /// Grades a settled run. <paramref name="sinkNodeIds"/> maps fixture id to node id, as
        /// produced by the rebuild; it is only ever looked up, never iterated, so its ordering is
        /// irrelevant. Failures are reported one at a time, in level-file order, because a player
        /// fixes one thing at a time.
        /// </summary>
        public static RunVerdict Grade(
            SimulationView view,
            LevelDefinition level,
            IReadOnlyDictionary<string, int> sinkNodeIds,
            bool settled)
        {
            if (!settled)
            {
                return RunVerdict.Fail(RunOutcome.NeverSettled,
                    $"the circuit never settled within {level.TickLimit} ticks -- something is " +
                    "feeding itself");
            }

            // Checked before the sequences. A collision usually breaks a sequence too, but "2 bits
            // destroyed" tells the player far more than "expected 1, received nothing" does.
            if (view.CorruptedCount > 0)
            {
                int destroyed = view.CorruptedCount;

                return RunVerdict.Fail(RunOutcome.Corrupted,
                    $"{destroyed} {(destroyed == 1 ? "bit was" : "bits were")} destroyed in a " +
                    "collision -- two bits reached the same input port");
            }

            for (int i = 0; i < level.Expectations.Count; i++)
            {
                RunVerdict verdict = GradeSink(view, level.Expectations[i], sinkNodeIds);

                if (!verdict.IsPass)
                    return verdict;
            }

            return RunVerdict.Pass($"all {level.VectorCount} " +
                                   $"{(level.VectorCount == 1 ? "vector" : "vectors")} correct");
        }

        private static RunVerdict GradeSink(
            SimulationView view,
            LevelExpectation expectation,
            IReadOnlyDictionary<string, int> sinkNodeIds)
        {
            string sinkId = expectation.SinkId;

            if (!sinkNodeIds.TryGetValue(sinkId, out int nodeId) ||
                !(NodeAt(view, nodeId) is SinkNode sink))
            {
                // Unreachable through the game: the level validated and the graph was just built from
                // it. Reported rather than thrown so a wiring mistake in a rebuild cannot crash play.
                return RunVerdict.Fail(RunOutcome.MissingOutput,
                    $"internal: sink '{sinkId}' is not in the graph", -1, sinkId);
            }

            IReadOnlyList<SinkNode.Reception> received = sink.Received;
            IReadOnlyList<ExpectedBit> expected = expectation.Expected;

            for (int k = 0; k < expected.Count; k++)
            {
                ExpectedBit want = expected[k];

                if (k >= received.Count)
                {
                    return RunVerdict.Fail(RunOutcome.MissingOutput,
                        $"vector {want.Vector}: {sinkId} expected {(int)want.Value}, received nothing",
                        want.Vector, sinkId);
                }

                Bit got = received[k].Value;

                if (got != want.Value)
                {
                    return RunVerdict.Fail(RunOutcome.WrongOutput,
                        $"vector {want.Vector}: {sinkId} expected {(int)want.Value}, got {(int)got}",
                        want.Vector, sinkId);
                }
            }

            if (received.Count <= expected.Count)
                return RunVerdict.Pass(null);

            int extra = received.Count - expected.Count;
            string bits = extra == 1 ? "bit" : "bits";

            // An empty bin gets its own wording. This is the branch that catches wiring one source
            // into every bin to see which one sticks.
            return expected.Count == 0
                ? RunVerdict.Fail(RunOutcome.ExtraOutput,
                    $"{sinkId} should have stayed empty, but {extra} {bits} arrived", -1, sinkId)
                : RunVerdict.Fail(RunOutcome.ExtraOutput,
                    $"{sinkId} received {extra} {bits} more than expected", -1, sinkId);
        }

        /// <summary>Null for a retired id, and for an id outside the issued range.</summary>
        private static Node NodeAt(SimulationView view, int nodeId) =>
            nodeId >= 0 && nodeId < view.NodeCount ? view.GetNode(nodeId) : null;
    }
}
