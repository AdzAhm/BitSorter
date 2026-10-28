using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The exercises as the game ships them (Part 15c): every file in Assets/Resources/Exercises, read as Unity reads it, and
    /// each one's reference solution in Tools/ci/Exercises/solutions. Shared with the oracle's tests.
    /// </summary>
    public static class ExerciseFiles
    {
        private static readonly JsonSerializerOptions Fields = new JsonSerializerOptions { IncludeFields = true };

        /// <summary>The repository's root: the folder above this one that holds both Assets and Tools.</summary>
        public static string Root
        {
            get
            {
                for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "Assets")) && Directory.Exists(Path.Combine(dir.FullName, "Tools")))
                        return dir.FullName;
                throw new DirectoryNotFoundException("No folder above the tests holds both Assets and Tools.");
            }
        }

        public static string Folder => Path.Combine(Root, "Assets", "Resources", "Exercises");

        public static string Solutions => Path.Combine(Root, "Tools", "ci", "Exercises", "solutions");

        /// <summary>Every exercise's id: its file's name.</summary>
        public static IEnumerable<string> Ids() =>
            Directory.EnumerateFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(id => id, StringComparer.Ordinal);

        public static ExerciseRecord Record(string id) =>
            JsonSerializer.Deserialize<ExerciseRecord>(File.ReadAllText(Path.Combine(Folder, id + ".json")), Fields);

        /// <summary>The exercise in <paramref name="id"/>'s file, failing the test with what is wrong with it.</summary>
        public static Exercise Load(string id)
        {
            var problems = new List<string>();
            Exercise exercise = Exercise.From(Record(id), problems);
            Assert.IsNotNull(exercise, string.Join("\n", problems));
            return exercise;
        }

        /// <summary>Every exercise, in the order it is played.</summary>
        public static List<Exercise> InOrder() => Ids().Select(Load).OrderBy(e => e.Order).ToList();

        /// <summary>The reference solution of <paramref name="exercise"/>, drawn from its file.</summary>
        public static Project Solution(Exercise exercise)
        {
            string path = Path.Combine(Solutions, exercise.Id + ".sv");
            Assert.IsTrue(File.Exists(path), $"{exercise.Id} has no reference solution at {path}");
            SyntaxTree tree = SyntaxReader.ReadText(File.ReadAllText(path), exercise.Id + ".sv");
            Assert.IsFalse(tree.HasErrors, string.Join("\n", tree.Diagnostics));
            ImportResult imported = BoardImporter.Import(tree, exercise.Module);
            Assert.IsTrue(imported.Succeeded, string.Join("\n", imported.Problems));
            return imported.Project;
        }
    }
}
