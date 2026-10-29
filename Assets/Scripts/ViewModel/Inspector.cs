using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using EngineGate = BitSorter.Sim.GateKind;

namespace BitSorter.ViewModel
{
    /// <summary>One thing the inspector shows a part or a wire to have, as text the student can change.</summary>
    public sealed class InspectorField
    {
        public InspectorField(string key, string label, string text, string hint = null)
        {
            Key = key;
            Label = label;
            Text = text ?? "";
            Hint = hint ?? "";
        }

        /// <summary>What <see cref="Inspector.Apply"/> knows it by: name, width, inputs, rise, fall, value, reset, start, tcq, tsetup, period, release, comment.</summary>
        public string Key { get; }

        /// <summary>What it is called on screen.</summary>
        public string Label { get; }

        /// <summary>What it is now, written as it would be typed.</summary>
        public string Text { get; }

        /// <summary>What may be written in it, shown when it is empty.</summary>
        public string Hint { get; }

        public override string ToString() => Key + "=" + Text;
    }

    /// <summary>What the inspector shows of one part or one wire: a title, what it is, its fields in order, and the quick ones.</summary>
    public sealed class Inspection
    {
        internal Inspection(string title, string says, int part, int wire, IReadOnlyList<InspectorField> fields, params string[] quick)
        {
            Title = title;
            Says = says;
            Part = part;
            Wire = wire;
            Fields = fields;

            var shown = new List<InspectorField>();
            foreach (string key in quick)
                if (Field(key) is InspectorField field)
                    shown.Add(field);
            Quick = shown;
        }

        /// <summary>The part's name and kind, or the wire's net: "AND g1", "WIRE n3".</summary>
        public string Title { get; }

        /// <summary>A line saying what it is, and anything it cannot change here.</summary>
        public string Says { get; }

        /// <summary>The part inspected, or -1 for a wire.</summary>
        public int Part { get; }

        /// <summary>The wire inspected, or -1 for a part.</summary>
        public int Wire { get; }

        public IReadOnlyList<InspectorField> Fields { get; }

        /// <summary>
        /// The fields of its kind worth having at hand (Part 11b), in the side panel beside the circuit: a gate's rise and
        /// fall, a port's name and width, a clock's period, a reset's release, a register's values, a constant's value, a
        /// mux's, splitter's, joiner's or lamp's width, a block's name, a wire's net's name. Each is one of
        /// <see cref="Fields"/>, which the bar over the circuit shows whole.
        /// </summary>
        public IReadOnlyList<InspectorField> Quick { get; }

        /// <summary>The field of this key, or null.</summary>
        public InspectorField Field(string key)
        {
            foreach (InspectorField field in Fields)
                if (field.Key == key)
                    return field;
            return null;
        }
    }

    /// <summary>
    /// The inspector (Part 10c): the fields a part or a wire has -- a gate's inputs, rise, fall and width, a port's
    /// name, width and comment and, for an input of the top module, its clock's period or its reset's release, a
    /// register's width, reset and values, a constant's value, a net's name -- each read back from what was typed
    /// with words for anything that is no number, time or value, and the one edit they make together. Unity-free.
    /// </summary>
    /// <remarks>
    /// **The document is still the judge.** The inspector reads text; whether a width, a count of inputs or a name
    /// is allowed is <see cref="BoardRules"/>' to say, and a refusal comes back in its words. Every field changed is
    /// one step for undo (<see cref="BoardEditor.Group"/>): a port renamed and its clock's period changed with it go
    /// back together.
    /// </remarks>
    public static class Inspector
    {
        /// <summary>
        /// What the inspector shows of <paramref name="target"/> in <paramref name="module"/>: a part's, for its body or
        /// one of its pins; a wire's, for the wire or one of its corners. Null for nothing, or something no longer there.
        /// </summary>
        public static Inspection Of(Project project, string module, CanvasTarget target)
        {
            Module m = project?.Find(module);
            if (m == null)
                return null;

            switch (target.Kind)
            {
                case CanvasTargetKind.Part:
                case CanvasTargetKind.Pin:
                    Part part = m.FindPart(target.Part);
                    return part == null ? null : OfPart(project, m, part);

                case CanvasTargetKind.Wire:
                case CanvasTargetKind.Bend:
                    Wire wire = m.FindWire(target.Wire);
                    return wire == null ? null : OfWire(project, m, wire);

                default:
                    return null;
            }
        }

