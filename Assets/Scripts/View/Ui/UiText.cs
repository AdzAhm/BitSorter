using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BitSorter.View
{
    /// <summary>
    /// The keyboard, as the game's own keys may read it: there is none while a text field is being
    /// typed in.
    /// </summary>
    /// <remarks>
    /// Naming a free-play board put the first text field into a game that binds N, M, Q, E, H, R,
    /// Space, Enter and Escape, and reads them in seventeen places. Typing "Adder" would have muted
    /// the sound, and Q in a name changed level. Every one of those places takes its keyboard from
    /// <see cref="Keyboard"/> instead of <c>Keyboard.current</c>, and they already cope with there
    /// being no keyboard, so while a field has the keys each simply sees none. The rule is one
    /// place, not seventeen checks, and <c>UiTextTests</c> holds every script under View to it.
    ///
    /// Derived, never set -- the rule pointer ownership follows, for the same reason: a flag that
    /// is claimed can leak, and a leaked "typing" is a game whose keys do nothing, with nothing on
    /// screen to say why.
    /// </remarks>
    public static class UiText
    {
        /// <summary>
        /// Whether a text field holds the keyboard now: a TextMesh Pro field the event system has selected, or a
        /// field of the workbench's shell (Part 11a), which the event system never selects -- the shell is asked
        /// itself (<see cref="ShellPanel.Typing"/>).
        /// </summary>
        public static bool Typing
        {
            get
            {
                if (ShellPanel.Typing)
                    return true;

                EventSystem events = EventSystem.current;
                GameObject selected = events != null ? events.currentSelectedGameObject : null;

                if (selected == null)
                    return false;

                TMP_InputField field = selected.GetComponent<TMP_InputField>();
                return field != null && field.enabled && field.gameObject.activeInHierarchy;
            }
        }

        /// <summary>
        /// <c>Keyboard.current</c>, or null while <see cref="Typing"/>. The only place under View
        /// that reads the keyboard directly.
        /// </summary>
        public static UnityEngine.InputSystem.Keyboard Keyboard =>
            Typing ? null : UnityEngine.InputSystem.Keyboard.current;

        /// <summary>
        /// The keyboard for a field to read a key it answers itself while it holds the keys, which its own events do not
        /// tell apart: the code editor's Ctrl+Enter (Part 12d), which the field would otherwise type as a new line. Never
        /// for a key the game binds -- those come from <see cref="Keyboard"/>.
        /// </summary>
        public static UnityEngine.InputSystem.Keyboard FieldKeys => UnityEngine.InputSystem.Keyboard.current;
    }
}
