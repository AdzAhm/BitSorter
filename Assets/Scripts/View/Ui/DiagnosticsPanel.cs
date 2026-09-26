using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Developer numbers, behind F2. Hidden by default.
    /// </summary>
    /// <remarks>
    /// What is in here is deliberately only what a player never needs: the tick, how many nodes are
    /// live, and the graph revision. Corruption is not here -- it is the feedback loop that makes
    /// balance-the-paths teach, so it lives in the game interface where it cannot be missed. See
    /// <see cref="BitsLostMeter"/>.
    ///
    /// Reads its own key, the way PlacementController reads 1-7 and WireDelayController reads the
    /// brackets. A component that owns a key reads it.
    /// </remarks>
    public sealed class DiagnosticsPanel : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private RectTransform _root;
        private TextMeshProUGUI _text;
        private bool _shown;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Image panel = UiTheme.Panel_("Diagnostics", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();
            // Bottom right. UiTheme owns which corner, because the clock diagram now has the other
            // catch readout, so with F2 open in free play the two drew in exactly the same rectangle.
            // That readout is now part of the setup panel, and this stays on the left regardless.
            UiTheme.AnchorBottomCorner(_root, UiTheme.DiagnosticsCorner, 96f);

            panel.raycastTarget = false;

            _text = UiTheme.Label("numbers", _root, UiType.Caption, UiTheme.TextDim, TextAlignmentOptions.TopLeft);
            UiTheme.Stretch(_text.rectTransform, 10f);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            // Not behind a full-screen panel, where every board key stands aside: pressed on the
            // main menu it did nothing visible, and the readout appeared once the menu closed.
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame && !UiModal.OpenOrJustClosed)
                _shown = !_shown;

            if (_root == null)
                return;

            // Out of the way of a full-screen panel, like every other readout. This did not, so
            // F2 left it drawn over the level list and the win panel -- and over the clock diagram
            // in the other corner, which comes up on the same key and did step aside.
            bool wanted = _shown && UiModal.HudVisible;

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
