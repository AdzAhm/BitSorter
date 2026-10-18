using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// What the workbench's menus have in common (Part 9b-2b, shared in 9b-3): a column of items over
    /// the stage, put up where it was asked for and kept on the screen, closed by a click off it or by
    /// Escape before Escape closes the workbench.
    /// </summary>
    /// <remarks>
    /// Kept thin, in the workbench's own uGUI, because Part 11's shell replaces it. It holds Escape while
    /// it is open and for the rest of the frame it closes in (<see cref="IHoldsEscape"/>), so one press
    /// closes the menu and leaves the workbench up, whichever of the two Unity updates first. Its
    /// blocker over the whole screen is a button beside the menu rather than behind it, so a click on
    /// the menu's own ground stays the menu's, and the click that closes it is a control's
    /// (<see cref="WorkbenchControlSample"/>) and never seeks or selects what is under it. Items that do
    /// nothing for what was clicked are left off rather than greyed.
    /// </remarks>
    public abstract class WaveMenu : MonoBehaviour, IHoldsEscape
    {
        private const float ItemHeight = 38f, Gap = 4f, Pad = 8f;

        private RectTransform _panel;
        private SchematicPalette _palette;
        private float _itemWidth;
        private readonly List<Button> _items = new List<Button>();
        private readonly HashSet<Button> _breakBefore = new HashSet<Button>();
        private int _closedFrame = -1;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>A menu of kind <typeparamref name="T"/> under <paramref name="root"/>, named <paramref name="name"/>, its items <paramref name="itemWidth"/> wide.</summary>
        protected static T Make<T>(RectTransform root, string name, SchematicPalette palette, float itemWidth) where T : WaveMenu
        {
            RectTransform container = UiTheme.Rect(name, root);
            UiTheme.Stretch(container);

            // A button over the whole screen, beside the menu: a click anywhere off it closes it, and is the button's.
            Image blockerImage = UiTheme.Panel_("blocker", container, new Color(0f, 0f, 0f, 0f));
            blockerImage.sprite = null;
            blockerImage.raycastTarget = true;
            var blocker = blockerImage.gameObject.AddComponent<Button>();
            blocker.targetGraphic = blockerImage;
            blocker.transition = Selectable.Transition.None;
            UiTheme.Stretch(blockerImage.rectTransform);

            var menu = container.gameObject.AddComponent<T>();
            blocker.onClick.AddListener(() => { menu.Close(); UiTheme.Defocus(); });

            Image panel = WorkbenchPanel.CanvasChip("menu", container, palette);
            panel.raycastTarget = true;
            menu._panel = panel.rectTransform;
            menu._panel.anchorMin = menu._panel.anchorMax = new Vector2(0f, 1f);
            menu._panel.pivot = new Vector2(0f, 1f);
            menu._palette = palette;
            menu._itemWidth = itemWidth;
            return menu;
        }

        /// <summary>An item at the end of the column, closing the menu and then doing <paramref name="chosen"/>; a gap above it if <paramref name="breakBefore"/>.</summary>
        protected Button Item(string name, string caption, Action chosen, bool breakBefore = false)
        {
            Button item = WorkbenchPanel.CanvasButton(name, _panel, caption, out TextMeshProUGUI _, UiType.Body, _palette);
            WorkbenchPanel.TopLeft(item.GetComponent<RectTransform>(), Vector2.zero, new Vector2(_itemWidth, ItemHeight));
            item.onClick.AddListener(() => { Close(); chosen(); UiTheme.Defocus(); });
            _items.Add(item);
            if (breakBefore)
                _breakBefore.Add(item);
            return item;
        }

        /// <summary>Puts the menu up with its corner at <paramref name="screenPoint"/>, the items shown laid out and the whole kept on the screen.</summary>
        protected void Show(Vector2 screenPoint)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            float y = Pad;
            bool first = true;
            foreach (Button item in _items)
            {
                if (!item.gameObject.activeSelf)
                    continue;
                if (_breakBefore.Contains(item) && !first)
                    y += Gap * 2f;
                item.GetComponent<RectTransform>().anchoredPosition = new Vector2(Pad, -y);
                y += ItemHeight + Gap;
                first = false;
            }

            var size = new Vector2(_itemWidth + 2f * Pad, y - Gap + Pad);
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
