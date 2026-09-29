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
        internal static LevelDefinition FourVectors(string expected, int maxLatency = 0)
        {
            return Parse($@"{{
                ""name"": ""Four vectors"",
                ""tickLimit"": 100,
                ""maxLatency"": {maxLatency},
                ""fixtures"": [
                    {{ ""id"": ""in"",  ""kind"": ""Source"", ""cell"": {{ ""x"": -3, ""y"": 0 }}, ""stream"": ""0011"" }},
                    {{ ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": {{ ""x"":  3, ""y"": 0 }} }}
                ],
                ""budget"": [ {{ ""kind"": ""Not"", ""count"": 2 }} ],
                ""expected"": [ {{ ""sink"": ""out"", ""values"": ""{expected}"" }} ]
            }}");
        }

        /// <summary>
        /// The same four vectors on a clock: one vector every <paramref name="clockPeriod"/> ticks.
        /// </summary>
        internal static LevelDefinition FourVectorsOnAClock(
            string expected, int clockPeriod, int maxLatency = 0)
        {
            return Parse($@"{{
                ""name"": ""Four vectors on a clock"",
                ""tickLimit"": 100,
                ""clockPeriod"": {clockPeriod},
                ""maxLatency"": {maxLatency},
                ""fixtures"": [
                    {{ ""id"": ""in"",  ""kind"": ""Source"", ""cell"": {{ ""x"": -3, ""y"": 0 }}, ""stream"": ""0011"" }},
                    {{ ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": {{ ""x"":  3, ""y"": 0 }} }}
                ],
                ""budget"": [ {{ ""kind"": ""Not"", ""count"": 2 }} ],
                ""expected"": [ {{ ""sink"": ""out"", ""values"": ""{expected}"" }} ]
            }}");
        }

        /// <summary>
        /// A level that opens on a circuit: a NOT at the middle cell, wired from the source and into
        /// the bin. Stocks one NOT, which the start uses.
        /// </summary>
        internal static LevelDefinition WithAStart() => Parse(@"{
                ""name"": ""Starts built"", ""hint"": ""a hint"", ""tickLimit"": 40,
                ""fixtures"": [
                    { ""id"": ""in"",  ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""1"" },
                    { ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"": 0 } }
                ],
                ""budget"": [ { ""kind"": ""Not"", ""count"": 1 } ],
                ""expected"": [ { ""sink"": ""out"", ""values"": ""0"" } ],
                ""start"": {
                    ""gates"": [ { ""kind"": ""Not"", ""cell"": { ""x"": 0, ""y"": 0 } } ],
                    ""wires"": [
                        { ""from"": { ""x"": -3, ""y"": 0 }, ""to"": { ""x"": 0, ""y"": 0 }, ""delay"": 1 },
                        { ""from"": { ""x"":  0, ""y"": 0 }, ""to"": { ""x"": 3, ""y"": 0 }, ""delay"": 1 }
                    ]
                }
            }");

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

        /// <summary>A level's starting circuit, on a board of its own to edit.</summary>
        internal static CircuitBlueprint FromStart(LevelDefinition level)
        {
            Assert.IsTrue(level.HasStart, "sanity: the level opens on an empty board");

            var board = new CircuitBlueprint();
            board.Restore(level.Start);
            return board;
        }

        /// <summary>Re-times the wire into one input from one output, as a scroll over it would.</summary>
        internal static void Retime(CircuitBlueprint board, Vector2Int from, Vector2Int to, int toPort, int delay)
        {
            int index = board.IndexOfWire(new CellPort(from, false, 0), new CellPort(to, true, toPort));
            Assert.GreaterOrEqual(index, 0, $"sanity: no wire from {from} into {to} port {toPort}");

            board.SetDelayAt(index, delay);
        }

        /// <summary>Takes the wire into one input from one output off the board.</summary>
        internal static void Unwire(CircuitBlueprint board, Vector2Int from, Vector2Int to, int toPort)
        {
            int index = board.IndexOfWire(new CellPort(from, false, 0), new CellPort(to, true, toPort));
            Assert.GreaterOrEqual(index, 0, $"sanity: no wire from {from} into {to} port {toPort}");

            board.RemoveWireAt(index);
        }

        /// <summary>Builds the circuit, runs it to a standstill, and grades it -- the whole Run cycle.</summary>
        internal static RunVerdict RunAndGrade(LevelDefinition level, CircuitBlueprint blueprint)
        {
            BuiltCircuit built = CircuitBuilder.Build(level, blueprint);

            return LevelGrader.RunToCompletion(built.Simulation, level, built.FixtureNodeIds);
        }
    }
}
