using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>
    /// How time 0 begins (M5, Ahmad's morning decision of 2026-11-02: follow ModelSim's order, decided again after Part 16's
    /// sweep). The standard leaves it open (IEEE 1800, 4.7), and the two simulators BitSorter is held to settle it apart.
    /// </summary>
    public enum StartOrder
    {
        /// <summary>
        /// As ModelSim begins, measured (Docs/oracle-notes.md, "Time 0"): an <c>always</c> that begins with an event control is
        /// waiting on it before anything runs, so it sees every change time 0 makes; the other processes then run in the
        /// order they are written, an instance's at its instance statement's place, <c>always_comb</c> and
        /// <c>always_latch</c> last; and continuous logic settles after them, as changes of its own.
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
        /// in the design's order -- each only sets itself waiting -- then the rest as written, depth first, and the
        /// <c>always_comb</c> and <c>always_latch</c> processes after them. A design made in code, with no places, keeps the
        /// design's order within each group.
        /// </summary>
        public static int[] Of(Design design)
        {
            IReadOnlyList<ProcessInfo> processes = design.Processes;
            IReadOnlyList<ScopeInfo> scopes = design.Scopes;
            var paths = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int s = 0; s < scopes.Count; s++)
                paths[scopes[s].Path] = s;

            var armed = new List<int>();
            var rest = new List<int>();
            var last = new List<int>();
            var keys = new Dictionary<int, List<SourceLocation>>();
            Dictionary<int, SourceLocation> scopeAt = PlacesOf(design, paths);

            for (int p = 0; p < processes.Count; p++)
            {
                ProcessInfo process = processes[p];
                if (process.Dormant)
                    continue;
                if ((process.Kind == "always" || process.Kind == "always_ff") && process.Code.Length > 0 && process.Code[0].Op == OpCode.WaitEvent)
                    armed.Add(p);
                else
                {
                    (process.Kind == "always_comb" || process.Kind == "always_latch" ? last : rest).Add(p);
                    keys[p] = KeyOf(process, scopes, paths, scopeAt);
                }
            }

            Comparison<int> written = (x, y) =>
            {
                int c = Compare(keys[x], keys[y]);
                return c != 0 ? c : x.CompareTo(y);
            };
            rest.Sort(written);
            last.Sort(written);

            var order = new int[armed.Count + rest.Count + last.Count];
            armed.CopyTo(order, 0);
            rest.CopyTo(order, armed.Count);
            last.CopyTo(order, armed.Count + rest.Count);
            return order;
        }

        /// <summary>
        /// Where each scope stands in its parent: its instance statement's place, or -- a generate block, written round what
        /// it holds -- the first place of anything in it.
        /// </summary>
        private static Dictionary<int, SourceLocation> PlacesOf(Design design, Dictionary<string, int> paths)
        {
            var at = new Dictionary<int, SourceLocation>();
            for (int s = 0; s < design.Scopes.Count; s++)
                if (design.Scopes[s].At.Line > 0)
                    at[s] = design.Scopes[s].At;

            // A scope with no place of its own takes the first of what is in it, its processes' and its scopes', deepest first.
            for (int s = design.Scopes.Count - 1; s > 0; s--)
            {
                if (at.ContainsKey(s))
                    continue;
                SourceLocation first = default;
                foreach (ProcessInfo process in design.Processes)
                    if (ScopeOf(process, paths) == s && process.At.Line > 0 && (first.Line == 0 || Before(process.At, first)))
                        first = process.At;
                for (int c = s + 1; c < design.Scopes.Count; c++)
                    if (design.Scopes[c].Parent == s && at.TryGetValue(c, out SourceLocation child) && (first.Line == 0 || Before(child, first)))
                        first = child;
                if (first.Line > 0)
                    at[s] = first;
            }
            return at;
        }

        /// <summary>A process's places from the top down: each scope's in its parent, then its own.</summary>
        private static List<SourceLocation> KeyOf(ProcessInfo process, IReadOnlyList<ScopeInfo> scopes, Dictionary<string, int> paths,
            Dictionary<int, SourceLocation> scopeAt)
        {
            var key = new List<SourceLocation> { process.At };
            for (int s = ScopeOf(process, paths); s > 0; s = scopes[s].Parent)
                key.Add(scopeAt.TryGetValue(s, out SourceLocation at) ? at : default);
            key.Reverse();
            return key;
        }

        /// <summary>The scope a process is written in, from its name -- its scope's path, then <c>.kind@line</c>; the top if none matches.</summary>
        private static int ScopeOf(ProcessInfo process, Dictionary<string, int> paths)
        {
            string name = process.Name;
            int cut = name.LastIndexOf("." + process.Kind + "@", StringComparison.Ordinal);
            return cut > 0 && paths.TryGetValue(name.Substring(0, cut), out int scope) ? scope : 0;
        }

        private static int Compare(List<SourceLocation> x, List<SourceLocation> y)
        {
            for (int i = 0; i < Math.Min(x.Count, y.Count); i++)
            {
                int c = x[i].Line.CompareTo(y[i].Line);
                if (c == 0)
                    c = x[i].Column.CompareTo(y[i].Column);
                if (c != 0)
                    return c;
            }
            return x.Count.CompareTo(y.Count);
        }

        private static bool Before(SourceLocation x, SourceLocation y) => x.Line < y.Line || x.Line == y.Line && x.Column < y.Column;
    }
}
