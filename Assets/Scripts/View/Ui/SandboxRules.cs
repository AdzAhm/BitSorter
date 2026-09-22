using System.Collections.Generic;
using BitSorter.LogicCore;

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
        /// Ticks between vectors. One is a vector every tick, as every level was before registers.
        /// </summary>
        /// <remarks>
        /// Worth raising the moment a register's output feeds anything that comes back to it: the
        /// shortest loop takes two ticks, so on a clock of 1 a state machine loses a bit a cycle
        /// however carefully it is wired.
        /// </remarks>
        public static StepRange Clock() =>
            new StepRange(SandboxConfig.MinClock, SandboxConfig.MaxClock);

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

        // -----------------------------------------------------------------
        // Filling the inputs
        // -----------------------------------------------------------------

        /// <summary>
        /// Most sources the truth-table button will fill.
        /// </summary>
        /// <remarks>
        /// Four would need sixteen vectors and free play streams eight. The button says so rather
        /// than disappearing, because "why is this dead" is the question a missing control cannot
        /// answer.
        /// </remarks>
        public const int MaxTableSources = 3;

        /// <summary>
        /// Why the truth-table button is off, or null when it is on.
        /// </summary>
        /// <remarks>
        /// One answer for both the button's state and the words beside it, so the two cannot
        /// disagree. They did: the button was off whenever a table would not fit *or* the streams
        /// already were one, and the note only knew about the first. A fresh sandbox opens in the
        /// second, so every new free-play session told the player that a table "needs 3 sources
        /// or fewer" while showing them two -- a false reason, which is worse than none, because
        /// it sends somebody looking for a problem that is not there.
        /// </remarks>
        public static string WhyNoTable(IReadOnlyList<string> sources, int vectors)
        {
            int count = sources?.Count ?? 0;

            if (count == 0)
                return "A table needs a source.";

            if (!CanFillTable(count))
                return $"A table of every combination needs {MaxTableSources} sources or fewer.";

            if (IsTable(sources, vectors))
                return "These streams are already every combination.";

            return null;
        }

        /// <summary>Whether every combination of this many sources fits in the vectors there are.</summary>
        public static bool CanFillTable(int sources) =>
            sources >= 1 && sources <= MaxTableSources && VectorsForTable(sources) <= SandboxConfig.MaxVectors;

        /// <summary>
        /// Whether the streams already are the full table, so filling them would change nothing.
        /// </summary>
        /// <remarks>
        /// A fresh sandbox opens on the two-input table -- it is what most circuits worth trying
        /// want fed into them -- so the button that fills it in is a no-op until the player edits
        /// something. Pressing a button and having nothing happen reads as broken, which is what
        /// it was reported as. Knowing the answer lets the button grey itself out instead, the way
        /// it already does when a full table would not fit.
        /// </remarks>
        public static bool IsTable(IReadOnlyList<string> sources, int vectors)
        {
            if (sources == null || !CanFillTable(sources.Count))
                return false;

            if (vectors != VectorsForTable(sources.Count))
                return false;

            string[] table = Table(sources.Count);

            for (int i = 0; i < table.Length; i++)
            {
                string stream = sources[i];

                // Streams are stored at MaxVectors and the level takes the first few, so only the
                // vectors actually in play are compared.
                if (stream == null || stream.Length < vectors)
                    return false;

                if (stream.Substring(0, vectors) != table[i])
                    return false;
            }

            return true;
        }

        /// <summary>How many vectors a full table of this many sources takes.</summary>
        public static int VectorsForTable(int sources) => 1 << sources;

        /// <summary>
        /// Every combination of <paramref name="sources"/> inputs, one stream each.
        /// </summary>
        /// <remarks>
        /// A counts slowest, the way it does in the levels' own tables and the way anyone writing one
        /// out by hand does it -- A is the most significant bit. Getting that backwards would give a
        /// table that is complete but reads upside down against every truth table in the game.
        /// </remarks>
        public static string[] Table(int sources)
        {
            if (!CanFillTable(sources))
                return System.Array.Empty<string>();

            int vectors = VectorsForTable(sources);
            var streams = new string[sources];

            for (int i = 0; i < sources; i++)
            {
                var bits = new char[vectors];
                int weight = sources - 1 - i;

                for (int v = 0; v < vectors; v++)
                    bits[v] = ((v >> weight) & 1) == 1 ? '1' : '0';

                streams[i] = new string(bits);
            }

            return streams;
        }

        // -----------------------------------------------------------------
        // Reading the outputs
        // -----------------------------------------------------------------

        /// <summary>
        /// Shown in a column no bit has arrived in.
        /// </summary>
        /// <remarks>
        /// A mark rather than a blank. "Nothing arrived" is a result in free play -- it is how a
        /// player sees that a gate is stalled or that a bit was lost -- and an empty cell reads as a
        /// rendering gap instead of an answer.
        /// </remarks>
        public const string Missing = "·";

        /// <summary>
        /// What a sink's nth column says: the bit that landed there, or <see cref="Missing"/>.
        /// </summary>
        /// <remarks>
        /// Arrival order, not vector number, and the two are not the same: a circuit that drops a bit
        /// shifts everything after it one column left. The column is a position in what came out, so
        /// what the player sees is what arrived.
        /// </remarks>
        public static string Cell(IReadOnlyList<SinkNode.Reception> caught, int column)
        {
            if (caught == null || column < 0 || column >= caught.Count)
                return Missing;

            return caught[column].Value == Bit.One ? "1" : "0";
        }

        /// <summary>
        /// Bits beyond the columns there are, or zero.
        /// </summary>
        /// <remarks>
        /// A sink can catch more than one bit per vector -- a fan-in, or a loop -- and a readout that
        /// simply stopped at the last column would hide exactly the surprise the player is looking
        /// for. Counted rather than drawn, and shown as "+n".
        /// </remarks>
        public static int Extra(IReadOnlyList<SinkNode.Reception> caught, int columns)
        {
            if (caught == null || caught.Count <= columns)
                return 0;

            return caught.Count - columns;
        }

        // -----------------------------------------------------------------
        // Run speed
        // -----------------------------------------------------------------

        /// <summary>
        /// What the speed buttons offer.
        /// </summary>
        /// <remarks>
        /// Free play only. A taught level runs at the authored rate, which is slow enough to watch a
        /// bit move, and the speed is not part of any puzzle -- but a sandbox is where somebody
        /// builds a circuit that takes fifty seconds to say what it does.
        ///
        /// Powers of two rather than a slider: three buttons have three states to reason about, and
        /// the clock divides exactly.
        /// </remarks>
        public static readonly int[] Speeds = { 1, 2, 4 };

        /// <summary>The nearest offered speed, defaulting to the authored one.</summary>
        public static int ClampSpeed(int speed)
        {
            for (int i = 0; i < Speeds.Length; i++)
            {
                if (Speeds[i] == speed)
                    return speed;
            }

            return Speeds[0];
        }
    }
}
