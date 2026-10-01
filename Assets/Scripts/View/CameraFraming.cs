namespace BitSorter.View
{
    /// <summary>Where the camera sits and how much it shows.</summary>
    public readonly struct Framing
    {
        /// <summary>Half the height of the view, in world units.</summary>
        public readonly float OrthographicSize;

        /// <summary>The camera's x position, in world units.</summary>
        public readonly float CameraX;

        /// <summary>
        /// The camera's y position, in world units: 0 unless something covers the bottom edge.
        /// </summary>
        public readonly float CameraY;

        public Framing(float orthographicSize, float cameraX, float cameraY = 0f)
        {
            OrthographicSize = orthographicSize;
            CameraX = cameraX;
            CameraY = cameraY;
        }

        public override string ToString() => $"size {OrthographicSize:F3}, x {CameraX:F3}, y {CameraY:F3}";
    }

    /// <summary>
    /// Frames the board in the part of the screen the interface leaves free.
    /// </summary>
    /// <remarks>
    /// Pure arithmetic, so the rule is testable without a camera or a canvas.
    ///
    /// The camera used to fit the board to the whole screen, and the parts list sits over the
    /// screen's left edge -- which is where the board's leftmost column is. Most levels keep a
    /// source in that column. Framing into the space between the insets moves the board clear of
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
            float leftInset, float rightInset) =>
            Fit(boardHalfWidth, 0f, authoredSize, screenWidth, screenHeight, leftInset, rightInset);

        /// <inheritdoc cref="Fit(float, float, float, float, float, float)"/>
        /// <param name="boardHalfHeight">
        /// Half the height the board needs, the names under its bottom row included, in world units.
        /// </param>
        /// <remarks>
        /// A board of seven rows is taller than the authored framing shows, and on a screen wide
        /// enough that the width never binds it would run off the top and bottom. The standard five
        /// rows fit the authored framing, so for them this changes nothing.
        /// </remarks>
        public static Framing Fit(
            float boardHalfWidth, float boardHalfHeight, float authoredSize,
            float screenWidth, float screenHeight,
            float leftInset, float rightInset) =>
            Fit(boardHalfWidth, boardHalfHeight, 0f, authoredSize,
                screenWidth, screenHeight, leftInset, rightInset, 0f);

        /// <inheritdoc cref="Fit(float, float, float, float, float, float, float)"/>
        /// <param name="boardTop">
        /// How far above the board's centre its top row reaches, in world units: the top of a part
        /// placed there.
        /// </param>
        /// <param name="topInset">Pixels covered along the top edge.</param>
        /// <remarks>
        /// The banner sits over the middle of the top edge, and a seven-row board fitted only by its
        /// height put its top row under it -- in Four lanes the AND there was cut off at the top.
        /// The camera stays centred on the board, so clearing the banner means showing more, never
        /// moving: the room above the centre is the size less the inset's share of it. The standard
        /// five rows clear a one- or two-line banner already, so for them this changes nothing.
        /// </remarks>
        public static Framing Fit(
            float boardHalfWidth, float boardHalfHeight, float boardTop, float authoredSize,
            float screenWidth, float screenHeight,
            float leftInset, float rightInset, float topInset) =>
            Fit(boardHalfWidth, boardHalfHeight, boardTop, authoredSize,
                screenWidth, screenHeight, leftInset, rightInset, topInset, 0f);

        /// <inheritdoc cref="Fit(float, float, float, float, float, float, float, float, float)"/>
        /// <param name="bottomInset">Pixels covered along the bottom edge: the timing diagram's strip.</param>
        /// <remarks>
        /// **With nothing along the bottom, this is exactly the framing above**, the same arithmetic
        /// in the same order, with the camera at y = 0 -- every reference shot is held to that.
        ///
        /// **With something there, the camera moves.** Clearing the banner by zooming alone was
        /// cheap because the banner is shallow; a strip a third of the screen tall, cleared the same
        /// way, would leave the board at a fraction of its size with empty screen above it. So the
        /// board is centred in the band between the banner and the strip, at the smallest size that
        /// fits it there -- and never a smaller size than it has without the strip, so opening the
        /// strip cannot make the board grow. A band under a quarter of the screen is ignored, as a
        /// banner that leaves no room is, and the strip then covers the board.
        /// </remarks>
        public static Framing Fit(
            float boardHalfWidth, float boardHalfHeight, float boardTop, float authoredSize,
            float screenWidth, float screenHeight,
            float leftInset, float rightInset, float topInset, float bottomInset)
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

            if (boardHalfHeight > size)
                size = boardHalfHeight;

            // size * (1 - 2 * top / height) world units show above the centre and below the inset.
            // An inset that would leave under a quarter of the screen is ignored, as the side ones
            // are: a board shrunk to nothing to clear it is the worse answer.
            float belowTheTop = 1f - 2f * (topInset < 0f ? 0f : topInset) / screenHeight;

            if (boardTop > 0f && belowTheTop >= 0.25f && boardTop / belowTheTop > size)
                size = boardTop / belowTheTop;

            // The board's centre belongs at the centre of the free span, which is
            // (left - right) / 2 pixels right of the screen's centre. Moving the camera the other
            // way by that many pixels' worth of world puts it there.
            float worldPerPixel = 2f * size / screenHeight;
            float cameraX = -(leftInset - rightInset) * 0.5f * worldPerPixel;

            float top = topInset < 0f ? 0f : topInset;
            float band = 1f - (top + bottomInset) / screenHeight;

            if (bottomInset <= 0f || band < 0.25f)
                return new Framing(size, cameraX);

            // The board runs from boardHalfHeight below its centre to boardTop above it, and the band
            // is 2 * size * band world units tall, so this is the smallest size it fits in.
            float inTheBand = (boardHalfHeight + boardTop) / (2f * band);

            if (inTheBand > size)
                size = inTheBand;

            // At this size the camera may sit anywhere from lowest (the board's top just under the
            // banner) to highest (its bottom just over the strip); halfway between centres the board
            // in the band. Below zero, since the strip is the deeper of the two.
            float lowest = boardTop - size * (1f - 2f * top / screenHeight);
            float highest = -boardHalfHeight + size * (1f - 2f * bottomInset / screenHeight);
            float cameraY = (lowest + highest) * 0.5f;

            worldPerPixel = 2f * size / screenHeight;
            cameraX = -(leftInset - rightInset) * 0.5f * worldPerPixel;

            return new Framing(size, cameraX, cameraY);
        }

        /// <inheritdoc cref="Fit(float, float, float, float, float, float, float, float, float, float)"/>
        /// <param name="partRight">
        /// How far right of the board's centre a part in its top-right cell reaches, in world units.
        /// Its top is <paramref name="boardTop"/>.
        /// </param>
        /// <param name="cornerWidth">
        /// Pixels the top-right corner takes in from the right edge -- the badges, their keys, and
        /// free play's folded tab -- or zero when nothing in that cell needs keeping out of it.
        /// </param>
        /// <param name="cornerHeight">Pixels the corner takes down from the top edge.</param>
        /// <remarks>
        /// **Only when the top-right part would reach into the corner** is the board framed again,
        /// with the corner's left edge as the right inset -- about 7% smaller at 16:9. Every
        /// framing that already clears it is the framing above, to the last bit, and so is one
        /// where a panel down the right already reaches further in than the corner does.
        ///
        /// The corner is not a side panel, because it is not a column: it is two badges and the
        /// keys under them, and below them the board's right edge is free. Counting it as a right
        /// inset on every level would shrink every board for a cell most levels leave empty.
        /// </remarks>
        public static Framing Fit(
            float boardHalfWidth, float boardHalfHeight, float boardTop, float authoredSize,
            float screenWidth, float screenHeight,
            float leftInset, float rightInset, float topInset, float bottomInset,
            float partRight, float cornerWidth, float cornerHeight)
        {
            Framing framing = Fit(boardHalfWidth, boardHalfHeight, boardTop, authoredSize,
                screenWidth, screenHeight, leftInset, rightInset, topInset, bottomInset);

            if (cornerWidth <= 0f || cornerHeight <= 0f || cornerWidth <= rightInset)
                return framing;

            // A corner that would leave no room is ignored, as side insets are: framed again, the
            // board would drop every inset and fill the screen, over the parts list.
            float left = leftInset < 0f ? 0f : leftInset;
            if (screenWidth - left - cornerWidth <= screenWidth * 0.25f)
                return framing;

            if (!Reaches(framing, partRight, boardTop, screenWidth, screenHeight, cornerWidth, cornerHeight))
                return framing;

            return Fit(boardHalfWidth, boardHalfHeight, boardTop, authoredSize,
                screenWidth, screenHeight, leftInset, cornerWidth, topInset, bottomInset);
        }

        /// <summary>
        /// Whether a part reaching this far right and this far up is drawn into the top-right corner.
        /// </summary>
        /// <remarks>
        /// Touching is not reaching into, as for <c>Rect.Overlaps</c>.
        /// </remarks>
        public static bool Reaches(
            Framing framing, float partRight, float partTop,
            float screenWidth, float screenHeight, float cornerWidth, float cornerHeight)
        {
            float pixelsPerWorld = screenHeight / (2f * framing.OrthographicSize);
            float right = screenWidth * 0.5f + (partRight - framing.CameraX) * pixelsPerWorld;
            float top = screenHeight * 0.5f + (partTop - framing.CameraY) * pixelsPerWorld;

            return right > screenWidth - cornerWidth && top > screenHeight - cornerHeight;
        }
    }
}
