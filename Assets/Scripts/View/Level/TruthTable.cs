using System.Collections.Generic;
using System.Text;

namespace BitSorter.View
{
    /// <summary>
    /// Renders a level's test vectors as the truth table they already are.
    /// </summary>
    /// <remarks>
    /// Nothing new is invented here. A level's sources carry one bit per vector and its expectations
    /// carry one character per vector, which *is* a truth table -- it was simply never shown, so a
    /// level like four-corners had to describe its function in prose ("a 1 on every row except
    /// A=0 B=1 C=1..."), which is unreadable at eight rows and gets worse with every input.
    ///
    /// Derived rather than authored, so it cannot disagree with what the grader checks. There is no
    /// second copy of the function to keep in step.
    ///
    /// Pure and string-returning so the layout is testable without a canvas.
    /// </remarks>
    public static class TruthTable
    {
        /// <summary>
        /// The whole table, one row per vector, columns separated by spaces and inputs separated
        /// from outputs by a bar. Rendered in a monospaced block by the panel that shows it.
        /// </summary>
        public static string Format(LevelDefinition level)
        {
            if (level == null)
                return string.Empty;

            var sources = new List<LevelFixture>();

            foreach (LevelFixture fixture in level.Fixtures)
            {
                if (fixture.Kind == FixtureKind.Source)
                    sources.Add(fixture);
            }

            if (sources.Count == 0 || level.Expectations.Count == 0)
                return string.Empty;

            int width = ColumnWidth(sources, level.Expectations);

            var text = new StringBuilder();

            AppendHeader(text, sources, level.Expectations, width);
            AppendRule(text, sources, level.Expectations, width);

            // One row per clock cycle, which is one per vector until a register is involved: a sink
            // fed through one receives the bit it was holding after the last vector has gone in, so
            // its expected values run a cycle past the streams and that cycle has a row too.
            int cycles = level.VectorCount;

            for (int i = 0; i < level.Expectations.Count; i++)
            {
                int length = level.Expectations[i].Values?.Length ?? 0;

                if (length > cycles)
                    cycles = length;
            }

            for (int cycle = 0; cycle < cycles; cycle++)
                AppendRow(text, sources, level.Expectations, cycle, width);

            return text.ToString();
        }

        /// <summary>
        /// Characters in the widest line of a rendered table, so a panel can size itself to it.
        /// </summary>
        /// <remarks>
        /// Derived from the finished table rather than recomputed from the level, so a panel and the
        /// text inside it cannot disagree about how wide the table is.
        /// </remarks>
        public static int WidestLine(string table)
        {
            int widest = 0;
            int current = 0;

            for (int i = 0; i < (table?.Length ?? 0); i++)
            {
                if (table[i] != '\n')
                {
                    current++;
                    continue;
                }

                if (current > widest)
                    widest = current;

                current = 0;
            }

            return current > widest ? current : widest;
        }

        /// <summary>
        /// How many characters every column is padded to: the longest name in this table, never
        /// fewer than three.
        /// </summary>
        /// <remarks>
        /// Derived rather than fixed, because a fixed width has to truncate and truncation
        /// collides. It was three, and route-the-bit grades `binOne` and `binZero` -- both of which
        /// came out as "bin", on the one level whose lesson is that the other bin must stay empty.
        ///
        /// There is no cap. A level with a very long fixture id gets a wide table, which is visible
        /// and self-explanatory and tells the author to shorten the id; two columns with the same
        /// heading is neither. Every shipped level's longest name is seven characters.
        /// </remarks>
        private static int ColumnWidth(
            List<LevelFixture> sources, IReadOnlyList<LevelExpectation> sinks)
        {
            int width = 3;

            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].Id.Length > width)
                    width = sources[i].Id.Length;
            }

            for (int i = 0; i < sinks.Count; i++)
            {
                if (sinks[i].SinkId.Length > width)
                    width = sinks[i].SinkId.Length;
            }

            return width;
        }

        private static void AppendHeader(
            StringBuilder text, List<LevelFixture> sources, IReadOnlyList<LevelExpectation> sinks,
            int width)
        {
            for (int i = 0; i < sources.Count; i++)
                text.Append(Cell(sources[i].Id, width));

            text.Append(" |");

            for (int i = 0; i < sinks.Count; i++)
                text.Append(Cell(sinks[i].SinkId, width));

            text.Append('\n');   // explicit, so the table reads the same on every platform
        }

        private static void AppendRule(
            StringBuilder text, List<LevelFixture> sources, IReadOnlyList<LevelExpectation> sinks,
            int width)
        {
            for (int i = 0; i < sources.Count; i++)
                text.Append(Cell("-", width));

            text.Append(" +");

            for (int i = 0; i < sinks.Count; i++)
                text.Append(Cell("-", width));

            text.Append('\n');   // explicit, so the table reads the same on every platform
        }

        private static void AppendRow(
            StringBuilder text, List<LevelFixture> sources,
            IReadOnlyList<LevelExpectation> sinks, int vector, int width)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                LevelFixture source = sources[i];

                // Past the end of the streams is a cycle with nothing going in, not a mistake: the
                // gap is drawn the same way a silent vector is, since it means the same thing here.
                string bit = vector < source.Stream.Count
                    ? ((int)source.Stream[vector]).ToString()
                    : ".";

                text.Append(Cell(bit, width));
            }

            text.Append(" |");

            for (int i = 0; i < sinks.Count; i++)
            {
                string values = sinks[i].Values;
                char c = vector < values.Length ? values[vector] : '?';

                // A silent vector is shown as a gap rather than a dash, because a dash next to a
                // column of noughts reads as a minus sign. A don't-care keeps its 'x'.
                text.Append(Cell(c == '-' ? "." : c.ToString(), width));
            }

            text.Append('\n');   // explicit, so the table reads the same on every platform
        }

        /// <summary>
        /// One column, padded to <paramref name="width"/> so the table lines up in a monospaced
        /// block. Never truncates: the width is chosen to fit.
        /// </summary>
        /// <remarks>
        /// This used to truncate to three characters, on the grounds that three is enough to tell
        /// "sum" from "cout". It is, and it is not enough to tell "binOne" from "binZero" -- see
        /// <see cref="ColumnWidth"/>. A heading has to be the name the level's goal uses, because
        /// that is the string the player is reading everywhere else.
        /// </remarks>
        private static string Cell(string content, int width) =>
            " " + content.PadLeft(width);
    }
}
