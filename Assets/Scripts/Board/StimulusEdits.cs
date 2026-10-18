using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Board
{
    /// <summary>
    /// Changes to what the testbench gives an input, as the workbench makes them from its waveform
    /// (Part 9b-3): a value set over a stretch of time, or a bit flipped over the stretch a moment is in.
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
        /// <summary>The value <paramref name="port"/> is given at <paramref name="time"/>: its last step at or before it, or null -- x -- before its first.</summary>
        public static LogicVec? ValueAt(Stimulus stimulus, string port, long time)
        {
            Track track = TrackOf(stimulus, port);
            if (track == null)
                return null;

            LogicVec? value = null;
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

            edited = With(stimulus, port, Fewest(made));
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
        private static List<Step> Fewest(List<Step> steps)
        {
            var fewest = new List<Step>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                if (i + 1 < steps.Count && steps[i + 1].Time == steps[i].Time)
                    continue;

                LogicVec held = fewest.Count > 0 ? fewest[fewest.Count - 1].Value : LogicVec.AllX(steps[i].Value.Width);
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
