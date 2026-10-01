using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Developer numbers, up with the clock diagram behind F2. Hidden by default.
    /// </summary>
    /// <remarks>
    /// What is in here is deliberately only what a player never needs: the tick, how many nodes are
    /// live, and the graph revision. Corruption is not here -- it is the feedback loop that makes
    /// balance-the-paths teach, so it lives in the game interface where it cannot be missed. See
    /// <see cref="BitsLostMeter"/>.
    ///
    /// It does not read F2 itself: it follows <see cref="ClockDiagram.IsOpen"/>, the one F2 flag.
    /// Two readers of one key each with a flag of its own came apart the first time one of them was
    /// switched some other way. It followed the timing diagram's flag for the day that diagram was
    /// on F2 (2026-09-30), and stayed with F2 when the timing diagram moved to F8.
    /// </remarks>
    public sealed class DiagnosticsPanel : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;
        [SerializeField] private ClockDiagram _clock;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private RectTransform _root;
        private TextMeshProUGUI _text;

        // The numbers the text says, so it is built again only when one of them moves: four
        // interpolated lines a frame were garbage on the one screen somebody opens to watch numbers.
        private int _drawnTick = -1;
        private int _drawnNodes = -1;
        private int _drawnEdges = -1;
        private int _drawnRevision = -1;

        /// <summary>Whether the numbers are on screen now.</summary>
        public bool IsShowing => _root != null && _root.gameObject.activeSelf;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_clock == null) _clock = FindFirstObjectByType<ClockDiagram>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Image panel = UiTheme.Panel_("Diagnostics", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();
            // Bottom right, under the timing diagram's strip, which starts above the toast. The
            // clock diagram takes the other corner.
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

            // Out of the way of a full-screen panel, like every other readout. This did not, so F2
            // left it drawn over the level list and the win panel, while the clock diagram on the
            // same key stepped aside.
            bool wanted = _clock != null && _clock.IsOpen && UiModal.HudVisible;

            if (_root.gameObject.activeSelf != wanted)
                _root.gameObject.SetActive(wanted);

            if (!wanted || _runner == null || !_runner.IsReady)
                return;

            SimulationView view = _runner.View;

            if (view.CurrentTick == _drawnTick && view.LiveNodeCount == _drawnNodes &&
                view.LiveEdgeCount == _drawnEdges && _runner.GraphRevision == _drawnRevision)
                return;

            _drawnTick = view.CurrentTick;
            _drawnNodes = view.LiveNodeCount;
            _drawnEdges = view.LiveEdgeCount;
            _drawnRevision = _runner.GraphRevision;

            _text.text =
                $"tick        {view.CurrentTick}\n" +
                $"nodes       {view.LiveNodeCount}\n" +
                $"wires       {view.LiveEdgeCount}\n" +
                $"revision    {_runner.GraphRevision}";
        }
    }
}
