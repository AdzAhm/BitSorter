namespace BitSorter.ViewModel
{
    /// <summary>
    /// The line a browser says, once a session, when drawing a document or working out its timing took long (Part 15g): the
    /// same simulator runs several times faster built for the desktop, and a student with a large drawing should know that
    /// before deciding the simulator is slow. Agreed with Ahmad: one codebase, the same features in both builds, and this
    /// one line where they differ (2026-10-28).
    /// </summary>
    public static class DesktopHint
    {
        /// <summary>How long, in milliseconds, is long: a pause a student notices after an edit.</summary>
        public const double SlowMilliseconds = 300;

        public const string Words = "A drawing this big takes the browser a moment after each edit: the desktop version of BitSorter runs it faster.";

        /// <summary>
        /// Whether to say it now: in a browser, not said yet this session, after drawing or timing work that took longer than
        /// <see cref="SlowMilliseconds"/> -- the slowest since last asked (<see cref="WorkbenchSession.TakeSlowestMilliseconds"/>).
        /// </summary>
        public static bool Due(bool inBrowser, bool saidAlready, double slowestMilliseconds) =>
            inBrowser && !saidAlready && slowestMilliseconds > SlowMilliseconds;
    }
}
