using System;
using System.Collections.Generic;
using BitSorter.ViewModel;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// FILE, OPEN (Part 15a): the student's circuits, the most recently opened first (<see cref="Documents.InOrder"/>), a
    /// click opening one. A shell menu like FILE itself, of <see cref="Slots"/> items filled each time it opens; past them,
    /// a line says how many older ones there are.
    /// </summary>
    public sealed class DocumentMenu : WaveMenu
    {
        /// <summary>The menu's name; a slot's is <see cref="SlotName"/>.</summary>
        public const string RootName = "Workbench open menu";

        /// <summary>How many circuits it lists at once.</summary>
        public const int Slots = 20;

        private const float ItemWidth = 420f;

        private readonly ShellButton[] _slots = new ShellButton[Slots];
        private readonly string[] _keys = new string[Slots];
        private UnityEngine.UIElements.Label _more;
        private Action<string> _open;

        /// <summary>The keys listed when it last opened, in their order.</summary>
        public IReadOnlyList<string> Listed { get; private set; } = Array.Empty<string>();

        /// <summary>The name of the slot <paramref name="index"/> places down.</summary>
        public static string SlotName(int index) => RootName + " " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static DocumentMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<string> open)
        {
            DocumentMenu menu = Make<DocumentMenu>(shell, owner, RootName, palette, ItemWidth);
            menu._open = open;
            menu._more = menu.Note("more");
            for (int i = 0; i < Slots; i++)
            {
                int at = i;
                menu._slots[i] = menu.Item(SlotName(i), "", () => menu._open?.Invoke(menu._keys[at]));
            }
            return menu;
        }

        /// <summary>Puts it up at <paramref name="screenPoint"/>, listing <paramref name="keys"/> in their order, <paramref name="open"/> marked.</summary>
        public void Open(Vector2 screenPoint, IReadOnlyList<string> keys, string open)
        {
            var listed = new List<string>();
            for (int i = 0; i < Slots; i++)
            {
                bool shown = i < keys.Count;
                _keys[i] = shown ? keys[i] : null;
                if (shown)
                {
                    listed.Add(keys[i]);
                    _slots[i].text = Documents.NameOf(keys[i]).ToUpperInvariant() + (keys[i] == open ? "  (OPEN NOW)" : "");
                }
                Offer(_slots[i], shown);
            }

            int older = keys.Count - Slots;
            _more.text = older > 0 ? $"and {older} older: rename or delete some to see them here" : "";
            Listed = listed;
            Show(screenPoint);
        }
    }
}
