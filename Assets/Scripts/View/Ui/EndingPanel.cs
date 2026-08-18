using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The last panel: what the whole run added up to, once every level is solved.
    /// </summary>
    /// <remarks>
    /// Solving the final level used to do exactly what solving the tutorial does, which made the
    /// end of the game indistinguishable from the middle of it. This replaces the ordinary win
    /// panel on that one occasion.
    ///
    /// Everything it says is derived. The level names come from the catalogue and the figures from
    /// the player's own records, so it cannot claim an arc the levels do not actually have -- if
    /// the level order changes, this changes with it.
    ///
    /// It stays on the right side of the line CLAUDE.md draws around scoring: a total of the
    /// player's own bests is the same kind of fact as the gate count already on the win panel,
    /// measured against nothing but itself. No rank, no par, no grade.
    /// </remarks>
    public sealed class EndingPanel : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private ProgressTracker _progress;
        [SerializeField] private MainMenu _menu;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private RectTransform _root;
        private TextMeshProUGUI _detail;

        private RunState _state = RunState.Editing;
        private bool _shown;

        /// <summary>
        /// Whether finishing this run should end the game rather than show the ordinary win panel.
        /// </summary>
        /// <remarks>
        /// Public and static because <see cref="WinPanel"/> has to ask the same question to know
        /// when to stand aside. Two panels deciding this separately is exactly the duplication that
        /// ends with both showing at once, or neither.
        ///
        /// The current level counts as solved without consulting the store. This is only ever
        /// called on a pass, and whether the tracker has recorded it yet depends on component
        /// update order -- which is not something the ending should hinge on.
        /// </remarks>
        public static bool IsTheEnd(LevelSession session, ProgressTracker progress)
        {
            if (session == null || progress == null || !session.IsLoaded)
                return false;

            return IsTheEnd(session.Catalogue, session.LevelIndex, session.LevelName, progress.Store);
        }

        /// <inheritdoc cref="IsTheEnd(LevelSession, ProgressTracker)"/>
        /// <remarks>
        /// Takes the store rather than the tracker so it can be tested. The tracker builds its
        /// store in Awake, which Edit Mode never runs; ProgressStore is a plain class that takes a
        /// path, which is the seam the progress tests already use.
        /// </remarks>
        public static bool IsTheEnd(
            IReadOnlyList<LevelEntry> catalogue,
            int levelIndex,
            string currentLevel,
            ProgressStore store)
        {
            if (catalogue == null || store == null || catalogue.Count == 0)
                return false;

            // The last level specifically. Solving everything else in some other order and
            // finishing on level three is not an ending.
            if (levelIndex != catalogue.Count - 1)
                return false;

            foreach (LevelEntry entry in catalogue)
            {
                if (entry.FileName == currentLevel)
                    continue;

                if (!store.IsComplete(entry.FileName))
                    return false;
            }

            return true;
        }

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_menu == null) _menu = FindFirstObjectByType<MainMenu>();
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

        private void Update()
        {
            if (_session == null || _root == null)
                return;

            RunState now = _session.State;

            if (now != _state)
            {
                if (now == RunState.Passed && IsTheEnd(_session, _progress))
                    Present();
                else if (_shown)
                    Show(false);

                _state = now;
            }

            // Escape as well as the buttons. A full-screen panel that only a mouse can dismiss is
            // one bad click away from feeling stuck, and level select cannot answer Escape while
            // this is registered as a modal.
            Keyboard keyboard = Keyboard.current;

            if (_shown && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Dismiss();
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Build()
        {
            Image scrim = UiTheme.Panel_("Ending", _canvas.transform, new Color(0f, 0f, 0f, 0.9f));
            _root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(_root);

            TextMeshProUGUI title = UiTheme.Label(
                "title", _root, 44f, UiTheme.Good, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 150f), new Vector2(760f, 58f));
            title.text = "EVERY BIN FED";

            _detail = UiTheme.Label("detail", _root, 19f, UiTheme.Text, TextAlignmentOptions.Top);
            UiTheme.Anchor(_detail.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 10f), new Vector2(640f, 220f));
            _detail.alignment = TextAlignmentOptions.Center;
            _detail.textWrappingMode = TextWrappingModes.Normal;

            Button menu = UiTheme.Button_("Menu", _root, "MAIN MENU", out TextMeshProUGUI _);
            UiTheme.Anchor(menu.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-90f, -150f),
                new Vector2(170f, UiTheme.ButtonHeight + 4f));
            menu.onClick.AddListener(ToMenu);

            Button stay = UiTheme.Button_("Stay", _root, "KEEP TINKERING", out TextMeshProUGUI _);
            UiTheme.Anchor(stay.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(90f, -150f),
                new Vector2(170f, UiTheme.ButtonHeight + 4f));
            stay.onClick.AddListener(Dismiss);
        }

        // -----------------------------------------------------------------
        // Contents
        // -----------------------------------------------------------------

        private void Present()
        {
            IReadOnlyList<LevelEntry> catalogue = _session.Catalogue;

            int gates = 0;
            int counted = 0;

            foreach (LevelEntry entry in catalogue)
            {
                int best = _progress.BestGates(entry.FileName);

                if (best <= 0)
                    continue;

                gates += best;
                counted++;
            }

            var text = new System.Text.StringBuilder();

            text.Append($"{catalogue.Count} levels solved.");

            // Only claimed when there is something to total. A player who solved every level with
            // no gates at all would otherwise be told about zero gates across zero levels.
            if (counted > 0)
                text.Append($"\n{gates} gates across your best solutions.");

            text.Append($"\n\nFrom \"{catalogue[0].DisplayName}\" to " +
                        $"\"{catalogue[catalogue.Count - 1].DisplayName}\".");

            text.Append("\n\nThe circuits are still there. So are the levels.");

            _detail.text = text.ToString();

            Show(true);
        }

        // -----------------------------------------------------------------
        // Showing
        // -----------------------------------------------------------------

        private void Show(bool visible)
        {
            _shown = visible;

            if (_root != null && _root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);

            if (visible)
            {
                UiTheme.BringToFront(_root);
                UiModal.Opened(this);
            }
            else
            {
                UiModal.Closed(this);
            }
        }

        private void ToMenu()
        {
            Show(false);

            if (_menu != null)
                _menu.Show(true);

            Deselect();
        }

        /// <summary>Leaves the solved board exactly as it was, the way the win panel does.</summary>
        private void Dismiss()
        {
            Show(false);
            Deselect();
        }

        private static void Deselect()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
