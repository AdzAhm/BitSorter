using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// One way through an arm: the tests it passed, the tests it failed -- an <c>else</c>, a <c>default</c> -- and what it
        /// set. An arrow's words are both: "done and !err", where the failed test alone read "otherwise" and lost the "done"
        /// (the review of 15h-6e).
        /// </summary>
        private sealed class Way
        {
            public readonly List<string> Conditions = new List<string>();
            public readonly List<string> Negations = new List<string>();
            public readonly Dictionary<string, Expression> Values = new Dictionary<string, Expression>(StringComparer.Ordinal);

            public Way Copy()
            {
                var copy = new Way();
                copy.Conditions.AddRange(Conditions);
                copy.Negations.AddRange(Negations);
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
            private readonly Dictionary<string, LogicVec> _members = new Dictionary<string, LogicVec>(StringComparer.Ordinal);
            private readonly Dictionary<string, Expression> _initial = new Dictionary<string, Expression>(StringComparer.Ordinal);

            /// <summary>Set where the register's process keeps something the reading does not show: not the whole module.</summary>
            private bool _partial;

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
                            Members(t.Type.Enum);
                            break;
                        case DataDeclaration d when d.Type != null:
                            if (d.Type.Enum != null)
                                Members(d.Type.Enum);
                            foreach (Declarator name in d.Declarators)
                            {
                                _declared[name.Name] = d.Type;
                                if (name.Initializer != null)
                                    _initial[name.Name] = name.Initializer;
                            }
                            break;
                    }
                }
            }

            /// <summary>Each member of <paramref name="type"/> and its value, a constant like a parameter: a label may name one.</summary>
            private void Members(EnumType type)
            {
                int width = type.BaseType != null ? Width(type.BaseType) : 32;
                ulong value = 0;
                foreach (EnumMember member in type.Members)
                {
                    if (member.Value != null)
                    {
                        if (!(Constant(member.Value) is LogicVec given) || !given.TryGetUInt64(out value))
                            return;   // worked out: none of its members is a constant here
                    }
                    _members[member.Name] = LogicVec.FromUInt64(value, Math.Max(1, width));
                    value++;
                }
            }

            public ControllerReading Read(out string why)
            {
                // Whole only where every item is a declaration or a process the reading takes: an assign, an instance, a
                // gate, a function, a generate block, an initial block or a wire given a value as it is declared is
                // something the diagram does not show (the review of 15h-6e).
                var processes = new List<ProceduralBlock>();
                bool other = false;
                foreach (ModuleItem item in _module.Items)
                {
                    switch (item)
                    {
                        case ProceduralBlock p when p.Kind != ProcessKind.Initial && p.Kind != ProcessKind.Final:
                            processes.Add(p);
                            break;
                        case DataDeclaration d:
                            foreach (Declarator name in d.Declarators)
                                other |= name.Initializer != null && d.NetType != null;
                            break;
                        case TypedefDeclaration _:
                        case ParameterDeclaration _:
                        case PortItemDeclaration _:
                        case ImportDeclaration _:
                            break;
                        default:
                            other = true;
                            break;
                    }
                }

                var used = new HashSet<ProceduralBlock>();
                MachineReading machine = ReadMachine(processes, used);
                var tables = new List<TableReading>();
                foreach (ProceduralBlock p in processes)
                {
                    if (used.Contains(p) || CombBody(p) == null)
                        continue;
                    TableReading table = ReadTable(p, machine?.Register);
                    if (table != null)
                    {
                        tables.Add(table);
                        used.Add(p);
                    }
                }

                bool whole = !other && !_partial && used.Count == processes.Count;
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
                    Expression resetWhen = null;
                    if (body is IfStatement reset && reset.Else != null)
                    {
                        foreach (Statement s in Flatten(reset.Then))
                            if (s is AssignmentStatement a && a.IsNonBlocking && SimpleName(a.Target) is string target)
                                resets[target] = a.Value;
                        rest = Single(reset.Else);
                        resetWhen = reset.Condition;
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
                            if (comb == register || !(CombBody(comb) is Statement combBody))
                                continue;
                            if (!(CaseOn(combBody, s1, out List<AssignmentStatement> defaults) is CaseStatement arms) || !Assigns(arms, n))
                                continue;
                            used.Add(register);
                            used.Add(comb);
                            // Whole only if the process keeps nothing else: each other register takes a value the case works
                            // out (count <= count_next), and the reset only constants.
                            foreach (Statement kept in Flatten(rest))
                                _partial |= !(kept is AssignmentStatement k) || !k.IsNonBlocking || k.Operator != "<="
                                    || !(SimpleName(k.Value) is string from) || SimpleName(k.Target) != s1 && !AssignsIn(combBody, from);
                            foreach (Expression given in resets.Values)
                                _partial |= Constant(given) == null && StateNamed(given) == null;
                            return Machine(s1, n, StartOf(s1, resets), defaults, arms, blocking: true, processes, used);
                        }
                    }

                    // One process: case (S) in the always_ff itself, after nonblocking defaults, setting S.
                    foreach (Statement statement in Flatten(rest))
                        if (statement is CaseStatement c && SimpleName(c.Subject) is string subject && Assigns(c, subject)
                            && CaseOn(rest, subject, out List<AssignmentStatement> ownDefaults, nonBlocking: true) is CaseStatement own)
                        {
                            used.Add(register);
                            return Machine(subject, subject, StartOf(subject, resets), ownDefaults, own, blocking: false, processes, used,
                                resetWhen != null ? LetGo(resetWhen) : null);
                        }
                }
                return null;
            }

            /// <summary>
            /// The value that lets a reset written <paramref name="when"/> go: <c>rst</c> at 0, <c>!rst_n</c> or <c>~rst_n</c> at 1,
            /// for a signal of one bit; null for any other test, which the check then does not hold.
            /// </summary>
            private ReadValue? LetGo(Expression when)
            {
                when = Unparenthesized(when);
                ulong idle = 0;
                if (when is UnaryExpression not && (not.Operator == "!" || not.Operator == "~"))
                {
                    when = Unparenthesized(not.Operand);
                    idle = 1;
                }
                if (!(SimpleName(when) is string signal) || WidthOf(signal) > 1)
                    return null;
                return new ReadValue(signal, idle == 1 ? "1" : "0", LogicVec.FromUInt64(idle, 1), 1);
            }

            /// <summary>
            /// What a register starts at: its reset's value where the process is <c>if (reset) ... else ...</c>, else the value it
            /// is declared with, else nothing known -- never a guess (the review of 15h-6e: a reset after the next value read as
            /// the first state).
            /// </summary>
            private Expression StartOf(string register, Dictionary<string, Expression> resets) =>
                resets.TryGetValue(register, out Expression reset) ? reset : _initial.TryGetValue(register, out Expression initial) ? initial : null;

            /// <summary>The name an expression gives a state or a constant, a package's included: <c>IDLE</c>, <c>p::IDLE</c>.</summary>
            private static string StateNamed(Expression e) =>
                Unparenthesized(e) is NameExpression n && n.Path.Count == 1 ? n.Name : null;

            private MachineReading Machine(string register, string next, Expression start, List<AssignmentStatement> defaults, CaseStatement arms,
                bool blocking, List<ProceduralBlock> processes, HashSet<ProceduralBlock> used, ReadValue? letGo = null)
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
                }
                // Of an enum or of labels alike (the 16d sweep: an empty enum read as a machine of no states, whose layout threw).
                if (states.Count < 2)
                    throw new NotReadable($"A case on {register} of fewer than two states is no state machine.");
                int startState = StateOf(start, states, codes) ?? -1;

                // Every other always_comb that is a case on the register only gives outputs: the three-process style.
                var outputArms = new List<(List<AssignmentStatement> Defaults, CaseStatement Arms)>();
                foreach (ProceduralBlock comb in processes)
                    if (!used.Contains(comb) && CombBody(comb) is Statement combBody && CaseOn(combBody, register, out List<AssignmentStatement> d) is CaseStatement c
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
                    List<Way> ways = Walk(ArmFor(arms, s, states, codes), Start(defaultValues), blocking);
                    var targets = new List<(int To, Way Way, List<string> Passed, List<string> Failed)>();
                    foreach (Way way in ways)
                    {
                        Expression target = way.Values.TryGetValue(next, out Expression t) ? t : null;
                        foreach ((int index, List<string> passed, List<string> failed) in Targets(target, register, s, states, codes))
                            targets.Add((index, way, passed, failed));
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
                        Settle(set, Walk(ArmFor(c, s, states, codes), Start(outDefaults), blocking: true), outDefaults, next);
                    }
                    settings.Add(set);
                }
                return new MachineReading(register, next, states, codes, startState, arrows, settings, letGo);
            }

            /// <summary>
            /// The states <paramref name="target"/> names -- a state, the register for staying, nothing for holding, or a ?: of
            /// them -- each with what chooses it.
            /// </summary>
            private IEnumerable<(int To, List<string> Passed, List<string> Failed)> Targets(Expression target, string register, int here,
                List<string> states, List<LogicVec> codes)
            {
                target = Unparenthesized(target);
                if (target == null || SimpleName(target) == register)
                {
                    yield return (here, new List<string>(), new List<string>());   // nothing sets it, or it is set to itself: it holds
                    yield break;
                }
                if (target is ConditionalExpression choice)
                {
                    foreach ((int to, List<string> passed, List<string> failed) in Targets(choice.WhenTrue, register, here, states, codes))
                    {
                        passed.Insert(0, Text(choice.Condition));
                        yield return (to, passed, failed);
                    }
                    foreach ((int to, List<string> passed, List<string> failed) in Targets(choice.WhenFalse, register, here, states, codes))
                    {
                        failed.Insert(0, Negated(choice.Condition));
                        yield return (to, passed, failed);
                    }
                    yield break;
                }
                if (!(StateOf(target, states, codes) is int index))
                    throw new NotReadable($"In {states[here]}, the next state is {Text(target)}, which is no state of {register}.");
                yield return (index, new List<string>(), new List<string>());
            }

            /// <summary>A condition failed, in the code's words: <c>!walk</c>, <c>walk</c> for <c>!walk</c>, <c>!(a == b)</c>.</summary>
            private static string Negated(Expression condition)
            {
                condition = Unparenthesized(condition);
                if (condition is UnaryExpression not && not.Operator == "!")
                    return Text(not.Operand);
                string text = Text(condition);
                return condition is NameExpression ? "!" + text : "!(" + text + ")";
            }

            /// <summary>The state <paramref name="e"/> is -- by its name, or by the value it is -- or null for none.</summary>
            private int? StateOf(Expression e, List<string> states, List<LogicVec> codes)
            {
                if (e == null)
                    return null;
                if (StateNamed(e) is string name && states.IndexOf(name) is int i && i >= 0)
                    return i;
                if (Constant(e) is LogicVec v && v.TryGetUInt64(out ulong value))
                    for (int s = 0; s < codes.Count; s++)
                        if (codes[s].TryGetUInt64(out ulong code) && code == value)
                            return s;
                return null;
            }

            /// <summary>A state's name as a label gives it: the constant it names, or the number as written.</summary>
            private static string LabelName(Expression label) => StateNamed(label) ?? Text(label, sized: true);

            /// <summary>
            /// The arrows out of state <paramref name="from"/>: one to each state its ways reach, labelled with what takes it, and
            /// carrying what the ways that take it set of <paramref name="varying"/> -- the outputs that differ by way.
            /// </summary>
            private IEnumerable<MachineArrow> Arrows(int from, List<(int To, Way Way, List<string> Passed, List<string> Failed)> targets,
                List<string> varying, Dictionary<string, Expression> defaults)
            {
                var order = new List<int>();
                var labels = new Dictionary<int, List<string>>();
                var taking = new Dictionary<int, List<Way>>();
                var always = new HashSet<int>();
                foreach ((int to, Way way, List<string> passed, List<string> failed) in targets)
                {
                    if (!labels.ContainsKey(to))
                    {
                        order.Add(to);
                        labels[to] = new List<string>();
                        taking[to] = new List<Way>();
                    }
                    if (!taking[to].Contains(way))
                        taking[to].Add(way);
                    // What was passed, then what was failed on the way; a way that only failed tests is the otherwise of the
                    // state's arrows -- the ways part the inputs between them, so it is exactly what no other arrow takes.
                    var parts = new List<string>(way.Conditions);
                    parts.AddRange(passed);
                    var fails = new List<string>(way.Negations);
                    fails.AddRange(failed);
                    string label = parts.Count > 0 ? string.Join(" and ", parts.Concat(fails))
                        : fails.Count > 0 ? MachineArrow.Otherwise : null;
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
                return new ReadValue(name, text ?? "", only != null ? Constant(only) : null, WidthOf(name));
            }

            /// <summary>Everything <paramref name="ways"/> set <paramref name="name"/> to, one after another: <c>1 or 0</c>.</summary>
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
                return new ReadValue(name, string.Join(" or ", texts), null, WidthOf(name));
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
                    var value = new ReadValue(name, string.Join(" or ", texts), texts.Count == 1 && only != null ? Constant(only) : null, WidthOf(name));
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
                if (!(CaseOnAnything(CombBody(process), out List<AssignmentStatement> defaults) is CaseStatement table))
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

                // A decoder sets numbers -- a control value, a parameter, an enum's member -- mostly: a case of signals is a mux
                // on the data, drawn, not tabled (the review of 15h-6e: case (sel) 0: y = a; 1: y = b read as a table).
                int constants = 0, cells = 0;
                foreach ((CaseItem _, Dictionary<string, Expression> sets) in arms)
                    foreach (Expression v in sets.Values)
                    {
                        cells++;
                        if (Constant(v) != null || Unparenthesized(v) is ConditionalExpression)
                            constants++;
                    }
                if (constants * 2 < cells)
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
                    var row = new List<TableCell>();
                    foreach (string column in columns)
                    {
                        if (sets.TryGetValue(column, out Expression v))
                            row.Add(new TableCell(Text(v), Constant(v), false));
                        else if (defaultValues.TryGetValue(column, out Expression d))
                            row.Add(new TableCell(Text(d), Constant(d), true));
                        else
                            row.Add(new TableCell("", null, true));
                    }
                    rows.Add(new TableRow(item.IsDefault ? MachineArrow.Otherwise : Join(labels), matches, item.IsDefault, row));
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
                        {
                            // y = 1; z = y: z is 1 on this way, as the engine has it, not "y".
                            Expression value = a.Value;
                            if (SimpleName(value) is string copied && copied != target && way.Values.TryGetValue(copied, out Expression earlier))
                                value = earlier;
                            way.Values[target] = value;
                        }
                        return ways;
                    case IfStatement choice:
                    {
                        string condition = Text(choice.Condition), failed = Negated(choice.Condition);
                        var then = new List<Way>();
                        var otherwise = new List<Way>();
                        foreach (Way way in ways)
                        {
                            Way t = way.Copy();
                            t.Conditions.Add(condition);
                            then.Add(t);
                            Way e = way.Copy();
                            e.Negations.Add(failed);
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
                        var every = new List<string>();
                        foreach (CaseItem item in inner.Items)
                            foreach (Expression label in item.Labels)
                                every.Add(Text(label, sized: true));
                        string none = subject + " is none of " + Join(every);
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
                                    w.Negations.Add(none);
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
                                w.Negations.Add(none);
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
                    if (a.IsNonBlocking != blocking && a.Operator == (blocking ? "=" : "<=") && SimpleName(a.Target) is string target)
                        values[target] = a.Value;
                return values;
            }

            /// <summary>
            /// The arm state <paramref name="state"/> takes: the first item one of whose labels names it or is its value -- an
            /// enum's A = 1 is taken by 2'd1: as well as by A: (the review of 15h-6e) -- else <c>default</c>'s, else none.
            /// </summary>
            private Statement ArmFor(CaseStatement arms, int state, List<string> states, List<LogicVec> codes)
            {
                Statement fallback = null;
                codes[state].TryGetUInt64(out ulong code);
                foreach (CaseItem item in arms.Items)
                {
                    if (item.IsDefault)
                        fallback = item.Body;
                    foreach (Expression label in item.Labels)
                        if (LabelName(label) == states[state] || Constant(label) is LogicVec v && v.TryGetUInt64(out ulong value) && value == code)
                            return item.Body;
                }
                return fallback;
            }

            /// <summary>
            /// What a combinational process runs: an <c>always_comb</c>'s body, or an <c>always @(*)</c>'s after its event;
            /// null for any other process.
            /// </summary>
            private static Statement CombBody(ProceduralBlock process) =>
                process.Kind == ProcessKind.AlwaysComb ? process.Body
                : process.Kind == ProcessKind.Always && process.Body is TimedStatement timed && timed.Control is EventControl e && e.Implicit ? timed.Body
                : null;

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
                if (body == null)
                    return null;
                CaseStatement found = null;
                foreach (Statement s in Flatten(body))
                {
                    if (found == null && s is AssignmentStatement a && a.IsNonBlocking == nonBlocking && a.IntraAssignment == null
                        && a.Operator == (nonBlocking ? "<=" : "=") && SimpleName(a.Target) != null)
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

            /// <summary>The value <paramref name="e"/> is where it is a number, a parameter written as one, or an enum's member; null otherwise.</summary>
            private LogicVec? Constant(Expression e, int depth = 0)
            {
                e = Unparenthesized(e);
                if (e is NumberLiteral literal && !literal.FillsContext)
                    return literal.Value;
                if (StateNamed(e) is string name)
                {
                    if (_members.TryGetValue(name, out LogicVec member))
                        return member;
                    if (depth < 8 && _parameters.TryGetValue(name, out Expression value) && value != null)
                        return Constant(value, depth + 1);
                }
                return null;
            }

            /// <summary>A signal's width as declared; 0 where it is not known.</summary>
            private int WidthOf(string signal) => _declared.TryGetValue(signal, out DataType type) ? Width(type) : 0;

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
