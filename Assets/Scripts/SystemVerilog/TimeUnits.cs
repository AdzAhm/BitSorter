using System;
using System.Collections.Generic;

namespace BitSorter.SystemVerilog
{
    /// <summary>
    /// SystemVerilog's time units, s to fs: what a delay may be written in, what <c>`timescale</c>
    /// names, and what a project counts its time in (Part 9b-3).
    /// </summary>
    /// <remarks>
    /// **Time is a whole number of one unit, and the unit only labels it** (decision D7, invariant 7):
    /// changing a project from ns to ps rescales no number, so a delay of 3 is 3 ps after it. That is
    /// why there is no conversion here -- only the names, read exactly as SystemVerilog writes them.
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
