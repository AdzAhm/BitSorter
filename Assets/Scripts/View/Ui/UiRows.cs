namespace BitSorter.View
{
    /// <summary>
    /// Where the HUD's rows sit: three stacks, each declared once, top to bottom or bottom to top.
    /// </summary>
    /// <remarks>
    /// Every piece of the HUD that shares an edge with another is placed from here. A component
    /// reads its row -- <c>UiRows.Clock.Offset</c> -- rather than working out where it goes, because
    /// two things that must not overlap cannot each own half the arithmetic: the project learnt that
    /// with the refusal toast and the controls line, the verdict and the first-time hint, the clock
    /// strip and the verdict, and the help panel and free play's setup panel.
    ///
    /// Heights stay in <see cref="UiTheme"/>, beside the builders that draw at them. Only where a row
    /// sits is decided here.
    /// </remarks>
    public static class UiRows
    {
        // -----------------------------------------------------------------
        // Along the top, downward from the top edge, centred
        // -----------------------------------------------------------------

        /// <summary>The stack under the top edge: banner, verdict, clock, first-time hint, tutorial.</summary>
        public static UiStack Top { get; }

        /// <summary>The status banner: the level's title and its goal.</summary>
        /// <remarks>
        /// Reserved at its tallest -- three lines of goal. The drawn banner shrinks to the goal it is
        /// showing, but every row below is placed from this, so a long goal can never push one of
        /// them off its row, and a short one leaves a slightly wider gap rather than a collision.
        /// </remarks>
        public static UiRow Banner { get; }

        /// <summary>The verdict line, hanging below the banner rather than inside it.</summary>
        /// <remarks>
        /// `StatusBanner` hangs it off the banner it draws, so it rides up under a short banner; this
        /// row is where it can reach. Nothing allowed for it once: it occupied 114-140 and the
        /// first-time hint began at 116, and both are up whenever a run settles with a gate still
        /// stalled, which is the same moment the verdict turns to FAIL.
        /// </remarks>
        public static UiRow Verdict { get; }

        /// <summary>The clock strip, under the verdict.</summary>
        /// <remarks>
        /// Worked out from the banner instead, it landed on the verdict, so "CLOCK 2 TICKS" and
        /// "FAIL -- vector 0: out wanted 1. Got 0." were drawn through each other on every clocked
        /// level that failed. Reserved on every level, not only the clocked ones: a row that came and
        /// went would move the first-time hint under the player mid-level.
        /// </remarks>
        public static UiRow Clock { get; }

        /// <summary>First-time hints, under the clock strip.</summary>
        /// <remarks>
        /// The top of the screen and not the toast row on purpose. The toast reports refusals and is
        /// coloured for them; a lesson sharing its row would read as one more thing the player did
        /// wrong. The banner is already where text is read, and the board stays clear.
        /// </remarks>
        public static UiRow Hint { get; }

        /// <summary>The tutorial's instruction strip, under the hint.</summary>
        /// <remarks>
        /// A hint and a tutorial step can both be up: a hint fires on what the board did, and the
        /// tutorial is asking for the next thing to do about it. So they stack.
        /// </remarks>
        public static UiRow Tutorial { get; }

        // -----------------------------------------------------------------
        // Along the bottom, upward from the bottom edge, centred
        // -----------------------------------------------------------------

        /// <summary>The stack above the bottom edge: run buttons, controls line, refusal toast.</summary>
        public static UiStack Bottom { get; }

        /// <summary>RUN, RESET and the rest, on the bottom margin.</summary>
        public static UiRow Buttons { get; }

        /// <summary>The keyboard reference, just above the buttons.</summary>
        public static UiRow Controls { get; }

        /// <summary>Refusals, above the controls line.</summary>
        /// <remarks>
        /// These two were placed independently once and landed on one row, so "no port there" drew
        /// straight over "drag a port to wire", at the moment the player most needed to read both.
        /// </remarks>
        public static UiRow Toast { get; }

        /// <summary>
        /// The solved card, above the refusal toast: the one row along the bottom the right-hand
        /// panels may reach down beside.
        /// </summary>
        /// <remarks>
        /// It is centred and narrower than the room the right-hand panels leave, so the two meet only
        /// in height -- and a card that pushed <see cref="PanelFloor"/> up with it would shorten the
        /// help and setup panels on every level, card or no card.
        /// </remarks>
        public static UiRow SolvedCard { get; }

        // -----------------------------------------------------------------
        // Down the right edge
        // -----------------------------------------------------------------

        /// <summary>The stack under the top-right corner: bits-lost meter, help badge, its key, panels.</summary>
        public static UiStack Right { get; }

        /// <summary>The bits-lost meter, under the board's right shoulder.</summary>
        public static UiRow BitsLost { get; }

        /// <summary>The help badge.</summary>
        /// <remarks>
        /// Ten below the meter rather than the usual eight, which is where it has always been: this
        /// stack was brought in to move no pixel. Neither figure clears the meter's punch, which
        /// scales it from its top-right corner -- at its peak it reaches sixteen pixels further down,
        /// over the top of the badge for a moment.
        /// </remarks>
        public static UiRow Badge { get; }

        /// <summary>The "H" hanging under the badge, which is part of the badge's block.</summary>
        /// <remarks>
        /// The badge is round and unlabelled, so this is what says it is a button and how to open it
        /// without aiming -- which is why nothing below may start above it.
        /// </remarks>
        public static UiRow BadgeKey { get; }

        /// <summary>
        /// The row the help panel and free play's setup panel both start on, reaching down to
        /// <see cref="PanelFloor"/>.
        /// </summary>
        /// <remarks>
        /// Open-ended: the setup panel stretches to the floor and the help panel is as tall as what
        /// it holds, so the row has no height of its own. Both start here rather than stating their
        /// own tops -- the help panel once said <c>-(Margin + 100)</c> while the setup panel beside it
        /// derived its top, and the two disagreed by exactly the amount that hid one behind the other.
        /// Side by side on one row, they take different halves of the column:
        /// <see cref="UiTheme.HelpRight"/>.
        /// </remarks>
        public static UiRow Panels { get; }

        /// <summary>
        /// How far above the bottom edge a panel down the right may reach: clear of the refusal
        /// toast, the highest full-width row along the bottom. Not of <see cref="SolvedCard"/>,
        /// which sits between the panels rather than under them.
        /// </summary>
        public static float PanelFloor { get; }

        /// <summary>Every stack, for whatever checks them.</summary>
        public static UiStack[] All => new[] { Top, Bottom, Right };

        static UiRows()
        {
            Top = new UiStack("top", UiTheme.Margin, UiTheme.Gap);
            Banner = Top.Add("banner", UiTheme.BannerHeight);
            Verdict = Top.Add("verdict", UiTheme.VerdictLineHeight, UiTheme.VerdictGap);
            Clock = Top.Add("clock", UiTheme.ClockHeight);
            Hint = Top.Add("first-time hint", UiTheme.HintHeight);
            Tutorial = Top.Add("tutorial", UiTheme.TutorialHeight);

            Bottom = new UiStack("bottom", UiTheme.Margin, UiTheme.Gap);
            Buttons = Bottom.Add("run buttons", UiTheme.ButtonHeight);
            Controls = Bottom.Add("controls line", UiTheme.ControlsHeight);
            Toast = Bottom.Add("refusal toast", UiTheme.ToastHeight);
            PanelFloor = Bottom.Next;
            SolvedCard = Bottom.Add("solved card", UiTheme.SolvedCardHeight);

            Right = new UiStack("right", UiTheme.Margin, UiTheme.Gap);
            BitsLost = Right.Add("bits-lost meter", UiTheme.BitsLostHeight);
            Badge = Right.Add("help badge", UiTheme.BadgeSize, UiTheme.Gap + 2f);
            BadgeKey = Right.Add("badge key", UiTheme.BadgeKeyHeight, 0f);
            Panels = Right.Add("help and setup panels", 0f);
        }
    }
}
