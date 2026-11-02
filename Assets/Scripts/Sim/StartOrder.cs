using System.Collections.Generic;

namespace BitSorter.Sim
{
    /// <summary>
    /// How time 0 begins (M5, Ahmad's morning decision of 2026-11-02: follow ModelSim's order, decided again after Part 16's
    /// sweep). The standard leaves it open (IEEE 1800, 4.7), and the two simulators BitSorter is held to settle it apart.
    /// </summary>
    public enum StartOrder
    {
        /// <summary>
        /// As ModelSim begins, measured (Docs/oracle-notes.md, "Time 0, as ModelSim begins it"): an <c>always</c> that begins
        /// with an event control is waiting on it before anything runs, so it sees every change time 0 makes; the other
        /// processes then run in the order they are written, depth first -- an instance's at its instance statement's place,
        /// a generate loop's iteration by iteration -- <c>always_comb</c> and <c>always_latch</c> last; and continuous logic
        /// and a stimulus's writes at time 0 arrive after them, as changes.
        /// </summary>
        ModelSim,

        /// <summary>
        /// As Icarus begins, and BitSorter did before M5: continuous logic settles from the starting values first, and then
        /// every process starts, in the design's order (an instance's before its parent's own). The Icarus oracle runs this.
        /// </summary>
        Icarus,
    }

    /// <summary>The order processes start in at time 0 under <see cref="StartOrder.ModelSim"/>.</summary>
    internal static class ModelSimStart
    {
        /// <summary>
        /// The processes that start at time 0, by their index: first every <c>always</c> that begins with an event control,
        /// in the design's order -- each only sets itself waiting -- then the rest in the order they are written
        /// (<see cref="ProcessInfo.Written"/>), and the <c>always_comb</c> and <c>always_latch</c> processes after them. A
        /// design made in code, whose processes were never met in a walk of the source, keeps the design's order. Worked out
        /// once a design, however many runs are made of it.
        /// </summary>
        public static int[] Of(Design design)
        {
            IReadOnlyList<ProcessInfo> processes = design.Processes;
            if (design.ModelSimStart != null)
                return design.ModelSimStart;   // a design does not change once built

            var armed = new List<int>();
            var rest = new List<int>();
            var last = new List<int>();
            for (int p = 0; p < processes.Count; p++)
            {
                ProcessInfo process = processes[p];
                if (process.Dormant)
                    continue;
                if ((process.Kind == "always" || process.Kind == "always_ff") && process.Code.Length > 0 && process.Code[0].Op == OpCode.WaitEvent)
                    armed.Add(p);
                else if (process.Kind == "always_comb" || process.Kind == "always_latch")
                    last.Add(p);
                else
                    rest.Add(p);
            }

            int AsWritten(int x, int y)
            {
                int c = processes[x].Written.CompareTo(processes[y].Written);
                return c != 0 ? c : x.CompareTo(y);
            }
            rest.Sort(AsWritten);
            last.Sort(AsWritten);

            var order = new int[armed.Count + rest.Count + last.Count];
            armed.CopyTo(order, 0);
            rest.CopyTo(order, armed.Count);
            last.CopyTo(order, armed.Count + rest.Count);
            design.ModelSimStart = order;
            return order;
        }
    }
}
