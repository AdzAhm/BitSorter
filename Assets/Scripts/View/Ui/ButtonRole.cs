namespace BitSorter.View
{
    /// <summary>
    /// What a button is for, which decides how loudly it is drawn.
    /// </summary>
    /// <remarks>
    /// Every button used to be drawn alike, so the win panel offered NEXT LEVEL and KEEP TINKERING as
    /// equals and CLEAR ALL looked like UNDO. A button now says what it is, and the look says what
    /// that looks like -- <see cref="UiTheme.FillOf"/>, once.
    ///
    /// <see cref="Primary"/> is also the only solid button: in a look whose panels are outlined, it
    /// is told apart by its shape as well as its colour, the same rule the board lives by.
    /// </remarks>
    public enum ButtonRole
    {
        /// <summary>Every button that is not one of the others.</summary>
        Secondary,

        /// <summary>
        /// The one thing a screen is asking the player to do next: RUN, NEXT LEVEL, GO ON.
        /// </summary>
        Primary,

        /// <summary>The way out that is not the point of the screen: KEEP TINKERING, SKIP.</summary>
        Quiet,

        /// <summary>A button that throws the player's work away: CLEAR ALL.</summary>
        Destructive,
    }
}
