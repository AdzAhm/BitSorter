using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using BitSorter.Ci;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The drawing of code linked to its code (Part 15h-5): every part of every written module's drawing -- the exercises' answers
    /// as WRITE IT writes them, the corpus imported as a student imports it -- comes from a line of the module's own code, and
    /// that line is the code making it: a port's names the port, an instance's its module or its name, a register's is an
    /// always; and each line's parts are the parts whose line it is.
    /// </summary>
    public class DrawingLinesTests
    {
        private static IEnumerable<(string What, Project Project)> Projects()
        {
            foreach (string id in ExerciseFiles.Ids())
            {
                Exercise exercise = ExerciseFiles.Load(id);
                Project solution = ExerciseFiles.Solution(exercise);
                yield return ("exercise " + id, solution.WithModule(exercise.Written(solution)));
            }

            string[] structural = Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/structural"), "*.sv").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            ProjectImport whole = ProjectImporter.Import(structural.Select(f => new SourceFile(Path.GetFileName(f), File.ReadAllText(f))).ToList());
            Assert.IsTrue(whole.Succeeded, "sanity: the structural corpus imports whole");
            yield return ("structural", whole.Project);

            foreach (string folder in new[] { "behavioural", "system" })
                foreach (string path in Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder), "*.sv").OrderBy(f => f, StringComparer.Ordinal))
                {
                    ProjectImport import = ProjectImporter.Import(new[] { new SourceFile(Path.GetFileName(path), File.ReadAllText(path)) });
                    if (import.Succeeded)
                        yield return (folder + " " + Path.GetFileName(path), import.Project);
                }
        }

        private static IEnumerable<(string Module, RtlDrawing Drawing)> Drawings(Project project, bool gates)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            if (!compiled.Succeeded)
                yield break;
            Elaboration e = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (e.HasErrors)
                yield break;
            var seen = new HashSet<string>();
            foreach (ScopeInfo scope in e.Design.Scopes)
            {
                if (scope.Kind != ScopeKind.Top && scope.Kind != ScopeKind.Instance)
                    continue;
                Module module = project.Find(scope.Module);
                if (module == null || !module.IsWritten || !seen.Add(scope.Module))
                    continue;
                yield return (scope.Module, RtlDrawing.Of(project, RtlExtractor.Of(e.Design, scope.Path), scope.Module, gates));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EveryPartOfEveryDrawing_ComesFromTheLineOfCodeThatMakesIt(bool gates)
        {
            int drawings = 0, parts = 0;
            var wrong = new List<string>();
            foreach ((string what, Project project) in Projects())
                foreach ((string name, RtlDrawing drawing) in Drawings(project, gates))
                {
                    drawings++;
                    string[] lines = project.Find(name).Source.Split('\n');
                    foreach (Part part in drawing.Module.Parts)
                    {
                        int line = drawing.LineOf(part.Id);
                        string where = $"{what} {name} {part.GetType().Name} {part.Name}";
                        if (line == 0)
                        {
                            if (!(part is BoxPart box && box.Undriven))
                                wrong.Add(where + ": no line");
                            continue;
                        }
                        parts++;
                        if (line < 1 || line > lines.Length)
                        {
                            wrong.Add(where + $": line {line} of {lines.Length}");
                            continue;
                        }
                        string text = lines[line - 1].Trim();
                        bool holds = part switch
                        {
                            PortPart port => text.Contains(port.Name, StringComparison.Ordinal),
                            BlockPart block => text.Contains(block.Module, StringComparison.Ordinal) || text.Contains(block.Name, StringComparison.Ordinal),
                            RegisterPart _ => text.Contains("always", StringComparison.Ordinal),
                            _ => text.Length > 0 && !text.StartsWith("//", StringComparison.Ordinal),
                        };
                        if (!holds)
                            wrong.Add(where + $": line {line} is \"{text}\"");
                        if (!drawing.PartsOn(line).Contains(part.Id))
                            wrong.Add(where + $": not among line {line}'s parts");
                    }
                }

            Assert.Greater(drawings, 40, "sanity: the exercises and the corpus drawn");
            Assert.Greater(parts, 500, "sanity: their parts given lines");
            Assert.IsEmpty(wrong, string.Join("\n", wrong.Take(40)));
        }
    }
}
