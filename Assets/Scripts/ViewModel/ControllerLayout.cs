using System;
using System.Collections.Generic;
using BitSorter.Board;

namespace BitSorter.ViewModel
{
    /// <summary>Where a controller is at a moment of the run (Part 15h-6e): its state, the one it came from, each table's row; -1 for none.</summary>
    public readonly struct ControllerMoment
    {
        public ControllerMoment(int state, int cameFrom, IReadOnlyList<int> rows)
        {
            State = state;
            CameFrom = cameFrom;
            Rows = rows;
        }

        public int State { get; }
        public int CameFrom { get; }
        public IReadOnlyList<int> Rows { get; }
    }

    /// <summary>A state's box on a controller's diagram: where it stands and its lines -- its name, then what it sets.</summary>
    public readonly struct ControllerBox
    {
        public ControllerBox(Vec2 min, Vec2 max, IReadOnlyList<string> lines)
        {
            Min = min;
            Max = max;
            Lines = lines;
        }

        /// <summary>Its top left corner; y runs down, as the screen's does.</summary>
        public Vec2 Min { get; }

        public Vec2 Max { get; }

        /// <summary>The state's name, then a line for each value it sets: a one-bit output set to 1 by its name alone.</summary>
        public IReadOnlyList<string> Lines { get; }

        public Vec2 Centre => new Vec2((Min.x + Max.x) / 2, (Min.y + Max.y) / 2);
    }

    /// <summary>An arrow on a controller's diagram: the reading's arrow it draws, its corners, and its words where they go.</summary>
    public readonly struct ControllerArrowShape
    {
        public ControllerArrowShape(int arrow, IReadOnlyList<Vec2> points, bool curved, string label, Vec2 labelAt)
        {
            Arrow = arrow;
            Points = points;
            Curved = curved;
            Label = label;
            LabelAt = labelAt;
        }

        /// <summary>Its index in <see cref="MachineReading.Arrows"/>.</summary>
        public int Arrow { get; }

        /// <summary>From the state it leaves to the edge of the one it reaches, where the head goes.</summary>
        public IReadOnlyList<Vec2> Points { get; }

        /// <summary>Whether it is drawn as a curve through its points, a way forward, rather than straight between them, a way back.</summary>
        public bool Curved { get; }

        /// <summary>Its condition and what it sets -- <c>start / load = 1</c> -- or null for none.</summary>
        public string Label { get; }

        /// <summary>The bottom left of its words.</summary>
        public Vec2 LabelAt { get; }
    }

    /// <summary>A cell of a control table as laid out: its rectangle, its words, and whether they are the default's or the header's.</summary>
    public readonly struct ControllerCell
    {
        public ControllerCell(Vec2 min, Vec2 max, string text, bool dim, bool header)
        {
            Min = min;
            Max = max;
            Text = text;
            Dim = dim;
            Header = header;
        }

        public Vec2 Min { get; }
        public Vec2 Max { get; }
        public string Text { get; }

        /// <summary>A value the row leaves to what is set before the case.</summary>
        public bool Dim { get; }

        /// <summary>The header row's, or the label column's.</summary>
        public bool Header { get; }
    }

    /// <summary>A control table as laid out: its title, its cells row by row, and each row's band to light.</summary>
    public sealed class ControllerTableShape
    {
        public ControllerTableShape(string title, Vec2 titleAt, IReadOnlyList<ControllerCell> cells, IReadOnlyList<(Vec2 Min, Vec2 Max)> rows, Vec2 min, Vec2 max)
        {
            Title = title;
            TitleAt = titleAt;
            Cells = cells;
            Rows = rows;
            Min = min;
            Max = max;
        }

        /// <summary><c>case (opcode)</c>.</summary>
        public string Title { get; }

        public Vec2 TitleAt { get; }
        public IReadOnlyList<ControllerCell> Cells { get; }

        /// <summary>Each row of the reading's table, header aside, as a band across the table.</summary>
        public IReadOnlyList<(Vec2 Min, Vec2 Max)> Rows { get; }

