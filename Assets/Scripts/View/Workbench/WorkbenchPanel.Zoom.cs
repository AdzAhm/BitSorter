using System;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// The zoom controls (Part 15h-4d, asked 2026-10-29: "a small zoom slider ... not working on its own"), each the zoom its
    /// window already has, never one of its own, and so moved by the wheel, the keys, FIT, a step inside and a new run too:
    /// <list type="bullet">
    /// <item>the circuit's, a slider at its window's lower right, its knob smaller than the time slider's -- a drag zooms
    /// about the window's middle through <see cref="SchematicView.ZoomTo"/>, even in the zoom's ratio
    /// (<see cref="ZoomSlider"/>), from as far out as the wheel goes to as far in; down over code;</item>
    /// <item>the waves', a range bar at the right end of their legend's row (<see cref="WaveRangeBar"/>, Ahmad's drawing): a
    /// line for the whole run and the stretch shown on it, an end dragged zooming and the middle dragged moving through time
    /// (<see cref="WaveRange"/>); down on every dock tab but WAVES.</item>
    /// </list>
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        public const string CircuitZoomName = "Workbench circuit zoom", WavesRangeName = "Workbench waves range";

        private const float ZoomSliderWidth = 140f, ZoomSliderHeight = 22f, ZoomKnob = 14f, WavesRangeWidth = 240f;

        private ShellSlider _circuitZoom;
        private WaveRangeBar _wavesRange;

        // The press on the range bar: what it took, and the stretch shown then, in the run's own units.
        private WaveRange.Grip _rangeGrip;
        private double _rangeFrom, _rangeTo;
        private bool _rangeHeld;

        /// <summary>The circuit's zoom slider, at its window's lower right.</summary>
        public ShellSlider CircuitZoom => _circuitZoom;

        /// <summary>The waves' range bar, at the right end of their legend's row.</summary>
        public WaveRangeBar WavesRange => _wavesRange;

        private void BuildZoomSliders(SchematicPalette palette)
        {
            _circuitZoom = ShellTheme.Slider(CircuitZoomName, palette, ZoomKnob);
            _circuitZoom.lowValue = 0f;
            _circuitZoom.highValue = 1f;
            Absolute(_circuitZoom, ZoomSliderWidth);
            _circuitPanel.Add(_circuitZoom);
            _circuitZoom.Changed += at =>
            {
                if (_viewState != null && _viewState.ZoomTo(_viewState.Window * 0.5f,
                        (float)ZoomSlider.FromSlider(at, _viewState.Floor, SchematicView.MaxZoom)))
                    ApplyView();
            };

            _wavesRange = new WaveRangeBar(WavesRangeName, palette);
            Absolute(_wavesRange, WavesRangeWidth);
            _dockPanel.Add(_wavesRange);
            _wavesRange.Pressed += (grip, at) =>
            {
                if (_wave == null)
                    return;
                WaveView view = _wave.View;
                if (grip == WaveRange.Grip.None)
                {
                    // A press on the line beside the stretch brings the stretch's middle there, and drags it from there.
                    if (WaveRange.Centre(view, at))
                        Redraw();
                    grip = WaveRange.Grip.Middle;
                }
                _rangeGrip = grip;
                _rangeFrom = view.Start;
                _rangeTo = Math.Min(view.End, view.Span);
                _rangeHeld = true;
            };
            _wavesRange.Moved += by =>
            {
                if (_rangeHeld && _wave != null && WaveRange.Drag(_wave.View, _rangeGrip, _rangeFrom, _rangeTo, by))
                    Redraw();
            };
            _wavesRange.Released += () => _rangeHeld = false;
        }

        private static void Absolute(VisualElement element, float width)
        {
            element.style.position = Position.Absolute;
            element.style.width = width;
            element.style.height = ZoomSliderHeight;
            element.style.display = DisplayStyle.None;
        }

        /// <summary>Each control where its window is, up while its zoom means something, and showing its zoom unless held.</summary>
        private void TickZoomSliders()
        {
            if (_circuitZoom != null)
            {
                bool shown = _viewState != null && _session != null && !_session.ShowsCode && !PageShowing;
                Shown(_circuitZoom, shown);
                if (shown)
                {
                    // Clear of the strip along the window's foot while a hint or the tutorial is up.
                    float bottom = 12f + LessonInset();
                    if (!Mathf.Approximately(_circuitZoom.resolvedStyle.bottom, bottom))
                    {
                        _circuitZoom.style.right = 12f;
                        _circuitZoom.style.bottom = bottom;
                    }
                    if (!_circuitZoom.Dragging)   // derived from the drag, never a flag that a lost drag could leave set
                    {
                        float at = ZoomSlider.ToSlider(_viewState.Zoom, _viewState.Floor, SchematicView.MaxZoom);
                        if (Mathf.Abs(_circuitZoom.value - at) > 1e-4f)
                            _circuitZoom.SetValueWithoutNotify(at);
                    }
                }
            }

            if (_wavesRange != null)
            {
                bool shown = _wave != null && _dockTab == DockTab.Waves && !PageShowing;
                Shown(_wavesRange, shown);
                if (shown)
                {
                    Vector2 legend = CornerOf(_regions.Legend), size = SizeOf(_regions.Legend), dock = CornerOf(_regions.Dock);
                    float left = legend.x + size.x - WavesRangeWidth - 8f - dock.x;
                    float top = legend.y - dock.y + (size.y - ZoomSliderHeight) * 0.5f;
                    if (!Mathf.Approximately(_wavesRange.resolvedStyle.left, left) || !Mathf.Approximately(_wavesRange.resolvedStyle.top, top))
                    {
                        _wavesRange.style.left = left;
                        _wavesRange.style.top = top;
                    }
                    (float from, float to) = WaveRange.Of(_wave.View);
                    _wavesRange.Show(from, to);
                }
            }
        }

        private static void Shown(VisualElement element, bool shown)
        {
            DisplayStyle display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display)
                element.style.display = display;
        }
    }
}
