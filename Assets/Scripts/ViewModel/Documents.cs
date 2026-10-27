using System;
using System.Collections.Generic;
using BitSorter.Board;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The student's circuits (Part 15a): each a document the save keeps under its key, the workbench showing one at a time.
    /// MY CIRCUIT is the first, always there, and may be neither renamed nor deleted; a circuit made with NEW, or imported,
    /// is kept under <see cref="Prefix"/> and its name; and each 4.x free-play board, which the save's migration made a
    /// document under <see cref="FreePlayPrefix"/> (Part 7b), is one too. A level's migrated board is not: it is the
    /// exercise of that name's (Part 15d).
    /// </summary>
    public static class Documents
    {
        /// <summary>The start of a circuit's key, its name following.</summary>
        public const string Prefix = "doc/";

        /// <summary>The start of an old free-play board's key, as the save's migration keeps it.</summary>
        public const string FreePlayPrefix = "free-play/";

        /// <summary>What MY CIRCUIT is called, as a name: its key is <see cref="WorkbenchSamples.MyCircuitKey"/>.</summary>
        public const string MyCircuitName = "my circuit";

        /// <summary>The longest name a circuit may have.</summary>
        public const int MostCharacters = 32;

        /// <summary>How many circuits the recent list remembers.</summary>
        public const int MostRecent = 16;

        /// <summary>Whether <paramref name="key"/> is a circuit's: MY CIRCUIT, one made or imported, or an old free-play board.</summary>
        public static bool IsDocument(string key) =>
            key == WorkbenchSamples.MyCircuitKey || IsNamed(key, Prefix) || IsNamed(key, FreePlayPrefix);

        /// <summary>Whether the circuit under <paramref name="key"/> may be renamed and deleted: every one but MY CIRCUIT.</summary>
        public static bool IsOwn(string key) => IsDocument(key) && key != WorkbenchSamples.MyCircuitKey;

        /// <summary>What the circuit under <paramref name="key"/> is called.</summary>
        public static string NameOf(string key)
        {
            if (key == WorkbenchSamples.MyCircuitKey)
                return MyCircuitName;
            if (IsNamed(key, Prefix))
                return key.Substring(Prefix.Length);
            if (IsNamed(key, FreePlayPrefix))
                return key.Substring(FreePlayPrefix.Length);
            return key ?? "";
        }

        /// <summary>The key a circuit called <paramref name="name"/> is kept under.</summary>
        public static string KeyOf(string name) => Prefix + (name ?? "").Trim();

        /// <summary>
        /// Why a circuit may not be called <paramref name="name"/>, or null when it may: it needs a name, of at most
        /// <see cref="MostCharacters"/> letters, digits, spaces, dashes, underscores and dots, that no circuit in
        /// <paramref name="keys"/> has -- by any case, MY CIRCUIT's included -- but <paramref name="except"/>, the one renamed.
        /// </summary>
        public static string WhyNotName(string name, IEnumerable<string> keys, string except = null)
        {
            string wanted = (name ?? "").Trim();
            if (wanted.Length == 0)
                return "A circuit needs a name.";
            if (wanted.Length > MostCharacters)
                return $"{wanted.Substring(0, MostCharacters)}... is too long: a circuit's name is at most {MostCharacters} characters.";
            foreach (char c in wanted)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.'))
                    return $"A circuit's name is letters, digits, spaces, dashes, underscores and dots: not '{c}'.";
            foreach (string key in keys ?? Array.Empty<string>())
                if (IsDocument(key) && key != except && string.Equals(NameOf(key), wanted, StringComparison.OrdinalIgnoreCase))
                    return $"There is a circuit called {NameOf(key)} already.";
            return null;
        }

        /// <summary>
        /// A name no circuit in <paramref name="keys"/> has. With no <paramref name="wanted"/>, a new circuit's: circuit1,
        /// circuit2... With one -- an imported project's -- that name as a circuit's may be, or it with " 2", " 3"... after it.
        /// </summary>
        public static string FreeName(IEnumerable<string> keys, string wanted = null)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in keys ?? Array.Empty<string>())
                if (IsDocument(key))
                    taken.Add(NameOf(key));

            string clean = Clean(wanted);
            if (clean.Length == 0)
            {
                for (int n = 1; ; n++)
                    if (!taken.Contains("circuit" + Number(n)))
                        return "circuit" + Number(n);
            }

            if (!taken.Contains(clean))
                return clean;
            for (int n = 2; ; n++)
            {
                string suffix = " " + Number(n);
                string made = (clean.Length + suffix.Length > MostCharacters ? clean.Substring(0, MostCharacters - suffix.Length).TrimEnd() : clean) + suffix;
                if (!taken.Contains(made))
                    return made;
            }
        }

        private static string Number(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The circuits among <paramref name="keys"/>, the most recently opened first as <paramref name="recent"/> has them,
        /// then the rest in the order they were kept.
        /// </summary>
        public static IReadOnlyList<string> InOrder(IEnumerable<string> keys, IReadOnlyList<string> recent)
        {
            var held = new List<string>();
            foreach (string key in keys ?? Array.Empty<string>())
                if (IsDocument(key) && !held.Contains(key))
                    held.Add(key);

            var ordered = new List<string>(held.Count);
            foreach (string key in recent ?? Array.Empty<string>())
                if (held.Contains(key) && !ordered.Contains(key))
                    ordered.Add(key);
            foreach (string key in held)
                if (!ordered.Contains(key))
                    ordered.Add(key);
            return ordered;
        }

        /// <summary><paramref name="recent"/> with <paramref name="key"/> at its front, at most <see cref="MostRecent"/> long.</summary>
        public static IReadOnlyList<string> Touched(IReadOnlyList<string> recent, string key)
        {
            var made = new List<string> { key };
            foreach (string each in recent ?? Array.Empty<string>())
                if (each != key && made.Count < MostRecent)
                    made.Add(each);
            return made;
        }

        /// <summary>The top module a new circuit called <paramref name="name"/> starts with: the name as SystemVerilog takes one.</summary>
        public static string ModuleNameFor(string name) => Migrator.NameFrom(name, "circuit");

        private static bool IsNamed(string key, string prefix) =>
            key != null && key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal);

        /// <summary><paramref name="text"/> with whatever a circuit's name may not hold left out, and cut to length.</summary>
        private static string Clean(string text)
        {
            var made = new System.Text.StringBuilder();
            foreach (char c in text ?? "")
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.')
                    made.Append(c);
            string clean = made.ToString().Trim();
            return clean.Length > MostCharacters ? clean.Substring(0, MostCharacters).Trim() : clean;
        }
    }
}
