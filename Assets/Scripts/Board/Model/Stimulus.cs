using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Board
{
    /// <summary>One change on an input: at <see cref="Time"/>, it becomes <see cref="Value"/>.</summary>
    public readonly struct Step
    {
        public Step(long time, LogicVec value, bool late = false)
        {
            Time = time;
            Value = value;
            Late = late;
        }

        public readonly long Time;
        public readonly LogicVec Value;

        /// <summary>
        /// Whether it is made after the rest of its moment -- a nonblocking assignment -- as a testbench changes an input in
        /// answer to a clock edge at the same time, so the edge sees the value from before (Part 13c, CONVERT).
        /// </summary>
        public readonly bool Late;
    }

    /// <summary>What an input of the top module is given, step by step.</summary>
    public sealed record Track
    {
        public string Port { get; init; }
        public IReadOnlyList<Step> Steps { get; init; } = Array.Empty<Step>();

        /// <summary>
        /// The value it holds from before time 0, as a declaration gives one -- with no event at time 0, where a step at time
        /// 0 is one -- or null for unknown until its first step (Part 13c, CONVERT: <c>logic rst = 1;</c>).
        /// </summary>
        public LogicVec? Start { get; init; }
    }

    /// <summary>An input driven as a clock: low for half a period, high for half, from time 0.</summary>
    public sealed record ClockTrack
    {
        public string Port { get; init; }

        /// <summary>The whole period, in time units: even, so each half is a whole number.</summary>
        public long Period { get; init; } = 10;

        public bool StartsHigh { get; init; }

        /// <summary>
        /// Whether its first level is set at time 0 -- an event, from unknown, as <c>initial clk = 0;</c> sets it -- rather
        /// than given by its declaration, with none (Part 13c, CONVERT).
        /// </summary>
        public bool SetAtZero { get; init; }
    }

    /// <summary>
    /// What an output must read at a moment (Part 15b, the brief's D5): the self-checking half of an exercise's testbench,
    /// printed into it as an <c>if</c> that reports what failed.
    /// </summary>
    /// <remarks>
    /// An x in <see cref="Expected"/> leaves that bit free, as <c>==?</c> does. A check passes only when the comparison is a
    /// definite 1 -- <c>(sum ==? 1'b1) !== 1'b1</c> fails it -- so an output read as x where a 0 or a 1 is wanted fails,
    /// where <c>if (sum !=? 1'b1)</c> would let it pass: <c>if</c> takes an x as false. A z must be a z, and then the whole
    /// value is compared exactly, an x in it meaning x (<c>!==</c>): an output a bus lets go of reads z.
    /// </remarks>
    public sealed record Check
    {
        public string Port { get; init; }
        public long Time { get; init; }
        public LogicVec Expected { get; init; }

        /// <summary>Whether the value wanted has a z in it, and so is compared exactly.</summary>
        public bool Exact
        {
            get
            {
                for (int i = 0; i < Expected.Width; i++)
                    if (Expected[i] == Logic4.Z)
                        return true;
                return false;
            }
        }

        /// <summary>Whether <paramref name="got"/> is what is wanted, as the printed check decides it.</summary>
        public bool Passes(LogicVec got)
        {
            LogicVec same = Exact ? LogicVec.CaseEqual(got, Expected) : LogicVec.WildcardEqual(got, Expected);
            return same.Width == 1 && same[0] == Logic4.One;
        }
    }

    /// <summary>
    /// What the testbench does to the top module's inputs: clocks, and every other input step by step,
    /// until <see cref="End"/>. A reset is an input like any other, given a pulse. It is printed as a
    /// testbench in the course's style, and that testbench is what runs.
    /// </summary>
    public sealed record Stimulus
    {
        public IReadOnlyList<Track> Tracks { get; init; } = Array.Empty<Track>();
        public IReadOnlyList<ClockTrack> Clocks { get; init; } = Array.Empty<ClockTrack>();

        /// <summary>
        /// What the outputs must read, and when (Part 15b): an exercise's own test. With any, the testbench checks itself and
        /// says what failed, as the course's self-checking testbenches do.
        /// </summary>
        public IReadOnlyList<Check> Checks { get; init; } = Array.Empty<Check>();

        /// <summary>When the run is meant to stop: the testbench stops here, or at its last step if that is later (<see cref="StopTime"/>).</summary>
        public long End { get; init; } = 100;

        /// <summary>
        /// When the testbench calls <c>$stop</c>: at <see cref="End"/>, or at the last step of any track
        /// if one comes later -- <c>BoardRules</c> allows a step after the end, and the testbench plays
        /// it rather than cutting it off. The workbench runs to here, so what it shows and what the
        /// printed testbench does are the same run.
        /// </summary>
        public long StopTime
        {
            get
            {
                long stop = End;
                foreach (Track track in Tracks)
                    foreach (Step step in track.Steps)
                        if (step.Time > stop)
                            stop = step.Time;
                foreach (Check check in Checks)
                    if (check.Time > stop)
                        stop = check.Time;
                return stop;
            }
        }
    }
}
