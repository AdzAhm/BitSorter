using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// What every popup of the workbench is in the shell (Part 11b-3): a menu, a field asking for one line, the + SIGNAL
    /// list. A layer over the whole window, taking the press that closes it, and a panel on it, put up where it was asked
    /// for and kept on the screen.
    /// </summary>
    /// <remarks>
    /// **The layer is a control** (<see cref="ShellTheme.ControlClass"/>): the press that closes a popup is the layer's,
    /// so it never seeks the waves or selects on the circuit under it (<see cref="WorkbenchControlSample"/>), as the uGUI
    /// blocker before it was a button. **The panel takes the presses on its own ground**, so a press between its items is
    /// the panel's and never reaches the layer that closes it. Drawn in the shell, it is over every panel of the docked
    /// layout: the uGUI popups were drawn under the shell, and needed a canvas of their own sorted over it (Part 11b-1).
    /// </remarks>
    public sealed class ShellPopup
    {
        /// <summary>The layer over the whole window that a press off the panel lands on, and the panel on it.</summary>
        public VisualElement Layer { get; }

        /// <inheritdoc cref="Layer"/>
        public VisualElement Panel { get; }

        /// <summary>Whether it is up.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The frame it last closed in, or -1: a popup holds Escape for the rest of that frame (<see cref="IHoldsEscape"/>).</summary>
        public int ClosedFrame { get; private set; } = -1;

        /// <summary>Called once a press off the panel has closed it.</summary>
        public event Action Dismissed;

        /// <summary>A popup named <paramref name="name"/> on <paramref name="shell"/>, shut.</summary>
        public ShellPopup(ShellPanel shell, string name, SchematicPalette palette)
        {
            Layer = new VisualElement { name = name };
            Layer.AddToClassList(ShellTheme.ControlClass);
            Layer.style.position = Position.Absolute;
            Layer.style.left = Layer.style.top = Layer.style.right = Layer.style.bottom = 0f;
            Layer.style.display = DisplayStyle.None;
            Layer.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target != Layer)
                    return;
                Close();
                e.StopPropagation();
                Dismissed?.Invoke();
            });

            Panel = ShellTheme.Chip("panel", palette);
            Panel.pickingMode = PickingMode.Position;
            Panel.style.position = Position.Absolute;
            Layer.Add(Panel);
            shell.Root.Add(Layer);
        }

        /// <summary>Puts it up, over everything else in the shell, its panel <paramref name="size"/> with its corner at <paramref name="screenPoint"/> and kept on the screen.</summary>
        public void Open(Vector2 screenPoint, Vector2 size)
        {
            IsOpen = true;
            Layer.style.display = DisplayStyle.Flex;
            Layer.BringToFront();
            Place(screenPoint, size);
        }

        /// <summary>The panel made <paramref name="size"/>, its corner at <paramref name="screenPoint"/> (pixels, y up) and the whole of it on the screen.</summary>
        public void Place(Vector2 screenPoint, Vector2 size)
        {
            float scale = Layer.panel != null ? Layer.panel.scaledPixelsPerPoint : 1f;
            float width = Screen.width / scale, height = Screen.height / scale;
            float x = Mathf.Clamp(screenPoint.x / scale, 0f, Mathf.Max(0f, width - size.x));
            float y = Mathf.Clamp((Screen.height - screenPoint.y) / scale, 0f, Mathf.Max(0f, height - size.y));
            ShellTheme.Place(Panel, x, y, size.x, size.y);
        }

        /// <summary>The panel made <paramref name="size"/> where it stands, still kept on the screen.</summary>
        public void Resize(Vector2 size)
        {
            float scale = Layer.panel != null ? Layer.panel.scaledPixelsPerPoint : 1f;
            float x = Mathf.Min(Panel.style.left.value.value, Mathf.Max(0f, Screen.width / scale - size.x));
            float y = Mathf.Min(Panel.style.top.value.value, Mathf.Max(0f, Screen.height / scale - size.y));
            ShellTheme.Place(Panel, x, y, size.x, size.y);
        }

        /// <summary>Shuts it, and lets go of any field in it.</summary>
        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            if (Layer.focusController?.focusedElement is VisualElement focused && Layer.Contains(focused))
                focused.Blur();
            Layer.style.display = DisplayStyle.None;
        }
    }
}
