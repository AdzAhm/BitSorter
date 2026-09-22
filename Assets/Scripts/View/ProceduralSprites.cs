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

        /// <summary>Radius of <see cref="Circle"/>, in the -1..1 space every predicate here uses.</summary>
        /// <remarks>
        /// Public because anything scaled to a wanted radius has to divide by it, and a second copy
        /// of the number would silently stop agreeing the first time this shape is retuned.
        /// </remarks>
        public const float CircleRadius = 0.86f;

        /// <summary>The flip-flop box, measured. See <see cref="FlipFlop"/>.</summary>
        /// <remarks>
        /// Public for the same reason <see cref="CircleRadius"/> is: the register draws the bit it
        /// is holding inside this outline, and whether that disc clears the notch and stays within
        /// the edges is arithmetic on these three numbers. <see cref="PortGeometry"/> holds the
        /// disc's own measurements, so the two can be checked against each other rather than eyed.
        /// </remarks>
        public const float FlipFlopHalfWidth = 0.54f;

        /// <inheritdoc cref="FlipFlopHalfWidth"/>
        public const float FlipFlopHalfHeight = 0.88f;

        /// <summary>How deep the clock notch cuts, and half how tall it is at the edge.</summary>
        /// <inheritdoc cref="FlipFlopHalfWidth"/>
        public const float FlipFlopNotch = 0.28f;

        // -----------------------------------------------------------------
        // Public shapes
        // -----------------------------------------------------------------

        public static Sprite Circle() => Mask("circle", NodeSize, p => InCircle(p, CircleRadius));

        public static Sprite CircleBubble(BodyStyle style = BodyStyle.Filled) =>
            Body("circleBubble", style, p => InCircle(p, 0.64f) || InBubble(p));

        public static Sprite RoundedSquare(BodyStyle style = BodyStyle.Filled) =>
            Body("roundedSquare", style, p => InSquircle(p, 0.86f));

        public static Sprite RoundedSquareBubble(BodyStyle style = BodyStyle.Filled) =>
            Body("roundedSquareBubble", style, p => InSquircle(p, 0.64f) || InBubble(p));

        public static Sprite Shield(BodyStyle style = BodyStyle.Filled) =>
            Body("shield", style, p => InShield(p, 0.86f));

        public static Sprite ShieldBubble(BodyStyle style = BodyStyle.Filled) =>
            Body("shieldBubble", style, p => InShield(p, 0.64f) || InBubble(p));

        /// <summary>A shield with the extra leading arc that distinguishes XOR from OR.</summary>
        public static Sprite ShieldArc(BodyStyle style = BodyStyle.Filled) =>
            Body("shieldArc", style, p => InShield(p, 0.80f) || InLeadingArc(p));

        /// <summary>
        /// A wide, short stadium for sources. Deliberately the only shape that is much wider than
        /// it is tall: bloom blurs interior detail and rounds off corners, so aspect ratio is the
        /// one cue that survives it. A diamond read too close to NOT's circle once both glowed.
        /// </summary>
        public static Sprite Capsule(BodyStyle style = BodyStyle.Filled) =>
            Body("capsule", style, p => InCapsule(p, 0.94f, 0.40f));

        public static Sprite Hexagon(BodyStyle style = BodyStyle.Filled) =>
            Body("hexagon", style, p => InHexagon(p, 0.88f));

        /// <summary>
        /// The flip-flop box: a tall, narrow rectangle with the clock's notch cut into its left
        /// edge, where a textbook draws the little triangle.
        /// </summary>
        /// <remarks>
        /// A register is not a gate and must not read as one. Every gate silhouette here is as wide
        /// as it is tall or wider; this is the only one that is taller than it is wide, which is the
        /// cue that survives bloom -- the same reasoning that made sources a wide capsule.
        ///
        /// The notch is cut out of the outline rather than drawn inside it, because interior detail
        /// is exactly what the glow eats.
        /// </remarks>
        public static Sprite FlipFlop(BodyStyle style = BodyStyle.Filled) =>
            Body("flipFlop", style, InFlipFlop);

        // -----------------------------------------------------------------
        // Interface chrome
        // -----------------------------------------------------------------

        /// <summary>Side of the panel backdrop's texture, and how much of each edge is a corner.</summary>
        /// <remarks>
        /// The corner is in texels, and <see cref="UiPixelsPerUnit"/> makes a texel one interface
        /// pixel, so a panel's corner is 10 pixels whatever size the panel is. Sized against the
        /// shortest thing built from this sprite rather than against the largest: two corners have
        /// to fit inside a 38-pixel toast with a middle left over to stretch.
        /// </remarks>
        public const int PanelSize = 32;

        /// <inheritdoc cref="PanelSize"/>
        public const int PanelCorner = 10;

        /// <summary>
        /// Must equal the canvas's <c>referencePixelsPerUnit</c>, which the scene builder leaves at
        /// Unity's default, or a nine-sliced corner is drawn at some other size than it was cut.
        /// </summary>
        public const float UiPixelsPerUnit = 100f;

        /// <summary>
        /// The backdrop behind a panel: a rectangle with rounded corners, nine-sliced.
        /// </summary>
        /// <remarks>
        /// Its own shape rather than <see cref="RoundedSquare"/>, which panels used to borrow from
        /// the AND gate. Two things were wrong with that. A squircle has no straight edges, so
        /// there is no middle of an edge to repeat along it -- the thing nine-slicing needs -- and
        /// it stops 14% short of its own texture, so every panel drawn from it carried a
        /// transparent margin. Stretched across a 330x380 rect that margin became a fade a hundred
        /// pixels deep, and the help panel's title and hint ended up on bare board.
        ///
        /// A rounded rectangle has flat edges that reach the texture's own edge, so the four corner
        /// tiles stay 10 pixels across and everything between them is solid.
        /// </remarks>
        public static Sprite Panel() => Mask(
            "panel", PanelSize, p => InRoundedRect(p, PanelCorner / (PanelSize * 0.5f)),
            new Vector4(PanelCorner, PanelCorner, PanelCorner, PanelCorner), UiPixelsPerUnit);

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

        /// <summary>
        /// An open ring, for an input port with nothing in it.
        /// </summary>
        /// <remarks>
        /// The hollow counterpart to <see cref="Dot"/>, and deliberately the same overall size.
        /// Filled against empty is the distinction that survives being glanced at on a paused
        /// board, where a difference in colour or brightness alone would not. Soft on both edges
        /// for the same reason the dot is: a hard ring shimmers against the board tiling.
        /// </remarks>
        public static Sprite Ring() => Field("ring", DotSize, p =>
        {
            float d = p.magnitude;

            float outer = 1f - Mathf.SmoothStep(0.62f, 0.98f, d);
            float inner = Mathf.SmoothStep(0.28f, 0.58f, d);

            return outer * inner;
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
            // Keyed on the palette, because unlike every mask in this file the tile bakes its
            // colours in: cached under one key, a second look would be drawn on the first one's board.
            string key = "board:" + Palette.Current.Name;

            if (TryCached(key, out Sprite cached))
                return cached;

            var texture = NewTexture(TileSize, TextureWrapMode.Repeat);
            var pixels = new Color32[TileSize * TileSize];

            Color baseColour = Palette.Current.Ground;
            Color trace = Palette.Current.GroundTrace;
            Color pad = Palette.Current.GroundPad;

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

            sprite.hideFlags = HideFlags.HideAndDontSave;

            Cache[key] = sprite;
            return sprite;
        }

        // -----------------------------------------------------------------
        // Builders
        // -----------------------------------------------------------------

        /// <summary>
        /// A cached sprite, if there is still one there to hand out.
        /// </summary>
        /// <remarks>
        /// The cache holds Unity objects, and Unity objects can be destroyed under a plain static
        /// dictionary. Nothing here is an asset -- every texture is generated at runtime -- so a
        /// sweep of untracked objects takes them and leaves the entries behind pointing at corpses.
        /// A destroyed sprite assigned to an Image reads back as no sprite at all, which is a blank
        /// panel rather than an error.
        ///
        /// <see cref="Store"/> marks what it makes as not-to-be-saved, which is what stops the
        /// sweep taking them in the first place; this is the belt to that pair of braces, because
        /// one entry surviving its sprite is the failure that cannot be seen until something is
        /// already drawn wrong. The texture is checked too: it can go on its own, leaving a sprite
        /// that is technically alive and draws nothing.
        /// </remarks>
        private static bool TryCached(string key, out Sprite sprite)
        {
            if (Cache.TryGetValue(key, out sprite) && sprite != null && sprite.texture != null)
                return true;

            sprite = null;
            return false;
        }

        /// <summary>White sprite whose alpha is the supersampled coverage of a shape.</summary>
        /// <summary>A gate body in a style. Filled is exactly <see cref="Mask"/>, under its old key.</summary>
        /// <remarks>
        /// Filled goes through the untouched mask path and cache key, so the shipped look cannot
        /// change by a pixel however the other styles are drawn -- and the reference screenshots
        /// hold it to that.
        /// </remarks>
        private static Sprite Body(string key, BodyStyle style, Func<Vector2, bool> inside) =>
            style == BodyStyle.Filled
                ? Mask(key, NodeSize, inside)
                : Styled(key + ":" + style, style, inside);

        /// <summary>Width of an outline, and of the rim a glass body brightens, in texels.</summary>
        /// <remarks>
        /// Out of <see cref="NodeSize"/>, so an outline is about a twentieth of the body: a few
        /// pixels on screen, drawn rather than hairline.
        /// </remarks>
        private const float OutlineTexels = 7f;

        /// <inheritdoc cref="OutlineTexels"/>
        private const float GlassRimTexels = 14f;

        /// <summary>How far in from the edge a raised body's bevel reaches, in texels.</summary>
        private const float BevelTexels = 12f;

        /// <summary>How much of a body an outline or glass style leaves showing inside the edge.</summary>
        private const float OutlineFill = 0.16f;

        /// <inheritdoc cref="OutlineFill"/>
        private const float GlassFill = 0.30f;

        /// <summary>
        /// A body drawn from its distance to its own edge: an outline, a glass rim, or a bevel.
        /// </summary>
        /// <remarks>
        /// All three styles are one idea. Every texel knows how far it is from the edge of the
        /// shape; an outline is solid near the edge and faint beyond it, glass fades from a bright
        /// rim to a see-through middle, and a raised body is shaded by which way its edge faces a
        /// light from the top left. The silhouette is exactly the filled one -- coverage is computed
        /// the same way -- so the shape rule survives every style: what separates gates is still
        /// their outline, never their fill.
        /// </remarks>
        private static Sprite Styled(string key, BodyStyle style, Func<Vector2, bool> inside)
        {
            if (TryCached(key, out Sprite cached))
                return cached;

            int size = NodeSize;
            float[] coverage = Coverage(size, inside);
            float[] depth = DepthInside(size, coverage);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float cover = coverage[i];
                    float d = depth[i];

                    float alpha = cover;
                    float shade = 1f;

                    switch (style)
                    {
                        case BodyStyle.Outline:
                            // A soft inner edge, a texel and a half wide, so the stroke is not jagged.
                            alpha = cover * Mathf.Lerp(1f, OutlineFill,
                                Mathf.SmoothStep(0f, 1f, (d - (OutlineTexels - 1f)) / 1.5f));
                            break;

                        case BodyStyle.Glass:
                            alpha = cover * Mathf.Lerp(1f, GlassFill, Mathf.SmoothStep(0f, 1f, d / GlassRimTexels));
                            break;

                        case BodyStyle.Raised:
                            shade = RaisedShade(depth, size, x, y, d);
                            break;
                    }

                    byte grey = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(shade));
                    pixels[i] = new Color32(grey, grey, grey, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(alpha)));
                }
            }

            return Store(key, size, pixels);
        }

        /// <summary>
        /// Brightness of a raised body at one texel: flat in the middle, lit on edges facing the top
        /// left and shadowed on edges facing away.
        /// </summary>
        private static float RaisedShade(float[] depth, int size, int x, int y, float d)
        {
            const float Flat = 0.80f;
            const float Relief = 0.24f;

            if (d >= BevelTexels)
                return Flat;

            // Depth rises inward, so its gradient points into the body and the surface faces the
            // other way. Central differences, clamped at the texture's edge.
            float gx = depth[y * size + Mathf.Min(x + 1, size - 1)] - depth[y * size + Mathf.Max(x - 1, 0)];
            float gy = depth[Mathf.Min(y + 1, size - 1) * size + x] - depth[Mathf.Max(y - 1, 0) * size + x];

            var facing = new Vector2(-gx, -gy);
            if (facing.sqrMagnitude < 1e-6f)
                return Flat;

            // Texture y runs up, so the top left is (-1, +1).
            float lit = Vector2.Dot(facing.normalized, new Vector2(-0.7071f, 0.7071f));
            float nearEdge = 1f - d / BevelTexels;

            return Flat + Relief * lit * nearEdge;
        }

        /// <summary>How much of each texel a shape covers, 0 to 1, supersampled like <see cref="Mask"/>.</summary>
        private static float[] Coverage(int size, Func<Vector2, bool> inside)
        {
            var coverage = new float[size * size];
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

                    coverage[y * size + x] = (float)hits / grid;
                }
            }

            return coverage;
        }

        /// <summary>
        /// For every texel inside the shape, how far it is from the nearest texel outside it, in
        /// texels. Zero outside.
        /// </summary>
        /// <remarks>
        /// A two-pass chamfer transform: one sweep forward and one back, each taking the smallest of
        /// its neighbours' distances plus the step to them. Off the texture counts as outside, so a
        /// shape touching the border is measured to it.
        /// </remarks>
        public static float[] DepthInside(int size, float[] coverage)
        {
            const float Far = 1e6f;
            const float Diagonal = 1.41421356f;
            var depth = new float[size * size];

            for (int i = 0; i < depth.Length; i++)
                depth[i] = coverage[i] >= 0.5f ? Far : 0f;

            float At(int x, int y) =>
                x < 0 || y < 0 || x >= size || y >= size ? 0f : depth[y * size + x];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    if (depth[i] == 0f)
                        continue;

                    depth[i] = Mathf.Min(depth[i],
                        Mathf.Min(At(x - 1, y) + 1f,
                        Mathf.Min(At(x, y - 1) + 1f,
                        Mathf.Min(At(x - 1, y - 1) + Diagonal, At(x + 1, y - 1) + Diagonal))));
                }
            }

            for (int y = size - 1; y >= 0; y--)
            {
                for (int x = size - 1; x >= 0; x--)
                {
                    int i = y * size + x;
                    if (depth[i] == 0f)
                        continue;

                    depth[i] = Mathf.Min(depth[i],
                        Mathf.Min(At(x + 1, y) + 1f,
                        Mathf.Min(At(x, y + 1) + 1f,
                        Mathf.Min(At(x + 1, y + 1) + Diagonal, At(x - 1, y + 1) + Diagonal))));
                }
            }

            return depth;
        }

        private static Sprite Mask(
            string key, int size, Func<Vector2, bool> inside,
            Vector4 border = default, float pixelsPerUnit = 0f)
        {
            if (TryCached(key, out Sprite cached))
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

            return Store(key, size, pixels, border, pixelsPerUnit);
        }

        /// <summary>White sprite whose alpha comes from a smooth field, no supersampling needed.</summary>
        private static Sprite Field(string key, int size, Func<Vector2, float> alpha)
        {
            if (TryCached(key, out Sprite cached))
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

        /// <summary>
        /// Turns a page of pixels into a cached sprite. A zero <paramref name="pixelsPerUnit"/>
        /// means one sprite to one world unit, which is what every board shape wants.
        /// </summary>
        private static Sprite Store(
            string key, int size, Color32[] pixels,
            Vector4 border = default, float pixelsPerUnit = 0f)
        {
            var texture = NewTexture(size, TextureWrapMode.Clamp);
            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit > 0f ? pixelsPerUnit : size, 0, SpriteMeshType.FullRect, border);

            sprite.hideFlags = HideFlags.HideAndDontSave;

            Cache[key] = sprite;
            return sprite;
        }

        private static Texture2D NewTexture(int size, TextureWrapMode wrap) =>
            new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = wrap,

                // Generated, not an asset, and referenced only by a static dictionary -- without
                // this it is swept the first time the editor unloads unused objects.
                hideFlags = HideFlags.HideAndDontSave,
            };

        // -----------------------------------------------------------------
        // Shape predicates, in -1..1 space
        // -----------------------------------------------------------------

        private static bool InCircle(Vector2 p, float radius) => p.sqrMagnitude <= radius * radius;

        /// <summary>
        /// A rectangle filling the whole sprite, with circular corners of this radius.
        /// </summary>
        /// <remarks>
        /// Not a squircle. This one has genuinely flat edges that run out to the sprite's own
        /// boundary, which is what lets <see cref="Panel"/> be nine-sliced: the middle of each edge
        /// is a strip that can be repeated along an edge of any length without changing shape.
        /// </remarks>
        private static bool InRoundedRect(Vector2 p, float radius)
        {
            float x = Mathf.Abs(p.x);
            float y = Mathf.Abs(p.y);

            if (x > 1f || y > 1f)
                return false;

            float dx = x - (1f - radius);
            float dy = y - (1f - radius);

            // Anywhere but the four corner squares is simply inside the rectangle.
            if (dx <= 0f || dy <= 0f)
                return true;

            return dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>A squircle: a square with softly rounded corners.</summary>
        private static bool InSquircle(Vector2 p, float half)
        {
            float x = Mathf.Abs(p.x) / half;
            float y = Mathf.Abs(p.y) / half;
            return x * x * x * x + y * y * y * y <= 1f;
        }

        /// <summary>A tall box with a triangular notch bitten out of the middle of its left edge.</summary>
        private static bool InFlipFlop(Vector2 p)
        {
            // Height matched to the other silhouettes, which sit at 0.86 to 0.88: the port stubs are
            // placed on this shape's faces, so a taller body would push them off its edges.
            const float halfWidth = FlipFlopHalfWidth;
            const float halfHeight = FlipFlopHalfHeight;
            const float notch = FlipFlopNotch;

            if (Mathf.Abs(p.x) > halfWidth || Mathf.Abs(p.y) > halfHeight)
                return false;

            // The notch narrows to a point as it goes in, which is the clock triangle in reverse.
            float depth = p.x + halfWidth;

            return depth >= notch || Mathf.Abs(p.y) >= notch - depth;
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
