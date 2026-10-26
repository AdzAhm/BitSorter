using System.Collections;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The shell's bridges (Part 11a), each proved in the real game before anything else relies on it: a UI Toolkit
    /// panel beside the game's uGUI canvas, asked about the pointer, the keyboard, Escape, its scale and its order as
    /// uGUI is. The workbench's toolbar is the first thing on it, so the toolbar is what each is proved with.
    /// </summary>
    [TestFixture]
    public class ShellPlayTests : InputTestFixture
    {
        private Keyboard _keyboard;
        private Mouse _mouse;

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
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
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

        /// <summary>The workbench open, its fade done, its run paused at its start.</summary>
        private static IEnumerator OpenTheWorkbench()
        {
            GameObject menuButton = GameObject.Find(MainMenu.WorkbenchButton);
            Assert.IsNotNull(menuButton, "sanity: the game boots into the main menu");
            menuButton.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            for (int frame = 0; frame < 240 && UiFade.AnyMoving; frame++)
                yield return null;
            yield return null;
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.IsOpen, "sanity: the workbench opens");
            workbench.Pause();
            workbench.Seek(0.0);
            yield return null;
        }

        private IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        private IEnumerator ClickAt(Vector2 point)
        {
            Move(_mouse.position, point);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ATapOnAShellButton_IsTheButtons_AndTheGameKnowsThePointerIsOverAControl()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Button play = ShellPanel.Find<Button>(WorkbenchPanel.PlayButton);
            Assert.IsNotNull(play, "PLAY is the shell's");
            Vector2 at = ShellPanel.ScreenPointOf(play);
            Move(_mouse.position, at);
            yield return null;
            Assert.IsTrue(ShellPanel.OverControlAt(at), "the shell says a control is under the pointer");
            Assert.IsTrue(Find<PointerGate>().PointerOverUi, "and the board's pointer gate counts the shell as interface");

            // A tap: press and release in one frame, the case the frame-start sample exists for.
            Press(_mouse.leftButton);
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
            Assert.IsTrue(workbench.Playing, "the tap pressed PLAY");
            Assert.IsEmpty(workbench.Editor.Selection.Parts, "and nothing on the circuit took it");
        }

        /// <summary>Whether <paramref name="field"/> -- the whole of it, whichever of its parts -- holds the keyboard.</summary>
        private static bool Holds(TextField field) =>
            field.panel?.focusController?.focusedElement is VisualElement focused && (focused == field || field.Contains(focused));

        /// <summary>The + SIGNAL list's search field.</summary>
        private static TextField Search() => ShellPanel.Find<VisualElement>(WaveSignalPicker.RootName).Q<TextField>("search");

        /// <summary>
        /// A key that asks for a field gives it the keyboard without being typed into it. The platform hands a key to UI
        /// Toolkit as well as to the game, and in the browser that half can come after the game has read the key: A typed
        /// an "a" into the + SIGNAL search it had just opened (the Part 11d smoke test).
        /// </summary>
        [UnityTest]
        public IEnumerator A_OpensTheSearch_WithoutTypingItsOwnA()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            // A, its UI Toolkit half arriving in the frame the game read it, after the game.
            Press(_keyboard.aKey);
            yield return null;
            Assert.IsTrue(workbench.Picker.IsOpen, "sanity: A opens the + SIGNAL list");
            TextField search = Search();
            using (KeyDownEvent down = KeyDownEvent.GetPooled('\0', KeyCode.A, EventModifiers.None))
                search.panel.visualTree.SendEvent(down);
            using (KeyDownEvent typed = KeyDownEvent.GetPooled('a', KeyCode.None, EventModifiers.None))
                search.panel.visualTree.SendEvent(typed);
            Release(_keyboard.aKey);
            yield return null;
            yield return null;

            Assert.AreEqual("", search.value, "the A that opened the list was typed into its search");
            Assert.IsTrue(Holds(search), "and the search has the keyboard");
        }

        /// <summary>
        /// A field given the keyboard keeps it until it is let go on purpose. In the browser UI Toolkit let a field go
        /// again, to nothing, in the frames after the press that gave it the keyboard -- a click on + SIGNAL, a
        /// double-click on a part -- so the first keys typed went nowhere (the Part 11d smoke test). The editor never
        /// does, so this lets the field go as the browser did: nothing pressed, nothing typed, nothing else focused.
        /// </summary>
        [UnityTest]
        public IEnumerator AFieldLetGoToNothing_IsGivenTheKeyboardBack_ButNotOnceLetGoOnPurpose()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            yield return ClickAt(ShellPanel.ScreenPointOf(ShellPanel.Find<Button>(WaveformView.AddButton)));
            Assert.IsTrue(workbench.Picker.IsOpen, "sanity: + SIGNAL opens the list");
            TextField search = Search();
            Assert.IsTrue(Holds(search), "sanity: and gives its search the keyboard");

            search.Blur();
            yield return null;
            yield return null;
            Assert.IsTrue(Holds(search), "the search, let go to nothing, was not given the keyboard back");

            // Let go on purpose -- a click on the circuit, which also closes the list -- it stays let go.
            yield return ClickAt(workbench.ViewMiddleOnScreen);
            yield return null;
            yield return null;
            Assert.IsFalse(UiText.Typing, "a click elsewhere let it go, and it stayed let go");

            // RUN TO, clicked into and let go to nothing, is given it back as well.
            TextField runTo = ShellPanel.Find<TextField>(WorkbenchPanel.RunToField);
            yield return ClickAt(ShellPanel.ScreenPointOf(runTo));
            Assert.IsTrue(Holds(runTo), "sanity: a click on RUN TO gives it the keyboard");
            runTo.Blur();
            yield return null;
            yield return null;
            Assert.IsTrue(Holds(runTo), "RUN TO, let go to nothing, was not given the keyboard back");
        }

        [UnityTest]
        public IEnumerator AShellField_HoldsTheKeys_AndEscapeLetsItGo_BeforeTheWorkbenchCloses()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Seek(10.0);
            yield return null;

            TextField runTo = ShellPanel.Find<TextField>(WorkbenchPanel.RunToField);
            yield return ClickAt(ShellPanel.ScreenPointOf(runTo));
            Assert.IsTrue(UiText.Typing, "a click on RUN TO gives it the keyboard");
            Assert.IsNull(UiText.Keyboard, "and the game's keys see none");

            yield return Tap(_keyboard.rKey);
            Assert.AreEqual(10.0, workbench.Time, 1e-9, "R typed into the field is no RESTART");

            // Escape as the platform hands it over: to the field, and to the game's keyboard, in one frame.
            Press(_keyboard.escapeKey);
            ShellPanel.SendKey(runTo, KeyCode.Escape);
            yield return null;
            Release(_keyboard.escapeKey);
            yield return null;
            yield return null;
            Assert.IsFalse(UiText.Typing, "Escape let the field go");
            Assert.IsTrue(workbench.IsOpen, "and was the field's alone: the workbench is still open");

            yield return Tap(_keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(workbench.IsOpen, "the next Escape is the workbench's");
        }

        /// <summary>
        /// What is typed into RUN TO, and then Enter, sets it; and Escape after typing puts back what was there. In the
        /// served browser build RUN TO was never set: Enter read the field's committed text, and a field that committed
        /// only on Enter had committed nothing yet (the Part 11a smoke test). The editor cannot type into a shell field --
        /// UI Toolkit takes typing from the platform, which a test cannot drive -- so this types as a field that is not
        /// delayed is typed into, its value changing a keystroke at a time; the browser build is what proved the fix.
        /// </summary>
        [UnityTest]
        public IEnumerator WhatIsTypedIntoRunTo_ThenEnter_SetsIt()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            TextField runTo = ShellPanel.Find<TextField>(WorkbenchPanel.RunToField);
            yield return ClickAt(ShellPanel.ScreenPointOf(runTo));
            Assert.IsTrue(UiText.Typing, "sanity: RUN TO has the keyboard");

            foreach (string typed in new[] { "2", "20" })
            {
                runTo.value = typed;
                yield return null;
            }

            Assert.IsFalse(runTo.isDelayed, "a field whose value is what it shows: one that commits only on Enter has committed nothing when Enter is read");
            Press(_keyboard.enterKey);
            ShellPanel.SendKey(runTo, KeyCode.Return);
            yield return null;
            Release(_keyboard.enterKey);
            yield return null;
            yield return null;

            Assert.AreEqual(20L, workbench.Playback.RunTo, "Enter set RUN TO to what was typed");
            Assert.IsFalse(UiText.Typing, "and gave the keyboard back");

            // Typed again, and Escape: what was there comes back, and RUN TO is as it was.
            yield return ClickAt(ShellPanel.ScreenPointOf(runTo));
            runTo.value = "35";
            yield return null;
            Press(_keyboard.escapeKey);
            ShellPanel.SendKey(runTo, KeyCode.Escape);
            yield return null;
            Release(_keyboard.escapeKey);
            yield return null;
            yield return null;
            Assert.AreEqual(20L, workbench.Playback.RunTo, "Escape set nothing");
            Assert.AreEqual("20", runTo.value, "and put back what RUN TO held");
            Assert.IsTrue(workbench.IsOpen, "and was the field's alone");
        }

        [UnityTest]
        public IEnumerator AClickOffAShellField_OnTheCanvasUnderIt_LetsItGo()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            yield return ClickAt(ShellPanel.ScreenPointOf(ShellPanel.Find<TextField>(WorkbenchPanel.RunToField)));
            Assert.IsTrue(UiText.Typing, "sanity: RUN TO has the keyboard");

            yield return ClickAt(workbench.ViewMiddleOnScreen);
            Assert.IsFalse(UiText.Typing, "a click on the circuit, which is uGUI's and never reaches the shell, let the field go");
        }

        [UnityTest]
        public IEnumerator TheShell_IsScaledAsTheCanvasIs_SortedOverIt_AndUnderTheFrameCounter_AndFollowsTheWorkbenchsFade()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Canvas canvas = null;
            foreach (Canvas found in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (found.isRootCanvas && found.name == "Game UI")
                    canvas = found;
            Assert.IsNotNull(canvas, "sanity: the game's canvas");

            VisualElement root = workbench.Shell.Root;
            Assert.AreEqual(canvas.scaleFactor, root.panel.scaledPixelsPerPoint, 1e-3f, "a unit of the shell is a unit of the canvas");
            Label time = root.Q<Label>("time");
            Assert.AreEqual(UiTheme.SizeOf(UiType.Body), time.resolvedStyle.fontSize, 1e-3f, "a Body label is Body's size");

            Assert.Greater(ShellPanel.SortingOrder, canvas.sortingOrder, "drawn over the game's canvas");
            Assert.Less(ShellPanel.SortingOrder, FrameRateCounter.SortingOrder, "and under the frame-rate counter");

            Assert.AreEqual(1f, root.resolvedStyle.opacity, 1e-3f, "faded in with the workbench");
            Assert.IsTrue(workbench.Shell.Visible);
            ShellPanel.Submit(ShellPanel.Find<Button>(WorkbenchPanel.BackButton));
            yield return null;
            Assert.IsFalse(workbench.Shell.Visible, "and gone with it");
        }

        [UnityTest]
        public IEnumerator EveryCharacterTheShellDraws_IsInItsFont()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            int checkedLabels = 0;
            workbench.Shell.Root.Query<TextElement>().ForEach(element =>
            {
                bool bold = element.ClassListContains(ShellTheme.BoldClass);
                UnityEngine.TextCore.Text.FontAsset font = ShellFonts.AssetOf(bold);
                Assert.IsNotNull(font, "the shell draws in IBM Plex, made from the scene's fonts");
                foreach (char c in element.text)
                    Assert.IsTrue(char.IsWhiteSpace(c) || font.HasCharacter(c, false, true), $"'{c}' of '{element.text}' is not in {font.name}");
                checkedLabels++;
            });
            Assert.Greater(checkedLabels, 10, "sanity: the toolbar's words were looked at");
        }

        /// <summary>
        /// What is typed in a shell field sits in the middle of its box, as its placeholder does. The theme the shell has
        /// none of is what stretches a field's text to the box's height, and without it every value sat at the box's top
        /// -- seen first in the inspector's bar (Part 11b-2), where every field shows a value.
        /// </summary>
        [UnityTest]
        public IEnumerator WhatIsTypedInAShellField_SitsInTheMiddleOfItsBox()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            TextField runTo = ShellPanel.Find<TextField>(WorkbenchPanel.RunToField);
            runTo.SetValueWithoutNotify("20");
            yield return null;
            yield return null;

            VisualElement box = runTo.Q(className: TextField.inputUssClassName);
            TextElement text = box.Query<TextElement>().Where(t => t.name != ShellTheme.PlaceholderName).First();
            Assert.IsNotNull(text, "sanity: the field's text");
            Assert.AreEqual(box.worldBound.center.y, text.worldBound.center.y, 1f,
                $"the text, {text.worldBound}, is not in the middle of its box, {box.worldBound}");
        }
    }
}
