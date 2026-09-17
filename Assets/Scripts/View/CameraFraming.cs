namespace BitSorter.View
{
    /// <summary>Where the camera sits and how much it shows.</summary>
    public readonly struct Framing
    {
        /// <summary>Half the height of the view, in world units.</summary>
        public readonly float OrthographicSize;

        /// <summary>The camera's x position, in world units.</summary>
        public readonly float CameraX;

        public Framing(float orthographicSize, float cameraX)
        {
            OrthographicSize = orthographicSize;
            CameraX = cameraX;
        }

        public override string ToString() => $"size {OrthographicSize:F3}, x {CameraX:F3}";
    }

    /// <summary>
    /// Frames the board in the part of the screen the interface leaves free.
    /// </summary>
    /// <remarks>
    /// Pure arithmetic, so the rule is testable without a camera or a canvas.
    ///
    /// The camera used to fit the board to the whole screen, and the parts list sits over the
    /// screen's left edge -- which is where the board's leftmost column is. Four shipped levels keep
    /// a source in that column. Framing into the space between the insets moves the board clear of
    /// whatever sits at the sides, and free play's docked panel is just a second inset.
    ///
    /// The authored size stays a floor, exactly as before: a screen with room to spare shows the
    /// board at the framing it was designed with rather than filling itself with it.
    /// </remarks>
    public static class CameraFraming
    {
        /// <param name="boardHalfWidth">Half the width the board needs, margin included, in world units.</param>
        /// <param name="authoredSize">The orthographic size the scene was designed with.</param>
        /// <param name="screenWidth">Screen width in pixels.</param>
        /// <param name="screenHeight">Screen height in pixels.</param>
        /// <param name="leftInset">Pixels covered along the left edge.</param>
        /// <param name="rightInset">Pixels covered along the right edge.</param>
        public static Framing Fit(
            float boardHalfWidth, float authoredSize,
            float screenWidth, float screenHeight,
            float leftInset, float rightInset)
        {
            if (screenWidth <= 0f || screenHeight <= 0f)
                return new Framing(authoredSize, 0f);

            leftInset = leftInset < 0f ? 0f : leftInset;
            rightInset = rightInset < 0f ? 0f : rightInset;

            float available = screenWidth - leftInset - rightInset;

            // Insets that leave nothing are ignored rather than obeyed: a board squeezed into no
            // width at all would be infinitely small, and the whole screen is the better answer.
            if (available <= screenWidth * 0.25f)
            {
                available = screenWidth;
                leftInset = 0f;
                rightInset = 0f;
            }

            // The board's full width, 2 * halfWidth, has to span no more than the free pixels.
            // One world unit is 2 * size / screenHeight pixels wide, which gives the smallest size.
            float needed = boardHalfWidth * screenHeight / available;
            float size = needed > authoredSize ? needed : authoredSize;

            // The board's centre belongs at the centre of the free span, which is
            // (left - right) / 2 pixels right of the screen's centre. Moving the camera the other
            // way by that many pixels' worth of world puts it there.
            float worldPerPixel = 2f * size / screenHeight;
            float cameraX = -(leftInset - rightInset) * 0.5f * worldPerPixel;

            return new Framing(size, cameraX);
        }
    }
}
