using System.Collections.Generic;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The workbench's docked layout, in the shell (Part 11b): the parts panel down the left, the circuit's window, the
    /// side panel down the right with the minimap at its foot, the splitter, the dock with the waveform's buttons over
    /// the waves and the legend at its foot, and the status line along the bottom -- each where
    /// <see cref="ShellLayout"/> says, from the window's size and the dock's height.
    /// </summary>
    /// <remarks>
    /// **The shell draws the panels and the drawings stay uGUI** (the plan's decision): the circuit, the waves, the
    /// legend's swatches and the minimap are placed under the panels that frame them (<c>PlaceStage</c>), at their own
    /// size, so the stage is no longer one card scaled to the window, and nothing on it is drawn smaller in a small
    /// window. **A panel's middle takes no clicks**, so a press on the circuit or the waves goes through the shell to
    /// them; only its buttons and the splitter take one.
    ///
    /// **The splitter is dragged**, as an editor's is: the dock's height follows the pointer within what the window has
    /// room for (<see cref="ShellLayout.ClampDock"/>), and is kept on release (<see cref="DockKey"/>). Nothing is built
    /// again for it: the circuit's window and the waves' are made another size, and a circuit fitted to its window is
    /// fitted again (<see cref="SchematicView.Resize"/>).
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The shell's panels' names, which the tests look for.</summary>
        public const string PartsPanelName = "Workbench parts", CircuitPanelName = "Workbench circuit", SidePanelName = "Workbench side",
            SplitterName = "Workbench splitter", DockName = "Workbench dock", StatusName = "Workbench status", WavesTabName = "Workbench waves tab";

        /// <summary>The setting the dock's height is kept under, in whole units of the window.</summary>
        public const string DockKey = "workbench.dock";

        /// <summary>How round a panel's corners are, and how far its contents stand in from its edge.</summary>
        private const float PanelCorner = 12f, PanelPad = 10f;

        /// <summary>The parts panel's buttons: how tall, the gap between two, the room a heading takes, and how many to a row.</summary>
        private const float PartRowHeight = 28f, PartGap = 4f, PartHeadingHeight = 24f;
        private const int PartColumns = 3;

        private VisualElement _partsPanel, _partsClip, _partsList, _partsThumb, _circuitPanel, _sidePanel, _splitter, _grip, _dockPanel;
        private float _partsHeight, _partsScroll;
        private Label _status;
        private ShellButton _namesButton, _unitButton;
        private readonly List<(CatalogEntry Entry, ShellButton Button)> _partButtons = new List<(CatalogEntry, ShellButton)>();
        private CatalogEntry _shownInHand;

        private ShellRegions _regions;
        private Vector2 _laidWindow = new Vector2(-1f, -1f);

        /// <summary>The dock's height as the student last dragged it, or under 0 for none yet (<see cref="ShellLayout.DefaultDockFor"/>); and as last laid out.</summary>
        private float _dockHeight = -1f, _laidDock = -1f;

        /// <summary>A drag on the splitter: the pointer it is, where it started, and the dock's height then; -1 for none.</summary>
        private int _splitterPointer = -1;
        private float _splitterFrom, _splitterDockFrom;

        /// <summary>Where every panel is now.</summary>
        public ShellRegions Regions => _regions;

        /// <summary>The window's size, in the canvas's units: what the shell's panels are laid out in.</summary>
        private Vec2 Window => new Vec2(Root.rect.width, Root.rect.height);

        /// <summary>Makes the dock <paramref name="height"/> tall, as far as the window has room, as a drag on the splitter does.</summary>
        public void SetDockHeight(float height)
        {
            if (Root == null)
                return;
            _dockHeight = ShellLayout.ClampDock(height, Window, StageTop);
            Fit();
        }

        // -----------------------------------------------------------------
        // Building the panels
        // -----------------------------------------------------------------

        private void BuildDock(SchematicPalette palette)
        {
            _dockHeight = Preferences.GetInt(DockKey, -1);

            _partsPanel = Panel(PartsPanelName, palette);
            _circuitPanel = Panel(CircuitPanelName, palette);
            _sidePanel = Panel(SidePanelName, palette);
            _dockPanel = Panel(DockName, palette);
            BuildDockHeader(palette);
            BuildTranscriptList(palette);
            BuildTimingList(palette);
            BuildInputsList();
            BuildLogicList();
            BuildStatesList();
            ShowDockTab(DockTab.Waves);
            BuildSplitter(palette);

            // One line along the bottom: the gestures where the pointer is, and ? for every key (Part 11d).
            _status = ShellTheme.Label(StatusName, KeyMap.StatusFor(PointerArea.Elsewhere), UiTheme.HelpLineType, palette.TextDim,
                TextAnchor.MiddleCenter);
            ShellTheme.Ellipsis(_status);
            _shell.Root.Add(_status);
        }

        /// <summary>Where the pointer was when the status line last named its gestures.</summary>
        private PointerArea _statusArea = PointerArea.Elsewhere;

        /// <summary>The status line naming the gestures of the circuit, the waves or a BLOCKS row in PARTS, whichever the pointer is over, and ? for every key.</summary>
        private void ShowStatus(Vector2 pointer)
        {
            PointerArea area = InView(pointer, out _) ? (Editor != null && Editor.PinOver.Kind == CanvasTargetKind.Pin ? PointerArea.Pin : PointerArea.Circuit)
                : _wave != null && _wavesClip != null && _wavesClip.gameObject.activeSelf && _wave.RegionAt(pointer, out _, out _, out _)
                    ? PointerArea.Waves
                    : BlockRowAt(pointer) != null ? PointerArea.Blocks
                    : PointerArea.Elsewhere;
            if (area == _statusArea)
                return;
            _statusArea = area;
            SetText(_status, KeyMap.StatusFor(area));
        }

        /// <summary>What the status line says now.</summary>
        public string StatusText => _status?.text;

        /// <summary>A panel's frame: a thin rounded edge round nothing, so the drawing under it shows and a press goes through.</summary>
        private VisualElement Panel(string name, SchematicPalette palette)
        {
            var panel = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            ShellTheme.Outline(panel, palette.CardEdge, Color.clear, 1f, PanelCorner);
            panel.style.position = Position.Absolute;
            panel.style.overflow = Overflow.Hidden;
            _shell.Root.Add(panel);
            return panel;
        }

        /// <summary>
        /// The dock's header: its tab, and the waveform's buttons -- + SIGNAL, GROUP, NAMES, UNIT and EXPORT -- each with its
        /// key beside it, as they stood over the waveform until Part 11b.
        /// </summary>
        private void BuildDockHeader(SchematicPalette palette)
        {
            VisualElement header = ShellTheme.Box("header", 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            header.style.right = 0f;
            _dockPanel.Add(header);

            // WAVES and TRANSCRIPT (Part 11c), CODE (12e), TIMING (14b), INPUTS, LOGIC and STATES (15g), and each one's tools beside them.
            BuildDockTabs(header, palette);

            var line = new VisualElement { name = "rule", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(line, 0f, ShellLayout.DockHeaderHeight - 1f, -1f, 1f);
            line.style.right = 0f;
            line.style.backgroundColor = palette.CardEdge;
            header.Add(line);

            const float height = 34f;
            float x = ToolsLeft + KeyRoom;
            ShellButton Add(string name, string caption, WorkbenchCommand command, float width)
            {
                ShellButton button = ShellTheme.Button(name, caption, palette, () => Do(command));
                ShellTheme.Place(button, x, 5f, width, height);
                ShellTheme.KeyBeside(button, height, WorkbenchKeys.For(command).Cap, palette);
                _wavesTools.Add(button);
                x += width + 12f + KeyRoom;
                return button;
            }

            Add(WaveformView.AddButton, "+ SIGNAL", WorkbenchCommand.AddSignal, 130f);
            Add(WaveformView.GroupButton, "GROUP", WorkbenchCommand.Group, 100f);
            _namesButton = Add(WaveformView.NamesButton, NamesCaption(WaveNames.Short), WorkbenchCommand.Names, 170f);

            // The unit as SystemVerilog writes it, in lower case: MS would be megaseconds (Part 9b-3).
            _unitButton = Add(WaveformView.UnitButton, UnitCaption(_model.Unit), WorkbenchCommand.Unit, 120f);

            // The run as a file: a VCD for GTKWave, or the waveform as a picture for a report (Part 9b-4).
            Add(WaveformView.ExportButton, "EXPORT", WorkbenchCommand.Export, 110f);
        }

        private static string NamesCaption(WaveNames names) => names == WaveNames.Full ? "NAMES: FULL" : "NAMES: SHORT";

        private static string UnitCaption(string unit) => "UNIT: " + unit;

        /// <summary>The words on the dock's buttons that follow the rows and the run: NAMES and UNIT.</summary>
        private void RefreshDockButtons()
        {
            SetText(_namesButton, NamesCaption(_list.Names));
            SetText(_unitButton, UnitCaption(_model.Unit));
        }

        /// <summary>
        /// The parts panel's buttons, under their headings, three to a row and a block to a row of its own: every part the
        /// circuit can be built from. Under its title they scroll, a row a notch of the wheel, when the panel is shorter
        /// than they are -- a short window, or a project of many modules.
        /// </summary>
        private void BuildPartsPanel(SchematicPalette palette)
        {
            NotePartsBuilt();
            _partsPanel.Clear();
            _partButtons.Clear();
            _shownInHand = null;

            // The entries' own layer, which takes a turn of the wheel anywhere over it, between the buttons included.
            _partsList = new VisualElement { name = "list" };
            _partsList.RegisterCallback<WheelEvent>(e =>
            {
                if (ScrollParts(e.delta.y > 0f ? PartRowHeight + PartGap : -(PartRowHeight + PartGap)))
                    e.StopPropagation();
            });

            // Their window, from the tabs' foot down: what scrolls past its top goes under the tabs, never over them --
            // the tabs are see-through, and the buttons showed through them (a playtest, 2026-10-26).
            _partsClip = ClipUnderTheTabs("list clip");
            _partsClip.Add(_partsList);
            _partsPanel.Add(_partsClip);

            float inside = ShellLayout.PartsWidth - 2f * PanelPad;
            float column = (inside - (PartColumns - 1) * PartGap) / PartColumns;
            float y = 0f;
            int placed = 0;
            CatalogGroup? group = null;

            // Inside written code there is nothing to place: its text is the block (Part 12d), and its drawing is the text's (15h-4).
            if (_session.ShowsWritten)
            {
                Label note = ShellTheme.Label(CodeNoteName, _session.ShowsDrawingOfCode
                        ? "This drawing is what the code builds: what you place, wire or delete on it is written into the code. CODE shows the text; Alt+Up steps out."
                        : "A written block has no parts: its code is what it does. Type it on the right; Alt+Up steps out.",
                    UiType.Caption, palette.TextDim);
                note.style.whiteSpace = WhiteSpace.Normal;
                ShellTheme.Place(note, PanelPad, y, inside, PartRowHeight * 4f);
                _partsList.Add(note);
                y += PartRowHeight * 4f + PartGap;
            }

            // + CODE, a block written as SystemVerilog rather than drawn (Part 12d), takes MORE's last cell: named first, then
            // made from a template header and put in hand like any part -- and in a cell of the grid PARTS grows no taller.
            void PlaceCodeCell()
            {
                ShellButton code = ShellTheme.Button(NewCodeBlockName, "+ CODE", palette, () => AskCodeBlockName(), UiType.Caption);
                ShellTheme.Place(code, PanelPad + (placed % PartColumns) * (column + PartGap), y, column, PartRowHeight);
                _partsList.Add(code);
                placed++;
                if (placed % PartColumns == 0)
                    y += PartRowHeight + PartGap;
            }

            // Over the drawing of code, its parts to place on it (Part 15h-4g); over the code itself, none.
            foreach (CatalogEntry entry in _session.ShowsWritten && !_session.ShowsDrawingOfCode ? new CatalogEntry[0] : PartsCatalog.For(_session.DrawnProject, _session.Module))
            {
                if (entry.Group != group && group == CatalogGroup.More)
                    PlaceCodeCell();

                if (entry.Group != group)
                {
                    if (placed % PartColumns != 0)
                        y += PartRowHeight + PartGap;
                    placed = 0;
                    group = entry.Group;

                    Label heading = ShellTheme.Label("heading", GroupHeading(entry.Group), UiType.Micro, palette.TextDim);
                    ShellTheme.Place(heading, PanelPad, y + 2f, -1f, PartHeadingHeight - 4f);
                    _partsList.Add(heading);
                    y += PartHeadingHeight;
                }

                // A block's name is a module's, and long: it has a row to itself.
                bool block = entry.Group == CatalogGroup.Blocks;
                CatalogEntry chosen = entry;
                ShellButton button = ShellTheme.Button(PartButtonName(entry.Key), PartsCatalog.Caption(_session.Project, entry), palette,
                    () => _editor.Take(chosen), UiType.Caption);
                ShellTheme.Ellipsis(button);
                ShellTheme.Place(button, PanelPad + (block ? 0f : (placed % PartColumns) * (column + PartGap)), y,
                    block ? inside : column, PartRowHeight);
                _partsList.Add(button);
                _partButtons.Add((entry, button));

                placed = block ? 0 : placed + 1;
                if (block || placed % PartColumns == 0)
                    y += PartRowHeight + PartGap;
            }

            if (group == CatalogGroup.More)
                PlaceCodeCell();

            _partsHeight = placed % PartColumns != 0 ? y + PartRowHeight : y - PartGap;
            ShellTheme.Place(_partsList, 0f, 0f, -1f, _partsHeight);
            _partsList.style.right = 0f;

            _partsThumb = new VisualElement { name = "scroll", pickingMode = PickingMode.Ignore };
            _partsThumb.style.position = Position.Absolute;
            _partsThumb.style.backgroundColor = palette.Muted;
            _partsPanel.Add(_partsThumb);
            // The scroll is kept, and clamped to the new list as its tab shows it (ScrollParts): a module deleted from
            // BLOCKS, at the list's foot, put the list back at its top every time (the 14g review).

            // PARTS and DESIGN (Part 11c), over the entries and the hierarchy.
            BuildLeftTabs(palette);
        }

        /// <summary>Where the parts panel's entries, and the hierarchy's rows, start: under its tabs.</summary>
        private const float PartsTop = 6f + PartHeadingHeight + 10f;

        /// <summary>How far the parts panel's entries are scrolled, and how far they can be.</summary>
        public float PartsScroll => _partsScroll;

        /// <inheritdoc cref="PartsScroll"/>
        public float PartsOverflow => Mathf.Max(0f, PartsTop + _partsHeight + PanelPad - _regions.Parts.height);

        /// <summary>Moves the parts panel's entries by <paramref name="units"/>, within what they can scroll, and its bar with them. True when they moved.</summary>
        private bool ScrollParts(float units)
        {
            if (_partsList == null)
                return false;

            float overflow = PartsOverflow;
            float scroll = Mathf.Clamp(_partsScroll + units, 0f, overflow);
            bool moved = !Mathf.Approximately(scroll, _partsScroll);
            _partsScroll = scroll;
            _partsList.style.top = -_partsScroll;
            PlaceUnderTheTabs(_partsClip);

            _partsThumb.style.display = overflow > 0.5f && !_designTab ? DisplayStyle.Flex : DisplayStyle.None;
            if (overflow > 0.5f)
            {
                float room = _regions.Parts.height - PartsTop - PanelPad;
                float thumb = Mathf.Max(24f, room * room / (room + overflow));
                ShellTheme.Place(_partsThumb, ShellLayout.PartsWidth - 6f, PartsTop + (room - thumb) * (_partsScroll / overflow), 3f, thumb);
            }

            return moved;
        }

        /// <summary>A window for a list in the parts panel that clips it at the tabs' foot, and takes no clicks of its own.</summary>
        private static VisualElement ClipUnderTheTabs(string name)
        {
            var clip = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            clip.style.position = Position.Absolute;
            clip.style.overflow = Overflow.Hidden;
            return clip;
        }

        /// <summary>A list's window from the tabs' foot to the parts panel's.</summary>
        private void PlaceUnderTheTabs(VisualElement clip)
        {
            if (clip == null)
                return;
            ShellTheme.Place(clip, 0f, PartsTop, ShellLayout.PartsWidth, Mathf.Max(0f, _regions.Parts.height - PartsTop));
        }

        private static string GroupHeading(CatalogGroup group)
        {
            switch (group)
            {
                case CatalogGroup.Ports: return "PORTS";
                case CatalogGroup.Gates: return "GATES";
                case CatalogGroup.More: return "MORE";
                default: return "BLOCKS";
            }
        }

        /// <summary>The module of the BLOCKS row under a point of the screen, or null: what a right-click there would delete.</summary>
        private string BlockRowAt(Vector2 screenPoint)
        {
            if (_shell == null || _partButtons.Count == 0)
                return null;
            VisualElement picked = _shell.PickAt(screenPoint);
            for (VisualElement e = picked; e != null; e = e.parent)
                foreach ((CatalogEntry entry, ShellButton button) in _partButtons)
                    if (e == button)
                        return entry.Kind == CatalogKind.Block ? entry.Module : null;
            return null;
        }

        /// <summary>
        /// A right-click on a BLOCKS row in PARTS: the question whether to delete its module, at the pointer -- or, while
        /// something uses it, why it cannot be, in the bar's line. A block's module stays when its last block goes, since it
        /// is the player's work, and this is how it goes (the 14g report: code1 stayed in CODE and PARTS).
        /// </summary>
        private void TakeBlockRowMenu(UnityEngine.InputSystem.Mouse mouse, Vector2 pointer)
        {
            if (!mouse.rightButton.wasPressedThisFrame || _ask == null)
                return;
            string module = BlockRowAt(pointer);
            if (module == null)
                return;

            string why = _session.WhyNotRemoveModule(module);
            if (why != null)
            {
                Notice(why);
                return;
            }

            bool written = _session.Project.Find(module)?.IsWritten == true;
            _ask.Ask(pointer, $"Delete the module {module}? Its {(written ? "code" : "drawing")} goes with it; Ctrl+Z brings it back.",
                "DELETE " + module, () => RemoveModule(module));
        }

        /// <summary>The module <paramref name="module"/> taken out, the bar's line saying so or why not.</summary>
        public BitSorter.Board.EditResult RemoveModule(string module)
        {
            BitSorter.Board.EditResult result = _session.RemoveModule(module);
            if (result.Done)
                Notice($"deleted the module {module}: Ctrl+Z brings it back");
            else
                Said(result);
            return result;
        }

        /// <summary>The panel's button for the part in hand in the accent, the rest as they were.</summary>
        private void ShowInHand()
        {
            if (_editor.InHand == _shownInHand)
                return;
            _shownInHand = _editor.InHand;

            SchematicPalette palette = SchematicPalette.Current;
            foreach ((CatalogEntry entry, ShellButton button) in _partButtons)
                button.style.color = entry == _shownInHand ? palette.Accent : palette.Text;
        }

        // -----------------------------------------------------------------
        // The splitter
        // -----------------------------------------------------------------

        private void BuildSplitter(SchematicPalette palette)
        {
            _splitter = new VisualElement { name = SplitterName };
            _splitter.AddToClassList(ShellTheme.ControlClass);
            _splitter.style.position = Position.Absolute;

            _grip = new VisualElement { name = "grip", pickingMode = PickingMode.Ignore };
            _grip.style.position = Position.Absolute;
            _grip.style.left = new Length(50f, LengthUnit.Percent);
            _grip.style.marginLeft = -28f;
            _grip.style.width = 56f;
            _grip.style.height = 4f;
            _grip.style.top = (ShellLayout.SplitterHeight - 4f) * 0.5f;
            _grip.style.borderTopLeftRadius = _grip.style.borderTopRightRadius =
                _grip.style.borderBottomLeftRadius = _grip.style.borderBottomRightRadius = 2f;
            _splitter.Add(_grip);
            Grip(lit: false);

            _splitter.RegisterCallback<PointerEnterEvent>(_ => Grip(lit: true));
            _splitter.RegisterCallback<PointerLeaveEvent>(_ => Grip(lit: _splitterPointer >= 0));
            _splitter.RegisterCallback<PointerDownEvent>(SplitterDown);
            _splitter.RegisterCallback<PointerMoveEvent>(SplitterMove);
            _splitter.RegisterCallback<PointerUpEvent>(SplitterUp);
            _splitter.RegisterCallback<PointerCaptureOutEvent>(_ => SplitterLetGo());
            _shell.Root.Add(_splitter);
        }

        /// <summary>The grip in the text's colour while the splitter is pointed at or dragged, quiet otherwise.</summary>
        private void Grip(bool lit)
        {
            SchematicPalette palette = SchematicPalette.Current;
            _grip.style.backgroundColor = lit ? palette.Text : palette.Muted;
        }

        private void SplitterDown(PointerDownEvent e)
        {
            if (e.button != 0)
                return;
            _splitterPointer = e.pointerId;
            _splitterFrom = e.position.y;
            _splitterDockFrom = _regions.Dock.height;
            _splitter.CapturePointer(e.pointerId);
            Grip(lit: true);
            e.StopPropagation();
        }

        private void SplitterMove(PointerMoveEvent e)
        {
            if (e.pointerId != _splitterPointer || !_splitter.HasPointerCapture(e.pointerId))
                return;

            // Up is taller: the dock's top follows the pointer.
            SetDockHeight(_splitterDockFrom + (_splitterFrom - e.position.y));
        }

        private void SplitterUp(PointerUpEvent e)
        {
            if (e.pointerId != _splitterPointer)
                return;
            if (_splitter.HasPointerCapture(e.pointerId))
                _splitter.ReleasePointer(e.pointerId);
            SplitterLetGo();
        }

        /// <summary>A drag on the splitter ended, however it ended: the dock's height is kept.</summary>
        private void SplitterLetGo()
        {
            if (_splitterPointer < 0)
                return;
            _splitterPointer = -1;
            Grip(lit: false);
            Preferences.SetInt(DockKey, Mathf.RoundToInt(_regions.Dock.height));
        }

        // -----------------------------------------------------------------
        // Laying the panels out
        // -----------------------------------------------------------------

        /// <summary>
        /// The panels laid out for the window as it is now and the dock's height, if either has changed since: the shell's
        /// panels moved, and the drawings under them placed again -- or, the waves another width, made again.
        /// </summary>
        private void Fit()
        {
            if (Root == null)
                return;

            Vector2 window = Root.rect.size;
            float dock = _dockHeight >= 0f ? _dockHeight : ShellLayout.DefaultDockFor(Window, StageTop);
            if (window == _laidWindow && dock == _laidDock)
                return;
            _laidWindow = window;
            _laidDock = dock;
            _regions = ShellLayout.Of(Window, StageTop, dock);
            PlaceShell();

            if (_stage == null)
                return;
            if (!Mathf.Approximately(_regions.Waves.width, _wavesWidth))
                RebuildStage(keepViews: true);
            else
                PlaceStage();
        }

        /// <summary>The shell's panels where the regions say, and the toolbar across the window.</summary>
        private void PlaceShell()
        {
            Place(_inspectorBar, _regions.Inspector);
            LayoutInspectorBar();
            Place(_partsPanel, _regions.Parts);
            Place(_circuitPanel, _regions.Circuit);
            Place(_sidePanel, _regions.Side);
            Place(_splitter, _regions.Splitter);
            Place(_dockPanel, _regions.Dock);
            Place(_status, _regions.Status);
            ScrollParts(0f);
            ScrollHierarchy(0f);
            PlaceTranscript();
            PlaceTiming();
            PlaceInputs();
            PlaceLogic();
            PlaceStates();
            FitToolbar(Window.x);
        }

        private static void Place(VisualElement element, Rect2 at)
        {
            if (element != null)
                ShellTheme.Place(element, at.x, at.y, at.width, at.height);
        }

        /// <summary>A region's corner and size, for a uGUI rect placed by <see cref="TopLeft"/>.</summary>
        private static Vector2 CornerOf(Rect2 at) => new Vector2(at.x, at.y);

        /// <inheritdoc cref="CornerOf"/>
        private static Vector2 SizeOf(Rect2 at) => new Vector2(at.width, at.height);
    }
}
