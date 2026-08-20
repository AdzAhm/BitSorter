namespace BitSorter.View
{
    /// <summary>
    /// A stepper's floor and ceiling, and what its two buttons may do at a given value.
    /// </summary>
    /// <remarks>
    /// A pair rather than two loose ints because the enable rule and the clamp have to agree. When
    /// they disagree the player gets a live button that does nothing, or -- the failure this exists to
    /// prevent -- a value outside the range with both buttons dead and no way back.
    /// </remarks>
    public readonly struct StepRange
    {
        public readonly int Min;
        public readonly int Max;

        public StepRange(int min, int max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>Whether the minus button is live.</summary>
        public bool CanDecrease(int value) => value > Min;

        /// <summary>Whether the plus button is live.</summary>
        public bool CanIncrease(int value) => value < Max;

        /// <summary>
        /// Whether a value can be moved at all.
        /// </summary>
        /// <remarks>
        /// The stranding check. A stepper where this is false shows the player two dead buttons and a
        /// number they cannot change -- which in the sandbox means a setup they cannot fix and no
        /// error explaining why. True for every range with room in it, at every value inside it.
        /// </remarks>
        public bool CanMove(int value) => CanDecrease(value) || CanIncrease(value);

        public int Clamp(int value) => value < Min ? Min : value > Max ? Max : value;
    }

    /// <summary>
    /// The sandbox panel's decisions: what each stepper may reach, and what clicking a bit does.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="SandboxPanel"/> so the bounds and the flip can be tested without a
    /// canvas, the same way <see cref="PointerRules"/> is split from <see cref="PointerGate"/>.
    ///
    /// The bounds live here rather than being written into the panel twice. The panel used to state
    /// each limit once when enabling a button and again when clamping the result, which is two copies
    /// of one fact and so two things to drift -- exactly what CLAUDE.md's derived-never-restated rule
    /// is about.
    ///
    /// Free play has nothing to be wrong about, so none of this refuses anything. Counts are clamped
    /// and streams are padded rather than rejected; the only hard floor is on vectors, because a
    /// stream of length zero emits nothing and the board would sit silent with no explanation.
    /// </remarks>
    public static class SandboxRules
    {
        /// <summary>
        /// How many sources the board has room for.
        /// </summary>
        /// <remarks>
        /// Zero is allowed and is not a mistake to be prevented: a sandbox with no sources is a
        /// legitimate intermediate state while the player rearranges, and
        /// <see cref="SandboxLevel.Warning"/> says plainly why nothing happens. The trap CLAUDE.md
        /// names applies here -- zero and unlimited both look falsy, so this asks whether a count
        /// <c>== 0</c> and never whether it is <c>&lt;= 0</c>.
        /// </remarks>
        public static StepRange Sources(int capacity) => new StepRange(0, capacity);

        /// <inheritdoc cref="Sources"/>
        public static StepRange Sinks(int capacity) => new StepRange(0, capacity);

        /// <summary>
        /// How many test vectors every source emits.
        /// </summary>
        /// <remarks>
        /// Floored at one, unlike the other two. Sources and sinks may go to zero because the board
        /// simply has none; vectors going to zero would leave every existing source holding an empty
        /// stream, emitting nothing, with the sources still drawn on the board and nothing to say why.
        /// </remarks>
        public static StepRange Vectors() =>
            new StepRange(SandboxConfig.MinVectors, SandboxConfig.MaxVectors);

        /// <summary>
        /// The stream with the bit at <paramref name="index"/> inverted.
        /// </summary>
        /// <remarks>
        /// Returns the stream unchanged for an index outside it, rather than throwing or padding. The
        /// panel rebuilds its rows on every edit, so a click can only land on a bit that was drawn --
        /// but a stale listener firing after a rebuild is the kind of thing that should do nothing
        /// rather than take down the frame.
        /// </remarks>
        public static string Flip(string stream, int index)
        {
            if (stream == null || index < 0 || index >= stream.Length)
                return stream;

            char[] bits = stream.ToCharArray();
            bits[index] = bits[index] == '1' ? '0' : '1';

            return new string(bits);
        }
    }
}
