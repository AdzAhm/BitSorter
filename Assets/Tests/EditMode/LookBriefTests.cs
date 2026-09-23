using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The brief every new look answers, from the critique of the shipped one. Classic is exempt:
    /// it is the game as it shipped, and these are the things that were wrong with it.
    /// </summary>
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

        /// <summary>Everything on the board that has a fill of its own.</summary>
        private static IEnumerable<(string, Color)> BoardFills(Palette p)
        {
            yield return ("Source", p.Source);
            yield return ("Sink", p.Sink);
            yield return ("Xor", p.Xor);
            yield return ("And", p.And);
            yield return ("Or", p.Or);
            yield return ("Nand", p.Nand);
            yield return ("Nor", p.Nor);
            yield return ("Not", p.Not);
            yield return ("Register", p.Register);
            yield return ("BitZero", p.BitZero);
            yield return ("OtherNode", p.OtherNode);
            yield return ("WireCore", p.WireCore);
            yield return ("WireMark", p.WireMark);
            yield return ("WireFlash", p.WireFlash);
            yield return ("PortInput", p.PortInput);
            yield return ("PortOutput", p.PortOutput);
            yield return ("Grid", p.Grid);
            yield return ("GroundTrace", p.GroundTrace);
            yield return ("GroundPad", p.GroundPad);
            yield return ("Waiting", p.Waiting);
            yield return ("Doomed", p.Doomed);
            yield return ("Highlight", p.Highlight);
        }

        private static float Distance(Color a, Color b) =>
            Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b));

        /// <summary>
        /// Good, Bad and Accent say something about state, and nothing on the board wears one.
        /// </summary>
        /// <remarks>
        /// The shipped board wore them to the last digit: every source was Good, every sink Bad and
        /// the NAND gate Accent, so every output on every board looked like an error.
        /// </remarks>
        [Test]
        public void NothingOnTheBoard_WearsAStateColour()
        {
            foreach (Look look in NewLooks())
            {
                Palette p = look.Colours;

                foreach ((string name, Color fill) in BoardFills(p))
                {
                    if (name == "Waiting" || name == "Doomed")
                        continue;   // these are state, drawn on the board on purpose

                    Assert.Greater(Distance(fill, p.Good), 0.12f, $"{look.Name}: {name} is Good");
                    Assert.Greater(Distance(fill, p.Bad), 0.12f, $"{look.Name}: {name} is Bad");
                    Assert.Greater(Distance(fill, p.Accent), 0.12f, $"{look.Name}: {name} is Accent");
                }
            }
        }

        /// <summary>A 1 is the only thing on the board in its colour.</summary>
        /// <remarks>
        /// The shipped board had three yellows -- the AND, the NOR and a 1 -- told apart by shape
        /// alone. A bit is small and moving; it should not have to be told apart from a gate.
        /// </remarks>
        [Test]
        public void AOne_HasAColourOfItsOwn()
        {
            foreach (Look look in NewLooks())
            {
                Palette p = look.Colours;

                foreach ((string name, Color fill) in BoardFills(p))
                {
                    Assert.Greater(Distance(p.BitOne, fill), 0.25f,
                        $"{look.Name}: a 1 is too close to {name} to be told apart by colour");
                }
            }
        }

        /// <summary>
        /// A 0 glows in a colour of its own: lifted for bloom it still reads as a colour, not as
        /// white, and it stands apart from the wire it travels on.
        /// </summary>
        /// <remarks>
        /// Two earlier decisions meet here. A 0 was made to glow because unlit it was a small grey
        /// dot a player lost track of; lit, the shipped one washed out to near-white and read like a
        /// 1. And the first draft of the new look went back to an unlit 0, dim teal, which vanished
        /// into its cyan wire. What survives both is a 0 that glows in its own hue. That it glows at
        /// all -- and less than a 1 -- is BitEmissionTests' to say.
        /// </remarks>
        [Test]
        public void AZero_GlowsInItsOwnColour()
        {
            foreach (Look look in NewLooks())
            {
                Color lifted = BitVisuals.Emissive(look.Colours.BitZero);
                Color shown = new Color(Mathf.Clamp01(lifted.r), Mathf.Clamp01(lifted.g), Mathf.Clamp01(lifted.b));

                float max = Mathf.Max(shown.r, Mathf.Max(shown.g, shown.b));
                float min = Mathf.Min(shown.r, Mathf.Min(shown.g, shown.b));

                Assert.GreaterOrEqual((max - min) / max, 0.4f,
                    $"{look.Name}: a 0 lifted for bloom washes out towards white");

                Assert.Greater(Distance(shown, look.Colours.WireCore), 0.3f,
                    $"{look.Name}: a 0 is drawn close to the colour of the wire it travels on");
            }
        }

        /// <summary>
        /// The chosen button -- the part in hand, the level being played, the speed in use -- stands
        /// out from the buttons beside it, at full strength.
        /// </summary>
        /// <remarks>
        /// It was the accent multiplied down, which darkens it and makes it see-through at once. On
        /// a panel with a drawn edge that made the chosen row the dimmest in the list: the level the
        /// player was on read as the one to skip.
        /// </remarks>
        [Test]
        public void AChosenButton_StandsOutFromTheRest()
        {
            foreach (Look look in NewLooks())
            {
                Palette p = look.Colours;

                Assert.GreaterOrEqual(p.Selected.a, 0.99f, $"{look.Name}: the chosen button is see-through");
                Assert.Greater(Distance(p.Selected, p.PanelEdge), 0.3f,
                    $"{look.Name}: the chosen button is drawn much like the ones beside it");
            }
        }

        /// <summary>A full-screen panel is seen at least 85% opaque, so it covers the board.</summary>
        [Test]
        public void AFullScreenPanel_CoversTheBoard()
        {
            float least = Palette.Seen(0.85f);

            foreach (Look look in NewLooks())
            {
                Palette p = look.Colours;

                Assert.GreaterOrEqual(p.MenuScrim.a, least, $"{look.Name}: the menu lets the board through");
                Assert.GreaterOrEqual(p.ListScrim.a, least, $"{look.Name}: the level list lets the board through");
                Assert.GreaterOrEqual(p.CardScrim.a, least, $"{look.Name}: the cards let the board through");
            }
        }

        /// <summary>
        /// Every colour text is drawn in reads on a panel at 4.5 to 1 or better, the dim text and
        /// the green of a solved row included.
        /// </summary>
        /// <remarks>
        /// Solved rows in the shipped level list were 4.17 to 1. A bordered panel's body is a share
        /// of its tint, so that is what the text sits on.
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
                             ("Good", p.Good), ("Bad", p.Bad),
                         })
                {
                    Assert.GreaterOrEqual(Contrast(ink, panel), 4.5f, $"{look.Name}: {name} on a panel");
                }

                Assert.GreaterOrEqual(Contrast(p.Text, button), 4.5f, $"{look.Name}: a button's caption");
            }
        }

        /// <summary>
        /// A 0 and a 1 in flight differ by their shape, not only their colour.
        /// </summary>
        /// <remarks>
        /// Neon Board shipped with a magenta 1 and an indigo 0 -- neighbours on the wheel, and
        /// under bloom the 0 lifted to a bright violet. A playtester could not tell them apart,
        /// which is the reason the gates were told apart by shape in the first place.
        /// </remarks>
        [Test]
        public void AZeroAndAOne_DifferByShape()
        {
            foreach (Look look in NewLooks())
            {
                Assert.AreEqual(BitStyle.Digit, look.Bits,
                    $"{look.Name}: a bit in flight says its value by colour alone");
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
