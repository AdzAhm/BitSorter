using UnityEngine;
using BitSorter.ViewModel;

namespace BitSorter.View
{
    /// <summary>
    /// The looks the game ships with, beyond <see cref="Look.Classic"/>.
    /// </summary>
    /// <remarks>
    /// **The two Schematic looks are a canvas on Neon Board's interface.** They draw the 5.0 workbench in the
    /// look Ahmad chose for it (and on paper), and keep Neon Board's panels and colours: a look nobody plays is a palette
    /// nobody checks, and drawing the interface a third way would be work for no one.
    /// </remarks>
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
        /// A lit circuit board: a black-violet floor ruled one cell apart, cyan traces, and a 1 that blazes magenta where a
        /// 0 glimmers indigo.
        /// </summary>
        /// <remarks>
        /// A 1 is the only magenta, so it cannot be mistaken for anything else. It was the 4.x board's look, which
        /// drew saturated glass parts lit from the top left; what is left of it is its colours and its bordered panels.
        /// </remarks>
        public static Look NeonBoard { get; } = Look.Classic.Derive("neon-board", look =>
        {
            look.Colours = Palette.Classic.Derive("neon-board", p =>
            {
                p.Ground = Hex(0x05040C);
                p.GroundTrace = Hex(0x1C1540);

                p.WireCore = Hex(0x1F8FB8);

                // A 0 glimmers in a cool indigo where a 1 blazes magenta, far from the cyan wire under
                // it -- round two's dim teal 0 was nearly the wire's own colour.
                p.BitZero = Hex(0x3A30A0);
                p.BitOne = Hex(0xFF2E9E);

                p.Scorch = Hex(0xFF4A3A);

                p.Panel = Hex(0x2B8FB8, Palette.Seen(0.9f));
                p.PanelEdge = Hex(0x7A5CFF);
                p.Text = Hex(0xEAF6FF);
                p.TextDim = Hex(0x8FA3C8);
                p.Accent = Hex(0xFF5CC8);

                // The primary button is solid, so its colour is the whole button and has to hold
                // text: a deep magenta rather than the accent itself, at better than 5:1 under
                // white. Quiet is the ordinary edge taken most of the way down, and destructive
                // wears the colour of loss.
                p.ButtonPrimary = Hex(0xA82A7C);
                p.ButtonQuiet = Hex(0x3E3380);
                p.ButtonDestructive = Hex(0xFF5A6E);
            });

            look.Panels = PanelStyle.Bordered;

            // The canvas in the board's own colours: its black-violet ground and ruled traces, a 1
            // in the board's magenta, a 0 a lighter indigo than the board's bit, a bus in a cyan of
            // the board's wires, the gates outlined in its trace colour, and twice the Schematic look's
            // glow, since glow is what the look is.
            look.Title = "NEON BOARD";
            look.Code = 2;
            Palette board = look.Colours;
            look.Canvas = SchematicPalette.Dark.Derive("neon-board", c =>
            {
                c.Ground = board.Ground;
                c.CardEdge = board.GroundTrace;
                c.Outline = board.WireCore;

                c.Text = board.Text;
                c.TextDim = board.TextDim;
                c.Muted = Hex(0x7383AA);
                c.Accent = board.Accent;

                c.Zero = Hex(0x7A6CFF);
                c.One = board.BitOne;
                c.Unknown = board.Scorch;
                c.HighZ = board.TextDim;
                // A mid cyan, between the board's wire and its marks: the marks' own was the loudest
                // stroke on the screen at Part 8's review.
                c.Bus = Hex(0x3FA9C9);

                c.Playhead = board.Accent;
                c.Grid = board.GroundTrace;
                c.Chip = Hex(0x0E0B1E);
                c.ChipEdge = board.GroundTrace;
                c.Cursor = Hex(0xFFD166);
                c.Selection = Color.Lerp(board.Ground, board.Accent, 0.16f);
            });
            look.CanvasGlow = 0.7f;
        });

        /// <summary>
        /// The 5.0 workbench as the look reference draws it, made better (Ahmad's choice, 2026-10-12:
        /// Quiet with a touch of Lively). Neon Board's interface.
        /// </summary>
        public static Look Schematic { get; } = NeonBoard.Derive("schematic", look =>
        {
            look.Title = "SCHEMATIC";
            look.Code = 3;
            look.Canvas = SchematicPalette.Dark;
            look.CanvasGlow = SchematicStyle.GlowStrength;
        });

        /// <summary>The Schematic look on paper, for a report's screenshots, with a little less glow, as glow is light.</summary>
        public static Look SchematicLight { get; } = NeonBoard.Derive("schematic-light", look =>
        {
            look.Title = "SCHEMATIC LIGHT";
            look.Code = 4;
            look.Canvas = SchematicPalette.Light;
            look.CanvasGlow = 0.25f;
        });
    }
}
