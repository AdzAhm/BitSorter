using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Live overlay: simulation stats in one block, controls in another, and a transient reason
    /// whenever an edit is refused.
    /// </summary>
    /// <remarks>
    /// Rows are laid out from a single cursor advanced by the style's real line height, and word
    /// wrap is switched off explicitly. GUI.skin.label wraps by default, which silently made long
    /// hint lines spill out of their row and draw over the next one.
    ///
    /// IMGUI needs no canvas, font asset or prefab, which is why the overlay is one AddComponent.
    /// It allocates a little per frame and is not a base for real UI; when this stops being a
    /// debug readout it should become a canvas.
    /// </remarks>
    public sealed class SimulationHud : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private PlacementController _placement;
        [SerializeField] private float _rejectedHintSeconds = 2f;
        [SerializeField] private int _fontSize = 18;

        [SerializeField] private Color _textColour = new Color(0.90f, 0.92f, 0.96f);
        [SerializeField] private Color _labelColour = new Color(0.58f, 0.62f, 0.70f);
        [SerializeField] private Color _corruptedColour = new Color(1.00f, 0.45f, 0.40f);
        [SerializeField] private Color _hintColour = new Color(0.55f, 0.58f, 0.66f);
        [SerializeField] private Color _panelColour = new Color(0.04f, 0.05f, 0.07f, 0.82f);

        private const float Margin = 14f;
        private const float Padding = 12f;
        private const float PanelWidth = 330f;
        private const float LabelColumn = 110f;
        private const float BlockGap = 10f;

        private GUIStyle _valueStyle;
        private GUIStyle _keyStyle;
        private GUIStyle _hintStyle;

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

            if (_placement == null)
                _placement = FindFirstObjectByType<PlacementController>();
        }

        private void OnGUI()
        {
            if (_runner == null || !_runner.IsReady)
                return;

            EnsureStyles();

            SimulationView view = _runner.View;
            CacheText(view);

            bool paused = _runner.IsPaused;
            bool rejected = _runner.WasRecentlyRejected(_rejectedHintSeconds)
                            && !string.IsNullOrEmpty(_runner.LastRejectionReason);

            // Height is counted before anything is drawn so the panel can sit behind the rows.
            int hintRows = paused ? 5 : 2;
            float height = Padding * 2f
                           + 3f * _statRow
                           + BlockGap + _hintRow          // palette
                           + BlockGap + hintRows * _hintRow
                           + (rejected ? BlockGap + _hintRow : 0f);

            DrawPanel(new Rect(Margin, Margin, PanelWidth, height));

            _y = Margin + Padding;

            // Live stats.
            StatRow("Tick", _tickText, _textColour);
            StatRow("Corrupted", _corruptedText, _shownCorrupted > 0 ? _corruptedColour : _textColour);
            StatRow("Nodes", _nodesText, _textColour);

            // Palette.
            _y += BlockGap;
            if (_placement != null)
                StatRow("Palette", GatePalette.Label(_placement.Selected), _textColour);
            else
                HintRow("no placement controller");

            // Controls, one action per row so nothing ever has to wrap.
            _y += BlockGap;

            if (paused)
            {
                HintRow("PAUSED");
                HintRow("space          resume");
                HintRow("right arrow    step one tick");
                HintRow("1-6 / click    select / place");
                HintRow("drag port      wire     right click  delete");
            }
            else
            {
                HintRow("space          pause");
                HintRow("pause to place, wire or delete");
            }

            if (!rejected)
                return;

            _y += BlockGap;
            HintRow(_runner.LastRejectionReason, _corruptedColour);
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

            // Rows advance by the style's real line height, not a guessed constant.
            _statRow = _valueStyle.lineHeight + 6f;
            _hintRow = _hintStyle.lineHeight + 4f;
        }
    }
}
