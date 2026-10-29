using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// A drawing of code written as code again (Part 15h-4g): what an edit of the drawing of a written module makes of its code,
    /// unlocked -- the drawing, edited, compiled as any drawn module is (<see cref="BoardCompiler"/>), and printed as the
    /// module's new source. The code is what is kept; the drawing is made again from it.
    /// </summary>
    /// <remarks>
    /// **The drawing has to be the code** for the code written from it to do what it did, and it is not always: a delay on an
    /// <c>assign</c> it does not show (<see cref="RtlDrawing.Unshown"/>), a process that is no circuit, a box no expression
    /// is. Each refuses, saying which. And the drawing is checked before it is trusted (<see cref="Differs"/>): written
    /// again unedited and run against the code it came from on random values, which the workbench asks before the first
    /// edit of a module's code, and a test of every exercise and corpus design.
    ///
    /// **What it keeps**: the ports in their order, every signal's name, every gate's delay, the comments over the module.
    /// What it does not: the rest of the comments, and the shape of the code -- a case is the mux it is, a loop the
    /// instances it made, an expression its operators -- which the workbench says before the first edit.
    /// </remarks>
    public static class DrawingPrinter
    {
        /// <summary>
        /// The source of <paramref name="edited"/> -- the drawing of a written module of <paramref name="original"/>, as edited
        /// -- under the comments the module's code opened with; null, and <paramref name="why"/>, where it cannot be written.
        /// </summary>
        public static string Print(Project original, RtlDrawing drawing, Module edited, out string why)
        {
            why = null;
            Module written = original?.Find(edited?.Name);
            if (drawing == null || edited == null || written == null || !written.IsWritten)
            {
                why = "There is no code to write the drawing into.";
                return null;
            }
            if (drawing.Unshown.Count > 0)
            {
                why = "The drawing does not show " + drawing.Unshown[0] + ", so code written from it would lose it: change the code instead.";
                return null;
            }

            ModuleDeclaration declaration = WrittenModules.Read(original, written).Declaration;
            if (declaration == null)
            {
                why = "Its code does not read, so the drawing cannot be written into it.";
                return null;
            }
            foreach (ParameterDeclaration parameter in declaration.HeaderParameters)
                if (!parameter.IsLocal)
                {
                    why = "It has a parameter, " + parameter.Name + ", and the drawing is of one instance of it: change the code instead.";
                    return null;
                }
            foreach (ModuleItem item in declaration.Items)
                if (item is ParameterDeclaration parameter && !parameter.IsLocal)
                {
                    why = "It has a parameter, " + parameter.Name + ", and the drawing is of one instance of it: change the code instead.";
                    return null;
                }

            List<string> order = PortOrder(declaration);
            Module printable = Printable(edited, order, out why);
            if (printable == null)
                return null;

            CompiledProject compiled = BoardCompiler.Compile(drawing.Project.WithModule(printable));
            if (!compiled.Succeeded)
            {
                foreach (BoardDiagnostic d in compiled.Diagnostics)
                    if (d.IsError)
                    {
                        why = d.Message;
                        return null;
                    }
                why = "The drawing does not compile.";
                return null;
            }

            // A drawn module lists its inputs before its outputs; the code's order is kept, for an instance that connects by it.
            ModuleDeclaration printed = compiled.Find(printable.Name).Declaration;
            printed.Ports.Sort((a, b) => Place(order, a.Name).CompareTo(Place(order, b.Name)));
            return Head(written.Source) + Printer.Print(printed);
        }

        /// <summary>The comments and blank lines a module's code opens with, kept over the code written again.</summary>
        private static string Head(string source)
        {
            var head = new StringBuilder();
            foreach (string line in (source ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal))
                    break;
                head.Append(line).Append('\n');
            }
            return head.ToString();
        }

        private static List<string> PortOrder(ModuleDeclaration declaration)
        {
            var order = new List<string>();
            foreach (PortDeclaration port in declaration.Ports)
                order.Add(port.Name);
            if (order.Count == 0)
                order.AddRange(declaration.PortNames);
            return order;
        }

        /// <summary>
        /// The drawing made a module the board compiles as its code: its ports in the code's order, every name one the
        /// language takes and none twice, a case on a wide subject a box of the <c>?:</c> it is, and a widening drawn as a
        /// wire a box of its own.
        /// </summary>
        private static Module Printable(Module module, List<string> portOrder, out string why)
        {
            why = null;
            int next = module.NextId;
            var parts = new List<Part>();
            var wires = new List<Wire>(module.Wires);
            var byId = new Dictionary<int, Part>();

            // The ports first, in the order the code declares them, so an instance that connects by order still does.
            var ports = new List<PortPart>();
            foreach (Part part in module.Parts)
                if (part is PortPart port)
                    ports.Add(port);
            ports.Sort((a, b) => Place(portOrder, a.Name).CompareTo(Place(portOrder, b.Name)));
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (PortPart port in ports)
            {
                parts.Add(port);
                taken.Add(port.Name);
            }

            // A signal driven nowhere here that only outputs read stays so: its box and its wires are nothing, as code.
            var dropped = new HashSet<int>();
            foreach (Part part in module.Parts)
            {
                if (!(part is BoxPart undriven) || !undriven.Undriven)
                    continue;
                bool toOutputs = true;
                foreach (Wire wire in module.Wires)
                    if (wire.From.Pin.Part == part.Id && !wire.From.IsTap
                        && !(wire.To.IsTap == false && module.FindPart(wire.To.Pin.Part) is PortPart output && output.Direction == PortDirection.Output))
                        toOutputs = false;
                if (!toOutputs)
                {
                    why = "It reads " + undriven.Label + ", which another part of the design gives its value: change the code instead.";
                    return null;
                }
                dropped.Add(part.Id);
            }
            wires.RemoveAll(w => !w.From.IsTap && dropped.Contains(w.From.Pin.Part));

            foreach (Part part in module.Parts)
            {
                if (part is PortPart || dropped.Contains(part.Id))
                    continue;
                Part made = part;
                if (part is BoxPart box && box.Code == null)
                {
                    why = "Its " + box.Label + " box is no expression code can say, so the drawing cannot be written as code: change the code instead.";
                    return null;
                }
                if (part is MuxPart mux && mux.Inputs != 2 && mux.Inputs != 4 && mux.Inputs != 8)
                    made = CaseBox(mux);
                parts.Add(made);
            }

            // Every signal keeps its name; one only a port's, or made up for a value inside an expression, is left to the board.
            var portOf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (PortPart port in ports)
                portOf[port.Name] = port.Id;
            var named = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < wires.Count; i++)
            {
                Wire wire = wires[i];
                string name = Identifier(wire.NetName);
                if (name != null && portOf.TryGetValue(name, out int port))
                    name = wire.Touches(port) ? null : name + "_net";
                wires[i] = wire with { NetName = name };
                if (name != null)
                    named.Add(name);
            }

            // Every other part's name its own: an instance keeps its name, made one the language takes.
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part is PortPart || part.Name == null)
                    continue;
                string name = Identifier(part is RegisterPart && part.Name.StartsWith("$", StringComparison.Ordinal) ? part.Name.Substring(1) : part.Name) ?? "u";
                // A register given a variable of its own -- its q a port's -- calls it its name and _q: r1_q is r1's.
                if (part is RegisterPart && name.Length > 2 && name.EndsWith("_q", StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - 2);
                string unique = name;
                for (int n = 2; taken.Contains(unique) || named.Contains(unique); n++)
                    unique = name + "_" + n.ToString(CultureInfo.InvariantCulture);
                taken.Add(unique);
                parts[i] = part with { Name = unique };
            }

            foreach (Part part in parts)
                byId[part.Id] = part;

            // A widening drawn as the wire it stands on, a box of its own: the board joins only pins of one width.
            for (int i = 0; i < wires.Count; i++)
            {
                Wire wire = wires[i];
                if (wire.From.IsTap || wire.To.IsTap)
                    continue;
                int from = WidthOf(byId, wire.From.Pin), to = WidthOf(byId, wire.To.Pin);
                if (from <= 0 || to <= 0 || from == to)
                    continue;
                int id = next++;
                var widen = new BoxPart
                {
                    Id = id,
                    Label = "ext",
                    Ports = new[] { new Pin("a", PinDirection.In, from), new Pin("y", PinDirection.Out, to) },
                    Code = at => at("a") ?? Sv.Value(LogicVec.AllX(from)),
                };
                parts.Add(widen);
                byId[id] = widen;
                wires[i] = wire with { To = Endpoint.At(id, "a"), NetName = wire.NetName };
                wires.Add(new Wire { Id = next++, From = Endpoint.At(id, "y"), To = wire.To });
            }

            return module with { Parts = parts, Wires = wires, NextId = next };
        }

        private static int Place(List<string> order, string name)
        {
            int at = order.IndexOf(name);
            return at < 0 ? int.MaxValue : at;
        }

        private static int WidthOf(Dictionary<int, Part> parts, PinRef pin) =>
            parts.TryGetValue(pin.Part, out Part part) && part.TryPin(null, pin.Pin, out Pin found) ? found.Width : -1;

        /// <summary>
        /// A case on a subject of more than three bits -- more inputs than the board's mux takes -- as a box of the <c>?:</c> it
        /// is: d<i>k</i> where the subject is <i>k</i>, the last otherwise, which a known subject never reaches.
        /// </summary>
        private static BoxPart CaseBox(MuxPart mux)
        {
            int inputs = mux.Inputs, select = 0;
            while ((1 << select) < inputs)
                select++;
            var pins = new List<Pin>();
            for (int i = 0; i < inputs; i++)
                pins.Add(new Pin("d" + i.ToString(CultureInfo.InvariantCulture), PinDirection.In, mux.Width));
            pins.Add(new Pin("s", PinDirection.In, select));
            pins.Add(new Pin("y", PinDirection.Out, mux.Width));
            int width = mux.Width;
            return new BoxPart
            {
                Id = mux.Id,
                Name = mux.Name,
                Label = "case",
                Ports = pins.ToArray(),
                Code = at =>
                {
                    Expression s = at("s") ?? Sv.Value(LogicVec.AllX(select));
                    Expression Data(int k) => at("d" + k.ToString(CultureInfo.InvariantCulture)) ?? Sv.Value(LogicVec.AllX(width));
                    Expression chosen = Data(inputs - 1);
                    for (int k = inputs - 2; k >= 0; k--)
                        chosen = new ConditionalExpression
                        {
                            Condition = new BinaryExpression { Operator = "===", Left = s, Right = Sv.Value(LogicVec.FromUInt64((ulong)k, select)) },
                            WhenTrue = Data(k),
                            WhenFalse = chosen,
                        };
                    return chosen;
                },
            };
        }

        /// <summary>A name the language takes made of <paramref name="name"/> -- <c>slice[3].s</c> is <c>slice_3_s</c> -- or null for none, or one made up (<c>$p4_y</c>).</summary>
        private static string Identifier(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] == '$')
                return null;
            var text = new StringBuilder(name.Length);
            foreach (char c in name)
                text.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '_') ? c : '_');
            while (text.Length > 1 && text[text.Length - 1] == '_')
                text.Length--;
            string made = text.ToString().Replace("__", "_");
            return char.IsDigit(made[0]) ? "_" + made : made;
        }

        // -----------------------------------------------------------------
        // The check: the drawing, written again, does what the code did
        // -----------------------------------------------------------------

        /// <summary>
        /// Whether the module <paramref name="netlist"/> was drawn from, written again from <paramref name="drawing"/> unedited,
        /// does what its code did (Part 15h-4g): <paramref name="original"/> with the module's code so written, elaborated as
        /// the run elaborates it, and the same scope's netlist run against <paramref name="netlist"/> -- every input and
        /// register given the same random values in both, by name, on <paramref name="vectors"/> rounds -- each output, and
        /// the value each register takes at its next edge, compared; and each register's edges, and each gate's delays. Null
        /// where they agree; else the first difference, or why it could not be written.
        /// </summary>
        public static string Differs(Project original, RtlNetlist netlist, Design design, RtlDrawing drawing, int vectors = 24)
        {
            if (original == null || netlist == null || design == null || drawing == null)
                return "There is no drawing to check.";
            string source = Print(original, drawing, drawing.Module, out string why);
            if (source == null)
                return why;

            var editor = new BoardEditor(original);
            EditResult set = editor.SetSource(drawing.Module.Name, source);
            if (!set.Done)
                return "The code written from the drawing is refused: " + set.Refusal;
            CompiledProject compiled = BoardCompiler.Compile(editor.Project);
            if (!compiled.Succeeded)
                return "The code written from the drawing does not compile.";
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
                return "The code written from the drawing does not elaborate: " + elaboration.Diagnostics[0].Message;
            Design after = elaboration.Design;
            RtlNetlist again = RtlExtractor.Of(after, netlist.Scope);
            if (again == null)
                return "The code written from the drawing has no " + netlist.Scope + ".";

            return Compare(netlist, design, again, after, vectors);
        }

        private static string Compare(RtlNetlist before, Design beforeDesign, RtlNetlist after, Design afterDesign, int vectors)
        {
            // Each register by its signal's name and its lowest bit, and on the code's side by every name its value goes by --
            // assign count = r1_q is one wire on the drawing, and the code written from it writes count.
            List<(string Name, RtlCell Cell)> RegistersOf(RtlNetlist netlist, Design design)
            {
                var registers = new List<(string, RtlCell)>();
                foreach (RtlCell cell in netlist.Cells)
                    if (cell.Kind == RtlKind.Register && cell.Signal >= 0)
                        registers.Add((design.Signals[cell.Signal].Name, cell));
                return registers;
            }

            List<(string Name, RtlCell Cell)> was = RegistersOf(before, beforeDesign), now = RegistersOf(after, afterDesign);
            if (was.Count != now.Count)
                return "The code written from the drawing has " + now.Count + " registers where the code has " + was.Count + ".";
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < was.Count; i++)
                foreach (string name in Aliases(before, beforeDesign, was[i].Cell))
                    byName[name + "@" + was[i].Cell.Lowest.ToString(CultureInfo.InvariantCulture)] = i;
            var pair = new int[now.Count];
            var used = new bool[was.Count];
            for (int k = 0; k < now.Count; k++)
            {
                int i = -1;
                foreach (string name in Aliases(after, afterDesign, now[k].Cell))
                    if (byName.TryGetValue(name + "@" + now[k].Cell.Lowest.ToString(CultureInfo.InvariantCulture), out int found) && !used[found])
                    {
                        i = found;
                        break;
                    }
                if (i < 0)
                    return "The register " + Short(now[k].Name, after) + " of the code written from the drawing is none of the code's.";
                used[i] = true;
                pair[k] = i;
                if (!Same(was[i].Cell.Edges, now[k].Cell.Edges))
                    return "The register " + Short(was[i].Name, before) + " waits on other edges in the code written from the drawing.";
            }

            // Every gate's delays, as a list of what there are: a primitive's, or a library cell's it is written as.
            string Delays(RtlNetlist netlist)
            {
                var all = new List<string>();
                foreach (RtlCell cell in netlist.Cells)
                    if (cell.Kind == RtlKind.Gate || cell.Kind == RtlKind.Instance && !cell.Delay.Equals(Delay.Zero))
                        all.Add(cell.Delay.Rise.ToString(CultureInfo.InvariantCulture) + "/" + cell.Delay.Fall.ToString(CultureInfo.InvariantCulture));
                all.Sort(StringComparer.Ordinal);
                return string.Join(",", all);
            }
            if (Delays(before) != Delays(after))
                return "A gate's delay is not the same in the code written from the drawing.";

            // The same values in: each signal by its name -- a register's as the code's register it is -- the rest at random.
            var random = new Random(before.Scope.Length * 7919 + before.Cells.Count);
            for (int round = 0; round < vectors; round++)
            {
                var given = new Dictionary<string, LogicVec>(StringComparer.Ordinal);
                LogicVec[] beforeSignals = new LogicVec[beforeDesign.Signals.Count];
                for (int i = 0; i < beforeSignals.Length; i++)
                {
                    SignalInfo info = beforeDesign.Signals[i];
                    LogicVec value = Known(random, info.Width, round);
                    beforeSignals[i] = info.Signed ? value.AsSigned() : value;
                    given[info.Name] = value;
                }
                for (int k = 0; k < now.Count; k++)
                    if (given.TryGetValue(was[pair[k]].Name, out LogicVec held))
                        given[now[k].Name] = held;
                LogicVec[] afterSignals = new LogicVec[afterDesign.Signals.Count];
                for (int i = 0; i < afterSignals.Length; i++)
                {
                    SignalInfo info = afterDesign.Signals[i];
                    LogicVec value = given.TryGetValue(info.Name, out LogicVec same) && same.Width == info.Width ? same : Known(random, info.Width, round);
                    afterSignals[i] = info.Signed ? value.AsSigned() : value;
                }

                LogicVec[] beforeNets = Settled(before, beforeSignals);
                LogicVec[] afterNets = Settled(after, afterSignals);

                for (int k = 0; k < before.Outputs.Count && k < after.Outputs.Count; k++)
                {
                    LogicVec a = beforeNets[before.Outputs[k]].AsUnsigned(), b = afterNets[after.Outputs[k]].AsUnsigned();
                    if (!a.Equals(b))
                        return "Written from the drawing, " + before.Nets[before.Outputs[k]].Name + " is " + b + " where the code gives " + a + ".";
                }
                for (int k = 0; k < now.Count; k++)
                {
                    RtlCell register = was[pair[k]].Cell;
                    LogicVec a = beforeNets[register.Inputs[0]].AsUnsigned(), b = afterNets[now[k].Cell.Inputs[0]].AsUnsigned();
                    if (!a.Equals(b))
                        return "Written from the drawing, the register " + Short(was[pair[k]].Name, before)
                            + " would take " + b + " where the code gives it " + a + ".";
                }
            }
            return null;
        }

        /// <summary>
        /// Every net of <paramref name="netlist"/>, a library cell's instance worked out as the gate it is -- the code may write
        /// <c>or #4 g (z, a, b)</c> and the code written from its drawing <c>OR2 #(...) g (...)</c>, the same gate -- where the
        /// netlist reads an instance's outputs off its signals: each cell's output set to its gate's value, until none changes.
        /// </summary>
        private static LogicVec[] Settled(RtlNetlist netlist, LogicVec[] signals)
        {
            LogicVec[] nets = RtlEvaluator.Evaluate(netlist, signals);
            for (int pass = 0; pass < 64; pass++)
            {
                bool changed = false;
                foreach (RtlCell cell in netlist.Cells)
                {
                    if (cell.Kind != RtlKind.Instance || !CellLibrary.TryKindOf(cell.Module, out GateKind kind, out int inputs) || cell.Inputs.Count != inputs)
                        continue;
                    int z = -1;
                    for (int i = 0; i < cell.OutputNames.Count; i++)
                        if (cell.OutputNames[i] == CellLibrary.Output)
                            z = i;
                    int signal = z < 0 ? -1 : netlist.Nets[cell.Outputs[z]].Signal;
                    if (signal < 0)
                        continue;
                    var values = new LogicVec[inputs];
                    for (int i = 0; i < inputs; i++)
                        values[i] = cell.Inputs[i] >= 0 ? nets[cell.Inputs[i]] : LogicVec.AllX(1);
                    LogicVec value = Gates.Evaluate(kind, values);
                    if (!value.Equals(signals[signal]))
                    {
                        signals[signal] = value;
                        changed = true;
                    }
                }
                if (!changed)
                    break;
                nets = RtlEvaluator.Evaluate(netlist, signals);
            }
            return nets;
        }

        /// <summary>Every name a register's value goes by in <paramref name="netlist"/>: its signal's, and each a wire of it alone makes.</summary>
        private static List<string> Aliases(RtlNetlist netlist, Design design, RtlCell register)
        {
            var names = new List<string> { design.Signals[register.Signal].Name };
            var nets = new List<int> { register.Output };
            for (int at = 0; at < nets.Count; at++)
                foreach (RtlCell cell in netlist.Cells)
                    if (cell.Kind == RtlKind.Join && cell.Inputs.Count == 1 && cell.Inputs[0] == nets[at] && cell.Lowests[0] == 0
                        && netlist.Nets[cell.Output].Width == netlist.Nets[nets[at]].Width && !nets.Contains(cell.Output))
                    {
                        nets.Add(cell.Output);
                        if (cell.Signal >= 0)
                            names.Add(design.Signals[cell.Signal].Name);
                    }
            return names;
        }

        private static string Short(string name, RtlNetlist netlist)
        {
            string prefix = netlist.Scope + ".";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        private static bool Same(IReadOnlyList<EdgeKind> a, IReadOnlyList<EdgeKind> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        /// <summary>A known value of <paramref name="width"/> bits: the first rounds all 0s and all 1s, then random.</summary>
        private static LogicVec Known(Random random, int width, int round)
        {
            var digits = new char[width];
            for (int i = 0; i < width; i++)
                digits[i] = round == 0 ? '0' : round == 1 ? '1' : random.Next(2) == 0 ? '0' : '1';
            return LogicVec.FromBinary(new string(digits));
        }
    }
}
