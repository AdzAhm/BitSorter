using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Pure maths for how a bit is drawn. Separated from <see cref="BitRenderer"/> so the curves
    /// can be pinned by tests without a scene.
    /// </summary>
    public static class BitVisuals
    {
        /// <summary>Fraction of the journey over which the squash builds up.</summary>
        public const float SquashWindow = 0.18f;

        public const float MaxSquash = 0.42f;
        public const float MaxStretch = 0.30f;

        /// <summary>
        /// How far into the arrival squash a bit is: 0 for most of the journey, rising to 1 as it
        /// reaches its target port.
        /// </summary>
        public static float SquashAmount(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            if (clamped <= 1f - SquashWindow)
                return 0f;

            return Mathf.Clamp01((clamped - (1f - SquashWindow)) / SquashWindow);
        }

        /// <summary>
        /// Scale for a bit, in the frame where local x runs along the wire. Compresses along
        /// travel and bulges across it, so the bit reads as hitting the port rather than
        /// vanishing into it. Never returns a non-positive axis.
        /// </summary>
        public static Vector2 ScaleAt(float progress, float size)
        {
            float amount = SquashAmount(progress);
            float along = 1f - MaxSquash * amount;
            float across = 1f + MaxStretch * amount;

            return new Vector2(size * along, size * across);
        }
    }
}
