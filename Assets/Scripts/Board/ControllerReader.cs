using System;
using System.Collections.Generic;
using System.Text;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// Reads a written module as the textbook draws a controller (Part 15h-6e): a state machine as its states and arrows, a
    /// decoder as its control table -- from the code itself, so a machine of any number of inputs reads, where the STATES
    /// tab's search by running stops at four input bits.
    /// </summary>
    /// <remarks>
    /// **A state machine** is a register of an enum type given a value in an <c>always_ff</c> -- the value its reset gives it
    /// the start -- whose next value is either a variable set in an <c>always_comb</c> that is, after plain assignments of
    /// what holds unless an arm says otherwise, one <c>case</c> on the register; or a <c>case</c> on the register in the
    /// <c>always_ff</c> itself. Each state's arm -- its own label's, else <c>default</c>'s -- is followed down every way through
    /// it: an <c>if</c> and its <c>else</c>, a nested <c>case</c> on something else, a <c>?:</c> in the next value. A second
    /// <c>always_comb</c> that is a <c>case</c> on the register gives each state's outputs too, the three-process style.
    ///
    /// **A decoder** is an <c>always_comb</c> that is, after plain assignments, one <c>case</c> whose arms only assign: a
    /// table, a row a label and a column a signal it sets.
    ///
    /// **Anything else is no reading**, and says why: a loop, a call, a part of a signal set, a next state worked out rather
    /// than named. The words on an arrow and in a cell are the code's own (<see cref="Printer.ExpressionText"/>); a sized
    /// literal is printed without its size.
    /// </remarks>
    public static class ControllerReader
    {
        /// <summary>How many ways through one state's arm may be followed before the arm is taken to be no textbook controller's.</summary>
        public const int MostWays = 256;

        /// <summary>
        /// <paramref name="module"/> read, with <paramref name="tree"/> for the parameters it sees and the enum its state is of;
        /// null, and why, where it is neither a state machine nor wholly decoders.
        /// </summary>
        public static ControllerReading Read(SyntaxTree tree, ModuleDeclaration module, out string why)
        {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            try
            {
                return new Reader(tree, module).Read(out why);
            }
            catch (NotReadable stop)
            {
                why = stop.Message;
                return null;
            }
        }

        private sealed class NotReadable : Exception
        {
            public NotReadable(string why) : base(why) { }
        }

        /// <summary>One way through an arm: what decided it, whether an <c>else</c> or a <c>default</c> did, and what it set.</summary>
        private sealed class Way
        {
            public readonly List<string> Conditions = new List<string>();
            public bool Otherwise;
            public readonly Dictionary<string, Expression> Values = new Dictionary<string, Expression>(StringComparer.Ordinal);

            public Way Copy()
            {
                var copy = new Way { Otherwise = Otherwise };
                copy.Conditions.AddRange(Conditions);
                foreach (KeyValuePair<string, Expression> v in Values)
                    copy.Values[v.Key] = v.Value;
                return copy;
            }
        }

        private sealed class Reader
        {
            private readonly SyntaxTree _tree;
            private readonly ModuleDeclaration _module;
            private readonly Dictionary<string, Expression> _parameters = new Dictionary<string, Expression>(StringComparer.Ordinal);
            private readonly Dictionary<string, EnumType> _types = new Dictionary<string, EnumType>(StringComparer.Ordinal);
            private readonly Dictionary<string, DataType> _declared = new Dictionary<string, DataType>(StringComparer.Ordinal);

            public Reader(SyntaxTree tree, ModuleDeclaration module)
            {
                _tree = tree;
                _module = module;
                Gather(tree.UnitItems);
                foreach (PackageDeclaration package in tree.Packages)
                    Gather(package.Items);
                foreach (ParameterDeclaration p in module.HeaderParameters)
                    _parameters[p.Name] = p.Value;
                foreach (PortDeclaration port in module.Ports)
                    if (port.Type != null)
                        _declared[port.Name] = port.Type;
                Gather(module.Items);
            }

            private void Gather(IEnumerable<ModuleItem> items)
            {
                foreach (ModuleItem item in items)
                {
                    switch (item)
                    {
                        case ParameterDeclaration p:
                            _parameters[p.Name] = p.Value;
                            break;
                        case TypedefDeclaration t when t.Type?.Enum != null:
                            _types[t.Name] = t.Type.Enum;
                            break;
                        case DataDeclaration d when d.Type != null:
                            foreach (Declarator name in d.Declarators)
                                _declared[name.Name] = d.Type;
                            break;
                    }
                }
            }

            public ControllerReading Read(out string why)
            {
                var processes = new List<ProceduralBlock>();
                bool other = false;
                foreach (ModuleItem item in _module.Items)
                {
                    if (item is ProceduralBlock p)
                    {
                        if (p.Kind != ProcessKind.Initial && p.Kind != ProcessKind.Final)
                            processes.Add(p);
                    }
                    else if (item is ContinuousAssign || item is ModuleInstantiation)
                        other = true;
                }

                var used = new HashSet<ProceduralBlock>();
                MachineReading machine = ReadMachine(processes, used);
                var tables = new List<TableReading>();
                foreach (ProceduralBlock p in processes)
                {
                    if (used.Contains(p) || p.Kind != ProcessKind.AlwaysComb)
                        continue;
                    TableReading table = ReadTable(p, machine?.Register);
                    if (table != null)
                    {
                        tables.Add(table);
                        used.Add(p);
                    }
                }

                bool whole = !other && used.Count == processes.Count;
                if (machine == null && (tables.Count == 0 || !whole))
                {
                    why = tables.Count == 0
                        ? "Neither a state machine -- an enum register with a case on it -- nor a decoder: no case of constants on one subject."
                        : "Its decoders are only some of it: the rest is not a table.";
                    return null;
                }
                why = null;
                return new ControllerReading(_module.Name, machine, tables, whole);
            }

            // ---- the state machine

            private MachineReading ReadMachine(List<ProceduralBlock> processes, HashSet<ProceduralBlock> used)
            {
                foreach (ProceduralBlock register in processes)
                {
                    if (register.Kind != ProcessKind.AlwaysFf && register.Kind != ProcessKind.Always)
                        continue;
                    if (!(register.Body is TimedStatement timed))
                        continue;
                    Statement body = Single(timed.Body);

                    // if (reset) S <= START; else <the rest>, or the rest alone: what the reset gives each register.
                    Statement rest = body;
                    var resets = new Dictionary<string, Expression>(StringComparer.Ordinal);
                    if (body is IfStatement reset && reset.Else != null)
                    {
                        foreach (Statement s in Flatten(reset.Then))
                            if (s is AssignmentStatement a && a.IsNonBlocking && SimpleName(a.Target) is string target)
                                resets[target] = a.Value;
                        rest = Single(reset.Else);
                    }

                    // The next value from a variable another process works out as a case on the register: S <= N, beside any
                    // other register the process keeps (a counter's count <= count_next).
                    foreach (Statement statement in Flatten(rest))
                    {
                        if (!(statement is AssignmentStatement next) || !next.IsNonBlocking || !(SimpleName(next.Target) is string s1)
                            || !(SimpleName(next.Value) is string n) || n == s1)
                            continue;
                        foreach (ProceduralBlock comb in processes)
                        {
                            if (comb == register || comb.Kind != ProcessKind.AlwaysComb)
                                continue;
                            if (!(CaseOn(comb.Body, s1, out List<AssignmentStatement> defaults) is CaseStatement arms) || !Assigns(arms, n))
                                continue;
                            used.Add(register);
                            used.Add(comb);
                            return Machine(s1, n, resets.TryGetValue(s1, out Expression start) ? start : null, defaults, arms, blocking: true, processes, used);
                        }
                    }

                    // One process: case (S) in the always_ff itself, after nonblocking defaults, setting S.
                    foreach (Statement statement in Flatten(rest))
                        if (statement is CaseStatement c && SimpleName(c.Subject) is string subject && Assigns(c, subject)
                            && CaseOn(rest, subject, out List<AssignmentStatement> ownDefaults, nonBlocking: true) is CaseStatement own)
                        {
                            used.Add(register);
                            return Machine(subject, subject, resets.TryGetValue(subject, out Expression start) ? start : null, ownDefaults, own,
                                blocking: false, processes, used);
                        }
                }
                return null;
            }

            private MachineReading Machine(string register, string next, Expression start, List<AssignmentStatement> defaults, CaseStatement arms,
                bool blocking, List<ProceduralBlock> processes, HashSet<ProceduralBlock> used)
            {
                var states = new List<string>();
                var codes = new List<LogicVec>();
                int width = _declared.TryGetValue(register, out DataType declared) ? Width(declared) : 32;
                if (Enumerated(register) is EnumType type)
                {
                    ulong value = 0;
                    foreach (EnumMember member in type.Members)
                    {
                        if (member.Value != null)
                        {
                            LogicVec? given = Constant(member.Value);
                            if (!(given is LogicVec g) || !g.TryGetUInt64(out value))
                                throw new NotReadable($"The value of state {member.Name} is worked out, not written.");
                        }
                        states.Add(member.Name);
                        codes.Add(LogicVec.FromUInt64(value, Math.Max(1, width)));
                        value++;
                    }
                }
                else
                {
                    // No enum: the states are the case's labels, each a constant -- localparam ONE = 2'd1 -- or a number.
                    foreach (CaseItem item in arms.Items)
                        foreach (Expression label in item.Labels)
                        {
                            if (!(Constant(label) is LogicVec code))
                                throw new NotReadable($"The state {Text(label, sized: true)} is worked out, not written.");
                            string name = LabelName(label);
                            if (!states.Contains(name))
                            {
                                states.Add(name);
                                codes.Add(code.Resize(Math.Max(1, width)));
                            }
                        }
                    if (states.Count < 2)
                        throw new NotReadable($"A case on {register} of fewer than two states is no state machine.");
                }
                int startState = StateOf(start, states, codes) ?? 0;

                // Every other always_comb that is a case on the register only gives outputs: the three-process style.
                var outputArms = new List<(List<AssignmentStatement> Defaults, CaseStatement Arms)>();
                foreach (ProceduralBlock comb in processes)
                    if (!used.Contains(comb) && comb.Kind == ProcessKind.AlwaysComb && CaseOn(comb.Body, register, out List<AssignmentStatement> d) is CaseStatement c
                        && !Assigns(c, next))
                    {
                        outputArms.Add((d, c));
                        used.Add(comb);
                    }

                var defaultValues = Defaults(defaults, blocking);
                var arrows = new List<MachineArrow>();
                var settings = new List<IReadOnlyList<ReadValue>>();
                for (int s = 0; s < states.Count; s++)
                {
                    List<Way> ways = Walk(ArmFor(arms, states[s]), Start(defaultValues), blocking);
                    var targets = new List<(int To, Way Way, string Extra)>();
                    foreach (Way way in ways)
                    {
                        Expression target = way.Values.TryGetValue(next, out Expression t) ? t : null;
                        foreach ((int index, string extra) in Targets(target, register, s, states, codes))
                            targets.Add((index, way, extra));
                    }
                    // What every way sets goes in the state's box (Moore); what differs by way, on the arrows its ways take
                    // (Mealy: start / load = 1).
                    var set = new List<ReadValue>();
                    var varying = new List<string>();
                    foreach (string name in Names(ways, defaultValues, next))
                    {
                        if (Value(ways, name, defaultValues) is ReadValue common)
                        {
                            if (!Unchanged(common, name, defaultValues))
                                set.Add(common);
                        }
                        else
                            varying.Add(name);
                    }
                    arrows.AddRange(Arrows(s, targets, varying, defaultValues));

                    foreach ((List<AssignmentStatement> d, CaseStatement c) in outputArms)
                    {
                        var outDefaults = Defaults(d, blocking: true);
                        Settle(set, Walk(ArmFor(c, states[s]), Start(outDefaults), blocking: true), outDefaults, next);
                    }
                    settings.Add(set);
                }
                return new MachineReading(register, next, states, codes, startState, arrows, settings);
            }

            /// <summary>
            /// The states <paramref name="target"/> names -- a state, the register for staying, nothing for holding, or a ?: of
            /// them -- each with what chooses it.
            /// </summary>
            private IEnumerable<(int To, string Condition)> Targets(Expression target, string register, int here, List<string> states, List<LogicVec> codes)
            {
                target = Unparenthesized(target);
                if (target == null || SimpleName(target) == register)
                {
                    yield return (here, null);   // nothing sets it, or it is set to itself: it holds
                    yield break;
                }
                if (target is ConditionalExpression choice)
                {
                    string condition = Text(choice.Condition);
                    foreach ((int to, string c) in Targets(choice.WhenTrue, register, here, states, codes))
                        yield return (to, c == null ? condition : condition + " and " + c);
                    foreach ((int to, string c) in Targets(choice.WhenFalse, register, here, states, codes))
                        yield return (to, c ?? MachineArrow.Otherwise);
                    yield break;
                }
                if (!(StateOf(target, states, codes) is int index))
                    throw new NotReadable($"In {states[here]}, the next state is {Text(target)}, which is no state of {register}.");
                yield return (index, null);
            }

            /// <summary>The state <paramref name="e"/> is -- by its name, or by the value it is -- or null for none.</summary>
            private int? StateOf(Expression e, List<string> states, List<LogicVec> codes)
            {
                if (e == null)
                    return null;
                if (SimpleName(e) is string name && states.IndexOf(name) is int i && i >= 0)
                    return i;
                if (Constant(e) is LogicVec v && v.TryGetUInt64(out ulong value))
                    for (int s = 0; s < codes.Count; s++)
                        if (codes[s].TryGetUInt64(out ulong code) && code == value)
                            return s;
                return null;
            }

            /// <summary>A state's name as a label gives it: the constant it names, or the number as written.</summary>
            private static string LabelName(Expression label) => SimpleName(label) ?? Text(label, sized: true);

            /// <summary>
            /// The arrows out of state <paramref name="from"/>: one to each state its ways reach, labelled with what takes it, and
            /// carrying what the ways that take it set of <paramref name="varying"/> -- the outputs that differ by way.
            /// </summary>
            private IEnumerable<MachineArrow> Arrows(int from, List<(int To, Way Way, string Extra)> targets, List<string> varying,
                Dictionary<string, Expression> defaults)
            {
                var order = new List<int>();
                var labels = new Dictionary<int, List<string>>();
                var taking = new Dictionary<int, List<Way>>();
                var always = new HashSet<int>();
                foreach ((int to, Way way, string extra) in targets)
                {
                    if (!labels.ContainsKey(to))
                    {
                        order.Add(to);
                        labels[to] = new List<string>();
                        taking[to] = new List<Way>();
                    }
                    if (!taking[to].Contains(way))
                        taking[to].Add(way);
                    var parts = new List<string>(way.Conditions);
                    if (extra != null && extra != MachineArrow.Otherwise)
                        parts.Add(extra);
                    string label = parts.Count > 0 ? string.Join(" and ", parts)
                        : way.Otherwise || extra == MachineArrow.Otherwise ? MachineArrow.Otherwise : null;
                    if (label == null)
                        always.Add(to);
                    else if (!labels[to].Contains(label))
                        labels[to].Add(label);
                }
                foreach (int to in order)
                {
                    string condition = order.Count == 1 || always.Contains(to) ? null
                        : labels[to].Contains(MachineArrow.Otherwise) ? MachineArrow.Otherwise
                        : string.Join(" or ", labels[to]);
                    var outputs = new List<ReadValue>();
                    foreach (string name in varying)
                    {
                        ReadValue value = Value(taking[to], name, defaults) ?? Joined(taking[to], name, defaults);
                        if (!Unchanged(value, name, defaults))
                            outputs.Add(value);
                    }
                    yield return new MachineArrow(from, to, condition, outputs);
                }
            }

            /// <summary>Every signal <paramref name="ways"/> set but <paramref name="next"/>, in the order they are set before the case, then as met.</summary>
            private static List<string> Names(List<Way> ways, Dictionary<string, Expression> defaults, string next)
            {
                var names = new List<string>();
                foreach (Way way in ways)
                    foreach (string name in way.Values.Keys)
                        if (name != next && !names.Contains(name))
                            names.Add(name);
                names.Sort((a, b) => Position(a, defaults).CompareTo(Position(b, defaults)));
                return names;
            }

            /// <summary>What every one of <paramref name="ways"/> sets <paramref name="name"/> to, where they agree; null where they do not.</summary>
            private ReadValue? Value(List<Way> ways, string name, Dictionary<string, Expression> defaults)
            {
                string text = null;
                Expression only = null;
                foreach (Way way in ways)
                {
                    Expression v = way.Values.TryGetValue(name, out Expression e) ? e : defaults.TryGetValue(name, out Expression d) ? d : null;
                    string t = v != null ? Text(v) : "";
                    if (text != null && t != text)
                        return null;
                    text = t;
                    only = v;
                }
                return new ReadValue(name, text ?? "", only != null ? Constant(only) : null);
            }

            /// <summary>Everything <paramref name="ways"/> set <paramref name="name"/> to, one after another: <c>1 / 0</c>.</summary>
            private ReadValue Joined(List<Way> ways, string name, Dictionary<string, Expression> defaults)
            {
                var texts = new List<string>();
                foreach (Way way in ways)
                {
                    Expression v = way.Values.TryGetValue(name, out Expression e) ? e : defaults.TryGetValue(name, out Expression d) ? d : null;
                    string t = v != null ? Text(v) : "";
                    if (!texts.Contains(t))
                        texts.Add(t);
                }
                return new ReadValue(name, string.Join(" / ", texts), null);
            }

            /// <summary>Whether <paramref name="value"/> is what <paramref name="name"/> is set to before the case anyway.</summary>
            private static bool Unchanged(ReadValue value, string name, Dictionary<string, Expression> defaults) =>
                value.Text.Length == 0 || defaults.TryGetValue(name, out Expression d) && d != null && Text(d) == value.Text;

            /// <summary>What a state's ways set, besides its next state, where it differs from what is set before the case.</summary>
            private void Settle(List<ReadValue> set, List<Way> ways, Dictionary<string, Expression> defaults, string next)
            {
                var names = new List<string>();
                foreach (Way way in ways)
                    foreach (string name in way.Values.Keys)
                        if (name != next && !names.Contains(name))
                            names.Add(name);
                names.Sort((a, b) => Position(a, defaults).CompareTo(Position(b, defaults)));
                foreach (string name in names)
                {
                    defaults.TryGetValue(name, out Expression otherwise);
                    string fallback = otherwise != null ? Text(otherwise) : null;
                    var texts = new List<string>();
                    Expression only = null;
                    foreach (Way way in ways)
                    {
                        Expression v = way.Values.TryGetValue(name, out Expression e) ? e : otherwise;
                        string text = v != null ? Text(v) : "";
                        if (!texts.Contains(text))
                            texts.Add(text);
                        only = v;
                    }
                    if (texts.Count == 1 && texts[0] == fallback)
                        continue;
                    int at = set.FindIndex(r => r.Signal == name);
                    var value = new ReadValue(name, string.Join(" / ", texts), texts.Count == 1 && only != null ? Constant(only) : null);
                    if (at >= 0)
                        set[at] = value;
                    else
                        set.Add(value);
                }
            }

            private static int Position(string name, Dictionary<string, Expression> defaults)
            {
                int i = 0;
                foreach (string key in defaults.Keys)
                {
                    if (key == name)
                        return i;
                    i++;
                }
                return int.MaxValue;
            }

            // ---- the decoders

            private TableReading ReadTable(ProceduralBlock process, string register)
            {
                if (!(CaseOnAnything(process.Body, out List<AssignmentStatement> defaults) is CaseStatement table))
                    return null;
                if (register != null && SimpleName(table.Subject) == register)
                    return null;

                var columns = new List<string>();
                var defaultValues = Defaults(defaults, blocking: true);
                columns.AddRange(defaultValues.Keys);
                var arms = new List<(CaseItem Item, Dictionary<string, Expression> Sets)>();
                foreach (CaseItem item in table.Items)
                {
                    var sets = new Dictionary<string, Expression>(StringComparer.Ordinal);
                    foreach (Statement s in Flatten(item.Body))
                    {
                        if (s is NullStatement)
                            continue;
                        if (!(s is AssignmentStatement a) || a.IsNonBlocking || a.Operator != "=" || a.IntraAssignment != null || !(SimpleName(a.Target) is string target))
                            return null;   // an arm that does more than set signals is no row of a table
                        if (!Decoded(a.Value))
                            return null;   // an ALU, an immediate's bits: a case that computes is drawn, not tabled
                        sets[target] = a.Value;
                        if (!columns.Contains(target))
                            columns.Add(target);
                    }
                    arms.Add((item, sets));
                }
                if (columns.Count == 0)
                    return null;

                var rows = new List<TableRow>();
                foreach ((CaseItem item, Dictionary<string, Expression> sets) in arms)
                {
                    var matches = new List<LogicVec>();
                    var labels = new List<string>();
                    foreach (Expression label in item.Labels)
                    {
                        labels.Add(Text(label, sized: true));
                        if (Constant(label) is LogicVec v)
                            matches.Add(v);
                    }
                    var cells = new List<TableCell>();
                    foreach (string column in columns)
                    {
                        if (sets.TryGetValue(column, out Expression v))
                            cells.Add(new TableCell(Text(v), Constant(v), false));
                        else if (defaultValues.TryGetValue(column, out Expression d))
                            cells.Add(new TableCell(Text(d), Constant(d), true));
                        else
                            cells.Add(new TableCell("", null, true));
                    }
                    rows.Add(new TableRow(item.IsDefault ? MachineArrow.Otherwise : Join(labels), matches, item.IsDefault, cells));
                }
                return new TableReading(Text(table.Subject), columns, rows);
            }

            /// <summary>
            /// Whether <paramref name="value"/> is what a decoder gives -- a number, a name, or a <c>?:</c> choosing between
            /// them -- rather than something worked out of the data, <c>a + b</c>, <c>{..}</c>, <c>a == b</c>.
            /// </summary>
            private static bool Decoded(Expression value)
            {
                value = Unparenthesized(value);
                return value is NumberLiteral || SimpleName(value) != null
                    || value is ConditionalExpression choice && Decoded(choice.WhenTrue) && Decoded(choice.WhenFalse);
            }

            // ---- walking an arm

            /// <summary>Every way through <paramref name="statement"/> from each of <paramref name="ways"/>.</summary>
            private List<Way> Walk(Statement statement, List<Way> ways, bool blocking)
            {
                switch (statement)
                {
                    case null:
                    case NullStatement _:
                        return ways;
                    case BlockStatement block:
                        if (block.Declarations.Count > 0)
                            throw new NotReadable("An arm declares a variable of its own.");
                        foreach (Statement s in block.Statements)
                            ways = Walk(s, ways, blocking);
                        return ways;
                    case AssignmentStatement a:
                        if (a.IntraAssignment != null || a.IsNonBlocking == blocking || (blocking && a.Operator != "="))
                            throw new NotReadable($"An arm sets {Text(a.Target)} as no controller's arm does.");
                        if (!(SimpleName(a.Target) is string target))
                            throw new NotReadable($"An arm sets {Text(a.Target)}, a part of a signal.");
                        foreach (Way way in ways)
                            way.Values[target] = a.Value;
                        return ways;
                    case IfStatement choice:
                    {
                        string condition = Text(choice.Condition);
                        var then = new List<Way>();
                        var otherwise = new List<Way>();
                        foreach (Way way in ways)
                        {
                            Way t = way.Copy();
                            t.Conditions.Add(condition);
                            then.Add(t);
                            Way e = way.Copy();
                            e.Otherwise = true;
                            otherwise.Add(e);
                        }
                        var result = Walk(choice.Then, then, blocking);
                        result.AddRange(Walk(choice.Else, otherwise, blocking));
                        return Capped(result);
                    }
                    case CaseStatement inner:
                    {
                        string subject = Text(inner.Subject);
                        var result = new List<Way>();
                        bool hasDefault = false;
                        foreach (CaseItem item in inner.Items)
                        {
                            var labels = new List<string>();
                            foreach (Expression label in item.Labels)
                                labels.Add(Text(label, sized: true));
                            hasDefault |= item.IsDefault;
                            var these = new List<Way>();
                            foreach (Way way in ways)
                            {
                                Way w = way.Copy();
                                if (item.IsDefault)
                                    w.Otherwise = true;
                                else
                                    w.Conditions.Add(subject + " is " + Join(labels));
                                these.Add(w);
                            }
                            result.AddRange(Walk(item.Body, these, blocking));
                        }
                        if (!hasDefault)
                            foreach (Way way in ways)
                            {
                                Way w = way.Copy();
                                w.Otherwise = true;
                                result.Add(w);
                            }
                        return Capped(result);
                    }
                    default:
                        throw new NotReadable($"An arm holds a {statement.GetType().Name.Replace("Statement", "").ToLowerInvariant()}, which no table or diagram shows.");
                }
            }

            private static List<Way> Capped(List<Way> ways) =>
                ways.Count > MostWays ? throw new NotReadable($"An arm has more than {MostWays} ways through it.") : ways;

            private static List<Way> Start(Dictionary<string, Expression> defaults)
            {
                var way = new Way();
                foreach (KeyValuePair<string, Expression> d in defaults)
                    way.Values[d.Key] = d.Value;
                return new List<Way> { way };
            }

            private static Dictionary<string, Expression> Defaults(List<AssignmentStatement> defaults, bool blocking)
            {
                var values = new Dictionary<string, Expression>(StringComparer.Ordinal);
                foreach (AssignmentStatement a in defaults)
                    if (a.IsNonBlocking != blocking && SimpleName(a.Target) is string target)
                        values[target] = a.Value;
                return values;
            }

            /// <summary>The arm a state takes: its own label's, else <c>default</c>'s, else none.</summary>
            private static Statement ArmFor(CaseStatement arms, string state)
            {
                Statement fallback = null;
                foreach (CaseItem item in arms.Items)
                {
                    if (item.IsDefault)
                        fallback = item.Body;
                    foreach (Expression label in item.Labels)
                        if (LabelName(label) == state)
                            return item.Body;
                }
                return fallback;
            }

            /// <summary>
            /// The <c>case</c> on <paramref name="subject"/> that <paramref name="body"/> is, after plain assignments; or null. The
            /// assignments before it are given back.
            /// </summary>
            private static CaseStatement CaseOn(Statement body, string subject, out List<AssignmentStatement> defaults, bool nonBlocking = false)
            {
                CaseStatement found = CaseOnAnything(body, out defaults, nonBlocking);
                return found != null && SimpleName(found.Subject) == subject ? found : null;
            }

            private static CaseStatement CaseOnAnything(Statement body, out List<AssignmentStatement> defaults, bool nonBlocking = false)
            {
                defaults = new List<AssignmentStatement>();
                CaseStatement found = null;
                foreach (Statement s in Flatten(body))
                {
                    if (found == null && s is AssignmentStatement a && a.IsNonBlocking == nonBlocking && a.IntraAssignment == null
                        && SimpleName(a.Target) != null)
                        defaults.Add(a);
                    else if (found == null && s is CaseStatement c && !c.Inside && c.Keyword == "case")
                        found = c;
                    else if (!(s is NullStatement))
                        return null;
                }
                return found;
            }

            private static bool Assigns(CaseStatement arms, string target)
            {
                foreach (CaseItem item in arms.Items)
                    if (AssignsIn(item.Body, target))
                        return true;
                return false;
            }

            private static bool AssignsIn(Statement s, string target)
            {
                switch (s)
                {
                    case AssignmentStatement a:
                        return SimpleName(a.Target) == target;
                    case BlockStatement b:
                        foreach (Statement inner in b.Statements)
                            if (AssignsIn(inner, target))
                                return true;
                        return false;
                    case IfStatement i:
                        return AssignsIn(i.Then, target) || AssignsIn(i.Else, target);
                    case CaseStatement c:
                        foreach (CaseItem item in c.Items)
                            if (AssignsIn(item.Body, target))
                                return true;
                        return false;
                    default:
                        return false;
                }
            }

            private static IEnumerable<Statement> Flatten(Statement s)
            {
                if (s is BlockStatement block && block.Declarations.Count == 0)
                {
                    foreach (Statement inner in block.Statements)
                        foreach (Statement flat in Flatten(inner))
                            yield return flat;
                }
                else if (s != null)
                    yield return s;
            }

            /// <summary>A block of one statement as that statement.</summary>
            private static Statement Single(Statement s)
            {
                while (s is BlockStatement block && block.Declarations.Count == 0 && block.Statements.Count == 1)
                    s = block.Statements[0];
                return s;
            }

            // ---- names, types and values

            private EnumType Enumerated(string signal)
            {
                if (!_declared.TryGetValue(signal, out DataType type))
                    return null;
                if (type.Enum != null)
                    return type.Enum;
                return type.Name != null && _types.TryGetValue(type.Name, out EnumType named) ? named : null;
            }

            private int Width(DataType type)
            {
                if (type.Enum?.BaseType != null)
                    return Width(type.Enum.BaseType);
                if (type.Name != null && type.Enum == null && _types.TryGetValue(type.Name, out EnumType named))
                    return named.BaseType != null ? Width(named.BaseType) : 32;
                if (type.Enum != null)
                    return 32;
                int width = 1;
                foreach (Dimension d in type.Packed)
                {
                    if (!(Constant(d.Left) is LogicVec l) || !(Constant(d.Right) is LogicVec r) || !l.TryGetInt64(out long left) || !r.TryGetInt64(out long right))
                        return 32;
                    width *= (int)Math.Abs(left - right) + 1;
                }
                return type.Keyword == "int" || type.Keyword == "integer" ? 32 : width;
            }

            /// <summary>The value <paramref name="e"/> is where it is a number or a parameter written as one; null otherwise.</summary>
            private LogicVec? Constant(Expression e, int depth = 0)
            {
                e = Unparenthesized(e);
                if (e is NumberLiteral literal && !literal.FillsContext)
                    return literal.Value;
                if (depth < 8 && SimpleName(e) is string name && _parameters.TryGetValue(name, out Expression value) && value != null)
                    return Constant(value, depth + 1);
                return null;
            }

            private static string SimpleName(Expression e) =>
                Unparenthesized(e) is NameExpression n && n.Path.Count == 1 && n.Package == null ? n.Name : null;

            private static Expression Unparenthesized(Expression e)
            {
                while (e is ParenthesizedExpression p)
                    e = p.Inner;
                return e;
            }

            /// <summary>
            /// An expression as written; a lone literal without its size, <c>2'd2</c> as <c>2</c> and <c>1'b1</c> as <c>1</c>,
            /// unless <paramref name="sized"/> -- a label, where the bits are the point.
            /// </summary>
            private static string Text(Expression e, bool sized = false)
            {
                if (!sized && Unparenthesized(e) is NumberLiteral literal && literal.Text != null)
                {
                    string t = literal.Text.Replace("_", "");
                    int tick = t.IndexOf('\'');
                    if (tick >= 0 && tick + 1 < t.Length)
                    {
                        char radix = char.ToLowerInvariant(t[tick + 1] == 's' || t[tick + 1] == 'S' ? (tick + 2 < t.Length ? t[tick + 2] : 'd') : t[tick + 1]);
                        if ((radix == 'd' || radix == 'b' && t.Length - tick <= 3) && literal.Value.TryGetUInt64(out ulong v))
                            return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    return t;
                }
                return Printer.ExpressionText(e);
            }

            private static string Join(List<string> labels)
            {
                if (labels.Count <= 1)
                    return labels.Count == 1 ? labels[0] : "";
                var text = new StringBuilder();
                for (int i = 0; i < labels.Count; i++)
                {
                    if (i > 0)
                        text.Append(i == labels.Count - 1 ? " or " : ", ");
                    text.Append(labels[i]);
                }
                return text.ToString();
            }
        }
    }
}
