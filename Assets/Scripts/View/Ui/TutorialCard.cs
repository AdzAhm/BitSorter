using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The tutorial's ending: what the player just learned, every control they have not met yet,
    /// and one button into the first level.
    /// </summary>
    /// <remarks>
    /// Built the way <see cref="EndingPanel"/> is, because that is already this game's idea of a
    /// finish -- a full-screen scrim, a large title, centred copy and a prominent button. Reusing
    /// the shape means the tutorial ends like the game's other endings rather than like a tooltip.
    /// It replaced a flat thirteen-line list in the thin instruction strip, which read as a config
    /// file printed over the board.
    ///
    /// The controls come from <see cref="ControlsReference.Groups"/>, so a binding added there
    /// appears in the right column here with no edit to this file.
    ///
    /// Registers with <see cref="UiModal"/> while it is up, so the board holds still behind it and
    /// Q cannot change level under a panel nobody can see through.
    /// </remarks>
    public sealed class TutorialCard : MonoBehaviour
    {
        [Tooltip("Canvas the card is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private const string Title = "THAT'S THE LOOP";

        private const string Body =
            "You picked a part, placed it, wired it up and ran it. " +
            "That is every level in this game.\n\nHere is everything else you can do.";

        private RectTransform _root;
        private bool _shown;

        /// <summary>Set when the player presses the button that ends the tutorial.</summary>
        /// <remarks>
        /// Consumed rather than expiring, for the reason <see cref="TutorialPanel"/> spells out: a
        /// flag cleared on a timer depends on the EventSystem beating the director to the frame.
        /// </remarks>
        private bool _finishPressed;

        public bool IsShowing => _shown;

        public bool ConsumeFinish()
        {
            bool pressed = _finishPressed;
            _finishPressed = false;
            return pressed;
        }

        private void Awake()
        {
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void OnDisable() => UiModal.Closed(this);

        private void Start()
        {
            if (_canvas == null)
                return;

            Build();
            Show(false);
        }

        /// <summary>
        /// Escape ends the tutorial, as the button does.
        /// </summary>
        /// <remarks>
        /// The same answer <see cref="EndingPanel"/> gives, for the same reason it gives it: a
        /// full-screen panel only a mouse can dismiss is one bad click away from feeling stuck. It
        /// matters more here than there, because this card registers with <see cref="UiModal"/> --
        /// so while it is up, Escape does not reach the level list, M does not reach the main menu,
        /// and <see cref="SimulationInput"/> reads nothing at all. The button was the only way out
        /// of the whole game.
        ///
        /// Escape *finishes* rather than merely hiding. <see cref="TutorialDirector"/> is sitting in
        /// its Card phase waiting for <see cref="ConsumeFinish"/>, so hiding without that would
        /// leave it waiting on a card nobody can see -- which is the stuck state, not the way out of
        /// it. Finishing also records the milestone, which is right: somebody who dismisses the
        /// ending has finished the tutorial.
        /// </remarks>
        private void Update()
        {
            if (!_shown)
                return;

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Finish();
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Build()
        {
            Image scrim = UiTheme.Scrim("Tutorial card", _canvas.transform, Palette.Current.CardScrim);
            _root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(_root);

            TextMeshProUGUI title = UiTheme.Label(
                "title", _root, 44f, UiTheme.Good, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 250f), new Vector2(760f, 58f));
            title.text = Title;

            TextMeshProUGUI body = UiTheme.Label(
                "body", _root, 19f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 170f), new Vector2(660f, 80f));
            body.textWrappingMode = TextWrappingModes.Normal;
            body.text = Body;

            float columnsBottom = BuildColumns();

            const float buttonHeight = UiTheme.ButtonHeight + 6f;

            // Placed under whichever column reaches lowest, rather than at a fixed offset. It was
            // fixed at -220, which happened to clear the columns until a control was added to the
            // reference -- eleven rows in the right-hand column reach exactly the button's top edge,
            // and the twelfth would have drawn through it. Deriving the position means adding a
            // binding cannot collide here at all.
            float buttonY = columnsBottom - ButtonGap - buttonHeight * 0.5f;

            Button play = UiTheme.Button_("Play", _root, "PLAY THE FIRST LEVEL",
                out TextMeshProUGUI _);
            UiTheme.Anchor(play.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, buttonY),
                new Vector2(300f, buttonHeight));

            play.onClick.AddListener(Finish);
        }

        /// <summary>
        /// Two columns: building on the left, running and everything else stacked on the right.
        /// </summary>
        /// <remarks>
        /// Two rather than three because the longest phrase here is "right arrow to step one tick",
        /// and a third column at this width would clip it. Which group lands where is decided by
        /// height rather than written down: the first group fills the left column and the rest stack
        /// down the right, so adding a control cannot silently push a heading off the panel.
        ///
        /// Returns how low the columns reach, so the button can be put below them rather than at a
        /// guessed offset.
        /// </remarks>
        private float BuildColumns()
        {
            const float columnWidth = 330f;

            IReadOnlyList<ControlGroup> groups = ControlsReference.Groups;

            float leftY = ColumnTop;
            float rightY = ColumnTop;

            for (int i = 0; i < groups.Count; i++)
            {
                bool left = i == 0;
                float x = left ? -175f : 175f;
                float y = left ? leftY : rightY;

                y = BuildGroup(groups[i], x, y, columnWidth, RowHeight, HeadingGap);

                if (left)
                    leftY = y;
                else
                    rightY = y;
            }

            return Mathf.Min(leftY, rightY);
        }

        /// <summary>Where the columns begin, measured from the middle of the card.</summary>
        private const float ColumnTop = 90f;

        /// <summary>Height of one heading or one control row.</summary>
        private const float RowHeight = 26f;

        /// <summary>Extra space after a group, so the headings separate the blocks.</summary>
        private const float HeadingGap = 12f;

        /// <summary>Space between the lowest control row and the button under it.</summary>
        private const float ButtonGap = 24f;

        /// <summary>
        /// Rows the taller of the two columns needs, headings counted.
        /// </summary>
        /// <remarks>
        /// The balance the two-column split depends on. The first group takes the left column and
        /// the rest stack down the right, which is fine while the two are comparable and starts
        /// wasting half the card when they are not -- and the right column is the one that grows,
        /// because new bindings are usually neither building nor running.
        ///
        /// Public so the balance can be asserted without a canvas. Nothing in the layout reads it;
        /// the layout measures as it goes.
        /// </remarks>
        public static int TallestColumnRows
        {
            get
            {
                IReadOnlyList<ControlGroup> groups = ControlsReference.Groups;

                int left = 0;
                int right = 0;

                for (int i = 0; i < groups.Count; i++)
                {
                    int rows = 1 + groups[i].Entries.Count;   // the heading, then its controls

                    if (i == 0)
                        left += rows;
                    else
                        right += rows;
                }

                return left > right ? left : right;
            }
        }

        /// <summary>Lays one group out downward from <paramref name="y"/> and returns the new y.</summary>
        private float BuildGroup(ControlGroup group, float x, float y, float width, float rowHeight,
            float headingGap)
        {
            TextMeshProUGUI heading = UiTheme.Label(
                group.Name, _root, 15f, UiTheme.Accent, TextAlignmentOptions.Left);
            UiTheme.Anchor(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x, y), new Vector2(width, rowHeight));

            // UiTheme.Label's first argument names the GameObject, not the text. Without this the
            // three headings drew as empty rows and the grouping the card exists for was invisible.
            heading.text = group.Name;

            y -= rowHeight;

            foreach (ControlEntry entry in group.Entries)
            {
                TextMeshProUGUI row = UiTheme.Label(
                    entry.Text, _root, 16f, UiTheme.Text, TextAlignmentOptions.Left);
                UiTheme.Anchor(row.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, y), new Vector2(width, rowHeight));
                row.text = entry.Text;

                y -= rowHeight;
            }

            return y - headingGap;
        }

        // -----------------------------------------------------------------
        // Showing
        // -----------------------------------------------------------------

        /// <summary>
        /// Ends the tutorial. Shared by the button and by Escape, so the two cannot diverge.
        /// </summary>
        /// <remarks>
        /// Public because it is the seam the press-consumption test reaches through -- driving the
        /// real button needs a canvas and an EventSystem, and driving Escape needs those plus a
        /// keyboard.
        /// </remarks>
        public void Finish()
        {
            _finishPressed = true;
            UiTheme.Defocus();
        }

        public void Show(bool visible)
        {
            if (_root == null)
                return;

            _shown = visible;

            if (_root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);

            if (visible)
            {
                UiTheme.BringToFront(_root);
                UiModal.Opened(this);
            }
            else
            {
                _finishPressed = false;   // a press nobody took belongs to a card that is gone
                UiModal.Closed(this);
            }
        }
    }
}
