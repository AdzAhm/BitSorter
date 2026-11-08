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

        /// <summary>The most characters of a name's end <see cref="Tail"/> keeps: more than any room on screen holds.</summary>
        public const int MostTail = 1024;

        /// <summary>
        /// <paramref name="text"/> cut from its start behind "...", until it is no wider than <paramref name="room"/> by
        /// <paramref name="width"/>: whole where it fits, and as it is where not even its last character fits behind "..." --
        /// a wave row's name, whose end tells it from the rest.
        /// </summary>
        /// <remarks>
        /// **The longest end that fits is found by halves** (browser-4), a dozen measures of at most <see cref="MostTail"/>
        /// characters each: a character cut at a time, each what was left laid out whole, a 5,000-letter port name was twelve
        /// million characters in one frame, and a browser's page ran out of memory on a longer one before it could end. An
        /// end grows no narrower as it takes a character more, so the answer is the one a character at a time finds; and a
        /// text longer than <see cref="MostTail"/> is wider than any room, at any size BitSorter writes in, so it is never
        /// measured whole.
        /// </remarks>
        public static string Tail(string text, float room, Func<string, float> width)
        {
            if (width == null)
                throw new ArgumentNullException(nameof(width));
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            if (text.Length <= MostTail && width(text) <= room)
                return text;

            // An end of `fits` characters fits behind "...", and one of `fails` does not: closed in on by halves.
            int fits = 0, fails = Math.Min(text.Length - 1, MostTail) + 1;
            while (fails - fits > 1)
            {
                int middle = fits + (fails - fits) / 2;
                if (width("..." + text.Substring(text.Length - middle)) <= room)
                    fits = middle;
                else
                    fails = middle;
            }

            return fits == 0 ? text : "..." + text.Substring(text.Length - fits);
        }
    }
}
