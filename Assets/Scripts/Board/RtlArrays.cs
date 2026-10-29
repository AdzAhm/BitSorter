using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    public sealed partial class RtlDrawing
    {
        private readonly Dictionary<int, string> _keys;

        /// <summary>
        /// The key a part of the drawing is kept by in a <see cref="Module.Arrangement"/> (Part 15h-4f): <c>port:a</c>,
        /// <c>inst:u</c>, <c>reg:state</c>, <c>sig:next</c> for a part the code names, else its kind and its place among
        /// them; null for none.
        /// </summary>
        public string KeyOf(int part) => _keys != null && _keys.TryGetValue(part, out string key) ? key : null;

        /// <summary>How many instances a generate loop's are drawn as one array block of (Part 15h-4e): each loop's copies but one.</summary>
        public int FoldedInstances { get; private set; }

        private sealed partial class Builder
        {
            /// <summary>
            /// A generate loop's instances of one module, drawn as one array block (Part 15h-4e, Ahmad's choice of 2026-10-29: his
            /// 64-bit ALU drew as one column of 64 blocks): what feeds each input pin, and where each output pin goes.
            /// </summary>
            private sealed class ArrayFold
            {
                public string Name;
                public string Module;
                public RtlCell[] Copies;
                public readonly List<(string Pin, int Width, int From, int Lowest, bool Same)> Inputs = new List<(string, int, int, int, bool)>();
                public readonly List<(string Pin, int Width, RtlCell Join, int Base)> Outputs = new List<(string, int, RtlCell, int)>();
                public int Block = -1;
            }

            private readonly Dictionary<int, ArrayFold> _arrays = new Dictionary<int, ArrayFold>();
            private readonly HashSet<int> _foldedAway = new HashSet<int>();
            private readonly Dictionary<int, (ArrayFold Fold, int Output)> _joinsOfArrays = new Dictionary<int, (ArrayFold, int)>();

            /// <summary>The boxes the fold makes, by id: what each says it does, and what it works out.</summary>
            public readonly Dictionary<int, (string Says, string Expression)> Made = new Dictionary<int, (string, string)>();

            public int FoldedInstances;

            /// <summary>Each part's key in an arrangement, by its id (<see cref="RtlDrawing.KeyOf"/>).</summary>
            public readonly Dictionary<int, string> Keys = new Dictionary<int, string>();

            /// <summary>
            /// <paramref name="module"/>, laid out by itself, with the parts <paramref name="arranged"/> keeps put where they were
            /// put by hand, and any other part one of them stands on moved down its column, clear of them all.
            /// </summary>
            public Module Arrange(Module module, Module arranged)
            {
                var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Part part in module.Parts)
                    Keys[part.Id] = KeyFor(module, part, ordinals);
                if (arranged == null || arranged.Arrangement.Count == 0)
                    return module;

                bool sameCode = arranged.ArrangedFor == arranged.Source;
                var placed = new Dictionary<int, Placement>(module.Layout.Parts);
                var kept = new HashSet<int>();
                foreach (Part part in module.Parts)
                    if (Keys[part.Id] is string key && arranged.Arrangement.TryGetValue(key, out Placement put)
                        && (sameCode || !key.StartsWith("cell:", StringComparison.Ordinal)))
                    {
                        placed[part.Id] = put;
                        kept.Add(part.Id);
                    }
                if (kept.Count == 0)
                    return module;

                var outlines = new List<PartOutline>();
                foreach (Part part in module.Parts)
                    if (kept.Contains(part.Id))
                        outlines.Add(PartGeometry.Placed(_project, part, placed[part.Id]));
                foreach (Part part in module.Parts)
                {
                    if (kept.Contains(part.Id))
                        continue;
                    Placement at = placed[part.Id];
                    for (int tries = 0; tries < 200; tries++)
                    {
                        PartOutline mine = PartGeometry.Placed(_project, part, at);
                        PartOutline over = outlines.Find(o => mine.Left < o.Right + 0.5 && o.Left < mine.Right + 0.5 && mine.Bottom < o.Top + 0.5 && o.Bottom < mine.Top + 0.5);
                        if (over == null)
                            break;
                        at = new Placement(new Point(at.Position.X, at.Position.Y - (mine.Top - over.Bottom) - 1), at.QuarterTurns, at.Flipped);
                    }
                    placed[part.Id] = at;
                    outlines.Add(PartGeometry.Placed(_project, part, at));
                }
                return module with { Layout = module.Layout with { Parts = placed } };
            }

            private string KeyFor(Module module, Part part, Dictionary<string, int> ordinals)
            {
                switch (part)
                {
                    case PortPart port:
                        return "port:" + port.Name;
                    case BlockPart block when InstanceOf.TryGetValue(block.Id, out string instance):
                        return (block.Copies > 1 ? "array:" : "inst:") + (block.Copies > 1 ? block.Name : instance);
                    case RegisterPart register when register.Name != null:
                        return "reg:" + register.Name.TrimStart('$');
                }
                foreach (Wire wire in module.Wires)
                    if (!wire.From.IsTap && wire.From.Pin.Part == part.Id && !string.IsNullOrEmpty(wire.NetName) && wire.NetName[0] != '$')
                        return "sig:" + wire.NetName;
                string kind = part.GetType().Name;
                int n = ordinals.TryGetValue(kind, out int seen) ? seen : 0;
                ordinals[kind] = n + 1;
                return "cell:" + kind + ":" + n.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>
            /// Every generate loop of one project module's instances -- <c>slice[0].u</c> to <c>slice[7].u</c> -- whose every input
            /// pin reads one net bit for bit in turn, or the same net for every copy, and whose every output goes into one
            /// signal's drivers in turn: drawn as one block of so many copies. Anything else is drawn as it was, copy by copy.
            /// </summary>
            private void FindArrays()
            {
                var groups = new SortedDictionary<string, SortedDictionary<int, RtlCell>>(StringComparer.Ordinal);
                foreach (RtlCell cell in _netlist.Cells)
                {
                    if (cell.Kind != RtlKind.Instance || cell.Module == null || _project.Find(cell.Module) == null)
                        continue;
                    int open = cell.Name.IndexOf('['), close = open >= 0 ? cell.Name.IndexOf(']', open) : -1;
                    if (open <= 0 || close < 0 || !int.TryParse(cell.Name.Substring(open + 1, close - open - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                        continue;
                    string key = cell.Name.Substring(0, open) + "|" + cell.Name.Substring(close + 1) + "|" + cell.Module;
                    if (!groups.TryGetValue(key, out SortedDictionary<int, RtlCell> copies))
                        groups[key] = copies = new SortedDictionary<int, RtlCell>();
                    copies[index] = cell;
                }

                var readers = new Dictionary<int, List<RtlCell>>();
                foreach (RtlCell cell in _netlist.Cells)
                    foreach (int input in cell.Inputs)
                        if (input >= 0)
                        {
                            if (!readers.TryGetValue(input, out List<RtlCell> list))
                                readers[input] = list = new List<RtlCell>();
                            list.Add(cell);
                        }
                var outputNets = new HashSet<int>(_netlist.Outputs);

                foreach (KeyValuePair<string, SortedDictionary<int, RtlCell>> group in groups)
                {
                    var copies = new List<RtlCell>(group.Value.Values);
                    int first = 0, n = copies.Count;
                    foreach (int index in group.Value.Keys)
                    {
                        first = index;
                        break;
                    }
                    bool contiguous = true;
                    int expected = first;
                    foreach (int index in group.Value.Keys)
                        contiguous &= index == expected++;
                    if (n < 2 || n > BlockPart.MaxCopies || !contiguous)
                        continue;
                    // Not a module with an inout, which a block of copies cannot have (BoardRules), nor one whose bus pins would
                    // be wider than a wire may be (the review of #118).
                    bool inout = false, wide = false;
                    foreach (Pin pin in new BlockPart { Module = copies[0].Module }.Pins(_project))
                    {
                        inout |= pin.Direction == PinDirection.InOut;
                        wide |= (long)pin.Width * n > BoardRules.MaxWidth;
                    }
                    if (inout || wide)
                        continue;
                    var fold = new ArrayFold { Name = group.Key.Substring(0, group.Key.IndexOf('|')), Module = copies[0].Module, Copies = copies.ToArray() };
                    if (Fold(fold, readers, outputNets))
                        foreach (RtlCell copy in copies)
                        {
                            if (copy == copies[0])
                                _arrays[copy.Id] = fold;
                            else
                                _foldedAway.Add(copy.Id);
                        }
                }
            }

            /// <summary>Whether <paramref name="fold"/>'s copies can be drawn as one block, each pin's feed or destination found.</summary>
            private bool Fold(ArrayFold fold, Dictionary<int, List<RtlCell>> readers, HashSet<int> outputNets)
            {
                RtlCell[] copies = fold.Copies;
                int n = copies.Length;
                var slices = new List<int>();
                for (int k = 0; k < copies[0].Inputs.Count; k++)
                {
                    string pin = copies[0].InputNames[k];
                    int w = WidthOf(copies[0].Inputs[k]);
                    var from = new int[n];
                    var lowest = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        if (copies[i].Inputs.Count != copies[0].Inputs.Count || copies[i].InputNames[k] != pin || copies[i].Inputs[k] < 0 || WidthOf(copies[i].Inputs[k]) != w)
                            return false;
                        int net = copies[i].Inputs[k];
                        RtlCell driver = Driver(net);
                        if (driver != null && driver.Kind == RtlKind.Slice && _netlist.Nets[net].Signal < 0 && driver.Inputs[0] >= 0)
                        {
                            from[i] = Root(driver.Inputs[0]);
                            lowest[i] = driver.Lowest;
                            if (readers.TryGetValue(net, out List<RtlCell> only) && only.Count == 1)
                                slices.Add(driver.Id);
                        }
                        else
                        {
                            from[i] = Root(net);
                            lowest[i] = 0;
                        }
                    }
                    bool one = true, step = true, same = true;
                    for (int i = 0; i < n; i++)
                    {
                        one &= from[i] == from[0];
                        step &= lowest[i] == lowest[0] + i * w;
                        same &= lowest[i] == lowest[0];
                    }
                    if (!one)
                        return false;
                    if (step && lowest[0] + n * w <= WidthOf(from[0]))
                        fold.Inputs.Add((pin, w, from[0], lowest[0], false));
                    else if (same && lowest[0] == 0 && WidthOf(from[0]) == w)
                        fold.Inputs.Add((pin, w, from[0], 0, true));
                    else
                        return false;
                }

                for (int k = 0; k < copies[0].Outputs.Count; k++)
                {
                    string pin = copies[0].OutputNames[k];
                    int w = WidthOf(copies[0].Outputs[k]);
                    RtlCell join = null;
                    int bottom = -1;
                    for (int i = 0; i < n; i++)
                    {
                        if (copies[i].Outputs.Count != copies[0].Outputs.Count || copies[i].OutputNames[k] != pin)
                            return false;
                        int net = copies[i].Outputs[k];
                        // The copy's own port, slice[3].u.y, or a value of no signal: never a signal of this scope's.
                        bool own = _netlist.Nets[net].Signal < 0 || _netlist.Nets[net].Name.StartsWith(copies[i].Name + ".", StringComparison.Ordinal);
                        if (net < 0 || Root(net) != net || !own || outputNets.Contains(net) || WidthOf(net) != w
                            || !readers.TryGetValue(net, out List<RtlCell> into) || into.Count != 1 || into[0].Kind != RtlKind.Join || into[0].TwoStateTarget)
                            return false;
                        RtlCell j = into[0];
                        int at = -1;
                        for (int p = 0; p < j.Inputs.Count; p++)
                            if (j.Inputs[p] == net)
                                at = j.Lowests[p];
                        if (join == null)
                        {
                            join = j;
                            bottom = at;
                        }
                        if (j != join || at != bottom + i * w)
                            return false;
                    }
                    // A signal one loop already gives its value to is that loop's: a second loop into it is drawn copy by copy
                    // (the review of #118: its outputs were left with no wire).
                    if (_joinsOfArrays.ContainsKey(join.Id))
                        return false;
                    fold.Outputs.Add((pin, w, join, bottom));
                }

                foreach (int slice in slices)
                    _foldedAway.Add(slice);
                foreach ((string _, int _, RtlCell join, int _) in fold.Outputs)
                    _joinsOfArrays[join.Id] = (fold, fold.Outputs.Count - 1);
                FoldedInstances += n - 1;
                return true;
            }

            /// <summary>
            /// A fold's block and what feeds its pins: a net straight on, a slice of one, or one net for every copy; its outputs a
            /// bus each, wired into the signals they were joined into when those are drawn.
            /// </summary>
            private void DrawArray(ArrayFold fold)
            {
                int n = fold.Copies.Length;
                int block = Add(new BlockPart { Name = Identifier(fold.Name), Module = fold.Module, Copies = n });
                fold.Block = block;
                InstanceOf[block] = fold.Copies[0].Name;
                foreach ((string pin, int w, int from, int lowest, bool same) in fold.Inputs)
                {
                    int width = n * w;
                    if (!same && lowest == 0 && WidthOf(from) == width)
                    {
                        _loads[from].Add((block, pin));
                        continue;
                    }
                    int net = from;
                    string label = same ? "{" + n.ToString(CultureInfo.InvariantCulture) + "{ }}"
                        : "[" + (lowest + width - 1).ToString(CultureInfo.InvariantCulture) + ":" + lowest.ToString(CultureInfo.InvariantCulture) + "]";
                    int fromWidth = WidthOf(from), count = n, low = lowest;
                    int box = Add(new BoxPart
                    {
                        Label = label,
                        Ports = new[] { new Pin("a", PinDirection.In, fromWidth), new Pin("y", PinDirection.Out, width) },
                        Code = at =>
                        {
                            Expression a = at("a") ?? Sv.Value(LogicVec.AllX(fromWidth));
                            if (same)
                            {
                                var value = new ConcatenationExpression();
                                value.Items.Add(a);
                                return new ReplicationExpression { Count = Sv.Number(count), Value = value };
                            }
                            return a is NameExpression name
                                ? new RangeSelectExpression { Target = name, Left = Sv.Number(low + width - 1), Right = Sv.Number(low), Kind = RangeKind.Constant }
                                : (Expression)Sv.Value(LogicVec.AllX(width));
                        },
                    });
                    Made[box] = same
                        ? ("The one value every copy of " + Identifier(fold.Name) + " is given, " + count.ToString(CultureInfo.InvariantCulture) + " times over.", "{" + count.ToString(CultureInfo.InvariantCulture) + "{...}}")
                        : ("Bits " + (lowest + width - 1).ToString(CultureInfo.InvariantCulture) + " to " + lowest.ToString(CultureInfo.InvariantCulture)
                            + " of what feeds it, a slice of them for each copy of " + Identifier(fold.Name) + ".", label);
                    _loads[from].Add((box, "a"));
                    Inner.Add(new InnerNet((nets, inner) => same ? Replicate(nets[net], count) : nets[net].Slice(low, width)));
                    _innerWires.Add((box, "y", block, pin, Inner.Count - 1));
                }

                for (int k = 0; k < fold.Outputs.Count; k++)
                {
                    (string pin, int w, RtlCell join, int bottom) = fold.Outputs[k];
                    if (join.Inputs.Count == n && bottom == 0 && WidthOf(join.Output) == n * w)
                    {
                        _foldedAway.Add(join.Id);
                        _source[Root(join.Output)] = (block, pin);   // the copies' outputs, each its place, are the signal
                    }
                }
            }

            /// <summary>
            /// A signal the copies' outputs were joined into with something else -- a carry's c, its cin at 0 and each copy's cout
            /// above it -- as a box of what it is put together from: the bus at its place, and each other piece at its own.
            /// </summary>
            private void DrawJoinOfArray(RtlCell join)
            {
                (ArrayFold fold, int _) = _joinsOfArrays[join.Id];
                int n = fold.Copies.Length;
                var pieces = new List<(int Low, int Width, string Pin)>();
                var ports = new List<Pin>();
                var feeds = new List<(int Net, string Pin)>();
                var buses = new List<(string Pin, string BlockPin, int[] Nets, int Low, int Width)>();
                var taken = new HashSet<int>();
                foreach ((string blockPin, int w, RtlCell j, int bottom) in fold.Outputs)
                {
                    if (j != join)
                        continue;
                    var nets = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        nets[i] = fold.Copies[i].Outputs[IndexOf(fold.Copies[i].OutputNames, blockPin)];
                        taken.Add(nets[i]);
                    }
                    string pin = Range(bottom, n * w);
                    buses.Add((pin, blockPin, nets, bottom, n * w));
                    pieces.Add((bottom, n * w, pin));
                    ports.Add(new Pin(pin, PinDirection.In, n * w));
                }
                for (int p = 0; p < join.Inputs.Count; p++)
                {
                    int net = join.Inputs[p];
                    if (taken.Contains(net))
                        continue;
                    int width = p < join.PieceWidths.Count ? join.PieceWidths[p] : WidthOf(net);
                    string pin = Range(join.Lowests[p], width);
                    if (WidthOf(net) != width)
                        pin += "'";
                    pieces.Add((join.Lowests[p], width, pin));
                    ports.Add(new Pin(pin, PinDirection.In, WidthOf(net)));
                    feeds.Add((net, pin));
                }
                int total = WidthOf(join.Output);
                ports.Add(new Pin("y", PinDirection.Out, total));
                bool wire = join.Wire;
                int box = Add(new BoxPart
                {
                    Label = join.Name,
                    Ports = ports.ToArray(),
                    Code = at =>
                    {
                        pieces.Sort((p, q) => q.Low.CompareTo(p.Low));
                        var all = new ConcatenationExpression();
                        int top = total;
                        Logic4 none = wire ? Logic4.Z : Logic4.X;
                        foreach ((int low, int width, string pin) in pieces)
                        {
                            if (low + width < top)
                                all.Items.Add(Sv.Value(LogicVec.Fill(none, top - low - width)));
                            all.Items.Add(at(pin) ?? Sv.Value(LogicVec.AllX(width)));
                            top = low;
                        }
                        if (top > 0)
                            all.Items.Add(Sv.Value(LogicVec.Fill(none, top)));
                        return all;
                    },
                });
                Made[box] = (join.Name + " put together from what drives it: the copies of " + Identifier(fold.Name) + " at their places.", join.Name);
                foreach ((int net, string pin) in feeds)
                    _loads[Root(net)].Add((box, pin));
                foreach ((string pin, string blockPin, int[] nets, int low, int width) in buses)
                {
                    int[] parts = nets;
                    Inner.Add(new InnerNet((values, inner) =>
                    {
                        LogicVec joined = values[parts[0]];
                        for (int i = 1; i < parts.Length; i++)
                            joined = LogicVec.Concat(values[parts[i]], joined);
                        return joined;
                    }));
                    _innerWires.Add((fold.Block, blockPin, box, pin, Inner.Count - 1));
                }
                _source[Root(join.Output)] = (box, "y");
            }

            private static int IndexOf(IReadOnlyList<string> names, string name)
            {
                for (int i = 0; i < names.Count; i++)
                    if (names[i] == name)
                        return i;
                return -1;
            }

            private static string Range(int low, int width) =>
                width == 1 ? "[" + low.ToString(CultureInfo.InvariantCulture) + "]"
                    : "[" + (low + width - 1).ToString(CultureInfo.InvariantCulture) + ":" + low.ToString(CultureInfo.InvariantCulture) + "]";

            private static LogicVec Replicate(LogicVec value, int count)
            {
                LogicVec all = value;
                for (int i = 1; i < count; i++)
                    all = LogicVec.Concat(value, all);
                return all;
            }
        }
    }
}
