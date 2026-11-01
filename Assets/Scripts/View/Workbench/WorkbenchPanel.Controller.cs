using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// A controller shown as the textbook draws one (Part 15h-6e): stepping into a state machine or a decoder written in code
    /// shows its diagram or its tables in the circuit's window (<see cref="ControllerView"/>), lit as the run goes, with DRAWING
    /// beside CODE in the window's top strip for the drawing of its code -- and DIAGRAM there to come back.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The switch between a controller's diagram and its drawing, left of the CODE switch.</summary>
        public const string ControllerSwitchName = "Workbench controller switch";

        /// <summary>What the switch says over the drawing of a controller: it turns to the diagram.</summary>
        public const string DiagramCaption = "DIAGRAM";

        private const float ControllerSwitchWidth = 116f;

        /// <summary>How far down the window the diagram starts: under its top strip, the breadcrumb and the switches.</summary>
        private const float ControllerTop = 48f;

        private ControllerView _controllerView;
        private UnityEngine.UI.Button _controllerSwitch;
        private TMPro.TextMeshProUGUI _controllerCaption;
        private bool _controllerViewShown, _controllerSwitchShown;
        private Rect _controllerPlace;

        /// <summary>The controller's diagram or tables, over the circuit's window while one is shown.</summary>
        public ControllerView ControllerView => _controllerView;

        /// <summary>DIAGRAM over a controller's drawing, DRAWING over its diagram.</summary>
        public UnityEngine.UI.Button ControllerSwitch => _controllerSwitch;

        /// <summary>Whether the circuit's window shows a controller as the textbook draws it, over its drawing.</summary>
        private bool ControllerShown => _session != null && _session.ShowsController && !PageShowing;

        private void BuildController(SchematicPalette palette)
        {
            _controllerView = new ControllerView();
            _controllerView.style.display = DisplayStyle.None;
            _circuitPanel.Add(_controllerView);
            _controllerSwitch = CanvasButton(ControllerSwitchName, Root, DiagramCaption, out _controllerCaption, UiType.Body, palette);
            _controllerSwitch.onClick.AddListener(() =>
            {
                if (_session != null)
                    _session.DrawController(!_session.ShowsController);
                UiTheme.Defocus();
            });
            Show(_controllerSwitch, false);
        }

        /// <summary>The diagram up while a controller is shown, under the window's top strip and over the strip along its foot, lit as the run is now.</summary>
        private void TickController()
        {
            if (_controllerView == null)
                return;
            bool shown = ControllerShown;
            if (shown != _controllerViewShown)
            {
                Shown(_controllerView, shown);
                _controllerViewShown = shown;
                if (shown)
                {
                    if (_minimap != null)
                        _minimap.gameObject.SetActive(false);   // the drawing's map, of a drawing out of sight
                }
                else
                    PlaceMinimap();
            }
            if (!shown)
                return;

            Vector2 window = SizeOf(_regions.Circuit);
            var place = new Rect(0f, ControllerTop, window.x, Mathf.Max(0f, window.y - ControllerTop - LessonInset()));
            if (place != _controllerPlace)
            {
                _controllerPlace = place;
                ShellTheme.Place(_controllerView, place.x, place.y, place.width, place.height);
            }
            _controllerView.Show(_session.Controller);
            _controllerView.Light(_session.ControllerNow());
        }

        /// <summary>The controller switch shown and placed, left of the CODE switch, saying what it turns to; whether it is shown.</summary>
        private bool TickControllerSwitch(bool overWritten, float codeLeft)
        {
            if (_controllerSwitch == null)
                return false;
            bool shown = overWritten && _session.ShowsDrawingOfCode && _session.Controller != null;
            if (shown != _controllerSwitchShown)
            {
                Show(_controllerSwitch, shown);
                if (shown)
                    _controllerSwitch.transform.SetAsLastSibling();
                _controllerSwitchShown = shown;
            }
            if (!shown)
                return false;
            Vector2 at = CornerOf(_regions.Circuit) + new Vector2(codeLeft - 8f - ControllerSwitchWidth, 8f);
            var rect = _controllerSwitch.GetComponent<RectTransform>();
            if (rect.anchoredPosition != new Vector2(at.x, -at.y))
                TopLeft(rect, at, new Vector2(ControllerSwitchWidth, 32f));
            string caption = _session.ShowsController ? DrawingCaption : DiagramCaption;
            if (_controllerCaption.text != caption)
                _controllerCaption.text = caption;
            return true;
        }
    }
}
