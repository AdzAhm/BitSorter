using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Every sprite the interface draws, generated at runtime. No external art.
    /// </summary>
    /// <remarks>
    /// Shapes are drawn by supersampling a point-in-shape predicate, which gives antialiased edges
    /// and works for any shape without per-shape distance-field maths. Everything is cached by
    /// name, so each texture is built once no matter how many panels ask for it.
    ///
    /// Coordinates passed to a predicate are normalised to -1..1 with the origin at the centre.
    ///
    /// Since Part 16b this is the panel backdrop and nothing else. The 4.x board's gate bodies, bits, sockets, glow and
    /// board tile went with the board, and the circle with the uGUI slider.
    /// </remarks>
    public static class ProceduralSprites
    {
        private const int SuperSamples = 4;   // 4x4 per pixel

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>How many sprites have been drawn since the domain loaded.</summary>
        /// <remarks>
        /// For telling when a sprite is built, which is the part that costs: asking for one that is
        /// already cached leaves this where it was.
        /// </remarks>
        public static int BuiltCount { get; private set; }

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
        /// Its own shape rather than a squircle, which panels used to borrow from the AND gate. Two things were wrong with that. A squircle has no straight edges, so
        /// there is no middle of an edge to repeat along it -- the thing nine-slicing needs -- and
        /// it stops 14% short of its own texture, so every panel drawn from it carried a
        /// transparent margin. Stretched across a 330x380 rect that margin became a fade a hundred
        /// pixels deep, and the help panel's title and hint ended up on bare board.
        ///
        /// A rounded rectangle has flat edges that reach the texture's own edge, so the four corner
        /// tiles stay 10 pixels across and everything between them is solid.
        /// </remarks>
        public static Sprite Panel(PanelStyle style = PanelStyle.Filled) =>
            style == PanelStyle.Filled
                ? Mask("panel", PanelSize, InPanel,
                    new Vector4(PanelCorner, PanelCorner, PanelCorner, PanelCorner), UiPixelsPerUnit)
                : BorderedPanel();

        private static bool InPanel(Vector2 p) => InRoundedRect(p, PanelCorner / (PanelSize * 0.5f));

        /// <summary>How wide a bordered panel's edge is, in texels -- drawn at about a canvas pixel and a half.</summary>
        private const float PanelRimTexels = 1.5f;

        /// <summary>How bright a bordered panel's body is, against its edge at full brightness.</summary>
        /// <remarks>
        /// Baked into the one sprite, so one tint draws both: the edge in the tint, the body at this
        /// fraction of it. Small things tinted with the accent become outlined chips in the same
        /// stroke.
        /// </remarks>
        private const float PanelBody = 0.22f;

        /// <summary>The panel shape with a thin edge drawn round a darker body, nine-sliced as Panel is.</summary>
        private static Sprite BorderedPanel()
        {
            const string key = "panel:Bordered";

            if (TryCached(key, out Sprite cached))
                return cached;

            float[] coverage = Coverage(PanelSize, InPanel);
            float[] depth = DepthInside(PanelSize, coverage);
            var pixels = new Color32[PanelSize * PanelSize];

            for (int i = 0; i < pixels.Length; i++)
            {
                float shade = Mathf.Lerp(1f, PanelBody, Mathf.SmoothStep(0f, 1f, depth[i] - PanelRimTexels));
                byte grey = (byte)Mathf.RoundToInt(255f * shade);
                pixels[i] = new Color32(grey, grey, grey, (byte)Mathf.RoundToInt(255f * coverage[i]));
            }

            return Store(key, PanelSize, pixels,
                new Vector4(PanelCorner, PanelCorner, PanelCorner, PanelCorner), UiPixelsPerUnit);
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

        /// <summary>White sprite whose alpha is the supersampled coverage of a shape.</summary>
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

        /// <summary>
        /// Turns a page of pixels into a cached sprite. A zero <paramref name="pixelsPerUnit"/>
        /// means one sprite to one world unit.
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
            BuiltCount++;
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
    }
}