        public Vec2 Min { get; }
        public Vec2 Max { get; }
    }

    /// <summary>
    /// Where a controller's diagram and tables go (Part 15h-6e): the states left to right from the start, a column a step --
    /// a state stands a column past every state that leads to it but those that come back round to it, as the textbook draws
    /// FETCH, DECODE and the kinds of instruction after them -- the ways forward curves between columns and the ways back
    /// running under the diagram and in from the left; the tables beside it. In units of the text it is set in, y down.
    /// </summary>
    /// <remarks>
    /// A way forward that skips a column passes through a gap kept for it in each column between, so it never runs through a
    /// state's box; and a column's states are ordered to cross fewer arrows, each stood level with what leads to it where it
    /// fits -- the drawing of code's own method (<see cref="BitSorter.Board.LayeredLayout"/>), on states.
    /// </remarks>
    public sealed class ControllerLayout
    {
        /// <summary>The room inside a box or a cell round its words, and between a way back's lanes.</summary>
        public const float Pad = 8f, Lane = 14f;

        /// <summary>The least room between two columns of states, and between two boxes of one column.</summary>
        public const float ColumnGap = 56f, RowGap = 28f;

        /// <summary>The room between the diagram and a table, and between two tables.</summary>
        public const float TableGap = 48f;

        private ControllerLayout(IReadOnlyList<ControllerBox> boxes, IReadOnlyList<ControllerArrowShape> arrows, IReadOnlyList<ControllerTableShape> tables)
        {
            Boxes = boxes;
            Arrows = arrows;
            Tables = tables;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            void Take(Vec2 a, Vec2 b)
            {
                minX = Math.Min(minX, Math.Min(a.x, b.x));
                minY = Math.Min(minY, Math.Min(a.y, b.y));
                maxX = Math.Max(maxX, Math.Max(a.x, b.x));
                maxY = Math.Max(maxY, Math.Max(a.y, b.y));
            }
            foreach (ControllerBox box in boxes)
                Take(box.Min, box.Max);
            foreach (ControllerArrowShape arrow in arrows)
                foreach (Vec2 p in arrow.Points)
                    Take(p, p);
            foreach (ControllerTableShape table in tables)
                Take(table.Min, table.Max);
            Min = boxes.Count + tables.Count > 0 ? new Vec2(minX, minY) : Vec2.zero;
            Max = boxes.Count + tables.Count > 0 ? new Vec2(maxX, maxY) : Vec2.zero;
        }

        /// <summary>Each state's box, in the reading's order.</summary>
        public IReadOnlyList<ControllerBox> Boxes { get; }

        /// <summary>Each arrow's shape, in the reading's order.</summary>
        public IReadOnlyList<ControllerArrowShape> Arrows { get; }

        /// <summary>Each table's shape, in the reading's order.</summary>
        public IReadOnlyList<ControllerTableShape> Tables { get; }

        /// <summary>The corners of everything drawn.</summary>
        public Vec2 Min { get; }

        public Vec2 Max { get; }

        /// <summary>A value as a box or an arrow writes it: a one-bit output set to 1 by its name alone.</summary>
        public static string Line(ReadValue value) => value.Text == "1" && value.Width == 1 ? value.Signal : value.Signal + " = " + value.Text;

