using System.Collections.Generic;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// Pages of the shell's own, over the circuit and under the header, for the screens that were the 4.x game's until Part
    /// 15g: Settings and the credits. They were uGUI panels over everything, in the 4.x board's colours whatever the style,
    /// and Ahmad asked that Settings look like the start page and that changing the style through it change everything
    /// (2026-10-28). Each is built by its own controller into a page asked for here, in the style's colours, and while one
    /// is up the workbench reads no keys and no pointer of its own, as for the start page (<see cref="PageShowing"/>).
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        private readonly List<VisualElement> _pages = new List<VisualElement>();

        /// <summary>
        /// A new page, hidden, the size of the start page and in the ground's colour, for <paramref name="name"/>'s
        /// controller to build into; null before the workbench is built.
        /// </summary>
        public VisualElement AddPage(string name)
        {
            if (_shell == null)
                return null;

            var page = new VisualElement { name = name, pickingMode = PickingMode.Position };
            page.style.position = Position.Absolute;
            page.style.left = 0f;
            page.style.right = 0f;
            page.style.top = TitleTop + HeaderHeight;
            page.style.bottom = 0f;
            page.style.backgroundColor = SchematicPalette.Current.Ground;
            page.style.display = DisplayStyle.None;
            _shell.Root.Add(page);
            _pages.Add(page);
            return page;
        }

        /// <summary>
        /// <paramref name="page"/> up, over whatever else is: the popups closed, a part in hand put down, the circuit kept,
        /// and the code editor let go -- in a browser it is a box the page lays over the canvas, which would cover the page.
        /// </summary>
        public void ShowPage(VisualElement page)
        {
            if (page == null)
                return;
            CloseThePopups();
            LetGoOnCanvas();
            _codeView?.Bind(null);
            KeepMyCircuit(now: true);
            page.style.display = DisplayStyle.Flex;
            page.BringToFront();
        }

        /// <summary><paramref name="page"/> down, back to what is under it.</summary>
        public void HidePage(VisualElement page)
        {
            if (page != null)
                page.style.display = DisplayStyle.None;
        }

        /// <summary>Whether one of these pages is up.</summary>
        private bool AnotherPageShowing
        {
            get
            {
                foreach (VisualElement page in _pages)
                    if (page.style.display == DisplayStyle.Flex)
                        return true;
                return false;
            }
        }

        /// <summary>Every one of these pages down: the start page or EXERCISES opened from the header over one.</summary>
        private void HideOtherPages()
        {
            foreach (VisualElement page in _pages)
                page.style.display = DisplayStyle.None;
        }
    }
}
