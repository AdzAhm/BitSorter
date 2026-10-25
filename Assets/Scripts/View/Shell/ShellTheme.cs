using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// The shell's builders (Part 11a): a label, a button with its key in a cap, a chip, a text field and a slider, in
    /// the workbench's own look -- the canvas's colours (<see cref="SchematicPalette"/>) and the game's sizes
    /// (<see cref="UiType"/>, <see cref="UiTheme.SizeOf"/>) -- as <c>WorkbenchPanel.CanvasButton</c> and its kin build
    /// them in uGUI. Every style is set here, in code: there is no USS to override it, so nothing in it is ever
    /// overridden, and no colour or size lives anywhere but the palette and <see cref="UiType"/>.
    /// </summary>
    public static class ShellTheme
    {
        /// <summary>The class every control carries -- something with clicks of its own -- which <see cref="ShellPanel.OverControlAt"/> looks for.</summary>
        public const string ControlClass = "shell-control";

        /// <summary>The class a label set in the bold face carries: which face its characters must be in.</summary>
        public const string BoldClass = "shell-bold";

        /// <summary>The name of a field's quiet word shown while it is empty (<see cref="Placeholder"/>).</summary>
        public const string PlaceholderName = "placeholder";

        /// <summary>How wide an outlined control's edge is, and how round its corners: as the uGUI controls draw theirs.</summary>
        public const float EdgeWidth = 1.5f, Corner = 9f;

        /// <summary>A plain container, taking no clicks, placed absolutely at <paramref name="left"/>, <paramref name="top"/>.</summary>
        public static VisualElement Box(string name, float left, float top, float width, float height)
        {
            var box = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            Place(box, left, top, width, height);
            return box;
        }

        /// <summary>Places an element absolutely in its parent; a width or height under 0 is left to the element.</summary>
        public static void Place(VisualElement element, float left, float top, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = left;
            element.style.top = top;
            if (width >= 0f)
                element.style.width = width;
            if (height >= 0f)
                element.style.height = height;
        }

        /// <summary>Places an element absolutely by its top-right corner, <paramref name="right"/> in from its parent's right.</summary>
        public static void PlaceRight(VisualElement element, float right, float top, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.right = right;
            element.style.top = top;
            element.style.width = width;
            element.style.height = height;
        }

        /// <summary>A line of text at <paramref name="type"/>'s size in <paramref name="colour"/>, on one line, taking no clicks.</summary>
        public static Label Label(string name, string text, UiType type, Color colour, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            var label = new Label(text) { name = name, pickingMode = PickingMode.Ignore };
            Text(label, type, colour, align, bold);
            return label;
        }

        /// <summary>Sets an element's text in the shell's face at <paramref name="type"/>'s size.</summary>
        public static void Text(VisualElement element, UiType type, Color colour, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            element.style.unityFontDefinition = bold ? ShellFonts.Bold : ShellFonts.Regular;
            element.style.fontSize = UiTheme.SizeOf(type);
            element.style.color = colour;
            element.style.unityTextAlign = align;
            element.style.whiteSpace = WhiteSpace.NoWrap;
            element.style.marginLeft = element.style.marginRight = element.style.marginTop = element.style.marginBottom = 0f;
            element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = 0f;

            // The bold face, never a bold style on top of it: UI Toolkit would thicken the bold face again.
            if (bold)
                element.AddToClassList(BoldClass);
        }

        /// <summary>Cuts a line of text short with an ellipsis where it runs past its box.</summary>
        public static void Ellipsis(TextElement element)
        {
            element.style.overflow = Overflow.Hidden;
            element.style.textOverflow = TextOverflow.Ellipsis;
        }

        /// <summary>Gives an element a rounded edge of <paramref name="edge"/> round a fill of <paramref name="fill"/>: an outline that is the same on any ground.</summary>
        public static void Outline(VisualElement element, Color edge, Color fill, float width = EdgeWidth, float corner = Corner)
        {
            element.style.backgroundColor = fill;
            element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = element.style.borderBottomColor = edge;
            element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = width;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = corner;
        }

        /// <summary>
        /// A button as the workbench draws REPLAY: a thin outline round the ground, its caption in the text colour.
        /// It takes no keyboard focus -- a focused button would answer Space and Enter as well as the workbench's own keys
        /// -- and a test presses it with <see cref="ShellPanel.Submit"/>.
        /// </summary>
        public static Button Button(string name, string caption, SchematicPalette palette, Action clicked, UiType type = UiType.Body)
        {
            var button = new Button(clicked) { name = name, text = caption, focusable = false };
            button.AddToClassList(ControlClass);
            Text(button, type, palette.Text, TextAnchor.MiddleCenter);
            Outline(button, palette.Outline, palette.Ground);
            return button;
        }

        /// <summary>A small chip in the canvas's colours: its edge, and its fill inside.</summary>
        public static VisualElement Chip(string name, SchematicPalette palette)
        {
            var chip = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            Outline(chip, palette.ChipEdge, palette.Chip);
            return chip;
        }

        /// <summary>A key's cap, <paramref name="key"/> in it, sized as the game sizes one (<see cref="UiTheme.KeyCapWidth"/>), taking no clicks.</summary>
        public static VisualElement KeyCap(string key, SchematicPalette palette)
        {
            VisualElement cap = Chip("key", palette);
            cap.style.width = UiTheme.KeyCapWidth(key);
            cap.style.height = UiTheme.ControlsHeight;
            cap.style.position = Position.Absolute;
            Label label = Label("key", key, UiTheme.KeyCapType, palette.TextDim, TextAnchor.MiddleCenter);
            label.style.flexGrow = 1f;
            cap.Add(label);
            return cap;
        }

        /// <summary>A button's key in a cap just over it, as the 4.x HUD draws RUN's.</summary>
        public static void KeyOver(VisualElement button, float buttonWidth, string key, SchematicPalette palette)
        {
            VisualElement cap = KeyCap(key, palette);
            cap.style.left = (buttonWidth - UiTheme.KeyCapWidth(key)) * 0.5f - EdgeWidth;
            cap.style.top = -(UiTheme.ControlsHeight + 4f) - EdgeWidth;
            button.Add(cap);
        }

        /// <summary>A button's key in a cap just left of it, for one with no room over it.</summary>
        public static void KeyBeside(VisualElement button, float buttonHeight, string key, SchematicPalette palette)
        {
            VisualElement cap = KeyCap(key, palette);
            cap.style.left = -(UiTheme.KeyCapWidth(key) + 6f) - EdgeWidth;
            cap.style.top = (buttonHeight - UiTheme.ControlsHeight) * 0.5f - EdgeWidth;
            button.Add(cap);
        }

        /// <summary>A button's key in a cap just right of it: FASTER's, whose pair has its key on the left.</summary>
        public static void KeyAfter(VisualElement button, float buttonWidth, float buttonHeight, string key, SchematicPalette palette)
        {
            VisualElement cap = KeyCap(key, palette);
            cap.style.left = buttonWidth + 6f - EdgeWidth;
            cap.style.top = (buttonHeight - UiTheme.ControlsHeight) * 0.5f - EdgeWidth;
            button.Add(cap);
        }

        // -----------------------------------------------------------------
        // A text field
        // -----------------------------------------------------------------

        private sealed class Finished
        {
            public int Frame = -1;
        }

        private static readonly ConditionalWeakTable<VisualElement, Finished> FinishedOn = new ConditionalWeakTable<VisualElement, Finished>();

        /// <summary>
        /// A field's record, made by a constructor called here. Never <c>GetOrCreateValue</c>: it makes the value by
        /// reflection, and the browser build's managed stripping took the constructor it needed -- Enter threw in the
        /// browser, the field never let go, and Space was typed into RUN TO (the Part 11a smoke test).
        /// </summary>
        private static Finished RecordOf(VisualElement field) => FinishedOn.GetValue(field, _ => new Finished());

        /// <summary>
        /// A single-line text field in the canvas's colours: an outlined box, the text, and a quiet
        /// <paramref name="placeholder"/> while it is empty -- a label of its own, since no theme draws UI Toolkit's in a
        /// colour of its own. Enter calls <paramref name="submitted"/> with what was typed, and so does letting it go by a
        /// click elsewhere, as the TextMesh Pro field did; Escape puts back what it held when it was clicked into.
        /// **Its value is what it shows, a keystroke at a time** (not delayed): a field that commits only on Enter had
        /// committed nothing when Enter was read, and in the browser RUN TO was never set (the Part 11a smoke test). **Enter and Escape let the field go a
        /// frame later** (<see cref="ShellPanel"/>, <see cref="FinishedBefore"/>): while it is focused the game's keys are
        /// off (<see cref="UiText"/>), so the press that finished it is the field's alone, where letting it go at once
        /// would hand that same Escape to the workbench and close it as well. **A field of a form** (the inspector's bar,
        /// Part 11b) is not submitted by letting it go -- <paramref name="submitOnLeave"/> false: what is typed in it waits
        /// for Enter or APPLY, with the form's other fields. **A search field keeps the keyboard after Enter**
        /// (<paramref name="releaseOnEnter"/> false, the + SIGNAL list's, Part 11b-3), for the next thing to be typed.
        /// </summary>
        public static TextField Field(string name, string placeholder, SchematicPalette palette, Action<string> submitted,
            bool submitOnLeave = true, UiType type = UiType.Body, bool releaseOnEnter = true)
        {
            var field = new TextField { name = name };
            field.AddToClassList(ControlClass);
            // Deprecated for the USS variables --unity-cursor-color and --unity-selection-color; the shell has no USS by
            // design (ShellRulesTests), and with no theme a caret would otherwise be drawn in no colour of the palette's.
#pragma warning disable CS0618
            field.textSelection.cursorColor = palette.Text;
            Color selection = palette.Accent;
            selection.a = 0.35f;
            field.textSelection.selectionColor = selection;
#pragma warning restore CS0618
            field.style.marginLeft = field.style.marginRight = field.style.marginTop = field.style.marginBottom = 0f;

            VisualElement input = field.Q(className: TextField.inputUssClassName) ?? field;
            Outline(input, palette.ChipEdge, palette.Chip, EdgeWidth, 6f);
            input.style.flexGrow = 1f;
            input.style.paddingLeft = input.style.paddingRight = 10f;
            input.style.paddingTop = input.style.paddingBottom = 0f;
            input.style.marginLeft = input.style.marginRight = input.style.marginTop = input.style.marginBottom = 0f;
            foreach (VisualElement part in new[] { field, input })
            {
                part.style.unityFontDefinition = ShellFonts.Regular;
                part.style.fontSize = UiTheme.SizeOf(type);
                part.style.color = palette.Text;
                part.style.unityTextAlign = TextAnchor.MiddleLeft;
            }

            // The text itself, as tall as the box, so what is typed sits in its middle: the theme the shell has none of
            // is what stretches it (the inspector's bar, Part 11b-2, showed every value at the top of its field).
            foreach (TextElement text in input.Query<TextElement>().ToList())
            {
                text.style.flexGrow = 1f;
                text.style.unityTextAlign = TextAnchor.MiddleLeft;
                text.style.marginTop = text.style.marginBottom = 0f;
                text.style.paddingTop = text.style.paddingBottom = 0f;
            }

            Label hint = Label(PlaceholderName, placeholder, type, palette.Muted);
            Place(hint, 10f + EdgeWidth, 0f, -1f, -1f);
            hint.style.bottom = 0f;
            input.Add(hint);
            void ShowHint()
            {
                bool focused = field.panel?.focusController?.focusedElement is VisualElement f && (f == field || field.Contains(f));
                hint.style.display = string.IsNullOrEmpty(field.text) && !focused ? DisplayStyle.Flex : DisplayStyle.None;
            }
            field.RegisterCallback<FocusInEvent>(_ => ShowHint());
            field.RegisterCallback<FocusOutEvent>(_ => ShowHint());
            field.RegisterValueChangedCallback(_ => ShowHint());
            field.RegisterCallback<GeometryChangedEvent>(_ => ShowHint());

            // What it held when it was clicked into, for Escape to put back; and whether Escape asked for that.
            string held = "";
            bool cancelled = false;
            field.RegisterCallback<FocusInEvent>(_ =>
            {
                held = field.value;
                cancelled = false;
            });
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (cancelled)
                    field.SetValueWithoutNotify(held);
                else if (submitOnLeave && field.value != held)
                    submitted?.Invoke(field.value);
                held = field.value;
                cancelled = false;
            });

            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    if (releaseOnEnter)
                        RecordOf(field).Frame = Time.frameCount;
                    submitted?.Invoke(field.value);
                    held = field.value;   // as the submission left it, so letting go does not submit it again
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    RecordOf(field).Frame = Time.frameCount;
                    cancelled = true;
                }
            }, TrickleDown.TrickleDown);
            return field;
        }

        /// <summary>Sets the quiet word a field shows while it is empty.</summary>
        public static void Placeholder(TextField field, string text)
        {
            Label hint = field?.Q<Label>(PlaceholderName);
            if (hint != null)
                hint.text = text;
        }

        /// <summary>Whether <paramref name="focused"/>, or the field it is inside, was finished with Enter or Escape before frame <paramref name="frame"/>.</summary>
        public static bool FinishedBefore(VisualElement focused, int frame)
        {
            for (VisualElement e = focused; e != null; e = e.parent)
            {
                if (e is TextField && FinishedOn.TryGetValue(e, out Finished finished) && finished.Frame >= 0 && finished.Frame < frame)
                {
                    finished.Frame = -1;
                    return true;
                }
            }

            return false;
        }

        // -----------------------------------------------------------------
        // A slider
        // -----------------------------------------------------------------

        /// <summary>The time slider as the reference draws it: the playhead's colour up to the moment, an outline's grey after, a round thumb in the text colour.</summary>
        public static ShellSlider Slider(string name, SchematicPalette palette) => new ShellSlider(name, palette);
    }

    /// <summary>
    /// A slider through the run (Part 11a): a track, the fill up to the value and a round thumb on it, made of plain
    /// elements so that no theme has to draw it. A press or a drag on it moves the value, and says so; setting it from
    /// the run does not.
    /// </summary>
    public sealed class ShellSlider : VisualElement
    {
        private readonly VisualElement _track, _fill, _thumb;
        private float _value, _low, _high = 1f;
        private int _dragging = -1;

        /// <summary>A press or a drag moved the value: what it moved it to.</summary>
        public event Action<float> Changed;

        public ShellSlider(string name, SchematicPalette palette)
        {
            this.name = name;
            AddToClassList(ShellTheme.ControlClass);
            focusable = false;

            float half = UiTheme.SliderHandle * 0.5f;
            _track = Bar("track", palette.Outline, half);
            _fill = Bar("fill", palette.Playhead, half);
            _fill.style.right = StyleKeyword.Auto;

            _thumb = new VisualElement { name = "thumb", pickingMode = PickingMode.Ignore };
            _thumb.style.position = Position.Absolute;
            _thumb.style.width = _thumb.style.height = UiTheme.SliderHandle;
            _thumb.style.top = new Length(50f, LengthUnit.Percent);
            _thumb.style.marginTop = -half;
            _thumb.style.backgroundColor = palette.Text;
            _thumb.style.borderTopLeftRadius = _thumb.style.borderTopRightRadius = _thumb.style.borderBottomLeftRadius = _thumb.style.borderBottomRightRadius = half;
            Add(_thumb);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        private VisualElement Bar(string barName, Color colour, float inset)
        {
            var bar = new VisualElement { name = barName, pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Absolute;
            bar.style.left = inset;
            bar.style.right = inset;
            bar.style.height = UiTheme.SliderTrack;
            bar.style.top = new Length(50f, LengthUnit.Percent);
            bar.style.marginTop = -UiTheme.SliderTrack * 0.5f;
            bar.style.backgroundColor = colour;
            Add(bar);
            return bar;
        }

        public float value
        {
            get => _value;
            set => Set(value, true);
        }

        public float lowValue
        {
            get => _low;
            set { _low = value; Layout(); }
        }

        public float highValue
        {
            get => _high;
            set { _high = value; Layout(); }
        }

        /// <summary>Moves the value without saying so: the run moving on.</summary>
        public void SetValueWithoutNotify(float to) => Set(to, false);

        private void Set(float to, bool notify)
        {
            to = Mathf.Clamp(to, _low, Mathf.Max(_low, _high));
            if (Mathf.Approximately(to, _value) && !notify)
                return;
            _value = to;
            Layout();
            if (notify)
                Changed?.Invoke(to);
        }

        /// <summary>How far along the track a point of the panel is, as a value.</summary>
        private float ValueAt(Vector2 local)
        {
            float half = UiTheme.SliderHandle * 0.5f, width = Mathf.Max(1f, layout.width - 2f * half);
            return Mathf.Lerp(_low, _high, Mathf.Clamp01((local.x - half) / width));
        }

        private void Layout()
        {
            float half = UiTheme.SliderHandle * 0.5f, width = Mathf.Max(0f, layout.width - 2f * half);
            float along = _high > _low ? Mathf.Clamp01((_value - _low) / (_high - _low)) : 0f;
            _fill.style.width = width * along;
            _thumb.style.left = width * along;
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0)
                return;
            _dragging = e.pointerId;
            this.CapturePointer(e.pointerId);
            Set(ValueAt(e.localPosition), true);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId == _dragging)
                Set(ValueAt(e.localPosition), true);
        }

        private void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != _dragging)
                return;
            _dragging = -1;
            this.ReleasePointer(e.pointerId);
        }
    }
}
