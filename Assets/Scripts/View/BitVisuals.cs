using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Pure maths and colour for how a bit is drawn. Separated from <see cref="BitRenderer"/> so
    /// the curves can be pinned by tests without a scene.
    /// </summary>
    public static class BitVisuals
    {
        /// <summary>What a zero looks like, anywhere it is drawn.</summary>
        /// <remarks>
        /// Here rather than on a renderer because a bit is now drawn in two places: travelling
        /// along a wire, and sitting in the input port it arrived at. Those must be the same
        /// colour or the bit appears to change identity on landing, and a serialized field on one
        /// renderer that the other has to copy is exactly the second copy that drifts.
        /// </remarks>
        public static readonly Color Zero = new Color(0.42f, 0.48f, 0.58f);

        /// <inheritdoc cref="Zero"/>
        public static readonly Color One = new Color(1.00f, 0.88f, 0.32f);

        /// <summary>The colour a bit of this value is drawn in, wherever it is.</summary>
        public static Color ColourFor(Bit value) => value == Bit.One ? One : Zero;

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
