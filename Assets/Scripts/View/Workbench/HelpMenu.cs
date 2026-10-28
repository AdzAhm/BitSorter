using System;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>What the toolbar's HELP offers (Part 15a).</summary>
    public enum HelpAction
    {
        KeyboardMap,
        StartPage,
        Tutorial,
        About,
    }

    /// <summary>
    /// HELP, in the toolbar (Part 15a): the keyboard map, as ? opens it; the start page, as HOME does; and ABOUT, the credits
    /// roll, which comes back to the workbench. A shell menu like FILE. And the guided tutorial (Part 15f).
    /// </summary>
    public sealed class HelpMenu : WaveMenu
    {
        /// <summary>The menu's name, and an item's: this, and its action.</summary>
        public const string RootName = "Workbench help menu", ActionItem = "Workbench help ";

        private const float ItemWidth = 300f;

        public static HelpMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<HelpAction> onChoose)
        {
            HelpMenu menu = Make<HelpMenu>(shell, owner, RootName, palette, ItemWidth);
            menu.Item(ActionItem + HelpAction.KeyboardMap, "KEYBOARD MAP  ( ? )", () => onChoose?.Invoke(HelpAction.KeyboardMap));
            menu.Item(ActionItem + HelpAction.StartPage, "START PAGE", () => onChoose?.Invoke(HelpAction.StartPage));
            menu.Item(ActionItem + HelpAction.Tutorial, "TUTORIAL", () => onChoose?.Invoke(HelpAction.Tutorial));
            menu.Item(ActionItem + HelpAction.About, "ABOUT BITSORTER", () => onChoose?.Invoke(HelpAction.About), breakBefore: true);
            return menu;
        }

        /// <summary>Puts it up with its corner at <paramref name="screenPoint"/>.</summary>
        public void Open(Vector2 screenPoint) => Show(screenPoint);
    }
}
