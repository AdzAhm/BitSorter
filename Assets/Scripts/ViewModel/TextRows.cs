using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A long line in rows that each fit their room (Part 15h-4d): cut after one of the given marks -- an expression's
    /// <c> + </c> between its products, its <c> | </c> -- so a term is never split; a piece too long for a row of its own
    /// cut at its spaces; a word too long for a row on one by itself. LOGIC's expressions ended in "..." at the panel's edge,
    /// and a 2-bit ALU's could not be read (Ahmad, 2026-10-29).
    /// </summary>
    public static class TextRows
    {
        /// <summary>The rows of <paramref name="text"/>, each no wider than <paramref name="room"/> by <paramref name="width"/>, where it can be.</summary>
        public static List<string> Of(string text, float room, Func<string, float> width, params string[] marks)
        {
            if (width == null)
                throw new ArgumentNullException(nameof(width));
            var rows = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                rows.Add(text ?? "");
                return rows;
            }

            // The pieces: up to and with each mark.
            var pieces = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
                foreach (string mark in marks)
                    if (mark.Length > 0 && string.CompareOrdinal(text, i, mark, 0, mark.Length) == 0)
                    {
                        pieces.Add(text.Substring(start, i + mark.Length - start));
                        start = i + mark.Length;
                        i += mark.Length - 1;
                        break;
                    }
            if (start < text.Length)
                pieces.Add(text.Substring(start));

            string row = "";
            foreach (string piece in pieces)
            {
                if (width((row + piece).TrimEnd()) <= room)
                {
                    row += piece;
                    continue;
                }
                if (row.Length > 0)
                    rows.Add(row.TrimEnd());
                row = "";
                if (width(piece.TrimEnd()) <= room)
                {
                    row = piece;
                    continue;
                }

                // A piece too long for a row: its words, as many to a row as fit.
                foreach (string word in piece.Split(' '))
                {
                    if (word.Length == 0)
                        continue;
                    string longer = row.Length == 0 ? word : row + " " + word;
                    if (row.Length == 0 || width(longer) <= room)
                    {
                        row = longer;
                        continue;
                    }
                    rows.Add(row);
                    row = word;
                }
                row += " ";
            }
            if (row.Trim().Length > 0)
                rows.Add(row.TrimEnd());
            return rows;
        }
    }
}
