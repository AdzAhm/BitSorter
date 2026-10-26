using System.Collections.Generic;
using System.Globalization;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using Project = BitSorter.Board.Project;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The inspector, docked (Part 11b-2): what is selected on the circuit, shown in two places that are always there --
    /// **the bar under the toolbar holds all of it**, the title, what it is and every field, with APPLY and REVERT; **the
    /// side panel holds the quick fields**, the ones of its kind worth having at hand (<see cref="Inspection.Quick"/>).
    /// Neither comes and goes, so the circuit's window never changes size with the selection (Ahmad's choice, 2026-10-25).
    /// </summary>
    /// <remarks>
    /// - **It follows the selection**: one part or one wire selected is inspected; nothing, or more than one, is said in
    ///   the bar's title. A double-click on a part or a wire, or Enter with one selected, puts the keyboard in the bar's
    ///   first field -- where the popover of Part 10c opened.
    /// - **The bar is a form**: what is typed in its fields waits for Enter in any of them, or APPLY, and is made as one
    ///   step, so fields that only make sense together -- a register made wider and given a wider reset value -- are
    ///   made together. REVERT puts the fields back. **A quick field is made on its own**, on Enter or on being let go,
    ///   as RUN TO is.
    /// - **The document is still the judge** (<see cref="Inspector.Apply"/>): a refusal is written in the bar, in the
    ///   document's words, in the text's colour, until the next thing is made. Both read the document again after every
    ///   edit, so a change made in one shows in the other.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The inspector's pieces' names, which the tests look for.</summary>
        public const string InspectorBarName = "Workbench inspector", InspectorApplyName = "Workbench inspector apply",
            InspectorRevertName = "Workbench inspector revert", QuickName = "Workbench quick";

        /// <summary>The bar's field of this key (<see cref="InspectorField.Key"/>).</summary>
        public static string InspectorFieldName(string key) => "Workbench inspector " + key;

        /// <summary>The side panel's quick field of this key.</summary>
        public static string QuickFieldName(string key) => "Workbench quick " + key;

        /// <summary>The bar's title and its line, and the fields: how wide, how far apart, and the room the buttons take.</summary>
        private const float BarPad = 14f, BarFieldWidth = 140f, BarWideFieldWidth = 200f, BarFieldGap = 12f, BarButtonWidth = 100f,
            BarButtonHeight = 36f, BarLeast = 300f;

        /// <summary>How tall a quick field is, and the room its label takes over it.</summary>
        private const float QuickFieldHeight = 32f, QuickLabelHeight = 20f, QuickGap = 8f;

        private VisualElement _inspectorBar, _barFields, _quick;
        private Label _barTitle, _barSays, _quickTitle;
        private ShellButton _applyButton, _revertButton;
        private readonly List<(InspectorField Field, TextField Input)> _barInputs = new List<(InspectorField, TextField)>();
        private readonly List<(InspectorField Field, TextField Input)> _quickInputs = new List<(InspectorField, TextField)>();

        /// <summary>What is inspected now, and what it was read from: the document, the one thing selected, and how many are.</summary>
        private Inspection _inspection;
        private Project _inspectedProject;
        private CanvasTarget _inspectedTarget;
        private int _inspectedParts = -1, _inspectedWires = -1;
        private string _refusal;

        /// <summary>What the inspector shows; null with nothing, or more than one thing, selected.</summary>
        public Inspection Inspection => _inspection;

        /// <summary>What the last edit from the inspector was refused with, as the bar shows it; null when nothing was.</summary>
        public string InspectorRefusal => _refusal;

        /// <summary>What the bar's title says now: the part or the wire, or what is selected when it is not one thing.</summary>
        public string InspectorTitle => _barTitle?.text;

        /// <summary>What each of the bar's fields holds now, by key.</summary>
        public Dictionary<string, string> InspectorTyped()
        {
            var typed = new Dictionary<string, string>();
            foreach ((InspectorField field, TextField input) in _barInputs)
                typed[field.Key] = input.value;
            return typed;
        }

        /// <summary>Puts <paramref name="text"/> in the bar's field of <paramref name="key"/>, as typing would; false for no such field.</summary>
        public bool InspectorType(string key, string text)
        {
            foreach ((InspectorField field, TextField input) in _barInputs)
            {
                if (field.Key != key)
                    continue;
                input.value = text;
                return true;
            }

            return false;
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void BuildInspector(SchematicPalette palette)
        {
            _inspectorBar = Panel(InspectorBarName, palette);

            _barTitle = ShellTheme.Label("title", "", UiType.Label, palette.Text, TextAnchor.MiddleLeft, bold: true);
            ShellTheme.Ellipsis(_barTitle);
            _inspectorBar.Add(_barTitle);
            _barSays = ShellTheme.Label("says", "", UiType.Caption, palette.TextDim);
            ShellTheme.Ellipsis(_barSays);
            _inspectorBar.Add(_barSays);

            _barFields = new VisualElement { name = "fields", pickingMode = PickingMode.Ignore };
            _barFields.style.position = Position.Absolute;
            _inspectorBar.Add(_barFields);

            _applyButton = ShellTheme.Button(InspectorApplyName, "APPLY", palette, InspectorApply);
            _revertButton = ShellTheme.Button(InspectorRevertName, "REVERT", palette, InspectorRevert);
            _inspectorBar.Add(_applyButton);
            _inspectorBar.Add(_revertButton);

            // The quick fields, at the top of the side panel; the minimap keeps its foot.
            Label heading = ShellTheme.Label("heading", "QUICK", UiType.Caption, palette.Text, TextAnchor.MiddleLeft, bold: true);
            ShellTheme.Place(heading, PanelPad, 6f, -1f, PartHeadingHeight);
            _sidePanel.Add(heading);
            _quickTitle = ShellTheme.Label("title", "", UiType.Caption, palette.TextDim);
            ShellTheme.Place(_quickTitle, PanelPad, 6f + PartHeadingHeight, ShellLayout.SideWidth - 2f * PanelPad, 22f);
            ShellTheme.Ellipsis(_quickTitle);
            _sidePanel.Add(_quickTitle);
            _quick = new VisualElement { name = QuickName, pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_quick, 0f, 6f + PartHeadingHeight + 28f, ShellLayout.SideWidth, -1f);
            _sidePanel.Add(_quick);

            ShowInspection(null, 0, 0);
        }

        // -----------------------------------------------------------------
        // Following the selection
        // -----------------------------------------------------------------

        /// <summary>
        /// The bar and the quick fields read again, if the document or the selection has changed since they were last read:
        /// the one thing selected, or what is selected when it is not one thing.
        /// </summary>
        private void RefreshInspector()
        {
            if (_editor == null || _session == null || _inspectorBar == null)
                return;

            CanvasTarget single = _editor.Single;
            int parts = _editor.Selection.Parts.Count, wires = _editor.Selection.Wires.Count;
            if (ReferenceEquals(_inspectedProject, _session.Project) && single.Equals(_inspectedTarget) && parts == _inspectedParts &&
                wires == _inspectedWires)
            {
                return;
            }

            _inspectedProject = _session.Project;
            _inspectedTarget = single;
            _inspectedParts = parts;
            _inspectedWires = wires;
            _refusal = null;
            ShowInspection(single.Kind == CanvasTargetKind.Nothing ? null : BitSorter.ViewModel.Inspector.Of(_session.Project, _session.Module, single),
                parts, wires);
        }

        /// <summary>The bar and the quick fields made for <paramref name="inspection"/>; with none, what is selected says so.</summary>
        private void ShowInspection(Inspection inspection, int parts, int wires)
        {
            SchematicPalette palette = SchematicPalette.Current;
            _inspection = inspection;
            _barFields.Clear();
            _barInputs.Clear();
            _quick.Clear();
            _quickInputs.Clear();

            bool one = inspection != null;
            _applyButton.style.display = _revertButton.style.display = one ? DisplayStyle.Flex : DisplayStyle.None;
            if (!one)
            {
                bool none = parts + wires == 0;
                SetText(_barTitle, none ? "NOTHING SELECTED" : SelectedText(parts, wires));
                SetText(_barSays, none
                    ? "Click a part or a wire to see it here and change it; a double-click on one, or Enter, starts typing."
                    : "One thing at a time is inspected: click one alone to see it here.");
                _barSays.style.color = palette.TextDim;
                SetText(_quickTitle, none ? "Select a part or a wire." : SelectedText(parts, wires).ToLowerInvariant());
                LayoutInspectorBar();
                return;
            }

            SetText(_barTitle, inspection.Title);
            ShowSays();
            SetText(_quickTitle, inspection.Title);

            foreach (InspectorField field in inspection.Fields)
            {
                InspectorField shown = field;
                TextField input = ShellTheme.Field(InspectorFieldName(field.Key), field.Hint, palette, _ => InspectorApply(),
                    submitOnLeave: false, type: UiType.Caption);
                input.SetValueWithoutNotify(field.Text);
                Label label = ShellTheme.Label("label " + field.Key, field.Label, UiType.Micro, palette.TextDim);
                _barFields.Add(label);
                _barFields.Add(input);
                _barInputs.Add((shown, input));
            }

            float y = 0f, inside = ShellLayout.SideWidth - 2f * PanelPad;
            foreach (InspectorField field in inspection.Quick)
            {
                string key = field.Key;
                Label label = ShellTheme.Label("label " + key, field.Label, UiType.Micro, palette.TextDim);
                ShellTheme.Place(label, PanelPad, y, inside, QuickLabelHeight);
                _quick.Add(label);
                TextField input = ShellTheme.Field(QuickFieldName(key), field.Hint, palette, text => ApplyQuick(key, text), type: UiType.Caption);
                input.SetValueWithoutNotify(field.Text);
                ShellTheme.Place(input, PanelPad, y + QuickLabelHeight, inside, QuickFieldHeight);
                _quick.Add(input);
                _quickInputs.Add((field, input));
                y += QuickLabelHeight + QuickFieldHeight + QuickGap;
            }

            LayoutInspectorBar();
        }

        private static string SelectedText(int parts, int wires)
        {
            string Count(int n, string one, string many) => n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
            if (wires == 0)
                return Count(parts, "PART", "PARTS") + " SELECTED";
            if (parts == 0)
                return Count(wires, "WIRE", "WIRES") + " SELECTED";
            return Count(parts, "PART", "PARTS") + " AND " + Count(wires, "WIRE", "WIRES") + " SELECTED";
        }

        /// <summary>The bar's line: what is inspected is, or the last refusal, in the text's colour, until the next thing is made.</summary>
        private void ShowSays()
        {
            SchematicPalette palette = SchematicPalette.Current;
            SetText(_barSays, _refusal ?? _inspection?.Says ?? "");
            _barSays.style.color = _refusal != null ? palette.Text : palette.TextDim;
        }

        /// <summary>
        /// The bar laid out across its width: APPLY and REVERT at its right, the fields in a row before them, and the title
        /// and its line in what is left -- at least <see cref="BarLeast"/>, past which the fields give way at the right.
        /// </summary>
        private void LayoutInspectorBar()
        {
            if (_inspectorBar == null)
                return;

            float width = _regions.Inspector.width, height = ShellLayout.InspectorHeight;
            float buttons = _applyButton.style.display == DisplayStyle.None ? 0f : 2f * BarButtonWidth + BarFieldGap + BarPad;
            ShellTheme.PlaceRight(_revertButton, BarPad, (height - BarButtonHeight) * 0.5f, BarButtonWidth, BarButtonHeight);
            ShellTheme.PlaceRight(_applyButton, BarPad + BarButtonWidth + BarFieldGap, (height - BarButtonHeight) * 0.5f, BarButtonWidth,
                BarButtonHeight);

            float fields = 0f;
            foreach ((InspectorField field, TextField _) in _barInputs)
                fields += FieldWidth(field) + BarFieldGap;

            float titleWidth = Mathf.Max(BarLeast, width - buttons - fields - 2f * BarPad);
            ShellTheme.Place(_barTitle, BarPad, 6f, titleWidth, 26f);
            ShellTheme.Place(_barSays, BarPad, 32f, titleWidth, 24f);

            float x = 0f;
            ShellTheme.Place(_barFields, BarPad + titleWidth + BarFieldGap, 0f, Mathf.Max(0f, width - buttons - titleWidth - 2f * BarPad), height);
            for (int i = 0; i < _barInputs.Count; i++)
            {
                float w = FieldWidth(_barInputs[i].Field);
                ShellTheme.Place(_barFields[2 * i], x, 6f, w, 18f);
                ShellTheme.Place(_barInputs[i].Input, x, 24f, w, 32f);
                x += w + BarFieldGap;
            }

            _barFields.style.overflow = Overflow.Hidden;
        }

        private static float FieldWidth(InspectorField field) => field.Key == "comment" || field.Key == "name" ? BarWideFieldWidth : BarFieldWidth;

        // -----------------------------------------------------------------
        // Making what is typed
        // -----------------------------------------------------------------

        /// <summary>What the bar's fields say, made as one step, as Enter in any of them and APPLY do.</summary>
        public void InspectorApply()
        {
            if (_inspection == null)
                return;
            _refusal = ApplyInspector(_inspection, InspectorTyped());
            ShowSays();
        }

        /// <summary>The bar's fields put back to what the part or the wire has.</summary>
        public void InspectorRevert()
        {
            foreach ((InspectorField field, TextField input) in _barInputs)
                input.SetValueWithoutNotify(field.Text);
            _refusal = null;
            ShowSays();
        }

        /// <summary>A quick field's <paramref name="text"/> made on its own, as Enter in it does: null when it was, or the words it was refused with.</summary>
        public string ApplyQuick(string key, string text)
        {
            if (_inspection == null)
                return "There is nothing to change.";
            _refusal = ApplyInspector(_inspection, new Dictionary<string, string> { [key] = text });
            ShowSays();
            return _refusal;
        }

        /// <summary>
        /// The keyboard in the bar's first field, all of it chosen, as a double-click and Enter give it -- Enter's the frame
        /// after, so the Enter is not the field's as well (<see cref="ShellPanel.FocusAfterTheKey"/>).
        /// </summary>
        private void FocusInspector(bool afterTheKey = false)
        {
            RefreshInspector();
            if (_barInputs.Count == 0)
                return;
            TextField first = _barInputs[0].Input;
            if (afterTheKey)
            {
                ShellPanel.FocusAfterTheKey(first, selectAll: true);
                return;
            }

            first.Focus();
            first.SelectAll();
        }
    }
}
