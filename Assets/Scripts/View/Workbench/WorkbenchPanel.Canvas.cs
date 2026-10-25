using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using BoardPoint = BitSorter.Board.Point;
using EditResult = BitSorter.Board.EditResult;
using Project = BitSorter.Board.Project;

namespace BitSorter.View
{
    /// <summary>
    /// The workbench's canvas, edited (Part 10a): the parts strip, the canvas's mouse as gestures -- wires drawn and
    /// reshaped with them since Part 10b -- the editing overlay, the drawing made again round an edit, and MY CIRCUIT
    /// kept in the save. Everything it decides is
    /// the view-model's (<see cref="CanvasEditor"/>, <see cref="CanvasPointer"/>, <see cref="PartsCatalog"/>); this
    /// turns the mouse into its gestures and draws what it says.
    /// </summary>
    /// <remarks>
    /// - **The strip is temporary** (Ahmad's choice, 2026-10-20): a column of the canvas's own buttons in the margin
    ///   left of the stage, which stays where it was -- a row under the time pushed the waveform off a 1080 screen --
    ///   until Part 11's shell gives the parts a panel. A click takes a part in hand; a click on the circuit places
    ///   it; Escape, or the right button, puts it down.
    /// - **The circuit's window is sized when a document opens**, and an edit never resizes it: the drawing grows
    ///   inside it and the view keeps everything where it was on screen (<see cref="SchematicView.Relayout"/>).
    /// - **MY CIRCUIT is written half a second after its last edit**, by real time as the 4.x board is, and when
    ///   the workbench is left or another sample shown. The samples are never written.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string PartsStripName = "Workbench parts";

        /// <summary>A strip button's name, for the entry of this key (<see cref="CatalogEntry.Key"/>).</summary>
        public static string PartButtonName(string key) => "Workbench part " + key;

        /// <summary>The window an empty circuit opens in, in stage units: the narrowest stage's, 360 tall.</summary>
        private static readonly Vector2 EmptyDrawing = new Vector2(MinStageWidth - 2f * SchematicAt.x, 360f);

        /// <summary>How tall a button of the strip is, the gap between two, how wide the strip is, and how far left of the stage it stands.</summary>
        private const float PartRowHeight = 28f, PartGap = 3f, PartsWidth = 100f, PartsGap = 14f;

        /// <summary>How long after its last edit MY CIRCUIT is written, in seconds of real time.</summary>
        private const double KeepDelay = 0.5;

        private CanvasEditor _editor;
        private readonly CanvasPointer _canvasPointer = new CanvasPointer();
        private readonly List<CanvasCommand> _canvasCommands = new List<CanvasCommand>(4);
        private readonly CanvasOverlay _overlayData = new CanvasOverlay();
        private WorkbenchGraphic _overlay;
        private int _overlayVersion = -1;
        private int _drawnLayout = -1;

        private RectTransform _partsStrip;
        private readonly List<(CatalogEntry Entry, TextMeshProUGUI Label)> _partButtons = new List<(CatalogEntry, TextMeshProUGUI)>();
        private CatalogEntry _shownInHand;

        /// <summary>Whether the circuit was clicked last, rather than the waveform: what Delete takes away.</summary>
        private bool _canvasFocused;

        /// <summary>Whether Space has been used to pan since it went down: then its release plays nothing.</summary>
        private bool _spacePanned;

        private Vector2 _canvasLast;

        /// <summary>The drawing's size when its document opened: what the circuit's window is sized by.</summary>
        private Vector2 _openedDrawing = EmptyDrawing;

        /// <summary>The ports' keys the waveform's rows were last made to follow.</summary>
        private List<string> _portKeys = new List<string>();

        /// <summary>MY CIRCUIT as last written, and when it is next to be; -1 for nothing waiting.</summary>
        private Project _keptProject;
        private double _keepAt = -1;

        /// <summary>The inspector (Part 10c).</summary>
        private InspectorPopover _inspector;

