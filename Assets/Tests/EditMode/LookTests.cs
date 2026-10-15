using NUnit.Framework;
using BitSorter.View;
using BitSorter.ViewModel;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>What a look is, and the one conversion every new look's alphas go through.</summary>
    public class LookTests
    {
        /// <summary>
        /// Classic changes nothing: every knob a look adds is a share of what the scene already
        /// says, and Classic's share is all of it.
        /// </summary>
        [Test]
        public void Classic_LeavesTheSceneAsItIs()
        {
            Look classic = Look.Classic;

            Assert.AreEqual(1f, classic.TrailLength);
            Assert.AreEqual(1f, classic.GateGlow);
            Assert.AreEqual(1f, classic.FixtureGlow);
            Assert.AreEqual(1f, classic.BoardUnits);
            Assert.AreEqual(1f, classic.BoardLineWidth);
            Assert.AreEqual(1f, classic.GridMarkSize);
            Assert.AreSame(Palette.Classic, classic.Colours);
        }

        /// <summary>
        /// In every look, a wire's delay number sits clear of the bits passing along that wire.
        /// </summary>
        /// <remarks>
        /// Where a digit passed under the number, every 0 went by with a 1 printed over its head.
        /// The number was made a third larger after a playtest said it was too small, and its
        /// distance from the wire is now worked out from the bit and the pill; this holds that
        /// working to its purpose. Measured against the bit's box on a straight wire -- a digit's
        /// ink is narrower than its box, which is the slack a diagonal wire uses.
        /// </remarks>
        [Test]
        public void EveryLook_KeepsAWiresNumberClearOfItsBits()
        {
            try
            {
                foreach (Look look in Look.All)
                {
                    Look.Use(look);

                    float halfABit = BitRenderer.DefaultBitSize * look.BitScale * 0.5f;

                    Assert.Greater(EdgeRenderer.LabelInnerEdge, halfABit,
                        $"in {look.Name} the wire's number starts {EdgeRenderer.LabelInnerEdge:F3} from the " +
                        $"wire and a bit reaches {halfABit:F3}, so the bits pass under the number");
                }
            }
            finally
            {
                Look.Use(null);
            }
        }

        /// <summary>
        /// A wire's number is clear of its buffer triangles too, at their full size: the number sits
        /// off to one side of the wire, and a triangle stands only a little proud of it.
        /// </summary>
        [Test]
        public void EveryLook_KeepsAWiresNumberClearOfItsBuffers()
        {
            try
            {
                foreach (Look look in Look.All)
                {
                    Look.Use(look);

                    Assert.Greater(EdgeRenderer.LabelInnerEdge, EdgeRenderer.MarkSize * 0.5f,
                        $"in {look.Name} a buffer triangle reaches under the wire's number");
                }
            }
            finally
            {
                Look.Use(null);
            }
        }

        /// <summary>
        /// A wire gets one buffer triangle per tick past the first, at full size where it has room
        /// and smaller where it has not -- so neighbours never touch, however many a short wire holds.
        /// </summary>
        [Test]
        public void BufferTriangles_ShrinkOnlyToKeepApart()
        {
            Assert.AreEqual(0f, EdgeRenderer.MarkSizeFor(4f, 1), "a delay-1 wire is bare");
            Assert.AreEqual(EdgeRenderer.MarkSize, EdgeRenderer.MarkSizeFor(4f, 3), "a long wire has room");

            // Neighbouring cells: a wire from one node's face to the next's.
            float shortest = 2f - PortGeometry.NodeSize;

            for (int delay = 2; delay <= 12; delay++)
            {
                float size = EdgeRenderer.MarkSizeFor(shortest, delay);
                float apart = shortest / delay;

                Assert.Greater(size, 0f, $"a delay-{delay} wire between neighbours lost its triangles");
                Assert.Less(size, apart, $"delay-{delay} triangles on the shortest wire touch");
            }
        }

        /// <summary>Every look can be found by its name, and no two share one.</summary>
        [Test]
        public void EveryLook_IsNamedOnce()
        {
            foreach (Look look in Look.All)
            {
                Assert.IsNotNull(look.Colours, $"{look.Name} has no palette");
                Assert.AreSame(look, Look.Named(look.Name), $"{look.Name} is not the look its name finds");
            }

            Assert.IsNull(Look.Named("no such look"));
        }

        /// <summary>
        /// The player's choice is kept as a number of the look's own, so no two looks share one and a
        /// look added or moved in the list cannot change anyone's choice.
        /// </summary>
        [Test]
        public void EveryLook_IsKeptAsANumberOfItsOwn_AndOffersItselfByName()
        {
            var codes = new System.Collections.Generic.HashSet<int>();
            foreach (Look look in Look.All)
            {
                Assert.IsTrue(codes.Add(look.Code), $"{look.Name} shares its number with another look");
                Assert.IsFalse(string.IsNullOrEmpty(look.Title), $"{look.Name} has nothing for Settings to call it");
            }

            CollectionAssert.AreEqual(new[] { 3, 4, 2, 1 }, System.Linq.Enumerable.Select(Look.All, l => l.Code),
                "the numbers are what players' choices were kept as; never renumber one");
        }

        [Test]
        public void TheChosenLook_IsTheOneStored_OrTheDefault()
        {
            Preferences.Redirected = new System.Collections.Generic.Dictionary<string, int>();
            try
            {
                Assert.AreSame(Look.Default, Look.Chosen, "nothing chosen yet");

                Preferences.SetInt(Look.PreferenceKey, Looks.SchematicLight.Code);
                Assert.AreSame(Looks.SchematicLight, Look.Chosen);

                Preferences.SetInt(Look.PreferenceKey, 99);
                Assert.AreSame(Look.Default, Look.Chosen, "a look this build does not have is the default, never nothing");
            }
            finally
            {
                Preferences.Redirected = null;
            }
        }

        /// <summary>
        /// The default is the workbench as Ahmad chose it -- its canvas, at his glow -- on Neon
        /// Board's board, so making it the default moved no pixel of the game.
        /// </summary>
        [Test]
        public void TheDefault_IsSchematic_OnNeonBoardsBoard()
        {
            Assert.AreSame(Looks.Schematic, Look.Default);
            Assert.AreSame(SchematicPalette.Dark, Looks.Schematic.Canvas);
            Assert.AreEqual(SchematicStyle.GlowStrength, Looks.Schematic.CanvasGlow);

            foreach (Look schematic in new[] { Looks.Schematic, Looks.SchematicLight })
            {
                Assert.AreSame(Looks.NeonBoard.Colours, schematic.Colours, $"{schematic.Name} should keep Neon Board's board");
                Assert.AreEqual(Looks.NeonBoard.Bodies, schematic.Bodies);
                Assert.AreEqual(Looks.NeonBoard.BloomIntensity, schematic.BloomIntensity);
            }
        }

        /// <summary>Nothing and everything are the same written as seen.</summary>
        [Test]
        public void Seen_KeepsTheEnds()
        {
            Assert.AreEqual(0f, Palette.Seen(0f), 1e-6f);
            Assert.AreEqual(1f, Palette.Seen(1f), 1e-6f);
        }

        /// <summary>
        /// The measured case: a black scrim written at 0.88 was seen as about 62% opaque, so asking
        /// for 62% has to give 0.88 back.
        /// </summary>
        [Test]
        public void Seen_UndoesTheLinearBlend()
        {
            Assert.AreEqual(0.88f, Palette.Seen(0.62f), 0.005f);
        }

        /// <summary>Asking for more opacity never gives less.</summary>
        [Test]
        public void Seen_Rises()
        {
            float previous = 0f;

            for (int step = 1; step <= 20; step++)
            {
                float alpha = Palette.Seen(step / 20f);
                Assert.GreaterOrEqual(alpha, previous);
                previous = alpha;
            }
        }
    }
}
