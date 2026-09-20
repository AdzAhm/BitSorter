using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The level's clock drawn the way a textbook draws one: a square wave, with a playhead on the
    /// tick the run is at. Behind F3, in the bottom left corner.
    /// </summary>
    /// <remarks>
    /// The pair to <see cref="ClockReadout"/> rather than a replacement for it. The readout is a
    /// row of pips under the banner and is part of the game: it says "a vector every second tick"
    /// to somebody who has never seen a timing diagram. This is the same fact in the notation the
    /// course uses, which is worth having and is worth nobody tripping over -- so it sits behind
    /// the same key the developer numbers do, and takes no room on the controls line.
    ///
    /// Only on levels with a clock. On every level up to the sequential chapter a vector arrives
    /// every tick, where this would be a square wave that is always high.
    /// </remarks>
    public sealed class ClockDiagram : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;

        [Tooltip("Canvas the diagram is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>How many clock cycles the diagram shows.</summary>
        private const int Cycles = 3;

        private const float PanelHeight = 76f;
        private const float WaveTop = 16f;
        private const float WaveBottom = 40f;
        private const float Inset = 12f;
        private const float Thickness = 2f;

        private RectTransform _root;
        private RectTransform _wave;
        private RectTransform _playhead;

        private readonly List<GameObject> _drawn = new List<GameObject>();

        private int _builtPeriod = -1;
        private bool _shown;

        /// <summary>
        /// Whether the clock is high on this tick: high on the tick a vector arrives, low for the
        /// rest of the period.
        /// </summary>
        /// <remarks>
        /// Pure, so the shape can be pinned without a canvas. This is the same fact
        /// <see cref="ClockReadout"/> lights its first pip on -- both ask what the tick is within
        /// the cycle -- and a period of one is high throughout, which is why the diagram is not
        /// drawn at all on a level without a clock.
        /// </remarks>
        public static bool IsHigh(int tick, int period)
        {
            if (period <= 1)
                return true;

            int within = tick <= 0 ? 0 : (tick - 1) % period;
            return within == 0;
        }

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

            Image panel = UiTheme.Panel_("Clock diagram", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();
            UiTheme.AnchorBottomCorner(_root, UiTheme.ClockDiagramCorner, PanelHeight);

            panel.raycastTarget = false;

            TextMeshProUGUI caption = UiTheme.Label(
                "caption", _root, 12f, UiTheme.TextDim, TextAlignmentOptions.TopLeft);
            UiTheme.Anchor(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Inset, -6f), new Vector2(200f, 16f));
            caption.text = "CLOCK";

            _wave = UiTheme.Rect("wave", _root);
            UiTheme.Stretch(_wave);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
                _shown = !_shown;

            LevelDefinition level = _session != null && _session.IsLoaded ? _session.Level : null;
            bool wanted = _shown && level != null && level.HasClock && UiModal.HudVisible;

            if (_root == null)
                return;

            if (_root.gameObject.activeSelf != wanted)
                _root.gameObject.SetActive(wanted);

            if (!wanted)
                return;

            if (level.ClockPeriod != _builtPeriod)
                Rebuild(level.ClockPeriod);

            MovePlayhead(level.ClockPeriod);
        }

        /// <summary>
        /// Draws the wave: a bar per tick at the height its level says, and a riser wherever two
        /// neighbouring ticks disagree.
        /// </summary>
        private void Rebuild(int period)
        {
            for (int i = 0; i < _drawn.Count; i++)
            {
                if (_drawn[i] != null)
                    Destroy(_drawn[i]);
            }

            _drawn.Clear();
            _builtPeriod = period;

            int ticks = Cycles * period;
            float width = UiTheme.CornerWidth - Inset * 2f;
            float step = width / ticks;

            for (int tick = 0; tick < ticks; tick++)
            {
                bool high = IsHigh(tick + 1, period);
                float y = high ? -WaveTop : -WaveBottom;

                Bar(new Vector2(Inset + tick * step, y), new Vector2(step, Thickness));

                if (tick == 0)
                    continue;

                bool before = IsHigh(tick, period);

                if (before != high)
                {
                    // The vertical edge between the two levels, drawn from whichever is on top.
                    Bar(new Vector2(Inset + tick * step, -WaveTop),
                        new Vector2(Thickness, WaveBottom - WaveTop + Thickness));
                }
            }

            // Drawn last so it sits over the wave.
            _playhead = Bar(new Vector2(Inset, -WaveTop + 6f),
                new Vector2(Thickness, WaveBottom - WaveTop + Thickness + 12f), UiTheme.Accent);
        }

        private RectTransform Bar(Vector2 position, Vector2 size) => Bar(position, size, UiTheme.TextDim);

        private RectTransform Bar(Vector2 position, Vector2 size, Color colour)
        {
            RectTransform rect = UiTheme.Rect("bar", _wave);
            UiTheme.Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
            rect.pivot = new Vector2(0f, 1f);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            _drawn.Add(rect.gameObject);
            return rect;
        }

        private void MovePlayhead(int period)
        {
            if (_playhead == null || _runner == null || !_runner.IsReady)
                return;

            int ticks = Cycles * period;
            float width = UiTheme.CornerWidth - Inset * 2f;
            float step = width / ticks;

            int tick = _runner.View.CurrentTick;
            int at = tick <= 0 ? 0 : (tick - 1) % ticks;

            Vector2 position = _playhead.anchoredPosition;
            _playhead.anchoredPosition = new Vector2(Inset + at * step, position.y);
        }
    }
}
