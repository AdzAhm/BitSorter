using BitSorter.LogicCore;

namespace BitSorter.View
{
    public enum WiringOutcome
    {
        Valid,

        /// <summary>Nothing clickable at the release point, or the port index no longer exists.</summary>
        NoPort,

        /// <summary>Released on the port the drag started from -- a click, not a drag.</summary>
        SamePort,

        /// <summary>Output to output, or input to input.</summary>
        SameKind,

        /// <summary>A node involved was removed while the drag was in progress.</summary>
        MissingNode,

        /// <summary>This exact output-to-input pair is already wired.</summary>
        Duplicate,
    }

    /// <summary>The result of validating a drag, with the resolved ports when it is valid.</summary>
    public readonly struct WiringVerdict
    {
        public readonly WiringOutcome Outcome;

        /// <summary>Player-facing reason, or null when the rejection should be silent.</summary>
        public readonly string Reason;

        public readonly OutputPort Source;
        public readonly InputPort Target;

        private WiringVerdict(WiringOutcome outcome, string reason, OutputPort source, InputPort target)
        {
            Outcome = outcome;
            Reason = reason;
            Source = source;
            Target = target;
        }

        public bool IsValid => Outcome == WiringOutcome.Valid;

        public static WiringVerdict Accept(OutputPort source, InputPort target) =>
            new WiringVerdict(WiringOutcome.Valid, null, source, target);

        public static WiringVerdict Reject(WiringOutcome outcome, string reason) =>
            new WiringVerdict(outcome, reason, null, null);

        public override string ToString() => IsValid ? "valid" : $"{Outcome}: {Reason ?? "(silent)"}";
    }

    /// <summary>
    /// Decides whether a drag between two ports may become an edge. Pure and free of UnityEngine
    /// types, so the whole matrix is testable without the engine or a MonoBehaviour.
    /// </summary>
    /// <remarks>
    /// Two things that look like mistakes are deliberately legal:
    ///
    /// Fan-in -- a second wire into an already-wired input, from a different output. LogicCore
    /// documents fan-in as how collisions arise and has a test that depends on it, so refusing it
    /// here would make a tested capability unreachable. The circuit it builds collides whenever
    /// both bits land together, which is a real mistake the player can then diagnose through
    /// CorruptedCount.
    ///
    /// Self-loops -- an output wired to an input on the same node. Well defined because every edge
    /// delay is at least 1, and it is the shape the planned RegisterNode work will need.
    ///
    /// Note what is never consulted: InputPort.IsOccupied, meaning the port currently holds a bit.
    /// That is transient state that changes every tick, not a wiring constraint. Gating on it
    /// would make ports randomly unwireable depending on which tick the player paused.
    /// </remarks>
    public static class WiringRules
    {
        public static WiringVerdict Validate(SimulationView view, PortAddress from, PortAddress to)
        {
            if (!from.IsValid || !to.IsValid)
                return WiringVerdict.Reject(WiringOutcome.NoPort, "No port there.");

            if (from == to)
                return WiringVerdict.Reject(WiringOutcome.SamePort, null);   // a click, stay quiet

            if (from.IsInput == to.IsInput)
                return WiringVerdict.Reject(WiringOutcome.SameKind, "Outputs connect to inputs.");

            // Either end may have been grabbed first; the edge is always output to input.
            PortAddress outputEnd = from.IsInput ? to : from;
            PortAddress inputEnd = from.IsInput ? from : to;

            Node outputNode = NodeAt(view, outputEnd);
            Node inputNode = NodeAt(view, inputEnd);

            if (outputNode == null || inputNode == null)
                return WiringVerdict.Reject(WiringOutcome.MissingNode, "That node is gone.");

            if (outputEnd.Index < 0 || outputEnd.Index >= outputNode.OutputCount ||
                inputEnd.Index < 0 || inputEnd.Index >= inputNode.InputCount)
            {
                return WiringVerdict.Reject(WiringOutcome.NoPort, "No port there.");
            }

            OutputPort source = outputNode.Out(outputEnd.Index);
            InputPort target = inputNode.In(inputEnd.Index);

            // A duplicate would put two bits on the same port every single tick -- always a
            // mistake, unlike fan-in from a different output, which is allowed above.
            for (int i = 0; i < source.Edges.Count; i++)
            {
                if (source.Edges[i].Target == target)
                    return WiringVerdict.Reject(WiringOutcome.Duplicate, "Already connected.");
            }

            return WiringVerdict.Accept(source, target);
        }

        /// <summary>
        /// Null for a removed node, and for an id outside the issued range -- GetNode throws on
        /// the latter, and a stale address is exactly where that would happen.
        /// </summary>
        private static Node NodeAt(SimulationView view, PortAddress address) =>
            address.NodeId >= 0 && address.NodeId < view.NodeCount
                ? view.GetNode(address.NodeId)
                : null;
    }
}