        private static Inspection OfWire(Project project, Module m, Wire wire)
        {
            Net net = Netlist.Of(project, m).NetOfWire(wire.Id);
            string netName = net?.Name ?? "";
            bool port = net != null && net.Naming == NetNaming.Port;
            var fields = new List<InspectorField>
            {
                new InspectorField("name", "NET NAME", wire.NetName ?? (port ? "" : netName), "empty for one of its own"),
            };
            string says = port
                ? $"This wire is on the port {netName}'s net, which keeps the port's name."
                : "Every wire joined to this one is on the same net, and has its name.";
            return new Inspection("WIRE " + netName, says, -1, wire.Id, fields, "name");
        }

        private static Inspection OfPart(Project project, Module m, Part part)
        {
            var fields = new List<InspectorField> { new InspectorField("name", "NAME", part.Name) };
            string unit = project.Settings?.TimeUnit ?? TimeUnits.Default;
            string kind;
            string says;
            string[] quick = { "width" };

            switch (part)
            {
                case GatePart gate:
                    kind = Primitive(gate.Kind);
                    if (!gate.IsTriState && gate.Kind != EngineGate.Not && gate.Kind != EngineGate.Buf)
                        fields.Add(new InspectorField("inputs", "INPUTS", Int(gate.Inputs), "2 to " + GatePart.MaxInputs));
                    fields.Add(new InspectorField("rise", "RISE", Time(gate.Rise, unit), "a time, 0 or more"));
                    fields.Add(new InspectorField("fall", "FALL", Time(gate.Fall, unit), "a time, 0 or more"));
                    if (!gate.IsTriState)
                        fields.Add(new InspectorField("width", "WIDTH", Int(gate.Width), "bits, 1 to " + BoardRules.MaxWidth));
                    says = "Rise is Tpdlh, how long a change to 1 takes to come out; fall is Tpdhl, a change to 0.";
                    if (project.Settings?.Library?.CellFor(gate.Kind, gate.Inputs) is LibraryCell cell)
                        says += $" It is the library's {cell.Name}: a delay changed here is the cell's, and every {cell.Name} follows.";
                    quick = new[] { "rise", "fall" };
                    break;

                case PortPart port:
                    kind = port.Direction == PortDirection.Output ? "OUTPUT" : "INPUT";
                    quick = new[] { "name", "width", "period", "release" };
                    fields.Add(new InspectorField("width", "WIDTH", Int(port.Width), "bits, 1 to " + BoardRules.MaxWidth));
                    says = port.Direction == PortDirection.Output
                        ? "What the circuit gives out, and a row in the waveform."
                        : "What the testbench drives: change its values on the waveform.";
                    if (m.Name == project.Top && port.Direction == PortDirection.Input && project.Stimulus != null)
                    {
                        ClockTrack clock = ClockOf(project.Stimulus, port.Name);
                        if (clock != null)
                        {
                            kind = "CLOCK";
                            fields.Add(new InspectorField("period", "PERIOD", Time(clock.Period, unit), "a time, 2 or more"));
                            says = "A clock, starting " + (clock.StartsHigh ? "high" : "low") + ", that turns over every half period.";
                        }
                        else if (TryRelease(project.Stimulus, port.Name, out long release))
                        {
                            kind = "RESET";
                            fields.Add(new InspectorField("release", "RELEASED AT", Time(release, unit), "a time after 0"));
                            says = "A reset, held at 1 from the start and let go once.";
                        }
                    }

                    fields.Add(new InspectorField("comment", "COMMENT", port.Comment, "a note printed beside it"));
                    break;

                case RegisterPart register:
                    kind = "REGISTER";
                    fields.Add(new InspectorField("width", "WIDTH", Int(register.Width), "bits, 1 to " + BoardRules.MaxWidth));
                    fields.Add(new InspectorField("reset", "RESET", register.HasReset ? "yes" : "no", "yes or no"));
                    fields.Add(new InspectorField("resetvalue", "RESET VALUE", Value(register.ResetValue), "a value, as 4'b0101 or 0"));
                    fields.Add(new InspectorField("start", "STARTS AT", Value(register.Initial), "a value, or empty for x"));
                    fields.Add(new InspectorField("tcq", "CLOCK TO Q", Time(register.Tcq, unit), "a time, 0 or more"));
                    fields.Add(new InspectorField("tsetup", "SETUP", Time(register.Tsetup, unit), "a time, 0 or more"));
                    says = "Takes d on each rising edge of its clock. Clock to Q is Tcq, how long q takes to change after the edge; setup is Tsetup, how long d must be still before it.";
                    quick = new[] { "resetvalue", "start", "tcq", "tsetup" };
                    break;

                case ConstantPart constant:
                    kind = "CONSTANT";
                    fields.Add(new InspectorField("value", "VALUE", Value(constant.Value), "a value, as 4'b0101 or 8'hFF"));
                    says = "Its width is its value's: 4'b0101 is four bits.";
                    quick = new[] { "value" };
                    break;

                case MuxPart mux:
                    kind = "MUX";
                    fields.Add(new InspectorField("inputs", "INPUTS", Int(mux.Inputs), "2, 4 or 8"));
                    fields.Add(new InspectorField("width", "WIDTH", Int(mux.Width), "bits, 1 to " + BoardRules.MaxWidth));
                    says = "Passes the input its select picks.";
                    break;

                case OperatorPart op:
                {
                    kind = "OPERATOR";
                    bool unary = Operators.IsUnary(op.Op);
                    fields.Add(new InspectorField("op", "OPERATOR", Operators.Token(op.Op), unary
                        ? "-, ~, !, &, |, ^, ~&, ~| or ~^"
                        : "+, -, *, /, %, &, |, ^, ~^, <, <=, >, >=, ==, !=, ===, !==, &&, ||, <<, >>, <<< or >>>"));
                    fields.Add(new InspectorField("width", "WIDTH", Int(op.Width), "bits, 1 to " + BoardRules.MaxWidth));
                    if (Operators.BOwnWidth(op.Op))
                        fields.Add(new InspectorField("bwidth", Operators.IsShift(op.Op) ? "AMOUNT WIDTH" : "B WIDTH", Int(op.WidthOfB), "bits, 1 to " + BoardRules.MaxWidth));
                    if (Operators.SignMatters(op.Op))
                        fields.Add(new InspectorField("signed", "SIGNED", op.Signed ? "yes" : "no", "yes or no"));
                    string token = Operators.Token(op.Op);
                    says = unary ? "y = " + token + "a, as the code writes it." : "y = a " + token + " b, as the code writes it.";
                    quick = new[] { "op", "width" };
                    break;
                }

                case SplitterPart splitter:
                    kind = "SPLITTER";
                    fields.Add(new InspectorField("width", "WIDTH", Int(splitter.Width), "bits, 2 to 64"));
                    says = "Takes a bus apart, a bit a pin.";
                    break;

                case JoinerPart joiner:
                    kind = "JOINER";
                    fields.Add(new InspectorField("width", "WIDTH", Int(joiner.Width), "bits, 2 to 64"));
                    says = "Puts bits together into a bus.";
                    break;

                case LampPart lamp:
                    kind = "LAMP";
                    fields.Add(new InspectorField("width", "WIDTH", Int(lamp.Width), "bits, 1 to " + BoardRules.MaxWidth));
                    says = "Shows the value it is given.";
                    break;

                case BlockPart block:
                    kind = "BLOCK";
                    fields.Add(new InspectorField("copies", "COPIES", Int(block.Copies), "1 to " + BlockPart.MaxCopies));
                    // A written block's inside is its code (Part 12d), and stepping in opens it.
                    bool written = project.Find(block.Module)?.IsWritten == true;
                    string inside = written ? "opens its code" : "steps inside it";
                    says = block.Copies > 1
                        ? $"{block.Copies} instances of {block.Module} side by side, each pin a bus with copy i's bits at i: printed as a generate for. A double-click {(written ? "opens its code" : "steps inside the first")}."
                        : written
                            ? $"An instance of {block.Module}, written as SystemVerilog: what it does is its code. A double-click {inside}."
                            : $"An instance of {block.Module}: what it does is that module's. A double-click {inside}.";
                    quick = new[] { "name", "copies" };
                    break;

                default:
                    kind = "PART";
                    says = "";
                    break;
            }

            return new Inspection(kind + " " + part.Name, says, part.Id, -1, fields, quick);
        }

