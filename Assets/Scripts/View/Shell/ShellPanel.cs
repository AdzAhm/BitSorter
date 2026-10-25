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
    /// </remarks>
    public sealed class ShellPanel : MonoBehaviour
    {
        /// <summary>Over the game's canvas (0) and under the frame-rate counter's (<see cref="FrameRateCounter.SortingOrder"/>).</summary>
        public const int SortingOrder = 50;

        private static readonly List<ShellPanel> Live = new List<ShellPanel>();

        private UIDocument _document;

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
            return shell;
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

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

        private void Update()
        {
            if (Root?.panel == null)
                return;

            // A field finished with Enter or Escape lets go now, a frame after (ShellTheme.Field).
            if (Root.focusController.focusedElement is VisualElement focused && ShellTheme.FinishedBefore(focused, Time.frameCount))
                focused.Blur();

            // A press anywhere but the focused field lets it go: a press on uGUI never reaches this panel to do it.
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
                && Root.focusController.focusedElement is VisualElement held)
            {
                VisualElement under = PickAt(mouse.position.ReadValue());
                if (under == null || (under != held && !held.Contains(under)))
                    held.Blur();
            }
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

        private static bool IsField(VisualElement element)
        {
            for (VisualElement e = element; e != null; e = e.parent)
                if (e is TextField)
                    return true;
            return false;
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
