using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// An output's Karnaugh map (Part 15g): its truth table laid out for grouping by eye, rows and columns in Gray order so
    /// that neighbours differ in one variable, x where the output reads x. Up to <see cref="MostVariables"/> variables: the
    /// first half of them across the top, the rest down the side, as the course draws it -- ab across and cd down, ab across
    /// and c down (it was the other way until the course's lectures and tutorials were read, 2026-11-02). **No groups are
    /// drawn**: the expression is beside it (<see cref="Minimizer"/>), and finding the groups is the exercise.
    /// </summary>
    public sealed class KarnaughLayout
    {
        /// <summary>The most variables a map is drawn for.</summary>
        public const int MostVariables = 4;

        private KarnaughLayout(string down, string across, string[] rowLabels, string[] columnLabels, char[,] cells)
        {
            Down = down;
            Across = across;
            RowLabels = rowLabels;
            ColumnLabels = columnLabels;
            Cells = cells;
        }

        /// <summary>The variables down the side, as one label: <c>cd</c>; empty for a map of one row.</summary>
        public string Down { get; }

        /// <summary>The variables across the top: <c>ab</c>.</summary>
        public string Across { get; }

        /// <summary>Each row's values of <see cref="Down"/>, in Gray order: 00, 01, 11, 10.</summary>
        public IReadOnlyList<string> RowLabels { get; }

        public IReadOnlyList<string> ColumnLabels { get; }

        /// <summary>The value in each cell, [row, column]: '0', '1' or 'x'.</summary>
        public char[,] Cells { get; }

        /// <summary>
        /// The map of <paramref name="values"/> -- one per truth-table row, the first of <paramref name="names"/> the most
        /// significant -- or null past <see cref="MostVariables"/> variables, or for none.
        /// </summary>
        public static KarnaughLayout Of(IReadOnlyList<string> names, IReadOnlyList<char> values)
        {
            int n = names.Count;
            if (n < 1 || n > MostVariables)
                return null;

            // The first variables across, the more of them when they are odd: a cell's row of the table is its column's bits,
            // then its row's.
            int across = (n + 1) / 2, down = n - across;
            string[] rows = Gray(down), columns = Gray(across);
            var cells = new char[rows.Length, columns.Length];
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < columns.Length; c++)
                {
                    int row = (Bits(columns[c]) << down) | Bits(rows[r]);
                    cells[r, c] = values[row];
                }

            return new KarnaughLayout(Join(names, across, down), Join(names, 0, across), rows, columns, cells);
        }

        /// <summary>The <paramref name="bits"/>-bit Gray code, as text: 0 bits is one empty label.</summary>
        private static string[] Gray(int bits)
        {
            var codes = new string[1 << bits];
            for (int g = 0; g < codes.Length; g++)
            {
                int code = g ^ (g >> 1);
                var text = new char[bits];
                for (int b = 0; b < bits; b++)
                    text[b] = (code >> (bits - 1 - b) & 1) != 0 ? '1' : '0';
                codes[g] = new string(text);
            }
            return codes;
        }

        private static int Bits(string code)
        {
            int value = 0;
            foreach (char c in code)
                value = value << 1 | (c == '1' ? 1 : 0);
            return value;
        }

        private static string Join(IReadOnlyList<string> names, int from, int count)
        {
            var parts = new List<string>(count);
            for (int i = from; i < from + count; i++)
                parts.Add(names[i]);
            return string.Join(count > 1 && Array.Exists(parts.ToArray(), p => p.Length > 1) ? " " : "", parts);
        }
    }
}