        // -----------------------------------------------------------------
        // Applying
        // -----------------------------------------------------------------

        /// <summary>
        /// Makes what <paramref name="typed"/> -- the text of each field, by key; a field left out is as it was -- says of
        /// what <paramref name="inspection"/> shows, in <paramref name="editor"/>'s document, as one step. Refused with
        /// words for text that is no number, time or value, and with the document's for anything it does not allow.
        /// Done with nothing to do when nothing changed.
        /// </summary>
        public static EditResult Apply(BoardEditor editor, string module, Inspection inspection, IReadOnlyDictionary<string, string> typed)
        {
            if (editor == null || inspection == null)
                return EditResult.Refused("There is nothing to change.");

            Module m = editor.Project.Find(module);
            if (m == null)
                return EditResult.Refused($"There is no module called {module}.");

            string Typed(string key)
            {
                InspectorField field = inspection.Field(key);
                if (field == null)
                    return null;
                return typed != null && typed.TryGetValue(key, out string text) ? (text ?? "").Trim() : field.Text;
            }

            bool Changed(string key)
            {
                InspectorField field = inspection.Field(key);
                return field != null && Typed(key) != field.Text.Trim();
            }

            if (inspection.Wire >= 0)
            {
                if (m.FindWire(inspection.Wire) == null)
                    return EditResult.Refused("That wire is no longer there.");
                if (!Changed("name"))
                    return EditResult.Ok(inspection.Wire);
                string name = Typed("name");
                return editor.NameWire(module, inspection.Wire, name.Length == 0 ? null : name);
            }

            Part part = m.FindPart(inspection.Part);
            if (part == null)
                return EditResult.Refused("That part is no longer there.");

            string unit = editor.Project.Settings?.TimeUnit ?? TimeUnits.Default;
            string why = null;
            Part changed = part;
            GateLibrary retimed = null;
            if (Changed("name"))
                changed = changed with { Name = Typed("name") };

            switch (changed)
            {
                case GatePart gate:
                    if (Changed("inputs") && TryInt(Typed("inputs"), "inputs", ref why, out int inputs))
                        gate = gate with { Inputs = inputs };
                    if (Changed("rise") && TryTime(Typed("rise"), unit, "rise", ref why, out long rise))
                        gate = gate with { Rise = rise };
                    if (Changed("fall") && TryTime(Typed("fall"), unit, "fall", ref why, out long fall))
                        gate = gate with { Fall = fall };
                    if (Changed("width") && TryInt(Typed("width"), "width", ref why, out int gateWidth))
                        gate = gate with { Width = gateWidth };

                    // Under a library (Part 14d) a gate keeps its cell's delays: a delay typed is the cell's, for every gate of
                    // it, and new inputs take the new cell's.
                    if (editor.Project.Settings?.Library is GateLibrary library && library.CellFor(gate.Kind, gate.Inputs) is LibraryCell cell)
                    {
                        if (Changed("rise") || Changed("fall"))
                            retimed = library.Retimed(cell, gate.Rise, gate.Fall);
                        else
                            gate = gate with { Rise = cell.Rise, Fall = cell.Fall };
                    }
                    changed = gate;
                    break;

                case PortPart port:
                    if (Changed("width") && TryInt(Typed("width"), "width", ref why, out int portWidth))
                        port = port with { Width = portWidth };
                    if (Changed("comment"))
                        port = port with { Comment = Typed("comment").Length == 0 ? null : Typed("comment") };
                    changed = port;
                    break;

                case RegisterPart register:
                    if (Changed("width") && TryInt(Typed("width"), "width", ref why, out int registerWidth))
                        register = register with { Width = registerWidth };
                    if (Changed("tcq") && TryTime(Typed("tcq"), unit, "clock to Q", ref why, out long tcq))
                        register = register with { Tcq = tcq };
                    if (Changed("tsetup") && TryTime(Typed("tsetup"), unit, "setup", ref why, out long tsetup))
                        register = register with { Tsetup = tsetup };
                    if (Changed("reset"))
                    {
                        string yes = Typed("reset").ToLowerInvariant();
                        if (yes == "yes" || yes == "y" || yes == "1" || yes == "true")
                            register = register with { HasReset = true };
                        else if (yes == "no" || yes == "n" || yes == "0" || yes == "false")
                            register = register with { HasReset = false };
                        else
                            why ??= "Reset is yes or no.";
                    }

                    // The values are read at the register's width as it will be, so a wider register takes a wider value.
                    if (Changed("resetvalue") || Changed("width"))
                    {
                        string text = Typed("resetvalue");
                        if (text.Length == 0)
                            register = register with { ResetValue = null };
                        else if (TryValue(text, register.Width, "reset value", ref why, out LogicVec reset))
                            register = register with { ResetValue = reset };
                    }

                    if (Changed("start") || Changed("width"))
                    {
                        string text = Typed("start");
                        if (text.Length == 0)
                            register = register with { Initial = null };
                        else if (TryValue(text, register.Width, "starting value", ref why, out LogicVec start))
                            register = register with { Initial = start };
                    }

                    changed = register;
                    break;

                case ConstantPart constant:
                    if (Changed("value"))
                    {
                        LiteralResult read = Literal.Parse(Typed("value").Replace(" ", ""));
                        if (!read.Ok)
                            why ??= "The value: " + read.Error;
                        else
                            constant = constant with { Value = read.FillsContext ? LogicVec.Fill(read.Value[0], constant.Value.Width) : read.Value };
                    }

                    changed = constant;
                    break;

                case MuxPart mux:
                    if (Changed("inputs") && TryInt(Typed("inputs"), "inputs", ref why, out int muxInputs))
                        mux = mux with { Inputs = muxInputs };
                    if (Changed("width") && TryInt(Typed("width"), "width", ref why, out int muxWidth))
                        mux = mux with { Width = muxWidth };
                    changed = mux;
                    break;

                case OperatorPart op:
                    if (Changed("op"))
                    {
                        // A token both kinds have -- &, -, ^ -- keeps the part's own: a reduction stays one, a binary & stays one.
                        if (Operators.TryToken(Typed("op").Trim(), Operators.IsUnary(op.Op), out OperatorKind operatorKind))
                            op = op with
                            {
                                Op = operatorKind,
                                BWidth = Operators.BOwnWidth(operatorKind) ? op.BWidth : 0,
                                Signed = Operators.SignMatters(operatorKind) && op.Signed,
                            };
                        else
                            why ??= "The operator is one SystemVerilog writes between a and b, as + or <, or before a, as ~ or &.";
                    }
                    if (Changed("width") && TryInt(Typed("width"), "width", ref why, out int operatorWidth))
                        op = op with { Width = operatorWidth };
                    if (Changed("bwidth") && TryInt(Typed("bwidth"), "b's width", ref why, out int bWidth))
                        op = op with { BWidth = bWidth };
                    if (Changed("signed"))
                    {
                        string yes = Typed("signed").ToLowerInvariant();
                        if (yes == "yes" || yes == "y" || yes == "1" || yes == "true")
                            op = op with { Signed = true };
                        else if (yes == "no" || yes == "n" || yes == "0" || yes == "false")
                            op = op with { Signed = false };
                        else
                            why ??= "Signed is yes or no.";
                    }
                    changed = op;
                    break;

                case SplitterPart splitter:
                    if (Changed("width") && TryInt(Typed("width"), "width", ref why, out int splitWidth))
                        splitter = splitter with { Width = splitWidth };
                    changed = splitter;
                    break;

                case JoinerPart joiner:
                    if (Changed("width") && TryInt(Typed("width"), "width", ref why, out int joinWidth))
                        joiner = joiner with { Width = joinWidth };
                    changed = joiner;
                    break;

                case LampPart lamp:
                    if (Changed("width") && TryInt(Typed("width"), "width", ref why, out int lampWidth))
                        lamp = lamp with { Width = lampWidth };
                    changed = lamp;
                    break;

                case BlockPart block:
                    if (Changed("copies") && TryInt(Typed("copies"), "copies", ref why, out int copies))
                        block = block with { Copies = copies };
                    changed = block;
                    break;
            }

            long period = 0, release = 0;
            if (Changed("period") && TryTime(Typed("period"), unit, "period", ref why, out period) && period < 2)
                why ??= "A clock's period is at least 2: it is high for half of it and low for the other half.";
            if (Changed("release") && TryTime(Typed("release"), unit, "release", ref why, out release) && release < 1)
                why ??= "A reset is let go after 0, or it is never held.";

            if (why != null)
                return EditResult.Refused(why);

            bool stimulus = Changed("period") || Changed("release");
            if (changed.Equals(part) && !stimulus && retimed == null)
                return EditResult.Ok(part.Id);

            string oldName = part.Name;
            return editor.Group(() =>
            {
                // The cell's new delays first: every gate of it follows, this one too.
                if (retimed != null)
                {
                    EditResult cell = editor.SetLibrary(retimed);
                    if (!cell.Done)
                        return cell;
                }

                if (!changed.Equals(part))
                {
                    EditResult done = editor.Change(module, changed);
                    if (!done.Done)
                        return done;
                }

                if (!stimulus)
                    return EditResult.Ok(part.Id);

                // The port's name as it is now: renamed in this same step, its tracks were renamed with it.
                string port = editor.Project.Find(module)?.FindPart(part.Id)?.Name ?? oldName;
                Stimulus s = editor.Project.Stimulus;
                if (Changed("period"))
                    s = s with { Clocks = WithClock(s.Clocks, port, period) };
                if (Changed("release"))
                    s = s with { Tracks = WithRelease(s.Tracks, port, release) };
                EditResult driven = editor.SetStimulus(s);
                return driven.Done ? EditResult.Ok(part.Id) : driven;
            });
        }

