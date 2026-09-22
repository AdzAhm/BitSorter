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

        /// <summary>
        /// How much brighter a bit in flight is drawn than its own colour.
        /// </summary>
        /// <remarks>
        /// Above one on purpose. The board renders to an HDR target and the bloom threshold sits
        /// at 1, so this is what separates a travelling bit from everything it travels past --
        /// and separating them needed more than a threshold, because five of the nine node
        /// colours peak at exactly 1.00, the same as a one. Nothing told the bloom which of them
        /// was the bit, so gates bloomed as hard as bits did and a NOT gate rendered as a
        /// featureless bright disc with its silhouette gone.
        ///
        /// Lifting bits instead of dimming everything else is what keeps the rest of the board
        /// its own colour. A gate now contributes a trace through the threshold's soft knee, which
        /// is the amount of glow a thing that is not a bit should have.
        ///
        /// It also fixed a zero being nearly invisible. A zero peaked at 0.58 and never crossed
        /// the old threshold at all, so a one glowed and a zero was a small grey dot that was easy
        /// to lose on a busy board -- while both are equally a value travelling along a wire.
        /// </remarks>
        public const float Emission = 1.8f;

        /// <summary>
        /// The brightness above which the board blooms.
        /// </summary>
        /// <remarks>
        /// Beside <see cref="Emission"/> because they are one decision: the lift exists to carry a
        /// bit over this line, and the line sits at 1 so that nothing in LDR can cross it. The
        /// scene builder writes the volume profile from this constant rather than stating its own
        /// number -- it used to hard-code 0.62 with a comment explaining that LDR colours needed a
        /// threshold below 1, which stopped being true the moment bits went HDR, and a rebuild
        /// would have quietly put it back.
        /// </remarks>
        public const float BloomThreshold = 1f;

        /// <summary>The colour a bit in flight is actually drawn in.</summary>
        /// <remarks>
        /// Applied to whatever colour the caller has arrived at, warning tints included, so a bit
        /// that is about to collide blazes rather than quietly changing hue.
        /// </remarks>
        public static Color Emissive(Color colour) =>
            new Color(colour.r * Emission, colour.g * Emission, colour.b * Emission, colour.a);

        /// <summary>How bright a colour is, as the bloom threshold measures it.</summary>
        /// <remarks>
        /// URP's bloom prefilter takes the largest channel rather than a perceptual luminance, so
        /// this is the number that decides what glows. Here so a test can ask it.
        /// </remarks>
        public static float Brightness(Color colour) =>
            Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));

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
