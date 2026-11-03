using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.EditMode.Tests
{
    /// <summary>
    /// <see cref="UiTheme"/>'s full-screen backdrops: the scrim and the panel sprite the workbench's pages are drawn on.
    /// </summary>
    /// <remarks>
    /// This held the 4.x game's HUD apart too -- its rows, its corners, its cards and the level list -- until Part 16a
    /// deleted the game.
    /// </remarks>
    public class UiThemeTests
    {
        // -----------------------------------------------------------------
        // Full-screen backdrops
        // -----------------------------------------------------------------

        /// <summary>
        /// A full-screen backdrop is a flat rectangle from edge to edge.
        /// </summary>
        /// <remarks>
        /// The panels used the rounded panel sprite for this, and its soft edges left the edges of
        /// the screen undimmed -- exactly where the HUD sits -- so a "full-screen" panel had the
        /// banner, run buttons and parts list lit up around it.
        /// </remarks>
        [Test]
        public void AScrim_CoversTheWholeScreenWithAFlatColour()
        {
            var parent = new GameObject("canvas", typeof(RectTransform));

            try
            {
                var colour = new Color(0f, 0f, 0f, 0.8f);
                UnityEngine.UI.Image scrim = UiTheme.Scrim("scrim", parent.transform, colour);
                var rect = scrim.rectTransform;

                Assert.IsNull(scrim.sprite, "a sprite with soft edges leaves the screen's edges undimmed");
                Assert.AreEqual(colour, scrim.color);
                Assert.AreEqual(Vector2.zero, rect.anchorMin);
                Assert.AreEqual(Vector2.one, rect.anchorMax);
                Assert.AreEqual(Vector2.zero, rect.offsetMin);
                Assert.AreEqual(Vector2.zero, rect.offsetMax);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// A panel's backdrop is nine-sliced, so its corners stay corner-sized however large it is.
        /// </summary>
        /// <remarks>
        /// Every sprite in <see cref="ProceduralSprites"/> is created without a nine-slice border,
        /// and <c>Image.Type.Sliced</c> with a zero border degenerates to a single stretched quad --
        /// so a panel drew the rounded silhouette scaled to its whole rect, solid in the middle and
        /// faded to nothing by the rim. Seen in the browser build: the help panel's title and its
        /// hint sat on bare board, with a sink's glow reading straight through them.
        ///
        /// It is the same defect the scrim above works around. That one covers the full-screen case
        /// by refusing the sprite altogether; this covers every other panel by making the sprite
        /// behave, which is the fix the workaround was standing in for.
        /// </remarks>
        [Test]
        public void APanelBackdrop_IsNineSlicedSoItsCornersStayCornerSized()
        {
            var parent = new GameObject("canvas", typeof(RectTransform));

            try
            {
                UnityEngine.UI.Image panel = UiTheme.Panel_("panel", parent.transform, UiTheme.Panel);

                Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, panel.type, "sanity: panels are sliced");
                Assert.IsNotNull(panel.sprite, "sanity: a panel has a backdrop sprite");

                Vector4 border = panel.sprite.border;
                float thinnest = Mathf.Min(Mathf.Min(border.x, border.y), Mathf.Min(border.z, border.w));

                Assert.Greater(thinnest, 0f,
                    "a zero border makes Image.Type.Sliced stretch the whole sprite instead");

                Rect rect = panel.sprite.rect;

                Assert.Less(border.x + border.z, rect.width, "no middle column left to stretch");
                Assert.Less(border.y + border.w, rect.height, "no middle row left to stretch");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The slices that get stretched along a panel's edges are solid.
        /// </summary>
        /// <remarks>
        /// The visible half of the test above. A nine-slice repeats the middle of each edge along
        /// that edge, so if the silhouette does not reach the edge of its own texture -- the
        /// squircle stops 14% short of it -- every panel is trimmed by a transparent margin and the
        /// stretched edge carries that margin with it.
        /// </remarks>
        [Test]
        public void APanelBackdrop_IsOpaqueWhereItIsStretched()
        {
            var parent = new GameObject("canvas", typeof(RectTransform));

            try
            {
                UnityEngine.UI.Image panel = UiTheme.Panel_("panel", parent.transform, UiTheme.Panel);
                Texture2D texture = panel.sprite.texture;

                int x = texture.width / 2;
                int y = texture.height / 2;

                Assert.AreEqual(1f, texture.GetPixel(x, y).a, 0.01f, "the middle of a panel");
                Assert.AreEqual(1f, texture.GetPixel(x, texture.height - 1).a, 0.01f, "its top edge");
                Assert.AreEqual(1f, texture.GetPixel(x, 0).a, 0.01f, "its bottom edge");
                Assert.AreEqual(1f, texture.GetPixel(0, y).a, 0.01f, "its left edge");
                Assert.AreEqual(1f, texture.GetPixel(texture.width - 1, y).a, 0.01f, "its right edge");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
