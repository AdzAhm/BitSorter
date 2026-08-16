using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Live overlay: the level and its parts budget, simulation stats, the run verdict, the controls,
    /// and a transient reason whenever an edit is refused.
    /// </summary>
    /// <remarks>
    /// Rows are laid out from a single cursor advanced by the style's real line height, and word
    /// wrap is switched off explicitly for the fixed rows. GUI.skin.label wraps by default, which
    /// silently made long hint lines spill out of their row and draw over the next one.
    ///
    /// The verdict and the level hint are the exceptions: they are author-written sentences of no
    /// fixed length, so they use a wrapping style and are measured with GUIStyle.CalcHeight. That
    /// measurement happens in the same pass that sizes the panel, so the backing rectangle cannot
    /// disagree with the rows drawn on it.
    ///
    /// IMGUI needs no canvas, font asset or prefab, which is why the overlay is one AddComponent.
    /// It allocates a little per frame and is not a base for real UI; when this stops being a
    /// debug readout it should become a canvas.
    /// </remarks>
    public sealed class SimulationHud : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;
        [SerializeField] private PlacementController _placement;
        [SerializeField] private float _rejectedHintSeconds = 2f;
        [SerializeField] private int _fontSize = 18;

        [SerializeField] private Color _textColour = new Color(0.90f, 0.92f, 0.96f);
        [SerializeField] private Color _labelColour = new Color(0.58f, 0.62f, 0.70f);
        [SerializeField] private Color _corruptedColour = new Color(1.00f, 0.45f, 0.40f);
        [SerializeField] private Color _passColour = new Color(0.45f, 0.90f, 0.55f);
        [SerializeField] private Color _hintColour = new Color(0.55f, 0.58f, 0.66f);
        [SerializeField] private Color _panelColour = new Color(0.04f, 0.05f, 0.07f, 0.82f);

        private const float Margin = 14f;
        private const float Padding = 12f;
        private const float PanelWidth = 360f;
        private const float LabelColumn = 120f;
        private const float BlockGap = 10f;

        private GUIStyle _valueStyle;
        private GUIStyle _keyStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _wrapStyle;

        private float _statRow;
        private float _hintRow;
        private float _y;

        private int _shownTick = -1;
        private int _shownCorrupted = -1;
        private int _shownNodes = -1;
        private string _tickText = "0";
        private string _corruptedText = "0";
        private string _nodesText = "0";

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            if (_session == null)
                _session = FindFirstObjectByType<LevelSession>();

            if (_placement == null)
                _placement = FindFirstObjectByType<PlacementController>();
        }

        private void OnGUI()
        {
            EnsureStyles();

            // Checked before IsReady. A level that would not load leaves the runner with no graph at
            // all, so without this the player gets an empty screen and no idea why.
            if (_session != null && _session.LoadError != null)
            {
                DrawLoadError(_session.LoadError);
                return;
            }

            if (_runner == null || !_runner.IsReady)
                return;

            SimulationView view = _runner.View;
            CacheText(view);

            bool rejected = _runner.WasRecentlyRejected(_rejectedHintSeconds)
                            && !string.IsNullOrEmpty(_runner.LastRejectionReason);

            LevelDefinition level = _session != null ? _session.Level : null;
            RunState state = _session != null ? _session.State : RunState.Editing;

            int budgetRows = level == null ? 0 : Mathf.Max(1, level.Budget.Count);

            // A paused run still says RUNNING otherwise, which reads as "nothing is wrong" while the
            // clock is in fact stopped.
            string status = state == RunState.Running && _runner.IsPaused
                ? "RUNNING (paused)"
                : StatusFor(state);
            string detail = DetailFor(level, state);
            string[] controls = ControlsFor(state);
            float innerWidth = PanelWidth - Padding * 2f;

            // Sized in the same pass that decides what to draw, so the two cannot drift apart.
            float height = Padding * 2f
                           + (level != null ? _statRow : 0f)      // level name
                           + 3f * _statRow                        // tick, corrupted, nodes
                           + BlockGap + budgetRows * _statRow
                           + BlockGap + _hintRow                  // palette
                           + BlockGap + _hintRow                  // status
                           + (detail != null ? WrappedHeight(detail, innerWidth) : 0f)
                           + BlockGap + controls.Length * _hintRow
                           + (rejected ? BlockGap + WrappedHeight(_runner.LastRejectionReason, innerWidth) : 0f);

            DrawPanel(new Rect(Margin, Margin, PanelWidth, height));

            _y = Margin + Padding;

            if (level != null)
                StatRow("Level", level.Name, _textColour);

            StatRow("Tick", _tickText, _textColour);
            StatRow("Corrupted", _corruptedText, _shownCorrupted > 0 ? _corruptedColour : _textColour);
            StatRow("Nodes", _nodesText, _textColour);

            // Parts budget. Remaining is computed from the blueprint every frame, never stored, so it
            // cannot disagree with what is actually on the board.
            _y += BlockGap;

            if (level == null)
            {
                StatRow("Parts", "no level", _hintColour);
            }
            else if (level.Budget.Count == 0)
            {
                StatRow("Parts", "wires only", _hintColour);
            }
            else
            {
                for (int i = 0; i < level.Budget.Count; i++)
                {
                    LevelBudgetEntry entry = level.Budget[i];
                    int remaining = _session.RemainingFor(entry.Kind);

                    StatRow(GatePalette.Label(entry.Kind), $"{remaining} of {entry.Count}",
                        remaining > 0 ? _textColour : _hintColour);
                }
            }

            _y += BlockGap;
            if (_placement != null)
                HintRow($"palette        {GatePalette.Label(_placement.Selected)}");
            else
                HintRow("no placement controller");

            _y += BlockGap;
            HintRow(status, StatusColourFor(state));

            if (detail != null)
                WrappedRow(detail, state == RunState.Failed ? _corruptedColour : _hintColour, innerWidth);

            // Controls, one action per row so nothing ever has to wrap.
            _y += BlockGap;
            for (int i = 0; i < controls.Length; i++)
                HintRow(controls[i]);

            if (!rejected)
                return;

            _y += BlockGap;
            WrappedRow(_runner.LastRejectionReason, _corruptedColour, innerWidth);
        }

        // -----------------------------------------------------------------
        // Run state presentation
        // -----------------------------------------------------------------

        private static string StatusFor(RunState state)
        {
            switch (state)
            {
                case RunState.Editing: return "EDITING";
                case RunState.Running: return "RUNNING";
                case RunState.Passed: return "PASS";
                case RunState.Failed: return "FAIL";
                default: return state.ToString();
            }
        }

        private Color StatusColourFor(RunState state)
        {
            switch (state)
            {
                case RunState.Passed: return _passColour;
                case RunState.Failed: return _corruptedColour;
                default: return _hintColour;
            }
        }

        /// <summary>
        /// The sentence under the status: the level's hint while building, the verdict once a run has
        /// ended, and nothing at all mid-run.
        /// </summary>
        private string DetailFor(LevelDefinition level, RunState state)
        {
            if (state == RunState.Passed || state == RunState.Failed)
            {
                string reason = _session != null ? _session.Verdict.Reason : null;
                return string.IsNullOrEmpty(reason) ? null : reason;
            }

            if (state == RunState.Editing && level != null && !string.IsNullOrEmpty(level.Hint))
                return level.Hint;

            return null;
        }

        private static readonly string[] EditingControls =
        {
            "enter          run",
            "1-6 / click    select / place",
            "drag port      wire",
            "right click    delete",
        };

        private static readonly string[] RunningControls =
        {
            "space          pause     right arrow  step",
            "r              reset and edit",
        };

        private static readonly string[] SettledControls =
        {
            "r              reset and edit",
            "enter          run again",
        };

        private static string[] ControlsFor(RunState state)
        {
            switch (state)
            {
                case RunState.Running: return RunningControls;
                case RunState.Passed:
                case RunState.Failed: return SettledControls;
                default: return EditingControls;
            }
        }

        // -----------------------------------------------------------------
        // Drawing
        // -----------------------------------------------------------------

        private void DrawLoadError(string error)
        {
            float innerWidth = PanelWidth - Padding * 2f;
            float height = Padding * 2f + _hintRow + WrappedHeight(error, innerWidth);

            DrawPanel(new Rect(Margin, Margin, PanelWidth, height));

            _y = Margin + Padding;
            HintRow("LEVEL DID NOT LOAD", _corruptedColour);
            WrappedRow(error, _textColour, innerWidth);
        }

        private void CacheText(SimulationView view)
        {
            // OnGUI runs more than once per frame, so these are rebuilt only on a real change.
            if (view.CurrentTick != _shownTick)
            {
                _shownTick = view.CurrentTick;
                _tickText = _shownTick.ToString();
            }

            if (view.CorruptedCount != _shownCorrupted)
            {
                _shownCorrupted = view.CorruptedCount;
                _corruptedText = _shownCorrupted.ToString();
            }

            if (view.LiveNodeCount != _shownNodes)
            {
                _shownNodes = view.LiveNodeCount;
                _nodesText = _shownNodes.ToString();
            }
        }

        private void StatRow(string label, string value, Color valueColour)
        {
            float x = Margin + Padding;

            GUI.Label(new Rect(x, _y, LabelColumn, _statRow), label, _keyStyle);

            _valueStyle.normal.textColor = valueColour;
            GUI.Label(new Rect(x + LabelColumn, _y, PanelWidth - LabelColumn - Padding * 2f, _statRow),
                value, _valueStyle);

            _y += _statRow;
        }

        private void HintRow(string text) => HintRow(text, _hintColour);

        private void HintRow(string text, Color colour)
        {
            _hintStyle.normal.textColor = colour;
            GUI.Label(new Rect(Margin + Padding, _y, PanelWidth - Padding * 2f, _hintRow), text, _hintStyle);
            _y += _hintRow;
        }

        /// <summary>A row for prose of unknown length: wraps, and advances by its measured height.</summary>
        private void WrappedRow(string text, Color colour, float width)
        {
            float height = WrappedHeight(text, width);

            _wrapStyle.normal.textColor = colour;
            GUI.Label(new Rect(Margin + Padding, _y, width, height), text, _wrapStyle);
            _y += height;
        }

        private float WrappedHeight(string text, float width) =>
            string.IsNullOrEmpty(text) ? 0f : _wrapStyle.CalcHeight(new GUIContent(text), width) + 4f;

        private void DrawPanel(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = _panelColour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            // GUI.skin is only valid inside OnGUI, so these cannot be built in Awake.
            if (_valueStyle != null)
                return;

            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft,
            };

            _keyStyle = new GUIStyle(_valueStyle);
            _keyStyle.normal.textColor = _labelColour;

            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(10, _fontSize - 4),
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft,
            };

            _wrapStyle = new GUIStyle(_hintStyle)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
            };

            // Rows advance by the style's real line height, not a guessed constant.
            _statRow = _valueStyle.lineHeight + 6f;
            _hintRow = _hintStyle.lineHeight + 4f;
        }
    }
}