        /// <summary>
        /// <paramref name="reading"/> laid out for text <paramref name="measure"/> gives the width of -- in the bold face for a
        /// state's name, the second argument -- and whose lines are <paramref name="lineHeight"/> apart: the face the view sets
        /// it in, measured, since a proportional face's width is no count of its letters.
        /// </summary>
        public static ControllerLayout Of(ControllerReading reading, Func<string, bool, float> measure, float lineHeight)
        {
            if (reading == null)
                throw new ArgumentNullException(nameof(reading));
            if (measure == null)
                throw new ArgumentNullException(nameof(measure));
            var boxes = new List<ControllerBox>();
            var arrows = new List<ControllerArrowShape>();
            float right = 0, top = 0;
            if (reading.Machine != null)
            {
                Diagram(reading.Machine, measure, lineHeight, boxes, arrows);
                foreach (ControllerBox box in boxes)
                {
                    right = Math.Max(right, box.Max.x);
                    top = Math.Min(top, box.Min.y);
                }
                foreach (ControllerArrowShape arrow in arrows)
                    foreach (Vec2 p in arrow.Points)
                        right = Math.Max(right, p.x);
                right += TableGap;
            }

            var tables = new List<ControllerTableShape>();
            foreach (TableReading table in reading.Tables)
            {
                ControllerTableShape shape = Table(table, new Vec2(right, top), measure, lineHeight);
                tables.Add(shape);
                right = shape.Max.x + TableGap;
            }
            return new ControllerLayout(boxes, arrows, tables);
        }

        // ---- the diagram

