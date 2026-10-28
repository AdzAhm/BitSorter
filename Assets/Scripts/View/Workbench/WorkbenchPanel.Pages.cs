using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// Pages of the shell's own, over the circuit and under the header, for the screens that were the 4.x game's until Part
    /// 15g: Settings and the credits. They were uGUI panels over everything, in the 4.x board's colours whatever the style,
    /// and Ahmad asked that Settings look like the start page and that changing the style through it change everything
    /// (2026-10-28). Each is built by its own controller into a page asked for here, in the style's colours. One is up at a
    /// time, over the start page or EXERCISES where one of those was up, and while one is up -- and in the frame it comes
    /// down -- the workbench reads no keys and no pointer of its own (<see cref="PageHoldsTheKeys"/>).
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        private readonly List<(VisualElement Page, Action Closed)> _pages = new List<(VisualElement, Action)>();

        /// <summary>The frame a page last came down in.</summary>
        private int _pageClosedFrame = -1;

        /// <summary>
        /// A new page, hidden, the size of the start page and in the ground's colour, for <paramref name="name"/>'s
        /// controller to build into; null before the workbench is built. <paramref name="closed"/> is how its controller
        /// takes it down when the workbench does -- another page, or a circuit, opened over it from the header -- so what
        /// the controller does on its way down is done then too.
        /// </summary>
        public VisualElement AddPage(string name, Action closed)
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
            _pages.Add((page, closed));
            return page;
        }

        /// <summary>
        /// <paramref name="page"/> up, over whatever else is and in place of any other of these pages: the popups closed, a
        /// part in hand put down, the circuit kept, and the code editor let go -- in a browser it is a box the page lays over
        /// the canvas, which would cover the page.
        /// </summary>
        public void ShowPage(VisualElement page)
        {
            if (page == null)
                return;
            HideOtherPages(page);
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
            if (page == null || page.style.display != DisplayStyle.Flex)
                return;
            page.style.display = DisplayStyle.None;
            _pageClosedFrame = UnityEngine.Time.frameCount;
        }

        /// <summary>Whether one of these pages is up.</summary>
        private bool AnotherPageShowing
        {
            get
            {
                foreach ((VisualElement page, Action _) in _pages)
                    if (page.style.display == DisplayStyle.Flex)
                        return true;
                return false;
            }
        }

        /// <summary>
        /// Whether one of these pages has the keys and the pointer: while it is up, and in the frame it comes down. The key
        /// or click that takes the credits down would otherwise reach the workbench too in the same frame, whenever the
        /// credits update first -- Delete deleting the selection, R running again; under a uGUI panel the workbench came
        /// back "opened this frame", which held them (the 15g-2 review).
        /// </summary>
        private bool PageHoldsTheKeys => AnotherPageShowing || _pageClosedFrame == UnityEngine.Time.frameCount;

        /// <summary>
        /// Every one of these pages but <paramref name="keep"/> down, each by its own controller: the start page,
        /// EXERCISES, another of them or a circuit opened from the header over one.
        /// </summary>
        private void HideOtherPages(VisualElement keep = null)
        {
            foreach ((VisualElement page, Action closed) in _pages)
            {
                if (page == keep || page.style.display != DisplayStyle.Flex)
                    continue;
                if (closed != null)
                    closed();
                else
                    HidePage(page);
            }
        }
    }
}
