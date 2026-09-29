using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// A level that opens on a circuit: the clear button says START OVER and puts that circuit back
    /// as one undo step, and a saved board still wins over the start.
    /// </summary>
    /// <remarks>
    /// Play Mode because clearing, undoing and the button all need the runner, which Edit Mode never
    /// builds. The level is written here rather than taken from Resources, so the behaviour is pinned
    /// apart from whichever levels ship with a start.
    /// </remarks>
    [TestFixture]
    public class StartingCircuitPlayTests
    {
        private const string Key = "starts-built";

        /// <summary>Where the start's one part sits.</summary>
        private static readonly Vector2Int Middle = new Vector2Int(0, 0);

        private const string Json = @"{
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

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        private static LevelDefinition Level()
        {
            LevelLoadResult result = LevelLoader.Parse(Json, new Vector2Int(4, 2));
            Assert.IsTrue(result.IsValid, $"sanity: the test's level is invalid: {result.Error}");
            return result.Level;
        }

        /// <summary>The game past its menu, with the tutorial behind it and nothing loaded by the test yet.</summary>
        private static IEnumerator TheGame()
        {
            yield return TestScene.Load();

            // A fresh save is exactly what the tutorial offers itself on.
            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);

            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;
        }

        private static IEnumerator OnTheLevel()
        {
            Assert.IsTrue(Find<LevelSession>().Adopt(Level(), Key), "sanity: the level was not taken");
            yield return null;
            yield return null;
        }

        private static Button ClearButton()
        {
            GameObject found = GameObject.Find("Run controls/Clear");
            Assert.IsNotNull(found, "sanity: no clear button on screen");
            return found.GetComponent<Button>();
        }

        private static string ClearCaption() => ClearButton().GetComponentInChildren<TextMeshProUGUI>().text;

        [UnityTest]
        public IEnumerator OnAnUntouchedStart_TheButtonSaysStartOver_AndHasNothingToDo()
        {
            yield return TheGame();
            yield return OnTheLevel();

            LevelSession session = Find<LevelSession>();

            Assert.IsTrue(session.IsAtStart, "sanity: the level should open on its start");
            Assert.AreEqual(1, session.Blueprint.Placements.Count, "the start's part is not on the board");
            Assert.AreEqual(RunControls.StartOverCaption, ClearCaption());
            Assert.IsFalse(ClearButton().interactable, "START OVER is lit on a board already at its start");
        }

        /// <summary>
        /// START OVER puts the level's circuit back, and one undo takes the player back to what they
        /// had -- the same single step CLEAR ALL has always been.
        /// </summary>
        [UnityTest]
        public IEnumerator StartOver_PutsTheStartBack_AsOneUndoStep()
        {
            yield return TheGame();
            yield return OnTheLevel();

            LevelSession session = Find<LevelSession>();

            Assert.IsTrue(session.TryRemoveAt(Middle), "sanity: could not take the start's part off");
            yield return null;

            Assert.IsFalse(session.IsAtStart);
            Assert.IsTrue(ClearButton().interactable, "START OVER is greyed out on a board that has left its start");

            session.ClearBoard();
            yield return null;

            Assert.IsTrue(session.IsAtStart, "START OVER did not put the start back");
            Assert.IsTrue(session.Blueprint.HasPlacementAt(Middle));
            Assert.AreEqual(2, session.Blueprint.Wires.Count, "the start's wires did not come back with it");

            Assert.IsTrue(session.Undo(), "START OVER left nothing to undo");
            yield return null;

            Assert.IsFalse(session.Blueprint.HasPlacementAt(Middle), "one undo did not bring back the edited board");
        }

        /// <summary>
        /// A player's saved board for the level wins over its start: coming back finds what they left.
        /// </summary>
        [UnityTest]
        public IEnumerator ASavedBoard_WinsOverTheStart()
        {
            yield return TheGame();

            var saved = new CircuitBlueprint();
            var elsewhere = new Vector2Int(1, 1);
            saved.Place(elsewhere, GateKind.Not);
            saved.AddWire(new BlueprintWire(
                new CellPort(new Vector2Int(-3, 0), false, 0), new CellPort(elsewhere, true, 0), 1));

            Find<ProgressTracker>().Store.SaveBoard(Key, BoardSerializer.ToSaved(Key, saved));

            yield return OnTheLevel();

            LevelSession session = Find<LevelSession>();

            Assert.IsTrue(session.Blueprint.HasPlacementAt(elsewhere), "the saved board was not restored");
            Assert.IsFalse(session.Blueprint.HasPlacementAt(Middle), "the start was put over the saved board");
            Assert.IsFalse(session.IsAtStart);
        }

        [UnityTest]
        public IEnumerator OnALevelWithoutAStart_TheButtonSaysClearAll()
        {
            yield return TheGame();

            LevelSession session = Find<LevelSession>();
            Assert.IsTrue(session.LoadLevel(session.AvailableLevels[0]), "sanity: the first level did not load");
            yield return null;
            yield return null;

            Assert.IsFalse(session.HasStart, "sanity: the first level opens on an empty board");
            Assert.AreEqual(RunControls.ClearCaption, ClearCaption());
        }
    }
}