        private static void Diagram(MachineReading machine, Func<string, bool, float> measure, float lh, List<ControllerBox> boxes, List<ControllerArrowShape> shapes)
        {
            int n = machine.States.Count;
            var outs = new List<int>[n];
            for (int i = 0; i < n; i++)
                outs[i] = new List<int>();
            foreach (MachineArrow a in machine.Arrows)
                if (a.From != a.To && !outs[a.From].Contains(a.To))
                    outs[a.From].Add(a.To);

            // The ways back: those to a state on the way to it, found from the start, then from any state not yet reached.
            var back = new HashSet<(int, int)>();
            var seen = new int[n];   // 0 not seen, 1 on the way, 2 done
            var order = new List<int>();
            void Visit(int v)
            {
                seen[v] = 1;
                order.Add(v);
                foreach (int w in outs[v])
                {
                    if (seen[w] == 1)
                        back.Add((v, w));
                    else if (seen[w] == 0)
                        Visit(w);
                }
                seen[v] = 2;
            }
            Visit(machine.Start >= 0 ? machine.Start : 0);
            for (int v = 0; v < n; v++)
                if (seen[v] == 0)
                    Visit(v);

            // Columns: the longest way forward from the start.
            var column = new int[n];
            var place = new int[n];
            for (int i = 0; i < order.Count; i++)
                place[order[i]] = i;
            bool moved = true;
            for (int round = 0; moved && round < n + 1; round++)
            {
                moved = false;
                foreach (int v in order)
                    foreach (int w in outs[v])
                        if (!back.Contains((v, w)) && column[w] < column[v] + 1)
                        {
                            column[w] = column[v] + 1;
                            moved = true;
                        }
            }
            int columns = 0;
            for (int v = 0; v < n; v++)
                columns = Math.Max(columns, column[v] + 1);

            // Each state's box size.
            var lines = new List<string>[n];
            var size = new Vec2[n];
            for (int v = 0; v < n; v++)
            {
                lines[v] = new List<string> { machine.States[v] };
                foreach (ReadValue set in machine.Settings[v])
                    lines[v].Add(Line(set));
                float longest = 0;
                for (int l = 0; l < lines[v].Count; l++)
                    longest = Math.Max(longest, measure(lines[v][l], l == 0));
                size[v] = new Vec2(longest + 2 * Pad, lines[v].Count * lh + 2 * Pad);
            }

            // Every way forward that skips a column takes a slot in each column it passes: a gap a line high.
            var slots = new List<(int Column, int Arrow, int Step)>();   // a slot's column, the arrow, and how far along it is
            var forward = new List<int>();
            for (int a = 0; a < machine.Arrows.Count; a++)
            {
                MachineArrow arrow = machine.Arrows[a];
                if (arrow.From == arrow.To || back.Contains((arrow.From, arrow.To)))
                    continue;
                forward.Add(a);
                for (int c = column[arrow.From] + 1; c < column[arrow.To]; c++)
                    slots.Add((c, a, c - column[arrow.From]));
            }

            // Within each column, states and slots in an order: by the mean place of what leads in, then out, a few times.
            var items = new List<(bool Slot, int Index)>[columns];
            for (int c = 0; c < columns; c++)
                items[c] = new List<(bool, int)>();
            foreach (int v in order)
                items[column[v]].Add((false, v));
            for (int s = 0; s < slots.Count; s++)
                items[slots[s].Column].Add((true, s));
            var rank = new Dictionary<(bool, int), float>();
            void Rank()
            {
                for (int c = 0; c < columns; c++)
                    for (int k = 0; k < items[c].Count; k++)
                        rank[items[c][k]] = k;
            }
            Rank();
            IEnumerable<(bool, int)> Neighbours((bool Slot, int Index) item, bool before)
            {
                if (item.Slot)
                {
                    (int c, int a, int step) = slots[item.Index];
                    MachineArrow arrow = machine.Arrows[a];
                    if (before)
                        yield return step == 1 ? (false, arrow.From) : (true, slots.FindIndex(x => x.Arrow == a && x.Step == step - 1));
                    else
                        yield return c + 1 == column[arrow.To] ? (false, arrow.To) : (true, slots.FindIndex(x => x.Arrow == a && x.Step == step + 1));
                    yield break;
                }
                foreach (int a in forward)
                {
                    MachineArrow arrow = machine.Arrows[a];
                    if (before && arrow.To == item.Index)
                        yield return column[arrow.From] + 1 == column[arrow.To] ? (false, arrow.From)
                            : (true, slots.FindIndex(x => x.Arrow == a && x.Column == column[arrow.To] - 1));
                    if (!before && arrow.From == item.Index)
                        yield return column[arrow.From] + 1 == column[arrow.To] ? (false, arrow.To)
                            : (true, slots.FindIndex(x => x.Arrow == a && x.Step == 1));
                }
            }
            for (int sweep = 0; sweep < 4; sweep++)
            {
                bool down = sweep % 2 == 0;
                for (int s = 0; s < columns; s++)
                {
                    int c = down ? s : columns - 1 - s;
                    var key = new Dictionary<(bool, int), float>();
                    foreach ((bool, int) item in items[c])
                    {
                        float sum = 0;
                        int count = 0;
                        foreach ((bool, int) other in Neighbours(item, before: down))
                        {
                            sum += rank[other];
                            count++;
                        }
                        key[item] = count > 0 ? sum / count : rank[item];
                    }
                    items[c].Sort((x, y) =>
                    {
                        int byKey = key[x].CompareTo(key[y]);
                        return byKey != 0 ? byKey : rank[x].CompareTo(rank[y]);
                    });
                    for (int k = 0; k < items[c].Count; k++)
                        rank[items[c][k]] = k;
                }
            }

            // Each column as wide as its widest box; between two columns room for the words of the ways into the second and
            // for the lanes of the ways back out of the first.
            var backFrom = new int[columns];
            foreach ((int from, int to) in back)
                backFrom[column[from]]++;
            var x = new float[columns];
            var width = new float[columns];
            float at = 0;
            for (int c = 0; c < columns; c++)
            {
                foreach ((bool slot, int index) in items[c])
                    if (!slot)
                        width[c] = Math.Max(width[c], size[index].x);
                x[c] = at;
                float words = 0;
                foreach (int a in forward)
                    if (column[machine.Arrows[a].To] == c + 1)
                        words = Math.Max(words, measure(Label(machine, machine.Arrows[a]) ?? "", false));
                at += width[c] + Math.Max(ColumnGap, words + 2 * Pad + backFrom[c] * Lane);
            }

            // Down each column, each item level with the mean of what leads in where it fits, then the column centred on that.
            var y = new Dictionary<(bool, int), float>();
            float Height((bool Slot, int Index) item) => item.Slot ? lh : size[item.Index].y;
            for (int c = 0; c < columns; c++)
            {
                var wanted = new List<float>();
                foreach ((bool, int) item in items[c])
                {
                    float sum = 0;
                    int count = 0;
                    foreach ((bool, int) other in Neighbours(item, before: true))
                        if (y.TryGetValue(other, out float oy))
                        {
                            sum += oy;
                            count++;
                        }
                    wanted.Add(count > 0 ? sum / count : float.NaN);
                }
                float total = 0;
                foreach ((bool, int) item in items[c])
                    total += Height(item) + RowGap;
                float next = -total / 2;   // stacked and centred where nothing leads in
                var placed = new List<float>();
                for (int k = 0; k < items[c].Count; k++)
                {
                    float h = Height(items[c][k]);
                    float centre = float.IsNaN(wanted[k]) ? next + h / 2 : Math.Max(wanted[k], next + h / 2);
                    placed.Add(centre);
                    next = centre + h / 2 + RowGap;
                }
                // Shift the column so that, on the mean, it stands where it was wanted.
                float shift = 0;
                int wants = 0;
                for (int k = 0; k < placed.Count; k++)
                    if (!float.IsNaN(wanted[k]))
                    {
                        shift += wanted[k] - placed[k];
                        wants++;
                    }
                shift = wants > 0 ? shift / wants : 0;
                for (int k = 0; k < placed.Count; k++)
                    y[items[c][k]] = placed[k] + shift;
            }

            for (int v = 0; v < n; v++)
            {
                float cx = x[column[v]] + width[column[v]] / 2, cy = y[(false, v)];
                boxes.Add(new ControllerBox(new Vec2(cx - size[v].x / 2, cy - size[v].y / 2), new Vec2(cx + size[v].x / 2, cy + size[v].y / 2), lines[v]));
            }

            // The ways in and out of each side of a box spread along it, so their heads and words do not stand on one another.
            float bottom = float.MinValue;
            foreach (ControllerBox box in boxes)
                bottom = Math.Max(bottom, box.Max.y);
            var inLeft = new Dictionary<int, List<int>>();
            var outRight = new Dictionary<int, List<int>>();
            for (int a = 0; a < machine.Arrows.Count; a++)
            {
                MachineArrow arrow = machine.Arrows[a];
                if (arrow.From == arrow.To)
                    continue;
                Add(inLeft, arrow.To, a);
                Add(outRight, arrow.From, a);
            }
            float Spread(Dictionary<int, List<int>> side, int state, int a)
            {
                List<int> list = side[state];
                ControllerBox box = boxes[state];
                float h = box.Max.y - box.Min.y - 2 * Pad;
                return list.Count == 1 ? box.Centre.y : box.Min.y + Pad + h * list.IndexOf(a) / (list.Count - 1);
            }

            // The ways back each take a lane under the diagram, those to one state sharing it.
            var lanes = new List<int>();
            var dropsInColumn = new int[columns];
            for (int a = 0; a < machine.Arrows.Count; a++)
            {
                MachineArrow arrow = machine.Arrows[a];
                string label = Label(machine, arrow);
                if (arrow.From == arrow.To)
                {
                    // A loop over the top of the box, its words above it.
                    ControllerBox box = boxes[arrow.From];
                    float r = box.Max.x - Pad * 2;
                    var loop = new[]
                    {
                        new Vec2(r, box.Min.y), new Vec2(r, box.Min.y - lh), new Vec2(r - lh * 1.5f, box.Min.y - lh), new Vec2(r - lh * 1.5f, box.Min.y),
                    };
                    shapes.Add(new ControllerArrowShape(a, loop, false, label, new Vec2(r - lh * 1.5f, box.Min.y - lh - 4)));
                    continue;
                }

                ControllerBox from = boxes[arrow.From], to = boxes[arrow.To];
                var start = new Vec2(from.Max.x, Spread(outRight, arrow.From, a));
                var end = new Vec2(to.Min.x, Spread(inLeft, arrow.To, a));
                if (!back.Contains((arrow.From, arrow.To)))
                {
                    var points = new List<Vec2> { start };
                    foreach ((int c, int sa, int step) in slots)
                        if (sa == a)
                            points.Add(new Vec2(x[c] + width[c] / 2, y[(true, slots.FindIndex(s => s.Arrow == a && s.Step == step))]));
                    points.Add(end);
                    float words = label != null ? measure(label, false) : 0;
                    shapes.Add(new ControllerArrowShape(a, points, true, label, new Vec2(end.x - Pad - words, end.y - 4)));
                    continue;
                }

                // Out to the right, down a lane of its own beside the column, along the shared lane under the diagram to the
                // left of the state it comes back to, and in.
                int lane = lanes.IndexOf(arrow.To);
                if (lane < 0)
                {
                    lanes.Add(arrow.To);
                    lane = lanes.Count - 1;
                }
                int fromColumn = column[arrow.From];
                float drop = from.Max.x + Pad + Lane * dropsInColumn[fromColumn]++;
                float under = bottom + RowGap + Lane * lane;
                float into = to.Min.x - Pad - Lane * lane;
                var path = new[] { start, new Vec2(drop, start.y), new Vec2(drop, under), new Vec2(into, under), new Vec2(into, end.y), end };
                shapes.Add(new ControllerArrowShape(a, path, false, label, new Vec2(drop + 4, start.y - 4)));
            }
        }

