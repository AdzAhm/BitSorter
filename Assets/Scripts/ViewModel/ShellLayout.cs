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
    ///
    /// **Each of the three panels folds away** (Ahmad, 2026-11-08: "so we can have a larger view of the map"): PARTS down
    /// the left, the side panel down the right and the dock along the bottom each have an arrow on the edge that faces the
    /// circuit (<see cref="ShellRegions.PartsFold"/> and the others), and folded each is a strip <see cref="Folded"/> wide
    /// at the window's edge holding only its arrow, the circuit's window taking the room (<see cref="ShellFolds"/>). The
    /// dock's arrow stands on the splitter beside its grip, the grip keeping the splitter's middle. The inspector bar, the
    /// toolbar and its controls -- an exercise's GOAL and CHECK among them -- never fold.
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

        /// <summary>How wide a folded side panel is, and how tall the folded dock: a strip that holds its arrow.</summary>
        public const float Folded = 24f;

        /// <summary>A fold arrow's button: as long as it is along its edge, and as thick across it -- the splitter's height.</summary>
        public const float ArrowLength = 32f, ArrowThickness = SplitterHeight;

        /// <summary>How far right of the splitter's middle the dock's arrow stands: clear of the grip, 56 wide there.</summary>
        public const float DockArrowOffset = 28f + Gap + 0.5f * ArrowLength;

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
        public static ShellRegions Of(Vec2 window, float top, float dock) => Of(window, top, dock, default);

        /// <summary>
        /// Where every panel is in <paramref name="window"/> under a toolbar <paramref name="top"/> tall, with the dock
        /// <paramref name="dock"/> tall while it is open, and the panels <paramref name="folds"/> says folded.
        /// </summary>
        public static ShellRegions Of(Vec2 window, float top, float dock, ShellFolds folds)
        {
            dock = folds.Dock ? Folded : ClampDock(dock, window, top);
            float left = Margin, right = Arith.Max(left, window.x - Margin), width = right - left;
            var inspector = new Rect2(left, top, width, InspectorHeight);
            top += InspectorHeight + Gap;

            float statusTop = Arith.Max(top, window.y - StatusHeight);
            float dockTop = Arith.Max(top, statusTop - dock);
            float splitterTop = Arith.Max(top, dockTop - SplitterHeight);
            float body = Arith.Max(0f, splitterTop - top);

            float partsWidth = folds.Parts ? Folded : PartsWidth, sideWidth = folds.Side ? Folded : SideWidth;
            float circuitLeft = left + partsWidth + Gap;
            float sideLeft = Arith.Max(circuitLeft, right - sideWidth);
            float circuitWidth = Arith.Max(0f, sideLeft - Gap - circuitLeft);

            var parts = new Rect2(left, top, partsWidth, body);
            var side = new Rect2(sideLeft, top, sideWidth, body);
            var splitter = new Rect2(left, splitterTop, width, SplitterHeight);
            var dockRect = new Rect2(left, dockTop, width, Arith.Max(0f, statusTop - dockTop));
            float inner = folds.Dock ? 0f : 1f;   // a folded dock's header, waves and legend have no height

            // Each arrow on the edge that faces the circuit, in the gap there; folded, in the middle of its strip.
            float middle = top + 0.5f * body;
            float partsArrowX = folds.Parts ? parts.center.x : parts.xMax + 0.5f * Gap;
            float sideArrowX = folds.Side ? side.center.x : side.xMin - 0.5f * Gap;
            float dockArrowX = left + 0.5f * width + DockArrowOffset;
            float dockArrowY = folds.Dock ? dockRect.center.y : splitter.center.y;

            return new ShellRegions(
                inspector: inspector,
                parts: parts,
                circuit: new Rect2(circuitLeft, top, circuitWidth, body),
                side: side,
                splitter: splitter,
                dock: dockRect,
                dockHeader: new Rect2(left, dockTop, width, inner * DockHeaderHeight),
                waves: new Rect2(left, dockTop + inner * DockHeaderHeight, width,
                    inner * Arith.Max(0f, dockRect.height - DockHeaderHeight - LegendHeight)),
                legend: new Rect2(left, statusTop - inner * LegendHeight, width, inner * LegendHeight),
                status: new Rect2(left, statusTop, width, StatusHeight),
                partsFold: Around(partsArrowX, middle, ArrowThickness, ArrowLength),
                sideFold: Around(sideArrowX, middle, ArrowThickness, ArrowLength),
                dockFold: Around(dockArrowX, dockArrowY, ArrowLength, ArrowThickness),
                folds: folds);
        }

        private static Rect2 Around(float x, float y, float width, float height) => new Rect2(x - 0.5f * width, y - 0.5f * height, width, height);

        /// <summary>QUIT's button (5.1): as wide and as tall as it stands in the status line's row.</summary>
        public const float QuitWidth = 72f, QuitHeight = 26f;

        /// <summary>
        /// Where QUIT stands in <paramref name="status"/>, the status line's row (5.1, a desktop build's alone; Ahmad: "I couldn't
        /// exit the simulator unless I exited full screen"): at its right end, in its middle -- the window's bottom right, where
        /// the start page and every other page leave the row's corner free.
        /// </summary>
        public static Rect2 QuitIn(Rect2 status) =>
            new Rect2(Arith.Max(status.x, status.xMax - QuitWidth), status.y + 0.5f * (status.height - QuitHeight), Arith.Min(QuitWidth, status.width), QuitHeight);

        /// <summary>
        /// The status line's text in <paramref name="status"/>: the whole row, or, with QUIT in it (<paramref name="quit"/>),
        /// clear of QUIT by a gap on both sides, so the text stays in the window's middle.
        /// </summary>
        public static Rect2 StatusTextIn(Rect2 status, bool quit)
        {
            if (!quit)
                return status;
            float room = QuitWidth + Gap;
            float width = Arith.Max(0f, status.width - 2f * room);
            return new Rect2(status.x + 0.5f * (status.width - width), status.y, width, status.height);
        }
    }

    /// <summary>The workbench's three panels that fold away (<see cref="ShellFolds"/>).</summary>
    public enum ShellFold
    {
        /// <summary>PARTS, with DESIGN in it, down the left.</summary>
        Parts,

        /// <summary>The side panel down the right: the selection's quick fields and the minimap.</summary>
        Side,

        /// <summary>The dock along the bottom: its tabs, the waves and the legend.</summary>
        Dock,
    }

    /// <summary>
    /// Which of the three panels are folded away (<see cref="ShellLayout"/>): a click on one's arrow folds or opens it, and H
    /// folds all three while any is open, else opens them (<see cref="AllToggled"/>). Kept between launches as one number,
    /// a bit a panel (<see cref="Bits"/>); none folded is the default, and what a machine that never folded one reads.
    /// </summary>
    public readonly struct ShellFolds : System.IEquatable<ShellFolds>
    {
        public ShellFolds(bool parts, bool side, bool dock)
        {
            Parts = parts;
            Side = side;
            Dock = dock;
        }

        public bool Parts { get; }
        public bool Side { get; }
        public bool Dock { get; }

        /// <summary>Whether <paramref name="panel"/> is folded.</summary>
        public bool this[ShellFold panel] => panel == ShellFold.Parts ? Parts : panel == ShellFold.Side ? Side : Dock;

        /// <summary>Whether every one is folded.</summary>
        public bool All => Parts && Side && Dock;

        /// <summary><paramref name="panel"/> folded if it is open, opened if it is folded; the others as they are.</summary>
        public ShellFolds Toggled(ShellFold panel) => new ShellFolds(
            panel == ShellFold.Parts ? !Parts : Parts, panel == ShellFold.Side ? !Side : Side, panel == ShellFold.Dock ? !Dock : Dock);

        /// <summary>H: every one folded while any is open, and every one opened once all are folded.</summary>
        public ShellFolds AllToggled() => All ? default : new ShellFolds(true, true, true);

        /// <summary>The folds as one number: 1 PARTS, 2 the side panel, 4 the dock.</summary>
        public int Bits => (Parts ? 1 : 0) | (Side ? 2 : 0) | (Dock ? 4 : 0);

        /// <summary>The folds <paramref name="bits"/> says (<see cref="Bits"/>); none for a number under 0, as a setting never made reads.</summary>
        public static ShellFolds FromBits(int bits) => bits < 0 ? default : new ShellFolds((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);

        public bool Equals(ShellFolds other) => Bits == other.Bits;
        public override bool Equals(object other) => other is ShellFolds f && Equals(f);
        public override int GetHashCode() => Bits;
        public static bool operator ==(ShellFolds a, ShellFolds b) => a.Equals(b);
        public static bool operator !=(ShellFolds a, ShellFolds b) => !a.Equals(b);
    }

    /// <summary>Where each of the workbench's docked panels is (<see cref="ShellLayout.Of"/>), in the window's units, y down.</summary>
    public readonly struct ShellRegions : System.IEquatable<ShellRegions>
    {
        public ShellRegions(Rect2 inspector, Rect2 parts, Rect2 circuit, Rect2 side, Rect2 splitter, Rect2 dock, Rect2 dockHeader,
            Rect2 waves, Rect2 legend, Rect2 status, Rect2 partsFold, Rect2 sideFold, Rect2 dockFold, ShellFolds folds)
        {
            PartsFold = partsFold;
            SideFold = sideFold;
            DockFold = dockFold;
            Folds = folds;
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

        /// <summary>PARTS' fold arrow: on its edge facing the circuit, or in its strip while it is folded.</summary>
        public Rect2 PartsFold { get; }

        /// <summary>The side panel's fold arrow, as <see cref="PartsFold"/> is.</summary>
        public Rect2 SideFold { get; }

        /// <summary>The dock's fold arrow: on the splitter beside its grip, or in its strip while it is folded.</summary>
        public Rect2 DockFold { get; }

        /// <summary>Which panels these regions have folded.</summary>
        public ShellFolds Folds { get; }

        /// <summary>The arrow of <paramref name="panel"/>.</summary>
        public Rect2 FoldOf(ShellFold panel) => panel == ShellFold.Parts ? PartsFold : panel == ShellFold.Side ? SideFold : DockFold;

        public bool Equals(ShellRegions other) =>
            Inspector.Equals(other.Inspector) && Parts.Equals(other.Parts) && Circuit.Equals(other.Circuit) && Side.Equals(other.Side) && Splitter.Equals(other.Splitter) &&
            Dock.Equals(other.Dock) && DockHeader.Equals(other.DockHeader) && Waves.Equals(other.Waves) && Legend.Equals(other.Legend) &&
            Status.Equals(other.Status) && PartsFold.Equals(other.PartsFold) && SideFold.Equals(other.SideFold) &&
            DockFold.Equals(other.DockFold) && Folds.Equals(other.Folds);

        public override bool Equals(object other) => other is ShellRegions r && Equals(r);

        public override int GetHashCode() => Circuit.GetHashCode() ^ (Dock.GetHashCode() << 1);
    }
}
