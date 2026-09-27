using System;
using System.Collections.Generic;

namespace BitSorter.View
{
    /// <summary>
    /// What a free-play board may be called, and what it is called when the player has not said.
    /// </summary>
    /// <remarks>
    /// Pure rules over a list of names, so the store, the naming panel and the tests all ask the
    /// same question. Names are compared ignoring case, because two boards called "Adder" and
    /// "adder" are one name to anybody reading the list.
    /// </remarks>
    public static class BoardNames
    {
        /// <summary>The most boards free play keeps.</summary>
        /// <remarks>
        /// A cap rather than a list that grows, because the list is stepped through with two arrows
        /// on a docked panel, not scrolled: eight is as many as anyone steps through to find one.
        /// </remarks>
        public const int MaxBoards = 8;

        /// <summary>The longest name, in characters, after trimming.</summary>
        /// <remarks>What fits between the two arrows on the setup panel at its type size.</remarks>
        public const int MaxLength = 24;

        /// <summary>What the first board is called: the one every earlier version kept.</summary>
        public const string First = "My board";

        /// <summary>A name with its outer spaces removed; null reads as empty.</summary>
        public static string Clean(string name) => (name ?? string.Empty).Trim();

        /// <summary>
        /// Why a name cannot be used, or null if it can.
        /// </summary>
        /// <param name="name">The name as typed; it is cleaned first.</param>
        /// <param name="others">The other boards' names -- not the one being renamed.</param>
        public static string Refusal(string name, IReadOnlyList<string> others)
        {
            string clean = Clean(name);

            if (clean.Length == 0)
                return "A board needs a name.";

            if (clean.Length > MaxLength)
                return $"Names are {MaxLength} characters at most.";

            if (Taken(clean, others))
                return "Another board already has that name.";

            return null;
        }

        /// <summary>"Board 2", "Board 3" and so on: the first of those not already taken.</summary>
        /// <remarks>
        /// From 2, because the first board is <see cref="First"/>, so a new one is the second. A gap
        /// left by a deleted board is filled rather than counted past.
        /// </remarks>
        public static string NextDefault(IReadOnlyList<string> names)
        {
            for (int number = 2; ; number++)
            {
                string candidate = "Board " + number;

                if (!Taken(candidate, names))
                    return candidate;
            }
        }

        /// <summary>
        /// "Adder copy", or "Adder copy 2" if that is taken, shortened to fit <see cref="MaxLength"/>.
        /// </summary>
        /// <remarks>
        /// The original's name is what gives way when the whole will not fit, never the suffix: a
        /// copy that did not say it was one would read as the board it was taken from.
        /// </remarks>
        public static string CopyOf(string name, IReadOnlyList<string> names)
        {
            string original = Clean(name);

            for (int number = 1; ; number++)
            {
                string suffix = number == 1 ? " copy" : " copy " + number;
                int room = MaxLength - suffix.Length;
                string stem = original.Length > room ? original.Substring(0, room).TrimEnd() : original;
                string candidate = stem + suffix;

                if (!Taken(candidate, names))
                    return candidate;
            }
        }

        private static bool Taken(string name, IReadOnlyList<string> names)
        {
            if (names == null)
                return false;

            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(Clean(names[i]), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
