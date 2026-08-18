namespace BitSorter.View
{
    /// <summary>Which interaction the pointer currently belongs to.</summary>
    public enum PointerOwner
    {
        /// <summary>Nobody is mid-interaction and the pointer is over the board.</summary>
        None,

        /// <summary>The pointer is over a canvas widget, so the click belongs to the interface.</summary>
        Ui,

        /// <summary>A wire is being dragged between ports.</summary>
        Wiring,

        /// <summary>A gate is being dragged out of the palette.</summary>
        Palette,
    }

    /// <summary>Something that reads the mouse and wants to know whether it may act.</summary>
    public enum PointerUser
    {
        Placement,
        Wiring,
        WireDelay,
        Palette,
    }

    /// <summary>
    /// Decides which mouse-reading component may act on a given frame.
    /// </summary>
    /// <remarks>
    /// This exists because nothing arbitrates today. On one left-press frame
    /// <see cref="PlacementController"/> calls TryPlaceGate and <see cref="WiringController"/> calls
    /// BeginDrag, independently, with no ordering and no consumption. That already misbehaves --
    /// pressing a port to start a wire also asks to place a gate on that port's own cell, which is
    /// refused as "that cell is taken" and shows the player a rejection they did not earn. Once a
    /// canvas exists it gets worse: a click on a Run button would also place a gate.
    ///
    /// **The owner is derived, never held.** There is no Claim/Release pair anywhere, on purpose. A
    /// claim protocol has exactly one catastrophic failure -- a claim that is never released silently
    /// disables input with no error and no way for the player to recover. Computing the owner from
    /// facts that are already true each frame makes that state unrepresentable: when a drag ends for
    /// any reason at all, including ones nobody anticipated, the fact goes false and the owner is
    /// None on the next frame.
    ///
    /// Pure and static so the whole matrix is testable without a scene, an EventSystem or a mouse.
    /// </remarks>
    public static class PointerRules
    {
        /// <summary>
        /// Who owns the pointer, given what is happening right now.
        /// </summary>
        /// <remarks>
        /// Order matters. A drag already in flight outranks the pointer merely being over a widget,
        /// because dragging a wire across the palette must not hand the pointer to the interface
        /// half way through. Between the two drags, a palette drag outranks a wire drag: the two
        /// cannot both begin, and if some future bug lets them, the newer interaction winning is the
        /// one the player can escape by releasing the button.
        /// </remarks>
        public static PointerOwner OwnerOf(bool paletteDragging, bool wiringDragging, bool pointerOverUi)
        {
            if (paletteDragging)
                return PointerOwner.Palette;

            if (wiringDragging)
                return PointerOwner.Wiring;

            if (pointerOverUi)
                return PointerOwner.Ui;

            return PointerOwner.None;
        }

        /// <summary>Whether <paramref name="user"/> may act while <paramref name="owner"/> holds the pointer.</summary>
        /// <remarks>
        /// The whole matrix, stated once:
        ///
        /// <code>
        /// owner \ user  | Placement | Wiring | WireDelay | Palette
        /// None          |     y     |    y   |     y     |    y
        /// Ui            |     n     |    n   |     n     |    y
        /// Wiring        |     n     |    y   |     n     |    n
        /// Palette       |     n     |    n   |     n     |    y
        /// </code>
        ///
        /// Two rows are worth explaining. Under <see cref="PointerOwner.Ui"/> the palette may still
        /// act, because a palette drag *starts* on a widget -- refusing it there would make the
        /// drag-out-of-the-menu interaction impossible. Under <see cref="PointerOwner.Wiring"/> the
        /// wiring controller may act because it is the owner; everyone else stands off, which is
        /// what removes the spurious placement rejection that fires today on every wire drag.
        /// </remarks>
        public static bool MayAct(PointerUser user, PointerOwner owner)
        {
            switch (owner)
            {
                case PointerOwner.None:
                    return true;

                case PointerOwner.Ui:
                    return user == PointerUser.Palette;

                case PointerOwner.Wiring:
                    return user == PointerUser.Wiring;

                case PointerOwner.Palette:
                    return user == PointerUser.Palette;

                default:
                    // An owner nobody taught this method about must not silently disable the board.
                    // Failing open keeps the game playable; failing closed would look like a freeze.
                    return true;
            }
        }
    }
}
