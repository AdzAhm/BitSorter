using System;
using BitSorter.Board;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// A testbench's choice (Part 15h-6c) -- a processor's PROGRAM -- as the chip on the controls' row opens it: each of its
    /// enum's members, the one running marked, a click choosing one. A shell menu like FILE's, of <see cref="Slots"/> items
    /// filled each time it opens, as OPEN's are.
    /// </summary>
    public sealed class ChoiceMenu : WaveMenu
    {
        /// <summary>The menu's name; a member's item is <see cref="SlotName"/>.</summary>
        public const string RootName = "Workbench choice menu";

        /// <summary>How many members it can list.</summary>
        public const int Slots = 8;

        private const float ItemWidth = 280f;
        private const string Running = "  (RUNNING)";

        private readonly ShellButton[] _slots = new ShellButton[Slots];
        private readonly string[] _members = new string[Slots];
        private Action<string, string> _choose;
        private string _name;

        /// <summary>The name of the item for member <paramref name="index"/>.</summary>
        public static string SlotName(int index) => RootName + " " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static ChoiceMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<string, string> choose)
        {
            ChoiceMenu menu = Make<ChoiceMenu>(shell, owner, RootName, palette, ItemWidth);
            menu._choose = choose;
            for (int i = 0; i < Slots; i++)
            {
                int at = i;
                menu._slots[i] = menu.Item(SlotName(i), "", () => menu._choose?.Invoke(menu._name, menu._members[at]));
            }
            return menu;
        }

        /// <summary>Puts it up at <paramref name="screenPoint"/>, listing <paramref name="choice"/>'s members, the chosen one marked.</summary>
        public void Open(Vector2 screenPoint, BenchChoice choice)
        {
            _name = choice?.Name;
            for (int i = 0; i < Slots; i++)
            {
                bool shown = choice != null && i < choice.Members.Count;
                _members[i] = shown ? choice.Members[i] : null;
                if (shown)
                    _slots[i].text = BenchChoice.Said(choice.Members[i]).ToUpperInvariant() + (choice.Members[i] == choice.Chosen ? Running : "");
                Offer(_slots[i], shown);
            }
            Show(screenPoint);
        }
    }
}
