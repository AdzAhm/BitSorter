using System;
using System.Globalization;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Time as the workbench writes it and reads it back: the axis's tick labels and the playhead's
    /// readout, written into a buffer the view keeps, and a time typed by the player turned into whole
    /// project units.
    /// </summary>
    /// <remarks>
    /// **The same in every culture**, always: a point before the fraction and a hyphen for a minus, in
    /// Haifa and in Berlin alike. The axis is in the course's notation, not the machine's, and a test's
    /// expected string has to be what the screen shows wherever the test runs. Nothing here reads the
    /// current culture -- the digits are written and read by hand -- so no setting can reach it.
    ///
    /// **Writing allocates nothing.** The axis relabels on every frame of a zoom or a pan, and the
    /// playhead's readout on every frame of playback, so <see cref="Write"/> fills a <c>char[]</c> the
    /// view keeps and hands TextMeshPro the characters (<c>SetCharArray</c>), as the frame-rate counter
    /// does. The workbench's first waveform made a string for every tenth of a unit of the run up front,
    /// which held only while the run was short: INPUTS lets a run's END go to 10,000,000, two hundred
    /// million strings in one frame (Part 16d). The playhead's readout is now characters too, and the
    /// one over the slider, a UI Toolkit label that takes only a string, a string made as the moment
    /// shown first reaches a tenth and kept for it (<see cref="Tenths"/>, <see cref="MomentWords"/>),
    /// never ahead of it.
    ///
    /// **Time is an integer** (invariant 7): the engine counts whole project units, and the unit's name
    /// is a label. A time on screen may still have a fraction -- the playhead moves smoothly between
    /// steps, and a zoomed axis ticks every half or quarter unit -- so <see cref="Write"/> takes a
    /// double and as many decimals as the tick step needs (<see cref="Decimals"/>). A time typed in
    /// becomes a step of the run, so <see cref="TryParse"/> refuses one that is not a whole number of
    /// project units, and says what it would have been, rather than rounding to a time nobody typed.
    /// </remarks>
    public static class TimeText
    {
        /// <summary>The most decimals a time is written with: a billionth of a unit, past any zoom the waveform allows.</summary>
        public const int MaxDecimals = 9;

        /// <summary>Digits in the largest whole number of units a time can have, <see cref="long.MaxValue"/>.</summary>
        private const int MostWholeDigits = 19;

        /// <summary>A time's whole part must fit in a <c>long</c>; past 2^63, which is a little over this, it cannot.</summary>
        private const double LargestMagnitude = 9.2e18;

        /// <summary>Powers of ten, 10^0 to 10^18: every one a <c>long</c> holds.</summary>
        private static readonly long[] PowersOfTen =
        {
            1L, 10L, 100L, 1_000L, 10_000L, 100_000L, 1_000_000L, 10_000_000L, 100_000_000L,
            1_000_000_000L, 10_000_000_000L, 100_000_000_000L, 1_000_000_000_000L, 10_000_000_000_000L,
            100_000_000_000_000L, 1_000_000_000_000_000L, 10_000_000_000_000_000L,
            100_000_000_000_000_000L, 1_000_000_000_000_000_000L,
        };

        /// <summary>
        /// The longest text <see cref="Write"/> can make with <paramref name="decimals"/> and
        /// <paramref name="unit"/>: a buffer this long is never too short, so the view sizes it once.
        /// </summary>
        public static int MaxLength(int decimals, string unit)
        {
            CheckDecimals(decimals);
            return 1 + MostWholeDigits + (decimals > 0 ? 1 + decimals : 0) + (string.IsNullOrEmpty(unit) ? 0 : 1 + unit.Length);
        }

        /// <summary>
        /// Writes <paramref name="time"/> with <paramref name="decimals"/> decimals and then
        /// <paramref name="unit"/> -- <c>12.5 ns</c>, <c>0 ns</c>, <c>3.25</c> with no unit -- into the
        /// start of <paramref name="into"/>, and returns how many characters it wrote. Allocates nothing.
        /// </summary>
        /// <remarks>
        /// Every decimal asked for is written, trailing zeros included, so the labels of one axis line
        /// up: <c>12.50</c> beside <c>12.75</c>. The last one is rounded half away from zero, and a time
        /// that rounds to zero is <c>0</c>, never <c>-0</c>.
        /// </remarks>
        public static int Write(double time, int decimals, string unit, char[] into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));
            if (double.IsNaN(time) || double.IsInfinity(time))
                throw new ArgumentOutOfRangeException(nameof(time), time, "A time on screen is a finite number.");
            CheckDecimals(decimals);

            double magnitude = Math.Abs(time);
            if (magnitude >= LargestMagnitude)
                throw new ArgumentOutOfRangeException(nameof(time), time, "A time's whole part has to fit in a long, as the engine's time does.");

            // The whole part and the fraction apart: subtracting a double's own floor is exact, so the
            // only rounding is the fraction's, and the whole part keeps every digit a long has.
            long whole = (long)Math.Floor(magnitude);
            long scale = PowersOfTen[decimals];
            long fraction = (long)Math.Round((magnitude - whole) * scale, MidpointRounding.AwayFromZero);
            if (fraction >= scale)
            {
                whole++;
                fraction -= scale;
            }

            bool negative = time < 0 && (whole != 0 || fraction != 0);
            int wholeDigits = DigitsOf(whole);
            int unitLength = string.IsNullOrEmpty(unit) ? 0 : unit.Length;
            int length = (negative ? 1 : 0) + wholeDigits + (decimals > 0 ? 1 + decimals : 0) + (unitLength > 0 ? 1 + unitLength : 0);

            if (into.Length < length)
                throw new ArgumentException($"The buffer holds {into.Length} characters and this time needs {length}: size it with MaxLength.", nameof(into));

            int at = 0;
            if (negative)
                into[at++] = '-';

            at += wholeDigits;
            for (int i = at - 1; i >= at - wholeDigits; i--)
            {
                into[i] = (char)('0' + (int)(whole % 10));
                whole /= 10;
            }

            if (decimals > 0)
            {
                into[at++] = '.';
                at += decimals;
                for (int i = at - 1; i >= at - decimals; i--)
                {
                    into[i] = (char)('0' + (int)(fraction % 10));
                    fraction /= 10;
                }
            }

            if (unitLength > 0)
            {
                into[at++] = ' ';
                unit.CopyTo(0, into, at, unitLength);
                at += unitLength;
            }

            return at;
        }

        /// <summary>
        /// The tenth of a unit <paramref name="time"/> is shown at, rounded half to even, from 0 to the run's
        /// <paramref name="end"/>: what the readouts write, <c>Tenths(12.5, 100) / 10.0</c> being 12.5.
        /// </summary>
        /// <remarks>
        /// A <c>long</c>, worked out in doubles: a run may be longer than an <c>int</c> counts in tenths -- RUN TO
        /// <c>1 ms</c> in a ps project is ten billion of them, which an <c>int</c> wrapped -- and a <c>float</c>
        /// rounds away the tenths of any time past about 1.6 million units. A time before the start, or none, is 0.
        /// </remarks>
        public static long Tenths(double time, long end)
        {
            long last = end <= 0 ? 0 : end > long.MaxValue / 10 ? long.MaxValue / 10 * 10 : end * 10;
            double scaled = Math.Round(time * 10.0);
            if (!(scaled > 0))
                return 0;
            return scaled >= last ? last : (long)scaled;
        }

        /// <summary>
        /// How many decimals the labels of an axis ticking every <paramref name="tickStep"/> units need
        /// to tell its ticks apart: 1 needs none, 0.5 one, 0.25 two, 0.1 one.
        /// </summary>
        /// <remarks>
        /// The fewest decimals at which the step is a whole number, allowing for a step that is not
        /// exactly representable -- 0.1 is not, and 0.3 times ten is 3.0000000000000004 -- by a
        /// tolerance of a billionth of the step. A step no number of decimals makes whole, a third, gets
        /// <see cref="MaxDecimals"/>; an axis that chooses its steps from 1, 2 and 5 never asks.
        /// </remarks>
        public static int Decimals(double tickStep)
        {
            if (!(tickStep > 0) || double.IsInfinity(tickStep))
                throw new ArgumentOutOfRangeException(nameof(tickStep), tickStep, "A tick step is a positive number of units.");

            for (int d = 0; d < MaxDecimals; d++)
            {
                double scaled = tickStep * PowersOfTen[d];
                double nearest = Math.Round(scaled);
                if (nearest >= 1 && Math.Abs(scaled - nearest) <= scaled * 1e-9)
                    return d;
            }

            return MaxDecimals;
        }

        /// <summary>
        /// Reads a time the player typed -- <c>50</c>, <c>50ns</c>, <c>50 ns</c>, <c>1.5 us</c>,
        /// <c>2ps</c> -- as a whole number of <paramref name="projectUnit"/>s. False, with
        /// <paramref name="why"/> saying what was wrong and how to write it, for a time that is not a
        /// whole number of project units, a negative one, a unit that is not one, or anything else.
        /// </summary>
        /// <remarks>
        /// A number with no unit is in project units. The units are SystemVerilog's own -- s, ms, us,
        /// ns, ps, fs -- with the u of us also taken as a micro sign or a Greek mu, and in any case,
        /// since a field on screen is not a source file and <c>50 NS</c> means nothing else. A number may have a fraction and
        /// SystemVerilog's underscores (<c>1_000</c>). Only the point is a decimal point, in every
        /// culture: <c>1,5</c> is not a time. On success <paramref name="why"/> is null.
        ///
        /// **Exact, never through a double.** The number is kept as its significant digits and a power
        /// of ten, and a change of unit only moves the power: <c>1.005 us</c> is 1005 ns, where 1.005
        /// times 1000 in doubles is 1004.9999999999999 and would be refused as not whole. Whole means the
        /// power comes out at zero or more once the digits' trailing zeros are counted into it, so a
        /// fraction of any length is judged rightly -- <c>1.000000000000000000001</c> is not whole, not
        /// too large -- and only a whole number past <see cref="long.MaxValue"/> is too large.
        ///
        /// A project whose unit is not a time unit -- the setting is a label, and could say anything --
        /// reads a plain number, or a number with that label, and refuses any other unit, since nothing
        /// converts to it.
        /// </remarks>
        public static bool TryParse(string text, string projectUnit, out long time, out string why)
        {
            time = 0;

            if (text == null || IsBlank(text))
            {
                why = "Write a time: 50, 50 ns or 1.5 us.";
                return false;
            }

            string shown = text.Trim();
            int n = text.Length;
            int i = SkipSpace(text, 0);

            bool negative = false;
            if (text[i] == '-')
            {
                negative = true;
                i++;
            }

            // The number: every digit in order, and how many came after the point.
            var digits = new char[n];
            int count = 0, fractionDigits = 0;
            bool point = false;

            for (; i < n; i++)
            {
                char c = text[i];

                if (c >= '0' && c <= '9')
                {
                    digits[count++] = c;
                    if (point)
                        fractionDigits++;
                }
                else if (c == '_' && count > 0)
                {
                    // SystemVerilog's digit separator.
                }
                else if (c == '.' && !point)
                {
                    point = true;
                }
                else
                {
                    break;
                }
            }

            int unitStart = SkipSpace(text, i);
            int unitEnd = unitStart;
            while (unitEnd < n && char.IsLetter(text[unitEnd]))
                unitEnd++;

            if (count == 0 || SkipSpace(text, unitEnd) != n)
            {
                why = $"'{shown}' is not a time. Write a number, with a unit or without: 50, 50 ns, 1.5 us.";
                return false;
            }

            int unitLength = unitEnd - unitStart;
            bool projectKnown = TryExponent(projectUnit, 0, projectUnit == null ? 0 : projectUnit.Length, out int projectExponent);
            int givenExponent = projectExponent;

            if (unitLength > 0 && !TryExponent(text, unitStart, unitLength, out givenExponent))
            {
                bool itsOwnName = !projectKnown && projectUnit != null && unitLength == projectUnit.Length &&
                    string.Compare(text, unitStart, projectUnit, 0, unitLength, StringComparison.OrdinalIgnoreCase) == 0;

                if (!itsOwnName)
                {
                    why = $"'{text.Substring(unitStart, unitLength)}' is not a time unit. Use s, ms, us, ns, ps or fs.";
                    return false;
                }

                givenExponent = projectExponent;
            }
            else if (unitLength > 0 && !projectKnown)
            {
                why = string.IsNullOrEmpty(projectUnit)
                    ? "This project's time has no unit to convert to: write the time as a plain number."
                    : $"This project counts time in '{projectUnit}', which no time unit converts to: write the time as a plain number.";
                return false;
            }

            // The significant digits, first to last, and the power of ten they are multiplied by.
            int first = 0, last = count - 1;
            while (first < count && digits[first] == '0')
                first++;

            if (first == count)
            {
                why = null;
                return true;
            }

            if (negative)
            {
                why = $"'{shown}' is before the run starts: a time is 0 or later.";
                return false;
            }

            int power = givenExponent - projectExponent - fractionDigits;
            while (digits[last] == '0')
            {
                last--;
                power++;
            }

            string unitName = string.IsNullOrEmpty(projectUnit) ? "units" : projectUnit;
            int significant = last - first + 1;

            if (power < 0)
            {
                why = $"'{shown}' is {WithPoint(digits, first, significant, -power)} {unitName}, and time here is a whole number of {unitName}.";
                return false;
            }

            long value = 0;
            bool fits = significant + power <= MostWholeDigits;
            for (int d = first; fits && d <= last; d++)
                fits = TryAppendDigit(ref value, digits[d] - '0');
            for (int z = 0; fits && z < power; z++)
                fits = TryAppendDigit(ref value, 0);

            if (!fits)
            {
                why = $"'{shown}' is later than any run can reach: a time is at most {long.MaxValue.ToString(CultureInfo.InvariantCulture)} {unitName}.";
                return false;
            }

            time = value;
            why = null;
            return true;
        }

        // -----------------------------------------------------------------
        // Pieces
        // -----------------------------------------------------------------

        private static void CheckDecimals(int decimals)
        {
            if (decimals < 0 || decimals > MaxDecimals)
                throw new ArgumentOutOfRangeException(nameof(decimals), decimals, $"A time is written with 0 to {MaxDecimals} decimals.");
        }

        /// <summary>How many digits a number of 0 or more is written with: 0 is one.</summary>
        private static int DigitsOf(long value)
        {
            int count = 1;
            while (value >= 10)
            {
                value /= 10;
                count++;
            }

            return count;
        }

        /// <summary>value * 10 + digit, if that still fits in a long.</summary>
        private static bool TryAppendDigit(ref long value, int digit)
        {
            if (value > (long.MaxValue - digit) / 10)
                return false;

            value = value * 10 + digit;
            return true;
        }

        /// <summary>
        /// A unit's power of ten, seconds being 0: <c>ns</c> is -9. Read in place from
        /// <paramref name="text"/>, in any case; the u of <c>us</c> may be the micro sign or the Greek mu.
        /// </summary>
        private static bool TryExponent(string text, int start, int length, out int exponent)
        {
            exponent = 0;
            if (text == null || length < 1 || length > 2)
                return false;

            if (char.ToLowerInvariant(text[start + length - 1]) != 's')
                return false;

            if (length == 1)
                return true;

            switch (char.ToLowerInvariant(text[start]))
            {
                case 'm': exponent = -3; return true;
                case 'u': case '\u00b5': case '\u03bc': exponent = -6; return true;
                case 'n': exponent = -9; return true;
                case 'p': exponent = -12; return true;
                case 'f': exponent = -15; return true;
                default: exponent = 0; return false;
            }
        }

        /// <summary>
        /// <paramref name="length"/> digits from <paramref name="start"/>, divided by
        /// 10^<paramref name="places"/> and written out with a point: for a refusal's message, which
        /// says what the typed time comes to.
        /// </summary>
        private static string WithPoint(char[] digits, int start, int length, int places)
        {
            var written = new string(digits, start, length);
            if (length <= places)
                written = new string('0', places - length + 1) + written;

            return written.Substring(0, written.Length - places) + "." + written.Substring(written.Length - places);
        }

        private static int SkipSpace(string text, int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            return i;
        }

        private static bool IsBlank(string text) => SkipSpace(text, 0) == text.Length;
    }

    /// <summary>
    /// The moment's words for the readouts: <c>t = 12.5 ns</c> over the slider, and <c>12.5 ns</c> at the playhead -- the same
    /// characters past <see cref="Prefix"/> -- for the tenth of a unit shown (<see cref="TimeText.Tenths"/>), written as the
    /// moment moves to another tenth and kept while it stands (Part 16d's sweep).
    /// </summary>
    /// <remarks>
    /// **One string for a tenth not shown before; none while the moment stands, none for a tenth shown before, none ahead.**
    /// The workbench wrote both readouts for every tenth of the run up front, whenever a run opened, was replayed or grew: a
    /// frame of playback then formatted nothing, but a run of 100,000 units made some 150 MB in one frame, and INPUTS lets END
    /// go to 10,000,000. Written as the moment was shown instead, every frame that reached another tenth made two strings,
    /// a tenth shown a moment before included -- which broke the zero-GC pass's word that a frame playing allocates nothing
    /// once warm (the review of the sweep). Now:
    ///
    /// - The playhead's words are not a string at all: <see cref="Characters"/> from <see cref="PlayheadStart"/>, handed to
    ///   TextMeshPro as the axis's labels are (<c>SetCharArray</c>), again only when <see cref="Version"/> moves.
    /// - The slider's readout is a UI Toolkit label, which takes only a string, so a tenth not shown before makes that one
    ///   string -- at most one a frame, 56 bytes for a run as long as INPUTS can make -- and it is kept, so a tenth shown
    ///   before makes none: the words of the last <see cref="Kept"/> tenths written, by tenth, while the unit is the same.
    ///   That is 1,638.4 units of run, longer than any sample's (the multicycle processor's, the longest, is 1,350 units) or
    ///   exercise's (look-ahead's, 832), so playing one again, dragging the playhead back over it or making it again after
    ///   an edit makes nothing; a longer run makes a tenth's string again once it has gone that far past it. What is kept
    ///   holds about 1 MB at most, 256 KB of it the two arrays made with the panel.
    ///
    /// <c>FrameAllocationTests</c> holds a tenth shown before and a moment standing to 0 bytes, and <c>TextTests</c> a tenth
    /// not shown before to its one string.
    /// </remarks>
    public sealed class MomentWords
    {
        /// <summary>What the slider's readout says before the moment's time.</summary>
        public const string Prefix = "t = ";

        /// <summary>How many tenths' words are kept, the latest written of each place in the ring: 1,638.4 units of run.</summary>
        public const int Kept = 16384;

        /// <summary>The words kept, each at its tenth's place in the ring, and which tenth each is for (-1 for none).</summary>
        private readonly string[] _kept = new string[Kept];

        private readonly long[] _keptStep = new long[Kept];

        /// <summary>The unit the words kept are in: another unit, and none of them is.</summary>
        private string _keptUnit;

        private char[] _chars = new char[0], _time = new char[0];
        private string _unit;

        public MomentWords()
        {
            for (int i = 0; i < Kept; i++)
                _keptStep[i] = -1;
        }

        /// <summary>The tenth of a unit the words are for; -1 before any is written, or once forgotten.</summary>
        public long Step { get; private set; } = -1;

        /// <summary>The moment over the slider, <c>t = 12.5 ns</c>; null before any is written.</summary>
        public string Moment { get; private set; }

        /// <summary>
        /// The moment's characters, <see cref="Moment"/>'s own from the start: the playhead's words are
        /// <see cref="PlayheadLength"/> of them from <see cref="PlayheadStart"/>. Written over as the moment moves.
        /// </summary>
        public char[] Characters => _chars;

        /// <summary>Where in <see cref="Characters"/> the playhead's words start: past <see cref="Prefix"/>.</summary>
        public int PlayheadStart => Prefix.Length;

        /// <summary>How many characters the playhead's words are, <c>12.5 ns</c> being 7; 0 before any is written.</summary>
        public int PlayheadLength { get; private set; }

        /// <summary>Changes each time the words are written: what the view hands TextMeshPro the playhead's characters on.</summary>
        public int Version { get; private set; }

        /// <summary>
        /// The words for tenth <paramref name="step"/>, 0 or later, in <paramref name="unit"/>, unless they are already its: the
        /// words kept for it if there are any, else written and kept.
        /// </summary>
        public void Write(long step, string unit)
        {
            if (step < 0)
                throw new ArgumentOutOfRangeException(nameof(step), step, "A tenth of the run is 0 or later, as TimeText.Tenths gives it.");
            if (step == Step && Moment != null && unit == _unit)
                return;

            if (unit != _keptUnit)
            {
                for (int i = 0; i < Kept; i++)
                {
                    _keptStep[i] = -1;
                    _kept[i] = null;
                }
                _keptUnit = unit;
            }

            int longest = TimeText.MaxLength(1, unit);
            if (_time.Length < longest)
            {
                _time = new char[longest];
                _chars = new char[Prefix.Length + longest];
                Prefix.CopyTo(0, _chars, 0, Prefix.Length);
            }

            int place = (int)(step % Kept);
            string moment = _keptStep[place] == step ? _kept[place] : null;
            if (moment == null)
            {
                int length = TimeText.Write(step / 10.0, 1, unit, _time);
                Array.Copy(_time, 0, _chars, Prefix.Length, length);
                moment = new string(_chars, 0, Prefix.Length + length);
                _kept[place] = moment;
                _keptStep[place] = step;
            }
            else
            {
                moment.CopyTo(0, _chars, 0, moment.Length);
            }

            Moment = moment;
            PlayheadLength = moment.Length - Prefix.Length;
            Step = step;
            _unit = unit;
            Version++;
        }

        /// <summary>
        /// The words forgotten, for a run opened, replayed or grown: written again as the moment is next shown -- from those
        /// kept, where its tenth's are.
        /// </summary>
        public void Forget()
        {
            Step = -1;
            Moment = null;
        }
    }
}
