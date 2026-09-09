using BitSorter.LogicCore;

namespace BitSorter.View
{
    /// <summary>
    /// What a renderer needs to know about a port's waiting state, and about the collision that is
    /// one tick away from it. Pure functions over simulation state, separated from the renderers
    /// the way <see cref="BitVisuals"/> is, so the rules can be pinned by tests without a scene.
    /// </summary>
    /// <remarks>
    /// Nothing here decides anything. Every answer is derived from <see cref="InputPort"/>,
    /// <see cref="Node"/> and <see cref="Edge"/>, which is what keeps state flowing one way.
    /// </remarks>
    public static class PortState
    {
        /// <summary>
        /// Whether <paramref name="node"/> is holding bits it cannot yet act on.
        /// </summary>
        /// <remarks>
        /// Between ticks this is equivalent to "any input port is occupied", because
        /// <see cref="Simulation.Tick"/> evaluates every ready node in the same tick that filled
        /// it: advance, deliver, then evaluate. A node whose ports were all full never survives to
        /// the next frame holding them. The condition is still written out in full rather than
        /// shortened to the first occupied port, because the view also renders mid-tick, and
        /// "ready and about to fire" must not read as "stuck".
        ///
        /// A node with no inputs is never stalled. <see cref="SourceNode"/> is vacuously ready and
        /// fires every tick, so reporting it as waiting would light up every source on the board.
        /// </remarks>
        public static bool IsStalled(Node node)
        {
            if (node == null || node.InputCount == 0)
                return false;

            if (node.IsReadyToEvaluate)
                return false;   // every port full: about to fire, not stuck

            for (int i = 0; i < node.InputCount; i++)
            {
                if (node.In(i).IsOccupied)
                    return true;   // something is held, and something is missing
            }

            return false;
        }

        /// <summary>
        /// Whether a bit on <paramref name="edge"/> will collide on the next tick, and if so
        /// whether the bit already sitting in the target port dies with it.
        /// </summary>
        /// <remarks>
        /// This is an exact prediction rather than a guess. Delivery happens in phase 2 of a tick
        /// and evaluation in phase 3, so nothing can empty the target port between now and the
        /// arrival: if the port is occupied and a bit is one tick out, they meet.
        ///
        /// The two outcomes are the ones <see cref="InputPort.Deliver"/> already distinguishes.
        /// Matching values leave the port holding its value and destroy only the arrival, so one
        /// bit is lost. Differing values are ambiguous, so both are destroyed and the port is
        /// cleared -- which is the case worth colouring differently, because the player loses a bit
        /// they could see sitting there.
        ///
        /// At most one bit per edge can be one tick out: they all share the edge's delay, so they
        /// can never bunch up.
        /// </remarks>
        public static bool WillCollide(Edge edge, out bool heldBitDies)
        {
            heldBitDies = false;

            if (edge == null || !edge.Target.IsOccupied)
                return false;

            Bit held = edge.Target.Pending.Value;

            for (int i = 0; i < edge.InTransitCount; i++)
            {
                BitInTransit bit = edge.GetBitInTransit(i);

                if (bit.TicksRemaining != 1)
                    continue;

                heldBitDies = bit.Value != held;
                return true;
            }

            return false;
        }
    }
}
