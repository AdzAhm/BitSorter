
namespace BitSorter.ViewModel
{
    /// <summary>
    /// How the schematic sits in its window: how far it is zoomed in, and where it has been moved to.
    /// Plain arithmetic, so the rules are held by tests under <c>dotnet test</c> rather than by a mouse.
    /// </summary>
    /// <remarks>
    /// In the window's own units, y down from its top-left corner, as the stage is written. A zoom of 1 is
    /// the drawing at its own size. **Fitted** is the whole drawing in the middle of the window, at least
    /// <see cref="Home"/> in from its edges, at its own size -- or, once it has outgrown the window, smaller,
    /// as much as it needs (Part 10). The wheel zooms about the point under the cursor, in to
    /// <see cref="MaxZoom"/> and out past the fit to <see cref="MinZoom"/>, for room to place a part beside
    /// what is there. A drag moves it as far as the drawing and <see cref="Room"/> round it, never so far
    /// that the window's middle is off both: a circuit dragged out of sight is a window that looks empty.
    ///
    /// **An edit never moves what is on screen.** The drawing grows and shrinks with the circuit, and its
    /// corner with it; <see cref="Relayout"/> takes the shift back out of the offset, so every part stays
    /// under the pointer that placed it.
    ///
    /// **The window is the docked layout's** (Part 11b), whatever the drawing's size, so the fit centres the
    /// drawing in it -- except a drawing with nothing in it yet (<see cref="Blank"/>), which starts at the window's
    /// top-left corner as a page does, so a circuit built from it down and to the right stays in sight. A window made
    /// another size by the splitter or the screen (<see cref="Resize"/>) keeps the zoom and what is at its corner --
    /// or, a view that was the fit, is the fit of the new window.
    /// </remarks>
    public sealed class SchematicView
    {
        /// <summary>The furthest the wheel zooms in: four times the drawing's own size.</summary>
        public const float MaxZoom = 4f;

        /// <summary>The furthest the wheel zooms out (Part 10): a quarter of its own size, or the fit if that is smaller.</summary>
        public const float MinZoom = 0.25f;

        /// <summary>How much one notch of the wheel zooms, in or out.</summary>
        public const float NotchFactor = 1.25f;

        /// <summary>How far past the drawing's edge a drag may take the window's middle, in the drawing's own units: six cells.</summary>
        public const float Room = 6f * DocumentSchematic.Cell;

        public SchematicView(Vec2 window, Vec2 home, Vec2 drawing, bool blank = false)
        {
            Window = window;
            Home = home;
            Drawing = drawing;
            Blank = blank;
            Fit();
        }

        /// <summary>
        /// Whether the drawing has nothing in it yet: its fit is then at <see cref="Home"/>, the window's top-left corner,
        /// rather than its middle. Set as an edit empties the drawing or puts the first thing in it; it moves nothing.
        /// </summary>
        public bool Blank { get; set; }

        /// <summary>The window's size.</summary>
        public Vec2 Window { get; private set; }

        /// <summary>Where the drawing's top-left corner sits when it is fitted at its own size.</summary>
        public Vec2 Home { get; }

        /// <summary>The drawing's own size, unzoomed.</summary>
        public Vec2 Drawing { get; private set; }

        /// <summary>How far it is zoomed in: 1 is its own size.</summary>
        public float Zoom { get; private set; }

        /// <summary>Where the drawing's top-left corner is now.</summary>
        public Vec2 Offset { get; private set; }

        /// <summary>
        /// The zoom the fit is: 1 while the drawing fits the room the window keeps for it, less once it has
        /// outgrown it. A drawing the window was built round fits it exactly, to the last rounding error. **However tall
        /// the drawing, the fit holds all of it** (Part 15h-4d): it stopped at a sixteenth, and a 64-bit ALU's column of
        /// instances, which needs far less, could never be seen whole nor zoomed out to (Ahmad, 2026-10-29).
        /// </summary>
        public float FitZoom
        {
            get
            {
                Vec2 room = Window - Home * 2f;
                if (room.x >= Drawing.x - 1e-3f && room.y >= Drawing.y - 1e-3f)
                    return 1f;
                return Arith.Max(1e-4f, Arith.Min(room.x / Arith.Max(Drawing.x, 1f), room.y / Arith.Max(Drawing.y, 1f)));
            }
        }