        /// <summary>The canvas's right-click menu, what Ctrl+C took, and the minimap with whether a press on it is held (Part 10d).</summary>
        private CanvasMenu _canvasMenu;
        private Clip _clip;
        private WorkbenchGraphic _minimap;
        private bool _minimapHeld;


        // -----------------------------------------------------------------
        // What the tests ask
        // -----------------------------------------------------------------

        /// <summary>The canvas being edited: its selection, the part in hand, a drag; null until the panel has first opened.</summary>
        public CanvasEditor Editor => _editor;

        /// <summary>What the editing overlay draws now.</summary>
        public CanvasOverlay Overlay => _overlayData;

        /// <summary>Where a point of the board is on the screen now.</summary>
        public Vector2 ScreenPointOf(BoardPoint board) =>
            _model.Layout.Board == null ? Vector2.zero : ScreenPointOf(_model.Layout.Board.ToStage(board));

        /// <summary>The inspector (Part 10c); open or shut.</summary>
        public InspectorPopover Inspector => _inspector;

        /// <summary>The canvas's right-click menu (Part 10d); open or shut.</summary>
        public CanvasMenu CanvasMenu => _canvasMenu;

        /// <summary>The minimap as it is drawn now; null while the view is the fit and it is not shown (Part 10d).</summary>
        public Minimap Map => _minimap != null && _minimap.gameObject.activeSelf ? _minimap.Map : null;

        /// <summary>The circuit zoomed about the window's middle by <paramref name="notches"/>, as + and - do: for a capture to set the view.</summary>
        public void ZoomAboutTheMiddleForTests(int notches) => ZoomAboutTheMiddle(notches);

        /// <summary>Where a point of the minimap is on the screen, the map's own units from its top-left corner.</summary>
        public Vector2 ScreenPointOfMap(Vec2 onMap)
        {
            var rect = (RectTransform)_minimap.transform;
            Vector3 world = rect.TransformPoint(new Vector3(onMap.x + WorkbenchDrawing.MinimapPad, -(onMap.y + WorkbenchDrawing.MinimapPad), 0f) + new Vector3(rect.rect.xMin, rect.rect.yMax, 0f));
            return RectTransformUtility.WorldToScreenPoint(null, world);
        }

        /// <summary>Takes the strip's entry of this key in hand, as its button does; false for none.</summary>
        public bool TakeFromStrip(string key)
        {
            CatalogEntry entry = PartsCatalog.Find(_session.Project, _session.Module, key);
            if (entry == null)
                return false;
            _editor.Take(entry);
            return true;
        }

        // -----------------------------------------------------------------
        // Opening a document
        // -----------------------------------------------------------------

        /// <summary>A document opened (<c>OpenRun</c>): its canvas, nothing selected, and the window sized round it.</summary>
        private void OpenCanvas()
        {
            _editor = new CanvasEditor(_session);
            _canvasPointer.Abandon();
            _drawnLayout = _session.LayoutVersion;
            _overlayVersion = -1;
            _portKeys = PortKeys(_model);
            _openedDrawing = _model.Layout.Parts.Count + _model.Layout.Ports.Count == 0 ? EmptyDrawing : _model.Layout.Size.ToUnity();
            _keptProject = _session.Project;
            _keepAt = -1;
        }

        /// <summary>The save the game keeps, through the progress tracker; null in a scene without one.</summary>
        private static ProgressStore Store()
        {
            ProgressTracker tracker = FindFirstObjectByType<ProgressTracker>();
            return tracker != null ? tracker.Store : null;
        }

        /// <summary>MY CIRCUIT as the save keeps it, or a blank document the first time.</summary>
        private static Project MyCircuit()
        {
            ProgressStore store = Store();
            return store != null && store.TryGetProject(WorkbenchSamples.MyCircuitKey, out Project kept, new List<string>()) && kept.TopModule != null
                ? kept
                : WorkbenchSamples.Blank();
        }

