using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The ring a bit leaves on a block's port as it goes into the box or comes out of it: a thin
    /// ring (<see cref="ProceduralSprites.PulseRing"/>) in the bit's colour, growing from the
    /// socket's size and fading away.
    /// </summary>
    /// <remarks>
    /// A block's inside is not drawn, so a bit vanished into the box and reappeared at an output
    /// some ticks later with nothing to say the box was working -- only the faint spark every port
    /// gets. The pulse is that beat made visible, and its colour says which value went in or came
    /// out (asked for after the 4.0.0 browser playtest, 2026-10-05).
    ///
    /// Pure arithmetic over the pulse's age, so the shape is testable without a scene. An event
    /// counts from its own start, and ends on its final value: at <see cref="Seconds"/> the ring
    /// is fully transparent, which is also the moment <see cref="BitRenderer"/> lets it go.
    /// </remarks>
    public static class PortPulse
    {
        /// <summary>How long a pulse lasts: under a tick, so at the authored speed one has gone before the next comes.</summary>
        public const float Seconds = 0.45f;

        /// <summary>Its size at the start, as a share of the socket's: the socket itself.</summary>
        public const float StartScale = 1f;

        /// <summary>
        /// Its size at the end, as a share of the socket's: half a gate's width. At socket size or a
        /// little over it hid under the socket and the sparks, and read as nothing; at a whole
        /// gate's width (3.6), and then two thirds (2.6), Ahmad asked for it smaller (2026-10-06).
        /// </summary>
        public const float EndScale = 2f;

        /// <summary>How opaque it starts.</summary>
        public const float StartAlpha = 0.95f;

        /// <summary>Its size at this age, as a share of the socket's: quick at first, then slowing.</summary>
        public static float ScaleAt(float age)
        {
            float t = Progress(age);
            return Mathf.Lerp(StartScale, EndScale, 1f - (1f - t) * (1f - t));
        }

        /// <summary>
        /// How opaque it is at this age, evenly down to exactly zero at <see cref="Seconds"/>: a fade
        /// that fell away faster left the ring transparent before it had grown big enough to see.
        /// </summary>
        public static float AlphaAt(float age) => StartAlpha * (1f - Progress(age));

        /// <summary>Whether a pulse this old has finished.</summary>
        public static bool IsOver(float age) => age >= Seconds;

        private static float Progress(float age) => Mathf.Clamp01(age / Seconds);
    }
}
