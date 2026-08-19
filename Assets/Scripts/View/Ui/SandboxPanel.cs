using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Free play's setup: how many sources, what each emits, and how many sinks.
    /// </summary>
    /// <remarks>
    /// Owns the <see cref="SandboxConfig"/> as well as drawing it, the way
    /// <see cref="LevelSelectPanel"/> both draws the level list and switches level. There is no
    /// separate controller because there is no second caller.
    ///
    /// Every edit rebuilds the level and re-adopts it, because changing a source changes the graph.
    /// The player's circuit survives that on its own: <see cref="LevelSession.Adopt"/> raises
    /// LevelUnloading and then LevelLoaded, and <see cref="ProgressTracker"/> saves on the first and
    /// restores on the second. Anything that no longer resolves -- a wire into a sink that has just
    /// been removed -- is dropped by the restore's existing checks rather than by anything here.
    ///
    /// Bits are toggled rather than typed. A text field would need focus handling, and this game
    /// binds Space, Enter, R and Q/E, all of which a focused field would swallow.
    /// </remarks>
    public sealed class SandboxPanel : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private ProgressTracker _progress;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private readonly List<GameObject> _body = new List<GameObject>();

        private SandboxConfig _config;
        private RectTransform _root;
        private RectTransform _bodyRoot;
        private bool _shown;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null || _session == null)
                return;

            Build();
            Show(false);
        }

        private void Update()
        {
            if (!_shown)
                return;

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Show(false);
        }

        /// <summary>Whether free play is the level currently loaded.</summary>
        public bool IsOpen => _session != null && _session.LevelName == SandboxLevel.Key;

        // -----------------------------------------------------------------
        // Entry
        // -----------------------------------------------------------------

        /// <summary>
        /// Switches to free play, restoring the setup last used, and shows the panel.
        /// </summary>
        public void Open()
        {
            if (_session == null)
                return;

            if (_config == null)
                _config = Stored() ?? SandboxLevel.Default(Extents());

            if (!IsOpen)
                Adopt();

            Show(true);
        }

        private SandboxConfig Stored()
        {
            SavedBoard board = _progress != null && _progress.Store != null
                ? _progress.Store.BoardFor(SandboxLevel.Key)
                : null;

            return board?.sandbox;
        }

        private Vector2Int Extents() =>
            _runner != null ? _runner.HalfExtents : new Vector2Int(4, 2);

        private void Adopt()
        {
            _session.Adopt(SandboxLevel.Build(_config, Extents()), SandboxLevel.Key);
            Persist();
        }

        /// <summary>
        /// Writes the setup into the sandbox's own saved board, leaving its circuit alone.
        /// </summary>
        /// <remarks>
        /// Reads the stored board back rather than writing a fresh one, because a fresh one would
        /// have no placements and saving it would wipe the circuit that was just restored onto it.
        /// </remarks>
        private void Persist()
        {
            if (_progress == null || _progress.Store == null)
                return;

            SavedBoard board = _progress.Store.BoardFor(SandboxLevel.Key) ?? new SavedBoard();
            board.sandbox = _config.Clone();

            _progress.Store.SaveBoard(SandboxLevel.Key, board);
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Build()
        {
            Image scrim = UiTheme.Panel_("Sandbox", _canvas.transform, new Color(0f, 0f, 0f, 0.78f));
            _root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(_root);

            TextMeshProUGUI title = UiTheme.Label(
                "title", _root, 26f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -44f), new Vector2(600f, 34f));
            title.text = "SANDBOX";

            TextMeshProUGUI blurb = UiTheme.Label(
                "blurb", _root, 13f, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(blurb.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -74f), new Vector2(600f, 20f));
            blurb.text = "every gate, no limits, nothing graded";

            TextMeshProUGUI help = UiTheme.Label(
                "help", _root, 13f, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 34f), new Vector2(600f, 20f));
            help.text = "escape to close    click a bit to flip it";

            _bodyRoot = UiTheme.Rect("body", _root);
            UiTheme.Anchor(_bodyRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -10f), new Vector2(560f, 380f));
        }

        /// <summary>
        /// Rebuilds every row. The counts decide the layout, so there is nothing stable to refresh --
        /// and this runs on a click, never per frame.
        /// </summary>
        private void Rebuild()
        {
            for (int i = 0; i < _body.Count; i++)
            {
                if (_body[i] != null)
                    Destroy(_body[i]);
            }

            _body.Clear();

            int capacity = SandboxLevel.Capacity(Extents());
            float y = 0f;

            y = Stepper(y, "Sources", _config.sources.Length, capacity, SetSources);

            for (int i = 0; i < _config.sources.Length; i++)
                y = StreamRow(y, i);

            y -= 10f;
            y = Stepper(y, "Sinks", _config.sinks, capacity, SetSinks);
            y = Stepper(y, "Vectors", _config.vectors,
                SandboxConfig.MaxVectors, SetVectors, SandboxConfig.MinVectors);
        }

        private float Stepper(
            float y, string caption, int value, int max, System.Action<int> set, int min = 0)
        {
            const float height = 30f;

            TextMeshProUGUI label = UiTheme.Label(
                caption, _bodyRoot, 16f, UiTheme.Text, TextAlignmentOptions.Left);
            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(150f, height));
            label.text = caption;
            _body.Add(label.gameObject);

            Step(y, 160f, "-", value > min, () => set(value - 1));

            TextMeshProUGUI count = UiTheme.Label(
                $"{caption} count", _bodyRoot, 16f, UiTheme.Accent, TextAlignmentOptions.Center);
            UiTheme.Anchor(count.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(196f, y), new Vector2(40f, height));
            count.text = value.ToString();
            _body.Add(count.gameObject);

            Step(y, 240f, "+", value < max, () => set(value + 1));

            return y - (height + 6f);
        }

        private void Step(float y, float x, string glyph, bool enabled, UnityEngine.Events.UnityAction go)
        {
            Button button = UiTheme.Button_($"step {glyph}", _bodyRoot, glyph, out TextMeshProUGUI label);

            UiTheme.Anchor(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(30f, 30f));

            button.interactable = enabled;
            label.color = enabled ? UiTheme.Text : UiTheme.TextDim;
            button.onClick.AddListener(() => { go(); Defocus(); });

            _body.Add(button.gameObject);
        }

        private float StreamRow(float y, int index)
        {
            const float height = 26f;

            TextMeshProUGUI id = UiTheme.Label(
                $"source {index}", _bodyRoot, 15f, UiTheme.TextDim, TextAlignmentOptions.Right);
            UiTheme.Anchor(id.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, y), new Vector2(40f, height));
            id.text = SandboxLevel.SourceId(index);
            _body.Add(id.gameObject);

            string stream = _config.sources[index];

            for (int v = 0; v < stream.Length; v++)
            {
                int vector = v;
                bool one = stream[v] == '1';

                Button bit = UiTheme.Button_(
                    $"bit {index} {v}", _bodyRoot, one ? "1" : "0", out TextMeshProUGUI label);

                UiTheme.Anchor(bit.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(66f + v * 30f, y), new Vector2(26f, height));

                // A one reads as lit and a zero as unlit, the same way BitVisuals colours a bit on
                // the board, so the stream looks like what it will emit.
                label.color = one ? UiTheme.Accent : UiTheme.TextDim;

                bit.onClick.AddListener(() => { Flip(index, vector); Defocus(); });
                _body.Add(bit.gameObject);
            }

            return y - (height + 4f);
        }

        // -----------------------------------------------------------------
        // Edits
        // -----------------------------------------------------------------

        private void SetSources(int count)
        {
            int capacity = SandboxLevel.Capacity(Extents());
            count = Mathf.Clamp(count, 0, capacity);

            var next = new string[count];

            for (int i = 0; i < count; i++)
            {
                next[i] = i < _config.sources.Length
                    ? _config.sources[i]
                    : SandboxConfig.NormaliseStream(string.Empty, _config.vectors);
            }

            _config.sources = next;
            Changed();
        }

        private void SetSinks(int count)
        {
            _config.sinks = Mathf.Clamp(count, 0, SandboxLevel.Capacity(Extents()));
            Changed();
        }

        private void SetVectors(int count)
        {
            _config.vectors = Mathf.Clamp(count, SandboxConfig.MinVectors, SandboxConfig.MaxVectors);
            Changed();
        }

        private void Flip(int index, int vector)
        {
            string stream = _config.sources[index];

            if (vector < 0 || vector >= stream.Length)
                return;

            char[] bits = stream.ToCharArray();
            bits[vector] = bits[vector] == '1' ? '0' : '1';

            _config.sources[index] = new string(bits);
            Changed();
        }

        private void Changed()
        {
            _config.Normalise(SandboxLevel.Capacity(Extents()), SandboxLevel.Capacity(Extents()));
            Adopt();
            Rebuild();
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
                Rebuild();
            }
            else
            {
                UiModal.Closed(this);
            }
        }

        private void OnDisable() => UiModal.Closed(this);

        private static void Defocus()
        {
            // Or the clicked button keeps focus and swallows Space and Enter, which run the board.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
