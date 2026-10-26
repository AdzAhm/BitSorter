using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// What the workbench's menus have in common (Part 9b-2b, shared in 9b-3, in the shell since 11b-3): a column of items
    /// put up where it was asked for and kept on the screen, closed by a click off it or by Escape before Escape closes
    /// the workbench.
    /// </summary>
    /// <remarks>
    /// Drawn in the shell (<see cref="ShellPopup"/>), over the docked panels. It holds Escape while it is open and for the
    /// rest of the frame it closes in (<see cref="IHoldsEscape"/>), so one press closes the menu and leaves the workbench
    /// up, whichever of the two Unity updates first; the component is what joins <see cref="UiEscape"/> and reads the key.
    /// The press that closes it is the popup's layer's, a control's (<see cref="WorkbenchControlSample"/>), so it never
    /// seeks or selects what is under it. Items that do nothing for what was clicked are left off rather than greyed.
    /// </remarks>
    public abstract class WaveMenu : MonoBehaviour, IHoldsEscape
    {
        private const float ItemHeight = 38f, Gap = 4f, Pad = 8f, NoteLine = 24f, NoteCharacter = 8.5f;

        private ShellPopup _popup;
        private SchematicPalette _palette;
        private float _itemWidth;
        private readonly List<ShellButton> _items = new List<ShellButton>();
        private readonly HashSet<ShellButton> _breakBefore = new HashSet<ShellButton>();
        private readonly List<Label> _notes = new List<Label>();

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => _popup != null && _popup.IsOpen;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || (_popup != null && _popup.ClosedFrame == Time.frameCount);

        /// <summary>
        /// A menu of kind <typeparamref name="T"/> on <paramref name="shell"/>, named <paramref name="name"/>, its items
        /// <paramref name="itemWidth"/> wide; the component that keeps it on a child of <paramref name="owner"/>.
        /// </summary>
        protected static T Make<T>(ShellPanel shell, Transform owner, string name, SchematicPalette palette, float itemWidth) where T : WaveMenu
        {
            var host = new GameObject(name);
            host.transform.SetParent(owner, false);
            var menu = host.AddComponent<T>();
            menu._popup = new ShellPopup(shell, name, palette);
            menu._palette = palette;
            menu._itemWidth = itemWidth;
            return menu;
        }

        /// <summary>An item at the end of the column, closing the menu and then doing <paramref name="chosen"/>; a gap above it if <paramref name="breakBefore"/>.</summary>
        protected ShellButton Item(string name, string caption, Action chosen, bool breakBefore = false)
        {
            ShellButton item = ShellTheme.Button(name, caption, _palette, () => { Close(); chosen(); });
            _popup.Panel.Add(item);
            _items.Add(item);
            if (breakBefore)
                _breakBefore.Add(item);
            return item;
        }

        /// <summary>
        /// A line of text above the items (Part 13d: what an ask asks), wrapped to the menu's width; empty text leaves it
        /// out. Its height is worked out from its length, since the popup is sized before UI Toolkit lays it out.
        /// </summary>
        protected Label Note(string name)
        {
            Label note = ShellTheme.Label(name, "", UiType.Label, _palette.Text);
            note.style.whiteSpace = WhiteSpace.Normal;
            _popup.Panel.Add(note);
            _notes.Add(note);
            return note;
        }

        /// <summary>How tall a note of <paramref name="text"/> is, wrapped to the menu's width.</summary>
        private float NoteHeight(string text)
        {
            int perLine = Math.Max(1, (int)(_itemWidth / NoteCharacter));
            int lines = 0;
            foreach (string paragraph in (text ?? "").Split('\n'))
                lines += Math.Max(1, (paragraph.Length + perLine - 1) / perLine);
            return lines * NoteLine;
        }

        /// <summary>Whether <paramref name="item"/> is on the menu the next time it opens.</summary>
        protected static void Offer(ShellButton item, bool shown) => item.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Puts the menu up with its corner at <paramref name="screenPoint"/>, the items offered laid out and the whole kept on the screen.</summary>
        protected void Show(Vector2 screenPoint)
        {
            float y = Pad;
            foreach (Label note in _notes)
            {
                if (string.IsNullOrEmpty(note.text))
                {
                    note.style.display = DisplayStyle.None;
                    continue;
                }

                note.style.display = DisplayStyle.Flex;
                float height = NoteHeight(note.text);
                ShellTheme.Place(note, Pad, y, _itemWidth, height);
                y += height + Gap * 2f;
            }

            bool first = true;
            foreach (ShellButton item in _items)
            {
                if (item.style.display == DisplayStyle.None)
                    continue;
                if (_breakBefore.Contains(item) && !first)
                    y += Gap * 2f;
                ShellTheme.Place(item, Pad, y, _itemWidth, ItemHeight);
                y += ItemHeight + Gap;
                first = false;
            }

            _popup.Open(screenPoint, new Vector2(_itemWidth + 2f * Pad, y - Gap + Pad));
            UiEscape.Opened(this);
        }

        /// <summary>Shuts it.</summary>
        public void Close() => _popup?.Close();

        private void Awake() => UiEscape.Join(this);

        private void OnDestroy() => UiEscape.Leave(this);

        private void Update()
        {
            Keyboard keyboard = UiText.Keyboard;
            if (IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame && UiEscape.TryTake(this))
                Close();
        }
    }
}
