using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Live text overlay: current tick, bits destroyed so far, and the pause state.
    /// </summary>
    /// <remarks>
    /// Drawn with IMGUI, which needs no canvas, font asset or prefab -- the whole overlay is one
    /// AddComponent. IMGUI allocates a little per frame and is not what shipping UI should be
    /// built on; when this stops being a debug readout, replace it with a canvas. The displayed
    /// strings are rebuilt only when their values actually change, since OnGUI runs more than
    /// once per frame.
    /// </remarks>
    public sealed class SimulationHud : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private PlacementController _placement;
        [SerializeField] private float _rejectedHintSeconds = 2f;
        [SerializeField] private int _fontSize = 20;
        [SerializeField] private Color _textColour = new Color(0.90f, 0.92f, 0.96f);
        [SerializeField] private Color _corruptedColour = new Color(1.00f, 0.45f, 0.40f);
        [SerializeField] private Color _hintColour = new Color(0.55f, 0.58f, 0.66f);

        private GUIStyle _valueStyle;
        private GUIStyle _hintStyle;

        private int _shownTick = -1;
        private int _shownCorrupted = -1;
        private int _shownNodes = -1;
        private string _tickText = "Tick  0";
        private string _corruptedText = "Corrupted  0";
        private string _nodesText = "Nodes  0";

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

            if (view.CurrentTick != _shownTick)
            {
                _shownTick = view.CurrentTick;
                _tickText = "Tick  " + _shownTick;
            }

            if (view.CorruptedCount != _shownCorrupted)
            {
                _shownCorrupted = view.CorruptedCount;
                _corruptedText = "Corrupted  " + _shownCorrupted;
            }

            const float left = 16f;
            const float width = 900f;   // wide enough for the palette line
            const float line = 26f;
            float y = 12f;

            _valueStyle.normal.textColor = _textColour;
            GUI.Label(new Rect(left, y, width, line), _tickText, _valueStyle);
            y += line;

            _valueStyle.normal.textColor = _shownCorrupted > 0 ? _corruptedColour : _textColour;
            GUI.Label(new Rect(left, y, width, line), _corruptedText, _valueStyle);
            y += line;

            if (view.LiveNodeCount != _shownNodes)
            {
                _shownNodes = view.LiveNodeCount;
                _nodesText = "Nodes  " + _shownNodes;
            }

            _valueStyle.normal.textColor = _textColour;
            GUI.Label(new Rect(left, y, width, line), _nodesText, _valueStyle);
            y += line;

            if (_placement != null)
            {
                GUI.Label(
                    new Rect(left, y, width, line),
                    "Palette  " + GatePalette.Label(_placement.Selected)
                                + "     1 NOT  2 AND  3 OR  4 XOR  5 NAND  6 NOR",
                    _hintStyle);
                y += line;
            }

            GUI.Label(
                new Rect(left, y, width, line),
                _runner.IsPaused
                    ? "PAUSED   space resumes   right arrow steps   click a cell to place   drag port to port to wire   right click to delete"
                    : "space pauses",
                _hintStyle);
            y += line;

            // Transient: whatever the last refused edit was, from placement or wiring alike.
            if (_runner.WasRecentlyRejected(_rejectedHintSeconds) && !string.IsNullOrEmpty(_runner.LastRejectionReason))
            {
                _hintStyle.normal.textColor = _corruptedColour;
                GUI.Label(new Rect(left, y, width, line), _runner.LastRejectionReason, _hintStyle);
                _hintStyle.normal.textColor = _hintColour;
            }
        }

        private void EnsureStyles()
        {
            // GUI.skin is only valid inside OnGUI, so these cannot be built in Awake.
            if (_valueStyle == null)
                _valueStyle = new GUIStyle(GUI.skin.label) { fontSize = _fontSize };

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, _fontSize - 5) };
                _hintStyle.normal.textColor = _hintColour;
            }
        }
    }
}
