using System.Collections.Generic;
using System.Globalization;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The dock's STATES tab (Part 15g), beside LOGIC: a clocked circuit's state diagram, found by running the engine from its
    /// start, breadth first (<see cref="StateExplorer"/>) -- drawn on the left (<see cref="StateDiagramView"/>), and its
    /// next-state table on the right. Ahmad asked for a state diagram (2026-10-28).
    /// </summary>
    /// <remarks>
    /// **Made while the tab is up, a few runs a frame**, and made again once the document changes; the header says how far
    /// it has got, whether the machine is Moore or Mealy, or why there is none -- no registers, no clock, too big to draw.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The tab's name, and its parts': each a name a test finds them by.</summary>
        public const string StatesTabName = "Workbench states tab", StatesSaysName = "Workbench states says",
            StatesDiagramName = "Workbench states diagram", StatesNextName = "Workbench states next ", StatesKeyName = "Workbench states key";

        private const float StatesTabLeft = LogicTabLeft + LogicTabWidth + 4f, StatesTabWidth = 90f, StatesRowHeight = 24f;

        private ShellButton _statesTabButton;
        private VisualElement _statesTools, _statesList, _statesTable, _statesContent;
        private Vector2 _statesScroll, _statesExtent;
        private StateDiagramView _statesDiagram;
        private Label _statesSays;
        private StateExplorer _states;
        private string _statesWhy;
        private BitSorter.Board.Project _statesOf;
        private bool _statesDrawn;

        /// <summary>Whether the dock shows STATES.</summary>
        public bool StatesShown => _dockTab == DockTab.States;

        /// <summary>The machine found, once it is; null until then, and when there is none.</summary>
        public StateMachine StateMachine => _states?.Machine;

        /// <summary>What the STATES tab's header says.</summary>
        public string StatesSays => _statesSays?.text;

        /// <summary>Shows STATES, as its tab does.</summary>
        public void ShowStates() => ShowDockTab(DockTab.States);

        /// <summary>Every run left now: what a test and a reference shot do.</summary>
        public void FinishStates()
        {
            RefreshStates();
            _states?.Finish();
            RefreshStates();
        }

        /// <summary>The STATES tab beside LOGIC, and the line its header says how far the exploring has got on.</summary>
        private void BuildStatesTab(VisualElement header, SchematicPalette palette)
        {
            _statesTabButton = Tab(StatesTabName, "STATES", palette, () => ShowDockTab(DockTab.States));
            ShellTheme.Place(_statesTabButton, StatesTabLeft, 6f, StatesTabWidth, TabHeight);
            header.Add(_statesTabButton);

            _statesTools = new VisualElement { name = "states tools", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_statesTools, 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _statesTools.style.right = 0f;
            header.Add(_statesTools);

            _statesSays = ShellTheme.Label(StatesSaysName, "", UiType.Caption, palette.TextDim);
            ShellTheme.Place(_statesSays, ToolsLeft, 0f, -1f, ShellLayout.DockHeaderHeight);
            _statesSays.style.right = 14f;
            ShellTheme.Ellipsis(_statesSays);
            _statesTools.Add(_statesSays);
        }

        /// <summary>The tab's list -- the diagram on the left, the table on the right -- in the dock under its header, shut until its tab is chosen.</summary>
        private void BuildStatesList()
        {
            _statesList = new VisualElement { name = "states" };
            _statesList.style.position = Position.Absolute;
            _statesList.style.overflow = Overflow.Hidden;
            _statesList.style.display = DisplayStyle.None;
            _statesDiagram = new StateDiagramView { name = StatesDiagramName };
            _statesList.Add(_statesDiagram);
            _statesTable = new VisualElement { name = "next states", pickingMode = PickingMode.Ignore };
            _statesTable.style.overflow = Overflow.Hidden;
            _statesList.Add(_statesTable);
            _statesContent = new VisualElement { name = "next states content", pickingMode = PickingMode.Ignore };
            _statesContent.style.position = Position.Absolute;
            _statesTable.Add(_statesContent);
            _statesList.RegisterCallback<WheelEvent>(e =>
            {
                // The table under the pointer scrolls, down with the wheel and across with Shift held, never past its ends: sixteen
                // states by sixteen inputs is wider and taller than the room beside the diagram.
                if (e.localMousePosition.x < _statesTable.layout.x)
                    return;
                bool across = e.shiftKey || Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y);
                float step = (across ? (e.delta.x != 0f ? e.delta.x : e.delta.y) : e.delta.y) > 0f ? 1f : -1f;
                if (across)
                    _statesScroll.x += step * 80f;
                else
                    _statesScroll.y += step * 3f * StatesRowHeight;
                ScrollStatesTable();
                e.StopPropagation();
            });
            _dockPanel.Add(_statesList);
        }

        /// <summary>The table moved to where it is scrolled, held between its ends.</summary>
        private void ScrollStatesTable()
        {
            float width = float.IsNaN(_statesTable.layout.width) ? 0f : _statesTable.layout.width;
            float height = float.IsNaN(_statesTable.layout.height) ? 0f : _statesTable.layout.height;
            _statesScroll.x = Mathf.Clamp(_statesScroll.x, 0f, Mathf.Max(0f, _statesExtent.x - width));
            _statesScroll.y = Mathf.Clamp(_statesScroll.y, 0f, Mathf.Max(0f, _statesExtent.y - height));
            _statesContent.style.left = -_statesScroll.x;
            _statesContent.style.top = -_statesScroll.y;
        }

        /// <summary>The list placed under the dock's header, where the transcript is: the diagram a little over half its width.</summary>
        private void PlaceStates()
        {
            if (_statesList == null)
                return;
            float height = Mathf.Max(0f, _regions.Dock.height - ShellLayout.DockHeaderHeight);
            ShellTheme.Place(_statesList, 0f, ShellLayout.DockHeaderHeight, _regions.Dock.width, height);
            float diagram = Mathf.Round(_regions.Dock.width * 0.55f);
            ShellTheme.Place(_statesDiagram, 0f, 0f, diagram, height);
            ShellTheme.Place(_statesTable, diagram + 10f, 0f, Mathf.Max(0f, _regions.Dock.width - diagram - 24f), height);
            _statesDrawn = false;
        }

        /// <summary>
        /// Each frame while the tab is up: the exploring started again once the document has changed, a few runs of it made, and
        /// the diagram and table drawn once it is done.
        /// </summary>
        private void RefreshStates()
        {
            if (_session == null || _statesList == null || _dockTab != DockTab.States)
                return;

            // Made again only when the design or its clock has changed: a part moved, a wire rerouted or an input's steps
            // edited leave the machine as it was (the 15g-5 review, of LOGIC restarting on every edit).
            BitSorter.Board.Project project = _session.Project;
            if (!ReferenceEquals(project, _statesOf))
            {
                bool same = _statesOf != null && TimingView.SameDesign(_statesOf, project) && ClockOf(_statesOf) == ClockOf(project);
                _statesOf = project;
                if (!same)
                {
                    _states = StateExplorer.Start(project, _session.Timing, () => UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0, out _statesWhy);
                    _statesContent.Clear();
                    _statesScroll = Vector2.zero;
                    _statesDiagram.Machine = null;
                    _statesDrawn = false;
                }
            }

            if (_states != null && !_states.Done)
            {
                _states.Frame();
                SetText(_statesSays, "Running every input from every state reached: "
                    + Mathf.FloorToInt((float)_states.Progress * 100f).ToString(CultureInfo.InvariantCulture) + "%.");
                if (!_states.Done)
                    return;
                _statesDrawn = false;
            }

            if (!_statesDrawn)
                DrawStates();
        }

        /// <summary>The input the stimulus clocks, if it clocks one: which input is the clock decides the machine.</summary>
        private static string ClockOf(BitSorter.Board.Project project)
        {
            foreach (BitSorter.Board.ClockTrack clock in project.Stimulus?.Clocks ?? System.Array.Empty<BitSorter.Board.ClockTrack>())
                return clock.Port;
            return null;
        }

        private void DrawStates()
        {
            _statesDrawn = true;
            _statesContent.Clear();
            _statesExtent = Vector2.zero;
            StateMachine machine = _states?.Machine;
            _statesDiagram.Machine = machine;
            if (machine == null)
            {
                SetText(_statesSays, _states?.Problem ?? _statesWhy ?? "");
                return;
            }

            SetText(_statesSays, machine.States.Count.ToString(CultureInfo.InvariantCulture) + " states reached from the start"
                + (machine.Cut ? " -- the first " + StateExplorer.MostStates.ToString(CultureInfo.InvariantCulture) + " drawn, the rest not explored" : "")
                + (machine.Moore
                    ? ": a Moore machine, its outputs depending on its state alone, under the bar in each."
                    : ": a Mealy machine, its outputs reading its inputs too, on each arrow after them as in/out."));

            // The next-state table: a row a state, a column an input, the next state in each -- and its outputs, a Mealy machine's
            // on each arrow, a Moore machine's in a column of its own.
            SchematicPalette palette = SchematicPalette.Current;
            int combinations = 1 << machine.InputBits.Count;
            const float stateWidth = 90f, gap = 8f;
            float cell = Mathf.Max(70f, machine.States[0].Length * 10f + (machine.Moore ? 20f : machine.OutputBits.Count * 10f + 30f));
            void Put(string name, string text, float x, float y, float width, Color colour, bool bold = false)
            {
                Label label = ShellTheme.Label(name, text, UiType.Caption, colour, bold: bold);
                ShellTheme.Place(label, x, y, width, StatesRowHeight);
                _statesContent.Add(label);
                _statesExtent = Vector2.Max(_statesExtent, new Vector2(x + width, y + StatesRowHeight + 10f));
            }

            // Which bit is which, over the table.
            float tableWidth = stateWidth + gap + (combinations + (machine.Moore ? 1 : 0)) * (cell + gap);
            Put(StatesKeyName, "state: " + string.Join(" ", machine.StateBits)
                + (machine.InputBits.Count > 0 ? "     in: " + string.Join(" ", machine.InputBits) : "     no input but the clock: one way on from each")
                + (machine.OutputBits.Count > 0 ? "     out: " + string.Join(" ", machine.OutputBits) : ""),
                0f, 10f, Mathf.Max(tableWidth, float.IsNaN(_statesTable.layout.width) ? 0f : _statesTable.layout.width - 10f), palette.TextDim);

            float y0 = 10f + StatesRowHeight + 4f;
            Put("head", "STATE", 0f, y0, stateWidth, palette.TextDim, bold: true);
            for (int c = 0; c < combinations; c++)
            {
                var input = new char[machine.InputBits.Count];
                for (int i = 0; i < input.Length; i++)
                    input[i] = (c >> (input.Length - 1 - i) & 1) != 0 ? '1' : '0';
                Put("head", machine.InputBits.Count > 0 ? "IN " + new string(input) : "NEXT", stateWidth + gap + c * (cell + gap), y0, cell,
                    palette.TextDim, bold: true);
            }
            if (machine.Moore)
                Put("head", "OUT", stateWidth + gap + combinations * (cell + gap), y0, cell, palette.TextDim, bold: true);

            for (int s = 0; s < machine.States.Count; s++)
            {
                float y = y0 + (s + 1) * StatesRowHeight;
                Put("state", (s == 0 ? "→ " : "  ") + machine.States[s], 0f, y, stateWidth, s == 0 ? palette.Accent : palette.Text);
                foreach (StateTransition t in machine.Transitions)
                {
                    if (t.From != s)
                        continue;
                    int c = 0;
                    foreach (char bit in t.Input)
                        c = c << 1 | (bit == '1' ? 1 : 0);
                    Put(StatesNextName + s + " " + c, machine.States[t.To] + (machine.Moore ? "" : " / " + t.Output),
                        stateWidth + gap + c * (cell + gap), y, cell, palette.Text);
                }
                if (machine.Moore && machine.OutputIn(s) is string output)
                    Put("out", output, stateWidth + gap + combinations * (cell + gap), y, cell, palette.Text);
            }
            ScrollStatesTable();
        }
    }
}
