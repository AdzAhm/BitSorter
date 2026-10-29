using System;
using System.Collections.Generic;

namespace BitSorter.Board
{
    /// <summary>
    /// Parts placed in columns by how far they are from the inputs (Part 15h-4): the inputs on the left, each part a column
    /// past everything that feeds it, the outputs on the right, a register after the logic that gives it its next value --
    /// so a value runs left to right, as the textbook draws a circuit, and a register's output comes back round. Within a
    /// column, the parts are ordered to cross fewer wires and stood level with what feeds them, as near as they fit.
    /// </summary>
    /// <remarks>
    /// The same parts and links lay out the same every time: no dictionary's order and no randomness decides anything, so a
    /// drawing of code stays where it was as the code changes elsewhere.
    /// </remarks>
    public static class LayeredLayout
    {
        /// <summary>The room between columns, in cells: wide enough for wires to turn and pass.</summary>
        public const double ColumnGap = 3;

        /// <summary>The room between parts in one column, in cells.</summary>
        public const double RowGap = 1;

        /// <summary>
        /// Where each of <paramref name="parts"/> stands. <paramref name="links"/> are what feeds what, by index into the
        /// parts, from a driver to a reader; <paramref name="first"/> stand in the first column and <paramref name="last"/> in
        /// the last, and a link into one of <paramref name="cuts"/> -- a register -- does not count, so it ends a loop.
        /// </summary>
        public static Placement[] Arrange(Project project, IReadOnlyList<Part> parts, IReadOnlyList<(int From, int To)> links,
            ISet<int> first, ISet<int> last, ISet<int> cuts)
        {
            int n = parts.Count;
            var into = new List<int>[n];
            var outOf = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                into[i] = new List<int>();
                outOf[i] = new List<int>();
            }
            foreach ((int from, int to) in links)
            {
                if (from == to || from < 0 || to < 0 || cuts.Contains(to) || first.Contains(to))
                    continue;
                if (!outOf[from].Contains(to))
                {
                    outOf[from].Add(to);
                    into[to].Add(from);
                }
            }
            BreakLoops(n, into, outOf);

            // Columns: the longest way in from anything with nothing before it.
            var column = new int[n];
            var order = TopologicalOrder(n, into, outOf);
            foreach (int v in order)
                foreach (int u in into[v])
                    column[v] = Math.Max(column[v], column[u] + 1);

            // A register stands a column past what gives it its next value; the outputs stand last of all.
            foreach ((int from, int to) in links)
                if (cuts.Contains(to) && from >= 0 && from != to && !cuts.Contains(from))
                    column[to] = Math.Max(column[to], column[from] + 1);
            int lastColumn = 0;
            for (int i = 0; i < n; i++)
                if (!last.Contains(i))
                    lastColumn = Math.Max(lastColumn, column[i]);
            for (int i = 0; i < n; i++)
            {
                if (first.Contains(i))
                    column[i] = 0;
                else if (last.Contains(i))
                    column[i] = lastColumn + 1;
                else if (column[i] == 0 && first.Count > 0)
                    column[i] = Math.Min(1, lastColumn);   // a constant or a box fed by nothing: beside the inputs, not among them
            }

            int columns = 0;
            for (int i = 0; i < n; i++)
                columns = Math.Max(columns, column[i] + 1);
            var inColumn = new List<int>[columns];
            for (int c = 0; c < columns; c++)
                inColumn[c] = new List<int>();
            for (int i = 0; i < n; i++)
                inColumn[column[i]].Add(i);

            // Every link between neighbouring columns, both ways, for ordering.
            var neighbours = new List<int>[n];
            for (int i = 0; i < n; i++)
                neighbours[i] = new List<int>();
            foreach ((int from, int to) in links)
            {
                if (from < 0 || to < 0 || from == to)
                    continue;
                neighbours[from].Add(to);
                neighbours[to].Add(from);
            }

