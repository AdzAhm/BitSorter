using System;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>What the menu for a moment does.</summary>
    public enum WaveTimeAction
    {
        /// <summary>Set a breakpoint at the moment, or take it away.</summary>
        ToggleBreak,

        /// <summary>Take every breakpoint away.</summary>
        ClearBreaks,
    }

    /// <summary>
    /// The menu a right-click on the waves or the time axis opens (Part 9b-3): a breakpoint at that
    /// moment, set or taken away, and every breakpoint taken away when there are any. Everything a
    /// workbench menu does besides is <see cref="WaveMenu"/>'s.
    /// </summary>
    public sealed class WaveTimeMenu : WaveMenu
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench time menu";

        /// <summary>An item's name: this, and what it does.</summary>
        public const string ActionItem = "Workbench time ";

        private const float ItemWidth = 230f;

        private ShellButton _toggle, _clear;

        /// <summary>The menu on <paramref name="shell"/>, shut, calling <paramref name="onAction"/> as an item is chosen.</summary>
        public static WaveTimeMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<WaveTimeAction> onAction)
        {
            WaveTimeMenu menu = Make<WaveTimeMenu>(shell, owner, RootName, palette, ItemWidth);
            menu._toggle = menu.Item(ActionItem + WaveTimeAction.ToggleBreak, "BREAK HERE", () => onAction?.Invoke(WaveTimeAction.ToggleBreak));
            menu._clear = menu.Item(ActionItem + WaveTimeAction.ClearBreaks, "NO BREAKS AT ALL", () => onAction?.Invoke(WaveTimeAction.ClearBreaks), breakBefore: true);
            return menu;
        }

        /// <summary>
        /// Opens the menu at <paramref name="screenPoint"/> for a moment written <paramref name="when"/>:
        /// to take its breakpoint away if <paramref name="hasBreak"/>, else to set one; and to take every
        /// breakpoint away only if there are any.
        /// </summary>
        public void Open(Vector2 screenPoint, string when, bool hasBreak, bool anyBreaks)
        {
            _toggle.text = (hasBreak ? "NO BREAK AT " : "BREAK AT ") + when;
            Offer(_clear, anyBreaks);
            Show(screenPoint);
        }
    }
}
