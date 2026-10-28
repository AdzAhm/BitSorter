using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Board
{
    /// <summary>
    /// Changes to what the testbench gives an input, as the workbench makes them from its waveform
    /// (Part 9b-3): a value set over a stretch of time, or a bit flipped over the stretch a moment is in --
    /// and from the INPUTS tab's table (Part 15g): a step set or taken away, a row of them taken away or
    /// moved, a truth table's every combination, a bus counting up, an input made a clock and back, and
    /// where the run ends.
    /// </summary>
    /// <remarks>
    /// **Pure, and on the stimulus alone**: each takes one and gives back another, or a refusal in
    /// words, and <see cref="BoardEditor"/> commits it like any edit -- checked by <see cref="BoardRules"/>,
    /// so a value of the wrong width is refused there with the rule's own message, and kept for undo.
    ///
    /// **An input is x until its first step**, as the testbench's <c>logic</c> is, and a step that gives
    /// it the value it already had is dropped as each edit is made, so the steps stay the fewest that
    /// say the same thing. **A clock is refused**: it is a period, not steps, and a step written into
    /// one would fight its <c>always</c> block. Times are whole numbers of the project's unit, as all
    /// time is.
    /// </remarks>
    public static class StimulusEdits
    {
        /// <summary>
        /// The value <paramref name="port"/> is given at <paramref name="time"/>: its last step at or before it, or before its
        /// first what it holds from the start (<see cref="Track.Start"/>, Part 13c) -- null, x, for none.
        /// </summary>
        public static LogicVec? ValueAt(Stimulus stimulus, string port, long time)
        {
            Track track = TrackOf(stimulus, port);
            if (track == null)
                return null;

            LogicVec? value = track.Start;
            foreach (Step step in track.Steps)
            {
                if (step.Time > time)
                    break;
                value = step.Value;
            }

            return value;
        }

        /// <summary>
        /// The stretch <paramref name="at"/> is in on <paramref name="port"/>: from its last step at or
        /// before it, or 0, to its next step after it, or <see cref="long.MaxValue"/> when none comes.
        /// </summary>
        public static (long From, long Until) StretchAt(Stimulus stimulus, string port, long at)
        {
            long from = 0, until = long.MaxValue;
            Track track = TrackOf(stimulus, port);
            if (track != null)
            {
                foreach (Step step in track.Steps)
                {
                    if (step.Time <= at)
                    {
                        from = step.Time;
                    }
                    else
                    {
                        until = step.Time;
                        break;
                    }
                }
            }

            return (from, until);
        }

        /// <summary>
        /// <paramref name="port"/> given <paramref name="value"/> from <paramref name="from"/> until
        /// <paramref name="until"/> -- <see cref="long.MaxValue"/> for to the end -- and from then on
        /// whatever it had at <paramref name="until"/> before. False, with the reason, for a clock, a
        /// stretch that starts before 0 or is empty, or no testbench.
        /// </summary>
        public static bool TrySet(Stimulus stimulus, string port, long from, long until, LogicVec value, out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(stimulus, port, out refusal))
                return false;
            if (from < 0)
            {
                refusal = "An input cannot change before the run starts, at 0.";
                return false;
            }
            if (until <= from)
            {
                refusal = "That stretch of time is empty: it has to end after it starts.";
                return false;
            }
            if (value.IsEmpty)
            {
                refusal = "An input is given a value, not nothing.";
                return false;
            }

            Track track = TrackOf(stimulus, port);
            IReadOnlyList<Step> steps = track?.Steps ?? Array.Empty<Step>();

            var made = new List<Step>(steps.Count + 2);
            bool stepAtUntil = false;
            foreach (Step step in steps)
                if (step.Time < from)
                    made.Add(step);

            made.Add(new Step(from, value));

            if (until != long.MaxValue)
            {
                foreach (Step step in steps)
                    if (step.Time == until)
                        stepAtUntil = true;

                // What held at the stretch's end goes on holding after it: a step there says so, unless
                // one is there already. Before the input's first step that is x, as the testbench starts it.
                if (!stepAtUntil)
                    made.Add(new Step(until, ValueAt(stimulus, port, until) ?? LogicVec.AllX(value.Width)));

                foreach (Step step in steps)
                    if (step.Time >= until)
                        made.Add(step);
            }

            edited = With(stimulus, port, Fewest(made, track?.Start));
            return true;
        }

        /// <summary>
        /// A one-bit <paramref name="port"/> flipped, 0 to 1 or 1 to 0, over the stretch
        /// <paramref name="at"/> is in (<see cref="StretchAt"/>), which <paramref name="from"/> says the start
        /// of. False, with the reason, for a bus or a stretch that is x or z, which have no one other
        /// value to flip to -- they are set instead -- and for whatever <see cref="TrySet"/> refuses.
        /// </summary>
        public static bool TryInvert(Stimulus stimulus, string port, long at, out Stimulus edited, out long from, out string refusal)
        {
            edited = null;
            from = 0;
            if (!CanEdit(stimulus, port, out refusal))
                return false;
            if (at < 0)
            {
                refusal = "An input cannot change before the run starts, at 0.";
                return false;
            }

            LogicVec? value = ValueAt(stimulus, port, at);
            if (value.HasValue && value.Value.Width != 1)
            {
                refusal = $"{port} is {value.Value.Width} bits, so it has no one other value to flip to: set it to one instead.";
                return false;
            }
            if (!value.HasValue || !value.Value.IsKnown)
            {
                string state = value.HasValue && value.Value[0] == Logic4.Z ? "z" : "x";
                refusal = $"{port} is {state} there, which has no other value to flip to: set it to 0 or 1 instead.";
                return false;
            }

            (long start, long until) = StretchAt(stimulus, port, at);
            from = start;
            LogicVec flipped = LogicVec.FromUInt64(value.Value[0] == Logic4.One ? 0UL : 1UL, 1);
            return TrySet(stimulus, port, start, until, flipped, out edited, out refusal);
        }

        // -----------------------------------------------------------------
        // The INPUTS table (Part 15g)
        // -----------------------------------------------------------------

        /// <summary>The most bits ALL COMBINATIONS counts through: 1,024 rows.</summary>
        public const int MaxCombinationBits = 10;

        /// <summary>How many values COUNT gives a bus at most: a byte's worth, where a wider one would count for ever.</summary>
        public const int MaxCount = 256;

        /// <summary>
        /// The latest moment the table puts a step or the end at: as far as an import runs a testbench
        /// (<see cref="ProjectImporter.MaxRun"/>). A run needs a finite end, and one typed as the largest number there
        /// is threw as the run was made, the document already changed (the 15g-4 review).
        /// </summary>
        public const long MaxTime = ProjectImporter.MaxRun;

        /// <summary>
        /// Whether <paramref name="a"/> and <paramref name="b"/> give the inputs the same: the same steps, starts, clocks, checks
        /// and end. A stimulus is a record over lists, so two alike are not <c>Equals</c>; an edit that changes nothing is no
        /// edit, and no undo step (the 15g-4 review).
        /// </summary>
        public static bool Same(Stimulus a, Stimulus b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.End != b.End || a.Tracks.Count != b.Tracks.Count || a.Clocks.Count != b.Clocks.Count
                || a.Checks.Count != b.Checks.Count)
                return false;

            for (int i = 0; i < a.Tracks.Count; i++)
            {
                Track x = a.Tracks[i], y = b.Tracks[i];
                if (!string.Equals(x.Port, y.Port, StringComparison.Ordinal) || x.Steps.Count != y.Steps.Count || !Equals(x.Start, y.Start))
                    return false;
                for (int j = 0; j < x.Steps.Count; j++)
                    if (x.Steps[j].Time != y.Steps[j].Time || x.Steps[j].Late != y.Steps[j].Late || !x.Steps[j].Value.Equals(y.Steps[j].Value))
                        return false;
            }
            for (int i = 0; i < a.Clocks.Count; i++)
                if (a.Clocks[i] != b.Clocks[i])
                    return false;
            for (int i = 0; i < a.Checks.Count; i++)
                if (a.Checks[i] != b.Checks[i])
                    return false;
            return true;
        }

        /// <summary>
        /// <paramref name="port"/> given <paramref name="value"/> at <paramref name="time"/>, held until its next step -- or, for
        /// null, its step there taken away, the value before it held on: a cell of the INPUTS table typed in, or emptied. Kept
        /// to the fewest steps, so a value the input already held there is no step, and the cell stays empty.
        /// </summary>
        public static bool TrySetStep(Stimulus stimulus, string port, long time, LogicVec? value, out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(stimulus, port, out refusal))
                return false;
            if (time < 0)
            {
                refusal = "An input cannot change before the run starts, at 0.";
                return false;
            }
            if (value.HasValue && value.Value.IsEmpty)
            {
                refusal = "An input is given a value, not nothing.";
                return false;
            }

            if (time > MaxTime)
            {
                refusal = $"The table goes as far as {MaxTime}: a moment past it would run for ever.";
                return false;
            }

            Track track = TrackOf(stimulus, port);
            IReadOnlyList<Step> steps = track?.Steps ?? Array.Empty<Step>();

            // A step retyped keeps whether it is made late, after the rest of its moment, as CONVERT made it: retyped plain,
            // it would race the clock edge it was made to follow (the 15g-4 review).
            bool late = false;
            foreach (Step step in steps)
                late |= step.Time == time && step.Late;

            var made = new List<Step>(steps.Count + 1);
            bool placed = !value.HasValue;
            foreach (Step step in steps)
            {
                if (!placed && step.Time > time)
                {
                    made.Add(new Step(time, value.Value, late));
                    placed = true;
                }
                if (step.Time != time)
                    made.Add(step);
            }
            if (!placed)
                made.Add(new Step(time, value.Value, late));

            edited = With(stimulus, port, Fewest(made, track?.Start));
            return true;
        }

        /// <summary>Every input's step at <paramref name="time"/> taken away: a row of the INPUTS table deleted, the values before it held on.</summary>
        public static bool TryRemoveRow(Stimulus stimulus, long time, out Stimulus edited, out string refusal)
        {
            edited = null;
            refusal = null;
            if (stimulus == null)
            {
                refusal = "There is no testbench to give the inputs anything.";
                return false;
            }

            var tracks = new List<Track>(stimulus.Tracks.Count);
            bool any = false;
            foreach (Track track in stimulus.Tracks)
            {
                var steps = new List<Step>(track.Steps.Count);
                foreach (Step step in track.Steps)
                {
                    if (step.Time == time)
                        any = true;
                    else
                        steps.Add(step);
                }
                tracks.Add(track with { Steps = Fewest(steps, track.Start) });
            }

            if (!any)
            {
                refusal = $"Nothing changes at {time}: there is no row there to take away.";
                return false;
            }

            edited = stimulus with { Tracks = tracks };
            return true;
        }

        /// <summary>
        /// Every input's step at <paramref name="from"/> moved to <paramref name="to"/>: a row's time typed again, past others
        /// or not -- each input's steps kept in time order. Refused before 0, and onto a moment where an input it changes
        /// already changes, which would put two values at one moment.
        /// </summary>
        public static bool TryMoveRow(Stimulus stimulus, long from, long to, out Stimulus edited, out string refusal)
        {
            edited = null;
            refusal = null;
            if (stimulus == null)
            {
                refusal = "There is no testbench to give the inputs anything.";
                return false;
            }
            if (to < 0)
            {
                refusal = "An input cannot change before the run starts, at 0.";
                return false;
            }
            if (to > MaxTime)
            {
                refusal = $"The table goes as far as {MaxTime}: a moment past it would run for ever.";
                return false;
            }
            if (to == from)
            {
                edited = stimulus;
                return true;
            }

            var tracks = new List<Track>(stimulus.Tracks.Count);
            bool any = false;
            foreach (Track track in stimulus.Tracks)
            {
                bool moves = false, taken = false;
                foreach (Step step in track.Steps)
                {
                    moves |= step.Time == from;
                    taken |= step.Time == to;
                }
                if (moves && taken)
                {
                    refusal = $"{track.Port} already changes at {to}: a row there would give it two values at once.";
                    return false;
                }
                if (!moves)
                {
                    tracks.Add(track);
                    continue;
                }

                any = true;
                // In time order, and steps at one moment in the order they were: CONVERT may give one moment two.
                var placed = new List<(Step Step, int Order)>(track.Steps.Count);
                for (int i = 0; i < track.Steps.Count; i++)
                {
                    Step step = track.Steps[i];
                    placed.Add((step.Time == from ? new Step(to, step.Value, step.Late) : step, i));
                }
                placed.Sort((a, b) => a.Step.Time != b.Step.Time ? a.Step.Time.CompareTo(b.Step.Time) : a.Order.CompareTo(b.Order));
                var steps = new List<Step>(placed.Count);
                foreach ((Step step, int _) in placed)
                    steps.Add(step);
                tracks.Add(track with { Steps = Fewest(steps, track.Start) });
            }

            if (!any)
            {
                refusal = $"Nothing changes at {from}: there is no row there to move.";
                return false;
            }

            edited = stimulus with { Tracks = tracks };
            return true;
        }

        /// <summary>
        /// <paramref name="ports"/> -- each with its width -- given every combination of their bits, one a <paramref name="step"/>
        /// apart from <paramref name="start"/>, counting up with the first port's bits the most significant: a truth table's
        /// rows, the other inputs as they were. Each chosen input's own steps are replaced, and the run ends a step after the
        /// last combination if it ended sooner. Refused past <see cref="MaxCombinationBits"/> bits, or for a clock.
        /// </summary>
        public static bool TryFillCombinations(Stimulus stimulus, IReadOnlyList<(string Port, int Width)> ports, long start, long step,
            out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanFill(stimulus, ports, start, step, out refusal))
                return false;

            int bits = 0;
            foreach ((string _, int width) in ports)
                bits += width;
            if (bits > MaxCombinationBits)
            {
                refusal = $"Those inputs are {bits} bits between them: every combination of more than {MaxCombinationBits} is over a thousand rows.";
                return false;
            }

            long rows = 1L << bits;
            if (!InTime(start, rows, step, out refusal))
                return false;
            Stimulus made = stimulus;
            int shift = bits;
            foreach ((string port, int width) in ports)
            {
                shift -= width;
                var steps = new List<Step>((int)rows);
                for (long row = 0; row < rows; row++)
                    steps.Add(new Step(start + row * step, LogicVec.FromUInt64((ulong)(row >> shift) & Mask(width), width)));
                made = With(made, port, Fewest(steps, TrackOf(made, port)?.Start));
            }

            edited = Ended(made, start + rows * step);
            return true;
        }

        /// <summary>
        /// <paramref name="port"/>, <paramref name="width"/> bits wide, counting up from 0, a value a <paramref name="step"/>
        /// apart from <paramref name="start"/>: every value it has, or <see cref="MaxCount"/> of a wider one. Its own steps
        /// are replaced, and the run ends a step after the last if it ended sooner.
        /// </summary>
        public static bool TryFillCount(Stimulus stimulus, string port, int width, long start, long step, out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanFill(stimulus, new[] { (port, width) }, start, step, out refusal))
                return false;

            long count = width >= 8 ? MaxCount : 1L << width;
            if (!InTime(start, count, step, out refusal))
                return false;
            var steps = new List<Step>((int)count);
            for (long i = 0; i < count; i++)
                steps.Add(new Step(start + i * step, LogicVec.FromUInt64((ulong)i, width)));
            edited = Ended(With(stimulus, port, Fewest(steps, TrackOf(stimulus, port)?.Start)), start + count * step);
            return true;
        }

        /// <summary>
        /// <paramref name="port"/> made a clock of <paramref name="period"/>, low for its first half from 0 -- its steps given up,
        /// a clock being a period and not steps. Refused for a period that is not even and at least 2.
        /// </summary>
        public static bool TryMakeClock(Stimulus stimulus, string port, long period, out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(stimulus, port, out refusal))
                return false;
            if (period < 2 || period % 2 != 0)
            {
                refusal = $"A clock's period is even, and at least 2, so each half is a whole number: {period} is not.";
                return false;
            }

            var tracks = new List<Track>(stimulus.Tracks.Count);
            foreach (Track track in stimulus.Tracks)
                if (!string.Equals(track.Port, port, StringComparison.Ordinal))
                    tracks.Add(track);
            var clocks = new List<ClockTrack>(stimulus.Clocks) { new ClockTrack { Port = port, Period = period } };
            edited = stimulus with { Tracks = tracks, Clocks = clocks };
            return true;
        }

        /// <summary><paramref name="port"/>, a clock, made an input given steps again: x until its first, as any input starts.</summary>
        public static bool TryUnmakeClock(Stimulus stimulus, string port, out Stimulus edited, out string refusal)
        {
            edited = null;
            refusal = null;
            if (stimulus == null)
            {
                refusal = "There is no testbench to give the inputs anything.";
                return false;
            }

            var clocks = new List<ClockTrack>(stimulus.Clocks.Count);
            bool found = false;
            foreach (ClockTrack clock in stimulus.Clocks)
            {
                if (string.Equals(clock.Port, port, StringComparison.Ordinal))
                    found = true;
                else
                    clocks.Add(clock);
            }

            if (!found)
            {
                refusal = $"{port} is no clock.";
                return false;
            }

            edited = stimulus with { Clocks = clocks };
            return true;
        }

        /// <summary>The run ending at <paramref name="end"/>: refused before 1, where nothing would be run at all.</summary>
        public static bool TrySetEnd(Stimulus stimulus, long end, out Stimulus edited, out string refusal)
        {
            edited = null;
            refusal = null;
            if (stimulus == null)
            {
                refusal = "There is no testbench to give the inputs anything.";
                return false;
            }
            if (end < 1)
            {
                refusal = "The run has to last a moment at least: it ends at 1 or later.";
                return false;
            }
            if (end > MaxTime)
            {
                refusal = $"The run ends by {MaxTime} at the latest: a run longer still would take for ever to make.";
                return false;
            }

            edited = stimulus with { End = end };
            return true;
        }

        private static bool CanFill(Stimulus stimulus, IReadOnlyList<(string Port, int Width)> ports, long start, long step, out string refusal)
        {
            refusal = null;
            if (ports == null || ports.Count == 0)
            {
                refusal = "Choose the inputs first: click a column's name.";
                return false;
            }
            foreach ((string port, int width) in ports)
            {
                if (!CanEdit(stimulus, port, out refusal))
                    return false;
                if (width < 1)
                {
                    refusal = $"{port} has no bits to give.";
                    return false;
                }
            }
            if (start < 0)
            {
                refusal = "An input cannot change before the run starts, at 0.";
                return false;
            }
            if (step < 1)
            {
                refusal = "A step is a moment at least: 1 or more.";
                return false;
            }
            return true;
        }

        /// <summary>Whether <paramref name="rows"/> rows a <paramref name="step"/> apart from <paramref name="start"/> end by <see cref="MaxTime"/>.</summary>
        private static bool InTime(long start, long rows, long step, out string refusal)
        {
            refusal = null;
            if (step <= (MaxTime - start) / rows)
                return true;
            refusal = $"{rows} rows {step} apart go past {MaxTime}, as far as the table goes: take a smaller step.";
            return false;
        }

        private static ulong Mask(int width) => width >= 64 ? ulong.MaxValue : (1UL << width) - 1;

        /// <summary>The stimulus ending at <paramref name="end"/> at the earliest.</summary>
        private static Stimulus Ended(Stimulus stimulus, long end) => stimulus.End >= end ? stimulus : stimulus with { End = end };

        private static bool CanEdit(Stimulus stimulus, string port, out string refusal)
        {
            refusal = null;
            if (stimulus == null)
            {
                refusal = "There is no testbench to give the inputs anything.";
                return false;
            }

            foreach (ClockTrack clock in stimulus.Clocks)
            {
                if (string.Equals(clock.Port, port, StringComparison.Ordinal))
                {
                    refusal = $"{port} is a clock: it ticks every {clock.Period} by itself, and has no steps to change.";
                    return false;
                }
            }

            return true;
        }

        private static Track TrackOf(Stimulus stimulus, string port)
        {
            if (stimulus == null)
                return null;
            foreach (Track track in stimulus.Tracks)
                if (string.Equals(track.Port, port, StringComparison.Ordinal))
                    return track;
            return null;
        }

        /// <summary>The steps with every one that changes nothing dropped: one at the same moment as the next, or giving the value already held -- x, before the first.</summary>
        private static List<Step> Fewest(List<Step> steps, LogicVec? start)
        {
            var fewest = new List<Step>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                if (i + 1 < steps.Count && steps[i + 1].Time == steps[i].Time)
                    continue;

                LogicVec held = fewest.Count > 0 ? fewest[fewest.Count - 1].Value : start ?? LogicVec.AllX(steps[i].Value.Width);
                if (steps[i].Value.Equals(held))
                    continue;

                fewest.Add(steps[i]);
            }

            return fewest;
        }

        /// <summary>The stimulus with <paramref name="port"/>'s steps replaced, its track added at the end if it had none.</summary>
        private static Stimulus With(Stimulus stimulus, string port, List<Step> steps)
        {
            var tracks = new List<Track>(stimulus.Tracks.Count + 1);
            bool found = false;
            foreach (Track track in stimulus.Tracks)
            {
                if (string.Equals(track.Port, port, StringComparison.Ordinal))
                {
                    tracks.Add(track with { Steps = steps });
                    found = true;
                }
                else
                {
                    tracks.Add(track);
                }
            }

            if (!found)
                tracks.Add(new Track { Port = port, Steps = steps });
            return stimulus with { Tracks = tracks };
        }
    }
}
