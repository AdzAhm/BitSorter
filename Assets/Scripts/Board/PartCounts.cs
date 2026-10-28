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
    /// A gate is counted by its kind -- <see cref="PartCount.Of"/> -- and a register as <see cref="PartCount.Register"/>, each
    /// a bit at a time, as a wide one prints as one per bit, and once in every instance of the module it is drawn in. A block
    /// is counted by its module, once a copy; and its insides are counted too, unless the module is itself a part the library
    /// stocks: then the block is the part, as a full adder an exercise stocks is one full adder and not the five gates in it.
    /// A written module's gates are the gate-level rules' to count (Part 15e), not these.
    ///
    /// A module is counted once however many instances it has, and its count multiplied, so a hierarchy of two blocks a level,
    /// thirty deep, costs thirty modules and not a billion instances (the #85 review); and a count saturates rather than wraps.
    /// </remarks>
    public static class PartCounts
    {
        public static Dictionary<string, int> Of(Project project, ICollection<string> counted = null)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            Module top = project?.TopModule;
            if (top == null)
                return result;

            var memo = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, long> part in Tally(project, top, counted, memo, new HashSet<string>(StringComparer.Ordinal)))
                result[part.Key] = (int)Math.Min(part.Value, int.MaxValue);
            return result;
        }

        /// <summary>One instance of <paramref name="module"/>'s parts, its uncounted blocks' insides with them.</summary>
        private static Dictionary<string, long> Tally(Project project, Module module, ICollection<string> counted,
            Dictionary<string, Dictionary<string, long>> memo, HashSet<string> path)
        {
            if (memo.TryGetValue(module.Name, out Dictionary<string, long> known))
                return known;

            var tally = new Dictionary<string, long>(StringComparer.Ordinal);

            // A module inside itself is the rules' to refuse; counting it would never end.
            if (module.IsWritten || !path.Add(module.Name))
                return tally;

            foreach (Part part in module.Parts)
            {
                switch (part)
                {
                    case GatePart gate:
                        Add(tally, PartCount.Of(gate.Kind), Math.Max(1, gate.Width));
                        break;
                    case RegisterPart register:
                        Add(tally, PartCount.Register, Math.Max(1, register.Width));
                        break;
                    case BlockPart block when !string.IsNullOrEmpty(block.Module):
                        long copies = Math.Max(1, block.Copies);
                        Add(tally, block.Module, copies);
                        if ((counted == null || !counted.Contains(block.Module)) && project.Find(block.Module) is Module inner)
                            foreach (KeyValuePair<string, long> each in Tally(project, inner, counted, memo, path))
                                Add(tally, each.Key, Times(each.Value, copies));
                        break;
                }
            }

            path.Remove(module.Name);
            memo[module.Name] = tally;
            return tally;
        }

        private static long Times(long a, long b) => a > 0 && b > long.MaxValue / a ? long.MaxValue : a * b;

        private static void Add(Dictionary<string, long> tally, string part, long more)
        {
            long now = tally.TryGetValue(part, out long n) ? n : 0;
            tally[part] = now > long.MaxValue - more ? long.MaxValue : now + more;
        }
    }
}
