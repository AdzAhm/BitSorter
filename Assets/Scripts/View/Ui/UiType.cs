namespace BitSorter.View
{
    /// <summary>
    /// What a piece of text is for, which decides how large it is drawn.
    /// </summary>
    /// <remarks>
    /// Seventeen sizes were in use, from 10 to 54, each chosen where the label was written: full-
    /// screen titles alone came in 40, 44 and 54, and a panel's small print in 12, 13 and 14. A
    /// size picked per label is a hierarchy nobody can see as a whole, so two things of the same
    /// kind drifted apart and two of different kinds ended up a point apart. A label now says what
    /// it is, and <see cref="UiTheme.SizeOf"/> says how large that is -- once.
    ///
    /// Nothing is smaller than <see cref="Micro"/>, twelve: below that the eleven-pixel music
    /// credit, the one line a licence requires be shown, was too small to read.
    /// </remarks>
    public enum UiType
    {
        /// <summary>The smallest print: a column number, a credit, a key under a badge.</summary>
        Micro,

        /// <summary>Small print beside something larger: a record, a note, a help line.</summary>
        Caption,

        /// <summary>Secondary text: a part's name, a count, a hint, the controls line.</summary>
        Label,

        /// <summary>Text meant to be read: a goal, a level's name, a card's body, a button.</summary>
        Body,

        /// <summary>A single value read at a glance: the bits lost, the help badge's mark.</summary>
        Numeral,

        /// <summary>A panel's title: the level's name on the banner, LEVELS, SOLVED.</summary>
        Heading,

        /// <summary>A full-screen card's title.</summary>
        Title,

        /// <summary>The game's own name, on the main menu.</summary>
        Display,
    }
}
