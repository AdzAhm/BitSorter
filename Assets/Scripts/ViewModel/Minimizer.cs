using System;
using System.Collections.Generic;
using System.Text;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A product term of a sum of products: the variables <see cref="Mask"/> leaves out are free, and each one it keeps is
    /// the bit of <see cref="Value"/> there -- 1 the variable, 0 its complement. Bit (n-1-i) is variable i, the first variable
    /// the most significant, as the truth table counts its rows.
    /// </summary>
    public readonly struct Implicant : IEquatable<Implicant>
    {
        public Implicant(int value, int mask)
        {
            Value = value & ~mask;
            Mask = mask;
        }

        public int Value { get; }

        /// <summary>The variables left free: a 1 for each.</summary>
        public int Mask { get; }

        /// <summary>Whether row <paramref name="row"/> is one this term covers.</summary>
        public bool Covers(int row) => (row & ~Mask) == Value;

        /// <summary>How many variables it names, of <paramref name="variables"/>.</summary>
        public int Literals(int variables)
        {
            int free = 0;
            for (int m = Mask; m != 0; m &= m - 1)
                free++;
            return variables - free;
        }

        public bool Equals(Implicant other) => Value == other.Value && Mask == other.Mask;
        public override bool Equals(object obj) => obj is Implicant other && Equals(other);
        public override int GetHashCode() => Value * 397 ^ Mask;
    }

    /// <summary>
    /// The LOGIC tab's expressions (Part 15g): an output as a minimal sum of products, by Quine and McCluskey's method -- every
    /// prime implicant, then the fewest of them that cover every row the output is 1 in, and of those the fewest literals. A
    /// row it reads x in is free, as a don't-care is. Written the textbook's way (<c>a'b + ab'</c>) and SystemVerilog's
    /// (<c>(~a &amp; b) | (a &amp; ~b)</c>).
    /// </summary>
    /// <remarks>
    /// **Up to <see cref="MostVariables"/> variables**: the cover is found exactly, by trying the primes for the row with the
    /// fewest that cover it and keeping the best found; a case so tangled that <see cref="MostTried"/> tries do not settle it
    /// keeps the best found so far, which still covers every 1 and no 0, and says so (<see cref="Result.Proven"/>).
    /// </remarks>
    public static class Minimizer
    {
        /// <summary>The most variables an expression is found for: 256 rows.</summary>
        public const int MostVariables = 8;

        /// <summary>The most covers tried before the best found so far is kept.</summary>
        public const int MostTried = 200_000;

        /// <summary>A sum of products, and whether it is shown to be the smallest there is.</summary>
        public sealed class Result
        {
            internal Result(IReadOnlyList<Implicant> terms, bool proven)
            {
                Terms = terms;
                Proven = proven;
            }

            /// <summary>The products, the one covering the lowest row first; none for an output always 0.</summary>
            public IReadOnlyList<Implicant> Terms { get; }

            /// <summary>Whether no cover with fewer products, or as few with fewer literals, exists: false only when the search was cut short.</summary>
            public bool Proven { get; }
        }

        /// <summary>
        /// The smallest sum of products of <paramref name="variables"/> variables that is 1 in every row of <paramref name="values"/>
        /// that is '1', 0 in every one that is '0', and either in one that is 'x'.
        /// </summary>
        public static Result Minimize(int variables, IReadOnlyList<char> values)
        {
            if (variables < 0 || variables > MostVariables)
                throw new ArgumentOutOfRangeException(nameof(variables), variables, "0 to " + MostVariables + " variables.");
            int rows = 1 << variables;
            var ones = new List<int>();
            var cares = new List<int>();
            for (int r = 0; r < rows; r++)
            {
                if (values[r] == '1')
                    ones.Add(r);
                if (values[r] != '0')
                    cares.Add(r);
            }
            if (ones.Count == 0)
                return new Result(Array.Empty<Implicant>(), true);

            List<Implicant> primes = Primes(variables, cares);

            // Only primes that cover a 1 earn a place: one that covers only don't-cares adds nothing.
            primes.RemoveAll(p => !ones.Exists(p.Covers));

            int tried = 0;
            List<Implicant> best = null;
            (int Terms, int Literals) bestCost = (int.MaxValue, int.MaxValue);
            var chosen = new List<Implicant>();

            void Search(List<int> uncovered)
            {
                if (++tried > MostTried)
                    return;
                (int, int) cost = Cost(chosen, variables);
                if (uncovered.Count == 0)
                {
                    if (Less(cost, bestCost))
                    {
                        bestCost = cost;
                        best = new List<Implicant>(chosen);
                    }
                    return;
                }
                if (chosen.Count + 1 > bestCost.Terms)
                    return;   // one more product cannot beat what is had

                // The row with the fewest primes covering it: each of them in turn.
                int row = uncovered[0], fewest = int.MaxValue;
                foreach (int r in uncovered)
                {
                    int count = 0;
                    foreach (Implicant p in primes)
                        if (p.Covers(r))
                            count++;
                    if (count < fewest)
                    {
                        fewest = count;
                        row = r;
                    }
                }

                foreach (Implicant p in primes)
                {
                    if (!p.Covers(row))
                        continue;
                    chosen.Add(p);
                    Search(uncovered.FindAll(r => !p.Covers(r)));
                    chosen.RemoveAt(chosen.Count - 1);
                }
            }

            Search(ones);
            if (best == null)
            {
                // Cut short before any cover was found: one prime for each 1 not covered yet, which covers every 1 and no 0.
                best = new List<Implicant>();
                foreach (int r in ones)
                    if (!best.Exists(t => t.Covers(r)))
                        best.Add(primes.Find(p => p.Covers(r)));
            }
            best.Sort((a, b) => Lowest(a, rows).CompareTo(Lowest(b, rows)));
            return new Result(best, tried <= MostTried);
        }

        /// <summary>Every prime implicant of the rows in <paramref name="cares"/>: each combined with its neighbours until none combine.</summary>
        private static List<Implicant> Primes(int variables, List<int> cares)
        {
            var current = new HashSet<Implicant>();
            foreach (int r in cares)
                current.Add(new Implicant(r, 0));
            var primes = new List<Implicant>();

            while (current.Count > 0)
            {
                var next = new HashSet<Implicant>();
                var combined = new HashSet<Implicant>();
                var list = new List<Implicant>(current);
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        Implicant a = list[i], b = list[j];
                        if (a.Mask != b.Mask)
                            continue;
                        int differ = a.Value ^ b.Value;
                        if (differ == 0 || (differ & (differ - 1)) != 0)
                            continue;   // they must differ in exactly one variable
                        next.Add(new Implicant(a.Value, a.Mask | differ));
                        combined.Add(a);
                        combined.Add(b);
                    }
                foreach (Implicant a in list)
                    if (!combined.Contains(a))
                        primes.Add(a);
                current = next;
            }

            return primes;
        }

        private static (int Terms, int Literals) Cost(List<Implicant> terms, int variables)
        {
            int literals = 0;
            foreach (Implicant t in terms)
                literals += t.Literals(variables);
            return (terms.Count, literals);
        }

        private static bool Less((int Terms, int Literals) a, (int Terms, int Literals) b) =>
            a.Terms != b.Terms ? a.Terms < b.Terms : a.Literals < b.Literals;

        private static int Lowest(Implicant term, int rows)
        {
            for (int r = 0; r < rows; r++)
                if (term.Covers(r))
                    return r;
            return rows;
        }

        // -----------------------------------------------------------------
        // Written out
        // -----------------------------------------------------------------

        /// <summary>
        /// The textbook's way: a product's variables side by side, a complement primed, products joined by +: <c>a'b + ab'</c>;
        /// <c>0</c> for no product, <c>1</c> for one with every variable free.
        /// </summary>
        public static string Textbook(IReadOnlyList<Implicant> terms, IReadOnlyList<string> names)
        {
            if (terms.Count == 0)
                return "0";
            var text = new StringBuilder();
            foreach (Implicant term in terms)
            {
                if (text.Length > 0)
                    text.Append(" + ");
                int before = text.Length;
                for (int i = 0; i < names.Count; i++)
                {
                    int bit = 1 << (names.Count - 1 - i);
                    if ((term.Mask & bit) != 0)
                        continue;
                    text.Append(names[i]);
                    if ((term.Value & bit) == 0)
                        text.Append('\'');
                }
                if (text.Length == before)
                    text.Append('1');
            }
            return text.ToString();
        }

        /// <summary>SystemVerilog's way: <c>(~a &amp; b) | (a &amp; ~b)</c>; <c>1'b0</c> and <c>1'b1</c> for the constants.</summary>
        public static string SystemVerilog(IReadOnlyList<Implicant> terms, IReadOnlyList<string> names)
        {
            if (terms.Count == 0)
                return "1'b0";
            var products = new List<string>(terms.Count);
            foreach (Implicant term in terms)
            {
                var literals = new List<string>();
                for (int i = 0; i < names.Count; i++)
                {
                    int bit = 1 << (names.Count - 1 - i);
                    if ((term.Mask & bit) == 0)
                        literals.Add((term.Value & bit) != 0 ? names[i] : "~" + names[i]);
                }
                if (literals.Count == 0)
                    return "1'b1";
                products.Add(literals.Count == 1 || terms.Count == 1 ? string.Join(" & ", literals) : "(" + string.Join(" & ", literals) + ")");
            }
            return string.Join(" | ", products);
        }
    }
}
