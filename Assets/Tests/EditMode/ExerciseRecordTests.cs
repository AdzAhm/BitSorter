using System.Collections.Generic;
using BitSorter.Board;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// An exercise's file read as the game reads it (Part 15b), through Unity's <c>JsonUtility</c>: its fields, and the
    /// defaults of those a file leaves out -- a delay of 1, no time limits, a count of any number. Under dotnet the same file
    /// is read by <c>System.Text.Json</c> (<c>ExerciseTests</c>); this holds the two to one shape.
    /// </summary>
    public class ExerciseRecordTests
    {
        private const string Json = @"{
            ""id"": ""half-adder"", ""title"": ""Half adder"", ""order"": 80, ""chapter"": 6,
            ""inputs"":  [ { ""name"": ""a"", ""values"": ""0011"" }, { ""name"": ""b"", ""values"": ""0101"" } ],
            ""outputs"": [ { ""name"": ""sum"", ""values"": ""0110"" }, { ""name"": ""carry"", ""values"": ""0001"" } ],
            ""parts"":   [ { ""kind"": ""Xor"", ""count"": 1 }, { ""kind"": ""And"" } ]
        }";

        [Test]
        public void JsonUtility_ReadsAnExercise_WithTheDefaultsItLeavesOut()
        {
            ExerciseRecord record = JsonUtility.FromJson<ExerciseRecord>(Json);
            Assert.AreEqual("half-adder", record.id);
            Assert.AreEqual(2, record.inputs.Length);
            Assert.AreEqual(1, record.inputs[0].width, "a bit wide unless it says");
            Assert.AreEqual(1L, record.delay);
            Assert.AreEqual(-1L, record.settleBy);
            Assert.AreEqual(-1L, record.period);
            Assert.AreEqual(-1, record.parts[1].count, "any number, where it gives no count");

            var problems = new List<string>();
            Exercise exercise = Exercise.From(record, problems);
            Assert.IsNotNull(exercise, string.Join("\n", problems));
            Assert.IsNotNull(exercise.ToProject(problems), string.Join("\n", problems));
            GateLibrary library = exercise.Library();
            Assert.AreEqual(1, library.MostOf("xor"));
            Assert.AreEqual(-1, library.MostOf("and"), "stocked, any number of it");
            Assert.AreEqual(0, library.MostOf(PartCount.Register), "no register where none is stocked");
        }
    }
}
