using System;
using BitSorter.ViewModel;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>What the canvas's menu does (Part 10d).</summary>
    public enum CanvasMenuAction
    {
        AlignLeft,
        AlignRight,
        AlignTop,
        AlignBottom,
        CentresAcross,
        CentresDown,
        SpreadAcross,
        SpreadDown,
        Turn,
        Mirror,
        RouteAgain,
        Copy,
        Cut,
        Paste,
        Delete,
    }

    /// <summary>
    /// The menu a right-click on the circuit opens (Part 10d): line the selected parts up by an edge or their middles,
    /// spread them evenly, turn or mirror them, put the selected wires back on the automatic route, copy, cut, paste and
    /// take away. What every item does is the canvas editor's (<see cref="CanvasEditor"/>); what every workbench menu does
    /// -- its blocker, Escape, staying on the screen -- is <see cref="WaveMenu"/>'s. Items that do nothing for what is
    /// selected are left off.
    /// </summary>
    public sealed class CanvasMenu : WaveMenu
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench canvas menu";

        /// <summary>An item's name: this, and the action.</summary>
        public const string ActionItem = "Workbench canvas ";

        private const float ItemWidth = 230f;

        private readonly ShellButton[] _items = new ShellButton[Enum.GetValues(typeof(CanvasMenuAction)).Length];

        /// <summary>The menu on <paramref name="shell"/>, shut, calling <paramref name="onAction"/> as an item is chosen.</summary>
        public static CanvasMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<CanvasMenuAction> onAction)
        {
            CanvasMenu menu = Make<CanvasMenu>(shell, owner, RootName, palette, ItemWidth);

            void Add(CanvasMenuAction action, string caption, bool breakBefore = false) =>
                menu._items[(int)action] = menu.Item(ActionItem + action, caption, () => onAction?.Invoke(action), breakBefore);

            Add(CanvasMenuAction.AlignLeft, "ALIGN LEFT");
            Add(CanvasMenuAction.AlignRight, "ALIGN RIGHT");
            Add(CanvasMenuAction.AlignTop, "ALIGN TOP");
            Add(CanvasMenuAction.AlignBottom, "ALIGN BOTTOM");
            Add(CanvasMenuAction.CentresAcross, "MIDDLES IN A COLUMN");
            Add(CanvasMenuAction.CentresDown, "MIDDLES IN A ROW");
            Add(CanvasMenuAction.SpreadAcross, "SPREAD ACROSS", breakBefore: true);
            Add(CanvasMenuAction.SpreadDown, "SPREAD DOWN");
            Add(CanvasMenuAction.Turn, "TURN", breakBefore: true);
            Add(CanvasMenuAction.Mirror, "MIRROR");
            Add(CanvasMenuAction.RouteAgain, "ROUTE AGAIN");
            Add(CanvasMenuAction.Copy, "COPY", breakBefore: true);
            Add(CanvasMenuAction.Cut, "CUT");
            Add(CanvasMenuAction.Paste, "PASTE");
            Add(CanvasMenuAction.Delete, "DELETE", breakBefore: true);
            return menu;
        }

        /// <summary>
        /// Opens the menu with its corner at <paramref name="screenPoint"/> for <paramref name="parts"/> parts and
        /// <paramref name="wires"/> wires selected, PASTE only when <paramref name="canPaste"/>. False, and shut, with
        /// nothing it could offer.
        /// </summary>
        public bool Open(Vector2 screenPoint, int parts, int wires, bool canPaste)
        {
            void Offer(CanvasMenuAction action, bool shown) => WaveMenu.Offer(_items[(int)action], shown);

            bool lineUp = parts >= 2, spread = parts >= 3;
            Offer(CanvasMenuAction.AlignLeft, lineUp);
            Offer(CanvasMenuAction.AlignRight, lineUp);
            Offer(CanvasMenuAction.AlignTop, lineUp);
            Offer(CanvasMenuAction.AlignBottom, lineUp);
            Offer(CanvasMenuAction.CentresAcross, lineUp);
            Offer(CanvasMenuAction.CentresDown, lineUp);
            Offer(CanvasMenuAction.SpreadAcross, spread);
            Offer(CanvasMenuAction.SpreadDown, spread);
            Offer(CanvasMenuAction.Turn, parts > 0);
            Offer(CanvasMenuAction.Mirror, parts > 0);
            Offer(CanvasMenuAction.RouteAgain, wires > 0);
            Offer(CanvasMenuAction.Copy, parts > 0);
            Offer(CanvasMenuAction.Cut, parts > 0);
            Offer(CanvasMenuAction.Paste, canPaste);
            Offer(CanvasMenuAction.Delete, parts + wires > 0);

            if (parts + wires == 0 && !canPaste)
                return false;
            Show(screenPoint);
            return true;
        }
    }
}
