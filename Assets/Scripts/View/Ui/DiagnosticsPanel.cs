using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Developer numbers, up with the timing diagram behind F2. Hidden by default.
    /// </summary>
    /// <remarks>
    /// What is in here is deliberately only what a player never needs: the tick, how many nodes are
    /// live, and the graph revision. Corruption is not here -- it is the feedback loop that makes
    /// balance-the-paths teach, so it lives in the game interface where it cannot be missed. See
    /// <see cref="BitsLostMeter"/>.
    ///
    /// It does not read F2 itself: it follows <see cref="WaveformPanel.IsOpen"/>, the one F2 flag,
    /// which the badge beside the help badge flips too. Two readers of one key each with a flag of
    /// its own came apart the first time one of them was switched some other way.
    /// </remarks>
    public sealed class DiagnosticsPanel : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;
        [SerializeField] private WaveformPanel _waveform;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private RectTransform _root;
        private TextMeshProUGUI _text;

        /// <summary>Whether the numbers are on screen now.</summary>
        public bool IsShowing => _root != null && _root.gameObject.activeSelf;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_waveform == null) _waveform = FindFirstObjectByType<WaveformPanel>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Image panel = UiTheme.Panel_("Diagnostics", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();
            // Bottom right, under the timing diagram's strip, which starts above the toast.
            UiTheme.AnchorBottomCorner(_root, UiTheme.DiagnosticsCorner, 96f);

            panel.raycastTarget = false;

            _text = UiTheme.Label("numbers", _root, UiType.Caption, UiTheme.TextDim, TextAlignmentOptions.TopLeft);
            UiTheme.Stretch(_text.rectTransform, 10f);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_root == null)
                return;

            // Out of the way of a full-screen panel, like every other readout. This did not, so
            // F2 left it drawn over the level list and the win panel.
            bool wanted = _waveform != null && _waveform.IsOpen && UiModal.HudVisible;

            if (_root.gameObject.activeSelf != wanted)
                _root.gameObject.SetActive(wanted);

            if (!wanted || _runner == null || !_runner.IsReady)
                return;

            SimulationView view = _runner.View;

            _text.text =
                $"tick        {view.CurrentTick}\n" +
                $"nodes       {view.LiveNodeCount}\n" +
                $"wires       {view.LiveEdgeCount}\n" +
                $"revision    {_runner.GraphRevision}";
        }
    }
}
