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
        [SerializeField] private int _fontSize = 20;
        [SerializeField] private Color _textColour = new Color(0.90f, 0.92f, 0.96f);
        [SerializeField] private Color _corruptedColour = new Color(1.00f, 0.45f, 0.40f);
        [SerializeField] private Color _hintColour = new Color(0.55f, 0.58f, 0.66f);

        private GUIStyle _valueStyle;
        private GUIStyle _hintStyle;

        private int _shownTick = -1;
        private int _shownCorrupted = -1;
        private string _tickText = "Tick  0";
        private string _corruptedText = "Corrupted  0";

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();
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
            const float width = 460f;
            const float line = 26f;
            float y = 12f;

            _valueStyle.normal.textColor = _textColour;
            GUI.Label(new Rect(left, y, width, line), _tickText, _valueStyle);
            y += line;

            _valueStyle.normal.textColor = _shownCorrupted > 0 ? _corruptedColour : _textColour;
            GUI.Label(new Rect(left, y, width, line), _corruptedText, _valueStyle);
            y += line;

            GUI.Label(
                new Rect(left, y, width, line),
                _runner.IsPaused
                    ? "PAUSED   space resumes   right arrow steps one tick"
                    : "space pauses",
                _hintStyle);
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