        /// <summary>
        /// Every net of <paramref name="module"/> whose name an edit changed, from <paramref name="before"/> to
        /// <paramref name="after"/>: a net is the same net when it holds the same pin. A part renamed renames the net it
        /// drives, and a port renamed its own, so one field can rename more than the wire it was typed for.
        /// </summary>
        public static List<(string From, string To)> NetsRenamed(Project before, Project after, string module)
        {
            var renamed = new List<(string, string)>();
            Module was = before?.Find(module), now = after?.Find(module);
            if (was == null || now == null)
                return renamed;

            Netlist then = Netlist.Of(before, was), next = Netlist.Of(after, now);
            foreach (Net net in then.Nets)
            {
                if (net.Pins.Count == 0)
                    continue;
                Net same = next.NetOf(net.Pins[0]);
                if (same != null && same.Name != net.Name)
                    renamed.Add((net.Name, same.Name));
            }

            return renamed;
        }

        // -----------------------------------------------------------------
        // Reading what was typed
        // -----------------------------------------------------------------

        private static bool TryInt(string text, string what, ref string why, out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;
            why ??= $"The {what} is a whole number: \"{text}\" is not one.";
            return false;
        }

        private static bool TryTime(string text, string unit, string what, ref string why, out long value)
        {
            if (TimeText.TryParse(text, unit, out value, out string notATime))
            {
                if (value >= 0)
                    return true;
                why ??= $"The {what} cannot be less than 0.";
                return false;
            }

            why ??= $"The {what}: {notATime}";
            return false;
        }

