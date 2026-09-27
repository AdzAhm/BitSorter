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

            var text = new StringBuilder();

            string header = Header(sources, level.Expectations);
            text.Append(header).Append('\n');   // explicit, so the table reads the same on every platform
            AppendRule(text, header);

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
                AppendRow(text, sources, level.Expectations, cycle);

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
        /// How many characters a column is: its own name's length.
        /// </summary>
        /// <remarks>
        /// Each column its own, not every column the longest name in the table. They were all padded
        /// to one width, so on the half adder -- whose longest name is "carry" -- "a" and "b" sat six
        /// characters apart with nothing under their headings but a lone dash, and a playtester took
        /// the table for broken (2026-09-27).
        ///
        /// Never truncated: a fixed width has to truncate and truncation collides. It was three, and
        /// route-the-bit grades `binOne` and `binZero` -- both of which came out as "bin", on the one
        /// level whose lesson is that the other bin must stay empty. A level with a very long fixture
        /// id gets a wide column, which is visible and tells the author to shorten the id.
        /// </remarks>
        private static int ColumnWidth(string name) => name.Length > 1 ? name.Length : 1;

        /// <summary>Between two columns, and around the bar between inputs and outputs.</summary>
        private const string Gap = "  ";

        private static string Header(List<LevelFixture> sources, IReadOnlyList<LevelExpectation> sinks)
        {
            var text = new StringBuilder();

            for (int i = 0; i < sources.Count; i++)
                text.Append(i > 0 ? Gap : string.Empty).Append(sources[i].Id);

            text.Append(Gap).Append('|');

            for (int i = 0; i < sinks.Count; i++)
                text.Append(Gap).Append(sinks[i].SinkId);

            return text.ToString();
        }

        /// <summary>
        /// A solid rule under the header, crossing the bar with a plus: a line, where it was a dash
        /// under each column that read as a row of minus signs.
        /// </summary>
        private static void AppendRule(StringBuilder text, string header)
        {
            int bar = header.IndexOf('|');

            for (int i = 0; i < header.Length; i++)
                text.Append(i == bar ? '+' : '-');

            text.Append('\n');   // explicit, so the table reads the same on every platform
        }

        private static void AppendRow(
            StringBuilder text, List<LevelFixture> sources,
            IReadOnlyList<LevelExpectation> sinks, int vector)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                LevelFixture source = sources[i];

                // Past the end of the streams is a cycle with nothing going in, not a mistake: the
                // gap is drawn the same way a silent vector is, since it means the same thing here.
                string bit = vector < source.Stream.Count
                    ? ((int)source.Stream[vector]).ToString()
                    : ".";

                text.Append(i > 0 ? Gap : string.Empty).Append(Cell(bit, ColumnWidth(source.Id)));
            }

            text.Append(Gap).Append('|');

            for (int i = 0; i < sinks.Count; i++)
            {
                string values = sinks[i].Values;

                // Past the end of this sink's expectation is a cycle it is not asked about, drawn
                // the same way the source columns draw a cycle with nothing going in. It happens
                // when one sink is fed through a register and another is not: the first runs a
                // cycle longer, and the table is as long as the longest.
                char c = vector < values.Length ? values[vector] : '.';

                // A silent vector is shown as a gap rather than a dash, because a dash next to a
                // column of noughts reads as a minus sign. A don't-care keeps its 'x'.
                text.Append(Gap).Append(Cell(c == '-' ? "." : c.ToString(), ColumnWidth(sinks[i].SinkId)));
            }

            text.Append('\n');   // explicit, so the table reads the same on every platform
        }

        /// <summary>
        /// One value, centred under its column's heading so the table lines up in a monospaced
        /// block. An odd width leaves the value dead centre; an even one puts it just left of it.
        /// </summary>
        /// <remarks>
        /// Centred rather than right-aligned. Right-aligned, a digit under "carry" sat under its
        /// last letter, away from the heading's middle, where the eye looks for it.
        /// </remarks>
        private static string Cell(string content, int width)
        {
            int left = (width - content.Length) / 2;

            return content.PadLeft(content.Length + left).PadRight(width);
        }
    }
}