        private static void Add(Dictionary<int, List<int>> side, int state, int arrow)
        {
            if (!side.TryGetValue(state, out List<int> list))
                side[state] = list = new List<int>();
            list.Add(arrow);
        }

        /// <summary>An arrow's words: its condition, then what it sets after a slash, as the textbook writes a Mealy output.</summary>
        public static string Label(MachineReading machine, MachineArrow arrow)
        {
            string outputs = null;
            foreach (ReadValue value in arrow.Outputs)
                outputs = outputs == null ? Line(value) : outputs + ", " + Line(value);
            if (outputs == null)
                return arrow.Condition;
            return (arrow.Condition ?? "") + " / " + outputs;
        }

        // ---- a table

        private static ControllerTableShape Table(TableReading table, Vec2 at, Func<string, bool, float> measure, float lh)
        {
            int columns = table.Columns.Count + 1;
            var widths = new float[columns];
            widths[0] = measure(table.Subject, true);
            for (int c = 1; c < columns; c++)
                widths[c] = measure(table.Columns[c - 1], true);
            foreach (TableRow row in table.Rows)
            {
                widths[0] = Math.Max(widths[0], measure(row.Label, false));
                for (int c = 1; c < columns; c++)
                    widths[c] = Math.Max(widths[c], measure(row.Cells[c - 1].Text, false));
            }
            for (int c = 0; c < columns; c++)
                widths[c] += 2 * Pad;

            float rowHeight = lh + Pad;
            var titleAt = new Vec2(at.x, at.y + lh);
            float top = at.y + lh + Pad;
            var cells = new List<ControllerCell>();
            var rows = new List<(Vec2, Vec2)>();
            float left = at.x;
            float x = left;
            for (int c = 0; c < columns; c++)
            {
                cells.Add(new ControllerCell(new Vec2(x, top), new Vec2(x + widths[c], top + rowHeight), c == 0 ? table.Subject : table.Columns[c - 1], false, true));
                x += widths[c];
            }
            float right = x;
            float y = top + rowHeight;
            foreach (TableRow row in table.Rows)
            {
                x = left;
                for (int c = 0; c < columns; c++)
                {
                    bool label = c == 0;
                    cells.Add(new ControllerCell(new Vec2(x, y), new Vec2(x + widths[c], y + rowHeight),
                        label ? row.Label : row.Cells[c - 1].Text, !label && row.Cells[c - 1].FromDefault, label));
                    x += widths[c];
                }
                rows.Add((new Vec2(left, y), new Vec2(right, y + rowHeight)));
                y += rowHeight;
            }
            return new ControllerTableShape("case (" + table.Subject + ")", titleAt, cells, rows, at, new Vec2(right, y));
        }
    }
}
