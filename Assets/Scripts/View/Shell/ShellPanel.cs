using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// A UI Toolkit panel of the workbench's shell (Part 11): a <see cref="UIDocument"/> on a <see cref="PanelSettings"/>
    /// made in code, scaled as the game's canvas is scaled and sorted over it, and the bridges that let the rest of the
    /// game ask about it as it asks about uGUI.
    /// </summary>
    /// <remarks>
    /// **Built in code, as the rest of the interface is** -- no UXML and no USS: every element is made by
    /// <see cref="ShellTheme"/>'s builders, which set its layout, colours and sizes from the palette and
    /// <see cref="UiType"/>. The panel is given an empty theme style sheet only so that UI Toolkit does not warn that
    /// it has none; nothing in it is drawn by a theme.
    ///
    /// **The bridges** (each held by <c>ShellPlayTests</c>):
    /// - *Pointer.* The event system sees the panel as one nameless object, so "is the pointer over a control" is asked
    ///   of the panel itself (<see cref="OverControlAt"/>), and <see cref="WorkbenchControlSample"/> and
    ///   <see cref="PointerGate"/> ask it too.
    /// - *Keyboard.* A focused shell text field is not the event system's selection, so <see cref="UiText.Typing"/>
    ///   asks <see cref="Typing"/> as well -- derived from the panel's focus each time, never set.
    /// - *Escape and Enter in a field* let it go a frame later (<see cref="ShellTheme.Field"/>), so the press that
    ///   finished the field cannot close the workbench in the same frame.
    /// - *A click anywhere else* lets a focused field go, which UI Toolkit cannot see when the click lands on uGUI.
    /// - *A field given the keyboard keeps it* until it is let go on purpose -- those two, or its popup or the panel
    ///   closing -- and is given it back when UI Toolkit lets it go to nothing, as it does in the browser after a press.
    /// - *A key that asks for a field* gives it the keyboard the frame after (<see cref="FocusAfterTheKey"/>), so the key
    ///   is not typed into it as well.
    /// - *No key moves the focus round it* (Part 14g): UI Toolkit's navigation -- the arrows and Tab -- is ignored, since
    ///   those keys are the workbench's, and navigation put the keyboard in a field after any click on the shell.
    /// </remarks>
    public sealed class ShellPanel : MonoBehaviour
    {
        /// <summary>Over the game's canvas (0) and under the frame-rate counter's (<see cref="FrameRateCounter.SortingOrder"/>).</summary>
        public const int SortingOrder = 50;

        private static readonly List<ShellPanel> Live = new List<ShellPanel>();

        private UIDocument _document;

        // The field given the keyboard -- by a press into it, or by a click or a key that asked for it -- and the frame it
        // was given in. It keeps the keyboard until it is let go on purpose (Update).
        private static TextField _holder;
        private static int _holderFrame = -1;

        // What was chosen in the holder at the end of the last frame it had the keyboard, -1 before it has had it a frame:
        // what it is given back with.
        private static int _holderCursor = -1, _holderSelect = -1;

        // A field a key asked to be given the keyboard, the frame it asked in, and whether all of its text is chosen.
        private static TextField _afterKey;
        private static int _afterKeyFrame = -1;
        private static bool _afterKeySelectsAll;

        /// <summary>The panel's root: the whole screen, taking no clicks of its own.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>A shell panel on a new object under <paramref name="parent"/>.</summary>
        public static ShellPanel Create(string name, Transform parent)
        {
            var host = new GameObject(name);
            host.SetActive(false);
            host.transform.SetParent(parent, false);

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = name;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int((int)UiTheme.ReferenceResolution.x, (int)UiTheme.ReferenceResolution.y);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = SortingOrder;
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            settings.clearColor = false;

            var shell = host.AddComponent<ShellPanel>();
            shell._document = host.AddComponent<UIDocument>();
            shell._document.panelSettings = settings;
            host.SetActive(true);

            shell.Root = shell._document.rootVisualElement;
            shell.Root.name = name;
            shell.Root.pickingMode = PickingMode.Ignore;
            shell.Root.style.flexGrow = 1f;
            shell.Root.RegisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);

            // The keyboard never moves the focus round the shell (Part 14g): the arrows and Tab are the workbench's -- a step
            // in time, the waves' rows, out of a block. A click on the shell makes it the event system's selection, and an
            // arrow after it was navigation that put the keyboard in a field, where every key of the workbench is the field's.
            // On the panel's own tree, over the document's root: with nothing focused the event is sent to the panel itself.
            VisualElement tree = shell.Root.panel?.visualTree ?? shell.Root;
            tree.RegisterCallback<NavigationMoveEvent>(e => tree.focusController?.IgnoreEvent(e), TrickleDown.TrickleDown);
            return shell;
        }

        private void OnEnable()
        {
            Live.Add(this);
            KeyCaps.Changed += ShowKeyCaps;
            ShowKeyCaps();
        }

        private void OnDisable()
        {
            Live.Remove(this);
            KeyCaps.Changed -= ShowKeyCaps;
        }

        /// <summary>Every key's cap beside a button shown or hidden, as SHORTCUT KEYS says (<see cref="KeyCaps"/>).</summary>
        private void ShowKeyCaps()
        {
            bool shown = KeyCaps.Shown;
            Root?.Query(className: ShellTheme.KeyCapClass).ForEach(cap => cap.visible = shown);
        }

        /// <summary>Whether the panel is drawn and takes the pointer.</summary>
        public bool Visible
        {
            get => Root != null && Root.resolvedStyle.display != DisplayStyle.None && Root.style.display != DisplayStyle.None;
            set
            {
                if (Root == null)
                    return;
                Root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (!value)
                    Root.focusController?.focusedElement?.Blur();
            }
        }

        /// <summary>How opaque the panel is drawn: it follows the fade of the panel it belongs to.</summary>
        public float Opacity
        {
            set
            {
                if (Root != null && !Mathf.Approximately(Root.style.opacity.value, value))
                    Root.style.opacity = value;
            }
        }

        /// <summary>A field taking the keyboard is the one that holds it from now on (<see cref="Update"/>).</summary>
        private static void OnFocusIn(FocusInEvent e)
        {
            TextField field = e.target is VisualElement target ? FieldOf(target) : null;
            if (field == null || field == _holder)
                return;
            _holder = field;
            _holderFrame = Time.frameCount;
            _holderCursor = _holderSelect = -1;
        }

        /// <summary>
        /// Gives <paramref name="field"/> the keyboard in the frame after this one -- all of its text chosen when
        /// <paramref name="selectAll"/> -- for a field a key or a click asks for: A for the + SIGNAL list, Enter for the
        /// inspector, + CODE for the new block's name.
        /// </summary>
        /// <remarks>
        /// The platform hands a key to UI Toolkit as well as to the game, and in the browser that half can arrive after
        /// the game has read the key. A field given the keyboard in its key's frame took the key as well: A typed an "a"
        /// into the search it opened, and Enter submitted the field it asked to start typing in (the Part 11d smoke test).
        /// A click the same the other way round: the game read + CODE's press a frame after UI Toolkit had opened the name
        /// field on it, under the pointer, and let the field go as for a press off it (the Part 14g smoke test).
        /// </remarks>
        public static void FocusAfterTheKey(TextField field, bool selectAll = false)
        {
            _afterKey = field;
            _afterKeyFrame = Time.frameCount;
            _afterKeySelectsAll = selectAll;
        }

        private void Update()
        {
            if (Root?.panel == null)
                return;

            int frame = Time.frameCount;

            // A field a key asked for is given the keyboard now, the frame after its key.
            if (_afterKey != null && frame > _afterKeyFrame && _afterKey.panel == Root.panel)
            {
                TextField asked = _afterKey;
                _afterKey = null;
                if (Visible && Shown(asked))
                {
                    asked.Focus();
                    if (_afterKeySelectsAll)
                        asked.SelectAll();
                }
            }

            // A field finished with Enter or Escape lets go now, a frame after (ShellTheme.Field).
            if (Root.focusController.focusedElement is VisualElement focused && ShellTheme.FinishedBefore(focused, frame))
                LetGo(focused);

            // A press anywhere but the focused field lets it go -- a press on uGUI never reaches this panel to do it --
            // except the press that gave the field the keyboard: a double-click on a part asks for its inspector. And a
            // press anywhere but the field given the keyboard ends its hold even when the event system has let it go
            // first, as it does for a press on uGUI.
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame))
            {
                VisualElement under = PickAt(mouse.position.ReadValue());
                if (Root.focusController.focusedElement is VisualElement held && !Inside(under, FieldOf(held) ?? held)
                    && !(FieldOf(held) == _holder && frame <= _holderFrame))
                {
                    LetGo(held);
                }

                if (_holder != null && _holder.panel == Root.panel && frame > _holderFrame && !Inside(under, _holder))
                    _holder = null;
            }

            // The field given the keyboard and let go to nothing is given it back. In the browser UI Toolkit lets a field
            // go, to nothing, in the frame it is given the keyboard and in the frames after the press that gave it --
            // a click into it, a click on + SIGNAL, a double-click on a part -- with the event system's selection
            // unchanged, and the first keys typed went nowhere (the Part 11a and 11d smoke tests). Let go on purpose, it
            // is no longer the holder: closed with its popup or its panel, or taken out with an inspection made again. It
            // is given back as it was, with what was chosen in it: it came back with the caret at its start, and a name
            // offered all chosen, to be typed over, was typed in front of (the Part 14g smoke test).
            if (_holder != null && _holder.panel == null)
                _holder = null;
            if (_holder != null && _holder.panel == Root.panel)
            {
                if (!Visible || !Shown(_holder))
                {
                    _holder = null;
                }
                else if (Root.focusController.focusedElement == null)
                {
                    _holder.Focus();
                    if (_holderCursor >= 0)
                        _holder.SelectRange(_holderCursor, _holderSelect);
                }
                else if (Inside(Root.focusController.focusedElement as VisualElement, _holder))
                {
                    _holderCursor = _holder.cursorIndex;
                    _holderSelect = _holder.selectIndex;
                }
            }
        }

        /// <summary>Lets <paramref name="focused"/> go on purpose: the field it is part of no longer holds the keyboard.</summary>
        private static void LetGo(VisualElement focused)
        {
            if (FieldOf(focused) == _holder)
                _holder = null;
            focused.Blur();
        }

        /// <summary>Whether <paramref name="under"/> is anywhere in <paramref name="field"/>, whichever of its parts.</summary>
        private static bool Inside(VisualElement under, VisualElement field) => under != null && (under == field || field.Contains(under));

        /// <summary>Whether <paramref name="element"/> is drawn: neither it nor anything it is in is hidden.</summary>
        private static bool Shown(VisualElement element)
        {
            for (VisualElement e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.style.display == DisplayStyle.None)
                    return false;
            return true;
        }

        /// <summary>The element under a point of the screen (pixels, y up) on this panel, if any takes the pointer.</summary>
        public VisualElement PickAt(Vector2 screenPoint)
        {
            if (Root?.panel == null || !Visible)
                return null;
            Vector2 at = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            return Root.panel.Pick(at);
        }

        // -----------------------------------------------------------------
        // The bridges, asked of every shell panel up
        // -----------------------------------------------------------------

        /// <summary>Whether a shell text field holds the keyboard: <see cref="UiText.Typing"/> asks this as well.</summary>
        public static bool Typing
        {
            get
            {
                foreach (ShellPanel shell in Live)
                    if (shell.Visible && shell.Root.focusController?.focusedElement is VisualElement focused && IsField(focused))
                        return true;
                return false;
            }
        }

        /// <summary>Whether anything of a shell panel up is under a point of the screen: <see cref="PointerGate"/>'s "over the interface".</summary>
        public static bool OverAnyAt(Vector2 screenPoint)
        {
            foreach (ShellPanel shell in Live)
                if (shell.PickAt(screenPoint) != null)
                    return true;
            return false;
        }

        /// <summary>Whether a control of a shell panel -- a button, a field, a slider -- is under a point of the screen.</summary>
        public static bool OverControlAt(Vector2 screenPoint)
        {
            foreach (ShellPanel shell in Live)
                for (VisualElement e = shell.PickAt(screenPoint); e != null; e = e.parent)
                    if (e.ClassListContains(ShellTheme.ControlClass))
                        return true;
            return false;
        }

        private static bool IsField(VisualElement element) => FieldOf(element) != null;

        /// <summary>The text field an element is, or is part of; null for none.</summary>
        private static TextField FieldOf(VisualElement element)
        {
            for (VisualElement e = element; e != null; e = e.parent)
                if (e is TextField field)
                    return field;
            return null;
        }

        // -----------------------------------------------------------------
        // For tests: an element by name, where it is on the screen, and a click on it
        // -----------------------------------------------------------------

        /// <summary>The element of type <typeparamref name="T"/> called <paramref name="name"/> on any shell panel up; null with none.</summary>
        public static T Find<T>(string name) where T : VisualElement
        {
            foreach (ShellPanel shell in Live)
            {
                T found = shell.Root?.Q<T>(name);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>The middle of an element on the screen, in pixels, y up: where a mouse would press it.</summary>
        public static Vector2 ScreenPointOf(VisualElement element)
        {
            Rect bound = element.worldBound;
            float scale = element.panel.scaledPixelsPerPoint;
            return new Vector2(bound.center.x * scale, Screen.height - bound.center.y * scale);
        }

        /// <summary>Where an element is on the screen, in pixels, y up: what a uGUI menu opened beside it is placed by.</summary>
        public static Rect ScreenRectOf(VisualElement element)
        {
            Rect bound = element.worldBound;
            float scale = element.panel.scaledPixelsPerPoint;
            return Rect.MinMaxRect(bound.xMin * scale, Screen.height - bound.yMax * scale, bound.xMax * scale, Screen.height - bound.yMin * scale);
        }

        /// <summary>
        /// A key handed to an element as UI Toolkit hands it one. UI Toolkit reads the platform's keys, so a test's
        /// simulated keyboard, which is the Input System's, never reaches a shell field: a test sends the key this way
        /// as well.
        /// </summary>
        public static void SendKey(VisualElement element, KeyCode key)
        {
            using (KeyDownEvent down = KeyDownEvent.GetPooled('\0', key, EventModifiers.None))
            {
                down.target = element;
                element.SendEvent(down);
            }
        }

        /// <summary>Presses a button as Enter on it would: what a test's click on a button the shell draws does.</summary>
        public static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = element;
                element.SendEvent(submit);
            }
        }
    }
}
