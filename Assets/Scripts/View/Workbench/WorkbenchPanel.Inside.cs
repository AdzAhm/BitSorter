using System.Globalization;
using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// Stepping inside a block (Part 12a): the canvas draws and edits the module of the instance stepped into, its wires
    /// showing that instance's values, while the run and the waves' rows stay the whole design's.
    /// </summary>
    /// <remarks>
    /// - **In:** a double-click on a block (any other part's opens its inspector), STEP INSIDE in a block's quick panel, or
    ///   a double-click on an instance in the DESIGN tree. **Out:** a crumb of the breadcrumb in the circuit window's
    ///   top-left corner, or Alt+Up (Ahmad's choices, 2026-10-26; Backspace stays a second DEL).
    /// - **Editing inside edits the module**, so every instance of it changes, as in SystemVerilog, and the inspector's line
    ///   says how many there are while nothing is selected.
    /// - A step lets go of what was selected or in hand -- it was the module stepped out of -- and fits the new drawing to the
    ///   window; the waves keep their rows and their view.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The breadcrumb, and a crumb's name: this, and its depth (0 the top).</summary>
        public const string BreadcrumbName = "Workbench breadcrumb", CrumbName = "Workbench crumb ";

        /// <summary>STEP INSIDE in a block's quick panel.</summary>
        public const string StepInsideName = "Workbench step inside";

        private const float CrumbHeight = 30f, CrumbGap = 6f;

        private VisualElement _breadcrumb;
        private int _drawnStep = -1;

        /// <summary>The instances stepped through, outermost first; empty at the top.</summary>
        public System.Collections.Generic.IReadOnlyList<string> SteppedPath => _session?.Path;

        /// <summary>The module the canvas draws and edits.</summary>
        public string ShownModule => _session?.Module;

        /// <summary>What the inspector bar's line says now.</summary>
        public string InspectorSays => _barSays?.text;

        /// <summary>Steps inside the block <paramref name="part"/> of the module shown, as a double-click on it does; false for anything else.</summary>
        public bool StepInto(int part) => _editor != null && _editor.StepInto(new CanvasTarget(CanvasTargetKind.Part, part)) && Stepped();

        /// <summary>Steps <paramref name="levels"/> out, as a crumb or Alt+Up does; false at the top.</summary>
        public bool StepOut(int levels = 1) => _editor != null && _editor.StepOut(levels) && Stepped();

        /// <summary>Steps to an instance by its path in the design, as a double-click in the DESIGN tree does.</summary>
        public bool StepToScope(string scope) => _editor != null && _editor.StepToScope(scope) && Stepped();

        private bool Stepped()
        {
            SteppedInOrOut();
            return true;
        }

        /// <summary>
        /// The canvas stepped into a block or out of one (<see cref="WorkbenchSession.StepVersion"/>) -- or an edit took
        /// the instance away under it: the module now shown drawn and fitted to the window, its values the instance's,
        /// the run and the waves' rows as they were.
        /// </summary>
        private void SteppedInOrOut()
        {
            _drawnStep = _session.StepVersion;
            _drawnLayout = _session.LayoutVersion;
            _model = _run.Model;
            _playback.Refresh();
            _portKeys = PortKeys(_model);
            _appliedSignals = new int[0];
            ApplyRows();
            _viewState = null;
            RebuildStage(keepViews: true);
            FitView();
            BuildPartsPanel(SchematicPalette.Current);
            _inspectedProject = null;   // read again: what the line says inside a block is not what it says at the top
            ShowBreadcrumb();
        }

        /// <summary>The breadcrumb in the circuit window's top-left corner: the top, then each instance stepped through, each a way back up.</summary>
        private void BuildBreadcrumb()
        {
            _breadcrumb = new VisualElement { name = BreadcrumbName, pickingMode = PickingMode.Ignore };
            _breadcrumb.style.position = Position.Absolute;
            _breadcrumb.style.left = 8f;
            _breadcrumb.style.top = 8f;
            _breadcrumb.style.flexDirection = FlexDirection.Row;
            _breadcrumb.style.alignItems = Align.Center;
            _circuitPanel.Add(_breadcrumb);
            ShowBreadcrumb();
        }

        /// <summary>The breadcrumb made for where the canvas is; nothing at the top, where there is nowhere to go back to.</summary>
        private void ShowBreadcrumb()
        {
            if (_breadcrumb == null)
                return;
            _breadcrumb.Clear();
            int depth = _session?.Path.Count ?? 0;
            _breadcrumb.style.display = depth == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (depth == 0)
                return;

            SchematicPalette palette = SchematicPalette.Current;
            for (int i = 0; i <= depth; i++)
            {
                if (i > 0)
                {
                    Label between = ShellTheme.Label("between", ">", UiType.Caption, palette.TextDim, TextAnchor.MiddleCenter);
                    between.style.marginLeft = between.style.marginRight = CrumbGap;
                    _breadcrumb.Add(between);
                }

                int levels = depth - i;
                string caption = i == 0 ? _session.Project.Top : _session.Path[i - 1] + " : " + ModuleAt(i);
                ShellButton crumb = ShellTheme.Button(CrumbName + i.ToString(CultureInfo.InvariantCulture), caption, palette,
                    () => StepOut(levels), UiType.Caption);
                crumb.style.height = CrumbHeight;
                crumb.style.paddingLeft = crumb.style.paddingRight = 10f;
                crumb.SetEnabled(levels > 0);
                _breadcrumb.Add(crumb);
            }

            VisualElement cap = ShellTheme.KeyCap(WorkbenchKeys.For(WorkbenchCommand.StepOut).Cap, palette);
            cap.style.position = Position.Relative;
            cap.style.marginLeft = 10f;
            _breadcrumb.Add(cap);
        }

        /// <summary>The module of the instance <paramref name="depth"/> steps in.</summary>
        private string ModuleAt(int depth)
        {
            var path = new System.Collections.Generic.List<string>();
            for (int i = 0; i < depth; i++)
                path.Add(_session.Path[i]);
            return WorkbenchSession.ModuleOn(_session.Project, path, out _);
        }

        /// <summary>What the inspector's line says inside a block while nothing is selected: that an edit here is the module's.</summary>
        private string InsideText()
        {
            if (_session == null || _session.Path.Count == 0)
                return null;
            string module = _session.Module, instance = _session.Path[_session.Path.Count - 1];
            int count = _session.InstancesOf(module);
            return count > 1
                ? $"Inside {instance}, a {module}: what you change here changes all {count.ToString(CultureInfo.InvariantCulture)} of them. Alt+Up steps out."
                : $"Inside {instance}, a {module}: what you change here is the module's. Alt+Up steps out.";
        }

        /// <summary>Whether <paramref name="part"/> of the module shown is a block that can be stepped into.</summary>
        private bool IsBlock(int part) =>
            _session != null && _session.Project.Find(_session.Module)?.FindPart(part) is BlockPart block && _session.Project.Find(block.Module) != null;
    }
}
