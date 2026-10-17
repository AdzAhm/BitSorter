using System;
using System.Collections.Generic;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>What a row's menu does besides setting a radix.</summary>
    public enum WaveRowAction
    {
        Group,
        Ungroup,
        Rename,
        Remove,
    }

    /// <summary>
    /// The menu a right-click on a waveform row opens (Part 9b-2b): the radix the selected rows are
    /// written in, grouping them or ungrouping a group, renaming the row, and taking the rows out. A
    /// click off it closes it, as Escape does, before Escape closes the workbench.
    /// </summary>
    /// <remarks>
    /// Kept thin, in the workbench's own uGUI, because Part 11's shell replaces it; what each item does
    /// is the list's (<see cref="WaveList"/>). It holds Escape while it is open and for the rest of the
    /// frame it closes in (<see cref="IHoldsEscape"/>), so one press closes the menu and leaves the
    /// workbench up, whichever of the two Unity updates first. Its blocker over the whole screen is a
    /// button beside the menu rather than behind it, so a click on the menu's own ground stays the
    /// menu's, and the click that closes it is a control's (<see cref="WorkbenchControlSample"/>) and
    /// never seeks or selects what is under it. Items that do nothing for the rows selected -- NAMES
    /// for a row that is no enum, UNGROUP with no group selected -- are left off.
    /// </remarks>
    public sealed class WaveRowMenu : MonoBehaviour, IHoldsEscape
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench row menu";

        /// <summary>A radix item's name: this, and the radix.</summary>
        public const string RadixItem = "Workbench radix ";

        /// <summary>An action's item's name: this, and the action.</summary>
        public const string ActionItem = "Workbench row ";

        /// <inheritdoc cref="RootName"/>
        public const string RemoveItem = ActionItem + nameof(WaveRowAction.Remove);

        private const float ItemWidth = 170f, ItemHeight = 38f, Gap = 4f, Pad = 8f;

        private static readonly WaveRadix[] Radixes =
            { WaveRadix.Binary, WaveRadix.Octal, WaveRadix.Hex, WaveRadix.Unsigned, WaveRadix.Signed, WaveRadix.Symbolic };

        private RectTransform _panel;
        private readonly List<Button> _items = new List<Button>();
        private readonly HashSet<Button> _breakBefore = new HashSet<Button>();
        private Button _names, _group, _ungroup;
        private int _closedFrame = -1;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>The menu under <paramref name="root"/>, shut, calling <paramref name="onRadix"/> or <paramref name="onAction"/> as an item is chosen.</summary>
        public static WaveRowMenu Build(RectTransform root, SchematicPalette palette, Action<WaveRadix> onRadix, Action<WaveRowAction> onAction)
        {
            RectTransform container = UiTheme.Rect(RootName, root);
            UiTheme.Stretch(container);

            // A button over the whole screen, beside the menu: a click anywhere off it closes it, and is the button's.
            Image blockerImage = UiTheme.Panel_("blocker", container, new Color(0f, 0f, 0f, 0f));
            blockerImage.sprite = null;
            blockerImage.raycastTarget = true;
            var blocker = blockerImage.gameObject.AddComponent<Button>();
            blocker.targetGraphic = blockerImage;
            blocker.transition = Selectable.Transition.None;
            UiTheme.Stretch(blockerImage.rectTransform);

            var menu = container.gameObject.AddComponent<WaveRowMenu>();
            blocker.onClick.AddListener(() => { menu.Close(); UiTheme.Defocus(); });

            Image panel = WorkbenchPanel.CanvasChip("menu", container, palette);
            panel.raycastTarget = true;
            menu._panel = panel.rectTransform;
            menu._panel.anchorMin = menu._panel.anchorMax = new Vector2(0f, 1f);
            menu._panel.pivot = new Vector2(0f, 1f);

            Button Item(string name, string caption, Action chosen)
            {
                Button item = WorkbenchPanel.CanvasButton(name, menu._panel, caption, out TextMeshProUGUI _, UiType.Body, palette);
                WorkbenchPanel.TopLeft(item.GetComponent<RectTransform>(), Vector2.zero, new Vector2(ItemWidth, ItemHeight));
                item.onClick.AddListener(() => { menu.Close(); chosen(); UiTheme.Defocus(); });
                menu._items.Add(item);
                return item;
            }

            foreach (WaveRadix radix in Radixes)
            {
                WaveRadix chosen = radix;
                Button item = Item(RadixItem + radix, Caption(radix), () => onRadix?.Invoke(chosen));
                if (radix == WaveRadix.Symbolic)
                    menu._names = item;
            }

            menu._group = Item(ActionItem + WaveRowAction.Group, "GROUP", () => onAction?.Invoke(WaveRowAction.Group));
            menu._breakBefore.Add(menu._group);
            menu._ungroup = Item(ActionItem + WaveRowAction.Ungroup, "UNGROUP", () => onAction?.Invoke(WaveRowAction.Ungroup));
            Item(ActionItem + WaveRowAction.Rename, "RENAME", () => onAction?.Invoke(WaveRowAction.Rename));
            Button remove = Item(RemoveItem, "REMOVE", () => onAction?.Invoke(WaveRowAction.Remove));
            menu._breakBefore.Add(remove);

            menu.gameObject.SetActive(false);
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
        /// for rows of an enum's type, GROUP only with signals selected, UNGROUP only with a group selected.
        /// </summary>
        public void Open(Vector2 screenPoint, bool enumRows, bool canGroup, bool canUngroup)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            WorkbenchPanel.Show(_names, enumRows);
            WorkbenchPanel.Show(_group, canGroup);
            WorkbenchPanel.Show(_ungroup, canUngroup);

            float y = Pad;
            foreach (Button item in _items)
            {
                if (!item.gameObject.activeSelf)
                    continue;
                if (_breakBefore.Contains(item))
                    y += Gap * 2f;
                item.GetComponent<RectTransform>().anchoredPosition = new Vector2(Pad, -y);
                y += ItemHeight + Gap;
            }

            var size = new Vector2(ItemWidth + 2f * Pad, y - Gap + Pad);
            _panel.sizeDelta = size;

            var root = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, null, out Vector2 local);
            Rect area = root.rect;
            float x = Mathf.Clamp(local.x - area.xMin, 0f, Mathf.Max(0f, area.width - size.x));
            float down = Mathf.Clamp(area.yMax - local.y, 0f, Mathf.Max(0f, area.height - size.y));
            _panel.anchoredPosition = new Vector2(x, -down);

            UiEscape.Opened(this);
        }

        /// <summary>Shuts it.</summary>
        public void Close()
        {
            if (!IsOpen)
                return;
            _closedFrame = Time.frameCount;
            gameObject.SetActive(false);
        }

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
