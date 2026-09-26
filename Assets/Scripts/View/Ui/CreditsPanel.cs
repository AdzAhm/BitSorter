using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The credits: the roll from <see cref="Credits"/>, rising slowly up the screen. Reached from
    /// Settings, and any key or click goes back there.
    /// </summary>
    /// <remarks>
    /// It rises from below the screen and comes to rest with the last word in the middle, rather
    /// than rolling off into an empty screen -- a roll that ends on nothing reads as the game having
    /// stopped. Counted on <see cref="Time.deltaTime"/>, like every other motion in the interface,
    /// so a capture of it is the same every time.
    /// </remarks>
    public sealed class CreditsPanel : FullScreenPanel
    {
        [SerializeField] private SettingsPanel _settings;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>How fast the roll rises, in reference pixels a second: slow enough to read, not a crawl.</summary>
        public const float Speed = 70f;

        /// <summary>How wide the roll's lines may run before they wrap.</summary>
        public const float Width = 900f;

        private RectTransform _roll;

        /// <summary>How far below the roll's top the last word's middle sits.</summary>
        private float _restDepth;

        /// <summary>How far the roll's top edge has risen above the bottom of the screen.</summary>
        private float _risen;

        /// <summary>Whether the roll is up.</summary>
        public bool IsOpen => IsShowing;

        /// <summary>How far the roll has risen, for the tests to watch it move.</summary>
        public float Risen => _risen;

        /// <summary>Whether the roll has come to rest on its last word.</summary>
        public bool AtRest => _roll != null && _risen >= RestingRise(RoomHeight, _restDepth);

        private float RoomHeight => Root != null ? Root.rect.height : 0f;

        private void Awake()
        {
            if (_settings == null) _settings = FindFirstObjectByType<SettingsPanel>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Build();
            Show(false);
        }

        private void Update()
        {
            if (!IsShowing)
                return;

            // Any key, or any click. Not on the frame the roll opened: the press that opened it was
            // aimed at the CREDITS button, not at this.
            if (!OpenedThisFrame && AnyPress())
            {
                Back();
                return;
            }

            _risen = Mathf.Min(_risen + Speed * Time.deltaTime, RestingRise(RoomHeight, _restDepth));
            _roll.anchoredPosition = new Vector2(0f, _risen);
        }

        /// <summary>
        /// How far the roll's top rises before it stops: until the last word's middle is at the
        /// middle of a screen <paramref name="room"/> tall.
        /// </summary>
        public static float RestingRise(float room, float restDepth) => room * 0.5f + restDepth;

        private static bool AnyPress()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                return true;

            Mouse mouse = Mouse.current;

            return mouse != null
                   && (mouse.leftButton.wasPressedThisFrame
                       || mouse.rightButton.wasPressedThisFrame
                       || mouse.middleButton.wasPressedThisFrame);
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private const float RoleGap = 6f;
        private const float LineGap = 4f;
        private const float GroupGap = 44f;

        private void Build()
        {
            // A scrim, and a catcher for clicks: any click goes back, wherever it lands.
            Image scrim = UiTheme.Scrim("Credits", _canvas.transform, Palette.Current.CardScrim);
            scrim.raycastTarget = true;
            Root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(Root);

            // Hung from the bottom of the screen by its top edge, so rising is one number.
            _roll = UiTheme.Rect("roll", Root);
            _roll.anchorMin = new Vector2(0.5f, 0f);
            _roll.anchorMax = new Vector2(0.5f, 0f);
            _roll.pivot = new Vector2(0.5f, 1f);

            var column = new UiColumn();
            IReadOnlyList<Credits.Line> lines = Credits.Roll(MainMenu.Tagline, MainMenu.VersionText);

            foreach (Credits.Line line in lines)
            {
                if (line.Kind == Credits.Kind.Gap)
                {
                    column.Space(GroupGap);
                    continue;
                }

                UiType type = TypeOf(line.Kind);
                float height = Mathf.Ceil(UiTheme.TextHeight(line.Text, type, Width));

                TextMeshProUGUI label = UiTheme.Label(
                    line.Kind.ToString(), _roll, type, ColourOf(line.Kind), TextAlignmentOptions.Top);
                label.textWrappingMode = TextWrappingModes.Normal;

                if (line.Kind == Credits.Kind.Role)
                {
                    label.fontStyle = FontStyles.Bold;
                    label.characterSpacing = 6f;
                }

                float top = column.Take(height, line.Kind == Credits.Kind.Role ? RoleGap : LineGap);
                UiTheme.Anchor(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -top), new Vector2(Width, height));
                label.text = line.Text;

                if (line.Kind == Credits.Kind.Farewell)
                    _restDepth = top + height * 0.5f;
            }

            _roll.sizeDelta = new Vector2(Width, column.Next);

            // In the corner, clear of the column the roll rises through. Centred at the foot, the way
            // Settings says how to leave it, every line of the roll passed through it on the way up.
            TextMeshProUGUI help = UiTheme.Label(
                "help", Root, UiType.Caption, UiTheme.TextDim, TextAlignmentOptions.Right);
            UiTheme.Anchor(help.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiTheme.Margin, 16f), new Vector2(HelpWidth, 20f));
            help.text = HelpText;
        }

        /// <summary>The line in the corner saying how to leave the roll.</summary>
        public const string HelpText = "any key to go back";

        /// <summary>How wide that line's box is, in the corner beside the roll's column.</summary>
        public const float HelpWidth = 240f;

        private static UiType TypeOf(Credits.Kind kind)
        {
            switch (kind)
            {
                case Credits.Kind.Title: return UiType.Display;
                case Credits.Kind.Subtitle: return UiType.Body;
                case Credits.Kind.Role: return UiType.Label;
                case Credits.Kind.Name: return UiType.Heading;
                case Credits.Kind.Farewell: return UiType.Title;
                default: return UiType.Body;
            }
        }

        private static Color ColourOf(Credits.Kind kind)
        {
            switch (kind)
            {
                case Credits.Kind.Title:
                case Credits.Kind.Farewell:
                    return UiTheme.Accent;
                case Credits.Kind.Name:
                    return UiTheme.Text;
                default:
                    return UiTheme.TextDim;
            }
        }

        // -----------------------------------------------------------------
        // Showing
        // -----------------------------------------------------------------

        /// <summary>Starts the roll from below the screen. Used by Settings' CREDITS button.</summary>
        public void Open() => Show(true);

        private void Back()
        {
            Show(false);

            if (_settings != null)
                _settings.Open();
        }

        private void Show(bool visible) => SetShowing(visible);

        protected override void OnShown()
        {
            _risen = 0f;

            if (_roll != null)
                _roll.anchoredPosition = Vector2.zero;
        }
    }
}
