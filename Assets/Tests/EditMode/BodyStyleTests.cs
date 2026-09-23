using System;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// How gate bodies are filled in each look: outlined, glass, raised -- and what no style may do.
    /// </summary>
    public class BodyStyleTests
    {
        private static readonly BodyStyle[] Styles =
            { BodyStyle.Outline, BodyStyle.Glass, BodyStyle.Raised, BodyStyle.LitGlass };

        /// <summary>Every shape a node's body can take.</summary>
        private static readonly Func<BodyStyle, Sprite>[] Bodies =
        {
            ProceduralSprites.CircleBubble, ProceduralSprites.RoundedSquare,
            ProceduralSprites.RoundedSquareBubble, ProceduralSprites.Shield,
            ProceduralSprites.ShieldBubble, ProceduralSprites.ShieldArc,
            ProceduralSprites.Capsule, ProceduralSprites.Hexagon, ProceduralSprites.FlipFlop,
        };

        /// <summary>
        /// A style changes how a body is filled and never where it is: the covered texels are
        /// exactly the filled body's.
        /// </summary>
        /// <remarks>
        /// Shape is what tells gates apart, because bloom washes colour toward white -- and aspect
        /// ratio is the one cue that survives even that. A style that nibbled an edge, or bled past
        /// one, would be quietly spending the thing every other rule on the board protects.
        /// </remarks>
        [Test]
        public void AStyle_NeverChangesASilhouette()
        {
            foreach (Func<BodyStyle, Sprite> body in Bodies)
            {
                Color32[] filled = body(BodyStyle.Filled).texture.GetPixels32();

                foreach (BodyStyle style in Styles)
                {
                    Color32[] styled = body(style).texture.GetPixels32();

                    for (int i = 0; i < filled.Length; i++)
                    {
                        if ((filled[i].a > 0) != (styled[i].a > 0))
                        {
                            Assert.Fail($"{body.Method.Name} drawn {style} covers texel {i} differently " +
                                        "from the filled body, so its silhouette has moved");
                        }
                    }
                }
            }
        }

        /// <summary>Filled is the shipped sprite itself, not a copy that happens to match.</summary>
        [Test]
        public void Filled_IsTheShippedSprite()
        {
            Assert.AreSame(ProceduralSprites.RoundedSquare(), ProceduralSprites.RoundedSquare(BodyStyle.Filled));
        }

        /// <summary>An outline is solid along the edge and faint in the middle.</summary>
        [Test]
        public void AnOutline_IsSolidAtTheEdge_AndFaintInside()
        {
            Sprite sprite = ProceduralSprites.RoundedSquare(BodyStyle.Outline);
            int size = sprite.texture.width;
            Color32[] pixels = sprite.texture.GetPixels32();
            int row = size / 2;

            int edge = FirstFullyCovered(ProceduralSprites.RoundedSquare().texture.GetPixels32(), size, row);

            Assert.AreEqual(255, pixels[row * size + edge + 2].a, "the stroke is not solid just inside the edge");

            byte middle = pixels[row * size + size / 2].a;
            Assert.That(middle, Is.InRange(20, 70), "the middle of an outlined body should be a faint fill");
        }

        /// <summary>A raised body is lit from the top left and shadowed on the far side.</summary>
        [Test]
        public void ARaisedBody_IsLitFromTheTopLeft()
        {
            Sprite sprite = ProceduralSprites.RoundedSquare(BodyStyle.Raised);
            int size = sprite.texture.width;
            Color32[] pixels = sprite.texture.GetPixels32();
            int row = size / 2;

            int left = FirstFullyCovered(ProceduralSprites.RoundedSquare().texture.GetPixels32(), size, row) + 2;
            int right = size - 1 - left;

            Assert.Greater(pixels[row * size + left].r, pixels[row * size + right].r,
                "the edge facing the light should be brighter than the edge facing away from it");
        }

        /// <summary>
        /// Lit glass has a rim brighter on the side facing the light than on the side facing away,
        /// and the same see-through middle as glass.
        /// </summary>
        [Test]
        public void LitGlass_IsBrighterOnTheLitRim()
        {
            Sprite sprite = ProceduralSprites.RoundedSquare(BodyStyle.LitGlass);
            int size = sprite.texture.width;
            Color32[] pixels = sprite.texture.GetPixels32();
            int row = size / 2;

            int left = FirstFullyCovered(ProceduralSprites.RoundedSquare().texture.GetPixels32(), size, row) + 2;
            int right = size - 1 - left;

            Assert.Greater(pixels[row * size + left].r, pixels[row * size + right].r,
                "the rim facing the light should be brighter than the rim facing away from it");

            byte glassMiddle = ProceduralSprites.RoundedSquare(BodyStyle.Glass).texture.GetPixels32()[row * size + size / 2].a;
            Assert.AreEqual(glassMiddle, pixels[row * size + size / 2].a, "the middle should be as see-through as glass");
        }

        /// <summary>The distance field measures each inside texel to the nearest outside one.</summary>
        [Test]
        public void DepthInside_MeasuresToTheNearestOutside()
        {
            const int size = 12;
            var coverage = new float[size * size];

            // An 8 by 8 block, texels 2 to 9 on both axes.
            for (int y = 2; y < 10; y++)
                for (int x = 2; x < 10; x++)
                    coverage[y * size + x] = 1f;

            float[] depth = ProceduralSprites.DepthInside(size, coverage);

            Assert.AreEqual(0f, depth[0], "outside is at depth zero");
            Assert.AreEqual(1f, depth[5 * size + 2], 1e-4f, "an edge texel is one step from outside");
            Assert.AreEqual(4f, depth[5 * size + 5], 1e-4f, "four steps in from the block's left edge");
        }

        private static int FirstFullyCovered(Color32[] pixels, int size, int row)
        {
            for (int x = 0; x < size; x++)
            {
                if (pixels[row * size + x].a == 255)
                    return x;
            }

            Assert.Fail("no fully covered texel on the middle row");
            return -1;
        }
    }
}