        /// <summary>Whether MY CIRCUIT has anything in it: what the workbench opens on, where the reference circuit is otherwise.</summary>
        private static bool MyCircuitHasAnything()
        {
            Project kept = MyCircuit();
            return kept.TopModule != null && kept.TopModule.Parts.Count > 0;
        }

        /// <summary>
        /// Writes MY CIRCUIT when it has changed: <paramref name="now"/>, or half a second after its last edit. The
        /// samples are never written.
        /// </summary>
        private void KeepMyCircuit(bool now)
        {
            if (_session == null || _sample != 0 || ReferenceEquals(_session.Project, _keptProject))
            {
                _keepAt = -1;
                return;
            }

            double time = UnityEngine.Time.unscaledTimeAsDouble;
            if (!now && _keepAt < 0)
            {
                _keepAt = time + KeepDelay;
                return;
            }

            if (!now && time < _keepAt)
                return;

            ProgressStore store = Store();
            if (store != null)
            {
                store.SetProject(WorkbenchSamples.MyCircuitKey, _session.Project);
                store.Save();
            }

            _keptProject = _session.Project;
            _keepAt = -1;
        }

        // -----------------------------------------------------------------
        // The parts strip
        // -----------------------------------------------------------------

        /// <summary>The strip, a column left of the stage: a button for every part the circuit can be built from.</summary>
        private void BuildPartsStrip(SchematicPalette palette)
        {
            if (_partsStrip != null)
            {
                _partsStrip.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(_partsStrip.gameObject);
                else
                    DestroyImmediate(_partsStrip.gameObject);
            }

            _partButtons.Clear();
            _shownInHand = null;
            IReadOnlyList<CatalogEntry> entries = PartsCatalog.For(_session.Project, _session.Module);

            _partsStrip = UiTheme.Rect(PartsStripName, Root);
            _partsStrip.anchorMin = _partsStrip.anchorMax = new Vector2(0.5f, 1f);
            _partsStrip.pivot = new Vector2(1f, 1f);
            _partsStrip.sizeDelta = new Vector2(PartsWidth, entries.Count * (PartRowHeight + PartGap));
            _partsStrip.SetSiblingIndex(_timeline.GetSiblingIndex() + 1);

            for (int i = 0; i < entries.Count; i++)
            {
                CatalogEntry entry = entries[i];
                Button button = CanvasButton(PartButtonName(entry.Key), _partsStrip, entry.Label, out TextMeshProUGUI label, UiType.Body, palette);
                TopLeft(button.GetComponent<RectTransform>(), new Vector2(0f, i * (PartRowHeight + PartGap)), new Vector2(PartsWidth, PartRowHeight));
                button.onClick.AddListener(() => { _editor.Take(entry); UiTheme.Defocus(); });
                label.enableAutoSizing = true;
                label.fontSizeMax = label.fontSize;
                label.fontSizeMin = UiTheme.SizeOf(UiType.Body) * 0.75f;   // a block's long module name shrinks rather than spills
                _partButtons.Add((entry, label));
            }

            PlacePartsStrip();
        }

        /// <summary>The strip beside the stage as it is drawn now: its top at the stage's, its right a gap short of the stage's left.</summary>
        private void PlacePartsStrip()
        {
            if (_partsStrip == null)
                return;
            float scale = _scale > 0f ? _scale : 1f;
            _partsStrip.anchoredPosition = new Vector2(-(StageWidth * scale * 0.5f + PartsGap), -StageTop);
        }

        /// <summary>The strip's button for the part in hand in the accent, the rest as they were.</summary>
        private void ShowInHand()
        {
            if (_editor.InHand == _shownInHand)
                return;
            _shownInHand = _editor.InHand;

            SchematicPalette palette = SchematicPalette.Current;
            foreach ((CatalogEntry entry, TextMeshProUGUI label) in _partButtons)
                label.color = entry == _shownInHand ? palette.Accent : palette.Text;
        }

        // -----------------------------------------------------------------
        // The mouse on the canvas
        // -----------------------------------------------------------------

