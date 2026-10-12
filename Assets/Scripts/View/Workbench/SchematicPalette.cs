using BitSorter.SystemVerilog;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The Schematic look's colours: what the 5.0 workbench draws with, as <see cref="Palette"/> is
    /// what the 4.x board draws with.
    /// </summary>
    /// <remarks>
    /// A set of its own rather than more fields on <see cref="Palette"/>: the workbench draws
    /// different things -- a value on a wire rather than a token, x and z, buses -- and borrowing
    /// fields named for sources, sinks and registers would make two looks share colours by accident,
    /// which is how every 4.x source once came out the error colour. Palette's rules hold all the
    /// same: every colour the workbench draws is here and none is a literal where it is drawn, and a
    /// palette is never changed in place. Part 8 makes the look a choice in Settings and decides
    /// how this and <see cref="Palette"/> sit together.
    ///
    /// The values are the look reference's ground, value colours and grey labels, as the mock Ahmad
    /// chose has them (Quiet with a touch of Lively, 2026-10-12). Where the mock wrote a white at
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
            Bus = Rgb(214, 211, 201),

            Playhead = Rgb(212, 83, 126),
            Grid = Rgb(40, 40, 40),
            Chip = Rgb(29, 29, 28),
            ChipEdge = Rgb(44, 44, 44),
        };

        /// <summary>The palette the workbench draws with. One until Part 8 adds the light variant.</summary>
        public static SchematicPalette Current => Dark;

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
    }
}
