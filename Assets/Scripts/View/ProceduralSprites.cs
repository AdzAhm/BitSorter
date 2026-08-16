using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Every sprite in the demo, generated at runtime. No external art.
    /// </summary>
    /// <remarks>
    /// Shapes are drawn by supersampling a point-in-shape predicate, which gives antialiased edges
    /// and works for any shape without per-shape distance-field maths. Everything is cached by
    /// name, so each texture is built once no matter how many nodes ask for it.
    ///
    /// Coordinates passed to a predicate are normalised to -1..1 with the origin at the centre.
    /// </remarks>
    public static class ProceduralSprites
    {
        private const int NodeSize = 128;
        private const int DotSize = 64;
        private const int TileSize = 128;
        private const int SuperSamples = 4;   // 4x4 per pixel

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        // -----------------------------------------------------------------
        // Public shapes
        // -----------------------------------------------------------------

        public static Sprite Circle() => Mask("circle", NodeSize, p => InCircle(p, 0.86f));

        public static Sprite CircleBubble() =>
            Mask("circleBubble", NodeSize, p => InCircle(p, 0.64f) || InBubble(p));

        public static Sprite RoundedSquare() => Mask("roundedSquare", NodeSize, p => InSquircle(p, 0.86f));

        public static Sprite RoundedSquareBubble() =>
            Mask("roundedSquareBubble", NodeSize, p => InSquircle(p, 0.64f) || InBubble(p));

        public static Sprite Shield() => Mask("shield", NodeSize, p => InShield(p, 0.86f));

        public static Sprite ShieldBubble() =>
            Mask("shieldBubble", NodeSize, p => InShield(p, 0.64f) || InBubble(p));

        /// <summary>A shield with the extra leading arc that distinguishes XOR from OR.</summary>
        public static Sprite ShieldArc() =>
            Mask("shieldArc", NodeSize, p => InShield(p, 0.80f) || InLeadingArc(p));

        public static Sprite Diamond() => Mask("diamond", NodeSize, p => Mathf.Abs(p.x) + Mathf.Abs(p.y) <= 0.88f);

        /// <summary>
        /// A wide, short stadium for sources. Deliberately the only shape that is much wider than
        /// it is tall: bloom blurs interior detail and rounds off corners, so aspect ratio is the
        /// one cue that survives it. A diamond read too close to NOT's circle once both glowed.
        /// </summary>
        public static Sprite Capsule() => Mask("capsule", NodeSize, p => InCapsule(p, 0.94f, 0.40f));

        public static Sprite Hexagon() => Mask("hexagon", NodeSize, p => InHexagon(p, 0.88f));

        /// <summary>Soft radial falloff, used behind everything that should appear to glow.</summary>
        public static Sprite Glow() => Field("glow", NodeSize, p =>
        {
            float d = Mathf.Clamp01(p.magnitude);
            return Mathf.Pow(1f - d, 2.5f);
        });

        /// <summary>Solid core with a soft edge, for bits and sparks.</summary>
        public static Sprite Dot() => Field("dot", DotSize, p =>
        {
            float d = p.magnitude;
            return 1f - Mathf.SmoothStep(0.35f, 1f, d);
        });

        // -----------------------------------------------------------------
        // Board background
        // -----------------------------------------------------------------

        /// <summary>
        /// A seamless circuit-board tile. Built as colour rather than a mask, and created with
        /// <see cref="SpriteMeshType.FullRect"/> so a SpriteRenderer can tile it.
        /// </summary>
        public static Sprite BoardTile()
        {
            if (Cache.TryGetValue("board", out Sprite cached))
                return cached;

            var texture = NewTexture(TileSize, TextureWrapMode.Repeat);
            var pixels = new Color32[TileSize * TileSize];

            var baseColour = new Color(0.055f, 0.065f, 0.085f);
            var trace = new Color(0.10f, 0.15f, 0.17f);
            var pad = new Color(0.13f, 0.20f, 0.22f);

            for (int y = 0; y < TileSize; y++)
            {
                for (int x = 0; x < TileSize; x++)
                {
                    float u = (x + 0.5f) / TileSize;
                    float v = (y + 0.5f) / TileSize;
                    Color colour = baseColour;

                    // Lines on the tile edges and through the middle. Edge lines meet their
                    // neighbour's, so the tiling seam is invisible.
                    const float thin = 0.012f;
                    if (u < thin || u > 1f - thin || v < thin || v > 1f - thin) colour = trace;
                    if (Mathf.Abs(u - 0.5f) < thin || Mathf.Abs(v - 0.5f) < thin) colour = trace;

                    // Pads where the traces cross.
                    float toCentre = new Vector2(u - 0.5f, v - 0.5f).magnitude;
                    if (toCentre < 0.055f) colour = pad;

                    float toCorner = Mathf.Min(
                        new Vector2(u, v).magnitude,
                        Mathf.Min(new Vector2(u - 1f, v).magnitude,
                            Mathf.Min(new Vector2(u, v - 1f).magnitude, new Vector2(u - 1f, v - 1f).magnitude)));
                    if (toCorner < 0.05f) colour = pad;

                    pixels[y * TileSize + x] = colour;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, TileSize, TileSize), new Vector2(0.5f, 0.5f),
                TileSize, 0, SpriteMeshType.FullRect);

            Cache["board"] = sprite;
            return sprite;
        }

        // -----------------------------------------------------------------
        // Builders
        // -----------------------------------------------------------------

        /// <summary>White sprite whose alpha is the supersampled coverage of a shape.</summary>
        private static Sprite Mask(string key, int size, Func<Vector2, bool> inside)
        {
            if (Cache.TryGetValue(key, out Sprite cached))
                return cached;

            var pixels = new Color32[size * size];
            const int grid = SuperSamples * SuperSamples;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;

                    for (int sy = 0; sy < SuperSamples; sy++)
                    {
                        for (int sx = 0; sx < SuperSamples; sx++)
                        {
                            float fx = (x + (sx + 0.5f) / SuperSamples) / size * 2f - 1f;
                            float fy = (y + (sy + 0.5f) / SuperSamples) / size * 2f - 1f;

                            if (inside(new Vector2(fx, fy)))
                                hits++;
                        }
                    }

                    byte alpha = (byte)Mathf.RoundToInt(255f * hits / grid);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            return Store(key, size, pixels);
        }

        /// <summary>White sprite whose alpha comes from a smooth field, no supersampling needed.</summary>
        private static Sprite Field(string key, int size, Func<Vector2, float> alpha)
        {
            if (Cache.TryGetValue(key, out Sprite cached))
                return cached;

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = (x + 0.5f) / size * 2f - 1f;
                    float fy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(alpha(new Vector2(fx, fy)));

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            return Store(key, size, pixels);
        }

        private static Sprite Store(string key, int size, Color32[] pixels)
        {
            var texture = NewTexture(size, TextureWrapMode.Clamp);
            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                size, 0, SpriteMeshType.FullRect);

            Cache[key] = sprite;
            return sprite;
        }

        private static Texture2D NewTexture(int size, TextureWrapMode wrap) =>
            new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = wrap,
            };

        // -----------------------------------------------------------------
        // Shape predicates, in -1..1 space
        // -----------------------------------------------------------------

        private static bool InCircle(Vector2 p, float radius) => p.sqrMagnitude <= radius * radius;

        /// <summary>A squircle: a square with softly rounded corners.</summary>
        private static bool InSquircle(Vector2 p, float half)
        {
            float x = Mathf.Abs(p.x) / half;
            float y = Mathf.Abs(p.y) / half;
            return x * x * x * x + y * y * y * y <= 1f;
        }

        /// <summary>Flat left edge tapering to a rounded point on the right, as OR-family gates do.</summary>
        private static bool InShield(Vector2 p, float half)
        {
            if (p.x < -half || p.x > half)
                return false;

            float u = (p.x + half) / (2f * half);          // 0 at the flat edge, 1 at the tip
            float height = half * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
            return Mathf.Abs(p.y) <= height;
        }

        /// <summary>The extra curve left of an XOR body.</summary>
        private static bool InLeadingArc(Vector2 p)
        {
            const float centre = -1.75f;   // circle centre well off to the left
            const float radius = 1.0f;
            const float thickness = 0.085f;

            float distance = Mathf.Abs(new Vector2(p.x - centre, p.y).magnitude - radius);
            return distance <= thickness && Mathf.Abs(p.y) <= 0.62f;
        }

        /// <summary>A stadium: a rectangle with semicircular caps on the left and right.</summary>
        private static bool InCapsule(Vector2 p, float halfWidth, float halfHeight)
        {
            float flat = Mathf.Max(0f, halfWidth - halfHeight);
            float x = Mathf.Abs(p.x);

            if (x <= flat)
                return Mathf.Abs(p.y) <= halfHeight;

            return new Vector2(x - flat, p.y).sqrMagnitude <= halfHeight * halfHeight;
        }

        private static bool InBubble(Vector2 p)
        {
            var centre = new Vector2(0.81f, 0f);
            return (p - centre).sqrMagnitude <= 0.17f * 0.17f;
        }

        /// <summary>
        /// Regular hexagon with vertices at (+/-radius, 0): inside the horizontal slab and inside
        /// both slanted edges.
        /// </summary>
        private static bool InHexagon(Vector2 p, float radius)
        {
            const float root3 = 1.7320508f;

            float x = Mathf.Abs(p.x);
            float y = Mathf.Abs(p.y);

            return y <= radius * root3 * 0.5f && root3 * x + y <= root3 * radius;
        }
    }
}
