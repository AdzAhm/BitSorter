using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>What EXPORT FOR SUBMISSION made: the zip, or why there is none, which files it filled, and what they use that it does not hold.</summary>
    public sealed class Submission
    {
        internal Submission(byte[] zip, IReadOnlyList<string> files, IReadOnlyList<string> notHandedIn, string refusal)
        {
            Zip = zip;
            Files = files;
            NotHandedIn = notHandedIn;
            Refusal = refusal;
        }

        /// <summary>The zip, or null when it was refused.</summary>
        public byte[] Zip { get; }

        /// <summary>The files filled, by their template's name.</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>
        /// The project's modules the files filled use, directly or through each other, and that the zip does not hold: the
        /// course's own, which the grader has, or one the student made here, which the grader does not -- the project cannot
        /// tell which, so the student is told.
        /// </summary>
        public IReadOnlyList<string> NotHandedIn { get; }

        /// <summary>Why there is no zip, or null.</summary>
        public string Refusal { get; }

        public bool Succeeded => Zip != null;
    }

    /// <summary>
    /// EXPORT FOR SUBMISSION (Part 13e): every module that came from a course template handed back as the course gave it,
    /// with the student's code between its markers (<see cref="CourseTemplate"/>) -- under the template's own file name,
    /// every given name kept. What the student wrote anywhere else is not the template's, so the export is refused, saying
    /// which module and why, rather than hand in something that silently left it out: a fault, ports that are no longer the
    /// template's, markers taken out, or a change outside them.
    /// </summary>
    public static class SubmissionExport
    {
        private static readonly Regex ModuleName = new Regex(@"(?m)^[ \t]*module\s+([A-Za-z_][A-Za-z0-9_$]*)", RegexOptions.CultureInvariant);
        private static readonly Regex Space = new Regex(@"\s+", RegexOptions.CultureInvariant);

        public static Submission Build(Project project, DateTime when)
        {
            Submission Refused(string why) => new Submission(null, Array.Empty<string>(), Array.Empty<string>(), why);

            var entries = new List<ZipEntry>();
            var names = new List<string>();
            var handedIn = new List<Module>();
            var filled = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);   // by file name, the module each is filled from
            var utf8 = new UTF8Encoding(false);
            string folder = ProjectExport.FolderOf(project) + "-submission";
            foreach (Module module in project.Modules)
            {
                if (module.Template == null)
                    continue;
                string file = module.Template.File;
                if (!ProjectFiles.IsValidName(file, out _))
                    return Refused($"{module.Name}'s template has no name a file can have ({file}): import it again.");
                if (filled.TryGetValue(file, out Module other))
                    return Refused(other.Template.Text == module.Template.Text
                        ? $"{file} holds two modules to write, {other.Name} and {module.Name}; a submission fills one a file, so it cannot hand {file} in."
                        : $"{other.Name} and {module.Name} both came from a file named {file}: import one homework's files at a time to hand them in.");
                filled[file] = module;

                if (!CourseTemplate.TryRead(module.Template.Text, out CourseTemplate template) || !CourseTemplate.TryRead(module.Template.Given, out CourseTemplate given))
                    return Refused($"{file}, {module.Name}'s template, no longer says where the code goes: import it again.");
                if (!module.IsWritten)
                    return Refused($"{module.Name} is drawn; a template's module is handed in as code.");

                string region = CourseTemplate.RegionIn(module.Source);
                if (region == null)
                    return Refused($"{module.Name}'s code has lost the template's markers: put // Put your code here back above your code" +
                        (template.HasEndMarker ? ", and // End of your code after it." : "."));

                if (WrittenModules.Read(project, module).FirstError is Diagnostic fault)
                    return Refused($"{module.Name} has a fault on line {fault.Location.Line}: {fault.Message} Mend it before handing it in.");

                // The header is outside the markers too, so ports come first: they say what changed, where the next says only where.
                string ports = PortsDiffer(project, module);
                if (ports != null)
                    return Refused(ports);

                if (!SameOutside(given, module.Source))
                    return Refused($"{module.Name} was changed outside the template's markers, its code or its comments. The submission hands in " +
                        $"{file} as the course gave it, with only what is between the markers yours: move the change between them.");

                string code = WithoutWhatTheImportAdded(region, template.Region, given.Region);
                entries.Add(new ZipEntry(folder + "/" + file, utf8.GetBytes(template.Fill(code))));
                names.Add(file);
                handedIn.Add(module);
            }

            if (entries.Count == 0)
                return Refused("No module here came from a course template: import the course's files to hand them in filled.");
            return new Submission(ZipWriter.Write(entries, when), names, NotHandedIn(project, handedIn), null);
        }

        /// <summary>
        /// Whether the text outside the markers is still what the template gave the module (<see cref="ModuleTemplate.Given"/>):
        /// its code and its comments, spacing and line endings aside. A comment changed there is a change too, since what is
        /// handed in there is the course's, and a student's note in it would be lost without a word.
        /// </summary>
        private static bool SameOutside(CourseTemplate given, string source)
        {
            CourseTemplate.TryRead(source, out CourseTemplate written);
            return Spaced(given.Text.Substring(0, given.RegionStart)) == Spaced(source.Substring(0, written.RegionStart))
                && Spaced(given.Text.Substring(given.RegionEnd)) == Spaced(source.Substring(written.RegionEnd));
        }

        private static string Spaced(string text) => Space.Replace(text, " ").Trim();

        /// <summary>
        /// The student's code without the lines the import put in the template's place, which the file holds already where the
        /// course had them: an include of declarations moved into the module lands there when the file has no end marker, as
        /// all three of the course's that include a file have none. Each line taken once, so the student's own copy stays.
        /// </summary>
        private static string WithoutWhatTheImportAdded(string region, string asFiled, string asImported)
        {
            var filed = new List<string>(Lines(asFiled));
            var added = new List<string>();
            foreach (string line in Lines(asImported))
                if (!filed.Remove(line))
                    added.Add(line);
            if (added.Count == 0)
                return region;

            var kept = new StringBuilder();
            foreach (string line in region.Replace("\r\n", "\n").Split('\n'))
                if (!added.Remove(line.Trim()))
                    kept.Append(line).Append('\n');
            return kept.ToString();
        }

        private static IEnumerable<string> Lines(string text)
        {
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                if (line.Trim().Length > 0)
                    yield return line.Trim();
        }

        /// <summary>The project's modules <paramref name="handedIn"/> use, directly or through each other, that are not handed in, each once.</summary>
        private static IReadOnlyList<string> NotHandedIn(Project project, List<Module> handedIn)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Module module in handedIn)
                seen.Add(module.Name);

            var next = new Queue<Module>(handedIn);
            while (next.Count > 0)
                foreach (Module used in WrittenModules.UsesOf(project, next.Dequeue()))
                    if (seen.Add(used.Name))
                    {
                        names.Add(used.Name);
                        next.Enqueue(used);
                    }

            return names;
        }

        /// <summary>
        /// How <paramref name="module"/>'s ports differ from its template's, or null when they are the same: names, directions
        /// and widths, in order -- or its name, when it is no longer the one the template gives it.
        /// </summary>
        private static string PortsDiffer(Project project, Module module)
        {
            Match named = ModuleName.Match(module.Template.Given);
            string givenName = named.Success ? named.Groups[1].Value : module.Name;
            if (!string.Equals(givenName, module.Name, StringComparison.Ordinal))
                return $"{module.Name} is {givenName} in {module.Template.File}, and is graded by that name: rename it back to {givenName}.";

            var asGiven = new Module { Name = module.Name, Source = module.Template.Given };
            IReadOnlyList<ModulePort> was = WrittenModules.PortsOf(project.WithModule(asGiven), asGiven);
            IReadOnlyList<ModulePort> now = WrittenModules.PortsOf(project, module);

            string Describe(IReadOnlyList<ModulePort> ports)
            {
                var parts = new List<string>();
                foreach (ModulePort port in ports)
                    parts.Add((port.Direction == PortDirection.Input ? "input " : port.Direction == PortDirection.Output ? "output " : "inout ") +
                        (port.Width > 1 ? "[" + (port.Width - 1) + ":0] " : "") + port.Name);
                return string.Join(", ", parts);
            }

            string before = Describe(was), after = Describe(now);
            return before == after ? null
                : $"{module.Name}'s ports are no longer {module.Template.File}'s: it has ({after}) where the template has ({before}). Put them back as the template gives them.";
        }
    }
}
