using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// How a panel's backdrop is drawn in each look, and what the bordered one may not change.
    /// </summary>
    public class PanelStyleTests
    {
        /// <summary>Filled is the shipped sprite itself, not a copy that happens to match.</summary>
        [Test]
        public void Filled_IsTheShippedSprite()
        {
            Assert.AreSame(ProceduralSprites.Panel(), ProceduralSprites.Panel(PanelStyle.Filled));
        }

        /// <summary>
        /// A bordered panel covers exactly the texels a filled one does and slices at the same
        /// border, so a look can change how panels are drawn and never where their edges fall.
        /// </summary>
        /// <remarks>
        /// Every panel is laid out against the filled sprite's corners. A bordered sprite with a
        /// different slice would round its corners at another radius, and one that covered
        /// different texels would put its edge a pixel off every panel in the game.
        /// </remarks>
        [Test]
        public void ABorderedPanel_KeepsTheShapeAndTheSlice()
        {
            Sprite filled = ProceduralSprites.Panel();
            Sprite bordered = ProceduralSprites.Panel(PanelStyle.Bordered);

            Assert.AreEqual(filled.border, bordered.border, "the nine-slice border moved");
            Assert.AreEqual(filled.pixelsPerUnit, bordered.pixelsPerUnit, "the corners would draw at another size");

            Color32[] a = filled.texture.GetPixels32();
            Color32[] b = bordered.texture.GetPixels32();

            Assert.AreEqual(a.Length, b.Length);

            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i].a, b[i].a, $"texel {i} is covered differently from the filled panel");
        }

        /// <summary>A bordered panel's edge is drawn brighter than its body.</summary>
        [Test]
        public void ABorderedPanel_HasABrightEdgeRoundADarkBody()
        {
            Sprite sprite = ProceduralSprites.Panel(PanelStyle.Bordered);
            int size = sprite.texture.width;
            Color32[] pixels = sprite.texture.GetPixels32();
            int row = size / 2;

            // The first texel along the middle row that the panel fully covers is its edge.
            int edge = 0;
            while (pixels[row * size + edge].a < 255)
                edge++;

            byte rim = pixels[row * size + edge].r;
            byte body = pixels[row * size + size / 2].r;

            Assert.Greater(rim, 200, "the edge should be drawn at close to the tint's full brightness");
            Assert.Less(body, 80, "the body should be a dark fraction of the tint");
        }
    }
}
