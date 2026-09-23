namespace BitSorter.View
{
    /// <summary>
    /// A cursor down a panel: each row takes its height and the space under it, and the next row
    /// starts below that. Distances are measured down from the panel's top edge.
    /// </summary>
    /// <remarks>
    /// Panels whose contents depend on the level -- free play's setup, the level list -- were laid
    /// out by passing a float down through every builder and having each one hand it back lowered
    /// by whatever it had drawn. That works, and it spreads one fact across every builder: each
    /// had to remember to subtract its own height and its own gap, in its own sign convention, and
    /// a builder that forgot put the next row on top of it. Here a row says how tall it is and how
    /// much room it wants under it, once, and the cursor does the arithmetic.
    ///
    /// The within-a-panel counterpart to <see cref="UiStack"/>, which places the HUD's panels
    /// against the edges of the screen. Unlike a stack it keeps no rows: a panel is rebuilt
    /// whenever its contents change, and nothing needs to look a row up again afterwards.
    /// </remarks>
    public sealed class UiColumn
    {
        private float _next;

        /// <param name="top">How far below the panel's top edge the first row starts.</param>
        public UiColumn(float top = 0f)
        {
            _next = top;
        }

        /// <summary>How far below the top the next row will start -- so, how much has been used.</summary>
        public float Next => _next;

        /// <summary>
        /// A row of <paramref name="height"/>, with <paramref name="spaceAfter"/> left clear under it.
        /// Returns how far below the top it starts.
        /// </summary>
        public float Take(float height, float spaceAfter = 0f)
        {
            float top = _next;
            _next += height + spaceAfter;
            return top;
        }

        /// <summary>Leaves <paramref name="space"/> empty before the next row.</summary>
        public void Space(float space) => _next += space;
    }
}
