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

            AppendHeader(text, sources, level.Expectations);
            AppendRule(text, sources, level.Expectations);

            for (int vector = 0; vector < level.VectorCount; vector++)
                AppendRow(text, sources, level.Expectations, vector);

            return text.ToString();
        }

        private static void AppendHeader(
            StringBuilder text, List<LevelFixture> sources, IReadOnlyList<LevelExpectation> sinks)
        {
            for (int i = 0; i < sources.Count; i++)
                text.Append(Cell(sources[i].Id));

            text.Append(" |");

            for (int i = 0; i < sinks.Count; i++)
                text.Append(Cell(sinks[i].SinkId));

            text.Append('\n');   // explicit, so the table reads the same on every platform
        }

        private static void AppendRule(
            StringBuilder text, List<LevelFixture> sources, IReadOnlyList<LevelExpectation> sinks)
        {
            for (int i = 0; i < sources.Count; i++)
                text.Append(Cell("-"));

            text.Append(" +");

            for (int i = 0; i < sinks.Count; i++)
                text.Append(Cell("-"));

            text.Append('\n');   // explicit, so the table reads the same on every platform
        }

        private static void AppendRow(
            StringBuilder text, List<LevelFixture> sources,
            IReadOnlyList<LevelExpectation> sinks, int vector)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                LevelFixture source = sources[i];
                string bit = vector < source.Stream.Count ? ((int)source.Stream[vector]).ToString() : "?";
                text.Append(Cell(bit));
            }

            text.Append(" |");

            for (int i = 0; i < sinks.Count; i++)
            {
                string values = sinks[i].Values;
                char c = vector < values.Length ? values[vector] : '?';

                // A silent vector is shown as a gap rather than a dash, because a dash next to a
                // column of noughts reads as a minus sign. A don't-care keeps its 'x'.
                text.Append(Cell(c == '-' ? "." : c.ToString()));
            }

            text.Append('\n');   // explicit, so the table reads the same on every platform
        }

        /// <summary>
        /// One column, padded so the table lines up in a monospaced block.
        /// </summary>
        /// <remarks>
        /// Truncated rather than widened for a long name. A fixture called "carry" would otherwise
        /// push every column out and turn the table into a scrolling mess; three characters is enough
        /// to tell "sum" from "cout" at a glance, which is all the header has to do.
        /// </remarks>
        private static string Cell(string content)
        {
            if (content.Length > 3)
                content = content.Substring(0, 3);

            return " " + content.PadLeft(3);
        }
    }
}
