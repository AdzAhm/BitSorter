using System;
using BitSorter.ViewModel;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>What a row's menu does besides setting a radix.</summary>
    public enum WaveRowAction
    {
        Group,
        Ungroup,
        Rename,
        Remove,
        BreakOnX,
        BreakOnValue,
        NoBreaks,
    }

    /// <summary>
    /// The menu a right-click on a waveform row opens (Part 9b-2b): the radix the selected rows are
    /// written in, grouping them or ungrouping a group, renaming the row, breakpoints on the row's
    /// signal (Part 9b-3), and taking the rows out.
    /// </summary>
    /// <remarks>
    /// What every workbench menu does -- its blocker, Escape, staying on the screen -- is
    /// <see cref="WaveMenu"/>'s; what each item does is the list's (<see cref="WaveList"/>) and the
    /// breakpoints' (<see cref="Breakpoints"/>). Items that do nothing for the rows selected -- NAMES
    /// for a row that is no enum, UNGROUP with no group selected, NO BREAKS on a row with none -- are
    /// left off.
    /// </remarks>
    public sealed class WaveRowMenu : WaveMenu
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench row menu";

        /// <summary>A radix item's name: this, and the radix.</summary>
        public const string RadixItem = "Workbench radix ";

        /// <summary>An action's item's name: this, and the action.</summary>
        public const string ActionItem = "Workbench row ";

        /// <inheritdoc cref="RootName"/>
        public const string RemoveItem = ActionItem + nameof(WaveRowAction.Remove);

        private const float ItemWidth = 190f;

        private static readonly WaveRadix[] Radixes =
            { WaveRadix.Binary, WaveRadix.Octal, WaveRadix.Hex, WaveRadix.Unsigned, WaveRadix.Signed, WaveRadix.Symbolic };

        private ShellButton _names, _group, _ungroup, _breakOnX, _breakOnValue, _noBreaks;

        /// <summary>The menu on <paramref name="shell"/>, shut, calling <paramref name="onRadix"/> or <paramref name="onAction"/> as an item is chosen.</summary>
        public static WaveRowMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<WaveRadix> onRadix,
            Action<WaveRowAction> onAction)
        {
            WaveRowMenu menu = Make<WaveRowMenu>(shell, owner, RootName, palette, ItemWidth);

            foreach (WaveRadix radix in Radixes)
            {
                WaveRadix chosen = radix;
                ShellButton item = menu.Item(RadixItem + radix, Caption(radix), () => onRadix?.Invoke(chosen));
                if (radix == WaveRadix.Symbolic)
                    menu._names = item;
            }

            ShellButton Add(WaveRowAction action, string caption, bool breakBefore = false) =>
                menu.Item(ActionItem + action, caption, () => onAction?.Invoke(action), breakBefore);

            menu._group = Add(WaveRowAction.Group, "GROUP", breakBefore: true);
            menu._ungroup = Add(WaveRowAction.Ungroup, "UNGROUP");
            Add(WaveRowAction.Rename, "RENAME");
            menu._breakOnX = Add(WaveRowAction.BreakOnX, "BREAK ON X", breakBefore: true);
            menu._breakOnValue = Add(WaveRowAction.BreakOnValue, "BREAK ON VALUE");
            menu._noBreaks = Add(WaveRowAction.NoBreaks, "NO BREAKS");
            Add(WaveRowAction.Remove, "REMOVE", breakBefore: true);
            return menu;
        }

        /// <summary>What a radix is called on the menu.</summary>
        public static string Caption(WaveRadix radix)
        {
            switch (radix)
            {
                case WaveRadix.Binary: return "BINARY";
                case WaveRadix.Octal: return "OCTAL";
                case WaveRadix.Hex: return "HEX";
                case WaveRadix.Unsigned: return "UNSIGNED";
                case WaveRadix.Signed: return "SIGNED";
                default: return "NAMES";
            }
        }

        /// <summary>
        /// Opens the menu with its corner at <paramref name="screenPoint"/>, kept on the screen: NAMES only
        /// for rows of an enum's type, GROUP only with signals selected, UNGROUP only with a group selected,
        /// the breakpoints only on a signal's row and NO BREAKS only where the signal has one.
        /// </summary>
        public void Open(Vector2 screenPoint, bool enumRows, bool canGroup, bool canUngroup, bool canBreak = false, bool hasBreaks = false)
        {
            Offer(_names, enumRows);
            Offer(_group, canGroup);
            Offer(_ungroup, canUngroup);
            Offer(_breakOnX, canBreak);
            Offer(_breakOnValue, canBreak);
            Offer(_noBreaks, canBreak && hasBreaks);
            Show(screenPoint);
        }
    }
}
