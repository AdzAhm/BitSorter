using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The level's clock drawn the way a textbook draws one: a square wave, with a playhead on the
    /// tick the run is at. Behind F2, in the bottom left corner.
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
    ///
    /// **Back on F2, after a day away.** The timing diagram replaced it on 2026-09-30 and took F2
    /// with it; Ahmad asked for the two apart the next day, so this is F2 again, as it was, and the
    /// timing diagram has F8 (<see cref="WaveformPanel"/>). This owns F2's one flag:
    /// <see cref="DiagnosticsPanel"/> follows <see cref="IsOpen"/> rather than reading the key itself.
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

        /// <summary>Whether F2 is on -- the intent, drawn or not.</summary>
        /// <remarks>
        /// On a level without a clock nothing of this is drawn, and the flag still says F2 is on:
        /// diagnostics in the other corner follows it on every level.
        /// </remarks>
        public bool IsOpen => _shown;

        /// <summary>Whether the diagram is on screen now.</summary>
        public bool IsShowing => _root != null && _root.gameObject.activeSelf;

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
                "caption", _root, UiType.Micro, UiTheme.TextDim, TextAlignmentOptions.TopLeft);
            UiTheme.Anchor(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Inset, -6f), new Vector2(200f, 16f));
            caption.text = "CLOCK";

            _wave = UiTheme.Rect("wave", _root);
            UiTheme.Stretch(_wave);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            Keyboard keyboard = UiText.Keyboard;

            // Not behind a full-screen panel, where every board key stands aside: pressed on the
            // main menu it did nothing visible, and the readout appeared once the menu closed.
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame && !UiModal.OpenOrJustClosed)
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

            // tick counts executed ticks from 0, as the timing diagram's clock row does -- one rule
            // for when the clock is high, not one per drawing of it.
            for (int tick = 0; tick < ticks; tick++)
            {
                bool high = WaveformRecorder.ClockOn(tick, period);
                float y = high ? -WaveTop : -WaveBottom;

                Bar(new Vector2(Inset + tick * step, y), new Vector2(step, Thickness));

                if (tick == 0)
                    continue;

                bool before = WaveformRecorder.ClockOn(tick - 1, period);

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
