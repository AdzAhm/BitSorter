using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.ViewModel
{
    /// <summary>A column of the INPUTS table: an input of the top, or, in an exercise, an output its test checks.</summary>
    public sealed class InputColumn
    {
        internal InputColumn(string port, int width, bool input, long clock, Radix radix, bool chosen)
        {
            Port = port;
            Width = width;
            IsInput = input;
            ClockPeriod = clock;
            Radix = radix;
            Chosen = chosen;
        }

        public string Port { get; }
        public int Width { get; }

        /// <summary>An input; false for an output an exercise's test checks, its values the ones it wants.</summary>
        public bool IsInput { get; }

        /// <summary>Its period when it is a clock, which has no steps of its own; 0 for none.</summary>
        public long ClockPeriod { get; }

        public bool IsClock => ClockPeriod > 0;

        /// <summary>The radix its values are written and read in.</summary>
        public Radix Radix { get; }

        /// <summary>Whether it is chosen, for ALL COMBINATIONS, COUNT and CLOCK.</summary>
        public bool Chosen { get; }

        /// <summary>What its head says: its name, its range when it is a bus.</summary>
        public string Name => Width > 1 ? Port + "[" + (Width - 1).ToString(CultureInfo.InvariantCulture) + ":0]" : Port;

        /// <summary>What its head says under its name: its radix, or that it is a clock, or the value an output must have.</summary>
        public string Kind => IsClock ? "CLOCK " + ClockPeriod.ToString(CultureInfo.InvariantCulture)
            : !IsInput ? "EXPECTED"
            : Radix == Radix.Hex ? "HEX" : Radix == Radix.Unsigned ? "DEC" : "BIN";
    }

    /// <summary>A row of the INPUTS table: a moment, and what each column changes to then -- empty for nothing.</summary>
    public sealed class InputRow
    {
        internal InputRow(long time, string[] cells, bool pending)
        {
            Time = time;
            Cells = cells;
            Pending = pending;
        }

        public long Time { get; }
        public IReadOnlyList<string> Cells { get; }

        /// <summary>Added with + ROW and not given a value yet: nothing in the document says it is there.</summary>
        public bool Pending { get; }
    }

    /// <summary>
    /// The INPUTS tab's table (Part 15g): a vector table of what the testbench gives the top's inputs, made from the
    /// stimulus and giving edits of it back. A column per input, in the radix it is written in; a row per moment something
    /// changes, each cell what an input changes to then, or empty for nothing. Ahmad asked for a way to make input sequences
    /// and vectors without code, for buses as well as bits (2026-10-28): a double-click on the waves flipped one bit or asked
    /// for one value, which was all there was.
    /// </summary>
    /// <remarks>
    /// **The document is the stimulus; the rest is the table's own** -- each column's radix, what is chosen, the STEP that +
    /// ROW, ALL COMBINATIONS and COUNT space their rows by, and a row added and not filled yet, which nothing in the stimulus
    /// says is there. <see cref="Of"/> carries them over from the table before. Each edit gives back the stimulus to commit
    /// and the moment the run changes from; the session commits it as one undo step and replays it, as SET does.
    ///
    /// **In an exercise it is read only**, and shows the exercise's own test: its rows, and in a column of each output it
    /// checks, the value it wants. An edit is refused in the document's own words -- the exercise's test drives the inputs
    /// -- as SET is; and with a written testbench it says the testbench drives them.
    ///
    /// **Under a written testbench it shows what the testbench applies** (Part 15h), read only: the testbench's stimulus is
    /// its code, so the table was its column heads over nothing, and Ahmad "couldn't do anything in inputs" (2026-10-28). The
    /// rows are what CONVERT would record (<see cref="BenchConverter"/>, a run of the testbench), so what is shown is what
    /// CONVERT gives, and the view offers CONVERT beside it.
    /// </remarks>
    public sealed class InputTable
    {
        /// <summary>How far apart + ROW, ALL COMBINATIONS and COUNT put their rows, until STEP says otherwise.</summary>
        public const long DefaultStep = 10;

        /// <summary>
        /// The most rows a testbench's table shows (the 15h review): an exhaustive testbench applies tens of thousands of
        /// vectors, every one a row, and the table is read one row a notch of the wheel. CONVERT gives every one.
        /// </summary>
        public const int MostAppliedRows = 2000;

        private readonly Dictionary<string, Radix> _radixes;
        private readonly HashSet<string> _chosen;
        private readonly List<long> _pending;
        private readonly Stimulus _stimulus;

        private InputTable(Stimulus stimulus, List<InputColumn> columns, List<InputRow> rows, string why, long step,
            Dictionary<string, Radix> radixes, HashSet<string> chosen, List<long> pending, string appliedBy, string cannotConvert,
            int leftOut)
        {
            _stimulus = stimulus;
            Columns = columns;
            Rows = rows;
            Why = why;
            AppliedBy = appliedBy;
            CannotConvert = cannotConvert;
            LeftOut = leftOut;
            Step = step;
            _radixes = radixes;
            _chosen = chosen;
            _pending = pending;
        }

        public IReadOnlyList<InputColumn> Columns { get; }
        public IReadOnlyList<InputRow> Rows { get; }

        /// <summary>Why it cannot be changed -- an exercise's test, or a written testbench, drives the inputs -- or null when it can.</summary>
        public string Why { get; }

        public bool ReadOnly => Why != null;

        /// <summary>The testbench whose values the rows are, read from a run of it; null when the rows are the stimulus's own.</summary>
        public string AppliedBy { get; }

        /// <summary>Why a written testbench driving the inputs cannot be converted, and so has no rows to show; null otherwise.</summary>
        public string CannotConvert { get; }

        /// <summary>How many of a testbench's moments are past <see cref="MostAppliedRows"/>, and not shown.</summary>
        public int LeftOut { get; }

        /// <summary>How far apart + ROW, ALL COMBINATIONS and COUNT put their rows.</summary>
        public long Step { get; }

        /// <summary>When the run is meant to end.</summary>
        public long End => _stimulus.End;

        /// <summary>The table of <paramref name="project"/>'s stimulus, keeping what <paramref name="before"/> had of its own.</summary>
        public static InputTable Of(Project project, InputTable before = null)
        {
            Stimulus stimulus = project.Stimulus ?? new Stimulus();
            string why = project.Settings?.Exercise != null ? BoardEditor.ExerciseDrivesTheInputs
                : project.Bench != null ? BoardRules.BenchDrivesTheInputs(project) : null;

            // A written testbench's stimulus is its code: what it applies, as CONVERT would record it from a run of it.
            string appliedBy = null, cannotConvert = null;
            if (project.Settings?.Exercise == null && project.Bench != null)
            {
                Conversion conversion = BenchConverter.Convert(project);
                if (conversion.Succeeded)
                {
                    stimulus = Started(conversion.Project.Stimulus);
                    appliedBy = project.Bench;
                }
                else
                {
                    cannotConvert = conversion.Refusal;
                }
            }

            var radixes = before != null ? new Dictionary<string, Radix>(before._radixes) : new Dictionary<string, Radix>(StringComparer.Ordinal);
            var chosen = before != null ? new HashSet<string>(before._chosen) : new HashSet<string>(StringComparer.Ordinal);
            // A row added and not filled yet is the editable table's: one carried into a read-only table is a blank row nobody
            // can fill (the 15h review: CONVERT a testbench, + ROW, Ctrl+Z).
            var pending = before != null && why == null ? new List<long>(before._pending) : new List<long>();
            long step = before?.Step ?? DefaultStep;

            var columns = new List<InputColumn>();
            Module top = project.TopModule;
            if (top != null)
            {
                foreach (ModulePort port in WrittenModules.PortsOf(project, top))
                {
                    bool input = port.Direction == PortDirection.Input;
                    if (!input && !Checked(stimulus, port.Name))
                        continue;
                    if (!radixes.TryGetValue(port.Name, out Radix radix))
                        radix = port.Width > 4 ? Radix.Hex : Radix.Binary;
                    columns.Add(new InputColumn(port.Name, port.Width, input, input ? ClockOf(stimulus, port.Name) : 0, radix,
                        input && chosen.Contains(port.Name)));
                }
            }

            // Every moment something changes, an exercise's checks among them, and the rows added and not filled yet: sorted
            // once, since a testbench's stimulus may run to a great many, and a testbench's held to its most.
            var every = new List<long>();
            foreach (Track track in stimulus.Tracks)
                foreach (Step each in track.Steps)
                    every.Add(each.Time);
            foreach (Check check in stimulus.Checks)
                every.Add(check.Time);
            every.Sort();
            var times = new SortedSet<long>();
            int moments = 0;
            for (int i = 0; i < every.Count; i++)
            {
                if (i > 0 && every[i] == every[i - 1])
                    continue;
                moments++;
                if (appliedBy == null || times.Count < MostAppliedRows)
                    times.Add(every[i]);
            }
            int leftOut = moments - times.Count;
            pending.RemoveAll(time => times.Contains(time));
            if (why == null && times.Count == 0 && pending.Count == 0)
                pending.Add(0);   // somewhere to start typing
            foreach (long time in pending)
                times.Add(time);

            return new InputTable(stimulus, columns, RowsOf(stimulus, columns, times, pending), why, step, radixes, chosen, pending,
                appliedBy, cannotConvert, leftOut);
        }

        /// <summary>
        /// The top's inputs nothing drives: no testbench of the project's own, and no step, start or clock in the stimulus --
        /// what a design imported without its testbench is left with, each reading z and what it feeds x. The import names them,
        /// and how to drive them (Part 15g): Ahmad's import of 2026-10-28 showed grey dashes and red hatching, and nothing said why.
        /// </summary>
        public static IReadOnlyList<string> Undriven(Project project)
        {
            var undriven = new List<string>();
            Module top = project.TopModule;
            if (project.Bench != null || top == null)
                return undriven;

            Stimulus stimulus = project.Stimulus ?? new Stimulus();
            foreach (ModulePort port in WrittenModules.PortsOf(project, top))
            {
                if (port.Direction != PortDirection.Input || ClockOf(stimulus, port.Name) > 0)
                    continue;
                bool driven = false;
                foreach (Track track in stimulus.Tracks)
                    driven |= string.Equals(track.Port, port.Name, StringComparison.Ordinal) && (track.Steps.Count > 0 || track.Start.HasValue);
                if (!driven)
                    undriven.Add(port.Name);
            }

            return undriven;
        }

        /// <summary>A value as a cell writes it: its digits in <paramref name="radix"/>, a bit as 0, 1, x or z.</summary>
        public static string Text(LogicVec value, Radix radix) =>
            value.Width == 1 ? value.Format(Radix.Binary) : !value.IsKnown && radix == Radix.Unsigned ? value.Format(Radix.Hex) : value.Format(radix);

        /// <summary>
        /// What is typed in a column of <paramref name="radix"/>, read as a value <paramref name="width"/> bits wide: digits in
        /// the column's radix, or any literal -- <c>5</c>, <c>'hA</c>, <c>4'b0101</c> -- or a bare x or z.
        /// </summary>
        public static bool TryRead(string text, int width, Radix radix, out LogicVec value, out string error)
        {
            string shown = (text ?? "").Trim();
            string typed = shown.Replace(" ", "").Replace("_", "");
            bool literal = typed.IndexOf('\'') >= 0 || (typed.Length == 1 && "xXzZ".IndexOf(typed[0]) >= 0);
            if (!literal && typed.Length > 0)
            {
                // 0x1F in a hex column is 1F, as a programmer writes it -- not a digit x inside the value (the 15g-4 review).
                if (radix == Radix.Hex && typed.Length > 2 && typed[0] == '0' && (typed[1] == 'x' || typed[1] == 'X'))
                    typed = typed.Substring(2);
                typed = radix == Radix.Hex ? "'h" + typed : radix == Radix.Binary ? "'b" + typed : typed;
            }
            if (Breakpoints.TryParseValue(typed, width, out value, out error))
                return true;
            error = error?.Replace(typed, shown);   // what was typed, not the literal it was made into
            return false;
        }

        // -----------------------------------------------------------------
        // The table's own
        // -----------------------------------------------------------------

        /// <summary>A row a <see cref="Step"/> after the last, or at 0, to type into: the table's until it is given a value.</summary>
        public bool TryAddRow(out InputTable table, out string refusal)
        {
            table = this;
            refusal = null;
            long last = -Step;
            foreach (InputRow row in Rows)
                last = Math.Max(last, row.Time);
            if (last > StimulusEdits.MaxTime - Step)
            {
                refusal = $"The table goes as far as {StimulusEdits.MaxTime}: a row a step after the last would be past it.";
                return false;
            }
            var pending = new List<long>(_pending) { last + Step };
            table = With(pending: pending);
            return true;
        }

        /// <summary>STEP typed: a whole number of the project's unit, 1 or more.</summary>
        public bool TrySetStep(string text, out InputTable table, out string refusal)
        {
            table = this;
            if (!TryTime(text, out long step, out refusal) || step < 1)
            {
                refusal ??= "A step is a moment at least: 1 or more.";
                return false;
            }
            table = With(step: step);
            return true;
        }

        /// <summary>Column <paramref name="column"/> chosen, or not, for ALL COMBINATIONS, COUNT and CLOCK: an input's alone.</summary>
        public InputTable Choose(int column)
        {
            if (column < 0 || column >= Columns.Count || !Columns[column].IsInput)
                return this;
            var chosen = new HashSet<string>(_chosen, StringComparer.Ordinal);
            string port = Columns[column].Port;
            if (!chosen.Remove(port))
                chosen.Add(port);
            return With(chosen: chosen);
        }

        /// <summary>Column <paramref name="column"/> written in its next radix: binary, hex, decimal, and round again.</summary>
        public InputTable NextRadix(int column)
        {
            if (column < 0 || column >= Columns.Count || Columns[column].Width == 1)
                return this;
            Radix radix = Columns[column].Radix;
            var radixes = new Dictionary<string, Radix>(_radixes, StringComparer.Ordinal);
            radixes[Columns[column].Port] = radix == Radix.Binary ? Radix.Hex : radix == Radix.Hex ? Radix.Unsigned : Radix.Binary;
            return With(radixes: radixes);
        }

        // -----------------------------------------------------------------
        // Edits of the stimulus: what to commit, and from when the run changes
        // -----------------------------------------------------------------

        /// <summary>
        /// A cell typed in: the input given that value at the row's moment, or for nothing, its step there taken away. A
        /// pending row given a value becomes the stimulus's own.
        /// </summary>
        public bool TrySetCell(int row, int column, string text, out Stimulus edited, out long from, out string refusal)
        {
            edited = null;
            from = 0;
            if (!CanEdit(out refusal) || !TryInputColumn(column, out InputColumn input, out refusal) || !TryRow(row, out InputRow at, out refusal))
                return false;

            LogicVec? value = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!TryRead(text, input.Width, input.Radix, out LogicVec read, out refusal))
                    return false;
                value = read;
            }

            from = at.Time;
            if (!StimulusEdits.TrySetStep(_stimulus, input.Port, at.Time, value, out edited, out refusal))
                return false;
            if (StimulusEdits.Same(edited, _stimulus))
            {
                edited = null;   // no edit, and no undo step
                if (value.HasValue)
                {
                    // The input holds that already there -- from an earlier row, or from before the run starts (CONVERT's
                    // start): the step would change nothing, so there is none, and the cell stays empty (the 15g-4 review).
                    refusal = $"{input.Port} already holds {text.Trim()} at {at.Time}: nothing changes.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A row's time typed again: its steps moved there. A pending row just moves, the stimulus as it was --
        /// <paramref name="edited"/> null, and true.
        /// </summary>
        public bool TrySetTime(int row, string text, out Stimulus edited, out long from, out InputTable table, out string refusal)
        {
            edited = null;
            from = 0;
            table = this;
            if (!CanEdit(out refusal) || !TryRow(row, out InputRow at, out refusal) || !TryTime(text, out long to, out refusal))
                return false;

            if (at.Pending)
            {
                var pending = new List<long>(_pending);
                pending.Remove(at.Time);
                pending.Add(to);
                table = With(pending: pending);
                return true;
            }

            from = Math.Min(at.Time, to);
            return Changes(StimulusEdits.TryMoveRow(_stimulus, at.Time, to, out edited, out refusal), ref edited);
        }

        /// <summary>A row taken away: its steps, or a pending row, which leaves the stimulus as it was -- <paramref name="edited"/> null.</summary>
        public bool TryRemoveRow(int row, out Stimulus edited, out long from, out InputTable table, out string refusal)
        {
            edited = null;
            from = 0;
            table = this;
            if (!CanEdit(out refusal) || !TryRow(row, out InputRow at, out refusal))
                return false;

            if (at.Pending)
            {
                var pending = new List<long>(_pending);
                pending.Remove(at.Time);
                table = With(pending: pending);
                return true;
            }

            from = at.Time;
            return StimulusEdits.TryRemoveRow(_stimulus, at.Time, out edited, out refusal);
        }

        /// <summary>ALL COMBINATIONS: every combination of the chosen inputs, a <see cref="Step"/> apart from 0, the first chosen the most significant.</summary>
        public bool TryAllCombinations(out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(out refusal))
                return false;
            var ports = new List<(string, int)>();
            foreach (InputColumn column in Columns)
                if (column.Chosen)
                    ports.Add((column.Port, column.Width));
            return Changes(StimulusEdits.TryFillCombinations(_stimulus, ports, 0, Step, out edited, out refusal), ref edited);
        }

        /// <summary>COUNT: the one input chosen counting up from 0, a <see cref="Step"/> apart.</summary>
        public bool TryCount(out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(out refusal) || !TryOneChosen("count", out InputColumn column, out refusal))
                return false;
            return Changes(StimulusEdits.TryFillCount(_stimulus, column.Port, column.Width, 0, Step, out edited, out refusal), ref edited);
        }

        /// <summary>
        /// CLOCK: the one input chosen made a clock that changes every <see cref="Step"/>, or a clock made an input again -- and
        /// let go of, in <paramref name="table"/>: left chosen, a clock stood in the way of the next COUNT or ALL COMBINATIONS
        /// (the 15g-4 review).
        /// </summary>
        public bool TryClock(out Stimulus edited, out InputTable table, out string refusal)
        {
            edited = null;
            table = this;
            if (!CanEdit(out refusal) || !TryOneChosen("make a clock of", out InputColumn column, out refusal))
                return false;
            bool done = column.IsClock
                ? StimulusEdits.TryUnmakeClock(_stimulus, column.Port, out edited, out refusal)
                : StimulusEdits.TryMakeClock(_stimulus, column.Port, Math.Min(2 * Step, StimulusEdits.MaxTime), out edited, out refusal);
            if (done)
            {
                var chosen = new HashSet<string>(_chosen, StringComparer.Ordinal);
                chosen.Remove(column.Port);
                table = With(chosen: chosen);
            }
            return done;
        }

        /// <summary>END typed: when the run is meant to end.</summary>
        public bool TrySetEnd(string text, out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(out refusal) || !TryTime(text, out long end, out refusal))
                return false;
            return Changes(StimulusEdits.TrySetEnd(_stimulus, end, out edited, out refusal), ref edited);
        }

        /// <summary>An edit made, with <paramref name="edited"/> null when it changes nothing: no edit, and no undo step.</summary>
        private bool Changes(bool done, ref Stimulus edited)
        {
            if (done && StimulusEdits.Same(edited, _stimulus))
                edited = null;
            return done;
        }

        // -----------------------------------------------------------------

        /// <summary>
        /// The table again from its own stimulus with some of its own changed -- a radix, a choice, the step or a pending row --
        /// each in a copy, so the table before is as it was. The columns are kept: the ports and what drives them have not changed.
        /// </summary>
        private InputTable With(Dictionary<string, Radix> radixes = null, HashSet<string> chosen = null, List<long> pending = null, long? step = null)
        {
            radixes ??= new Dictionary<string, Radix>(_radixes, StringComparer.Ordinal);
            chosen ??= new HashSet<string>(_chosen, StringComparer.Ordinal);
            pending ??= new List<long>(_pending);

            var columns = new List<InputColumn>(Columns.Count);
            foreach (InputColumn column in Columns)
            {
                Radix radix = radixes.TryGetValue(column.Port, out Radix r) ? r : column.Radix;
                columns.Add(new InputColumn(column.Port, column.Width, column.IsInput, column.ClockPeriod, radix,
                    column.IsInput && chosen.Contains(column.Port)));
            }

            var times = new SortedSet<long>();
            foreach (InputRow row in Rows)
                if (!row.Pending)
                    times.Add(row.Time);
            pending.RemoveAll(time => times.Contains(time));
            foreach (long time in pending)
                times.Add(time);

            return new InputTable(_stimulus, columns, RowsOf(_stimulus, columns, times, pending), Why, step ?? Step, radixes, chosen, pending,
                AppliedBy, CannotConvert, LeftOut);
        }

        /// <summary>A row for each of <paramref name="times"/>: each column's value then, or nothing.</summary>
        private static List<InputRow> RowsOf(Stimulus stimulus, List<InputColumn> columns, SortedSet<long> times, List<long> pending)
        {
            // Each column's values by moment, found once: a step looked for among every step, for every cell, was a table's
            // worth of scans of the stimulus, and a testbench's ran to half a minute (the 15h review). The first step at a
            // moment wins, as it always did.
            var at = new Dictionary<long, LogicVec>[columns.Count];
            var column = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int c = 0; c < columns.Count; c++)
            {
                at[c] = new Dictionary<long, LogicVec>();
                column[(columns[c].IsInput ? "i " : "o ") + columns[c].Port] = c;
            }
            foreach (Track track in stimulus.Tracks)
                if (column.TryGetValue("i " + track.Port, out int c))
                    foreach (Step step in track.Steps)
                        if (!at[c].ContainsKey(step.Time))
                            at[c][step.Time] = step.Value;
            foreach (Check check in stimulus.Checks)
                if (column.TryGetValue("o " + check.Port, out int c) && !at[c].ContainsKey(check.Time))
                    at[c][check.Time] = check.Expected;

            var rows = new List<InputRow>(times.Count);
            foreach (long time in times)
            {
                var cells = new string[columns.Count];
                for (int c = 0; c < columns.Count; c++)
                    cells[c] = at[c].TryGetValue(time, out LogicVec value) ? Text(value, columns[c].Radix) : "";
                rows.Add(new InputRow(time, cells, pending.Contains(time)));
            }
            return rows;
        }

        /// <summary>
        /// <paramref name="stimulus"/> with each track's start -- a declaration's value, before anything ran -- a step at 0
        /// where it has none there: what a testbench's table shows at 0. An input given its value where it is declared read
        /// blank, as though nothing drove it (the 15h review). Only for the table that shows a testbench, which nothing commits.
        /// </summary>
        private static Stimulus Started(Stimulus stimulus)
        {
            var tracks = new List<Track>(stimulus.Tracks.Count);
            bool changed = false;
            foreach (Track track in stimulus.Tracks)
            {
                bool atZero = false;
                foreach (Step step in track.Steps)
                    atZero |= step.Time == 0;
                if (track.Start is LogicVec start && !atZero)
                {
                    var steps = new List<Step>(track.Steps.Count + 1) { new Step(0, start) };
                    steps.AddRange(track.Steps);
                    tracks.Add(track with { Steps = steps, Start = null });
                    changed = true;
                }
                else
                {
                    tracks.Add(track);
                }
            }
            return changed ? stimulus with { Tracks = tracks } : stimulus;
        }

        private bool CanEdit(out string refusal)
        {
            refusal = Why;
            return Why == null;
        }

        private bool TryInputColumn(int column, out InputColumn input, out string refusal)
        {
            input = column >= 0 && column < Columns.Count ? Columns[column] : null;
            refusal = input == null ? "There is no such column."
                : !input.IsInput ? $"{input.Port} is an output: the test reads it, and nothing gives it a value."
                : input.IsClock ? $"{input.Port} is a clock: it ticks by itself. Choose it and press CLOCK to give it steps instead."
                : null;
            return refusal == null;
        }

        private bool TryRow(int row, out InputRow at, out string refusal)
        {
            at = row >= 0 && row < Rows.Count ? Rows[row] : null;
            refusal = at == null ? "There is no such row." : null;
            return at != null;
        }

        private bool TryOneChosen(string doing, out InputColumn column, out string refusal)
        {
            column = null;
            int count = 0;
            foreach (InputColumn each in Columns)
            {
                if (!each.Chosen)
                    continue;
                column = each;
                count++;
            }
            refusal = count == 1 ? null : $"Choose one input to {doing}: click its column's name.";
            return count == 1;
        }

        private static bool TryTime(string text, out long time, out string refusal)
        {
            refusal = null;
            if (long.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out time))
            {
                if (time <= StimulusEdits.MaxTime)
                    return true;
                refusal = $"The table goes as far as {StimulusEdits.MaxTime}: {time} is past it.";
                return false;
            }
            refusal = $"\"{(text ?? "").Trim()}\" is no moment: a whole number of the circuit's unit, 0 or more.";
            return false;
        }

        private static long ClockOf(Stimulus stimulus, string port)
        {
            foreach (ClockTrack clock in stimulus.Clocks)
                if (string.Equals(clock.Port, port, StringComparison.Ordinal))
                    return clock.Period;
            return 0;
        }

        private static bool Checked(Stimulus stimulus, string port)
        {
            foreach (Check check in stimulus.Checks)
                if (string.Equals(check.Port, port, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
