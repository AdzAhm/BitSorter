using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>One name of an enum and the value it stands for: <c>IDLE = 2'b00</c>.</summary>
    public readonly struct EnumMemberInfo
    {
        public EnumMemberInfo(string name, LogicVec value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value;
        }

        public string Name { get; }
        public LogicVec Value { get; }
    }

    /// <summary>
    /// An enum type as a signal declared with it carries it: its members' names and values, so a
    /// waveform can write <c>IDLE</c> where the value is <c>2'b00</c> (Part 9b; D11).
    /// </summary>
    /// <remarks>
    /// The members are kept in declaration order. A value no member has -- an FSM's state register
    /// holding something its enum never names, or any x or z -- has no name, and the waveform writes
    /// it as a number instead.
    /// </remarks>
    public sealed class EnumInfo
    {
        public EnumInfo(string name, int width, IReadOnlyList<EnumMemberInfo> members)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width), width, "An enum is at least one bit wide.");

            Name = name;
            Width = width;
            Members = members ?? Array.Empty<EnumMemberInfo>();
        }

        /// <summary>The typedef's name, <c>state_t</c>; null for an enum declared in place, <c>enum {A, B} s;</c>.</summary>
        public string Name { get; }

        /// <summary>The width of its base type: 32 unless the enum names one.</summary>
        public int Width { get; }

        /// <summary>Its members, in the order they were declared.</summary>
        public IReadOnlyList<EnumMemberInfo> Members { get; }

        /// <summary>
        /// The name of the member holding <paramref name="value"/>, compared bit for bit at the enum's
        /// width -- signedness aside, as <c>===</c> compares -- or false when no member does.
        /// </summary>
        public bool TryName(LogicVec value, out string name)
        {
            if (value.IsKnown)
            {
                foreach (EnumMemberInfo member in Members)
                {
                    if (member.Value.Width == value.Width && LogicVec.CaseEqual(member.Value, value).IsTrue)
                    {
                        name = member.Name;
                        return true;
                    }
                }
            }

            name = null;
            return false;
        }
    }
}
