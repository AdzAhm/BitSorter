using System;
using System.Collections.Generic;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// The menu a right-click on a waveform row opens (Part 9b-2b): the radix the selected rows are
    /// written in, and taking them out. A click outside closes it, as Escape does, before Escape
    /// closes the workbench.
    /// </summary>
    /// <remarks>
    /// Kept thin, in the workbench's own uGUI, because Part 11's shell replaces it; what each item does
    /// is the list's (<see cref="WaveList"/>). It holds Escape while it is open and for the rest of the
    /// frame it closes in (<see cref="IHoldsEscape"/>), so one press closes the menu and leaves the
    /// workbench up, whichever of the two Unity updates first. Its blocker over the whole screen is a
    /// button, so the click that closes it is a control's (<see cref="WorkbenchControlSample"/>) and
    /// never seeks or selects what is under it.
    /// </remarks>
    public sealed class WaveRowMenu : MonoBehaviour, IHoldsEscape
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench row menu";

        /// <summary>A radix item's name: this, and the radix.</summary>
        public const string RadixItem = "Workbench radix ";

        /// <inheritdoc cref="RootName"/>
        public const string RemoveItem = "Workbench remove rows";

        private const float ItemWidth = 170f, ItemHeight = 38f, Gap = 4f, Pad = 8f;

        private static readonly WaveRadix[] Radixes =
            { WaveRadix.Binary, WaveRadix.Octal, WaveRadix.Hex, WaveRadix.Unsigned, WaveRadix.Signed, WaveRadix.Symbolic };

        private RectTransform _panel;
        private Button _names;
        private readonly List<Button> _items = new List<Button>();
        private Action<WaveRadix> _onRadix;
        private Action _onRemove;
        private int _closedFrame = -1;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>The menu under <paramref name="root"/>, shut, calling <paramref name="onRadix"/> or <paramref name="onRemove"/> as an item is chosen.</summary>
        public static WaveRowMenu Build(RectTransform root, SchematicPalette palette, Action<WaveRadix> onRadix, Action onRemove)
        {
            // A button over the whole screen: a click anywhere off the menu closes it, and is the button's.
            Image blockerImage = UiTheme.Panel_(RootName, root, new Color(0f, 0f, 0f, 0f));
            blockerImage.sprite = null;
            blockerImage.raycastTarget = true;
            var blocker = blockerImage.gameObject.AddComponent<Button>();
            blocker.targetGraphic = blockerImage;
            blocker.transition = Selectable.Transition.None;
            UiTheme.Stretch(blockerImage.rectTransform);

            var menu = blocker.gameObject.AddComponent<WaveRowMenu>();
            menu._onRadix = onRadix;
            menu._onRemove = onRemove;
            blocker.onClick.AddListener(() => { menu.Close(); UiTheme.Defocus(); });

            Image panel = WorkbenchPanel.CanvasChip("menu", blocker.transform, palette);
            menu._panel = panel.rectTransform;
            menu._panel.anchorMin = menu._panel.anchorMax = new Vector2(0f, 1f);
            menu._panel.pivot = new Vector2(0f, 1f);

            float y = Pad;
            foreach (WaveRadix radix in Radixes)
            {
                Button item = WorkbenchPanel.CanvasButton(RadixItem + radix, menu._panel, Caption(radix), out TextMeshProUGUI _, UiType.Body, palette);
                WorkbenchPanel.TopLeft(item.GetComponent<RectTransform>(), new Vector2(Pad, y), new Vector2(ItemWidth, ItemHeight));
                WaveRadix chosen = radix;
                item.onClick.AddListener(() => { menu.Close(); menu._onRadix?.Invoke(chosen); UiTheme.Defocus(); });
                menu._items.Add(item);
                if (radix == WaveRadix.Symbolic)
                    menu._names = item;
                y += ItemHeight + Gap;
            }

            Button remove = WorkbenchPanel.CanvasButton(RemoveItem, menu._panel, "REMOVE", out TextMeshProUGUI _, UiType.Body, palette);
            WorkbenchPanel.TopLeft(remove.GetComponent<RectTransform>(), new Vector2(Pad, y + Gap), new Vector2(ItemWidth, ItemHeight));
            remove.onClick.AddListener(() => { menu.Close(); menu._onRemove?.Invoke(); UiTheme.Defocus(); });
            menu._items.Add(remove);

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
        /// Opens the menu with its corner at <paramref name="screenPoint"/>, kept on the screen;
        /// NAMES offered only for <paramref name="enumRows"/>, rows of an enum's type.
        /// </summary>
        public void Open(Vector2 screenPoint, bool enumRows)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            WorkbenchPanel.Show(_names, enumRows);

            // Laid out again without NAMES when it is not offered.
            float y = Pad;
            foreach (Button item in _items)
            {
                if (!item.gameObject.activeSelf)
                    continue;
                if (item == _items[_items.Count - 1])
                    y += Gap;
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
