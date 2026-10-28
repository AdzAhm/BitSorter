using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// The credits: the roll from <see cref="Credits"/>, rising slowly up the page. Reached from Settings and from HELP's
    /// ABOUT, and any key or click goes back. A page of the workbench's own since Part 15g, in the style's colours, as
    /// Settings is: it was a uGUI screen in the 4.x board's.
    /// </summary>
    /// <remarks>
    /// It rises from below the page and comes to rest with the last word in the middle, rather than rolling off into an
    /// empty page -- a roll that ends on nothing reads as the game having stopped. Counted on <see cref="Time.deltaTime"/>,
    /// like every other motion in the interface, so a capture of it is the same every time.
    /// </remarks>
    public sealed class CreditsPanel : MonoBehaviour, IHoldsEscape
    {
        [SerializeField] private SettingsPanel _settings;
        [SerializeField] private WorkbenchPanel _workbench;

        /// <summary>How fast the roll rises, in reference pixels a second: slow enough to read, not a crawl.</summary>
        public const float Speed = 70f;

        /// <summary>How wide the roll's lines may run before they wrap.</summary>
        public const float Width = 900f;

        /// <summary>The page's name in the shell.</summary>
        public const string PageName = "Workbench credits page";

        private VisualElement _page, _roll;
        private Label _farewell;

        /// <summary>How far the roll's top edge has risen above the bottom of the page.</summary>
        private float _risen;

        /// <summary>The frames it last came up and went down in.</summary>
        private int _openedFrame = -1, _closedFrame = -1;

        /// <summary>Whether the roll is up.</summary>
        public bool IsShowing => _page != null && _page.style.display == DisplayStyle.Flex;

        /// <inheritdoc cref="IsShowing"/>
        public bool IsOpen => IsShowing;

        /// <summary>How far the roll has risen, for the tests to watch it move.</summary>
        public float Risen => _risen;

        /// <summary>Whether the roll has come to rest on its last word.</summary>
        public bool AtRest => _roll != null && _risen >= RestingRise(RoomHeight, RestDepth);

        private float RoomHeight => _page != null && !float.IsNaN(_page.layout.height) ? _page.layout.height : 0f;

        /// <summary>How far below the roll's top the last word's middle sits, once the roll is laid out.</summary>
        private float RestDepth =>
            _farewell != null && !float.IsNaN(_farewell.layout.height) ? _farewell.layout.y + _farewell.layout.height * 0.5f : 0f;

        private void Awake()
        {
            if (_settings == null) _settings = FindFirstObjectByType<SettingsPanel>();
            if (_workbench == null) _workbench = FindFirstObjectByType<WorkbenchPanel>();
            UiEscape.Join(this);
        }

        private void OnDestroy() => UiEscape.Leave(this);

        /// <inheritdoc/>
        /// <remarks>Any key goes back, Escape among them, and nothing else takes that press.</remarks>
        public bool HoldsEscapeNow => IsShowing || _closedFrame == Time.frameCount;

        private void Update()
        {
            if (!IsShowing)
                return;

            // Any key, or any click. Not on the frame the roll opened: the press that opened it was aimed at the CREDITS
            // button, not at this.
            if (_openedFrame != Time.frameCount && AnyPress())
            {
                Keyboard keyboard = UiText.Keyboard;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                    UiEscape.TryTake(this);
                Back();
                return;
            }

            _risen = Mathf.Min(_risen + Speed * Time.deltaTime, RestingRise(RoomHeight, RestDepth));
            Place();
        }

        /// <summary>The roll hung from the page's foot by its top edge, risen so far: rising is one number.</summary>
        private void Place()
        {
            if (_roll != null)
                _roll.style.top = RoomHeight - _risen;
        }

        /// <summary>
        /// How far the roll's top rises before it stops: until the last word's middle is at the middle of a page
        /// <paramref name="room"/> tall.
        /// </summary>
        public static float RestingRise(float room, float restDepth) => room * 0.5f + restDepth;

        private static bool AnyPress()
        {
            Keyboard keyboard = UiText.Keyboard;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                return true;

            // A mouse button on its release, not its press. The page under comes back in the frame this answers, and a
            // press answered here could still go on to it in the same frame -- to whichever setting was now under the
            // pointer, which it then pressed on release.
            Mouse mouse = Mouse.current;
            return mouse != null
                   && (mouse.leftButton.wasReleasedThisFrame
                       || mouse.rightButton.wasReleasedThisFrame
                       || mouse.middleButton.wasReleasedThisFrame);
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private const float RoleGap = 6f;
        private const float LineGap = 4f;
        private const float GroupGap = 44f;

        private void Build()
        {
            _page = _workbench != null ? _workbench.AddPage(PageName) : null;
            if (_page == null)
                return;
            SchematicPalette palette = SchematicPalette.Current;
            _page.style.overflow = Overflow.Hidden;

            _roll = new VisualElement { name = "roll", pickingMode = PickingMode.Ignore };
            _roll.style.position = Position.Absolute;
            _roll.style.left = 0f;
            _roll.style.right = 0f;
            _roll.style.alignItems = Align.Center;
            _page.Add(_roll);

            IReadOnlyList<Credits.Line> lines = Credits.Roll(MainMenu.Tagline, MainMenu.VersionText);
            foreach (Credits.Line line in lines)
            {
                if (line.Kind == Credits.Kind.Gap)
                {
                    var gap = new VisualElement { name = "gap", pickingMode = PickingMode.Ignore };
                    gap.style.height = GroupGap;
                    _roll.Add(gap);
                    continue;
                }

                bool emphatic = line.Kind == Credits.Kind.Role || line.Kind == Credits.Kind.Title || line.Kind == Credits.Kind.Farewell;
                Label label = ShellTheme.Label(line.Kind.ToString(), line.Text, TypeOf(line.Kind), ColourOf(line.Kind, palette),
                    TextAnchor.UpperCenter, bold: emphatic);
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.width = Width;
                label.style.marginBottom = line.Kind == Credits.Kind.Role ? RoleGap : LineGap;
                if (line.Kind == Credits.Kind.Role)
                    label.style.letterSpacing = 6f;
                _roll.Add(label);
                if (line.Kind == Credits.Kind.Farewell)
                    _farewell = label;
            }

            // In the corner, clear of the column the roll rises through. Centred at the foot, every line of the roll passed
            // through it on the way up.
            Label help = ShellTheme.Label("help", HelpText, UiType.Caption, palette.Muted, TextAnchor.MiddleRight);
            help.style.position = Position.Absolute;
            help.style.right = 24f;
            help.style.bottom = 16f;
            help.style.width = HelpWidth;
            _page.Add(help);
        }

        /// <summary>The line in the corner saying how to leave the roll.</summary>
        /// <remarks>
        /// With its verb. "any key to go back" matched the terse "escape to go back" on Settings, but a key is a thing you
        /// press and "any key" is not one, so the line read as missing a word -- a playtest said so (2026-09-27).
        /// </remarks>
        public const string HelpText = "press any key to go back";

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

        private static Color ColourOf(Credits.Kind kind, SchematicPalette palette)
        {
            switch (kind)
            {
                case Credits.Kind.Title:
                case Credits.Kind.Farewell:
                    return palette.Accent;
                case Credits.Kind.Name:
                    return palette.Text;
                default:
                    return palette.TextDim;
            }
        }

        // -----------------------------------------------------------------
        // Showing
        // -----------------------------------------------------------------

        /// <summary>Starts the roll from below the page. Used by Settings' CREDITS button, which it goes back to.</summary>
        public void Open() => Open(null);

        /// <summary>
        /// Starts the roll, which goes back to <paramref name="back"/> when it is done -- the simulator, from HELP's ABOUT
        /// (Part 15a) -- or to Settings.
        /// </summary>
        public void Open(System.Action back)
        {
            _back = back;
            if (_page == null)
                Build();
            if (_page == null)
                return;
            _risen = 0f;
            _workbench.ShowPage(_page);
            Place();
            _openedFrame = Time.frameCount;
            UiEscape.Opened(this);
        }

        private System.Action _back;

        private void Back()
        {
            _workbench.HidePage(_page);
            _closedFrame = Time.frameCount;

            System.Action back = _back;
            _back = null;
            if (back != null)
                back();
            else if (_settings != null)
                _settings.Reopen();
        }
    }
}
