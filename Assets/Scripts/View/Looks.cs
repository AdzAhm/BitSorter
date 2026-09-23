using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The looks the game ships with, beyond <see cref="Look.Classic"/>.
    /// </summary>
    /// <remarks>
    /// Chosen out of three directions rendered side by side -- Instrument, Neon and Tactile -- and a
    /// second round on the one picked, which kept most of Neon and took Tactile's physical cues.
    /// The directions that were not chosen live on the looks-round-1 branch, not here: a look nobody
    /// plays is a palette nobody checks.
    ///
    /// Colours are written as hex because that is how they are chosen, and every alpha as the
    /// opacity it should look, through <see cref="Palette.Seen"/>.
    /// </remarks>
    public static class Looks
    {
        private static Color Hex(uint rgb, float alpha = 1f) => new Color(
            ((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        /// <summary>
        /// A lit circuit board: a black-violet floor ruled one cell apart with a copper pad on every
        /// cell, cyan traces, saturated glass parts lit from the top left, and bits that own the
        /// light -- a 1 that blazes magenta, a 0 that glimmers indigo.
        /// </summary>
        /// <remarks>
        /// Of the rules the board already lived by, two carry the weight here. A 1 is the only
        /// magenta on the board, so it cannot be mistaken for a part. And the parts glow at half the
        /// strength Neon first gave them, with the rim falling away on the side facing from the light,
        /// because at full strength the gates shone as loud as the bits they were passing.
        ///
        /// The AND is a deeper green than round two's lime: lime is simply the most luminous hue, and
        /// it made the AND the loudest gate on any board it was on.
        ///
        /// A bit is its own digit. Magenta and indigo are neighbours, and under bloom the indigo 0
        /// lifted to a bright violet -- a playtester could not tell a 0 from a 1 by colour, which is
        /// the reason the gates were told apart by shape in the first place.
        /// </remarks>
        public static Look NeonBoard { get; } = Look.Classic.Derive("neon-board", look =>
        {
            look.Colours = Palette.Classic.Derive("neon-board", p =>
            {
                p.Ground = Hex(0x05040C);
                p.GroundTrace = Hex(0x1C1540);
                p.GroundPad = Hex(0x1C1540);
                p.Grid = Hex(0xB06A32);

                p.WireCasing = Hex(0x020106);
                p.WireCore = Hex(0x1F8FB8);
                p.WireHover = Hex(0x8BEFFF);
                p.WireFlash = Hex(0xFFE9FF);
                p.WireMark = Hex(0x6FE0FF);
                p.DelayLabel = Hex(0xE9F4FF);
                p.DelayLabelBacking = Hex(0x05040C, Palette.Seen(0.85f));

                p.Source = Hex(0x9A7444);
                p.Sink = Hex(0x9A7444);
                p.Xor = Hex(0x3D8BFF);
                p.And = Hex(0x74D93C);
                p.Or = Hex(0xA66BFF);
                p.Nand = Hex(0x2EE6C8);
                p.Nor = Hex(0xFFD84A);
                p.Not = Hex(0xFF8A4F);
                p.Register = Hex(0xC7CCEA);
                p.OtherNode = Hex(0x8088A8);

                // A 0 glimmers in a cool indigo where a 1 blazes magenta: lit just past the bloom
                // threshold, so it never shrinks to a dot a player loses, in a colour that stays a
                // colour when lifted rather than washing to white, and far from the cyan wire under
                // it -- round two's dim teal 0 was nearly the wire's own colour.
                p.BitZero = Hex(0x3A30A0);
                p.BitOne = Hex(0xFF2E9E);

                p.PortInput = Hex(0x5A74A8);
                p.PortOutput = Hex(0xC49A5A);
                p.Waiting = Hex(0xFFB020);
                p.Doomed = Hex(0xFF3B2F);
                p.Scorch = Hex(0xFF4A3A);

                p.WirePreview = Hex(0x9AA6D6, 0.85f);
                p.WirePreviewValid = Hex(0x4DFFA0, 0.95f);
                p.WirePreviewInvalid = Hex(0xFF5A5A, 0.95f);
                p.Highlight = Hex(0xE9F4FF);

                p.Panel = Hex(0x2B8FB8, Palette.Seen(0.9f));
                p.PanelEdge = Hex(0x7A5CFF);
                p.Text = Hex(0xEAF6FF);
                p.TextDim = Hex(0x8FA3C8);
                p.Accent = Hex(0xFF5CC8);
                p.Good = Hex(0x4DFFA0);
                p.Bad = Hex(0xFF5A6E);

                p.MenuScrim = Hex(0x04030A, Palette.Seen(0.90f));
                p.ListScrim = Hex(0x04030A, Palette.Seen(0.88f));
                p.CardScrim = Hex(0x04030A, Palette.Seen(0.93f));

                // Panels here draw their edge in the colour they are given and their body at a
                // fifth of it, so a state is a full-strength colour: the edge is what says chosen,
                // or a lesson, or a refusal.
                p.Selected = Hex(0xFF5CC8);
                p.HintBackdrop = Hex(0xFF5CC8);
                p.BadgeBackdrop = Hex(0x7A2E66);
                p.ToastBackdrop = Hex(0xFF5A6E);
                p.MeterBackdrop = Hex(0xFF5A6E);
                p.Rule = Hex(0x7A2E66);
                p.Credit = Hex(0x8FA3C8);

                // The primary button is solid, so its colour is the whole button and has to hold
                // text: a deep magenta rather than the accent itself, at better than 5:1 under
                // white. Quiet is the ordinary edge taken most of the way down, and destructive
                // wears the colour of loss.
                p.ButtonPrimary = Hex(0xA82A7C);
                p.ButtonQuiet = Hex(0x3E3380);
                p.ButtonDestructive = Hex(0xFF5A6E);
            });

            look.Bodies = BodyStyle.LitGlass;
            look.Bits = BitStyle.Digit;
            look.BitScale = 1.3f;
            look.BitGlow = 0.35f;
            look.TrailWidth = 0.5f;
            look.Grid = GridStyle.Dots;
            look.Panels = PanelStyle.Bordered;
            look.BoardUnits = 4f;
            look.BoardLineWidth = 1.6f;
            look.GridMarkSize = 1.6f;
            look.BloomIntensity = 1.3f;
            look.BloomScatter = 0.7f;
            look.TrailLength = 0.45f;
            look.GateGlow = 0.7f;
            look.FixtureGlow = 0.2f;
        });
    }
}
