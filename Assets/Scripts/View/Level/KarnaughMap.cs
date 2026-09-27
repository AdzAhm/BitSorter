using System.Collections.Generic;
using System.Text;

namespace BitSorter.View
{
    /// <summary>
    /// Renders one bin of a level's truth table as a Karnaugh map.
    /// </summary>
    /// <remarks>
    /// The same function the table shows, laid out the way the course teaches minimisation: rows and
    /// columns in Gray order, so neighbouring cells differ in one input and a group of 1s is a term.
    /// Derived from the level's streams and expectations exactly as <see cref="TruthTable"/> is, so
    /// it cannot disagree with what the grader checks.
    ///
    /// One bin per map, shown one at a time. Stacking every bin's map made the K-map the tallest
    /// thing in the help panel rather than the most compact: three four-input maps are 23 lines,
    /// which does not fit between the badge and the run buttons. One map is at most six lines, and
    /// no level's table is shorter than its map, so the table alone decides the panel's size.
    ///
    /// No groups are drawn. The groups are the answer.
    /// </remarks>
    public static class KarnaughMap
    {
        /// <summary>
        /// Whether this level has a map worth showing: a combinational function of two to four
        /// inputs, with each combination tested at most once.
        /// </summary>
        /// <remarks>
        /// One input has no map worth the name, and past four a map stops being something a student
        /// reads by eye -- the course stops there too. A register makes the output depend on history
        /// the map has no axis for, and a clock spaces the vectors out, which is the sequential
        /// chapter's business either way.
        ///
        /// A combination tested twice could want two different answers, and a cell holds one. None
        /// of the shipped levels does it; refusing it here means one that did would lose its map
        /// rather than show whichever answer happened to be written last.
        /// </remarks>
        public static bool Applies(LevelDefinition level)
        {
            if (level == null || LevelCatalog.IsSequential(level) || level.HasClock)
                return false;

            List<LevelFixture> sources = Sources(level);

            if (sources.Count < 2 || sources.Count > 4)
                return false;

            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].Stream.Count != level.VectorCount)
                    return false;
            }

            for (int i = 0; i < level.Expectations.Count; i++)
            {
                if ((level.Expectations[i].Values?.Length ?? 0) != level.VectorCount)
                    return false;
            }

            var seen = new HashSet<int>();

            for (int vector = 0; vector < level.VectorCount; vector++)
            {
                if (!seen.Add(Combination(sources, vector)))
                    return false;
            }

            return GradedBins(level).Count > 0;
        }

        /// <summary>The bins that get a map, in the level's expectation order; empty when it has none.</summary>
        /// <remarks>
        /// A bin that must stay empty on every row has no function to map, so it gets no map and no
        /// tab -- route-the-bit's second bin is the shape of it, though that level has one input and
        /// no map at all.
        /// </remarks>
        public static IReadOnlyList<string> Bins(LevelDefinition level) =>
            Applies(level) ? GradedBins(level) : new List<string>();

        /// <summary>
        /// One bin's map, as monospaced lines; empty when the level has no map or grades no such bin.
        /// </summary>
        /// <remarks>
        /// The first half of the inputs, rounded down, label the rows and the rest the columns, so
        /// two inputs make a 2 x 2 map, three a 2 x 4 and four a 4 x 4. A cell shows what the bin
        /// should get when the row's inputs are followed by the column's, in the level's source
        /// order: a 0 or a 1, an x where the level says either will do, a dot where it wants nothing
        /// at all, and an x for a combination no row tests -- to the grader that one is also free.
        /// </remarks>
        public static string Format(LevelDefinition level, string sinkId)
        {
            LevelExpectation bin = BinById(level, sinkId);

            if (bin == null)
                return string.Empty;

            List<LevelFixture> sources = Sources(level);
            int rowCount = sources.Count / 2;
            List<LevelFixture> rowInputs = sources.GetRange(0, rowCount);
            List<LevelFixture> columnInputs = sources.GetRange(rowCount, sources.Count - rowCount);

            string[] rowCodes = GrayCodes(rowInputs.Count);
            string[] columnCodes = GrayCodes(columnInputs.Count);

            string rowLabel = Label(rowInputs);
            int rowWidth = rowLabel.Length > rowInputs.Count ? rowLabel.Length : rowInputs.Count;

            var text = new StringBuilder();

            // The column inputs' name, over the first code's first digit.
            text.Append(' ', rowWidth + Gutter.Length + CellWidth - columnInputs.Count);
            text.Append(Label(columnInputs));
            text.Append('\n');   // explicit, so the map reads the same on every platform

            text.Append(rowLabel.PadLeft(rowWidth)).Append(Gutter);
            AppendCells(text, columnCodes);

            for (int r = 0; r < rowCodes.Length; r++)
            {
                var cells = new string[columnCodes.Length];

                for (int c = 0; c < columnCodes.Length; c++)
                    cells[c] = CellFor(level, sources, bin, rowCodes[r] + columnCodes[c]);

                text.Append(rowCodes[r].PadLeft(rowWidth)).Append(Gutter);
                AppendCells(text, cells);
            }

            return text.ToString();
        }

        /// <summary>
        /// How many lines one of this level's maps takes: the input names, the column codes, and a
        /// line per row code. Zero when the level has no map.
        /// </summary>
        public static int LineCount(LevelDefinition level) =>
            Applies(level) ? 2 + GrayCodes(Sources(level).Count / 2).Length : 0;

        /// <summary>Between the row codes and the grid.</summary>
        private const string Gutter = "  ";

        /// <summary>
        /// Every cell is two characters wide, so a one-input axis ("0", "1") spaces its cells the
        /// same as a two-input one ("00", "01") and every map has the same pitch.
        /// </summary>
        private const int CellWidth = 2;

        private static void AppendCells(StringBuilder text, string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                    text.Append(' ');

                text.Append(cells[i].PadLeft(CellWidth));
            }

            text.Append('\n');   // explicit, so the map reads the same on every platform
        }

        /// <summary>
        /// What one cell shows, for the inputs spelled out in <paramref name="bits"/>, one character
        /// per source in source order.
        /// </summary>
        private static string CellFor(
            LevelDefinition level, List<LevelFixture> sources, LevelExpectation bin, string bits)
        {
            int wanted = 0;

            for (int i = 0; i < bits.Length; i++)
                wanted = (wanted << 1) | (bits[i] == '1' ? 1 : 0);

            for (int vector = 0; vector < level.VectorCount; vector++)
            {
                if (Combination(sources, vector) != wanted)
                    continue;

                char c = bin.Values[vector];

                // A silent vector as a dot, as the table draws it: a dash beside noughts reads as a
                // minus sign.
                return c == '-' ? "." : c.ToString();
            }

            return "x";
        }

        /// <summary>A vector's inputs as one number, first source most significant.</summary>
        private static int Combination(List<LevelFixture> sources, int vector)
        {
            int combination = 0;

            for (int i = 0; i < sources.Count; i++)
                combination = (combination << 1) | (int)sources[i].Stream[vector];

            return combination;
        }

        /// <summary>Gray order: neighbours differ in one bit, and so do the two ends.</summary>
        private static string[] GrayCodes(int inputs) =>
            inputs == 1 ? new[] { "0", "1" } : new[] { "00", "01", "11", "10" };

        /// <summary>
        /// The inputs' names as the board labels them, run together when each is one letter ("BC")
        /// and spaced when any is longer ("I1 I0"), so a name never runs into its neighbour.
        /// </summary>
        private static string Label(List<LevelFixture> inputs)
        {
            bool oneLetterEach = true;

            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Id.Length != 1)
                    oneLetterEach = false;
            }

            var names = new string[inputs.Count];

            for (int i = 0; i < inputs.Count; i++)
                names[i] = LevelRules.BoardLabel(inputs[i].Id);

            return string.Join(oneLetterEach ? string.Empty : " ", names);
        }

        private static List<LevelFixture> Sources(LevelDefinition level)
        {
            var sources = new List<LevelFixture>();

            foreach (LevelFixture fixture in level.Fixtures)
            {
                if (fixture.Kind == FixtureKind.Source)
                    sources.Add(fixture);
            }

            return sources;
        }

        private static List<string> GradedBins(LevelDefinition level)
        {
            var bins = new List<string>();

            for (int i = 0; i < level.Expectations.Count; i++)
            {
                string values = level.Expectations[i].Values ?? string.Empty;

                if (values.Trim('-').Length > 0)
                    bins.Add(level.Expectations[i].SinkId);
            }

            return bins;
        }

        private static LevelExpectation BinById(LevelDefinition level, string sinkId)
        {
            if (!Applies(level))
                return null;

            for (int i = 0; i < level.Expectations.Count; i++)
            {
                LevelExpectation expectation = level.Expectations[i];

                if (expectation.SinkId == sinkId && GradedBins(level).Contains(sinkId))
                    return expectation;
            }

            return null;
        }
    }
}
