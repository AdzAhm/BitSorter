using System;
using System.Collections.Generic;
using BitSorter.Board;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The exercises the game ships (Part 15d): every file in Resources/Exercises, read by <c>JsonUtility</c> as
    /// <c>ExerciseRecordTests</c> holds it to, in the order they are played. Read once, the first time it is asked for. A
    /// file that is wrong is left out, saying why in the log -- <c>ExerciseCurriculumTests</c> fails it long before a build.
    /// </summary>
    public static class ExerciseCatalog
    {
        /// <summary>The folder under Resources the exercises are in.</summary>
        public const string Folder = "Exercises";

        private static List<Exercise> _run;

        /// <summary>Every exercise, in the order it is played.</summary>
        public static IReadOnlyList<Exercise> Run => _run ??= Load();

        private static List<Exercise> Load()
        {
            var run = new List<Exercise>();
            foreach (TextAsset file in Resources.LoadAll<TextAsset>(Folder))
            {
                var problems = new List<string>();
                Exercise exercise = null;
                try
                {
                    exercise = Exercise.From(JsonUtility.FromJson<ExerciseRecord>(file.text), problems);
                }
                catch (ArgumentException e)
                {
                    problems.Add(e.Message);
                }

                if (exercise == null)
                    Debug.LogError($"BitSorter: the exercise {file.name} was left out: {string.Join(" ", problems)}");
                else
                    run.Add(exercise);
            }

            run.Sort((a, b) => a.Order.CompareTo(b.Order));
            return run;
        }
    }
}
