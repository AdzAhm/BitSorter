using System;
using System.Collections.Generic;
using BitSorter.ViewModel;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// FILE, OPEN (Part 15a): the student's circuits, the most recently opened first (<see cref="Documents.InOrder"/>), a
    /// click opening one. A shell menu like FILE itself, of <see cref="Slots"/> items filled each time it opens, and a page
    /// of them at a time: OLDER and NEWER step through the rest. It stopped at twenty with nothing past them, and with every
    /// import a circuit of its own a term's homework reaches that (the #83 review).
    /// </summary>
    public sealed class DocumentMenu : WaveMenu
    {
        /// <summary>The menu's name; a slot's is <see cref="SlotName"/>, and its two steps' these.</summary>
        public const string RootName = "Workbench open menu", OlderItem = RootName + " older", NewerItem = RootName + " newer";

        /// <summary>How many circuits it lists at once.</summary>
        public const int Slots = 12;

        private const float ItemWidth = 420f;

        /// <summary>What a slot's caption has room for: the item's width, less its padding.</summary>
        private const float CaptionWidth = ItemWidth - 40f;

        private const string OpenNow = "  (OPEN NOW)";

        private readonly ShellButton[] _slots = new ShellButton[Slots];
        private readonly string[] _keys = new string[Slots];
        private ShellButton _newer, _older;
        private UnityEngine.UIElements.Label _page;
        private Action<string> _open;
        private IReadOnlyList<string> _all = Array.Empty<string>();
        private string _openNow;
        private Vector2 _at;
        private int _first;

        /// <summary>The keys listed on the page shown when it last opened, in their order.</summary>
        public IReadOnlyList<string> Listed { get; private set; } = Array.Empty<string>();

        /// <summary>Whether OLDER, and NEWER, were offered when it last opened.</summary>
        public bool OffersOlder => _older != null && _older.style.display != UnityEngine.UIElements.DisplayStyle.None;

        /// <inheritdoc cref="OffersOlder"/>
        public bool OffersNewer => _newer != null && _newer.style.display != UnityEngine.UIElements.DisplayStyle.None;

        /// <summary>The name of the slot <paramref name="index"/> places down.</summary>
        public static string SlotName(int index) => RootName + " " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static DocumentMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<string> open)
        {
            DocumentMenu menu = Make<DocumentMenu>(shell, owner, RootName, palette, ItemWidth);
            menu._open = open;
            menu._page = menu.Note("page");
            menu._newer = menu.Item(NewerItem, "NEWER CIRCUITS", () => menu.Step(-1));
            for (int i = 0; i < Slots; i++)
            {
                int at = i;
                menu._slots[i] = menu.Item(SlotName(i), "", () => menu._open?.Invoke(menu._keys[at]), breakBefore: i == 0);
            }
            menu._older = menu.Item(OlderItem, "OLDER CIRCUITS", () => menu.Step(1), breakBefore: true);
            return menu;
        }

        /// <summary>Puts it up at <paramref name="screenPoint"/>, listing <paramref name="keys"/> in their order from the first, <paramref name="open"/> marked.</summary>
        public void Open(Vector2 screenPoint, IReadOnlyList<string> keys, string open)
        {
            _all = keys ?? Array.Empty<string>();
            _openNow = open;
            _at = screenPoint;
            _first = 0;
            Fill();
        }

        /// <summary>The next page, older or newer by <paramref name="pages"/>, in the same place: a step closes the menu, as any item does.</summary>
        private void Step(int pages)
        {
            _first = Math.Max(0, Math.Min(_first + pages * Slots, (_all.Count - 1) / Slots * Slots));
            Fill();
        }

        private void Fill()
        {
            var listed = new List<string>();
            for (int i = 0; i < Slots; i++)
            {
                int at = _first + i;
                bool shown = at < _all.Count;
                _keys[i] = shown ? _all[at] : null;
                if (shown)
                {
                    listed.Add(_all[at]);
                    string suffix = _all[at] == _openNow ? OpenNow : "";
                    _slots[i].text = UiTheme.Shortened(Documents.NameOf(_all[at]).ToUpperInvariant(), CaptionWidth - UiTheme.TextWidth(suffix, UiType.Body), UiType.Body) + suffix;
                }
                Offer(_slots[i], shown);
            }

            Offer(_newer, _first > 0);
            Offer(_older, _first + Slots < _all.Count);
            _page.text = _all.Count > Slots
                ? "circuits " + (_first + 1) + " to " + Math.Min(_first + Slots, _all.Count) + " of " + _all.Count + ", the latest opened first"
                : "";
            Listed = listed;
            Show(_at);
        }
    }
}
