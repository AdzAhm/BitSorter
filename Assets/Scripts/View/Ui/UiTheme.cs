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
    /// Colours are taken from the board rather than invented: the panel background is the board's own
    /// base colour lifted slightly, and the accents are the node palette from
    /// <see cref="NodeShapes"/>. Anything else would look bolted on.
    /// </remarks>
    public static class UiTheme
    {
        public static readonly Color Panel = new Color(0.075f, 0.085f, 0.11f, 0.92f);
        public static readonly Color PanelEdge = new Color(0.16f, 0.22f, 0.26f, 1f);
        public static readonly Color Text = new Color(0.86f, 0.89f, 0.94f);
        public static readonly Color TextDim = new Color(0.55f, 0.60f, 0.68f);
        public static readonly Color Accent = new Color(0.46f, 0.94f, 0.90f);
        public static readonly Color Good = new Color(0.36f, 0.92f, 0.55f);
        public static readonly Color Bad = new Color(0.98f, 0.44f, 0.44f);

        public const float Margin = 16f;
        public const float Gap = 8f;
        public const float ButtonHeight = 44f;
        public const float PaletteButton = 64f;

        /// <summary>A stretched child RectTransform, ready to be anchored by the caller.</summary>
        public static RectTransform Rect(string name, Transform parent)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            return host.GetComponent<RectTransform>();
        }

        /// <summary>A filled panel background using the shared rounded silhouette.</summary>
        public static Image Panel_(string name, Transform parent, Color colour)
        {
            RectTransform rect = Rect(name, parent);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ProceduralSprites.RoundedSquare();
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
