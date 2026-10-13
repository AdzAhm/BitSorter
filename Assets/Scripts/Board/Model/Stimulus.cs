using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Board
{
    /// <summary>One change on an input: at <see cref="Time"/>, it becomes <see cref="Value"/>.</summary>
    public readonly struct Step
    {
        public Step(long time, LogicVec value)
        {
            Time = time;
            Value = value;
        }

        public readonly long Time;
        public readonly LogicVec Value;
    }

    /// <summary>What an input of the top module is given, step by step.</summary>
    public sealed record Track
    {
        public string Port { get; init; }
        public IReadOnlyList<Step> Steps { get; init; } = Array.Empty<Step>();
    }

    /// <summary>An input driven as a clock: low for half a period, high for half, from time 0.</summary>
    public sealed record ClockTrack
    {
        public string Port { get; init; }

        /// <summary>The whole period, in time units: even, so each half is a whole number.</summary>
        public long Period { get; init; } = 10;

        public bool StartsHigh { get; init; }
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

        /// <summary>When the run stops.</summary>
        public long End { get; init; } = 100;
    }
}
