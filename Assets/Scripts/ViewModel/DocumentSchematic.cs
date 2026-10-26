using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;
using BitSorter.Sim;
using EngineGate = BitSorter.Sim.GateKind;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A document's top module as the workbench draws it (Part 8): every part as its symbol, every
    /// net as the branches a change travels from its driver to each load, the ports as boxes, and
    /// the ports again as the waveform's rows -- all from the document and its geometry, nothing
    /// written by hand.
    /// </summary>
    /// <remarks>
    /// The signals are the compiled design's own names, from the same netlist the compiler named
    /// them with (<see cref="CompiledModule.Nets"/>), under <see cref="CompiledProject.DesignPath"/>.
    /// A gate with delay prints as a library cell, so its change is recorded on the cell's output
    /// before the cell's port hands it to the net: that output is one of the net's
    /// <see cref="NetLayout.Aliases"/>, so the ring still travels the drawn wire.
    /// </remarks>
    public static class DocumentSchematic
    {
        /// <summary>How many stage units a cell is: the reference's AND is one cell tall.</summary>
        public const float Cell = 48f;

        /// <summary>The room left round the drawing, in stage units.</summary>
        public const float Margin = 30f;

        /// <summary>How far over its outline a part's name reaches, in cells.</summary>
        private const double NameRoom = 0.4;

        /// <summary>
        /// The layout of <paramref name="moduleName"/> -- the top when null -- its signals read under
        /// <paramref name="instancePath"/>, the design path when null: an inner module drawn as one of its instances
        /// runs it, <c>full_adder_tb.dut.h1</c> (Part 12a, stepping inside a block). Null for a module the project
        /// does not have.
        /// </summary>
        public static SchematicLayout Build(Project project, CompiledProject compiled, string moduleName = null, string instancePath = null)
        {
            Module module = moduleName == null ? project.TopModule : project.Find(moduleName);
            if (module == null)
                return null;
            CompiledModule top = compiled.Find(module.Name);
            Netlist netlist = top.Nets;
            string path = instancePath ?? compiled.DesignPath;
            string unit = project.Settings?.TimeUnit ?? TimeUnits.Default;

            var outlines = new Dictionary<int, PartOutline>();
            foreach (Part part in module.Parts)
                outlines[part.Id] = PartGeometry.Placed(project, part, module.Layout.PlacementOf(part.Id));

            List<NetPath> paths = WireGeometry.Nets(project, module, netlist);

            // The board's extent: every part and every wire, so nothing is drawn off the stage.
            double left = double.MaxValue, right = double.MinValue, bottom = double.MaxValue, topY = double.MinValue;
            void Include(BoardPoint p)
            {
                left = Math.Min(left, p.X);
                right = Math.Max(right, p.X);
                bottom = Math.Min(bottom, p.Y);
                topY = Math.Max(topY, p.Y);
            }

            foreach (Part part in module.Parts)
            {
                PartOutline outline = outlines[part.Id];
                Include(new BoardPoint(outline.Left, outline.Bottom));
                Include(new BoardPoint(outline.Right, outline.Top));

                // And what is written round it: a name over it, a delay over the wire leaving it.
                if (!(part is PortPart) && PartLayout.NameAbove(SymbolOf(part)))
                    Include(new BoardPoint((outline.Left + outline.Right) / 2, outline.Top + NameRoom));
                if (DelayOf(part, unit).Length > 0)
                    Include(PartGeometry.Transform(module.Layout.PlacementOf(part.Id), new BoardPoint(0.55, 0.3 + NameRoom / 2)));
            }

            foreach (NetPath net in paths)
                foreach (NetBranch branch in net.Branches)
                    foreach (BoardPoint p in branch.Points)
                        Include(p);

            if (left == double.MaxValue)
                left = right = bottom = topY = 0;

            var frame = new SchematicLayout.Frame(Cell, left, topY, Margin);
            var size = new Vec2((float)(right - left) * Cell + 2 * Margin, (float)(topY - bottom) * Cell + 2 * Margin);

            // Which pins a wire reaches: their leads are the wires' ends, not stubs of the part.
            var wired = new Dictionary<int, List<string>>();
            void Wired(PinRef pin)
            {
                if (!wired.TryGetValue(pin.Part, out List<string> pins))
                    wired[pin.Part] = pins = new List<string>();
                if (!pins.Contains(pin.Pin))
                    pins.Add(pin.Pin);
            }

            foreach (NetPath net in paths)
            {
                if (net.Branches.Count == 0)
                    continue;
                Wired(net.Source);
                foreach (NetBranch branch in net.Branches)
                    Wired(branch.Load);
            }

            var parts = new List<PartLayout>();
            var partsById = new Dictionary<int, PartLayout>();
            var ports = new List<PortLayout>();
            foreach (Part part in module.Parts)
            {
                Placement placement = module.Layout.PlacementOf(part.Id);
                PartOutline local = PartGeometry.Local(project, part);

                if (part is PortPart port)
                {
                    PartOutline placed = outlines[part.Id];
                    Vec2 a = frame.ToStage(new BoardPoint(placed.Left, placed.Top));
                    Vec2 b = frame.ToStage(new BoardPoint(placed.Right, placed.Bottom));
                    var box = Rect2.MinMaxRect(Arith.Min(a.x, b.x), Arith.Min(a.y, b.y), Arith.Max(a.x, b.x), Arith.Max(a.y, b.y));
                    ports.Add(new PortLayout(Label(port), path + "." + port.Name, box, port.Direction == PortDirection.Output));
                    continue;
                }

                // What it drives, whose flips make it flash -- or, for a lamp, what it shows.
                string output = null;
                foreach (Pin pin in part.Pins(project))
                {
                    if (pin.Direction != PinDirection.Out && !(part is LampPart))
                        continue;
                    Net net = netlist.NetOf(new PinRef(part.Id, pin.Name));
                    if (net != null)
                        output = path + "." + net.Name;
                    break;
                }

                string[] pinsWired = wired.TryGetValue(part.Id, out List<string> list) ? list.ToArray() : Array.Empty<string>();
                var layout = new PartLayout(SymbolOf(part), part, placement, local, NameOf(part), DelayOf(part, unit), output, pinsWired, frame);
                parts.Add(layout);
                partsById[part.Id] = layout;
            }

            // Where a wire meets a part's body, past its pin: a port's pin is on its box already.
            bool TryBody(PinRef pin, out Vec2 at)
            {
                at = default;
                if (!partsById.TryGetValue(pin.Part, out PartLayout part) || !part.Local.TryPin(pin.Pin, out PinSpot spot))
                    return false;
                (double x, double y) = SymbolGeometry.LeadEnd(part, spot);
                at = part.ToStage(x, y);
                return true;
            }

            var nets = new List<NetLayout>();
            foreach (NetPath net in paths)
            {
                if (net.Branches.Count == 0)
                    continue;

                string signal = path + "." + net.Net.Name;
                var aliases = new List<string> { signal };
                if (module.FindPart(net.Source.Part) is GatePart driver && PrintsAsCell(driver))
                    aliases.Add(path + "." + driver.Name + "." + CellLibrary.Output);

                // Each branch runs from the driver's body to the load's, so a change leaves the gate
                // that made it and is in the wire's colour all the way in.
                bool fromBody = TryBody(net.Source, out Vec2 source);
                var branches = new List<StageRoute>();
                foreach (NetBranch branch in net.Branches)
                {
                    var points = new List<Vec2>(branch.Points.Count + 2);
                    if (fromBody)
                        points.Add(source);
                    foreach (BoardPoint p in branch.Points)
                        points.Add(frame.ToStage(p));
                    if (TryBody(branch.Load, out Vec2 load))
                        points.Add(load);
                    branches.Add(new StageRoute(points.ToArray()));
                }

                var junctions = new List<Vec2>();
                foreach (BoardPoint junction in net.Junctions)
                    junctions.Add(frame.ToStage(junction));

                var loads = new List<string>();
                foreach (NetBranch branch in net.Branches)
                    loads.Add(Describe(module, branch.Load));
                string name = net.Net.Width > 1 ? net.Net.Name + "[" + (net.Net.Width - 1).ToString(CultureInfo.InvariantCulture) + ":0]" : net.Net.Name;
                string says = name + "   from " + Describe(module, net.Source) + " to " + string.Join(", ", loads);

                nets.Add(new NetLayout(net.Net.Name, signal, aliases, net.Net.Width, junctions, branches, says));
            }

            // A row per port, in the order they stand on the board: top to bottom, then left to
            // right, so an output beside its input reads beside it in the waveform as well.
            var ordered = new List<PortPart>(module.Ports);
            ordered.Sort((p, q) =>
            {
                BoardPoint a = module.Layout.PlacementOf(p.Id).Position, b = module.Layout.PlacementOf(q.Id).Position;
                int byHeight = b.Y.CompareTo(a.Y);
                return byHeight != 0 ? byHeight : a.X.CompareTo(b.X);
            });

            var rows = new List<WaveRow>();
            foreach (PortPart port in ordered)
                rows.Add(new WaveRow(Label(port), path + "." + port.Name));

            // A written module has no port parts to stand anywhere: its header's ports, in its header's order (Part 13b).
            if (module.IsWritten)
                foreach (ModulePort port in WrittenModules.PortsOf(project, module))
                    rows.Add(new WaveRow(port.Width > 1 ? port.Name + "[" + (port.Width - 1).ToString(CultureInfo.InvariantCulture) + ":0]" : port.Name,
                        path + "." + port.Name));

            return new SchematicLayout(size, nets, parts, ports, rows, compiled.DesignPath, compiled.Cells, unit, frame);
        }

        /// <summary>A pin as hovering names it: <c>input a</c> for a port, <c>g1.y (NOT)</c> for a part's.</summary>
        private static string Describe(Module module, PinRef pin)
        {
            Part part = module.FindPart(pin.Part);
            if (part is PortPart port)
                return (port.Direction == PortDirection.Output ? "output " : "input ") + port.Name;
            return part.Name + "." + pin.Pin + " (" + NameOf(part) + ")";
        }

        /// <summary>Whether a gate prints as one of BitSorter's cells, as <see cref="BoardCompiler"/> decides.</summary>
        public static bool PrintsAsCell(GatePart gate) =>
            gate.Width == 1 && !gate.IsIdeal && CellLibrary.CellFor(gate.Kind, gate.Inputs) != null;

        public static PartSymbol SymbolOf(Part part)
        {
            switch (part)
            {
                case GatePart gate:
                    switch (gate.Kind)
                    {
                        case EngineGate.And: return PartSymbol.And;
                        case EngineGate.Or: return PartSymbol.Or;
                        case EngineGate.Xor: return PartSymbol.Xor;
                        case EngineGate.Nand: return PartSymbol.Nand;
                        case EngineGate.Nor: return PartSymbol.Nor;
                        case EngineGate.Xnor: return PartSymbol.Xnor;
                        case EngineGate.Not: return PartSymbol.Not;
                        case EngineGate.Bufif0:
                        case EngineGate.Bufif1: return PartSymbol.TriBuffer;
                        case EngineGate.Notif0:
                        case EngineGate.Notif1: return PartSymbol.TriInverter;
                        default: return PartSymbol.Buffer;
                    }

                case MuxPart _: return PartSymbol.Mux;
                case RegisterPart _: return PartSymbol.Register;
                case ConstantPart _: return PartSymbol.Constant;
                case LampPart _: return PartSymbol.Lamp;
                case SplitterPart _: return PartSymbol.Splitter;
                case JoinerPart _: return PartSymbol.Joiner;
                default: return PartSymbol.Block;
            }
        }

        /// <summary>A gate is labelled with its kind, as a textbook labels one; a block with its module; anything else with its name.</summary>
        public static string NameOf(Part part)
        {
            switch (part)
            {
                case GatePart gate: return gate.Kind.ToString().ToUpperInvariant();
                case BlockPart block: return block.Copies > 1 ? block.Module + " ×" + block.Copies : block.Module;   // an array: half_adder x4
                case ConstantPart constant: return constant.Value.ToLiteral(SystemVerilog.Radix.Binary);
                case MuxPart mux: return "MUX";
                case RegisterPart _: return "REG";
                default: return part.Name;
            }
        }

        /// <summary>
        /// A gate's delay as written beside it, in the project's <paramref name="unit"/>: <c>rise 2 / fall 3 ns</c>
        /// when they differ, <c>3 ns</c> when not, nothing when ideal.
        /// </summary>
        public static string DelayOf(Part part, string unit = TimeUnits.Default)
        {
            if (!(part is GatePart gate) || gate.IsIdeal)
                return "";
            string rise = gate.Rise.ToString(CultureInfo.InvariantCulture), fall = gate.Fall.ToString(CultureInfo.InvariantCulture);
            return gate.Rise == gate.Fall ? rise + " " + unit : "rise " + rise + " / fall " + fall + " " + unit;
        }

        /// <summary>A port's name, with its range when it is a bus: <c>S[3:0]</c>.</summary>
        public static string Label(PortPart port) =>
            port.Width > 1 ? port.Name + "[" + (port.Width - 1).ToString(CultureInfo.InvariantCulture) + ":0]" : port.Name;
    }
}