            // Within each column, ordered by the mean place of what each links to: down, then up, a few times.
            var place = new double[n];
            for (int c = 0; c < columns; c++)
                for (int k = 0; k < inColumn[c].Count; k++)
                    place[inColumn[c][k]] = k;
            var key = new double[n];
            for (int sweep = 0; sweep < 6; sweep++)
            {
                bool down = sweep % 2 == 0;
                for (int s = 0; s < columns; s++)
                {
                    int c = down ? s : columns - 1 - s;
                    int towards = down ? c - 1 : c + 1;
                    List<int> here = inColumn[c];
                    foreach (int v in here)
                    {
                        double sum = 0;
                        int count = 0;
                        foreach (int u in neighbours[v])
                            if (column[u] == towards)
                            {
                                sum += place[u];
                                count++;
                            }
                        key[v] = count > 0 ? sum / count : place[v];
                    }
                    here.Sort((a, b) =>
                    {
                        int byKey = key[a].CompareTo(key[b]);
                        return byKey != 0 ? byKey : place[a].CompareTo(place[b]);
                    });
                    for (int k = 0; k < here.Count; k++)
                        place[here[k]] = k;
                }
            }

            // Each column as wide as its widest part, with room between; each part as tall as it is, stood level with the
            // mean height of what feeds it from the column before where it fits, else below the part above it.
            var outlines = new PartOutline[n];
            for (int i = 0; i < n; i++)
                outlines[i] = PartGeometry.Local(project, parts[i]);
            var x = new double[columns];
            double at = 0;
            for (int c = 0; c < columns; c++)
            {
                double left = 0, right = 0;
                foreach (int v in inColumn[c])
                {
                    left = Math.Min(left, outlines[v].Left);
                    right = Math.Max(right, outlines[v].Right);
                }
                x[c] = Snap(at - left);
                at = x[c] + right + ColumnGap;
            }

            var y = new double[n];
            for (int c = 0; c < columns; c++)
            {
                double floor = double.MaxValue;   // the lowest the next part's top may be, going down the column
                foreach (int v in inColumn[c])
                {
                    double sum = 0;
                    int count = 0;
                    foreach (int u in into[v])
                        if (column[u] < c)
                        {
                            sum += y[u];
                            count++;
                        }
                    double wanted = count > 0 ? sum / count : (floor == double.MaxValue ? 0 : floor - outlines[v].Top);
                    double highest = floor == double.MaxValue ? double.MaxValue : floor - outlines[v].Top;
                    y[v] = Snap(Math.Min(wanted, highest));
                    floor = y[v] + outlines[v].Bottom - RowGap;
                }
            }

            var placements = new Placement[n];
            for (int i = 0; i < n; i++)
                placements[i] = new Placement(new Point(x[column[i]], y[i]));
            return placements;
        }

        /// <summary>Every loop broken where it closes: a link back to a part already on the way is taken out.</summary>
        private static void BreakLoops(int n, List<int>[] into, List<int>[] outOf)
        {
            var state = new byte[n];   // 0 not seen, 1 on the way, 2 done
            var stack = new Stack<(int Node, int Next)>();
            for (int start = 0; start < n; start++)
            {
                if (state[start] != 0)
                    continue;
                stack.Push((start, 0));
                state[start] = 1;
                while (stack.Count > 0)
                {
                    (int node, int next) = stack.Pop();
                    if (next < outOf[node].Count)
                    {
                        stack.Push((node, next + 1));
                        int to = outOf[node][next];
                        if (state[to] == 1)
                        {
                            outOf[node].RemoveAt(next);
                            into[to].Remove(node);
                            stack.Pop();
                            stack.Push((node, next));
                        }
                        else if (state[to] == 0)
                        {
                            state[to] = 1;
                            stack.Push((to, 0));
                        }
                    }
                    else
                    {
                        state[node] = 2;
                    }
                }
            }
        }

        private static List<int> TopologicalOrder(int n, List<int>[] into, List<int>[] outOf)
        {
            var waiting = new int[n];
            var ready = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                waiting[i] = into[i].Count;
                if (waiting[i] == 0)
                    ready.Enqueue(i);
            }
            var order = new List<int>(n);
            while (ready.Count > 0)
            {
                int v = ready.Dequeue();
                order.Add(v);
                foreach (int w in outOf[v])
                    if (--waiting[w] == 0)
                        ready.Enqueue(w);
            }
            return order;
        }

        private static double Snap(double value) => Math.Round(value * 4) / 4;
    }
}
