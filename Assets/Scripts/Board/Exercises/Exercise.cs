using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// An exercise as its file holds it (Part 15b): plain fields in the shape Unity's <c>JsonUtility</c> reads, and that
    /// <c>System.Text.Json</c> reads under dotnet with its fields included. <see cref="Exercise.From"/> makes the exercise of
    /// it, saying what is wrong with it.
    /// </summary>
    [Serializable]
    public sealed class ExerciseRecord
    {
        /// <summary>What it is known by for good: the 4.x level's file name for the thirty, so progress carries over.</summary>
        public string id;

        public string title;

        /// <summary>Its place in the run, and its chapter's.</summary>
        public int order;
        public int chapter;

        /// <summary>The objective, which may name gates; and the nudge, which may not (CLAUDE.md, "goal and hint").</summary>
        public string goal;
        public string hint;

        public SignalRecord[] inputs = new SignalRecord[0];

        /// <summary>What each output must read at each vector: an x leaves a bit free, but in a value with a z, x means x.</summary>
        public SignalRecord[] outputs = new SignalRecord[0];

        /// <summary>The parts it stocks: a gate of a kind, a register, or a block, each with how many; a count of -1 is any number.</summary>
        public StockRecord[] parts = new StockRecord[0];

        /// <summary>Every gate's rise and fall.</summary>
        public long delay = 1;

        /// <summary>Whether a constant may be drawn: none unless the exercise says, as 4.x stocked none.</summary>
        public bool constants;

        /// <summary>Whether it has a clock, <c>clk</c>, rising half-way through each vector.</summary>
        public bool clock;

        /// <summary>How long each vector has; 0 to have it worked out from the stock (<see cref="Exercise.Cycle"/>).</summary>
        public long cycle;

        /// <summary>A timing exercise's: the slowest path from an input to an output may take no longer. -1 for none.</summary>
        public long settleBy = -1;

        /// <summary>A pipelining exercise's: the design must run at this period, its slowest register-to-register path in it. -1 for none.</summary>
        public long period = -1;

        /// <summary>The circuit it opens on, as structural SystemVerilog of its top module; empty for a blank one.</summary>
        public string start;

        /// <summary>The blocks it stocks, as structural SystemVerilog modules; empty for none.</summary>
        public string blocks;

        /// <summary>Whether it may be answered in code (Part 15e).</summary>
        public bool code;

        /// <summary>
        /// What a register placed in it starts at: <c>0</c> or <c>1</c>, as every 4.x register started at 0 -- or empty for x,
        /// as a flip-flop starts in 5.0 until something resets it (Part 15c).
        /// </summary>
        public string registerStart;
    }

    /// <summary>An input or an output of an exercise, as its file holds it.</summary>
    [Serializable]
    public sealed class SignalRecord
    {
        public string name;
        public int width = 1;

        /// <summary>A character a vector for a bit -- <c>0110</c> -- or, wider, a binary word a vector with spaces between.</summary>
        public string values;
    }

    /// <summary>A part an exercise stocks, as its file holds it: a gate's <c>kind</c> -- or <c>Register</c> -- or a <c>block</c>'s module, and how many.</summary>
    [Serializable]
    public sealed class StockRecord
    {
        public string kind;
        public string block;
        public int count = -1;

        /// <summary>A gate's inputs; 0 for its usual -- one for NOT and BUF, two for the rest.</summary>
        public int inputs;
    }

    /// <summary>An input or an output of an exercise: its name, its width, and its value at each vector.</summary>
    public sealed class ExerciseSignal
    {
        public ExerciseSignal(string name, int width, IReadOnlyList<LogicVec> values)
        {
            Name = name;
            Width = width;
            Values = values;
        }

        public string Name { get; }
        public int Width { get; }
        public IReadOnlyList<LogicVec> Values { get; }
    }

    /// <summary>
    /// An exercise (Part 15b): a circuit to build to a test, with the parts to build it of. BitSorter's thirty levels come
    /// back as these, keeping their ids, beside new ones (Part 15c); the workbench opens one, drives it with its own test,
    /// and grades it (<see cref="ExerciseGrader"/>, Part 15d).
    /// </summary>
    /// <remarks>
    /// **Its test is the migration's timing** (<see cref="MigrationResult"/>): each vector has a <see cref="Cycle"/>, its
    /// inputs change at its start, a clock -- when there is one -- rises half-way, and the outputs are read just before
    /// that edge (<see cref="SampleTime"/>), once everything since the inputs changed has settled. A 4.x board migrated reads
    /// what its level expected at those moments (<c>MigrationTests</c>), so the thirty mean what they meant.
    ///
    /// **It is held to a library of its stock** (<see cref="Library"/>): a cell for each gate kind it stocks, at its delay,
    /// the counts, and no constants unless it gives them.
    /// </remarks>
    public sealed class Exercise
    {
        /// <summary>The clock's name, when there is one.</summary>
        public const string ClockName = "clk";

        /// <summary>The unit an exercise counts time in: the course's, so a gate of delay 1 is 1 ns.</summary>
        public const string TimeUnit = "ns";

        /// <summary>What the library an exercise is held to is called.</summary>
        public const string LibraryName = "EXERCISE";

        private Exercise() { }

        public string Id { get; private set; }
        public string Title { get; private set; }
        public int Order { get; private set; }
        public int Chapter { get; private set; }
        public string Goal { get; private set; }
        public string Hint { get; private set; }
        public IReadOnlyList<ExerciseSignal> Inputs { get; private set; }
        public IReadOnlyList<ExerciseSignal> Outputs { get; private set; }

        /// <summary>The parts it stocks and how many of each: a gate kind's name, or a block's module (<see cref="PartCount"/>).</summary>
        public IReadOnlyList<PartCount> Stock { get; private set; }

        /// <summary>A cell of each gate kind it stocks, at <see cref="Delay"/>.</summary>
        public IReadOnlyList<LibraryCell> Cells { get; private set; }

        public long Delay { get; private set; }
        public bool Constants { get; private set; }
        public bool Clocked { get; private set; }
        public long Cycle { get; private set; }
        public long SettleBy { get; private set; }
        public long Period { get; private set; }
        public string Start { get; private set; }
        public string Blocks { get; private set; }
        public bool Code { get; private set; }

        /// <summary>What a register placed in it starts at; null for x (<see cref="ExerciseRecord.registerStart"/>).</summary>
        public LogicVec? RegisterStart { get; private set; }

        /// <summary>How many vectors the test runs: the longest of the inputs' and the outputs'.</summary>
        public int Vectors { get; private set; }

        /// <summary>The top module's name: the id as SystemVerilog takes a name -- <c>half-adder</c> is <c>half_adder</c>.</summary>
        public string Module => Migrator.NameFrom(Id, "exercise");

        /// <summary>When vector <paramref name="vector"/>'s outputs are read: the last moment before its clock edge.</summary>
        public long SampleTime(int vector) => vector * Cycle + Cycle / 2 - 1;

        /// <summary>The exercise <paramref name="record"/> says, or null with what is wrong with it in <paramref name="problems"/>.</summary>
        public static Exercise From(ExerciseRecord record, List<string> problems)
        {
            if (record == null)
            {
                problems.Add("There is no exercise.");
                return null;
            }

            int before = problems.Count;
            string id = (record.id ?? "").Trim();
            void Problem(string words) => problems.Add((id.Length > 0 ? id + ": " : "") + words);

            if (id.Length == 0)
                Problem("an exercise needs an id.");
            if (string.IsNullOrWhiteSpace(record.title))
                Problem("it needs a title.");

            var names = new HashSet<string>(StringComparer.Ordinal);
            List<ExerciseSignal> Signals(SignalRecord[] records, string kind)
            {
                var signals = new List<ExerciseSignal>();
                foreach (SignalRecord signal in records ?? new SignalRecord[0])
                {
                    if (signal == null)
                        continue;
                    string why = BoardRules.WhyNotAName(signal.name);
                    if (why != null)
                        Problem($"the {kind} '{signal.name}': {why}");
                    else if (!names.Add(signal.name))
                        Problem($"there are two signals called {signal.name}.");
                    else if (record.clock && signal.name == ClockName)
                        Problem($"{ClockName} is the clock's name.");
                    if (signal.width < 1 || signal.width > 64)
                        Problem($"the {kind} {signal.name} is {signal.width} bits wide: 1 to 64, please.");
                    signals.Add(new ExerciseSignal(signal.name, Math.Max(1, signal.width), Values(signal.values, signal.width, $"the {kind} {signal.name}", Problem)));
                }
                if (signals.Count == 0)
                    Problem($"it needs an {kind}.");
                return signals;
            }

            List<ExerciseSignal> inputs = Signals(record.inputs, "input");
            List<ExerciseSignal> outputs = Signals(record.outputs, "output");

            // Every input as long as every other; every output as long, or longer when a register hands on a value after the
            // inputs have run out -- the inputs then hold their last. Anything else is a slip in the file, and a slip that went
            // unchecked would leave vectors the test never asks about (the #85 review).
            int given = inputs.Count > 0 ? inputs[0].Values.Count : 0;
            int vectors = outputs.Count > 0 ? outputs[0].Values.Count : 0;
            foreach (ExerciseSignal signal in inputs)
                if (signal.Values.Count != given)
                    Problem($"the input {signal.Name} has {signal.Values.Count} values, and {inputs[0].Name} {given}: every input has as many.");
            foreach (ExerciseSignal signal in outputs)
                if (signal.Values.Count != vectors)
                    Problem($"the output {signal.Name} has {signal.Values.Count} values, and {outputs[0].Name} {vectors}: every output has as many.");
            if (vectors < given)
                Problem($"its outputs have {vectors} values for {given} vectors of inputs: every vector is checked.");
            if (given == 0)
                Problem("its inputs have no values.");

            // A test that asks nothing is passed by an empty board: a value misspelt in the file would do it.
            bool asks = false;
            foreach (ExerciseSignal output in outputs)
                foreach (LogicVec value in output.Values)
                    asks |= !value.IsAllX;
            if (outputs.Count > 0 && !asks)
                Problem("its test checks nothing: every output's every value is x.");

            var stock = new List<PartCount>();
            var cells = new List<LibraryCell>();
            var stockedParts = new HashSet<string>(StringComparer.Ordinal);
            long stocked = 0;
            foreach (StockRecord part in record.parts ?? new StockRecord[0])
            {
                if (part == null)
                    continue;
                if (part.count < -1)
                    Problem($"a count of {part.count} is no count.");
                long counted = part.count < 0 ? 8 : part.count;

                string key;
                if (!string.IsNullOrEmpty(part.block))
                {
                    key = part.block;
                    stocked += 4 * counted;
                }
                else if (string.Equals(part.kind, PartCount.Register, StringComparison.OrdinalIgnoreCase))
                {
                    key = PartCount.Register;
                    stocked += counted;
                }
                else if (Gate(part.kind, out GateKind kind))
                {
                    key = PartCount.Of(kind);
                    int gateInputs = part.inputs > 0 ? part.inputs : kind == GateKind.Not || kind == GateKind.Buf ? 1 : 2;
                    if (!GateLibrary.CanHold(kind, gateInputs))
                        Problem($"BitSorter has no {kind.ToString().ToUpperInvariant()} of {gateInputs} inputs.");
                    else
                        cells.Add(new LibraryCell(kind, gateInputs, record.delay, record.delay));
                    stocked += counted;
                }
                else
                {
                    Problem($"'{part.kind}' is no gate.");
                    continue;
                }

                // A count is a kind's, of whatever inputs: two of one kind would be two counts of one thing.
                if (!stockedParts.Add(key))
                    Problem($"it stocks {key} twice: one count a part.");
                stock.Add(new PartCount(key, part.count));
            }

            if (record.delay < 0)
                Problem("a delay cannot be below 0.");
            string start = (record.registerStart ?? "").Trim();
            if (start.Length > 0 && start != "0" && start != "1")
                Problem($"a register cannot start at '{start}': 0, 1, or empty for x.");

            // A vector's outputs are read just before half-way (SampleTime), and a gate a delay after its inputs change must
            // have answered by then -- with a moment to spare, or the read and the answer fall in one instant and the read,
            // made first, sees the old value (the #85 review).
            long least = 2 * Math.Max(0, record.delay) + 4;
            if (record.cycle < 0 || record.cycle % 2 != 0 || (record.cycle > 0 && record.cycle < least))
                Problem($"a cycle of {record.cycle} is no cycle: 0 to have it worked out, or an even length of at least {least}.");

            // Long enough for any answer of its stock to settle -- twice through every part it stocks, at least ten -- and
            // past a timing exercise's limit, with the moment to spare, so one that only just meets it still has its outputs
            // read settled.
            long cycle = record.cycle > 0 ? record.cycle : Math.Max(10, 2 * Math.Max(1, record.delay) * (stocked + 2));
            if (record.settleBy > 0)
                cycle = Math.Max(cycle, 2 * record.settleBy + 4);
            cycle = Math.Max(cycle, least);
            cycle += cycle % 2;

            if (problems.Count > before)
                return null;

            return new Exercise
            {
                Id = id,
                Title = record.title.Trim(),
                Order = record.order,
                Chapter = record.chapter,
                Goal = record.goal ?? "",
                Hint = record.hint ?? "",
                Inputs = inputs,
                Outputs = outputs,
                Stock = stock,
                Cells = cells,
                Delay = record.delay,
                Constants = record.constants,
                Clocked = record.clock,
                Cycle = cycle,
                SettleBy = record.settleBy,
                Period = record.period,
                Start = record.start ?? "",
                Blocks = record.blocks ?? "",
                Code = record.code,
                RegisterStart = start == "0" || start == "1" ? LogicVec.FromBinary(start) : (LogicVec?)null,
                Vectors = vectors,
            };
        }

        /// <summary>
        /// A gate kind by its name, as a word -- <c>Bufif1</c> has a digit in it, but no number nor two names with a comma, which
        /// <c>Enum.TryParse</c> takes.
        /// </summary>
        private static bool Gate(string name, out GateKind kind)
        {
            kind = default;
            if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0]))
                return false;
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c))
                    return false;
            return Enum.TryParse(name, ignoreCase: true, out kind) && Enum.IsDefined(typeof(GateKind), kind);
        }

        /// <summary>
        /// <paramref name="text"/> as a value a vector: a character each for a bit, or for a wider signal a binary word each,
        /// spaces, tabs or new lines between them.
        /// </summary>
        private static List<LogicVec> Values(string text, int width, string what, Action<string> problem)
        {
            var values = new List<LogicVec>();
            if (string.IsNullOrWhiteSpace(text))
                return values;

            string[] words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (width == 1 && words.Length == 1)
                words = Array.ConvertAll(words[0].ToCharArray(), c => c.ToString());
            foreach (string word in words)
            {
                bool good = word.Length == width;
                foreach (char c in word)
                    good &= "01xzXZ".IndexOf(c) >= 0;
                if (!good)
                {
                    problem($"{what}'s '{word}' is no value of {width} bit{(width == 1 ? "" : "s")}: 0, 1, x and z, one a bit.");
                    continue;
                }
                values.Add(LogicVec.FromBinary(word.ToLowerInvariant()));
            }
            return values;
        }

        /// <summary>
        /// The library the exercise holds a design to: its cells at its delay, its stock's counts -- a part it stocks any number
        /// of has none -- and constants or none.
        /// </summary>
        public GateLibrary Library()
        {
            // Every part it stocks is counted, any number of it at -1 -- a block stocked so is still counted whole -- and a
            // register, when it stocks none, at 0: an exercise without one is no place for one.
            var counts = new List<PartCount>(Stock);
            if (!counts.Exists(c => c.Part == PartCount.Register))
                counts.Add(new PartCount(PartCount.Register, 0));
            return new GateLibrary { Name = LibraryName, Cells = Cells, NoConstants = !Constants, Counts = counts };
        }

        /// <summary>
        /// The exercise's own test: every input stepped at each vector's start (and held at its last after its values end), the
        /// clock when there is one, and every output checked at each <see cref="SampleTime"/> where a bit of it is asked.
        /// </summary>
        public Stimulus Test()
        {
            var tracks = new List<Track>();
            foreach (ExerciseSignal input in Inputs)
            {
                var steps = new List<Step>();
                for (int v = 0; v < input.Values.Count; v++)
                    if (v == 0 || !LogicVec.CaseEqual(input.Values[v], input.Values[v - 1]).IsTrue)
                        steps.Add(new Step(v * Cycle, input.Values[v]));
                tracks.Add(new Track { Port = input.Name, Steps = steps });
            }

            var checks = new List<Check>();
            for (int v = 0; v < Vectors; v++)
                foreach (ExerciseSignal output in Outputs)
                    if (v < output.Values.Count && !output.Values[v].IsAllX)
                        checks.Add(new Check { Port = output.Name, Time = SampleTime(v), Expected = output.Values[v] });

            var clocks = Clocked
                ? new[] { new ClockTrack { Port = ClockName, Period = Cycle } }   // starting at 0, as the migration's
                : Array.Empty<ClockTrack>();
            return new Stimulus { Tracks = tracks, Clocks = clocks, Checks = checks, End = Vectors * Cycle };
        }

        /// <summary>
        /// The document the exercise opens as: its start circuit, or its ports on a blank board -- inputs down the left, the
        /// clock first, outputs down the right; the blocks it stocks as modules; its test; and its library. Null, saying why,
        /// when its circuit cannot be drawn or breaks a rule.
        /// </summary>
        public Project ToProject(List<string> problems)
        {
            string top = Module;
            Project project;

            if (!string.IsNullOrWhiteSpace(Start))
            {
                SyntaxTree tree = Read(Start + "\n" + Blocks, problems);
                if (tree == null)
                    return null;
                ImportResult imported = BoardImporter.Import(tree, top);
                if (!imported.Succeeded)
                {
                    foreach (string problem in imported.Problems)
                        problems.Add(Id + ": " + problem);
                    return null;
                }
                project = imported.Project;
            }
            else
            {
                var editor = new BoardEditor(Project.New(top));
                int row = 0;
                void Port(string name, int width, PortDirection direction, double x)
                {
                    EditResult added = editor.Add(top, new PortPart { Name = name, Width = width, Direction = direction }, new Placement(x, 2 * row++));
                    if (!added.Done)
                        problems.Add(Id + ": " + added.Refusal);
                }
                if (Clocked)
                    Port(ClockName, 1, PortDirection.Input, 0);
                foreach (ExerciseSignal input in Inputs)
                    Port(input.Name, input.Width, PortDirection.Input, 0);
                row = Clocked ? 1 : 0;
                foreach (ExerciseSignal output in Outputs)
                    Port(output.Name, output.Width, PortDirection.Output, 16);
                project = editor.Project;
            }

            // The blocks the start circuit does not use, each drawn on its own with whatever it is built of.
            if (!string.IsNullOrWhiteSpace(Blocks))
            {
                SyntaxTree tree = Read(Blocks, problems);
                if (tree == null)
                    return null;
                var modules = new List<Module>(project.Modules);
                foreach (ModuleDeclaration block in tree.Modules)
                {
                    if (project.Find(block.Name) != null || modules.Exists(m => m.Name == block.Name))
                        continue;
                    ImportResult imported = BoardImporter.Import(tree, block.Name);
                    if (!imported.Succeeded)
                    {
                        foreach (string problem in imported.Problems)
                            problems.Add(Id + ": " + problem);
                        return null;
                    }
                    foreach (Module module in imported.Project.Modules)
                        if (!modules.Exists(m => m.Name == module.Name))
                            modules.Add(module);
                }
                project = project with { Modules = modules };
            }

            project = project with
            {
                Name = top,
                Top = top,
                Stimulus = Test(),
                Settings = (project.Settings ?? new ProjectSettings()) with { DefaultGateDelay = Delay, TimeUnit = TimeUnit },
            };

            int before = problems.Count;

            // Every block it stocks is a module of the design, and not its top -- or the top would be a block's insides, held
            // to nothing.
            foreach (PartCount part in Stock)
                if (part.Part != PartCount.Register && !Gate(part.Part, out _))
                {
                    if (part.Part == top)
                        problems.Add($"{Id}: the top, {top}, cannot be a block it stocks.");
                    else if (project.Find(part.Part) == null)
                        problems.Add($"{Id}: it stocks the block {part.Part}, which its blocks do not have.");
                }
            if (problems.Count > before)
                return null;

            // Its library, every gate given its cell's delays as setting one gives them -- a start circuit written with plain
            // primitives has none of its own.
            var retiming = new BoardEditor(project);
            EditResult set = retiming.SetLibrary(Library());
            if (!set.Done)
                problems.Add(Id + ": " + set.Refusal);
            project = retiming.Project with
            {
                Settings = retiming.Project.Settings with { Library = Library(), Exercise = Id, RegisterStart = RegisterStart },
            };
            foreach (ExerciseSignal signal in Inputs)
                Expect(project, signal, PortDirection.Input, problems);
            foreach (ExerciseSignal signal in Outputs)
                Expect(project, signal, PortDirection.Output, problems);
            foreach (BoardDiagnostic diagnostic in BoardRules.Check(project))
                if (diagnostic.IsError)
                    problems.Add(Id + ": " + diagnostic.Message);
            return problems.Count > before ? null : project;
        }

        /// <summary>
        /// <paramref name="saved"/> -- the student's own answer, kept from before, or a 4.x board of the level's migrated --
        /// made this exercise's again (Part 15d): its test, its library, its settings, and the blocks it stocks as it gives
        /// them. Null, saying why, when it is not an answer to it: its top is not the exercise's, or lacks a port the test
        /// drives; the exercise then opens fresh.
        /// </summary>
        public Project Adopt(Project saved, List<string> problems)
        {
            var fresh = new List<string>();
            Project opened = ToProject(fresh);
            if (opened == null || saved?.TopModule == null || saved.Top != opened.Top)
            {
                problems.AddRange(fresh);
                return null;
            }

            int before = problems.Count;
            foreach (ExerciseSignal signal in Inputs)
                Expect(saved, signal, PortDirection.Input, problems);
            foreach (ExerciseSignal signal in Outputs)
                Expect(saved, signal, PortDirection.Output, problems);
            if (problems.Count > before)
                return null;

            // The blocks it gives, as it gives them: a stocked module is the exercise's, whatever the saved one became.
            var modules = new List<Module>(saved.Modules);
            foreach (Module given in opened.Modules)
            {
                if (given.Name == opened.Top)
                    continue;
                int at = modules.FindIndex(m => m.Name == given.Name);
                if (at >= 0)
                    modules[at] = given;
                else
                    modules.Add(given);
            }

            Project adopted = saved with
            {
                Modules = modules,
                Stimulus = opened.Stimulus,
                Bench = null,
                Settings = opened.Settings with { Library = null, Exercise = null },
            };

            // Every gate given its cell's delays, as the exercise's own opening does; where that is refused, as it was kept,
            // and CHECK says why.
            var retiming = new BoardEditor(adopted);
            if (retiming.SetLibrary(Library()).Done)
                adopted = retiming.Project;
            return adopted with { Settings = opened.Settings };
        }

        /// <summary>
        /// <paramref name="project"/>'s top written as code (Part 15e): the drawing as it prints -- every gate a cell of the
        /// course's, wired as drawn -- or, where it does not print, its ports; under a header saying the goal and what it may
        /// be built of. It keeps its ids' count, so a top drawn again never gives an id twice.
        /// </summary>
        public Module Written(Project project)
        {
            Module top = project.TopModule;
            string body = PrintedTop(project) ?? Header(top);
            return new Module { Name = top.Name, Comment = top.Comment, Source = Brief(project) + body, NextId = top.NextId };
        }

        /// <summary>
        /// <paramref name="project"/>'s top drawn again (Part 15e): the exercise's own, as it opens -- its ports, or its start
        /// circuit -- counting its ids on from where the written one had got to.
        /// </summary>
        public Module Drawn(Project project, List<string> problems)
        {
            Project opened = ToProject(problems);
            Module top = opened?.TopModule;
            if (top == null)
                return null;
            return top with { NextId = Math.Max(top.NextId, project.TopModule?.NextId ?? 1) };
        }

        /// <summary>The comment a written answer opens with: the exercise, its goal, and what it may be built of.</summary>
        private string Brief(Project project)
        {
            var text = new System.Text.StringBuilder();
            text.Append("// ").Append(Title).Append(", answered in code.\n");
            foreach (string line in Wrapped(Goal, 96))
                text.Append("// ").Append(line).Append('\n');
            text.Append("//\n");

            var stock = new List<string>();
            foreach (PartCount count in Stock)
                if (count.Most != 0)
                    stock.Add((count.Most < 0 ? "any number of " : count.Most.ToString(CultureInfo.InvariantCulture) + " ") + count.Said);
            foreach (string line in Wrapped("Gates only, as the course writes them: a primitive (xor #1 g1 (y, a, b);) or a cell " +
                "(XOR2 g1 (.A(a), .B(b), .Z(y));), never an operator. It gives " + string.Join(", ", stock) + ".", 96))
                text.Append("// ").Append(line).Append('\n');
            if (RegisterStart is LogicVec start)
                text.Append("// A register is an always_ff of plain writes, and starts at ").Append(start.Format(Radix.Binary))
                    .Append(" here: logic q = 1'b").Append(start.Format(Radix.Binary)).Append(";\n");
            text.Append('\n');
            return text.ToString();
        }

        /// <summary>The drawn top as it prints, from its <c>module</c> on; null where the drawing does not print.</summary>
        private static string PrintedTop(Project project)
        {
            foreach (PrintedSource printed in BoardPrinter.Print(project))
            {
                string text = printed.Text ?? "";
                int at = text.StartsWith("module " + project.Top, StringComparison.Ordinal) ? 0 : text.IndexOf("\nmodule " + project.Top + " ", StringComparison.Ordinal);
                if (at < 0)
                    at = text.IndexOf("\nmodule " + project.Top + "(", StringComparison.Ordinal);
                if (at >= 0)
                    return text.Substring(at == 0 ? 0 : at + 1);
            }
            return null;
        }

        /// <summary>A module of <paramref name="top"/>'s ports and nothing in it.</summary>
        private static string Header(Module top)
        {
            var text = new System.Text.StringBuilder();
            text.Append("module ").Append(top.Name).Append(" (\n");
            var ports = new List<PortPart>(top.Ports);
            for (int i = 0; i < ports.Count; i++)
            {
                PortPart port = ports[i];
                text.Append("    ").Append(port.Direction == PortDirection.Input ? "input" : port.Direction == PortDirection.Output ? "output" : "inout")
                    .Append(" logic ").Append(port.Width > 1 ? "[" + (port.Width - 1).ToString(CultureInfo.InvariantCulture) + ":0] " : "")
                    .Append(port.Name).Append(i < ports.Count - 1 ? ",\n" : "\n");
            }
            text.Append(");\n\nendmodule\n");
            return text.ToString();
        }

        private static IEnumerable<string> Wrapped(string text, int width)
        {
            string line = "";
            foreach (string word in (text ?? "").Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line;
                    line = "";
                }
                line = line.Length == 0 ? word : line + " " + word;
            }
            if (line.Length > 0)
                yield return line;
        }

        /// <summary>The top has <paramref name="signal"/> as a port of its width and way, as a start circuit must.</summary>
        private void Expect(Project project, ExerciseSignal signal, PortDirection direction, List<string> problems)
        {
            foreach (PortPart port in project.TopModule.Ports)
                if (port.Name == signal.Name)
                {
                    if (port.Direction != direction || port.Width != signal.Width)
                        problems.Add($"{Id}: {signal.Name} is not the {signal.Width}-bit {(direction == PortDirection.Input ? "input" : "output")} the test drives.");
                    return;
                }
            problems.Add($"{Id}: the circuit has no port {signal.Name}.");
        }

        private SyntaxTree Read(string text, List<string> problems)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, Id + ".sv");
            if (!tree.HasErrors)
                return tree;
            foreach (Diagnostic diagnostic in tree.Diagnostics)
                problems.Add(Id + ": " + diagnostic);
            return null;
        }

        public override string ToString() => Id + " (" + Title + ")";

        /// <summary>A count as the exercise says it: <c>2 AND</c>, <c>any number of XOR</c>.</summary>
        public static string Said(PartCount count) =>
            (count.Most < 0 ? "any number of " : count.Most.ToString(CultureInfo.InvariantCulture) + " ") + count.Said;
    }
}
