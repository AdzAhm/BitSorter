using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// One parameter of a testbench that picks among named choices (Part 15h-6c): a processor's PROGRAM, a parameter of an enum
    /// type -- what the workbench offers as a chip, PROGRAM: Fibonacci, and runs with what is chosen.
    /// </summary>
    public sealed class BenchChoice
    {
        public BenchChoice(string name, IReadOnlyList<string> members, string declared, string chosen, IReadOnlyList<long?> values = null)
        {
            Name = name;
            Members = members;
            Declared = declared;
            Chosen = chosen;
            Values = values ?? new long?[members.Count];
        }

        /// <summary>
        /// Each member's value, as a number -- 0, 1, 2 for an enum that gives none -- or null where the code works it out
        /// another way. What an exported run.do gives ModelSim's -G, which reads a member's name as a string of characters
        /// and runs another program without a word (measured, 2026-11-02).
        /// </summary>
        public IReadOnlyList<long?> Values { get; }

        /// <summary>The value of the member chosen, or null when it has none as a number.</summary>
        public long? ChosenValue
        {
            get
            {
                for (int i = 0; i < Members.Count; i++)
                    if (Members[i] == Chosen)
                        return Values[i];
                return null;
            }
        }

        /// <summary>The parameter's name: <c>PROGRAM</c>.</summary>
        public string Name { get; }

        /// <summary>Its enum's members, in the order declared: <c>SUM</c>, <c>FIBONACCI</c>, <c>ISA</c>.</summary>
        public IReadOnlyList<string> Members { get; }

        /// <summary>The member its declaration gives it, or null when that is no member by name.</summary>
        public string Declared { get; }

        /// <summary>The member the run takes: the one the project chose, else the declared one.</summary>
        public string Chosen { get; }

        /// <summary>A member as the chip says it: <c>FIBONACCI</c> is Fibonacci, <c>BIT_SERIAL</c> Bit serial.</summary>
        public static string Said(string member)
        {
            if (string.IsNullOrEmpty(member))
                return "";
            string words = member.Replace('_', ' ').ToLowerInvariant();
            return char.ToUpperInvariant(words[0]) + words.Substring(1);
        }
    }

    /// <summary>
    /// What a testbench lets the run choose, and the values the run is given for it (Part 15h-6c): the top's own parameters
    /// of an enum type -- those an instantiation could set: a #( ) list's, else the body's that are not localparams -- as
    /// the project's <see cref="ProjectSettings.BenchParameters"/> set them, an enum member named as the code names it.
    /// </summary>
    public static class BenchChoices
    {
        /// <summary>The top's parameters of an enum type, each with what is chosen; none for a project that does not compile.</summary>
        public static IReadOnlyList<BenchChoice> Of(CompiledProject compiled, ProjectSettings settings)
        {
            var result = new List<BenchChoice>();
            // A testbench's alone: a design run without one is run by the bench made from its stimulus, and what elaborates
            // it by itself -- LOGIC, STATES, TIMING -- takes no choice (the review of 15h-6c).
            if (compiled == null || !compiled.Succeeded || compiled.Testbench == null)
                return result;
            ModuleDeclaration top = Find(compiled.Tree, compiled.Top);
            if (top == null)
                return result;

            foreach (ParameterDeclaration p in Settable(top))
            {
                EnumType @enum = EnumOf(compiled.Tree, top, p.Type);
                List<string> members = MembersOf(@enum);
                if (members == null || members.Count == 0)
                    continue;
                string declared = p.Value is NameExpression name && name.Path.Count == 1 && members.Contains(name.Name) ? name.Name : null;
                string chosen = settings?.BenchParameters != null && settings.BenchParameters.TryGetValue(p.Name, out string given)
                    && members.Contains(given) ? given : declared;
                result.Add(new BenchChoice(p.Name, members, declared, chosen, ValuesOf(@enum)));
            }
            return result;
        }

        /// <summary>
        /// <paramref name="project"/> with each of <paramref name="chosen"/> -- a parameter's name, one of its members -- that its
        /// testbench offers set in its settings, as <see cref="BoardEditor.SetBenchParameter"/> sets one, but with no step and
        /// no run: a sample, made afresh as it opens, on the program last chosen on it (Ahmad, 2026-11-02). A name the bench
        /// has not, or a member it does not offer, is passed over, so what was remembered of an older sample never stops it
        /// opening.
        /// </summary>
        public static Project Applied(Project project, IReadOnlyDictionary<string, string> chosen)
        {
            if (project == null || chosen == null || chosen.Count == 0)
                return project;

            ProjectSettings settings = project.Settings ?? new ProjectSettings();
            Dictionary<string, string> values = null;
            foreach (BenchChoice choice in Of(BoardCompiler.Compile(project), settings))
            {
                if (!chosen.TryGetValue(choice.Name, out string member) || member == choice.Chosen || !Offers(choice, member))
                    continue;
                if (values == null)
                {
                    values = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (settings.BenchParameters != null)
                        foreach (KeyValuePair<string, string> pair in settings.BenchParameters)
                            values[pair.Key] = pair.Value;
                }
                values[choice.Name] = member;
            }
            return values == null ? project : project with { Settings = settings with { BenchParameters = values } };
        }

        private static bool Offers(BenchChoice choice, string member)
        {
            foreach (string each in choice.Members)
                if (each == member)
                    return true;
            return false;
        }

        /// <summary>
        /// The values <paramref name="given"/> sets on <paramref name="topName"/>'s own parameters, as the elaborator takes them:
        /// an enum member by name, a number as a literal. One the top cannot be given -- no such parameter, a localparam, a
        /// value that is neither -- is left out, so a testbench changed or replaced runs on its own values rather than failing.
        /// </summary>
        public static IReadOnlyDictionary<string, Expression> Values(SyntaxTree tree, string topName, IReadOnlyDictionary<string, string> given)
        {
            var values = new Dictionary<string, Expression>(StringComparer.Ordinal);
            if (tree == null || given == null || given.Count == 0)
                return values;
            ModuleDeclaration top = Find(tree, topName);
            if (top == null)
                return values;

            foreach (ParameterDeclaration p in Settable(top))
            {
                if (!given.TryGetValue(p.Name, out string value) || string.IsNullOrWhiteSpace(value))
                    continue;
                List<string> members = MembersOf(EnumOf(tree, top, p.Type));
                if (members != null && members.Contains(value))
                {
                    var name = new NameExpression();
                    name.Path.Add(value);
                    values[p.Name] = name;
                }
                else if (Literal.Parse(value) is LiteralResult number && number.Error == null)
                    values[p.Name] = new NumberLiteral { Text = value, Value = number.Value };
            }
            return values;
        }

        private static ModuleDeclaration Find(SyntaxTree tree, string name)
        {
            foreach (ModuleDeclaration module in tree.Modules)
                if (module.Name == name)
                    return module;
            return null;
        }

        /// <summary>The parameters an instantiation could set: a #( ) list's that are not localparams, else the body's.</summary>
        private static IEnumerable<ParameterDeclaration> Settable(ModuleDeclaration module)
        {
            foreach (ParameterDeclaration p in module.HeaderParameters)
                if (!p.IsLocal)
                    yield return p;
            if (module.HasParameterList)
                yield break;
            foreach (ModuleItem item in module.Items)
                if (item is ParameterDeclaration p && !p.IsLocal)
                    yield return p;
        }

        /// <summary>The enum <paramref name="type"/> is -- in place, or a typedef's in the module or outside every module -- or null.</summary>
        private static EnumType EnumOf(SyntaxTree tree, ModuleDeclaration module, DataType type)
        {
            EnumType @enum = type?.Enum;
            if (@enum == null && type?.Name != null && type.Package == null)
                @enum = TypedefIn(module.Items, type.Name) ?? TypedefIn(tree.UnitItems, type.Name);
            return @enum;
        }

        private static List<string> MembersOf(EnumType @enum)
        {
            if (@enum == null)
                return null;
            var members = new List<string>(@enum.Members.Count);
            foreach (EnumMember member in @enum.Members)
                members.Add(member.Name);
            return members;
        }

        /// <summary>
        /// Each member's value as the standard numbers them: the one written, else one more than the member before, from 0.
        /// Null from a value written as anything but a number on: what it is, only elaboration says.
        /// </summary>
        private static IReadOnlyList<long?> ValuesOf(EnumType @enum)
        {
            var values = new long?[@enum.Members.Count];
            long? next = 0;
            for (int i = 0; i < values.Length; i++)
            {
                Expression written = @enum.Members[i].Value;
                if (written != null)
                    next = written is NumberLiteral number && number.Value.TryGetUInt64(out ulong v) && v <= long.MaxValue ? (long)v : (long?)null;   // 4'd9 is 9, never -7
                values[i] = next;
                next = next + 1;
            }
            return values;
        }

        private static EnumType TypedefIn(IEnumerable<ModuleItem> items, string name)
        {
            foreach (ModuleItem item in items)
                if (item is TypedefDeclaration t && t.Name == name && t.Type?.Enum != null)
                    return t.Type.Enum;
            return null;
        }
    }
}
