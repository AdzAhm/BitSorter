using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>What CONVERT made of a written testbench: the project driven by a stimulus instead, or why not.</summary>
    public sealed class Conversion
    {
        internal Conversion(Project project, string refusal, IReadOnlyList<string> lost)
        {
            Project = project;
            Refusal = refusal;
            Lost = lost;
        }

        /// <summary>The project with the stimulus in place of the testbench; null when it was refused.</summary>
        public Project Project { get; }

        /// <summary>Why it cannot be converted, or null.</summary>
        public string Refusal { get; }

        /// <summary>What the testbench did that a stimulus does not -- printing, checking, files -- each a line to say before converting.</summary>
        public IReadOnlyList<string> Lost { get; }

        public bool Succeeded => Project != null;
    }

    /// <summary>
    /// CONVERT (Part 13c): a written testbench (<see cref="Project.Bench"/>) turned into the stimulus, so its inputs can be
    /// edited on the waves. It records what the testbench actually drove: the project is run as written to its end, every
    /// input of the design is read back change by change, and each becomes a track -- or a clock, when it toggles at one
    /// steady period from time 0 to the end. Because it reads the run rather than the text, it takes any testbench, its
    /// loops, tasks, <c>$random</c> and waits included, and the design sees the same inputs at the same times.
    /// </summary>
    /// <remarks>
    /// What the testbench did beyond driving the inputs -- what it printed, what it checked, the files it wrote -- has no
    /// place in a stimulus, and is listed in <see cref="Conversion.Lost"/> for the confirmation to say. The testbench module
    /// is taken out of the project; the modules it alone used stay, as any module may.
    /// </remarks>
    public static class BenchConverter
    {
        /// <summary>How many toggles after time 0 make an input a clock rather than a few pulses.</summary>
        public const int ClockToggles = 4;

        private static readonly Regex SystemTask = new Regex(@"\$(display|write|monitor|strobe|info|warning|error|fatal|fopen|fclose|fdisplay|fwrite|fstrobe|fmonitor|writememh|writememb|dumpfile|dumpvars)\b",
            RegexOptions.CultureInvariant);

        private static readonly Regex Assertion = new Regex(@"\bassert\b", RegexOptions.CultureInvariant);

        public static Conversion Convert(Project project)
        {
            Conversion Refused(string why) => new Conversion(null, why, Array.Empty<string>());

            if (project?.Bench == null)
                return Refused("There is no testbench to convert: the stimulus drives the inputs already.");
            Module bench = project.Find(project.Bench);
            Module top = project.TopModule;
            if (bench == null || top == null)
                return Refused("The project's testbench or design is missing.");

            CompiledProject compiled = BoardCompiler.Compile(project);
            if (!compiled.Succeeded)
                return Refused("The project does not compile, so its testbench cannot be run to see what it drives: " + First(compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
                return Refused("The project does not elaborate, so its testbench cannot be run to see what it drives.");

            // The testbench's instance of the design, whose connections say what drives each input.
            ModuleInstantiation instantiation = InstantiationOf(WrittenModules.Read(project, bench).Declaration, top.Name);
            if (instantiation == null || instantiation.Instances.Count == 0)
                return Refused($"The testbench's instance of {top.Name} could not be found.");
            if (instantiation.Parameters.Count > 0)
                return Refused($"The testbench gives {top.Name} parameters, #(...), which a stimulus cannot yet: give them as the module's own defaults, then CONVERT.");
            ModuleInstance instance = instantiation.Instances[0];

            // Run as written, to where the timeline ends.
            long end = project.Stimulus?.StopTime ?? 0;
            Design design = elaboration.Design;
            var sim = new Simulator(design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            sim.RunTo(end);

            var tracks = new List<Track>();
            var clocks = new List<ClockTrack>();
            IReadOnlyList<ModulePort> ports = WrittenModules.PortsOf(project, top);

            // A stimulus drives inputs; what a testbench drives onto an inout would be lost without a word (the 14f review).
            foreach (ModulePort port in ports)
                if (port.Direction == PortDirection.Inout)
                    return Refused($"{top.Name}'s {port.Name} is an inout, which a stimulus cannot drive: the testbench stays as written.");
            for (int index = 0; index < ports.Count; index++)
            {
                ModulePort port = ports[index];
                if (port.Direction != PortDirection.Input)
                    continue;

                LogicVec? start = null;
                var steps = new List<Step>();
                string driver = DriverOf(instance, index, port.Name);
                if (driver != null && design.TryFind(project.Bench + "." + driver, out SignalId own))
                {
                    // The testbench's own signal, in its own rounds: the design's port lags them, and shows a declaration's
                    // value as a change at time 0. What it held before anything ran is a declaration's, with no event; a
                    // change after the first round of its moment -- a nonblocking assignment, or one made in answer to an
                    // edge -- is made late, so an edge at the same time sees the value from before, as it did here.
                    LogicVec first = sim.History.Change(own, 0).Value;
                    if (!IsAllX(first))
                        start = Sized(first, port.Width);
                    for (int i = 1; i < sim.History.ChangeCount(own); i++)
                    {
                        ValueChange change = sim.History.Change(own, i);
                        if (change.Time > end)
                            break;
                        steps.Add(new Step(change.Time, Sized(change.Value, port.Width), late: change.Delta > FirstRound(sim, change.Time)));
                    }
                }
                else if (design.TryFind(compiled.DesignPath + "." + port.Name, out SignalId inside))
                {
                    // Connected to something that is no plain name -- a constant, an expression: the design's port, as it settled.
                    foreach (ValueChange change in sim.History.SettledChanges(inside))
                    {
                        if (change.Time > end)
                            break;
                        if (steps.Count == 0 && change.Time == 0 && IsAllX(change.Value))
                            continue;
                        steps.Add(new Step(change.Time, Sized(change.Value, port.Width)));
                    }
                }
                else
                {
                    return Refused($"The design's input {port.Name} could not be found in the run.");
                }

                ClockTrack clock = port.Width == 1 ? ClockOf(port.Name, start, steps, end) : null;
                if (clock != null)
                    clocks.Add(clock);
                else if (steps.Count > 0 || start != null)
                    tracks.Add(new Track { Port = port.Name, Steps = steps, Start = start });
            }

            var modules = new List<Module>(project.Modules.Count);
            foreach (Module module in project.Modules)
                if (module != bench)
                    modules.Add(module);

            Project converted = project with
            {
                Modules = modules,
                Bench = null,
                Stimulus = new Stimulus { Tracks = tracks, Clocks = clocks, End = Math.Max(1, end) },
            };
            return new Conversion(converted, null, LostIn(bench));
        }

        /// <summary>
        /// A clock, when the input starts at 0 or 1 -- from its declaration, or set at time 0 -- and then toggles in the first
        /// round of every half period to within half a period of the end, at least <see cref="ClockToggles"/> times; null
        /// otherwise.
        /// </summary>
        private static ClockTrack ClockOf(string port, LogicVec? start, List<Step> steps, long end)
        {
            bool setAtZero = start == null;
            int first = 0;
            LogicVec level;
            if (setAtZero)
            {
                if (steps.Count == 0 || steps[0].Time != 0 || steps[0].Late || !IsBit(steps[0].Value))
                    return null;
                level = steps[0].Value;
                first = 1;
            }
            else
            {
                if (!IsBit(start.Value))
                    return null;
                level = start.Value;
            }

            int toggles = steps.Count - first;
            if (toggles < ClockToggles)
                return null;

            long half = steps[first].Time;
            if (half <= 0)
                return null;

            LogicVec previous = level;
            for (int i = first; i < steps.Count; i++)
            {
                Step step = steps[i];
                if (step.Time != (i - first + 1) * half || step.Late || !IsBit(step.Value) || step.Value[0] == previous[0])
                    return null;
                previous = step.Value;
            }

            if (steps[steps.Count - 1].Time + half <= end)
                return null;   // it stopped toggling before the end, which a clock in the stimulus would not

            return new ClockTrack { Port = port, Period = 2 * half, StartsHigh = level[0] == Logic4.One, SetAtZero = setAtZero };
        }

        /// <summary>
        /// The round of a moment a process's own assignments are made in: the first, but at time 0 the one the processes
        /// started in, after what has no delay settled from its starting values.
        /// </summary>
        private static int FirstRound(Simulator sim, long time) => time == 0 ? sim.ProcessesStartedIn : 0;

        /// <summary>The first instantiation of <paramref name="module"/> in <paramref name="declaration"/>, a generate region's included.</summary>
        private static ModuleInstantiation InstantiationOf(ModuleDeclaration declaration, string module)
        {
            ModuleInstantiation Find(IEnumerable<ModuleItem> items)
            {
                foreach (ModuleItem item in items)
                {
                    if (item is ModuleInstantiation instantiation && instantiation.Module == module)
                        return instantiation;
                    ModuleInstantiation inside = item switch
                    {
                        GenerateRegion region => Find(region.Items),
                        GenerateBlock block => Find(block.Items),
                        _ => null,
                    };
                    if (inside != null)
                        return inside;
                }

                return null;
            }

            return declaration == null ? null : Find(declaration.Items);
        }

        /// <summary>
        /// The testbench's signal an input of the design is connected to, when it is a plain name -- <c>.a(a)</c>, <c>.a</c>,
        /// <c>.*</c>, or by order -- and null when it is anything else.
        /// </summary>
        private static string DriverOf(ModuleInstance instance, int index, string port)
        {
            int positional = 0;
            foreach (PortConnection connection in instance.Connections)
            {
                if (connection.IsWildcard)
                    continue;
                bool named = connection.Name != null;
                if (named ? connection.Name != port : positional++ != index)
                    continue;
                if (connection.IsShorthand)
                    return port;
                return connection.Value is NameExpression name && name.Path.Count == 1 ? name.Name : null;
            }

            // Not connected by name or place: .* connects it to the testbench's signal of the same name.
            foreach (PortConnection connection in instance.Connections)
                if (connection.IsWildcard)
                    return port;
            return null;
        }

        /// <summary>What the testbench does that a stimulus cannot, each as a line.</summary>
        private static IReadOnlyList<string> LostIn(Module bench)
        {
            var lost = new List<string>();
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Match task in SystemTask.Matches(bench.Source ?? ""))
            {
                counts.TryGetValue(task.Value, out int count);
                counts[task.Value] = count + 1;
            }

            foreach (KeyValuePair<string, int> task in counts)
                lost.Add($"{task.Key}, {Places(task.Value)}");

            int assertions = Assertion.Matches(bench.Source ?? "").Count;
            if (assertions > 0)
                lost.Add($"assert, {Places(assertions)}");
            return lost;
        }

        private static string Places(int n) => n == 1 ? "in one place" : "in " + n.ToString(CultureInfo.InvariantCulture) + " places";

        private static bool IsAllX(LogicVec value)
        {
            for (int i = 0; i < value.Width; i++)
                if (value[i] != Logic4.X)
                    return false;
            return true;
        }

        private static bool IsBit(LogicVec value) => value.Width == 1 && (value[0] == Logic4.Zero || value[0] == Logic4.One);

        /// <summary>A value as wide as the port it drives.</summary>
        private static LogicVec Sized(LogicVec value, int width) => value.Width == width ? value : value.Resize(width);

        private static string First(IReadOnlyList<BoardDiagnostic> diagnostics)
        {
            foreach (BoardDiagnostic d in diagnostics)
                if (d.IsError)
                    return d.Message;
            return "";
        }
    }
}
