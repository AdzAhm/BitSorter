using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Levels and wiring shorthand shared by the level, blueprint and grading tests.
    /// </summary>
    /// <remarks>
    /// The levels here are built by parsing JSON rather than by calling the LevelDefinition
    /// constructor. That keeps every test honest about the real path: a definition that could only be
    /// produced in a test would prove nothing about a definition the loader produces.
    /// </remarks>
    internal static class LevelTestFixtures
    {
        /// <summary>Matches PlacementGrid's defaults: 9 cells across, 5 down.</summary>
        internal static readonly Vector2Int Board = new Vector2Int(4, 2);

        internal static readonly Vector2Int SourceCell = new Vector2Int(-3, 0);
        internal static readonly Vector2Int BinOneCell = new Vector2Int(3, 1);
        internal static readonly Vector2Int BinZeroCell = new Vector2Int(3, -1);
        internal static readonly Vector2Int MiddleCell = new Vector2Int(0, 0);

        /// <summary>
        /// The shape of level 1: one source emitting 0, two bins, a budget of one NOT. Solved by
        /// source -> NOT -> binOne, leaving binZero empty.
        /// </summary>
        internal static LevelDefinition Routing()
        {
            return Parse(@"{
                ""name"": ""Route the bit"",
                ""hint"": ""make the bit match the bin"",
                ""tickLimit"": 100,
                ""fixtures"": [
                    { ""id"": ""in"",      ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"":  0 }, ""stream"": ""0"" },
                    { ""id"": ""binOne"",  ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"":  1 } },
                    { ""id"": ""binZero"", ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"": -1 } }
                ],
                ""budget"": [ { ""kind"": ""Not"", ""count"": 1 } ],
                ""expected"": [
                    { ""sink"": ""binOne"",  ""values"": ""1"" },
                    { ""sink"": ""binZero"", ""values"": ""-"" }
                ]
            }");
        }

        /// <summary>
        /// A four-vector level whose sink expects a bit from every vector, for tests about sequences
        /// rather than about routing. The source feeds nothing by default.
        /// </summary>
        internal static LevelDefinition FourVectors(string expected)
        {
            return Parse($@"{{
                ""name"": ""Four vectors"",
                ""tickLimit"": 100,
                ""fixtures"": [
                    {{ ""id"": ""in"",  ""kind"": ""Source"", ""cell"": {{ ""x"": -3, ""y"": 0 }}, ""stream"": ""0011"" }},
                    {{ ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": {{ ""x"":  3, ""y"": 0 }} }}
                ],
                ""budget"": [ {{ ""kind"": ""Not"", ""count"": 2 }} ],
                ""expected"": [ {{ ""sink"": ""out"", ""values"": ""{expected}"" }} ]
            }}");
        }

        /// <summary>Parses a level and fails the test rather than the assertion if it is invalid.</summary>
        internal static LevelDefinition Parse(string json)
        {
            LevelLoadResult result = LevelLoader.Parse(json, Board);

            // A broken test fixture must not read as a failing rule.
            Assert.IsTrue(result.IsValid, $"test fixture level is invalid: {result.Error}");

            return result.Level;
        }

        /// <summary>Wires an output port on one cell to an input port on another.</summary>
        internal static void Wire(
            CircuitBlueprint blueprint,
            Vector2Int from,
            Vector2Int to,
            int fromPort = 0,
            int toPort = 0,
            int delay = 1)
        {
            blueprint.AddWire(new BlueprintWire(
                new CellPort(from, false, fromPort),
                new CellPort(to, true, toPort),
                delay));
        }

        /// <summary>Builds the circuit, runs it to a standstill, and grades it -- the whole Run cycle.</summary>
        internal static RunVerdict RunAndGrade(LevelDefinition level, CircuitBlueprint blueprint)
        {
            BuiltCircuit built = CircuitBuilder.Build(level, blueprint);

            return LevelGrader.RunToCompletion(built.Simulation, level, built.FixtureNodeIds);
        }
    }
}
