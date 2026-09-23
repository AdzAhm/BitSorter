using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The level's clock, drawn as the beat it actually is: one pip per tick in a cycle, lit in
    /// turn as the run moves through it.
    /// </summary>
    /// <remarks>
    /// Only on levels that have a clock. Every level up to the sequential chapter runs a vector
    /// every tick, where a clock would be a row of one pip saying nothing.
    ///
    /// It exists because the clock is the one rule of this chapter that has no other home on
    /// screen. A player can see bits, wires and gates; "a new vector arrives every third tick, and
    /// your loop has that long to come back" is invisible until something collides. The pips make
    /// the beat watchable, and a register's capture lands on the beat -- which is what makes the
    /// connection between the two without a sentence having to claim it.
    ///
    /// Polled against a cached copy, like every other readout here: the strings are rebuilt only
    /// when the level changes, and the lit pip only when the tick does.
    /// </remarks>
    public sealed class ClockReadout : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Canvas the readout is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private const float PipSize = 10f;
        private const float PipGap = 5f;
        private const float RowHeight = 22f;

        /// <summary>Left inset, and where the beat starts after the label.</summary>
        private const float Inset = 12f;
        private const float PipRowLeft = 122f;

        /// <summary>
        /// How wide the strip is for a period, measured from what goes in it.
        /// </summary>
        /// <remarks>
        /// A fixed width was fine while every panel faded out towards its edges, because the empty
        /// end of the strip faded with it. Now that a panel has a real edge, a two-tick clock in a
        /// strip sized for four is a bar that is half empty for no reason.
        /// </remarks>
        public static float WidthFor(int period)
        {
            float beat = period <= 0 ? 0f : period * (PipSize + PipGap) - PipGap;
            return PipRowLeft + beat + Inset;
        }

        private RectTransform _root;
        private TextMeshProUGUI _label;
        private RectTransform _pipRow;

        private readonly List<Image> _pips = new List<Image>();

        private int _builtPeriod = -1;
        private int _litPip = -1;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Image panel = UiTheme.Panel_("Clock", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();
            panel.raycastTarget = false;   // a readout, never a click target

            // Under the banner and under the verdict, on the row UiTheme reserves for it. Worked
            // out here instead, it landed on the verdict: both were placed from the banner, and
            // only one of them knew the other existed.
            UiTheme.Anchor(_root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiRows.Clock.Offset),
                new Vector2(WidthFor(0), UiRows.Clock.Height));

            _label = UiTheme.Label("period", _root, 13f, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(_label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(Inset, 0f), new Vector2(PipRowLeft - Inset * 2f, RowHeight));

            var row = new GameObject("Beat", typeof(RectTransform));
            row.transform.SetParent(_root, false);
            _pipRow = row.GetComponent<RectTransform>();
            UiTheme.Anchor(_pipRow, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(PipRowLeft, 0f), new Vector2(126f, RowHeight));

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_root == null)
                return;

            LevelDefinition level = _session != null && _session.IsLoaded ? _session.Level : null;
            bool wanted = level != null && level.HasClock && UiModal.HudVisible;

            if (_root.gameObject.activeSelf != wanted)
                _root.gameObject.SetActive(wanted);

            if (!wanted)
                return;

            if (level.ClockPeriod != _builtPeriod)
                Rebuild(level.ClockPeriod);

            Light(TickInCycle(level.ClockPeriod));
        }

        /// <summary>Where the run is inside the current cycle, or the first pip when it is not running.</summary>
        private int TickInCycle(int period)
        {
            if (_runner == null || !_runner.IsReady)
                return 0;

            int tick = _runner.View.CurrentTick;

            return tick <= 0 ? 0 : (tick - 1) % period;
        }

        private void Rebuild(int period)
        {
            for (int i = _pipRow.childCount - 1; i >= 0; i--)
                Destroy(_pipRow.GetChild(i).gameObject);

            _pips.Clear();
            _litPip = -1;
            _builtPeriod = period;
            _label.text = $"CLOCK  {period} TICKS";

            _root.sizeDelta = new Vector2(WidthFor(period), _root.sizeDelta.y);

            for (int i = 0; i < period; i++)
            {
                Image pip = UiTheme.Panel_($"Pip {i}", _pipRow, UiTheme.TextDim * 0.5f);
                pip.raycastTarget = false;

                // A pip is a beat, not a panel, and at ten pixels it is shorter than two of the
                // panel sprite's corners -- which would leave nothing between them to slice.
                pip.sprite = ProceduralSprites.Circle();

                UiTheme.Anchor(pip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(i * (PipSize + PipGap) + PipSize * 0.5f, 0f),
                    new Vector2(PipSize, PipSize));

                _pips.Add(pip);
            }
        }

        /// <summary>
        /// Lights the pip the run is on. The first pip of a cycle is the one a vector arrives on,
        /// so it is drawn in the accent colour and the rest are dim.
        /// </summary>
        private void Light(int index)
        {
            if (index == _litPip)
                return;

            _litPip = index;

            for (int i = 0; i < _pips.Count; i++)
            {
                if (_pips[i] == null)
                    continue;

                bool lit = i == index;
                Color colour = i == 0 ? UiTheme.Accent : UiTheme.TextDim;

                _pips[i].color = lit ? colour : colour * 0.35f;
            }
        }
    }
}
