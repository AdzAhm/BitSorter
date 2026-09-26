using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="UiTheme"/>'s shared layout arithmetic: the right-hand column and the two bottom
    /// corners. The rows along the top, the bottom and down the right are <see cref="UiRows"/>, and
    /// <see cref="UiStackTests"/> holds them apart.
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

        /// <summary>The right-hand bottom corner, where diagnostics sits.</summary>
        private static readonly Vector2 RightCorner = new Vector2(1f, 0f);

        /// <summary>
        /// On free play the help panel and the setup panel are two rectangles on one column, and
        /// they have to take different halves of it.
        /// </summary>
        /// <remarks>
        /// Both dock to the top right. Free play is the one level with no expectations, so the help
        /// panel shrinks to its hint and sits squarely over the setup panel's title and its
        /// collapse button -- and because a panel backdrop is never a raycast target, the clicks
        /// land on the steppers underneath the panel the player can see.
        ///
        /// This is the failure the rows above are here to prevent, arrived at horizontally: the
        /// help panel stated its own inset instead of taking one from here.
        /// </remarks>
        [Test]
        public void OnFreePlay_TheHelpPanelAndTheSetupPanel_DoNotShareTheSameColumn()
        {
            // Both measured as insets from the right edge, so a panel's left edge is its inset
            // plus its width.
            float setupLeft = UiTheme.Margin + UiTheme.SetupWidth;
            float helpRight = UiTheme.HelpRight(besideSetupPanel: true);

            Assert.GreaterOrEqual(helpRight, setupLeft,
                "the help panel is drawn on top of free play's setup panel");
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
            float pair = 2f * (UiTheme.CornerWidth + UiTheme.Margin);

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

                UiTheme.AnchorBottomCorner(rect, RightCorner, 100f);

                Assert.AreEqual(RightCorner, rect.anchorMin);
                Assert.AreEqual(RightCorner, rect.anchorMax);
                Assert.AreEqual(UiTheme.CornerWidth, rect.sizeDelta.x);
                Assert.AreEqual(100f, rect.sizeDelta.y);

                Assert.Less(rect.anchoredPosition.x, 0f,
                    "a right-hand panel has to move left off its own edge to stay on screen");
                Assert.AreEqual(UiTheme.Margin, rect.anchoredPosition.y);

                // The left corner, stated rather than borrowed from whichever readout happens to
                // be on that side: the readouts have swapped corners once already, and this is a
                // test of the anchoring, not of where they currently sit.
                UiTheme.AnchorBottomCorner(rect, new Vector2(0f, 0f), 96f);

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


        /// <summary>
        /// The clock strip is as wide as the beat it has to show.
        /// </summary>
        /// <remarks>
        /// Its width was fixed at the widest period the game uses, which nothing noticed while
        /// every panel faded out towards its edges -- the empty end of the strip faded with it.
        /// Once panels got real edges, a two-tick clock sat in a bar half of which was empty.
        /// </remarks>
        [Test]
        public void TheClockStrip_IsAsWideAsItsBeat()
        {
            float two = ClockReadout.WidthFor(2);
            float three = ClockReadout.WidthFor(3);
            float four = ClockReadout.WidthFor(4);

            Assert.Less(two, three, "a longer period needs a longer strip");
            Assert.AreEqual(three - two, four - three, 0.0001f, "each pip costs the same");

            Assert.Greater(ClockReadout.WidthFor(1), 0f);
            Assert.Less(ClockReadout.WidthFor(0), ClockReadout.WidthFor(1),
                "no beat at all is the narrowest the strip gets");
        }


        /// <summary>
        /// Every shipped level's goal fits the room the banner gives it.
        /// </summary>
        /// <remarks>
        /// The goal wraps, and its label is centred in a box with no room to wrap into -- so a goal
        /// that needs three lines overflows the box in both directions, and the top of it prints
        /// straight through the title. Seen on Flip on one in a browser build: "Keep a bit and flip
        /// it every time a 1 arrives" drawn across "FLIP ON ONE 12 / 17".
        ///
        /// Measured with the real label rather than counted in characters, because what matters is
        /// where TextMeshPro actually breaks the lines. <see cref="UiTheme.GoalHeight"/> is the
        /// same measurement the banner sizes itself from, so the two cannot disagree.
        /// </remarks>
        [Test]
        public void EveryLevelsGoal_FitsTheBannerWithoutClimbingIntoTheTitle()
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, asset.name);

                float needed = UiTheme.GoalHeight(parsed.Level.Goal);

                Assert.LessOrEqual(needed, UiTheme.BannerGoalHeight,
                    $"{asset.name}'s goal wraps to {needed:F0}px and the banner gives the goal " +
                    $"{UiTheme.BannerGoalHeight}px, so it prints over the title");
            }
        }


        /// <summary>
        /// The solved card has room for every level's name and for the most a circuit's cost can
        /// say.
        /// </summary>
        /// <remarks>
        /// A strip, so the room is tight by design, and measured with the real label for the reason
        /// the goal is: where TextMeshProUGUI breaks the lines is what decides whether the last one
        /// prints below the card. The worst case is the longest breakdown the parts lists allow,
        /// a delay budget, a record, and a line about beating it.
        /// </remarks>
        [Test]
        public void TheSolvedCard_HoldsEveryNameAndTheLongestCost()
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, asset.name);

                float needed = UiTheme.TextHeight(parsed.Level.Name, WinPanel.DetailType, WinPanel.NameWidth);
                Assert.LessOrEqual(needed, WinPanel.NameHeight,
                    $"{asset.name}'s name needs {needed:F0}px under SOLVED and has {WinPanel.NameHeight}px");
            }

            const string worst =
                "12 gates  -  4 XOR, 3 NAND, 3 NOT, 2 NOR\n" +
                "12 of 12 delay spent\n" +
                "best so far   12 gates   24 ticks\n" +
                "smaller and faster than last time";

            float cost = UiTheme.TextHeight(worst, WinPanel.DetailType, WinPanel.DetailWidth);
            Assert.LessOrEqual(cost, WinPanel.DetailHeight,
                $"the longest cost needs {cost:F0}px and the card has {WinPanel.DetailHeight}px");
        }

        /// <summary>
        /// The controls line fits its box on one line, and the box fits the narrowest screen.
        /// </summary>
        /// <remarks>
        /// The line never wraps, so running long means running out of its box sideways, and nothing
        /// measured it: the box was a thousand wide and the text had grown to within forty pixels of
        /// that before M was added to it. The canvas is widest at the reference 16:9 and narrowest
        /// at 4:3, where the scaler (matching width and height equally) makes it
        /// sqrt(1920 x 1080 x 4/3) wide.
        /// </remarks>
        [Test]
        public void TheControlsLine_FitsItsBox_AndTheBoxFitsANarrowScreen()
        {
            float needed = UiTheme.TextWidth(ControlsReference.Line, UiTheme.ControlsType);
            Assert.LessOrEqual(needed, UiTheme.ControlsWidth,
                $"the controls line needs {needed:F0}px and its box is {UiTheme.ControlsWidth}px");

            float narrowest = Mathf.Sqrt(1920f * 1080f * 4f / 3f);
            Assert.LessOrEqual(UiTheme.ControlsWidth + 2f * UiTheme.Margin, narrowest,
                $"the controls line's box is wider than a 4:3 screen ({narrowest:F0}px)");
        }

        /// <summary>
        /// A full-screen card's paragraph fits the box it is set in.
        /// </summary>
        /// <remarks>
        /// The cards' paragraphs went up a step, to <see cref="UiType.Lead"/>, after a playtest said
        /// the chapter card read as small print. A paragraph that outgrows its box does not clip: it
        /// prints over the title above it or the button below.
        /// </remarks>
        [Test]
        public void EveryCardsParagraph_FitsItsBox()
        {
            float chapter = UiTheme.TextHeight(ChapterCard.Body, ChapterCard.BodyType, ChapterCard.BodyWidth);
            Assert.LessOrEqual(chapter, ChapterCard.BodyHeight,
                $"the chapter card's paragraph needs {chapter:F0}px and has {ChapterCard.BodyHeight}px");

            float tutorial = UiTheme.TextHeight(TutorialCard.Body, TutorialCard.BodyType, TutorialCard.BodyWidth);
            Assert.LessOrEqual(tutorial, TutorialCard.BodyHeight,
                $"the tutorial card's paragraph needs {tutorial:F0}px and has {TutorialCard.BodyHeight}px");
        }

        /// <summary>
        /// At the reference resolution the whole run shows in the level list at once, with no
        /// scrolling.
        /// </summary>
        /// <remarks>
        /// A list too tall for its room scrolls now; before it had a scroll view it grew out of both
        /// ends and printed over the LEVELS title, which is what the two chapter headings did the
        /// first time they were added. Scrolling makes that safe, not good: the reference shape is
        /// the one most players first meet the list at, and there the run should be one picture --
        /// both chapters, and where the player is in them.
        /// </remarks>
        [Test]
        public void TheWholeRun_ShowsWithoutScrollingAtTheReferenceResolution()
        {
            int levels = 0;

            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                if (asset != null)
                    levels++;
            }

            Assert.Greater(levels, 0, "sanity: there are levels to list");

            float room = LevelSelectPanel.ListRoom(UiTheme.ReferenceResolution.y);
            float needed = LevelSelectPanel.ListHeight(levels, 2);

            Assert.LessOrEqual(needed, room,
                $"{levels} levels in two chapters need {needed:F0}px and the list has {room:F0}px " +
                "between its title and its help line, so at the reference resolution it scrolls");
        }


        /// <summary>
        /// The clock diagram's wave is high on the tick a vector arrives and low for the rest.
        /// </summary>
        /// <remarks>
        /// The same fact the clock readout lights its first pip on, which is why both ask what the
        /// tick is within the cycle rather than each deciding what a cycle is. A period of one is
        /// high throughout -- a square wave that never falls -- which is why the diagram is not
        /// drawn on a level without a clock.
        /// </remarks>
        [Test]
        public void TheClockWave_IsHighOnTheTickAVectorArrives()
        {
            // Tick 0 is before the run starts, and tick 1 is the first tick of the first cycle.
            Assert.IsTrue(ClockDiagram.IsHigh(0, 3), "the run has not started");
            Assert.IsTrue(ClockDiagram.IsHigh(1, 3), "the first vector arrives");
            Assert.IsFalse(ClockDiagram.IsHigh(2, 3));
            Assert.IsFalse(ClockDiagram.IsHigh(3, 3));
            Assert.IsTrue(ClockDiagram.IsHigh(4, 3), "the second vector, a period later");

            for (int tick = 0; tick < 8; tick++)
            {
                Assert.IsTrue(ClockDiagram.IsHigh(tick, 1),
                    "a vector every tick is a wave that never falls");
            }
        }

        /// <summary>
        /// The two F2 readouts are in opposite bottom corners.
        /// </summary>
        /// <remarks>
        /// They come up together on the same key, and both are anchored with
        /// <see cref="UiTheme.AnchorBottomCorner"/> at the same width -- so sharing a corner would
        /// put them in exactly the same rectangle, which is what the catch readout and diagnostics
        /// already did once.
        /// </remarks>
        [Test]
        public void TheTwoF2Readouts_TakeDifferentCorners()
        {
            Assert.AreNotEqual(UiTheme.DiagnosticsCorner, UiTheme.ClockDiagramCorner);

            Assert.AreEqual(0f, UiTheme.DiagnosticsCorner.y, "both sit on the bottom edge");
            Assert.AreEqual(0f, UiTheme.ClockDiagramCorner.y, "both sit on the bottom edge");
        }

    }
}
