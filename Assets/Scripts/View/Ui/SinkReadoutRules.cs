using System.Collections.Generic;
using System.Text;
using BitSorter.LogicCore;

namespace BitSorter.View
{
    /// <summary>
    /// What the sink readout says, when it says anything at all, and how big it has to be to say it.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="SinkReadout"/> so the wording and the appear/disappear rule can be
    /// tested without a canvas, the same way <see cref="BitsLostReadout"/> is split from
    /// <see cref="BitsLostMeter"/>.
    ///
    /// Everything here is derived from what the simulation already recorded. The component keeps no
    /// tally of its own -- CLAUDE.md's rule that anything shown to the player is derived rather than
    /// restated -- so a reset needs no telling and there is no second copy to drift.
    /// </remarks>
    public static class SinkReadoutRules
    {
        /// <summary>
        /// Shown when a sink has caught nothing yet.
        /// </summary>
        /// <remarks>
        /// A mark rather than a blank line. "Nothing arrived" is a result in free play, and an empty
        /// row reads as a rendering gap the player has to interpret rather than as an answer.
        /// </remarks>
        public const string Nothing = "--";

        /// <summary>Height of one sink's row, and the fixed chrome above and below the list.</summary>
        public const float RowHeight = 20f;

        /// <inheritdoc cref="RowHeight"/>
        public const float Chrome = 46f;

        /// <summary>
        /// Whether the readout belongs on screen.
        /// </summary>
        /// <remarks>
        /// Free play only, and that restriction is the whole justification for the panel. A graded
        /// level already states what each sink was supposed to receive and then says whether it did;
        /// showing the raw catch alongside those would be a third account of one fact. In a sandbox
        /// there is no intended answer, so what came out is the only result there is.
        ///
        /// Plain facts rather than the objects they came from, so the whole matrix is reachable from
        /// Edit Mode without a session, a runner or a canvas.
        /// </remarks>
        public static bool IsVisible(bool hasLevel, bool isGraded, bool runnerReady) =>
            hasLevel && !isGraded && runnerReady;

        /// <summary>
        /// The same rule against a level that may not be loaded yet.
        /// </summary>
        /// <remarks>
        /// The null check lives here rather than at the call site so there is one place that decides
        /// what an absent level means -- not visible, because a readout of a board that does not exist
        /// yet has nothing to report.
        /// </remarks>
        public static bool IsVisible(LevelDefinition level, bool runnerReady) =>
            IsVisible(level != null, level != null && level.IsGraded, runnerReady);

        /// <summary>Whether this sink has caught anything at all.</summary>
        public static bool CaughtAnything(IReadOnlyList<SinkNode.Reception> caught) =>
            caught != null && caught.Count > 0;

        /// <summary>
        /// Writes the caught bits into <paramref name="into"/>, in arrival order, space separated.
        /// </summary>
        /// <remarks>
        /// Takes the builder rather than returning a string because <see cref="SinkReadout"/> refreshes
        /// every frame while a run is going, and one builder reused across rows keeps that path from
        /// allocating per sink per frame.
        /// </remarks>
        public static void Write(StringBuilder into, IReadOnlyList<SinkNode.Reception> caught)
        {
            into.Clear();

            if (caught == null)
                return;

            for (int i = 0; i < caught.Count; i++)
            {
                if (i > 0)
                    into.Append(' ');

                into.Append((int)caught[i].Value);
            }
        }

        /// <summary>
        /// The row's full text: the bits, or <see cref="Nothing"/> when none have arrived.
        /// </summary>
        /// <remarks>
        /// Defined in terms of <see cref="Write"/> so the tested wording and the wording the player
        /// sees cannot drift apart -- the component takes the same two pieces and joins them itself.
        /// </remarks>
        public static string Describe(IReadOnlyList<SinkNode.Reception> caught)
        {
            if (!CaughtAnything(caught))
                return Nothing;

            var text = new StringBuilder();
            Write(text, caught);

            return text.ToString();
        }

        /// <summary>
        /// How tall the panel must be to hold <paramref name="rows"/> sinks.
        /// </summary>
        /// <remarks>
        /// Never shorter than one row. A sandbox with no sinks still draws the frame and its heading,
        /// and a panel collapsed to its chrome would read as a rendering fault rather than as an empty
        /// list. Grown to fit rather than scrolled: the board caps sinks at a single column.
        /// </remarks>
        public static float PanelHeight(int rows) => Chrome + (rows < 1 ? 1 : rows) * RowHeight;

        /// <summary>
        /// The sinks, in order, as one string.
        /// </summary>
        /// <remarks>
        /// Cheap to compare per frame, and it changes exactly when the rows would need rebuilding --
        /// which in free play is whenever a sink is added or removed.
        /// </remarks>
        public static string Signature(LevelDefinition level)
        {
            if (level == null)
                return string.Empty;

            var text = new StringBuilder();

            for (int i = 0; i < level.Fixtures.Count; i++)
            {
                if (level.Fixtures[i].Kind != FixtureKind.Sink)
                    continue;

                text.Append(level.Fixtures[i].Id).Append('|');
            }

            return text.ToString();
        }
    }
}
