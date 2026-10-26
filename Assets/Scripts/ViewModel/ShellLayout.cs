namespace BitSorter.ViewModel
{
    /// <summary>
    /// Where each of the workbench's docked panels is, in a window of a given size: plain arithmetic, so the rules
    /// are held by tests under <c>dotnet test</c> rather than by dragging a window's edge.
    /// </summary>
    /// <remarks>
    /// **The docked layout** (Part 11b), in the window's own units, y down from its top-left corner, under the
    /// toolbar:
    /// - the **inspector** bar, the window's whole width: the selection's every field (11b-2);
    /// - the **parts** panel down the left and the **side** panel down the right, each a fixed width;
    /// - the **circuit's** window between them, taking what they leave;
    /// - the **splitter** under all three, which a drag moves;
    /// - the **dock** under it, the window's whole width: its header (the tabs and the waveform's buttons), the
    ///   **waves**, and the **legend** at its foot;
    /// - the **status** line along the bottom.
    ///
    /// **Panels keep their size in a small window**, as an editor's do (Ahmad's choice, 2026-10-25): the circuit's
    /// window and the waves take what is left, and the circuit fits its window by zoom, so no word is drawn smaller.
    /// **The dock's height is the student's**, dragged on the splitter, and is held between the least that shows a
    /// row or two of waves and the most that leaves the circuit <see cref="MinCircuit"/> (<see cref="ClampDock"/>);
    /// where a window is too short for both, the dock keeps its least and the circuit gives way. Until it is dragged it
    /// is half the room under the inspector, and no taller than the reference circuit's waves (<see cref="DefaultDockFor"/>).
    /// </remarks>
    public static class ShellLayout
    {
        /// <summary>How far every panel stands in from the window's edges, and the gap between two side by side.</summary>
        public const float Margin = 16f, Gap = 10f;

        /// <summary>How wide the parts panel and the side panel are, whatever the window's width.</summary>
        public const float PartsWidth = 224f, SideWidth = 240f;

        /// <summary>How tall the splitter is: the gap between the circuit and the dock, which a drag moves.</summary>
        public const float SplitterHeight = 12f;

        /// <summary>How tall the dock's header and the legend at its foot are.</summary>
        public const float DockHeaderHeight = 44f, LegendHeight = 30f;

        /// <summary>How tall the status line along the bottom is: one line, the gestures where the pointer is (Part 11d).</summary>
        public const float StatusHeight = 32f;

        /// <summary>How tall the inspector bar is: a title and a line over it, beside a row of labelled fields.</summary>
        public const float InspectorHeight = 64f;

        /// <summary>The least the circuit's window and the waves are given while the window has the room.</summary>
        public const float MinCircuit = 160f, MinWaves = 72f;

        /// <summary>The tallest the dock is before the student has dragged it: the reference circuit's nine rows of waves.</summary>
        public const float DefaultDock = 370f;

        /// <summary>The room under the inspector for the circuit and the dock, in <paramref name="window"/> under a toolbar <paramref name="top"/> tall.</summary>
        private static float Room(Vec2 window, float top) => window.y - top - InspectorHeight - Gap - StatusHeight - SplitterHeight;

        /// <summary>The dock's height before the student has dragged it: half the room, and no more than <see cref="DefaultDock"/>.</summary>
        public static float DefaultDockFor(Vec2 window, float top) => ClampDock(Arith.Min(DefaultDock, 0.5f * Room(window, top)), window, top);

        /// <summary>The least the dock may be: its header, a row or two of waves, and the legend.</summary>
        public const float MinDock = DockHeaderHeight + MinWaves + LegendHeight;

        /// <summary>The most the dock may be in <paramref name="window"/> under a toolbar <paramref name="top"/> tall: what leaves the circuit its least.</summary>
        public static float MaxDock(Vec2 window, float top) => Arith.Max(MinDock, Room(window, top) - MinCircuit);

        /// <summary><paramref name="dock"/> held to what <paramref name="window"/> has room for.</summary>
        public static float ClampDock(float dock, Vec2 window, float top) => Arith.Clamp(dock, MinDock, MaxDock(window, top));

        /// <summary>Where every panel is in <paramref name="window"/> under a toolbar <paramref name="top"/> tall, with the dock <paramref name="dock"/> tall.</summary>
        public static ShellRegions Of(Vec2 window, float top, float dock)
        {
            dock = ClampDock(dock, window, top);
            float left = Margin, right = Arith.Max(left, window.x - Margin), width = right - left;
            var inspector = new Rect2(left, top, width, InspectorHeight);
            top += InspectorHeight + Gap;

            float statusTop = Arith.Max(top, window.y - StatusHeight);
            float dockTop = statusTop - dock;
            float splitterTop = Arith.Max(top, dockTop - SplitterHeight);
            float body = Arith.Max(0f, splitterTop - top);

            float circuitLeft = left + PartsWidth + Gap;
            float sideLeft = Arith.Max(circuitLeft, right - SideWidth);
            float circuitWidth = Arith.Max(0f, sideLeft - Gap - circuitLeft);

            return new ShellRegions(
                inspector: inspector,
                parts: new Rect2(left, top, PartsWidth, body),
                circuit: new Rect2(circuitLeft, top, circuitWidth, body),
                side: new Rect2(sideLeft, top, SideWidth, body),
                splitter: new Rect2(left, splitterTop, width, SplitterHeight),
                dock: new Rect2(left, dockTop, width, dock),
                dockHeader: new Rect2(left, dockTop, width, DockHeaderHeight),
                waves: new Rect2(left, dockTop + DockHeaderHeight, width, Arith.Max(0f, dock - DockHeaderHeight - LegendHeight)),
                legend: new Rect2(left, statusTop - LegendHeight, width, LegendHeight),
                status: new Rect2(left, statusTop, width, StatusHeight));
        }
    }

    /// <summary>Where each of the workbench's docked panels is (<see cref="ShellLayout.Of"/>), in the window's units, y down.</summary>
    public readonly struct ShellRegions : System.IEquatable<ShellRegions>
    {
        public ShellRegions(Rect2 inspector, Rect2 parts, Rect2 circuit, Rect2 side, Rect2 splitter, Rect2 dock, Rect2 dockHeader,
            Rect2 waves, Rect2 legend, Rect2 status)
        {
            Inspector = inspector;
            Parts = parts;
            Circuit = circuit;
            Side = side;
            Splitter = splitter;
            Dock = dock;
            DockHeader = dockHeader;
            Waves = waves;
            Legend = legend;
            Status = status;
        }

        /// <summary>The inspector bar, under the toolbar.</summary>
        public Rect2 Inspector { get; }
        public Rect2 Parts { get; }
        public Rect2 Circuit { get; }
        public Rect2 Side { get; }
        public Rect2 Splitter { get; }

        /// <summary>The whole dock: its header, the waves and the legend.</summary>
        public Rect2 Dock { get; }
        public Rect2 DockHeader { get; }
        public Rect2 Waves { get; }
        public Rect2 Legend { get; }
        public Rect2 Status { get; }

        public bool Equals(ShellRegions other) =>
            Inspector.Equals(other.Inspector) && Parts.Equals(other.Parts) && Circuit.Equals(other.Circuit) && Side.Equals(other.Side) && Splitter.Equals(other.Splitter) &&
            Dock.Equals(other.Dock) && DockHeader.Equals(other.DockHeader) && Waves.Equals(other.Waves) && Legend.Equals(other.Legend) &&
            Status.Equals(other.Status);

        public override bool Equals(object other) => other is ShellRegions r && Equals(r);

        public override int GetHashCode() => Circuit.GetHashCode() ^ (Dock.GetHashCode() << 1);
    }
}
