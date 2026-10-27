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

        /// <summary>What each output must read at each vector; an x leaves a bit free.</summary>
        public SignalRecord[] outputs = new SignalRecord[0];

        /// <summary>The parts it stocks: a gate of a kind, or a block, each with how many; a count of -1 is any number.</summary>
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

    /// <summary>A part an exercise stocks, as its file holds it: a gate's <c>kind</c> or a <c>block</c>'s module, and how many.</summary>
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

            int vectors = 0;
            foreach (ExerciseSignal signal in inputs)
                vectors = Math.Max(vectors, signal.Values.Count);
            foreach (ExerciseSignal signal in outputs)
                vectors = Math.Max(vectors, signal.Values.Count);
            if (vectors == 0)
                Problem("its test has no vectors.");

            var stock = new List<PartCount>();
            var cells = new List<LibraryCell>();
            long stocked = 0;
            foreach (StockRecord part in record.parts ?? new StockRecord[0])
            {
                if (part == null)
                    continue;
                if (part.count < -1)
                    Problem($"a count of {part.count} is no count.");
                long counted = part.count < 0 ? 8 : part.count;

                if (!string.IsNullOrEmpty(part.block))
                {
                    stock.Add(new PartCount(part.block, part.count));
                    stocked += 4 * counted;
                    continue;
                }

                if (!Enum.TryParse(part.kind, ignoreCase: true, out GateKind kind))
                {
                    Problem($"'{part.kind}' is no gate.");
                    continue;
                }

                int gateInputs = part.inputs > 0 ? part.inputs : kind == GateKind.Not || kind == GateKind.Buf ? 1 : 2;
                if (CellLibrary.CellFor(kind, gateInputs) == null)
                    Problem($"BitSorter has no {kind.ToString().ToUpperInvariant()} of {gateInputs} inputs.");
                else
                    cells.Add(new LibraryCell(kind, gateInputs, record.delay, record.delay));
                stock.Add(new PartCount(PartCount.Of(kind), part.count));
                stocked += counted;
            }

            if (record.delay < 0)
                Problem("a delay cannot be below 0.");
            if (record.cycle < 0 || record.cycle % 2 != 0)
                Problem($"a cycle of {record.cycle} is no cycle: 0 to have it worked out, or an even length.");

            // Long enough for any answer of its stock to settle -- twice through every part it stocks, at least ten -- and
            // past a timing exercise's limit, so one that only just meets it still has its outputs read settled.
            long cycle = record.cycle > 0 ? record.cycle : Math.Max(10, 2 * Math.Max(1, record.delay) * (stocked + 2));
            if (record.settleBy > 0)
                cycle = Math.Max(cycle, 2 * record.settleBy + 2);
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
                Vectors = vectors,
            };
        }

        /// <summary>
        /// <paramref name="text"/> as a value a vector: a character each for a bit, or for a wider signal a binary word each,
        /// spaces between them.
        /// </summary>
        private static List<LogicVec> Values(string text, int width, string what, Action<string> problem)
        {
            var values = new List<LogicVec>();
            if (string.IsNullOrWhiteSpace(text))
                return values;

            string[] words = width == 1 && text.Trim().IndexOf(' ') < 0
                ? Array.ConvertAll(text.Trim().ToCharArray(), c => c.ToString())
                : text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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
            var counts = new List<PartCount>();
            foreach (PartCount count in Stock)
                if (count.Most >= 0)
                    counts.Add(count);
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
                    problems.AddRange(imported.Problems);
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
                        problems.AddRange(imported.Problems);
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
                Settings = (project.Settings ?? new ProjectSettings()) with { DefaultGateDelay = Delay, Library = Library() },
            };

            int before = problems.Count;
            foreach (ExerciseSignal signal in Inputs)
                Expect(project, signal, PortDirection.Input, problems);
            foreach (ExerciseSignal signal in Outputs)
                Expect(project, signal, PortDirection.Output, problems);
            foreach (BoardDiagnostic diagnostic in BoardRules.Check(project))
                if (diagnostic.IsError)
                    problems.Add(Id + ": " + diagnostic.Message);
            return problems.Count > before ? null : project;
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
