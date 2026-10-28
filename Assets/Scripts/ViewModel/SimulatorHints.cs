using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// 5.0's first-time hints (Part 15f): each explains something the simulator does, once ever, the first time the student
    /// meets it -- fired by what happens, never by which exercise is open. An exercise's goal states its objective and its
    /// hint nudges towards its answer; these are the third thing, and <c>SimulatorHintTests</c> holds them apart from both
    /// by the four-word rule. Kept in the save's <c>hintsSeen</c> under ids of their own, beside 4.x's.
    /// </summary>
    public static class SimulatorHints
    {
        public const string GateDelay = "delay5", Clock = "clock5", Glitch = "glitch5", HighZ = "highZ5", Unknown = "unknown5";

        /// <summary>Every hint, in the order they are asked about when more than one is due.</summary>
        public static IReadOnlyList<string> All { get; } = new[] { Clock, GateDelay, HighZ, Glitch, Unknown };

        /// <summary>What a hint says; null for an id that is none.</summary>
        public static string TextFor(string id)
        {
            switch (id)
            {
                case GateDelay:
                    return "Every gate takes time: a change at its inputs reaches its output a delay later. On the waves the output steps a little to the right of what caused it.";
                case Clock:
                    return "clk is a clock: it rises and falls on its own, one cycle after another. A register takes in its d as the clock rises, and keeps it until the next rise.";
                case Glitch:
                    return "That short pulse is a glitch: two paths into one gate arrived at different moments, so its output changed twice. CHECK reads an output once it is steady.";
                case HighZ:
                    return "z is high impedance: nothing drives the wire at all. A tri-state gate lets go of its output this way, so another driver can use the wire.";
                case Unknown:
                    return "x is an unknown value: nothing has driven the signal yet, or two drivers disagree, or a register was never given one. CHECK counts an x as wrong.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The first hint the run earns that <paramref name="seen"/> has not been shown, or null: a clock in its stimulus; a
        /// gate whose output changed after the run began; a z on a port of the design at <paramref name="designPath"/>; a
        /// glitch on one of its outputs -- two changes, back to where it was, with no input changing between them.
        /// </summary>
        public static string Due(Project project, Design design, History history, string designPath, Func<string, bool> seen)
        {
            if (project?.TopModule == null || design == null || history == null)
                return null;

            if (!seen(Clock) && project.Stimulus != null && project.Stimulus.Clocks.Count > 0)
                return Clock;

            var inputs = new List<SignalId>();
            var outputs = new List<SignalId>();
            foreach (PortPart port in project.TopModule.Ports)
                if (design.TryFind(string.IsNullOrEmpty(designPath) ? port.Name : designPath + "." + port.Name, out SignalId id))
                    (port.Direction == PortDirection.Input ? inputs : outputs).Add(id);

            if (!seen(GateDelay) && GateChanged(design, history))
                return GateDelay;
            if (!seen(HighZ) && (AnyZ(history, outputs) || AnyZ(history, inputs)))
                return HighZ;
            if (!seen(Glitch) && AnyGlitch(history, inputs, outputs))
                return Glitch;
            return null;
        }

        /// <summary>The hint a CHECK earns, or null: an x read where a check wanted a value.</summary>
        public static string AfterCheck(Grade grade, Func<string, bool> seen)
        {
            if (grade == null || seen(Unknown))
                return null;
            foreach (CheckFailure failure in grade.Failures)
                if (failure.Got != null && failure.Got.IndexOf('x') >= 0)
                    return Unknown;
            return null;
        }

        private static bool GateChanged(Design design, History history)
        {
            foreach (DriverInfo driver in design.Drivers)
                if (driver is GateDriver && (driver.Delay.Rise > 0 || driver.Delay.Fall > 0))
                    for (int i = 0; i < history.ChangeCount(driver.Target); i++)
                        if (history.Change(driver.Target, i).Time > 0)
                            return true;
            return false;
        }

        private static bool AnyZ(History history, List<SignalId> signals)
        {
            foreach (SignalId signal in signals)
                for (int i = 0; i < history.ChangeCount(signal); i++)
                    if (history.Change(signal, i).Value.Format(Radix.Binary).IndexOf('z') >= 0)
                        return true;
            return false;
        }

        /// <summary>An output that changes and changes back with no input changing between: a pulse no input asked for.</summary>
        private static bool AnyGlitch(History history, List<SignalId> inputs, List<SignalId> outputs)
        {
            var changed = new List<long>();
            foreach (SignalId input in inputs)
                for (int i = 0; i < history.ChangeCount(input); i++)
                    changed.Add(history.Change(input, i).Time);
            changed.Sort();

            foreach (SignalId output in outputs)
            {
                List<ValueChange> changes = history.SettledChanges(output);
                for (int i = 2; i < changes.Count; i++)
                {
                    ValueChange before = changes[i - 2], away = changes[i - 1], back = changes[i];
                    if (!before.Value.IsKnown || !away.Value.IsKnown || !back.Value.IsKnown || back.Time == away.Time)
                        continue;
                    if (!before.Value.Equals(back.Value) || before.Value.Equals(away.Value))
                        continue;
                    if (!AnyIn(changed, away.Time, back.Time))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Whether a time in <paramref name="sorted"/> lies after <paramref name="from"/> and no later than <paramref name="to"/>.</summary>
        private static bool AnyIn(List<long> sorted, long from, long to)
        {
            foreach (long time in sorted)
                if (time > from && time <= to)
                    return true;
            return false;
        }
    }
}
