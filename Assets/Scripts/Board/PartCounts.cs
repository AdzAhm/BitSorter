using System;
using System.Collections.Generic;
using BitSorter.Sim;

namespace BitSorter.Board
{
    /// <summary>
    /// What a design has of each part (Part 15b), from its top down: what an exercise's stock is held to
    /// (<see cref="GateLibrary.Counts"/>) and what its solved card counts.
    /// </summary>
    /// <remarks>
    /// A gate is counted by its kind -- <see cref="PartCount.Of"/> -- a bit at a time, as a wide gate prints as one per bit,
    /// and once in every instance of the module it is drawn in. A block is counted by its module, once a copy; and its
    /// insides are counted too, unless the module is itself a part with a count: then the block is the part, as a full adder
    /// an exercise stocks is one full adder and not the five gates in it. A written module's gates are counted by the
    /// gate-level rules (Part 15e), not here.
    /// </remarks>
    public static class PartCounts
    {
        public static Dictionary<string, int> Of(Project project, ICollection<string> counted = null)
        {
            var tally = new Dictionary<string, int>(StringComparer.Ordinal);
            Module top = project?.TopModule;
            if (top != null)
                Walk(project, top, 1, counted, tally, new HashSet<string>(StringComparer.Ordinal));
            return tally;
        }

        private static void Walk(Project project, Module module, long times, ICollection<string> counted, Dictionary<string, int> tally,
            HashSet<string> path)
        {
            // A module inside itself is the rules' to refuse; counting it would never end.
            if (module.IsWritten || !path.Add(module.Name))
                return;

            foreach (Part part in module.Parts)
            {
                if (part is GatePart gate)
                    Add(tally, PartCount.Of(gate.Kind), times * Math.Max(1, gate.Width));
                else if (part is BlockPart block && !string.IsNullOrEmpty(block.Module))
                {
                    long copies = times * Math.Max(1, block.Copies);
                    Add(tally, block.Module, copies);
                    if ((counted == null || !counted.Contains(block.Module)) && project.Find(block.Module) is Module inner)
                        Walk(project, inner, copies, counted, tally, path);
                }
            }

            path.Remove(module.Name);
        }

        private static void Add(Dictionary<string, int> tally, string part, long more)
        {
            long now = (tally.TryGetValue(part, out int n) ? n : 0) + more;
            tally[part] = (int)Math.Min(now, int.MaxValue);
        }
    }
}
