using System;

namespace BitSorter.SystemVerilog.Tests
{
    /// <summary>
    /// Random 4-state values, weighted towards where operators go wrong. Seeded, so a failure is the
    /// same failure on the next run. The oracle project links this file too.
    /// </summary>
    /// <remarks>
    /// Uniform random bits would almost never produce the cases that matter: values that are all x
    /// or all z, a single unknown bit among known ones, a sign bit set, all ones (the carry that runs
    /// the whole width), zero (division), and widths on either side of a 64-bit word.
    /// </remarks>
    internal sealed class RandomValues
    {
        /// <summary>Widths that sit on or beside a word boundary, chosen often.</summary>
        public static readonly int[] EdgeWidths = { 1, 2, 3, 7, 8, 15, 16, 31, 32, 33, 63, 64, 65, 100, 127, 128, 129, 130 };

        private readonly Random _random;

        public RandomValues(int seed) => _random = new Random(seed);

        public int Next(int maxExclusive) => _random.Next(maxExclusive);

        /// <summary>A width: an edge width half the time, anything from 1 to <paramref name="max"/> otherwise.</summary>
        public int Width(int max = 130)
        {
            if (_random.Next(2) == 0)
            {
                int pick = EdgeWidths[_random.Next(EdgeWidths.Length)];
                if (pick <= max)
                    return pick;
            }

            return 1 + _random.Next(max);
        }

        /// <summary>A value of the given width, its signedness chosen by the caller or at random.</summary>
        public LogicVec Value(int width, bool? signed = null)
        {
            bool isSigned = signed ?? _random.Next(2) == 0;
            int kind = _random.Next(20);
            var digits = new char[width];

            for (int i = 0; i < width; i++)
            {
                char c;
                switch (kind)
                {
                    case 0: c = 'x'; break;                                     // all x
                    case 1: c = 'z'; break;                                     // all z
                    case 2: c = '1'; break;                                     // all ones
                    case 3: c = '0'; break;                                     // zero
                    case 4: c = i == 0 ? '1' : '0'; break;                      // just the sign bit
                    case 5: c = _random.Next(width) == 0 ? "xz"[_random.Next(2)] : Bit(); break;  // one unknown, or so
                    case 6: case 7: case 8:
                        c = _random.Next(8) == 0 ? "xz"[_random.Next(2)] : Bit(); break;          // a scatter of unknowns
                    default: c = Bit(); break;                                  // known, at random
                }

                digits[i] = c;
            }

            return LogicVec.FromBinary(new string(digits), isSigned);
        }

        /// <summary>A known value from 0 up to <paramref name="maxInclusive"/>, as a shift count or an index would be.</summary>
        public LogicVec Count(int width, int maxInclusive)
        {
            int value = _random.Next(maxInclusive + 1);
            if (_random.Next(10) == 0)
                return Value(width, false);

            return LogicVec.FromUInt64((ulong)value, width).Resize(width);
        }

        private char Bit() => _random.Next(2) == 0 ? '0' : '1';
    }
}
