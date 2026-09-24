using System;
using System.Collections.Generic;
using BitSorter.LogicCore;
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

        /// <summary>How many sprites have been drawn since the domain loaded.</summary>
        /// <remarks>
        /// For telling when a sprite is built, which is the part that costs: asking for one that is
        /// already cached leaves this where it was.
        /// </remarks>
        public static int BuiltCount { get; private set; }

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

        /// <summary>
        /// The AND gate as a course draws it: a D -- a flat back, straight top and bottom, and a
        /// semicircular front.
        /// </summary>
        /// <remarks>
        /// It was a rounded square, a shape that named no gate anyone would recognise from their
        /// lecture notes; the textbook symbol is the one a student is learning to read. As wide as
        /// it is tall, like every other gate, so aspect ratio still marks out the source and the
        /// register. Its back corners are square and its front is round, which is what separates
        /// it from the OR family's pointed front even once bloom has softened the edges.
        /// </remarks>
        public static Sprite DShape(BodyStyle style = BodyStyle.Filled) =>
            Body("dShape", style, p => InD(p, 0.86f));

        /// <summary>NAND: the AND gate's D with the inverting bubble at its output.</summary>
        public static Sprite DShapeBubble(BodyStyle style = BodyStyle.Filled) =>
            Body("dShapeBubble", style, p => InD(p, 0.64f) || InBubble(p));

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

        /// <summary>How wide a ring round something on the interface is, in texels.</summary>
        /// <remarks>Heavier than a bordered panel's edge: it is pointing at something, not framing it.</remarks>
        private const float PanelRingTexels = 3f;

        /// <summary>
        /// The panel shape as an outline and nothing else, nine-sliced as <see cref="Panel"/> is: a
        /// ring to draw round something on the interface without covering it.
        /// </summary>
        /// <remarks>
        /// The tutorial's rings on the interface used the AND gate's filled squircle, stretched over
        /// the target and drawn on top of it -- so the ring round RUN, on the step asking for RUN,
        /// was a pale slab with the caption lost underneath.
        /// </remarks>
        public static Sprite PanelRing()
        {
            const string key = "panel:ring";

            if (TryCached(key, out Sprite cached))
                return cached;

            float[] coverage = Coverage(PanelSize, InPanel);
            float[] depth = DepthInside(PanelSize, coverage);
            var pixels = new Color32[PanelSize * PanelSize];

            for (int i = 0; i < pixels.Length; i++)
            {
                float rim = 1f - Mathf.SmoothStep(0f, 1f, depth[i] - PanelRingTexels);
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * coverage[i] * rim));
            }

            return Store(key, PanelSize, pixels,
                new Vector4(PanelCorner, PanelCorner, PanelCorner, PanelCorner), UiPixelsPerUnit);
        }

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

        /// <summary>How many texels across a digit is drawn: finer than a dot, because it has a shape to keep.</summary>
        private const int DigitSize = 128;

        /// <summary>How wide a digit's stroke is, and how soft its edge, on the sprite's -1..1 square.</summary>
        private const float DigitStroke = 0.24f;
        private const float DigitSoftness = 0.05f;

        /// <summary>
        /// A bit's value drawn as its digit, in a neon stroke: a tall rounded 0, and a 1 with a flag
        /// and a foot.
        /// </summary>
        /// <remarks>
        /// Stroked rather than filled, like a neon sign, so it keeps its shape when it blooms --
        /// a filled glyph blooms into a blob, and a stroke into a glowing letter. The 1 has a foot
        /// so it cannot be read as a bar or an l, and so it carries about as much light as the 0.
        ///
        /// Taller than it is wide, both of them, so a digit is never mistaken for the round
        /// socket it is travelling towards.
        /// </remarks>
        public static Sprite BitGlyph(Bit value) => value == Bit.One
            ? Field("digit:1", DigitSize, p => Stroked(OneDistance(p)))
            : Field("digit:0", DigitSize, p => Stroked(ZeroDistance(p)));

        /// <summary>
        /// A bit that is being held -- in a socket, or inside a register -- as a disc with its
        /// digit cut out of it, like a stamped coin.
        /// </summary>
        /// <remarks>
        /// A disc and not the stroked digit a bit in flight is, because a socket already has a
        /// meaning for hollow: a ring is an empty socket, and a stroked 0 in one would read as
        /// nothing there. The disc says full; the cut says which value.
        ///
        /// The disc is exactly <see cref="Circle"/>'s, so the register's held bit keeps the
        /// geometry <see cref="PortGeometry"/> pins to that sprite.
        /// </remarks>
        public static Sprite HeldBit(Bit value) => value == Bit.One
            ? Mask("held:1", NodeSize, p =>
                InCircle(p, CircleRadius) && OneDistance(p / CoinDigit) * CoinDigit > CoinStroke * 0.5f)
            : Mask("held:0", NodeSize, p =>
                InCircle(p, CircleRadius) && ZeroDistance(p / CoinDigit) * CoinDigit > CoinStroke * 0.5f);

        /// <summary>
        /// Builds every sprite a bit can be drawn as in the current look, so none is built mid-run.
        /// </summary>
        /// <remarks>
        /// Each is otherwise built the first time it is drawn, which is always mid-run -- and a held
        /// bit's supersampled disc took 65-80 ms in the editor. Called as the board is set up, where
        /// a load already costs a moment and nothing on it is moving.
        /// </remarks>
        public static void WarmBits()
        {
            if (Look.Current.Bits != BitStyle.Digit)
                return;

            BitGlyph(Bit.Zero);
            BitGlyph(Bit.One);
            HeldBit(Bit.Zero);
            HeldBit(Bit.One);
        }

        /// <summary>
        /// A cross, stroked like the digits: laid over a waiting bit that an imminent collision
        /// will destroy along with the one arriving.
        /// </summary>
        /// <remarks>
        /// The two outcomes of a collision one tick out were told apart by hue alone -- amber when
        /// only the arrival dies, red when the waiting bit dies too -- and amber and red are the
        /// pair a red-green colour-blind player cannot separate. The difference between the two is
        /// whether the waiting bit survives, so the cue says exactly that, on that bit, by shape.
        /// </remarks>
        public static Sprite DoomMark() => Field("doom mark", DigitSize, p => Stroked(Mathf.Min(
            Segment(p, new Vector2(-0.62f, -0.62f), new Vector2(0.62f, 0.62f)),
            Segment(p, new Vector2(-0.62f, 0.62f), new Vector2(0.62f, -0.62f)))));

        /// <summary>How large the digit cut into a held bit is, against the stroked one.</summary>
        private const float CoinDigit = 0.6f;

        /// <summary>
        /// How wide the cut is, on the sprite's -1..1 square. Heavier than the stroked digit's, in
        /// proportion: a socket is small, and a cut much finer than this closes up at its size.
        /// </summary>
        private const float CoinStroke = 0.24f;

        /// <summary>How far a point is from the line a 1 is drawn along: a bar, a flag and a foot.</summary>
        private static float OneDistance(Vector2 p) => Mathf.Min(
            Segment(p, new Vector2(0.06f, -0.8f), new Vector2(0.06f, 0.8f)),
            Mathf.Min(
                Segment(p, new Vector2(0.06f, 0.8f), new Vector2(-0.3f, 0.5f)),
                Segment(p, new Vector2(-0.3f, -0.8f), new Vector2(0.42f, -0.8f))));

        /// <summary>How far a point is from the line a 0 is drawn along: a tall rounded loop.</summary>
        private static float ZeroDistance(Vector2 p) =>
            Mathf.Abs(Segment(p, new Vector2(0f, -0.34f), new Vector2(0f, 0.34f)) - 0.46f);

        /// <summary>How much of a stroke covers a point this far from the line it is drawn along.</summary>
        /// <remarks>
        /// Not <see cref="Mathf.SmoothStep"/>, which is not the shader smoothstep its name suggests:
        /// it eases between its first two arguments, so a stroke drawn with it covered the whole
        /// sprite and every digit came out a square.
        /// </remarks>
        private static float Stroked(float distance)
        {
            float t = Mathf.InverseLerp(
                DigitStroke * 0.5f - DigitSoftness, DigitStroke * 0.5f + DigitSoftness, distance);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>How far <paramref name="p"/> is from the segment between <paramref name="a"/> and <paramref name="b"/>.</summary>
        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

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
            // A look that changes the tile's shape adds that to the key too.
            Look look = Look.Current;
            float units = look.BoardUnits > 0f ? look.BoardUnits : 1f;
            bool shipped = units == 1f && look.BoardLineWidth == 1f;
            string key = "board:" + Palette.Current.Name + (shipped ? "" : $":{units}:{look.BoardLineWidth}");

            if (TryCached(key, out Sprite cached))
                return cached;

            // The same texels per world unit whatever the tile covers, so a larger tile is no
            // blurrier than the shipped one.
            int size = TileSize * Mathf.Max(1, Mathf.CeilToInt(units));

            var texture = NewTexture(size, TextureWrapMode.Repeat);
            var pixels = new Color32[size * size];

            Color baseColour = Palette.Current.Ground;
            Color trace = Palette.Current.GroundTrace;
            Color pad = Palette.Current.GroundPad;

            // Every size below is in world units, divided down to the tile's own 0..1.
            float thin = 0.012f * look.BoardLineWidth / units;
            float centrePad = 0.055f / units;
            float cornerPad = 0.05f / units;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    Color colour = baseColour;

                    // Lines on the tile edges and through the middle. Edge lines meet their
                    // neighbour's, so the tiling seam is invisible.
                    if (u < thin || u > 1f - thin || v < thin || v > 1f - thin) colour = trace;
                    if (Mathf.Abs(u - 0.5f) < thin || Mathf.Abs(v - 0.5f) < thin) colour = trace;

                    // Pads where the traces cross.
                    float toCentre = new Vector2(u - 0.5f, v - 0.5f).magnitude;
                    if (toCentre < centrePad) colour = pad;

                    float toCorner = Mathf.Min(
                        new Vector2(u, v).magnitude,
                        Mathf.Min(new Vector2(u - 1f, v).magnitude,
                            Mathf.Min(new Vector2(u, v - 1f).magnitude, new Vector2(u - 1f, v - 1f).magnitude)));
                    if (toCorner < cornerPad) colour = pad;

                    pixels[y * size + x] = colour;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                size / units, 0, SpriteMeshType.FullRect);

            sprite.hideFlags = HideFlags.HideAndDontSave;

            Cache[key] = sprite;
            BuiltCount++;
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

                        case BodyStyle.LitGlass:
                            alpha = cover * Mathf.Lerp(1f, GlassFill, Mathf.SmoothStep(0f, 1f, d / GlassRimTexels));
                            shade = LitGlassShade(depth, size, x, y, d);
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

            if (!FacingTheLight(depth, size, x, y, out float lit))
                return Flat;

            float nearEdge = 1f - d / BevelTexels;

            return Flat + Relief * lit * nearEdge;
        }

        /// <summary>
        /// Brightness of a lit glass body at one texel: its rim at full brightness where it faces
        /// the light and at about half where it faces away, and its see-through middle a little
        /// below full.
        /// </summary>
        private static float LitGlassShade(float[] depth, int size, int x, int y, float d)
        {
            const float Middle = 0.85f;
            const float Shadowed = 0.5f;

            float rim = 1f - Mathf.SmoothStep(0f, 1f, d / GlassRimTexels);

            if (rim <= 0f || !FacingTheLight(depth, size, x, y, out float lit))
                return Middle;

            float edge = Mathf.Lerp(Shadowed, 1f, lit * 0.5f + 0.5f);
            return Mathf.Lerp(Middle, edge, rim);
        }

        /// <summary>
        /// How squarely the surface at one texel faces a light from the top left: 1 facing it, -1
        /// facing away. False where the surface has no direction, deep in a flat middle.
        /// </summary>
        private static bool FacingTheLight(float[] depth, int size, int x, int y, out float lit)
        {
            // Depth rises inward, so its gradient points into the body and the surface faces the
            // other way. Central differences, clamped at the texture's edge.
            float gx = depth[y * size + Mathf.Min(x + 1, size - 1)] - depth[y * size + Mathf.Max(x - 1, 0)];
            float gy = depth[Mathf.Min(y + 1, size - 1) * size + x] - depth[Mathf.Max(y - 1, 0) * size + x];

            var facing = new Vector2(-gx, -gy);
            if (facing.sqrMagnitude < 1e-6f)
            {
                lit = 0f;
                return false;
            }

            // Texture y runs up, so the top left is (-1, +1).
            lit = Vector2.Dot(facing.normalized, new Vector2(-0.7071f, 0.7071f));
            return true;
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

        /// <summary>
        /// The D: a square back half, and a front half that is a semicircle of the same height.
        /// </summary>
        private static bool InD(Vector2 p, float half)
        {
            if (p.x < -half || p.x > half || Mathf.Abs(p.y) > half)
                return false;

            return p.x <= 0f || p.sqrMagnitude <= half * half;
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