        /// <summary>
        /// Where the fit puts the drawing's corner: centred in the room the window keeps for it, at home when it fills
        /// that room -- or when it is <see cref="Blank"/>.
        /// </summary>
        public Vec2 FitOffset => Blank ? Home : Home + (Window - Home * 2f - Drawing * FitZoom) * 0.5f;

        /// <summary>Whether it is the fit: the whole circuit, where it belongs.</summary>
        public bool IsFitted => Zoom == FitZoom && Offset == FitOffset;

        /// <summary>The furthest out the wheel goes: <see cref="MinZoom"/>, or the fit when that is further.</summary>
        private float Floor => Arith.Min(MinZoom, FitZoom);

        /// <summary>Back to the whole circuit, where it belongs.</summary>
        public void Fit()
        {
            Zoom = FitZoom;
            Offset = FitOffset;
        }

        /// <summary>Zooms by whole notches, in for more than zero, keeping <paramref name="point"/> where it is. True when anything moved.</summary>
        public bool ZoomAbout(Vec2 point, int notches)
        {
            Vec2 before = Offset;
            float wasZoom = Zoom;
            float fit = FitZoom;

            // Notches in and as many out come back to exactly the fit, not to a rounding error beside it.
            float zoom = Arith.Clamp(Zoom * Arith.Pow(NotchFactor, notches), Floor, MaxZoom);
            if (Arith.Abs(zoom - fit) < 1e-4f * fit)
                zoom = fit;

            // The point under the cursor is the same point of the drawing before and after.
            Offset = point - (point - Offset) * (zoom / Zoom);
            Zoom = zoom;

            // Back out at the fit, it is the fit, wherever it had been moved: a fit that left the circuit
            // off to one side would be one that does not look like one.
            if (Zoom == fit && wasZoom > fit)
                Offset = FitOffset;

            Keep();
            return Offset != before || Zoom != wasZoom;
        }

        /// <summary>
        /// The window made <paramref name="window"/> (Part 11b): the zoom and the drawing's corner stay where they were,
        /// as far as the window's middle stays on the drawing -- or, a view that was the fit, is the new window's fit.
        /// </summary>
        public void Resize(Vec2 window)
        {
            bool fitted = IsFitted;
            Window = window;
            if (fitted)
                Fit();
            else
                Keep();
        }

        /// <summary>Moves the drawing by <paramref name="delta"/>. True when it moved.</summary>
        public bool Pan(Vec2 delta)
        {
            Vec2 before = Offset;
            Offset += delta;
            Keep();
            return Offset != before;
        }

        /// <summary>
        /// The drawing made again by an edit (Part 10): <paramref name="drawing"/> its new size, and
        /// <paramref name="shift"/> how far a point of the board moved on it (<see cref="SchematicLayout.Frame.ShiftFrom"/>).
        /// The offset takes the shift back, so nothing moves on screen; a view that was the fit is the fit of
        /// the new drawing only when that leaves everything where it was.
        /// </summary>
        public void Relayout(Vec2 drawing, Vec2 shift)
        {
            Drawing = drawing;
            Offset -= shift * Zoom;
            Keep();
        }

        /// <summary>
        /// Holds the window's middle on the drawing, or within <see cref="Room"/> of it -- or within half the window,
        /// when that is more: a circuit with almost nothing in it yet still pans as far as the window is wide.
        /// </summary>
        private void Keep()
        {
            Vec2 middle = Window * 0.5f, size = Drawing * Zoom;
            var room = new Vec2(Arith.Max(Room * Zoom, middle.x), Arith.Max(Room * Zoom, middle.y));
            Offset = new Vec2(
                Arith.Clamp(Offset.x, middle.x - size.x - room.x, middle.x + room.x),
                Arith.Clamp(Offset.y, middle.y - size.y - room.y, middle.y + room.y));
        }
    }
}
