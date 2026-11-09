using BitSorter.SystemVerilog;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The canvas's colours: what the 5.0 workbench draws with, where <see cref="Palette"/> is the
    /// interface's -- its panels, text and buttons -- and the ground the camera clears to, and was the
    /// 4.x board's too until Part 16b. Every <see cref="Look"/> has one (<see cref="Look.Canvas"/>).
    /// </summary>
    /// <remarks>
    /// A set of its own rather than more fields on <see cref="Palette"/>: the workbench draws
    /// different things -- a value on a wire rather than a token, x and z, buses -- and borrowing
    /// fields named for sources, sinks and registers would make two looks share colours by accident,
    /// which is how every 4.x source once came out the error colour. Palette's rules hold all the
    /// same: every colour the workbench draws is here and none is a literal where it is drawn, and a
    /// palette is never changed in place -- <see cref="Derive"/> a new one.
    ///
    /// <see cref="Dark"/> is the Schematic look: the look reference's ground, value colours and grey
    /// labels, as the mock Ahmad chose has them (Quiet with a touch of Lively, 2026-10-12).
    /// <see cref="Light"/> is the same on paper, for a report's screenshots; Neon Board's and
    /// Classic's canvases are made from their own boards' colours. Where the mock wrote a white at
    /// some opacity over the ground -- an outline, the card's hairline, the grid -- the colour here
    /// is what that blend came to, written out opaque: a translucent colour blends differently in
    /// this project's linear colour space than in a browser, and an opaque one is the same in both.
    /// </remarks>
    public sealed class SchematicPalette
    {
        /// <summary>What the look is called.</summary>
        public string Name { get; private set; }

        /// <summary>The flat ground under everything, and the hairline round the card.</summary>
        public Color Ground, CardEdge;

        /// <summary>A part's outline at rest: gates and port boxes.</summary>
        public Color Outline;

        /// <summary>Port names; secondary text such as a part's name; the quietest, delays and tick numbers.</summary>
        public Color Text, TextDim, Muted;

        /// <summary>The playhead's time.</summary>
        public Color Accent;

        /// <summary>A one-bit value's colour: 0, 1, unknown and undriven.</summary>
        public Color Zero, One, Unknown, HighZ;

        /// <summary>A bus with a known value.</summary>
        public Color Bus;

        /// <summary>The playhead's line.</summary>
        public Color Playhead;

        /// <summary>The waveform's time grid.</summary>
        public Color Grid;

        /// <summary>A small rounded chip: its fill and its edge.</summary>
        public Color Chip, ChipEdge;

        /// <summary>
        /// The second cursor's line and its time (Part 9b-2b): a colour no value, nor the playhead,
        /// is drawn in, so a cursor is never read as a wire or the moment shown -- and never red, which
        /// means x.
        /// </summary>
        public Color Cursor;

        /// <summary>
        /// The band behind a selected row, and behind the rows of the wire the pointer is on: the
        /// accent faintly over the ground, written out opaque, with every word and value still readable on it.
        /// </summary>
        public Color Selection;

        /// <summary>The look on its dark ground, as the reference draws it.</summary>
        public static SchematicPalette Dark { get; } = new SchematicPalette
        {
            Name = "schematic",

            Ground = Rgb(21, 21, 21),
            CardEdge = Rgb(44, 44, 44),
            Outline = Rgb(150, 150, 150),

            Text = Rgb(241, 239, 232),
            TextDim = Rgb(194, 192, 182),
            Muted = Rgb(156, 154, 146),
            Accent = Rgb(133, 183, 235),

            Zero = Rgb(127, 119, 221),
            One = Rgb(212, 83, 126),
            Unknown = Rgb(229, 72, 77),
            HighZ = Rgb(138, 138, 134),
            // A warm sand rather than the near-white the reference's text is: at Part 8's review the
            // bus was the brightest thing on the screen.
            Bus = Rgb(180, 174, 156),

            Playhead = Rgb(212, 83, 126),
            Grid = Rgb(40, 40, 40),
            Chip = Rgb(29, 29, 28),
            ChipEdge = Rgb(44, 44, 44),
            Cursor = Rgb(230, 196, 92),
            Selection = Rgb(39, 47, 55),
        };

        /// <summary>
        /// The Schematic look on paper, for screenshots in a report: the same value colours taken
        /// dark enough to hold 3:1 on a light ground, grey labels at 4.5:1, and a bus drawn dark.
        /// </summary>
        public static SchematicPalette Light { get; } = new SchematicPalette
        {
            Name = "schematic-light",

            Ground = Rgb(247, 246, 242),
            CardEdge = Rgb(214, 212, 204),
            Outline = Rgb(110, 110, 106),

            Text = Rgb(30, 30, 28),
            TextDim = Rgb(74, 73, 68),
            Muted = Rgb(102, 101, 96),
            Accent = Rgb(36, 99, 170),

            Zero = Rgb(92, 82, 201),
            One = Rgb(194, 57, 108),
            Unknown = Rgb(208, 46, 52),
            HighZ = Rgb(128, 128, 124),
            // A warm grey rather than a near-black slab, the light look's answer to the same review.
            Bus = Rgb(120, 116, 104),

            Playhead = Rgb(194, 57, 108),
            Grid = Rgb(230, 228, 222),
            Chip = Rgb(238, 236, 230),
            ChipEdge = Rgb(214, 212, 204),
            // Its time is written in it, so 4.5:1 as a word: (156, 112, 0) was 4.10 (the accessibility review, 2026-11-03).
            Cursor = Rgb(146, 104, 0),
            Selection = Rgb(224, 232, 241),
        };

        /// <summary>
        /// Classic's canvas: 2.0's board drawn as a schematic -- its navy ground, its slate 0 and
        /// yellow 1, its teal wires as the outlines and its pale teal for a bus. Made from
        /// <see cref="Palette.Classic"/>, so the two cannot drift apart.
        /// </summary>
        public static SchematicPalette Classic { get; } = FromClassic(Palette.Classic);

        private static SchematicPalette FromClassic(Palette p) => new SchematicPalette
        {
            Name = "classic",

            Ground = p.Ground,
            CardEdge = Opaque(p.PanelEdge),
            Outline = p.WireCore,

            Text = p.Text,
            TextDim = p.TextDim,
            // A little under TextDim and still 4.5:1: Classic has no third grey of its own.
            Muted = new Color(0.47f, 0.52f, 0.60f),
            Accent = p.Accent,

            Zero = p.BitZero,
            One = p.BitOne,
            Unknown = p.Scorch,
            // Classic's 0 is already a grey-blue, so an undriven wire is a plain light grey -- and dashed.
            HighZ = new Color(0.70f, 0.70f, 0.70f),
            // Between the board's wire and its marks: its marks' own pale teal was the loudest stroke.
            Bus = new Color(0.46f, 0.66f, 0.74f),

            Playhead = p.Accent,
            Grid = p.GroundTrace,
            Chip = Opaque(p.Panel),
            ChipEdge = Opaque(p.PanelEdge),
            // Classic's 1 is the yellow a cursor would be anywhere else, so its cursor is its text's white.
            Cursor = p.Text,
            Selection = Color.Lerp(p.Ground, p.Accent, 0.16f),
        };

        /// <summary>The palette the workbench draws with: the current look's canvas.</summary>
        public static SchematicPalette Current => Overriding ?? Look.Current.Canvas ?? Dark;

        /// <summary>
        /// The palette drawn in instead of the look's, for as long as it is set: Schematic Light while the
        /// waveform's PNG is drawn (<see cref="WaveformPng"/>), and null the rest of the time.
        /// </summary>
        public static SchematicPalette Overriding { get; set; }

        /// <summary>A copy of this palette under a new name, changed by <paramref name="change"/>.</summary>
        public SchematicPalette Derive(string name, System.Action<SchematicPalette> change)
        {
            var copy = (SchematicPalette)MemberwiseClone();
            copy.Name = name;
            copy._words = null;   // worked out from this palette's colours, which the copy may change
            change?.Invoke(copy);
            return copy;
        }

        /// <summary>The colour a value is drawn in: 0, 1, x or z for one bit; for a bus, x if any bit is unknown, z if all are undriven.</summary>
        public Color Of(LogicVec value)
        {
            if (value.Width == 1)
            {
                switch (value[0])
                {
                    case Logic4.Zero: return Zero;
                    case Logic4.One: return One;
                    case Logic4.Z: return HighZ;
                    default: return Unknown;
                }
            }

            if (value.IsAllZ) return HighZ;
            return value.IsKnown ? Bus : Unknown;
        }

        /// <summary>
        /// The colour <paramref name="value"/> is written in as a word on <see cref="Ground"/>: the digit in a change's ring,
        /// which is filled with the ground. Its own colour (<see cref="Of"/>) where that reads at <see cref="WordContrast"/>,
        /// and otherwise lifted toward the text as far as it must (<see cref="Legible"/>) -- in Schematic Light a z and a bus,
        /// 3.67:1 and 4.32 in their own colours, and in Classic a 0, 4.38 (the accessibility review, 2026-11-03). The wire
        /// keeps its colour; only the word is lifted.
        /// </summary>
        /// <remarks>
        /// Every ring's digit asks it every frame, so the five value colours' words are worked out the first time it is asked
        /// and kept: a palette is never changed in place, and <see cref="Derive"/> gives its copy none of them.
        /// </remarks>
        public Color WordOf(LogicVec value)
        {
            if (_words == null)
                _words = new[] { Legible(Zero, Ground), Legible(One, Ground), Legible(Unknown, Ground), Legible(HighZ, Ground), Legible(Bus, Ground) };

            Color ink = Of(value);
            if (ink == Zero) return _words[0];
            if (ink == One) return _words[1];
            if (ink == Unknown) return _words[2];
            if (ink == HighZ) return _words[3];
            return ink == Bus ? _words[4] : Legible(ink, Ground);
        }

        // WordOf's five, in Of's colours' order -- 0, 1, x, z, a bus -- once it has been asked.
        private Color[] _words;

        /// <summary>
        /// <paramref name="colour"/> with the alpha that makes it look <paramref name="opacity"/>
        /// opaque over <see cref="Ground"/>, as a browser blends it.
        /// </summary>
        /// <remarks>
        /// The project renders in linear colour space, so the interface blends there, and a light
        /// colour at low alpha over a dark ground comes out far stronger than the same alpha in a
        /// browser, which blends the sRGB values: the mock's glow at 35% would have looked nearer
        /// 60%. This finds the alpha whose linear blend has the brightness the browser's blend has.
        /// The reverse of <see cref="Palette.Seen"/>'s problem, which is a dark scrim over light.
        /// </remarks>
        public Color Over(Color colour, float opacity)
        {
            opacity = Mathf.Clamp01(opacity);

            float ground = Luminance(Ground);
            float full = Luminance(colour);
            float wanted = Luminance(Color.LerpUnclamped(Ground, colour, opacity));

            float alpha = Mathf.Abs(full - ground) < 1e-5f ? opacity : Mathf.Clamp01((wanted - ground) / (full - ground));
            return new Color(colour.r, colour.g, colour.b, alpha);
        }

        /// <summary>The band along a selected wire, over the drawing: the accent, faint enough for the wire's own colour to show through it.</summary>
        public Color SelectedWireBand => Over(Accent, 0.45f);

        /// <summary>The band along the timing path's wires (Part 14b), in the second cursor's colour.</summary>
        public Color PathBand => Over(Cursor, 0.4f);

        /// <summary>An x's box on the waves: its faint fill.</summary>
        public Color HatchFill => Over(Unknown, 0.18f);

        /// <summary>An x's box on the waves: the bars across it.</summary>
        public Color HatchBar => Over(Unknown, 0.8f);

        /// <summary>What WCAG 2 asks of a word against what it is written on: 4.5 to 1.</summary>
        public const float WordContrast = 4.5f;

        /// <summary>How finely <see cref="Legible"/> steps a colour toward the text's.</summary>
        private const int LegibleSteps = 32;

        /// <summary>
        /// <paramref name="ink"/> as a word written on <paramref name="ground"/>: itself where it reads there at
        /// <see cref="WordContrast"/>, and otherwise the least step of it toward <see cref="Text"/> that does, its alpha kept.
        /// </summary>
        /// <remarks>
        /// For a value's readout, on the ground or a row's band (<see cref="Selection"/>), where the value's own colour is held
        /// to 3:1, as its wave needs, but not always the 4.5:1 a word needs: in Schematic a 1 on the band was 3.45:1, and on
        /// Schematic Light's paper z was 3.67 and a bus 4.32 (the accessibility review, 2026-11-03). The colours themselves
        /// stay: darkened to read, Schematic Light's z was its outline's grey. Stepped toward the text rather than to it, so the
        /// word keeps the value's hue -- a 1 on the band is a lighter pink, never white -- and only as far as it must. Derived
        /// from the palette, never a colour of its own.
        /// </remarks>
        public Color Legible(Color ink, Color ground)
        {
            if (Contrast(ink, ground) >= WordContrast)
                return ink;

            for (int step = 1; step < LegibleSteps; step++)
            {
                Color lifted = Color.Lerp(ink, Text, step / (float)LegibleSteps);
                if (Contrast(lifted, ground) >= WordContrast)
                    return new Color(lifted.r, lifted.g, lifted.b, ink.a);
            }

            return new Color(Text.r, Text.g, Text.b, ink.a);
        }

        /// <summary>Relative luminance, from a colour written in sRGB (WCAG 2's definition).</summary>
        public static float Luminance(Color colour)
        {
            Color linear = colour.linear;
            return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
        }

        /// <summary>WCAG 2's contrast ratio between two opaque colours, from 1 to 21.</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        private static Color Opaque(Color colour) => new Color(colour.r, colour.g, colour.b, 1f);
    }
}
