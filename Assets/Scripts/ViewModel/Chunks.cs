using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// How a layer of the workbench is cut into chunks: runs of whole items whose estimated cost stays
    /// under a budget.
    /// </summary>
    /// <remarks>
    /// The arithmetic of the view's <c>WorkbenchLayers</c>, which owns the budgets, since they are
    /// about uGUI's meshes: one holds 65,000 vertices, so every layer is cut by whole items -- the
    /// wires by net, the changes on their way by the same nets, the parts by part, the waveform by row
    /// -- into chunks a mesh each. Here so it is tested without the editor.
    /// </remarks>
    public static class Chunks
    {
        /// <summary>
        /// Cuts items 0 to <paramref name="count"/> into runs whose estimates stay under
        /// <paramref name="budget"/>, the first run carrying <paramref name="first"/> besides. An item
        /// over the budget alone is a run of its own, and nothing at all is one empty run.
        /// </summary>
        public static List<(int Start, int Count)> Cut(int count, Func<int, int> cost, int budget, int first = 0)
        {
            var runs = new List<(int Start, int Count)>();
            int start = 0, sum = first;

            for (int i = 0; i < count; i++)
            {
                int item = cost(i);
                if (i > start && sum + item > budget)
                {
                    runs.Add((start, i - start));
                    start = i;
                    sum = 0;
                }

                sum += item;
            }

            if (count > start || runs.Count == 0)
                runs.Add((start, count - start));
            return runs;
        }
    }
}
