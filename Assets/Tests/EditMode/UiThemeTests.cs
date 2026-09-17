using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="UiTheme"/>'s shared layout arithmetic: the rows along the bottom of the board,
    /// the stack under the banner, and the two bottom corners.
    /// </summary>
    /// <remarks>
    /// These are the numbers that decide whether two panels draw on top of each other, and the
    /// project has shipped that bug three times -- the refusal toast over the controls line, the
    /// verdict over the first-time hint, and the diagnostics panel over free play's catch readout.
    /// Each time the fix was to move the arithmetic here. Nothing pinned it, so nothing stopped the
    /// next pair.
    ///
    /// Pure constants, so this needs no canvas. It cannot prove a panel is drawn where its rect
    /// says -- only that the rects the panels are built from do not collide.
    /// </remarks>
    public class UiThemeTests
    {
        // -----------------------------------------------------------------
        // The two bottom corners
        // -----------------------------------------------------------------

        /// <summary>
        /// Free play's catch readout and the diagnostics panel cannot overlap.
        /// </summary>
        /// <remarks>
        /// They did, exactly: both anchored bottom-right at (-16, 16) with a width of 230, each
        /// stating its own numbers, so in free play with F3 open they occupied the identical
        /// rectangle. Opposite corners plus a width under half the canvas is what makes that
        /// unrepresentable at any window shape.
        /// </remarks>
        [Test]
        public void TheTwoBottomCornerReadouts_AreOnOppositeSides()
        {
            Assert.AreNotEqual(
                UiTheme.ReadoutCorner.x, UiTheme.DiagnosticsCorner.x,
                "both bottom-corner readouts anchor to the same side, so they will draw over each " +
                "other -- free play with diagnostics open is where this shows");

            Assert.AreEqual(0f, UiTheme.ReadoutCorner.y, "both sit on the bottom edge");
            Assert.AreEqual(0f, UiTheme.DiagnosticsCorner.y, "both sit on the bottom edge");
        }

        /// <summary>
        /// Opposite corners is only enough while each panel is narrower than half the screen.
        /// </summary>
        /// <remarks>
        /// The canvas scaler matches width or height at 0.5, so how many canvas units wide the
        /// screen is depends on the window's shape and is not always the reference width. Holding
        /// each readout to well under half of it leaves room for that to move without the two
        /// meeting in the middle.
        /// </remarks>
        [Test]
        public void ABottomCornerReadout_IsFarNarrowerThanHalfTheCanvas()
        {
            float pair = 2f * (UiTheme.ReadoutWidth + UiTheme.Margin);

            Assert.Less(pair, UiTheme.ReferenceResolution.x * 0.6f,
                $"two readouts plus their margins come to {pair} canvas units against a reference " +
                $"width of {UiTheme.ReferenceResolution.x}, which leaves too little room for the " +
                "canvas to be narrower than reference at a tall window shape");
        }

        [Test]
        public void AnchoringIntoACorner_InsetsAwayFromTheEdgeItIsOn()
        {
            var host = new GameObject("probe", typeof(RectTransform));

            try
            {
                var rect = host.GetComponent<RectTransform>();

                UiTheme.AnchorBottomCorner(rect, UiTheme.ReadoutCorner, 100f);

                Assert.AreEqual(UiTheme.ReadoutCorner, rect.anchorMin);
                Assert.AreEqual(UiTheme.ReadoutCorner, rect.anchorMax);
                Assert.AreEqual(UiTheme.ReadoutWidth, rect.sizeDelta.x);
                Assert.AreEqual(100f, rect.sizeDelta.y);

                Assert.Less(rect.anchoredPosition.x, 0f,
                    "a right-hand panel has to move left off its own edge to stay on screen");
                Assert.AreEqual(UiTheme.Margin, rect.anchoredPosition.y);

                UiTheme.AnchorBottomCorner(rect, UiTheme.DiagnosticsCorner, 96f);

                Assert.Greater(rect.anchoredPosition.x, 0f,
                    "a left-hand panel has to move right off its own edge");
                Assert.AreEqual(UiTheme.Margin, rect.anchoredPosition.y);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        // -----------------------------------------------------------------
        // The rows along the bottom
        // -----------------------------------------------------------------

        /// <summary>
        /// Each row along the bottom clears the one below it.
        /// </summary>
        /// <remarks>
        /// The toast used to draw over the controls line, at the moment the player most needed to
        /// read both. The rows have been defined in terms of each other ever since; this is the
        /// assertion that says so.
        /// </remarks>
        [Test]
        public void TheBottomRows_DoNotOverlap()
        {
            Assert.GreaterOrEqual(UiTheme.ControlsRow, UiTheme.ButtonRow + UiTheme.ButtonHeight,
                "the controls line overlaps the button row");

            Assert.GreaterOrEqual(UiTheme.ToastRow, UiTheme.ControlsRow + UiTheme.ControlsHeight,
                "the refusal toast overlaps the controls line");
        }

        /// <summary>
        /// The stack under the banner clears the verdict line that hangs below it.
        /// </summary>
        /// <remarks>
        /// The verdict is anchored outside the banner, so every row below has to allow for it.
        /// Nothing did: the first-time hint began twenty-four units inside the verdict, and both
        /// are on screen at once whenever a run settles with a gate still stalled.
        /// </remarks>
        [Test]
        public void TheTopStack_DoesNotOverlap()
        {
            Assert.GreaterOrEqual(
                UiTheme.HintRow, UiTheme.Margin + UiTheme.BannerHeight + UiTheme.VerdictHeight,
                "the first-time hint overlaps the verdict line hanging below the banner");

            Assert.GreaterOrEqual(UiTheme.TutorialRow, UiTheme.HintRow + UiTheme.HintHeight,
                "the tutorial's instruction strip overlaps the first-time hint");
        }

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
    }
}
