using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using BitSorter.ViewModel;
using UnityEngine;

namespace BitSorter.EditMode.Tests
{
    /// <summary>
    /// The brief every new look answers, from the critique of the shipped one. Classic is exempt from the interface's:
    /// it is the game as it shipped, and these are the things that were wrong with it. The canvas's brief, further
    /// down, holds every look.
    /// </summary>
    /// <remarks>
    /// This held the looks to the 4.x board's brief as well -- no gate, source or sink in a state colour, a 1 in a
    /// colour of its own, a bit told by its shape, full-screen panels that cover -- until Part 16b took out the colours
    /// the board drew with.
    /// </remarks>
    public class LookBriefTests
    {
        private static IEnumerable<Look> NewLooks()
        {
            foreach (Look look in Look.All)
            {
                if (look != Look.Classic)
                    yield return look;
            }
        }

        private static float Distance(Color a, Color b) =>
            Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b));

        /// <summary>
        /// Every colour text is drawn in reads on a panel at 4.5 to 1 or better, the dim text and
        /// the accent included.
        /// </summary>
        /// <remarks>
        /// A bordered panel's body is a share of its tint, so that is what the text sits on.
        /// </remarks>
        [Test]
        public void TextOnAPanel_IsReadable()
        {
            foreach (Look look in NewLooks())
            {
                Palette p = look.Colours;
                Color panel = Body(look, p.Panel, p.Ground);
                Color button = Body(look, p.PanelEdge, p.Ground);

                foreach ((string name, Color ink) in new[]
                         {
                             ("Text", p.Text), ("TextDim", p.TextDim), ("Accent", p.Accent),
                         })
                {
                    Assert.GreaterOrEqual(Contrast(ink, panel), 4.5f, $"{look.Name}: {name} on a panel");
                }

                Assert.GreaterOrEqual(Contrast(p.Text, button), 4.5f, $"{look.Name}: a button's caption");
            }
        }

        /// <summary>
        /// Every kind of button can be read, the solid one included.
        /// </summary>
        /// <remarks>
        /// The primary button is drawn solid, so its colour is the whole of what its caption sits on
        /// -- a bright one would make the loudest button on the screen the hardest to read.
        /// </remarks>
        [Test]
        public void EveryKindOfButton_CanBeRead()
        {
            foreach (Look look in NewLooks())
            {
                Palette p = look.Colours;

                Assert.GreaterOrEqual(p.ButtonPrimary.a, 0.99f, $"{look.Name}: the primary button is see-through");
                Assert.GreaterOrEqual(Contrast(p.Text, p.ButtonPrimary), 4.5f,
                    $"{look.Name}: the primary button's caption");

                Assert.GreaterOrEqual(Contrast(p.Text, Body(look, p.ButtonQuiet, p.Ground)), 4.5f,
                    $"{look.Name}: a quiet button's caption");
                Assert.GreaterOrEqual(Contrast(p.Text, Body(look, p.ButtonDestructive, p.Ground)), 4.5f,
                    $"{look.Name}: a destructive button's caption");
            }
        }

        /// <summary>
        /// A quiet button is quieter than an ordinary one, and a destructive one does not pass for
        /// either.
        /// </summary>
        /// <remarks>
        /// Quieter by its edge and not by its caption: a dim caption is how a button says it cannot
        /// be pressed, and KEEP TINKERING is always there to press.
        /// </remarks>
        [Test]
        public void ButtonsDifferByWhatTheyAreFor()
        {
            foreach (Look look in NewLooks())
            {
                Palette p = look.Colours;

                Assert.Less(Luminance(p.ButtonQuiet), Luminance(p.PanelEdge) * 0.5f,
                    $"{look.Name}: a quiet button is as loud as an ordinary one");
                Assert.Greater(Distance(p.ButtonDestructive, p.PanelEdge), 0.3f,
                    $"{look.Name}: CLEAR ALL is drawn like UNDO");
                Assert.Greater(Distance(p.ButtonPrimary, p.PanelEdge), 0.3f,
                    $"{look.Name}: the primary button is drawn in the ordinary colour");
            }
        }

        /// <summary>What a panel's middle looks like over the ground.</summary>
        private static Color Body(Look look, Color tint, Color ground)
        {
            float share = look.Panels == PanelStyle.Bordered ? 0.22f : 1f;
            Color body = new Color(tint.r * share, tint.g * share, tint.b * share);

            // Composited in linear, as the renderer does.
            return Color.Lerp(ground.linear, body.linear, tint.a).gamma;
        }

        // -----------------------------------------------------------------
        // The canvas's brief: every look's, Classic's included -- its canvas is new, not 2.0's
        // -----------------------------------------------------------------

        /// <summary>A wire in any value, and a part's outline, stand 3:1 off the ground (WCAG's for graphics).</summary>
        [Test]
        public void OnTheCanvas_EveryValueAndEveryOutline_StandsClearOfTheGround()
        {
            foreach (Look look in Look.All)
            {
                SchematicPalette c = look.Canvas;
                Assert.IsNotNull(c, $"{look.Name} has no canvas");

                foreach ((string what, Color colour) in new[]
                         { ("0", c.Zero), ("1", c.One), ("x", c.Unknown), ("z", c.HighZ), ("a bus", c.Bus), ("an outline", c.Outline) })
                {
                    Assert.GreaterOrEqual(SchematicPalette.Contrast(colour, c.Ground), 3f,
                        $"{look.Name}: {what} is not 3:1 against the canvas's ground");
                }
            }
        }

        /// <summary>Every word on the canvas reads at 4.5:1 (WCAG's for text), the quietest included.</summary>
        /// <remarks>
        /// The second cursor's time, and the span to it, are words in the cursor's colour (<c>WaveformView</c>), as a code
        /// editor's warning is: held to 4.5:1 since the accessibility review (2026-11-03), where Schematic Light's was 4.10.
        /// </remarks>
        [Test]
        public void OnTheCanvas_TextIsReadable()
        {
            foreach (Look look in Look.All)
            {
                SchematicPalette c = look.Canvas;

                foreach ((string what, Color colour) in new[]
                         {
                             ("text", c.Text), ("secondary text", c.TextDim), ("a delay", c.Muted), ("the playhead's time", c.Accent),
                             ("the second cursor's time", c.Cursor),
                         })
                {
                    Assert.GreaterOrEqual(SchematicPalette.Contrast(colour, c.Ground), 4.5f,
                        $"{look.Name}: {what} is not 4.5:1 against the canvas's ground");
                }
            }
        }

        /// <summary>
        /// A value is a word as well as a wire: each row's readout beside the waves and the digit in a change's ring are
        /// written in the value's own colour, at <see cref="UiType.Micro"/>, so every value's colour reads on the ground at
        /// 4.5:1 (WCAG's for text) and not only the 3:1 a wire needs.
        /// </summary>
        /// <remarks>
        /// The accessibility review of 2026-11-03: Schematic Light's bus readout was 4.32 and its z 3.67, and Classic's 0
        /// 4.38 -- every one above the 3:1 of a wire, which is all this brief asked of them.
        /// </remarks>
        [Test]
        public void OnTheCanvas_AValueWrittenAsAWord_ReadsOnTheGround()
        {
            foreach (Look look in Look.All)
            {
                SchematicPalette c = look.Canvas;

                foreach ((string what, Color colour) in new[]
                         { ("0", c.Zero), ("1", c.One), ("x", c.Unknown), ("z", c.HighZ), ("a bus", c.Bus) })
                {
                    Assert.GreaterOrEqual(SchematicPalette.Contrast(colour, c.Ground), 4.5f,
                        $"{look.Name}: a readout of {what} is not 4.5:1 against the canvas's ground");
                }
            }
        }

        /// <summary>
        /// A 0 and a 1 differ by more than colour: a 1 is drawn heavier, and every ring holds its
        /// digit. And by colour as well, by a good margin.
        /// </summary>
        [Test]
        public void OnTheCanvas_AZeroAndAOne_DifferByWeightAsWellAsColour()
        {
            Assert.Greater(SchematicStyle.OneWidth, SchematicStyle.ZeroWidth, "a 1 is drawn a little heavier than a 0");

            foreach (Look look in Look.All)
                Assert.GreaterOrEqual(Distance(look.Canvas.Zero, look.Canvas.One), 0.3f, $"{look.Name}: a 0 and a 1 are too alike");
        }

        /// <summary>An undriven wire is dashed, and not in a 0's or a 1's colour either.</summary>
        [Test]
        public void OnTheCanvas_AnUndrivenWire_IsNotTakenForAValue()
        {
            foreach (Look look in Look.All)
            {
                SchematicPalette c = look.Canvas;
                Assert.GreaterOrEqual(Distance(c.HighZ, c.Zero), 0.2f, $"{look.Name}: z is too like a 0");
                Assert.GreaterOrEqual(Distance(c.HighZ, c.One), 0.2f, $"{look.Name}: z is too like a 1");
            }
        }

        /// <summary>
        /// The second cursor stands 3:1 off the ground and apart from every value's colour and the
        /// playhead's, so it is never read as a wire or the moment shown (Part 9b-2b).
        /// </summary>
        [Test]
        public void OnTheCanvas_TheCursor_IsNoValueAndNotThePlayhead()
        {
            foreach (Look look in Look.All)
            {
                SchematicPalette c = look.Canvas;
                Assert.GreaterOrEqual(SchematicPalette.Contrast(c.Cursor, c.Ground), 3f, $"{look.Name}: the cursor is not 3:1 against the ground");

                foreach ((string what, Color colour) in new[]
                         { ("a 0", c.Zero), ("a 1", c.One), ("x", c.Unknown), ("z", c.HighZ), ("a bus", c.Bus), ("the playhead", c.Playhead) })
                    Assert.GreaterOrEqual(Distance(c.Cursor, colour), 0.2f, $"{look.Name}: the cursor is too like {what}");
            }
        }

        /// <summary>A selected row's band leaves its name readable at 4.5:1 and its wave at 3:1, and is not the ground.</summary>
        [Test]
        public void OnTheCanvas_ASelectedRow_StaysReadable()
        {
            foreach (Look look in Look.All)
            {
                SchematicPalette c = look.Canvas;
                Assert.Greater(Distance(c.Selection, c.Ground), 0.03f, $"{look.Name}: the selection band is the ground");

                foreach ((string what, Color colour) in new[] { ("text", c.Text), ("a row's name", c.TextDim), ("the playhead's time", c.Accent) })
                    Assert.GreaterOrEqual(SchematicPalette.Contrast(colour, c.Selection), 4.5f, $"{look.Name}: {what} on the selection band");

                foreach ((string what, Color colour) in new[] { ("0", c.Zero), ("1", c.One), ("x", c.Unknown), ("z", c.HighZ), ("a bus", c.Bus), ("the cursor", c.Cursor) })
                    Assert.GreaterOrEqual(SchematicPalette.Contrast(colour, c.Selection), 3f, $"{look.Name}: {what} on the selection band");
            }
        }

        /// <summary>
        /// A value written as a word on a row's band -- its readout, a bus's label -- reads at 4.5:1 there as on the ground:
        /// its colour taken toward the text as far as it must (<see cref="SchematicPalette.Legible"/>), and no further, so it is
        /// still its value's colour, a 0 still apart from a 1; and on the ground, where it already reads, left as it is.
        /// </summary>
        /// <remarks>
        /// The band, behind a selected row and fainter behind a pointed-at wire's, holds a value's colour to 3:1, which a wave
        /// needs and a word does not: in Schematic a 1's readout on it was 3.45:1 (the accessibility review, 2026-11-03).
        /// </remarks>
        [Test]
        public void OnTheCanvas_AValueWrittenOnABand_IsLegible_AndStillItsValuesColour()
        {
            foreach (Look look in Look.All)
            {
                SchematicPalette c = look.Canvas;
                Color faint = Color.Lerp(c.Ground, c.Selection, 0.5f);   // as WaveformView.SetBands makes it

                foreach ((string what, Color colour) in new[]
                         { ("0", c.Zero), ("1", c.One), ("x", c.Unknown), ("z", c.HighZ), ("a bus", c.Bus), ("a bus's label", c.Text) })
                {
                    Assert.AreEqual(colour, c.Legible(colour, c.Ground), $"{look.Name}: {what} is changed on the ground, where it reads");

                    foreach ((string band, Color under) in new[] { ("the selection band", c.Selection), ("a pointed-at wire's band", faint) })
                    {
                        Color written = c.Legible(colour, under);
                        Assert.GreaterOrEqual(SchematicPalette.Contrast(written, under), SchematicPalette.WordContrast,
                            $"{look.Name}: {what} written on {band}");
                        Assert.Less(Distance(written, colour), 0.3f, $"{look.Name}: {what} on {band} is no longer its value's colour");
                    }
                }

                Assert.GreaterOrEqual(Distance(c.Legible(c.Zero, c.Selection), c.Legible(c.One, c.Selection)), 0.3f,
                    $"{look.Name}: a 0 and a 1 written on the selection band are too alike");
            }
        }

        /// <summary>Reduced motion leaves out the canvas's glow and its gates' flashes, whatever the look.</summary>
        [Test]
        public void ReducedMotion_LeavesOutTheGlowAndTheFlash()
        {
            Preferences.Redirected = new Dictionary<string, int>();
            try
            {
                foreach (Look look in Look.All)
                {
                    Look.Use(look);

                    ReducedMotion.Set(false);
                    Assert.AreEqual(look.CanvasGlow, CanvasMotion.Glow, $"{look.Name}: its own glow, with motion in full");
                    Assert.AreEqual(0.5f, CanvasMotion.Flash(0.5f));

                    ReducedMotion.Set(true);
                    Assert.AreEqual(0f, CanvasMotion.Glow, $"{look.Name}: glow left with motion reduced");
                    Assert.AreEqual(0f, CanvasMotion.Flash(0.5f), $"{look.Name}: a gate flashed with motion reduced");
                }
            }
            finally
            {
                Look.Use(null);
                Preferences.Redirected = null;
            }
        }

        /// <summary>WCAG contrast ratio between two opaque colours.</summary>
        private static float Contrast(Color a, Color b)
        {
            float la = Luminance(a);
            float lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        private static float Luminance(Color c)
        {
            Color l = c.linear;
            return 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
        }
    }
}
