using System.Collections;
using BitSorter.LogicCore;
using BitSorter.View;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// A block on the real board: chosen from its row, placed, wired at the ports on its box, run,
    /// scored, removed and brought back.
    /// </summary>
    /// <remarks>
    /// The levels are written here rather than shipped, so the behaviour is pinned apart from
    /// whichever levels come to stock a block. How a block is built and timed is
    /// <c>BlockBuildTests</c>; this is the board around it.
    /// </remarks>
    [TestFixture]
    public class BlockPlayTests
    {
        internal const string HalfAdderKey = "boxed-half-adder";
        internal const string FullAdderKey = "boxed-full-adder";

        /// <summary>The half adder's level, with one HA block in the parts list and no gates.</summary>
        internal const string HalfAdderJson = @"{
            ""name"": ""Boxed half adder"", ""hint"": ""a hint"", ""goal"": ""a goal"", ""tickLimit"": 40,
            ""fixtures"": [
                { ""id"": ""a"",     ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"":  1 }, ""stream"": ""0011"" },
                { ""id"": ""b"",     ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": -1 }, ""stream"": ""0101"" },
                { ""id"": ""sum"",   ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"":  1 } },
                { ""id"": ""carry"", ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"": -1 } }
            ],
            ""blocks"": [ {
                ""name"": ""HA"",
                ""inputs"":  [ { ""id"": ""a"", ""cell"": { ""x"": -2, ""y"": 1 } }, { ""id"": ""b"", ""cell"": { ""x"": -2, ""y"": -1 } } ],
                ""outputs"": [ { ""id"": ""s"", ""cell"": { ""x"":  2, ""y"": 1 } }, { ""id"": ""c"", ""cell"": { ""x"":  2, ""y"": -1 } } ],
                ""gates"": [ { ""kind"": ""Xor"", ""cell"": { ""x"": 0, ""y"": 1 } }, { ""kind"": ""And"", ""cell"": { ""x"": 0, ""y"": -1 } } ],
                ""wires"": [
                    { ""from"": { ""x"": -2, ""y"":  1 }, ""to"": { ""x"": 0, ""y"":  1 }, ""toPort"": 0 },
                    { ""from"": { ""x"": -2, ""y"": -1 }, ""to"": { ""x"": 0, ""y"":  1 }, ""toPort"": 1 },
                    { ""from"": { ""x"": -2, ""y"":  1 }, ""to"": { ""x"": 0, ""y"": -1 }, ""toPort"": 0 },
                    { ""from"": { ""x"": -2, ""y"": -1 }, ""to"": { ""x"": 0, ""y"": -1 }, ""toPort"": 1 },
                    { ""from"": { ""x"":  0, ""y"":  1 }, ""to"": { ""x"": 2, ""y"":  1 } },
                    { ""from"": { ""x"":  0, ""y"": -1 }, ""to"": { ""x"": 2, ""y"": -1 } }
                ]
            } ],
            ""budget"": [ { ""block"": ""HA"", ""count"": 1 } ],
            ""expected"": [ { ""sink"": ""sum"", ""values"": ""0110"" }, { ""sink"": ""carry"", ""values"": ""0001"" } ]
        }";

        /// <summary>A full adder in a box, three inputs and so two cells tall, on the wide board.</summary>
        internal const string FullAdderJson = @"{
            ""name"": ""Boxed full adder"", ""hint"": ""a hint"", ""goal"": ""a goal"", ""tickLimit"": 60,
            ""board"": { ""columns"": 13, ""rows"": 7 },
            ""fixtures"": [
                { ""id"": ""a"",    ""kind"": ""Source"", ""cell"": { ""x"": -6, ""y"": 2 }, ""stream"": ""00001111"" },
                { ""id"": ""b"",    ""kind"": ""Source"", ""cell"": { ""x"": -6, ""y"": 1 }, ""stream"": ""00110011"" },
                { ""id"": ""cin"",  ""kind"": ""Source"", ""cell"": { ""x"": -6, ""y"": 0 }, ""stream"": ""01010101"" },
                { ""id"": ""s"",    ""kind"": ""Sink"",   ""cell"": { ""x"":  6, ""y"": 2 } },
                { ""id"": ""cout"", ""kind"": ""Sink"",   ""cell"": { ""x"":  6, ""y"": 0 } }
            ],
            ""blocks"": [ {
                ""name"": ""FA"",
                ""inputs"":  [ { ""id"": ""a"",   ""cell"": { ""x"": -4, ""y"":  1 } }, { ""id"": ""b"", ""cell"": { ""x"": -4, ""y"": 0 } },
                               { ""id"": ""cin"", ""cell"": { ""x"": -4, ""y"": -1 } } ],
                ""outputs"": [ { ""id"": ""s"",   ""cell"": { ""x"":  4, ""y"":  1 } }, { ""id"": ""cout"", ""cell"": { ""x"": 4, ""y"": -1 } } ],
                ""gates"": [
                    { ""kind"": ""Xor"", ""cell"": { ""x"": -2, ""y"":  1 } }, { ""kind"": ""And"", ""cell"": { ""x"": -2, ""y"": -1 } },
                    { ""kind"": ""Xor"", ""cell"": { ""x"":  0, ""y"":  1 } }, { ""kind"": ""And"", ""cell"": { ""x"":  0, ""y"": -1 } },
                    { ""kind"": ""Or"",  ""cell"": { ""x"":  2, ""y"": -1 } }
                ],
                ""wires"": [
                    { ""from"": { ""x"": -4, ""y"":  1 }, ""to"": { ""x"": -2, ""y"":  1 }, ""toPort"": 0 },
                    { ""from"": { ""x"": -4, ""y"":  0 }, ""to"": { ""x"": -2, ""y"":  1 }, ""toPort"": 1 },
                    { ""from"": { ""x"": -4, ""y"":  1 }, ""to"": { ""x"": -2, ""y"": -1 }, ""toPort"": 0 },
                    { ""from"": { ""x"": -4, ""y"":  0 }, ""to"": { ""x"": -2, ""y"": -1 }, ""toPort"": 1 },
                    { ""from"": { ""x"": -2, ""y"":  1 }, ""to"": { ""x"":  0, ""y"":  1 }, ""toPort"": 0 },
                    { ""from"": { ""x"": -4, ""y"": -1 }, ""to"": { ""x"":  0, ""y"":  1 }, ""toPort"": 1, ""delay"": 2 },
                    { ""from"": { ""x"": -2, ""y"":  1 }, ""to"": { ""x"":  0, ""y"": -1 }, ""toPort"": 0 },
                    { ""from"": { ""x"": -4, ""y"": -1 }, ""to"": { ""x"":  0, ""y"": -1 }, ""toPort"": 1, ""delay"": 2 },
                    { ""from"": { ""x"": -2, ""y"": -1 }, ""to"": { ""x"":  2, ""y"": -1 }, ""toPort"": 0, ""delay"": 2 },
                    { ""from"": { ""x"":  0, ""y"": -1 }, ""to"": { ""x"":  2, ""y"": -1 }, ""toPort"": 1 },
                    { ""from"": { ""x"":  0, ""y"":  1 }, ""to"": { ""x"":  4, ""y"":  1 } },
                    { ""from"": { ""x"":  2, ""y"": -1 }, ""to"": { ""x"":  4, ""y"": -1 } }
                ]
            } ],
            ""budget"": [ { ""kind"": ""Not"", ""count"": 1 }, { ""block"": ""FA"", ""count"": 2 } ],
            ""expected"": [ { ""sink"": ""s"", ""values"": ""01101001"" }, { ""sink"": ""cout"", ""values"": ""00010111"" } ]
        }";

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        [TearDown]
        public void ClearPlantedSave() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        internal static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        internal static LevelDefinition Parse(string json)
        {
            LevelLoadResult result = LevelLoader.Parse(json, new Vector2Int(4, 2));
            Assert.IsTrue(result.IsValid, $"sanity: the test's level is invalid: {result.Error}");
            return result.Level;
        }

        /// <summary>The game past its menu, with the tutorial behind it.</summary>
        internal static IEnumerator TheGame()
        {
            yield return TestScene.Load();

            // A fresh save is exactly what the tutorial offers itself on.
            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);

            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;
        }

        internal static IEnumerator OnTheLevel(string json, string key)
        {
            Assert.IsTrue(Find<LevelSession>().Adopt(Parse(json), key), "sanity: the level was not taken");
            yield return null;
            yield return null;
        }

        /// <summary>Picks a block up from its row of the parts list and puts it down with its top on a cell.</summary>
        internal static void PlaceFromItsRow(string block, Vector2Int at)
        {
            GameObject row = GameObject.Find($"Part block {block}");
            Assert.IsNotNull(row, $"no row for {block} in the parts list");
            row.GetComponent<Button>().onClick.Invoke();

            PlacementController placement = Find<PlacementController>();
            Assert.IsNotNull(placement.SelectedBlock, "the row did not put the block in hand");
            Assert.AreEqual(block, placement.SelectedBlock.Name);

            Assert.IsTrue(Find<LevelSession>().TryPlaceBlock(placement.SelectedBlock, at), $"{block} was refused at {at}");
        }

        /// <summary>The placed block, as the last build expanded it.</summary>
        internal static BuiltBlock Block(int index = 0)
        {
            SimulationRunner runner = Find<SimulationRunner>();
            Assert.Greater(runner.Circuit.Blocks.Count, index, "sanity: no block on the board");
            return runner.Circuit.Blocks[index];
        }

        /// <summary>Wires a source to one of a block's inputs, by the port on its box.</summary>
        internal static void WireIn(string source, int port, int delay = 1, int block = 0)
        {
            SimulationRunner runner = Find<SimulationRunner>();
            var from = new PortAddress(runner.FixtureNodeIds[source], false, 0);
            var to = new PortAddress(Block(block).InputAnchors[port], true, 0);

            Assert.IsTrue(Find<LevelSession>().TryConnect(from, to, delay), $"{source} was not wired to input {port}");
        }

        /// <summary>Wires one of a block's outputs, by the port on its box, to a sink.</summary>
        internal static void WireOut(int port, string sink, int delay = 1, int block = 0)
        {
            SimulationRunner runner = Find<SimulationRunner>();
            var from = new PortAddress(Block(block).OutputAnchors[port], false, 0);
            var to = new PortAddress(runner.FixtureNodeIds[sink], true, 0);

            Assert.IsTrue(Find<LevelSession>().TryConnect(from, to, delay), $"output {port} was not wired to {sink}");
        }

        internal static readonly Vector2Int HalfAdderAt = new Vector2Int(0, 0);
        internal static readonly Vector2Int FullAdderAt = new Vector2Int(0, 2);

        internal static void BuildTheHalfAdder()
        {
            PlaceFromItsRow("HA", HalfAdderAt);
            WireIn("a", 0);
            WireIn("b", 1);
            WireOut(0, "sum");
            WireOut(1, "carry");
        }

        internal static void BuildTheFullAdder()
        {
            PlaceFromItsRow("FA", FullAdderAt);
            WireIn("a", 0);
            WireIn("b", 1);
            WireIn("cin", 2);
            WireOut(0, "s");
            WireOut(1, "cout");
        }

        /// <summary>Runs the board to its end a tick at a time, and gives the panels two frames to see it.</summary>
        internal static IEnumerator RunToTheEnd()
        {
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            session.Run();

            for (int tick = 0; tick < 100 && !runner.IsIdle(); tick++)
                runner.StepOneTick();

            Assert.IsTrue(runner.IsIdle(), "the run never came to a standstill");

            yield return null;
            yield return null;
        }

        // -----------------------------------------------------------------
        // The tests
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator AHalfAdderInABox_IsPlacedFromItsRow_SolvesTheLevel_AndScoresItsGates()
        {
            yield return TheGame();
            yield return OnTheLevel(HalfAdderJson, HalfAdderKey);

            BuildTheHalfAdder();
            yield return RunToTheEnd();

            Assert.AreEqual(RunState.Passed, Find<LevelSession>().State, Find<LevelSession>().Verdict.ToString());

            GameObject detail = GameObject.Find("Win/detail");
            Assert.IsNotNull(detail, "sanity: the solved card is not up");

            string text = detail.GetComponent<TextMeshProUGUI>().text;
            StringAssert.StartsWith("2 gates  -  1 HA", text, "a block did not score the gates inside it");
        }

        [UnityTest]
        public IEnumerator ABlocksInsides_AreNotDrawn_AndItsPortsSitOnItsBox()
        {
            yield return TheGame();
            yield return OnTheLevel(HalfAdderJson, HalfAdderKey);

            PlaceFromItsRow("HA", HalfAdderAt);
            yield return null;

            SimulationRunner runner = Find<SimulationRunner>();
            BuiltBlock block = Block();

            Assert.IsNotNull(GameObject.Find($"Block {block}"), "the block's box is not drawn");

            foreach (Transform node in GameObject.Find("Nodes").transform)
            {
                if (!node.name.StartsWith("Node "))
                    continue;

                int id = int.Parse(node.name.Substring(5, node.name.IndexOf(' ', 5) - 5));
                Assert.AreEqual(-1, runner.Circuit.BlockOf(id), $"{node.name}, part of the block, was drawn as a node");
            }

            for (int i = 0; i < block.InputAnchors.Count; i++)
            {
                GameObject socket = GameObject.Find($"In {block.InputAnchors[i]}.0");
                Assert.IsNotNull(socket, $"input {i} has no socket");
                Assert.AreEqual(runner.BlockPortPosition(block, true, i), (Vector2)socket.transform.position,
                    $"input {i}'s socket is not on the box");

                Assert.IsNull(GameObject.Find($"Out {block.InputAnchors[i]}.0"),
                    $"input {i}'s anchor showed the side of it inside the box");
            }

            for (int j = 0; j < block.OutputAnchors.Count; j++)
            {
                GameObject socket = GameObject.Find($"Out {block.OutputAnchors[j]}.0");
                Assert.IsNotNull(socket, $"output {j} has no port");
                Assert.AreEqual(runner.BlockPortPosition(block, false, j), (Vector2)socket.transform.position);
            }
        }

        [UnityTest]
        public IEnumerator ABlock_GoesFromItsLowerCell_AndOneUndoBringsItBackWithItsWires()
        {
            yield return TheGame();
            yield return OnTheLevel(FullAdderJson, FullAdderKey);

            BuildTheFullAdder();
            LevelSession session = Find<LevelSession>();
            Assert.AreEqual(5, session.Blueprint.Wires.Count, "sanity: the full adder is not wired");

            Assert.IsTrue(session.TryRemoveAt(FullAdderAt + Vector2Int.down), "the block did not go from its lower cell");
            Assert.AreEqual(0, session.Blueprint.Blocks.Count);
            Assert.AreEqual(0, session.Blueprint.Wires.Count, "the block's wires stayed behind");

            Assert.IsTrue(session.Undo());
            Assert.AreEqual(1, session.Blueprint.Blocks.Count, "undo did not bring the block back");
            Assert.AreEqual(5, session.Blueprint.Wires.Count, "undo did not bring its wires back");
            Assert.AreEqual(1, Find<SimulationRunner>().Circuit.Blocks.Count, "the board was not rebuilt with it");

            yield return RunToTheEnd();
            Assert.AreEqual(RunState.Passed, session.State, session.Verdict.ToString());
        }

        [UnityTest]
        public IEnumerator AWireIntoABlock_IsFoundOnTheWireDrawn_AndRetimedAsDrawn()
        {
            yield return TheGame();
            yield return OnTheLevel(HalfAdderJson, HalfAdderKey);

            PlaceFromItsRow("HA", HalfAdderAt);
            WireIn("a", 0);
            yield return null;

            SimulationRunner runner = Find<SimulationRunner>();
            Edge edge = runner.View.GetEdge(0);
            Assert.IsTrue(runner.IsDrawn(edge), "sanity: the wire into the block draws nothing");

            runner.DrawnEndsOf(edge, out Vector2 from, out Vector2 to);
            Assert.AreEqual(runner.BlockPortPosition(Block(), true, 0), to, "the wire does not end on the box");

            Assert.IsTrue(Find<LevelSession>().TryChangeWireDelay((from + to) * 0.5f, 1), "the wire under the cursor was not found");
            yield return null;

            Assert.AreEqual(2, Find<LevelSession>().Blueprint.Wires[0].Delay);
            Assert.AreEqual(2, runner.ShapeOf(runner.View.GetEdge(0)).DrawnDelay);

            GameObject label = GameObject.Find("Edge 0 delay/number");
            Assert.IsNotNull(label, "sanity: no delay label on the wire");
            Assert.AreEqual("2", label.GetComponent<TextMeshPro>().text, "the label is not the delay drawn");
        }
    }
}
