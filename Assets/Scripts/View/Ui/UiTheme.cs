using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Colours, sizes and the small builders every panel uses, so the interface reads as one thing.
    /// </summary>
    /// <remarks>
    /// The interface is assembled in code rather than authored in the scene, matching how the rest
    /// of the view works -- <see cref="PlacementGrid"/> builds its dots, <see cref="BoardBackground"/>
    /// builds itself, and <see cref="ProceduralSprites"/> draws every sprite at runtime. The scene is
    /// generated too, so an authored hierarchy would be dozens of RectTransforms for the scene builder
    /// to reproduce by hand and get subtly wrong.
    ///
    /// Colours come from <see cref="Palette"/>, which the board draws from too, so the interface
    /// cannot drift away from the board it sits on.
    /// </remarks>
    public static class UiTheme
    {
        public static Color Panel => Palette.Current.Panel;
        public static Color PanelEdge => Palette.Current.PanelEdge;
        public static Color Text => Palette.Current.Text;
        public static Color TextDim => Palette.Current.TextDim;
        public static Color Accent => Palette.Current.Accent;
        public static Color Good => Palette.Current.Good;
        public static Color Bad => Palette.Current.Bad;

        public const float Margin = 16f;
        public const float Gap = 8f;
        public const float ButtonHeight = 44f;
        public const float PaletteButton = 64f;

        /// <summary>
        /// The canvas size every offset in this file is expressed against.
        /// </summary>
        /// <remarks>
        /// The scene builder hands this to the CanvasScaler, rather than stating 1920x1080 of its
        /// own, so "canvas units" means one thing in both places.
        /// </remarks>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        // -----------------------------------------------------------------
        // The right column
        // -----------------------------------------------------------------

        /// <summary>The bits-lost meter's height. Where it sits is <see cref="UiRows.BitsLost"/>.</summary>
        public const float BitsLostHeight = 46f;

        /// <summary>The help badge's size. Where it sits is <see cref="UiRows.Badge"/>.</summary>
        public const float BadgeSize = 38f;

        /// <summary>The key hint hanging under the badge. Where it sits is <see cref="UiRows.BadgeKey"/>.</summary>
        public const float BadgeKeyHeight = 20f;

        /// <summary>
        /// Free play's setup panel's width. It runs from <see cref="UiRows.Panels"/> down to
        /// <see cref="UiRows.PanelFloor"/>.
        /// </summary>
        /// <remarks>
        /// Wide enough for eight bit cells beside a sink's name, which is what makes a caught bit
        /// line up under the vector that produced it.
        /// </remarks>
        public const float SetupWidth = 300f;

        /// <summary>
        /// The help panel's narrowest. It starts on <see cref="UiRows.Panels"/>, beside the setup
        /// panel on free play.
        /// </summary>
        public const float HelpMinimumWidth = 330f;

        /// <summary>
        /// How far in from the right edge the help panel sits, given whether free play's setup
        /// panel is docked beside it.
        /// </summary>
        /// <remarks>
        /// They share a row, so they have to take different halves of the column: the help panel
        /// steps left of the setup panel's full width rather than over it. It steps aside whether
        /// or not the setup panel is collapsed at that moment, because a help panel that moved
        /// when the setup panel was folded away would be a panel that is never twice in the same
        /// place.
        /// </remarks>
        public static float HelpRight(bool besideSetupPanel) =>
            besideSetupPanel ? Margin + SetupWidth + Gap : Margin;

        // -----------------------------------------------------------------
        // The bottom corners
        // -----------------------------------------------------------------

        /// <summary>
        /// Width of a bottom-corner readout.
        /// </summary>
        /// <remarks>
        /// One corner is in use: diagnostics, on the left. Free play's catch readout held the right
        /// one until it moved into the setup panel, where it sits under the streams that produced it
        /// -- and the pair is what this constant is for. The two once anchored bottom-right at the
        /// same offset with the same width, each stating its own numbers, so in free play with F3
        /// open they occupied exactly the same rectangle.
        ///
        /// Narrow enough that a second corner readout could return without colliding with this one,
        /// which <see cref="UiThemeTests"/> keeps true.
        /// </remarks>
        public const float CornerWidth = 230f;

        /// <summary>
        /// Diagnostics takes the right corner, and the clock diagram the left.
        /// </summary>
        /// <remarks>
        /// Both are behind F3 and both are bottom-corner readouts, so they are stated together:
        /// they are the pair this constant's width exists to keep apart. The right one is clear at
        /// this height -- free play's setup panel stops 148 from the bottom and a corner readout is
        /// 96 tall on the margin -- and the palette is centred on the left edge and stops well
        /// above it, so neither corner is contended.
        ///
        /// Diagnostics was on the left. The clock diagram is a picture of the level's beat and
        /// belongs beside the board's own left edge, where the sources are.
        /// </remarks>
        public static readonly Vector2 DiagnosticsCorner = new Vector2(1f, 0f);

        /// <inheritdoc cref="DiagnosticsCorner"/>
        public static readonly Vector2 ClockDiagramCorner = new Vector2(0f, 0f);

        /// <summary>
        /// Puts a readout in one of the two bottom corners, at the shared width.
        /// </summary>
        /// <remarks>
        /// Anchored to its own corner rather than positioned absolutely, because the canvas scaler
        /// matches width or height at 0.5 -- so how many canvas units wide the screen is depends on
        /// the window's shape, and only an anchor keeps a panel on the edge it belongs to.
        /// </remarks>
        public static void AnchorBottomCorner(RectTransform rect, Vector2 corner, float height)
        {
            float inset = corner.x > 0.5f ? -Margin : Margin;

            Anchor(rect, corner, corner, new Vector2(inset, Margin),
                new Vector2(CornerWidth, height));
        }

        // -----------------------------------------------------------------
        // The rows' heights. Where each row sits is UiRows.
        // -----------------------------------------------------------------

        /// <summary>The keyboard reference along the bottom.</summary>
        public const float ControlsHeight = 26f;

        /// <summary>The refusal toast.</summary>
        public const float ToastHeight = 38f;

        /// <summary>The status banner's width; the rows under it share it.</summary>
        public const float BannerWidth = 780f;

        /// <summary>Width the title and the goal wrap inside, within the banner's own width.</summary>
        public const float BannerTextWidth = 760f;

        /// <summary>The title's row: its inset from the top, plus its own height.</summary>
        public const float BannerTitleBlock = 40f;

        /// <summary>
        /// Room under the title for the goal: three wrapped lines, which is the longest any level
        /// has.
        /// </summary>
        /// <remarks>
        /// Reserved, not always drawn. The banner shrinks to whatever its goal actually needs, so a
        /// one-line goal is not sitting in a box with forty empty pixels under it -- but every row
        /// below the banner is placed from the full figure, so a long goal can never push one of
        /// them off its row. <see cref="UiThemeTests"/> refuses a goal that would not fit.
        /// </remarks>
        public const float BannerGoalHeight = 66f;

        /// <summary>Size the goal is set at, shared with whatever measures it.</summary>
        public const float BannerGoalFontSize = 19f;

        /// <summary>Breathing room under the goal.</summary>
        public const float BannerPad = 12f;

        /// <summary>
        /// Tall enough for the title and the goal, and nothing else.
        /// </summary>
        /// <remarks>
        /// <inheritdoc cref="BannerWidth"/>
        ///
        /// Was 122 while the banner also carried the level's hint. Dropping that line freed thirty
        /// pixels, and because every row below is measured from here, the first-time hint and the
        /// tutorial's instruction strip both moved up with it rather than leaving a hole.
        ///
        /// Added up from the three rows above rather than stated, so the goal getting more room
        /// moves everything below the banner instead of running off the bottom of it.
        /// </remarks>
        public const float BannerHeight = BannerTitleBlock + BannerGoalHeight + BannerPad;

        /// <summary>
        /// How far below the drawn banner the verdict hangs, and how tall its line is.
        /// </summary>
        /// <remarks>
        /// Shared between `StatusBanner`, which hangs the verdict off the banner it draws, and
        /// <see cref="UiRows.Verdict"/>, which reserves the room it can reach.
        /// </remarks>
        public const float VerdictGap = 6f;

        /// <inheritdoc cref="VerdictGap"/>
        public const float VerdictLineHeight = 26f;

        /// <summary>The clock strip, its own gap below it included.</summary>
        public const float ClockHeight = 22f + Gap;

        /// <summary>A first-time hint.</summary>
        public const float HintHeight = 46f;

        /// <summary>The tutorial's instruction strip.</summary>
        public const float TutorialHeight = 52f;

        /// <summary>
        /// How tall a goal wraps to in the banner, measured with the label that will draw it.
        /// </summary>
        /// <remarks>
        /// Here rather than inside <see cref="StatusBanner"/> because two things need the answer:
        /// the banner, to size itself to what it is showing, and a test, to refuse a goal that
        /// would not fit. Each measuring it for itself is the second copy that drifts -- and the
        /// test assembly does not reference TextMeshPro, which is the other reason the measuring
        /// belongs on this side of the line.
        ///
        /// The ruler is built once and kept. It is marked not-to-be-saved for the reason the
        /// generated sprites are, and re-made if something destroys it anyway.
        /// </remarks>
        public static float GoalHeight(string goal) =>
            TextHeight(goal, BannerGoalFontSize, BannerTextWidth);

        /// <summary>
        /// The height a wrapped run of text needs, measured with a real label rather than guessed.
        /// </summary>
        /// <remarks>
        /// One ruler, reused and reconfigured per call. It is HideAndDontSave, so it is neither
        /// saved into a scene nor swept up as an untracked object -- the same flag every generated
        /// sprite carries, for the same reason.
        ///
        /// Guessing is what this replaces. A box sized by counting the lines somebody expected is
        /// a box that fits until a level is written with one more, and nothing tells you which
        /// level did it: the text simply prints past the panel.
        /// </remarks>
        public static float TextHeight(string text, float fontSize, float width, float lineSpacing = 0f)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            if (_ruler == null)
            {
                var host = new GameObject("UiTheme ruler", typeof(RectTransform))
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };

                _ruler = Label("ruler", host.transform, fontSize, Accent, TextAlignmentOptions.Top);
                _ruler.textWrappingMode = TextWrappingModes.Normal;
            }

            _ruler.fontSize = fontSize;
            _ruler.lineSpacing = lineSpacing;

            return _ruler.GetPreferredValues(text, width, 0f).y;
        }

        private static TextMeshProUGUI _ruler;

        /// <summary>A stretched child RectTransform, ready to be anchored by the caller.</summary>
        public static RectTransform Rect(string name, Transform parent)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            return host.GetComponent<RectTransform>();
        }

        /// <summary>
        /// The backdrop behind a panel that takes the whole screen: a plain rectangle, edge to edge.
        /// </summary>
        /// <remarks>
        /// Not <see cref="Panel_"/>. Its rounded, sliced sprite fades out towards the rect's edges,
        /// so stretched over the screen it left the screen's edges -- which is where the HUD sits --
        /// undimmed beside a supposedly full-screen panel. With no sprite, Unity draws a flat colour.
        /// </remarks>
        public static Image Scrim(string name, Transform parent, Color colour)
        {
            RectTransform rect = Rect(name, parent);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour;

            return image;
        }

        /// <summary>Shows or hides a piece of interface, touching it only when that changes.</summary>
        public static void SetShown(Component part, bool shown)
        {
            if (part != null && part.gameObject.activeSelf != shown)
                part.gameObject.SetActive(shown);
        }

        /// <summary>A filled panel background using the shared rounded silhouette.</summary>
        /// <remarks>
        /// <see cref="ProceduralSprites.Panel"/> rather than the AND gate's squircle, which this
        /// borrowed and could not nine-slice: with no border the whole sprite was stretched across
        /// the rect, so a panel was solid in the middle and faded out by its edges.
        ///
        /// Anything shorter than two corners -- the clock readout's pips -- has to replace the
        /// sprite, the way the help badge already replaces it with a circle.
        /// </remarks>
        public static Image Panel_(string name, Transform parent, Color colour)
        {
            RectTransform rect = Rect(name, parent);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ProceduralSprites.Panel(Look.Current.Panels);
            image.type = Image.Type.Sliced;
            image.color = colour;

            return image;
        }

        /// <summary>
        /// A text element. Wrapping is off by default because every label here is one short line and
        /// a wrapped label silently changes a panel's height.
        /// </summary>
        public static TextMeshProUGUI Label(
            string name, Transform parent, float size, Color colour,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            RectTransform rect = Rect(name, parent);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = colour;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;   // labels must never eat a click meant for the board

            return text;
        }

        /// <summary>
        /// A button with a label. The click handler is the caller's to attach.
        /// </summary>
        /// <remarks>
        /// Navigation is switched off deliberately. A selected Button consumes Space and Enter, and
        /// this game binds both -- Space pauses and Enter runs -- so a button that kept focus after a
        /// click would swallow the very keys the player expects to work next.
        /// </remarks>
        public static Button Button_(string name, Transform parent, string caption, out TextMeshProUGUI label)
        {
            Image background = Panel_(name, parent, PanelEdge);

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            ColorBlock colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colours.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colours.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            button.colors = colours;

            label = Label(name + " label", background.transform, 18f, Text, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.text = caption;

            return button;
        }

        /// <summary>
        /// Puts a panel in front of its siblings.
        /// </summary>
        /// <remarks>
        /// A canvas draws children in sibling order, which is the order they were created -- so
        /// without this, which panel covers which is decided by the order components happen to run
        /// their Start, and the main menu opened *underneath* the level list. Whatever was opened
        /// most recently should be the thing in front.
        /// </remarks>
        public static void BringToFront(RectTransform rect)
        {
            if (rect != null)
                rect.SetAsLastSibling();
        }

        /// <summary>Makes a child fill its parent.</summary>
        public static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>Anchors a rect to one corner at a fixed size, in canvas units.</summary>
        public static void Anchor(RectTransform rect, Vector2 corner, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }
    }
}