        /// <summary>
        /// The mouse on the circuit as gestures (<see cref="CanvasPointer"/>): a press on its window -- any button --
        /// starts one, and a press held goes on being the canvas's wherever the pointer goes until it is let go.
        /// </summary>
        private void TakeCanvas(Mouse mouse, Vector2 pointer, Keyboard keyboard, bool overControl)
        {
            bool shift = keyboard != null && keyboard.shiftKey.isPressed;
            bool ctrl = keyboard != null && keyboard.ctrlKey.isPressed;
            bool alt = keyboard != null && keyboard.altKey.isPressed;
            bool space = keyboard != null && keyboard.spaceKey.isPressed;
            bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame, middle = mouse.middleButton.wasPressedThisFrame;

            // The minimap's own: a press on it, and a drag held from it, move the view (Part 10d).
            if (left && !overControl && OverMinimap(pointer, out Vec2 onMap))
            {
                _minimapHeld = true;
                _canvasFocused = true;
                CentreOnMap(onMap);
            }
            else if (_minimapHeld)
            {
                if (!mouse.leftButton.isPressed)
                    _minimapHeld = false;
                else if (MapPoint(pointer, out Vec2 heldOn))
                    CentreOnMap(heldOn);
            }
            else if ((left || right || middle) && !overControl && !OverFit(pointer) && InView(pointer, out Vector2 inView))
            {
                _canvasFocused = true;

                Vec2 stage = StagePoint(pointer);
                CanvasTarget target = _editor.TargetAt(stage, _viewState.Zoom);
                var press = new CanvasPress
                {
                    Button = left ? 0 : right ? 1 : 2,
                    Window = inView.ToStage(),
                    Board = _editor.ToBoard(stage),
                    Target = target,
                    OnSelected = _editor.Selection.Has(target),
                    InHand = _editor.InHand != null,
                    Drafting = _editor.Drafting,
                    Time = UnityEngine.Time.timeAsDouble,   // the game's clock, as the waveform's double-click is counted on
                    Shift = shift,
                    Ctrl = ctrl,
                    Alt = alt,
                    Space = space,
                };

                _canvasPointer.Press(press, _canvasCommands);
                _canvasLast = pointer;
            }
            else if (_canvasPointer.Held)
            {
                Vec2 stage = StagePoint(pointer);
                if (pointer != _canvasLast)
                {
                    _canvasPointer.Move(ViewPoint(pointer).ToStage(), _editor.ToBoard(stage), alt, _canvasCommands);
                    _canvasLast = pointer;
                }

                if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed)
                    _canvasPointer.Release(_editor.ToBoard(stage), _editor.TargetAt(stage, _viewState.Zoom), alt, _canvasCommands);
            }

            for (int i = 0; i < _canvasCommands.Count; i++)
                DoCanvas(_canvasCommands[i], space);
            _canvasCommands.Clear();