        private static bool TryValue(string text, int width, string what, ref string why, out LogicVec value)
        {
            if (Breakpoints.TryParseValue(text, width, out value, out string notAValue))
                return true;
            why ??= $"The {what}: {notAValue}";
            return false;
        }

        // -----------------------------------------------------------------
        // Writing what is
        // -----------------------------------------------------------------

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Time(long value, string unit) => value.ToString(CultureInfo.InvariantCulture) + " " + unit;

        /// <summary>A value as it would be typed: its width and its bits, as 4'b0101; empty for none.</summary>
        private static string Value(LogicVec? value) =>
            value is LogicVec v ? v.Width.ToString(CultureInfo.InvariantCulture) + "'b" + v.Format(Radix.Binary) : "";

        private static string Primitive(EngineGate kind) => kind.ToString().ToUpperInvariant();

        // -----------------------------------------------------------------
        // The stimulus an input has
        // -----------------------------------------------------------------

        private static ClockTrack ClockOf(Stimulus stimulus, string port)
        {
            foreach (ClockTrack clock in stimulus.Clocks)
                if (clock.Port == port)
                    return clock;
            return null;
        }

        /// <summary>Whether <paramref name="port"/>'s steps are a reset's: 1 from the start, and 0 from one time on.</summary>
        private static bool TryRelease(Stimulus stimulus, string port, out long release)
        {
            release = 0;
            foreach (Track track in stimulus.Tracks)
            {
                if (track.Port != port)
                    continue;
                if (track.Steps.Count != 2 || track.Steps[0].Time != 0 || track.Steps[1].Time <= 0)
                    return false;
                if (!IsBit(track.Steps[0].Value, Logic4.One) || !IsBit(track.Steps[1].Value, Logic4.Zero))
                    return false;
                release = track.Steps[1].Time;
                return true;
            }

            return false;
        }

        private static bool IsBit(LogicVec value, Logic4 bit) => value.Width == 1 && value[0] == bit;

        private static IReadOnlyList<ClockTrack> WithClock(IReadOnlyList<ClockTrack> clocks, string port, long period)
        {
            var list = new List<ClockTrack>(clocks.Count);
            foreach (ClockTrack clock in clocks)
                list.Add(clock.Port == port ? clock with { Period = period } : clock);
            return list;
        }

        private static IReadOnlyList<Track> WithRelease(IReadOnlyList<Track> tracks, string port, long release)
        {
            var list = new List<Track>(tracks.Count);
            foreach (Track track in tracks)
            {
                if (track.Port != port)
                {
                    list.Add(track);
                    continue;
                }

                list.Add(track with { Steps = new[] { track.Steps[0], new Step(release, track.Steps[1].Value) } });
            }

            return list;
        }
    }
}
