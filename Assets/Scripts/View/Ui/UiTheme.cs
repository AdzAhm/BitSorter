using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Colours, sizes and the small builders the workbench's uGUI parts are made of, so the interface reads as one thing.
    /// </summary>
    /// <remarks>
    /// The interface is assembled in code rather than authored in the scene, matching how the rest
    /// of the view works -- <see cref="ProceduralSprites"/> draws every sprite at runtime. The scene is
    /// generated too, so an authored hierarchy would be dozens of RectTransforms for the scene builder
    /// to reproduce by hand and get subtly wrong.
    ///
    /// Colours come from <see cref="Palette"/>. The sizes of the 4.x game's HUD -- its rows, corners, cards, banner and
    /// toast -- went with the game, in Part 16b.
    /// </remarks>
    public static class UiTheme
    {
        public static Color Panel => Palette.Current.Panel;
        public static Color PanelEdge => Palette.Current.PanelEdge;
        public static Color Text => Palette.Current.Text;
        public static Color Accent => Palette.Current.Accent;

        public const float Margin = 16f;
        public const float ButtonHeight = 44f;

        /// <summary>
        /// The canvas size every offset in this file is expressed against.
        /// </summary>
        /// <remarks>
        /// The scene builder hands this to the CanvasScaler, rather than stating 1920x1080 of its
        /// own, so "canvas units" means one thing in both places.
        /// </remarks>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        // -----------------------------------------------------------------
        // Keys and the status line
        // -----------------------------------------------------------------

        /// <summary>A key's cap: how tall it is drawn.</summary>
        public const float ControlsHeight = 26f;

        /// <summary>A key over the button it presses, drawn as a key: its name in a small outlined cap.</summary>
        /// <remarks>
        /// The keys over the buttons were dim words -- "Enter", "shift+R" -- in the same style as
        /// the sentences beside them, so they read as more small print rather than as keys to press.
        /// A cap says "key" by its shape before it is read.
        /// </remarks>
        public const UiType KeyCapType = UiType.Body;

        /// <summary>Room either side of a key's name inside its cap.</summary>
        public const float KeyCapPad = 8f;

        /// <summary>How wide a key's cap is drawn.</summary>
        public static float KeyCapWidth(string key) => TextWidth(key, KeyCapType) + 2f * KeyCapPad;

        /// <summary>A key drawn as a key: its name in a cap, looking like a quiet button that takes no click.</summary>
        public static RectTransform KeyCap(string name, Transform parent, string key)
        {
            Image cap = Panel_(name, parent, FillOf(ButtonRole.Quiet));
            cap.raycastTarget = false;

            TextMeshProUGUI label = Label("key", cap.transform, KeyCapType, Text, TextAlignmentOptions.Center);
            label.raycastTarget = false;
            Stretch(label.rectTransform);
            label.text = key;

            RectTransform rect = cap.rectTransform;
            rect.sizeDelta = new Vector2(KeyCapWidth(key), ControlsHeight);
            rect.gameObject.AddComponent<KeyCapView>();   // SHORTCUT KEYS hides it (KeyCaps)
            return rect;
        }

        /// <summary>
        /// The type the workbench's status line is set in.
        /// </summary>
        /// <remarks>
        /// Caption, then Label after a playtest found it hard to read, and Body in the text colour
        /// since a second playtest (2026-09-27) asked for it bigger and clearer again: at Label in the
        /// dim colour, on a screen scaled down to a browser tab, it was the hardest thing on the
        /// screen to read.
        /// </remarks>
        public const UiType HelpLineType = UiType.Body;

        /// <summary>
        /// The width a run of text needs on one line, measured with a real label rather than guessed. For the labels that never
        /// wrap, where running long means running out of the box sideways.
        /// </summary>
        /// <remarks>
        /// One ruler, reused and reconfigured per call. It is HideAndDontSave, so it is neither
        /// saved into a scene nor swept up as an untracked object -- the same flag every generated
        /// sprite carries, for the same reason.
        ///
        /// Guessing is what this replaces. A box sized by counting the characters somebody expected is
        /// a box that fits until a name is written with one more, and nothing tells you which
        /// one did it: the text simply prints past the panel.
        /// </remarks>
        public static float TextWidth(string text, UiType type) =>
            string.IsNullOrEmpty(text) ? 0f : Ruler(type).GetPreferredValues(text, float.PositiveInfinity, 0f).x;

        /// <summary>
        /// <paramref name="text"/> as it fits <paramref name="width"/> on one line in <paramref name="type"/>: whole, or cut
        /// short with "..." -- for a name in a box that must not clip what stands beside it, as an ellipsis style clips the box's
        /// own children (Part 15a's circuit names).
        /// </summary>
        public static string Shortened(string text, float width, UiType type)
        {
            if (string.IsNullOrEmpty(text) || TextWidth(text, type) <= width)
                return text ?? "";
            for (int length = text.Length - 1; length > 1; length--)
            {
                string shortened = text.Substring(0, length).TrimEnd() + "...";
                if (TextWidth(shortened, type) <= width)
                    return shortened;
            }
            return text.Substring(0, 1) + "...";
        }

        private static TextMeshProUGUI Ruler(UiType type)
        {
            if (_ruler == null)
            {
                var host = new GameObject("UiTheme ruler", typeof(RectTransform))
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };

                _ruler = Label("ruler", host.transform, type, Accent, TextAlignmentOptions.Top);
                _ruler.textWrappingMode = TextWrappingModes.Normal;
            }

            // The font every label is drawn in, asked for each time rather than kept from when the
            // ruler was built. It was kept, and when the game changed font the ruler went on
            // measuring in the old one until the editor next reloaded -- two goals and a hint that
            // no longer fit passed every fitting test in the meantime.
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;

            if (font != null && _ruler.font != font)
                _ruler.font = font;

            _ruler.fontSize = SizeOf(type);
            return _ruler;
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
        /// so stretched over the screen it left the screen's edges undimmed beside a supposedly
        /// full-screen panel. With no sprite, Unity draws a flat colour.
        /// </remarks>
        public static Image Scrim(string name, Transform parent, Color colour)
        {
            RectTransform rect = Rect(name, parent);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour;

            return image;
        }

        /// <summary>
        /// Takes keyboard focus off whatever button was just clicked.
        /// </summary>
        /// <remarks>
        /// A Button that keeps focus consumes Space and Enter, and this game binds both to the run:
        /// press Space to pause after clicking RUN, and the focused button clicks itself again
        /// instead. Every button that is not a text field calls this once it has done its work.
        ///
        /// Written out twelve times under four names -- Fire, Deselect, Defocus, and nothing -- before
        /// it was one call. Twelve copies of a reason is eleven places the reason can be left out.
        /// </remarks>
        public static void Defocus()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>
        /// Takes the keyboard from <paramref name="field"/>, and only if it still has it: a field that finishes because
        /// another was clicked must not take the keyboard from that one too (two code fields on the workbench, Part 12e).
        /// </summary>
        public static void Defocus(TMP_InputField field)
        {
            UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
            if (field != null && events != null && events.currentSelectedGameObject == field.gameObject)
                events.SetSelectedGameObject(null);
        }

        /// <summary>
        /// Gives keyboard focus to a text field, the one thing in the game that should hold it.
        /// </summary>
        /// <remarks>
        /// Beside <see cref="Defocus"/> so that focus is taken and given in one place. The field
        /// holding focus is what takes the game's keys away from it (<see cref="UiText.Typing"/>),
        /// so giving focus to anything else would silence the keys with nothing typed.
        /// </remarks>
        public static void Focus(TMP_InputField field)
        {
            if (field == null)
                return;

            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(field.gameObject);

            field.ActivateInputField();
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
        /// Anything shorter than two corners has to replace the sprite.
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

        /// <summary>How large each kind of text is drawn. See <see cref="UiType"/>.</summary>
        public static float SizeOf(UiType type)
        {
            switch (type)
            {
                case UiType.Micro: return 12f;
                case UiType.Caption: return 13f;
                case UiType.Label: return 15f;
                case UiType.Body: return 18f;
                case UiType.Numeral: return 22f;
                case UiType.Lead: return 24f;
                case UiType.Heading: return 26f;
                case UiType.Title: return 40f;
                case UiType.Display: return 54f;
                default: return 18f;
            }
        }

        /// <summary>
        /// A text element, as large as <paramref name="type"/> says. Wrapping is off by default
        /// because every label here is one short line and a wrapped label silently changes a
        /// panel's height.
        /// </summary>
        public static TextMeshProUGUI Label(
            string name, Transform parent, UiType type, Color colour,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            RectTransform rect = Rect(name, parent);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = SizeOf(type);
            text.color = colour;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;   // labels must never eat a click meant for the board

            return text;
        }

        /// <summary>
        /// A button with a caption, set as <paramref name="captionType"/> and drawn as its
        /// <paramref name="role"/> says. The click handler is the caller's to attach.
        /// </summary>
        /// <remarks>
        /// The caption's size is asked for here rather than changed afterwards, which is what the
        /// 4.x game's small buttons did -- made at full size, then shrunk.
        /// </remarks>
        public static Button Button_(
            string name, Transform parent, string caption, out TextMeshProUGUI label,
            UiType captionType = UiType.Body, ButtonRole role = ButtonRole.Secondary)
        {
            Button button = RowButton(name, parent);

            var background = (Image)button.targetGraphic;
            background.color = FillOf(role);

            // Solid whatever the look's panels are: in an outlined look the one button a screen is
            // asking for is told apart by its shape, not only by its colour.
            if (role == ButtonRole.Primary)
                background.sprite = ProceduralSprites.Panel(PanelStyle.Filled);

            label = Label(name + " label", button.transform, captionType, Text, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.text = caption;

            return button;
        }

        /// <summary>
        /// A button with no caption, for a row that lays out its own contents.
        /// </summary>
        /// <remarks>
        /// Those rows used to make an ordinary button and destroy its caption straight away, four
        /// times over.
        ///
        /// Navigation is switched off deliberately. A selected Button consumes Space and Enter, and
        /// this game binds both -- Space pauses and Enter runs -- so a button that kept focus after a
        /// click would swallow the very keys the player expects to work next.
        /// </remarks>
        public static Button RowButton(string name, Transform parent)
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

            return button;
        }

        /// <summary>A button's colour by what it is for.</summary>
        public static Color FillOf(ButtonRole role)
        {
            switch (role)
            {
                case ButtonRole.Primary: return Palette.Current.ButtonPrimary;
                case ButtonRole.Quiet: return Palette.Current.ButtonQuiet;
                case ButtonRole.Destructive: return Palette.Current.ButtonDestructive;
                default: return PanelEdge;
            }
        }

        /// <summary>How tall a slider's track is drawn; its handle and hit area are taller.</summary>
        public const float SliderTrack = 6f;

        /// <summary>A slider's handle, across.</summary>
        public const float SliderHandle = 20f;

        /// <summary>
        /// Puts a panel in front of its siblings.
        /// </summary>
        /// <remarks>
        /// A canvas draws children in sibling order, which is the order they were created -- so
        /// without this, which panel covers which is decided by the order components happen to run
        /// their Start. Whatever was opened most recently should be the thing in front.
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