            // The part in hand, and the wire being drawn, follow the pointer while it is on the circuit.
            _editor.Point(InView(pointer, out _) && !overControl ? StagePoint(pointer) : (Vec2?)null, alt, _viewState.Zoom);
            ShowInHand();
        }

        /// <summary>What a gesture on the canvas asks for: the view's own -- a pan, a wire's signal -- or the editor's.</summary>
        private void DoCanvas(CanvasCommand command, bool space)
        {
            switch (command.Gesture)
            {
                case CanvasGesture.Pan:
                    if (space)
                        _spacePanned = true;
                    if (_viewState.Pan(command.Pan))
                        ApplyView();
                    return;

                case CanvasGesture.AddWireSignal:
                    AddWire();
                    return;

                case CanvasGesture.Menu:
                    _editor.Do(command);
                    if (Mouse.current != null)
                        _canvasMenu.Open(Mouse.current.position.ReadValue(), _editor.Selection.Parts.Count, _editor.Selection.Wires.Count, _clip != null && !_clip.IsEmpty);
                    return;

                case CanvasGesture.Inspect:
                    _editor.Do(command);
                    OpenInspector(command.Target, Mouse.current != null ? Mouse.current.position.ReadValue() + new Vector2(16f, -16f) : ScreenPointOf(command.At));
                    return;

                default:
                    EditResult? result = _editor.Do(command);
                    if (result.HasValue)
                        Said(result.Value);
                    return;
            }
        }

        // -----------------------------------------------------------------
        // The inspector (Part 10c)
        // -----------------------------------------------------------------

        /// <summary>The inspector of <paramref name="target"/>, a part or a wire, its corner at <paramref name="screenPoint"/>.</summary>
        private void OpenInspector(CanvasTarget target, Vector2 screenPoint)
        {
            Inspection inspection = BitSorter.ViewModel.Inspector.Of(_session.Project, _session.Module, target);
            if (inspection != null)
                _inspector.Open(inspection, screenPoint);
        }

        /// <summary>Enter: the inspector of the one part or wire selected on the circuit, beside it; nothing with none, or more.</summary>
        private void InspectSelected()
        {
            if (_editor == null || UiEscape.AnyHolds || !_canvasFocused)
                return;
            CanvasTarget single = _editor.Single;
            if (single.Kind == CanvasTargetKind.Nothing)
                return;

            BoardPoint at = default;
            BitSorter.Board.Module top = _session.Project.Find(_session.Module);
            if (single.Kind == CanvasTargetKind.Part)
                at = top.Layout.PlacementOf(single.Part).Position;
            else if (BitSorter.Board.WireGeometry.Wires(_session.Project, top).TryGetValue(single.Wire, out var points) && points.Count > 0)
                at = BitSorter.Board.WireGeometry.PointAt(points, BitSorter.Board.WireGeometry.Length(points) / 2);
            OpenInspector(single, ScreenPointOf(at) + new Vector2(24f, -24f));
        }

        /// <summary>
        /// What the inspector's fields say, made as one step and replayed: null when it was made, or the words it was
        /// refused with. A net or a port renamed keeps its row in the waveform, under its new name.
        /// </summary>
        private string ApplyInspector(Inspection inspection, Dictionary<string, string> typed)
        {
            Project before = _session.Project;
            EditResult result = _session.Inspect(inspection, typed);
            if (!result.Done)
                return result.Refusal;

            foreach ((string from, string to) in BitSorter.ViewModel.Inspector.NetsRenamed(before, _session.Project, _session.Module))
            {
                _list.Rekey(from, to);
                for (int i = 0; i < _portKeys.Count; i++)
                    if (_portKeys[i] == from)
                        _portKeys[i] = to;
            }

            return null;
        }

        // -----------------------------------------------------------------
        // Copy, paste, the canvas's menu and the minimap (Part 10d)
        // -----------------------------------------------------------------

        /// <summary>Ctrl+C: the parts selected and the wires between them, kept for a paste -- into this circuit or another sample's.</summary>
        private void CopySelected()
        {
            Clip clip = _editor.Copy();
            if (clip != null)
                _clip = clip;
            else
                Said(EditResult.Refused(_editor.Refusal));
        }

        /// <summary>Ctrl+X: copied, and taken away.</summary>
        private void CutSelected()
        {
            Clip clip = _editor.Cut();
            if (clip != null)
                _clip = clip;
            else
                Said(EditResult.Refused(_editor.Refusal ?? "Select a part to cut."));
        }

        /// <summary>What an item of the canvas's menu does.</summary>
        private void CanvasMenuDo(CanvasMenuAction action)
        {
            switch (action)
            {
                case CanvasMenuAction.AlignLeft: CanvasEdit(_editor.Arrange(ArrangeBy.Left)); break;
                case CanvasMenuAction.AlignRight: CanvasEdit(_editor.Arrange(ArrangeBy.Right)); break;
                case CanvasMenuAction.AlignTop: CanvasEdit(_editor.Arrange(ArrangeBy.Top)); break;
                case CanvasMenuAction.AlignBottom: CanvasEdit(_editor.Arrange(ArrangeBy.Bottom)); break;
                case CanvasMenuAction.CentresAcross: CanvasEdit(_editor.Arrange(ArrangeBy.CentresAcross)); break;
                case CanvasMenuAction.CentresDown: CanvasEdit(_editor.Arrange(ArrangeBy.CentresDown)); break;
                case CanvasMenuAction.SpreadAcross: CanvasEdit(_editor.Arrange(ArrangeBy.SpreadAcross)); break;
                case CanvasMenuAction.SpreadDown: CanvasEdit(_editor.Arrange(ArrangeBy.SpreadDown)); break;
                case CanvasMenuAction.Turn: CanvasEdit(_editor.Turn(1)); break;
                case CanvasMenuAction.Mirror: CanvasEdit(_editor.Mirror()); break;
                case CanvasMenuAction.RouteAgain: CanvasEdit(_editor.AutoRoute()); break;
                case CanvasMenuAction.Copy: CopySelected(); break;
                case CanvasMenuAction.Cut: CutSelected(); break;
                case CanvasMenuAction.Paste: CanvasEdit(_editor.Paste(_clip)); break;
                case CanvasMenuAction.Delete: CanvasEdit(_editor.Delete()); break;
            }
        }

        /// <summary>The minimap's layer, in the window's bottom-right corner, over the circuit.</summary>
        private void BuildMinimap()
        {
            _minimap = Layer("minimap", _view, WorkbenchGraphic.Layer.Minimap, Vector2.zero, Vector2.one);
            _minimap.gameObject.SetActive(false);
            PlaceMinimap();
        }

        /// <summary>The minimap drawn for the view as it is now, sized to it, and shown only while the view is not the fit.</summary>
        private void PlaceMinimap()
        {
            if (_minimap == null || _viewState == null)
                return;
            Minimap map = BitSorter.ViewModel.Minimap.Of(_viewState);
            _minimap.Map = map;
            _minimap.gameObject.SetActive(map != null);
            if (map == null)
                return;

            var size = new Vector2(map.Size.x + 2f * WorkbenchDrawing.MinimapPad, map.Size.y + 2f * WorkbenchDrawing.MinimapPad);
            TopLeft((RectTransform)_minimap.transform, ViewSize - size - new Vector2(8f, 8f), size);
            _minimap.SetVerticesDirty();
        }

        /// <summary>Whether <paramref name="screenPoint"/> is on the minimap, and where on it, in the map's units.</summary>
        private bool OverMinimap(Vector2 screenPoint, out Vec2 onMap)
        {
            onMap = default;
            return _minimap != null && _minimap.gameObject.activeSelf && _minimap.Map != null &&
                   RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_minimap.transform, screenPoint, null) &&
                   MapPoint(screenPoint, out onMap);
        }

        /// <summary>A point of the screen in the minimap's units, inside it or not.</summary>
        private bool MapPoint(Vector2 screenPoint, out Vec2 onMap)
        {
            onMap = default;
            if (_minimap == null || _minimap.Map == null)
                return false;
            var rect = (RectTransform)_minimap.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, null, out Vector2 local))
                return false;
            onMap = new Vec2(local.x - rect.rect.xMin - WorkbenchDrawing.MinimapPad, rect.rect.yMax - local.y - WorkbenchDrawing.MinimapPad);
            return true;
        }

        /// <summary>The view moved so that the window's middle is where the minimap was pressed.</summary>
        private void CentreOnMap(Vec2 onMap)
        {
            Minimap map = _minimap?.Map;
            if (map != null && map.CentreOn(_viewState, onMap))
                ApplyView();
        }

        /// <summary>An edit of the selection made from a key: its refusal said, if it was one.</summary>
        private void CanvasEdit(EditResult result) => Said(result);

        /// <summary>Delete: the circuit's selection when the circuit was clicked last and something on it is selected, else the waveform's rows.</summary>
        private void RemoveSelected()
        {
            if (_canvasFocused && !_editor.Selection.IsEmpty)
                CanvasEdit(_editor.Delete());
            else
                RemoveRows();
        }

        /// <summary>Escape on the canvas: a part in hand put down, a wire being drawn let go, or a drag. False when there was none, and Escape is the workbench's.</summary>
        private bool LetGoOnCanvas()
        {
            if (_editor == null)
                return false;
            if (_editor.InHand != null)
            {
                _editor.PutDown();
                return true;
            }

            if (_editor.Drafting)
            {
                _canvasPointer.Abandon();
                _editor.AbandonWire();
                return true;
            }

            if (_canvasPointer.Held && (_canvasPointer.Moving || _canvasPointer.Dragging))
            {
                _canvasPointer.Abandon();
                _editor.Abandon();
                return true;
            }

            return false;
        }

        /// <summary>A point of the screen on the schematic's drawing, in its own units, y down -- inside the window or not.</summary>
        private Vec2 StagePoint(Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_schematic, screenPoint, null, out Vector2 local);
            return new Vec2(local.x, -local.y);
        }

        /// <summary>A point of the screen in the circuit's window, y down -- inside it or not: what a drag is measured in.</summary>
        private Vector2 ViewPoint(Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_view, screenPoint, null, out Vector2 local);
            return new Vector2(local.x, -local.y);
        }

        // -----------------------------------------------------------------
        // The overlay, and the drawing made again
        // -----------------------------------------------------------------

        /// <summary>The editing layer, over everything drawn in the circuit's window.</summary>
        private void BuildOverlay(SchematicLayout layout)
        {
            _overlay = Layer("edit", _schematic, WorkbenchGraphic.Layer.Edit, Vector2.zero, layout.Size.ToUnity());
            _overlay.Overlay = _overlayData;
            _overlayVersion = -1;
        }

        /// <summary>The overlay drawn again, when what it shows has changed.</summary>
        private void DrawOverlay()
        {
            if (_overlay == null || _editor == null || _editor.Version == _overlayVersion)
                return;
            _overlayVersion = _editor.Version;
            _editor.Overlay(_overlayData);
            _overlay.SetVerticesDirty();
        }

        /// <summary>
        /// An edit drew the run again (<see cref="WorkbenchSession.LayoutVersion"/>): the panel follows the run's new
        /// model, keeps everything where it was on screen, and builds the stage round it.
        /// </summary>
        private void Relaid()
        {
            _drawnLayout = _session.LayoutVersion;
            SchematicLayout.Frame before = _model.Layout.Board;
            _model = _run.Model;
            _playback.Refresh();
            KeepInPlace(before);
            _appliedSignals = new int[0];
            ApplyRows();
            RebuildStage(keepViews: true);
        }

        /// <summary>The view told the drawing changed under it, and by how much its board moved: nothing moves on screen.</summary>
        private void KeepInPlace(SchematicLayout.Frame before)
        {
            _drawnLayout = _session.LayoutVersion;
            SchematicLayout.Frame after = _model.Layout.Board;
            _viewState?.Relayout(_model.Layout.Size, before != null && after != null ? after.ShiftFrom(before) : default);
            _editor?.Relaid();
        }

        /// <summary>The waveform's rows following the ports through an edit, once a run that has them has taken over.</summary>
        private void FollowPorts()
        {
            List<string> now = PortKeys(_model);
            _list.FollowPorts(_portKeys, now);
            _portKeys = now;
        }

        /// <summary>The keys of the ports a model's run has, in the order the layout lists them.</summary>
        private static List<string> PortKeys(WorkbenchModel model)
        {
            var keys = new List<string>();
            if (model.Signals == null)
                return keys;
            foreach (WaveRow row in model.Layout.Rows)
                if (model.Session.Design.TryFind(row.Signal, out SignalId id))
                    keys.Add(model.Signals.KeyOf(id.Index));
            return keys;
        }
    }
}
