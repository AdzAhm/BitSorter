using System;
using System.Collections.Generic;

namespace BitSorter.SystemVerilog
{
    /// <summary>
    /// SystemVerilog's time units, s to fs: what a delay may be written in, what <c>`timescale</c>
    /// names, and what a project counts its time in (Part 9b-3).
    /// </summary>
    /// <remarks>
    /// **A project's time is a whole number of one unit, and the unit only labels it** (decision D7,
    /// invariant 7): changing a project from ns to ps rescales no number, so a delay of 3 is 3 ps after it,
    /// and a project's files say <c>`timescale 1ns/1ns</c>, their precision their unit. A file read from
    /// elsewhere may name a finer precision, <c>1ns/1ps</c>, and then a tick is that precision and its
    /// delays may be fractions of a unit (Part L7, <see cref="TryTimescale"/>).
    /// A field on screen reads them more loosely, in any case and with a micro sign
    /// (<c>ViewModel.TimeText</c>), since a field is not a source file.
    ///
    /// **A project in ps prints no <c>`timescale</c>**, and one in any other unit prints
    /// <c>`timescale 1ns/1ns</c> at the top of every file (<see cref="Timescale"/>): D7 says export adds
    /// none unless asked, choosing a unit is the asking, and the course's ModelSim runs a file with none
    /// in ps -- every waveform in the course's own simulation reports reads "145 ps" for 145 units
    /// (checked 2026-10-19). So ps is the default, and a waveform here reads as one there does.
    /// </remarks>
    public static class TimeUnits
    {
        /// <summary>What a project counts in unless it says otherwise: ps, as the course's ModelSim does.</summary>
        public const string Default = "ps";

        /// <summary>Every unit, longest first.</summary>
        public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[] { "s", "ms", "us", "ns", "ps", "fs" });

        /// <summary>The units as a list in words, for a message: <c>s, ms, us, ns, ps or fs</c>.</summary>
        public const string Listed = "s, ms, us, ns, ps or fs";

        /// <summary>Whether <paramref name="name"/> is a unit, exactly as SystemVerilog writes it: <c>ns</c>, never <c>NS</c>.</summary>
        public static bool IsUnit(string name)
        {
            if (name == null)
                return false;
            foreach (string unit in All)
                if (string.Equals(unit, name, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>The <c>`timescale</c> a project in <paramref name="unit"/> prints, its precision the unit itself since time is whole: <c>1ps/1ps</c>.</summary>
        public static string Timescale(string unit)
        {
            if (!IsUnit(unit))
                throw new ArgumentException($"'{unit}' is not a time unit: use {Listed}.", nameof(unit));
            return "1" + unit + "/1" + unit;
        }

        /// <summary>
        /// A <c>`timescale</c>'s unit and precision as powers of ten of a second (Part L7): <c>1ns/1ps</c> is -9
        /// and -12, <c>10ns/1ns</c> -8 and -9, <c>100ps / 10fs</c> -10 and -14. False for anything that is not
        /// one, and for a precision coarser than its unit, which the standard does not allow (IEEE 1800, 22.7).
        /// </summary>
        public static bool TryTimescale(string timescale, out int unit, out int precision)
        {
            unit = precision = 0;
            if (timescale == null)
                return false;

            int slash = timescale.IndexOf('/');
            return slash > 0 && TryTime(timescale.Substring(0, slash), out unit) &&
                   TryTime(timescale.Substring(slash + 1), out precision) && precision <= unit;
        }

        /// <summary><c>1ns</c>, <c>10 ps</c>, <c>100us</c>: a power of ten of a second.</summary>
        private static bool TryTime(string text, out int exponent)
        {
            exponent = 0;
            string t = text.Replace(" ", "").Trim();
            int digits = 0;
            while (digits < t.Length && char.IsDigit(t[digits]))
                digits++;

            string magnitude = t.Substring(0, digits), name = t.Substring(digits);
            int extra = magnitude == "1" ? 0 : magnitude == "10" ? 1 : magnitude == "100" ? 2 : -1;
            int place = 0;
            foreach (string unit in All)
            {
                if (string.Equals(unit, name, StringComparison.Ordinal))
                {
                    exponent = -3 * place + extra;
                    return extra >= 0;
                }

                place++;
            }

            return false;
        }

        /// <summary>
        /// The unit a <c>`timescale</c> counts in, when it is one whole unit -- <c>1ns/1ps</c> is ns, as
        /// <c>1 ps / 1 ps</c> is ps. False for one that counts in tens or hundreds of a unit
        /// (<c>10ns/1ns</c>), which no project unit can say, and for anything that is not a timescale.
        /// </summary>
        public static bool TryUnitOf(string timescale, out string unit)
        {
            unit = null;
            if (timescale == null)
                return false;

            int slash = timescale.IndexOf('/');
            string first = (slash < 0 ? timescale : timescale.Substring(0, slash)).Trim();
            if (first.Length < 2 || first[0] != '1')
                return false;

            string name = first.Substring(1).Trim();
            if (!IsUnit(name))
                return false;

            unit = name;
            return true;
        }
    }
}
