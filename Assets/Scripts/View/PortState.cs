using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

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
        /// A gate that is waiting, and a collision that will destroy only the arriving bit.
        /// </summary>
        /// <remarks>
        /// Here rather than on the renderers because the waiting language is spoken in three
        /// places -- the socket, the gate body and the wire -- and it only reads as one idea if
        /// they agree. Three serialized colours that have to be kept in step by hand are three
        /// chances for the board to start saying two different things at once.
        ///
        /// Amber against red is the whole distinction: something is waiting, versus something is
        /// about to be lost that you can currently see.
        /// </remarks>
        public static Color Waiting => Palette.Current.Waiting;

        /// <inheritdoc cref="Waiting"/>
        public static Color Doomed => Palette.Current.Doomed;

        /// <summary>
        /// How to colour an imminent collision, given whether the waiting bit dies with it.
        /// </summary>
        public static Color WarningColour(bool heldBitDies) => heldBitDies ? Doomed : Waiting;

        /// <summary>
        /// How fast an imminent collision throbs, shared by the port, the wire and the bit on it.
        /// </summary>
        /// <remarks>
        /// One rate, for the same reason there is one colour. Three things warning about a single
        /// collision at three rates read as three unrelated flickers; in step they read as one
        /// event with three parts. Deliberately faster than a stalled gate's breathing, so urgency
        /// is told by rate as well as by colour.
        /// </remarks>
        public const float WarningHz = 3.5f;

        /// <summary>
        /// A 0..1 throb for anything drawing attention to itself, so every warning on the board
        /// breathes in step instead of interfering with itself.
        /// </summary>
        public static float Pulse(float time, float hertz) =>
            0.5f + 0.5f * Mathf.Sin(time * hertz * Mathf.PI * 2f);

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

    /// <summary>
    /// Which ports have had a collision the player has not been shown yet.
    /// </summary>
    /// <remarks>
    /// A stateful helper beside the pure rules, exactly as <see cref="StallClock"/> sits beside
    /// <see cref="HintRules"/> and for the same reason: the decision is a small state machine over
    /// simulation state, and keeping it out of the renderer is what makes it reachable from Edit
    /// Mode. <see cref="PortRenderer"/> owns the countdown and the drawing; this owns only the
    /// question of whether a collision is news.
    /// </remarks>
    public sealed class CollisionWatch
    {
        /// <summary>The collision tick each port has already been flashed for.</summary>
        private readonly Dictionary<PortAddress, int> _shown = new Dictionary<PortAddress, int>();

        /// <summary>Forgets everything, for a rebuild that replaced the ports this described.</summary>
        /// <remarks>
        /// Needed because a rebuild restarts the clock at zero, so the same tick number belongs to
        /// a different run and its collisions have not been shown.
        /// </remarks>
        public void Clear() => _shown.Clear();

        /// <summary>
        /// Whether <paramref name="port"/> has collided since it was last flashed.
        /// </summary>
        /// <param name="collidedOnTick">
        /// The tick a collision last destroyed a bit at this port, or -1 for never.
        /// </param>
        /// <remarks>
        /// Answers true at most once per collision, and records the answer -- so a caller polling
        /// every frame gets one flash per event rather than one per look.
        ///
        /// There is deliberately no tick argument. The previous rule also required the collision to
        /// have happened on the tick just executed, and that fails in both directions: it re-fires
        /// for as long as the clock is stopped, and it misses a collision outright when the runner
        /// falls far enough behind to execute two ticks between frames. A collision the caller has
        /// not been told about is news whenever it is noticed.
        /// </remarks>
        public bool IsNews(PortAddress port, int collidedOnTick)
        {
            if (collidedOnTick < 0)
                return false;

            if (_shown.TryGetValue(port, out int already) && already == collidedOnTick)
                return false;

            _shown[port] = collidedOnTick;
            return true;
        }
    }
}
