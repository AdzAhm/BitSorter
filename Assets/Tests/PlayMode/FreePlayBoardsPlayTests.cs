using System.Collections;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Free play's named boards, through the real scene: switching, copying, naming and deleting.
    /// </summary>
    /// <remarks>
    /// An <see cref="InputTestFixture"/>, because the name is typed: the keyboard here is the
    /// fixture's own, so a key pressed while the field has focus can be shown to reach nothing else.
    /// </remarks>
    [TestFixture]
    public class FreePlayBoardsPlayTests : InputTestFixture
    {
        private Keyboard _keyboard;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        public override void Setup()
        {
            base.Setup();

            // As PanelPlayTests explains: the fixture's read-value cache is something the game never
            // runs with, and its self-check fails tests whose every assertion passed.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);

            _keyboard = InputSystem.AddDevice<Keyboard>();
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

        /// <summary>Clicks a button in the interface by its object's name.</summary>
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

        private static string BoardName() =>
            GameObject.Find("Board name").GetComponent<TextMeshProUGUI>().text;

        private static bool HasGate(Vector2Int cell, GateKind kind)
        {
            foreach (GatePlacement placement in Session.Blueprint.Placements)
            {
                if (placement.Cell == cell && placement.Kind == kind)
                    return true;
            }

            return false;
        }

        private IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        // -----------------------------------------------------------------
        // Switching
        // -----------------------------------------------------------------

        /// <summary>
        /// A new board is empty, and going back to the first finds its circuit as it was left --
        /// and the new board's circuit is still there when it is gone back to.
        /// </summary>
        [UnityTest]
        public IEnumerator ANewBoard_IsEmpty_AndSwitchingBackRestoresEachBoard()
        {
            yield return OpenFreePlay();

            var first = new Vector2Int(0, 0);
            var second = new Vector2Int(1, 1);

            Assert.IsTrue(Session.TryPlaceGate(GateKind.Xor, first), "could not place on the first board");
            Assert.AreEqual(BoardNames.First, BoardName());

            yield return Press("Board new");

            Assert.AreEqual("Board 2", BoardName());
            Assert.AreEqual(0, Session.Blueprint.Placements.Count, "a new board came with a circuit");
            Assert.IsFalse(Session.CanUndo, "a new board kept the last board's undo history");

            Assert.IsTrue(Session.TryPlaceGate(GateKind.And, second), "could not place on the new board");

            yield return Press("Board previous");

            Assert.AreEqual(BoardNames.First, BoardName());
            Assert.IsTrue(HasGate(first, GateKind.Xor), "the first board's XOR did not come back");
            Assert.IsFalse(HasGate(second, GateKind.And), "the new board's AND followed the player back");

            yield return Press("Board next");

            Assert.AreEqual("Board 2", BoardName());
            Assert.IsTrue(HasGate(second, GateKind.And), "the new board's AND was lost");
            Assert.IsFalse(HasGate(first, GateKind.Xor));
        }

        /// <summary>A copy is the board on screen -- its circuit and its setup -- under a name that says so.</summary>
        [UnityTest]
        public IEnumerator ACopy_IsTheBoardOnScreen()
        {
            yield return OpenFreePlay();

            Assert.IsTrue(Session.TryPlaceGate(GateKind.Or, new Vector2Int(0, 1)));
            int sources = Store.BoardFor(SandboxLevel.Key).sandbox.sources.Length;

            yield return Press("Board copy");

            Assert.AreEqual(BoardNames.First + " copy", BoardName());
            Assert.AreEqual(2, Store.FreePlayCount);
            Assert.IsTrue(HasGate(new Vector2Int(0, 1), GateKind.Or), "the copy came without the circuit");
            Assert.AreEqual(sources, Store.BoardFor(SandboxLevel.Key).sandbox.sources.Length,
                "the copy came with a different setup");
        }

        // -----------------------------------------------------------------
        // Naming
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator RenamingThroughThePanel_ShowsTheNewName()
        {
            yield return OpenFreePlay();
            yield return Press("Board rename");

            NameBoardPanel namer = Find<NameBoardPanel>();
            Assert.IsTrue(namer.IsShowing, "RENAME did not ask for a name");
            Assert.AreEqual(BoardNames.First, namer.Typed, "the field did not start on the board's name");
            Assert.IsTrue(UiText.Typing, "the field did not take the keyboard");

            namer.SetTyped("Adder");
            yield return Press("Name ok");

            Assert.IsFalse(namer.IsShowing);
            Assert.AreEqual("Adder", BoardName());
            Assert.AreEqual("Adder", Store.FreePlayName(Store.ActiveFreePlay));
            Assert.IsFalse(UiText.Typing, "the field kept the keyboard after closing");
        }

        /// <summary>A name another board has is refused in the panel, which stays open to fix it.</summary>
        [UnityTest]
        public IEnumerator ANameAlreadyTaken_IsRefused_AndThePanelStaysOpen()
        {
            yield return OpenFreePlay();
            yield return Press("Board new");
            yield return Press("Board rename");

            NameBoardPanel namer = Find<NameBoardPanel>();
            namer.SetTyped(BoardNames.First.ToUpperInvariant());
            yield return Press("Name ok");

            Assert.IsTrue(namer.IsShowing, "a taken name closed the panel");
            Assert.IsNotEmpty(namer.Refusal, "a taken name was refused without saying why");
            Assert.AreEqual("Board 2", Store.FreePlayName(Store.ActiveFreePlay), "the refused name was kept");

            yield return Press("Name cancel");
            Assert.IsFalse(namer.IsShowing);
        }

        /// <summary>
        /// N typed into the name mutes nothing -- and N with the field closed does mute, so the
        /// first half is not passing because N does nothing at all.
        /// </summary>
        [UnityTest]
        public IEnumerator TypingN_MutesNothing_WhileNOutsideTheFieldDoes()
        {
            yield return OpenFreePlay();

            bool before = GameAudio.Muted;

            yield return Press("Board rename");
            Assert.IsTrue(UiText.Typing, "sanity: the field should have the keyboard");

            yield return Tap(_keyboard.nKey);
            Assert.AreEqual(before, GameAudio.Muted, "typing N into a name toggled the sound");

            yield return Press("Name cancel");
            Assert.IsFalse(UiText.Typing);

            yield return Tap(_keyboard.nKey);
            Assert.AreNotEqual(before, GameAudio.Muted, "sanity: N outside the field should toggle the sound");

            yield return Tap(_keyboard.nKey);
            Assert.AreEqual(before, GameAudio.Muted, "sanity: the sound was not put back");
        }

        /// <summary>Escape in the field leaves the name as it was and closes -- without opening the main menu.</summary>
        [UnityTest]
        public IEnumerator EscapeInTheField_CancelsWithoutOpeningTheMainMenu()
        {
            yield return OpenFreePlay();
            yield return Press("Board rename");

            NameBoardPanel namer = Find<NameBoardPanel>();
            namer.SetTyped("Not kept");

            yield return Tap(_keyboard.escapeKey);
            yield return null;

            Assert.IsFalse(namer.IsShowing, "Escape did not close the naming panel");
            Assert.AreEqual(BoardNames.First, Store.FreePlayName(Store.ActiveFreePlay), "Escape kept the typed name");
            Assert.IsFalse(Find<MainMenu>().IsShowing, "the Escape that cancelled the name opened the main menu too");
        }

        // -----------------------------------------------------------------
        // Deleting
        // -----------------------------------------------------------------

        /// <summary>
        /// DELETE asks first, and where DELETE was there is nothing to click: the second click of a
        /// double-click answers nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator Delete_AsksFirst_AndADoubleClickCannotAnswer()
        {
            yield return OpenFreePlay();
            yield return Press("Board new");
            Assert.AreEqual(2, Store.FreePlayCount);

            var corners = new Vector3[4];
            GameObject.Find("Board delete").GetComponent<RectTransform>().GetWorldCorners(corners);
            Vector2 where = (corners[0] + corners[2]) * 0.5f;

            yield return Press("Board delete");

            Assert.AreEqual(2, Store.FreePlayCount, "DELETE deleted without asking");
            Assert.IsNotNull(GameObject.Find("Board question"), "DELETE did not ask");

            foreach (Button button in GameObject.Find("Sandbox setup").GetComponentsInChildren<Button>())
            {
                Assert.IsFalse(
                    RectTransformUtility.RectangleContainsScreenPoint(button.GetComponent<RectTransform>(), where),
                    $"'{button.name}' is where DELETE was, so a double-click would press it");
            }

            yield return Press("Board delete cancel");
            Assert.AreEqual(2, Store.FreePlayCount, "CANCEL deleted the board");

            yield return Press("Board delete");
            yield return Press("Board delete yes");

            Assert.AreEqual(1, Store.FreePlayCount);
            Assert.AreEqual(BoardNames.First, BoardName(), "the board that took its place did not open");
        }

        [UnityTest]
        public IEnumerator DeletingTheLastBoard_EmptiesIt_AndKeepsItsName()
        {
            yield return OpenFreePlay();

            Assert.IsTrue(Session.TryPlaceGate(GateKind.Nand, new Vector2Int(0, 0)));

            yield return Press("Board delete");
            yield return Press("Board delete yes");

            Assert.AreEqual(1, Store.FreePlayCount);
            Assert.AreEqual(BoardNames.First, BoardName());
            Assert.AreEqual(0, Session.Blueprint.Placements.Count, "the last board kept its circuit");
            Assert.AreEqual(SandboxLevel.Key, Session.LevelName, "deleting left free play");
        }
    }
}
