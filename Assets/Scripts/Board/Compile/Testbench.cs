using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// A project's stimulus as a testbench in the course's style: the top's ports as <c>logic</c>, the
    /// design as <c>dut</c>, each clock as <c>always #half clk = ~clk;</c>, and every other input set
    /// in one <c>initial</c> block, step by step, ending on <c>$stop</c>. This is the testbench that
    /// runs, not a copy of one -- the engine elaborates it as it elaborates the design.
    /// </summary>
    internal static class Testbench
    {
        public static ModuleDeclaration Build(Project project, HashSet<string> taken) => Build(project, taken, out _);

        /// <summary>The testbench, and what it calls the design it drives: <c>dut</c>, unless a port is called that.</summary>
        public static ModuleDeclaration Build(Project project, HashSet<string> taken, out string dutName)
        {
            Module top = project.TopModule;
            Stimulus stimulus = project.Stimulus;
            IReadOnlyList<ModulePort> ports = WrittenModules.PortsOf(project, top);   // a drawn top's port parts, or a written one's header (Part 13b)

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModulePort port in ports)
                names.Add(port.Name);

            var bench = new ModuleDeclaration
            {
                Name = Unique(top.Name + "_tb", taken),
                Comment = $"drives {top.Name} from the stimulus drawn in BitSorter",
            };

            var clocks = new Dictionary<string, ClockTrack>(StringComparer.Ordinal);
            foreach (ClockTrack clock in stimulus.Clocks)
                clocks[clock.Port] = clock;

            var starts = new Dictionary<string, LogicVec>(StringComparer.Ordinal);
            foreach (Track track in stimulus.Tracks)
                if (track.Start != null)
                    starts[track.Port] = track.Start.Value;

            // An output the design declares as a net -- a bus, a tri (Part 14f), or a written wire -- is a net here too: a
            // simulator that joins a port to what it is connected to, as Icarus does, would make it a variable of two
            // drivers.
            var nets = new HashSet<string>(StringComparer.Ordinal);
            if (!top.IsWritten)
            {
                Netlist netlist = Netlist.Of(project, top);
                foreach (PortPart port in top.Ports)
                    if (port.Direction == PortDirection.Output && BoardRules.IsTri(netlist.NetOf(new PinRef(port.Id, "in")), top))
                        nets.Add(port.Name);
            }
            else
            {
                // As the elaboration made it, however the header or the body declared it (the 14f review: inout io; wire io;).
                foreach (ModulePort port in ports)
                    if (port.Direction != PortDirection.Input && port.IsNet)
                        nets.Add(port.Name);
            }

            // The ports, one declaration a width; a clock has its own, with the level it starts at, and so has an input that
            // holds a value from before time 0 (Part 13c) -- a declaration's value is no event, where a step at 0 is one.
            var byWidth = new Dictionary<int, DataDeclaration>();
            foreach (ModulePort port in ports)
            {
                if (nets.Contains(port.Name))
                {
                    var net = new DataDeclaration { NetType = "wire", Type = Sv.Logic(port.Width) };
                    net.Declarators.Add(new Declarator { Name = port.Name });
                    bench.Items.Add(net);
                    continue;
                }

                LogicVec? start = clocks.TryGetValue(port.Name, out ClockTrack clock)
                    ? clock.SetAtZero ? (LogicVec?)null : LogicVec.FromUInt64(clock.StartsHigh ? 1UL : 0UL, 1)
                    : starts.TryGetValue(port.Name, out LogicVec given) ? given : (LogicVec?)null;
                if (start != null)
                {
                    var own = new DataDeclaration { Type = Sv.Logic(port.Width) };
                    own.Declarators.Add(new Declarator { Name = port.Name, Initializer = Sv.Value(start.Value) });
                    bench.Items.Add(own);
                    continue;
                }

                if (!byWidth.TryGetValue(port.Width, out DataDeclaration declaration))
                {
                    declaration = new DataDeclaration { Type = Sv.Logic(port.Width) };
                    byWidth[port.Width] = declaration;
                    bench.Items.Add(declaration);
                }

                declaration.Declarators.Add(new Declarator { Name = port.Name });
            }

            // The design, every port by name.
            var dut = new ModuleInstance { Name = Unique("dut", names) };
            dutName = dut.Name;
            foreach (ModulePort port in ports)
                dut.Connections.Add(Sv.Connect(port.Name, Sv.Name(port.Name)));
            var instantiation = new ModuleInstantiation { Module = top.Name };
            instantiation.Instances.Add(dut);
            bench.Items.Add(instantiation);

            foreach (ClockTrack clock in stimulus.Clocks)
            {
                var half = new DelayControl();
                half.Values.Add(Sv.Number(clock.Period / 2));
                bench.Items.Add(new ProceduralBlock
                {
                    Kind = ProcessKind.Always,
                    Body = new TimedStatement { Control = half, Body = Sv.Set(Sv.Name(clock.Port), Sv.Not(Sv.Name(clock.Port))) },
                });
            }

            // A self-checking testbench (Part 15b) counts what fails, in a variable no port is called.
            string errors = null;
            if (stimulus.Checks.Count > 0)
            {
                errors = Unique("errors", names);
                var count = new DataDeclaration { Type = new DataType { Keyword = "int" } };
                count.Declarators.Add(new Declarator { Name = errors, Initializer = Sv.Number(0) });
                bench.Items.Add(count);
            }

            bench.Items.Add(new ProceduralBlock { Kind = ProcessKind.Initial, Body = Steps(ports, stimulus, errors) });
            return bench;
        }

        /// <summary>
        /// Every step of every track, in time order and then in port order, each new time after a delay; and every check, at its
        /// moment before the steps of that moment, counted in <paramref name="errors"/> when it fails, with a line saying so.
        /// </summary>
        private static BlockStatement Steps(IReadOnlyList<ModulePort> ports, Stimulus stimulus, string errors = null)
        {
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ports.Count; i++)
                order[ports[i].Name] = i;

            var steps = new List<(long Time, int Order, int Index, string Port, LogicVec Value, bool Late)>();

            // A clock set at time 0 (Part 13c) is set before anything else then, as `initial clk = 0;` above the rest sets it.
            foreach (ClockTrack clock in stimulus.Clocks)
                if (clock.SetAtZero)
                    steps.Add((0, -1, 0, clock.Port, LogicVec.FromUInt64(clock.StartsHigh ? 1UL : 0UL, 1), false));

            foreach (Track track in stimulus.Tracks)
            {
                order.TryGetValue(track.Port, out int place);
                for (int i = 0; i < track.Steps.Count; i++)
                    steps.Add((track.Steps[i].Time, place, i, track.Port, track.Steps[i].Value, track.Steps[i].Late));
            }

            // A check comes before the steps of its moment, which it reads the outputs as they were before.
            var checks = new Dictionary<int, Check>();
            if (errors != null)
                for (int i = 0; i < stimulus.Checks.Count; i++)
                {
                    checks[i] = stimulus.Checks[i];
                    steps.Add((stimulus.Checks[i].Time, -2, i, null, default, false));
                }

            steps.Sort((x, y) => x.Time != y.Time ? x.Time.CompareTo(y.Time) : x.Order != y.Order ? x.Order.CompareTo(y.Order) : x.Index.CompareTo(y.Index));

            var body = new BlockStatement();
            long now = 0;
            foreach ((long time, int place, int index, string port, LogicVec value, bool late) in steps)
            {
                Statement statement = place == -2 ? CheckStatement(checks[index], errors) : Sv.Set(Sv.Name(port), Sv.Value(value), nonBlocking: late);
                body.Statements.Add(time > now ? After(time - now, statement) : statement);
                now = time;
            }

            if (errors != null)
            {
                var summary = new CallStatement { Name = "$display", HasParentheses = true };
                summary.Arguments.Add(new StringLiteral { Value = "%0d of " + stimulus.Checks.Count + " checks failed" });
                summary.Arguments.Add(Sv.Name(errors));
                body.Statements.Add(summary);
            }

            Statement stop = new CallStatement { Name = "$stop" };
            long end = stimulus.StopTime;   // Math.Max(End, now): the last step is the latest of them all
            body.Statements.Add(end > now ? After(end - now, stop) : stop);
            return body;
        }

        /// <summary>
        /// <c>if ((sum ==? 1'b1) !== 1'b1) begin $display("FAIL sum at %0t: %b, wanted 1", $time, sum); errors = errors + 1; end</c>
        /// -- or, for a value with a z, <c>if (sum !== 1'bz)</c> (<see cref="Check"/>). The line is what the grader's says too.
        /// </summary>
        private static Statement CheckStatement(Check check, string errors)
        {
            Expression failed = check.Exact
                ? new BinaryExpression { Operator = "!==", Left = Sv.Name(check.Port), Right = Sv.Value(check.Expected) }
                : new BinaryExpression
                {
                    Operator = "!==",
                    Left = Sv.Paren(new BinaryExpression { Operator = "==?", Left = Sv.Name(check.Port), Right = Sv.Value(check.Expected) }),
                    Right = Sv.Value(LogicVec.FromUInt64(1, 1)),
                };

            var say = new CallStatement { Name = "$display", HasParentheses = true };
            say.Arguments.Add(new StringLiteral { Value = "FAIL " + check.Port + " at %0t: %b, wanted " + check.Expected.Format(Radix.Binary) });
            say.Arguments.Add(new CallExpression { Name = "$time" });
            say.Arguments.Add(Sv.Name(check.Port));

            var count = Sv.Set(Sv.Name(errors), new BinaryExpression { Operator = "+", Left = Sv.Name(errors), Right = Sv.Number(1) });
            var then = new BlockStatement();
            then.Statements.Add(say);
            then.Statements.Add(count);
            return new IfStatement { Condition = failed, Then = then };
        }

        private static Statement After(long delay, Statement then)
        {
            var wait = new DelayControl();
            wait.Values.Add(Sv.Number(delay));
            return new TimedStatement { Control = wait, Body = then };
        }

        private static string Unique(string name, HashSet<string> taken)
        {
            string unique = name;
            for (int n = 2; taken.Contains(unique); n++)
                unique = name + n;
            taken.Add(unique);
            return unique;
        }
    }
}
