using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// How the schematic sits in its window: how far it is zoomed in, and where it has been moved to.
    /// Plain arithmetic, so the rules are held by Edit Mode tests rather than by a mouse.
    /// </summary>
    /// <remarks>
    /// In the window's own units, y down from its top-left corner, as the stage is written. Fitted is
    /// the drawing at its own size where the window was built to show it whole (<see cref="Home"/>);
    /// the wheel zooms in from there, about the point under the cursor, and never out past it, so the
    /// whole circuit is always one step away. A drag moves it, but never so far that the window's
    /// middle is off the drawing: a circuit dragged out of sight is a window that looks empty.
    /// </remarks>
    public sealed class SchematicView
    {
        /// <summary>The furthest the wheel zooms in from the fit.</summary>
        public const float MaxZoom = 4f;

        /// <summary>How much one notch of the wheel zooms, in or out.</summary>
        public const float NotchFactor = 1.25f;

        public SchematicView(Vector2 window, Vector2 home, Vector2 drawing)
        {
            Window = window;
            Home = home;
            Drawing = drawing;
            Fit();
        }

        /// <summary>The window's size.</summary>
        public Vector2 Window { get; }

        /// <summary>Where the drawing's top-left corner sits when it is fitted.</summary>
        public Vector2 Home { get; }

        /// <summary>The drawing's own size, unzoomed.</summary>
        public Vector2 Drawing { get; }

        /// <summary>How far it is zoomed in: 1 is fitted.</summary>
        public float Zoom { get; private set; }

        /// <summary>Where the drawing's top-left corner is now.</summary>
        public Vector2 Offset { get; private set; }

        /// <summary>Whether it is as it was built: the whole circuit, where it belongs.</summary>
        public bool IsFitted => Zoom == 1f && Offset == Home;

        /// <summary>Back to the whole circuit, where it belongs.</summary>
        public void Fit()
        {
            Zoom = 1f;
            Offset = Home;
        }

        /// <summary>Zooms by whole notches, in for more than zero, keeping <paramref name="point"/> where it is. True when anything moved.</summary>
        public bool ZoomAbout(Vector2 point, int notches)
        {
            Vector2 before = Offset;
            float wasZoom = Zoom;

            // Notches in and as many out come back to exactly 1, not to a rounding error short of it.
            float zoom = Mathf.Clamp(Zoom * Mathf.Pow(NotchFactor, notches), 1f, MaxZoom);
            if (zoom - 1f < 1e-4f)
                zoom = 1f;

            // The point under the cursor is the same point of the drawing before and after.
            Offset = point - (point - Offset) * (zoom / Zoom);
            Zoom = zoom;

            // All the way out is fitted, wherever it had been moved: a zoom out that left the circuit
            // off to one side at its own size would be a fit that does not look like one.
            if (Zoom == 1f)
                Offset = Home;

            Keep();
            return Offset != before || Zoom != wasZoom;
        }

        /// <summary>Moves the drawing by <paramref name="delta"/>. True when it moved.</summary>
        public bool Pan(Vector2 delta)
        {
            Vector2 before = Offset;
            Offset += delta;
            Keep();
            return Offset != before;
        }

        /// <summary>Holds the window's middle on the drawing.</summary>
        private void Keep()
        {
            Vector2 middle = Window * 0.5f, size = Drawing * Zoom;
            Offset = new Vector2(
                Mathf.Clamp(Offset.x, middle.x - size.x, middle.x),
                Mathf.Clamp(Offset.y, middle.y - size.y, middle.y));
        }
    }
}
