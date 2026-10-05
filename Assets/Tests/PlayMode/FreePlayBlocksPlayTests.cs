using System.Collections;
using BitSorter.LogicCore;
using BitSorter.View;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Free play's blocks: MAKE BLOCK turns the open board into one, the library puts it in the
    /// parts list, and DELETE asks before it takes it out again.
    /// </summary>
    [TestFixture]
    public class FreePlayBlocksPlayTests : InputTestFixture
    {
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        public override void Setup()
        {
            base.Setup();

            // As PanelPlayTests explains: the fixture's read-value cache is something the game never
            // runs with, and its self-check fails tests whose every assertion passed.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        public override void TearDown()
        {
            SaveGuard.Clear();
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        private static ProgressStore Store => Find<ProgressTracker>().Store;
        private static LevelSession Session => Find<LevelSession>();
        private static SimulationRunner Runner => Find<SimulationRunner>();

        private static IEnumerator OpenFreePlay()
        {
            yield return TestScene.Load();

            Find<MainMenu>().Show(false);
            yield return null;

            Find<SandboxPanel>().Open();
            yield return null;
            yield return null;

            Assert.AreEqual(SandboxLevel.Key, Session.LevelName, "sanity: free play did not load");
        }

        private static IEnumerator Press(string name)
        {
            GameObject target = GameObject.Find(name);
            Assert.IsNotNull(target, $"there is no '{name}' on screen");

            Button button = target.GetComponent<Button>();
            Assert.IsTrue(button.interactable, $"'{name}' cannot be pressed");

            button.onClick.Invoke();
            yield return null;
            yield return null;
        }

        private static int NodeAt(Vector2Int cell)
        {
            for (int id = 0; id < Runner.View.NodeCount; id++)
            {
                if (Runner.View.GetNode(id) != null && Runner.TryCellOf(id, out Vector2Int at) && at == cell)
                    return id;
            }

            Assert.Fail($"nothing on {cell}");
            return -1;
        }

        private static void Wire(int fromNode, int toNode)
        {
            Assert.IsTrue(Session.TryConnect(new PortAddress(fromNode, false, 0), new PortAddress(toNode, true, 0)),
                $"could not wire {fromNode} to {toNode}");
        }

        /// <summary>Free play's two sources each through a NOT to the sink on their row.</summary>
        private static void BuildTwoInverters()
        {
            for (int row = 0; row < 2; row++)
            {
                var cell = new Vector2Int(0, SandboxLevel.Board.y - row);
                Assert.IsTrue(Session.TryPlaceGate(GateKind.Not, cell), $"could not place a NOT on {cell}");

                Wire(Runner.FixtureNodeIds[SandboxLevel.SourceId(row)], NodeAt(cell));
                Wire(NodeAt(cell), Runner.FixtureNodeIds[SandboxLevel.SinkId(row)]);
            }
        }

        /// <summary>MAKE BLOCK, the name typed, and OK.</summary>
        private static IEnumerator MakeBlock(string name)
        {
            yield return Press("Block make");

            NameBoardPanel namer = Find<NameBoardPanel>();
            Assert.IsTrue(namer.IsShowing, "MAKE BLOCK did not ask for a name");

            namer.SetTyped(name);
            yield return Press("Name ok");
            Assert.IsFalse(namer.IsShowing, $"the name {name} was refused: {namer.Refusal}");
        }

        [UnityTest]
        public IEnumerator ABoardMadeIntoABlock_JoinsThePartsList_AndGoesOnTheBoard()
        {
            yield return OpenFreePlay();
            BuildTwoInverters();

            yield return Press("Block make");

            NameBoardPanel namer = Find<NameBoardPanel>();
            Assert.AreEqual("B1", namer.Typed, "the name did not start on the library's next free one");
            Assert.AreEqual(SandboxPanel.NameThisBlock, GameObject.Find("Name board/title").GetComponent<TextMeshProUGUI>().text);

            namer.SetTyped("NOT2");
            yield return Press("Name ok");

            Assert.IsFalse(namer.IsShowing, $"the name was refused: {namer.Refusal}");
            Assert.AreEqual(1, Store.Library.Count, "the block did not reach the library");
            Assert.AreEqual("NOT2", GameObject.Find("Block name").GetComponent<TextMeshProUGUI>().text);
            Assert.IsNotNull(GameObject.Find("Part block NOT2"), "the block has no row in the parts list");

            Session.ClearBoard();
            yield return null;

            BlockPlayTests.PlaceFromItsRow("NOT2", new Vector2Int(0, 0));
            Assert.AreEqual(1, Runner.Circuit.Blocks.Count, "the block was not built onto the board");
            Assert.AreEqual(2, Runner.Circuit.Blocks[0].Definition.Inputs.Count);
        }

        [UnityTest]
        public IEnumerator ABoardThatCannotBeABlock_SaysWhy_AndAsksForNoName()
        {
            yield return OpenFreePlay();

            // Source A wired through, source B left alone: an input that leads nowhere.
            var cell = new Vector2Int(0, SandboxLevel.Board.y);
            Assert.IsTrue(Session.TryPlaceGate(GateKind.Not, cell));
            Wire(Runner.FixtureNodeIds[SandboxLevel.SourceId(0)], NodeAt(cell));
            Wire(NodeAt(cell), Runner.FixtureNodeIds[SandboxLevel.SinkId(0)]);

            yield return Press("Block make");

            Assert.IsFalse(Find<NameBoardPanel>().IsShowing, "a board that cannot be a block was asked for a name");
            StringAssert.Contains("Input B", Runner.LastRejectionReason, "the refusal did not say which input");
            Assert.AreEqual(0, Store.Library.Count);
        }

        [UnityTest]
        public IEnumerator DeletingABlock_AsksFirst_AndLeavesTheBoardItStandsOn()
        {
            yield return OpenFreePlay();
            BuildTwoInverters();
            yield return MakeBlock("NOT2");

            Session.ClearBoard();
            yield return null;
            BlockPlayTests.PlaceFromItsRow("NOT2", new Vector2Int(0, 0));

            yield return Press("Block delete");

            Assert.IsNull(GameObject.Find("Block delete"), "DELETE is still where a double-click's second click lands");
            Assert.AreEqual(1, Store.Library.Count, "DELETE took the block out without asking");

            yield return Press("Block delete yes");

            Assert.AreEqual(0, Store.Library.Count, "the block is still in the library");
            Assert.IsNull(GameObject.Find("Part block NOT2"), "the deleted block kept its row");
            Assert.AreEqual(SandboxPanel.NoBlocksYet, GameObject.Find("Block name").GetComponent<TextMeshProUGUI>().text);
            Assert.AreEqual(1, Session.Blueprint.Blocks.Count, "deleting it from the library took it off the board");
        }
    }
}
