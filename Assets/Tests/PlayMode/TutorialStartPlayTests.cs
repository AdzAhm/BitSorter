using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The tutorial's opening moment: that a step is never already satisfied before the player has
    /// done anything.
    /// </summary>
    /// <remarks>
    /// Every bug this fixture covers had the same shape and none were visible from the script. A
    /// step is a predicate over board state, so anything that puts state on the board before the
    /// player arrives completes a step for free -- and the tutorial then skips the lesson it exists
    /// to teach, silently, looking like it worked.
    ///
    /// It happened twice from two unrelated directions. `PlacementController` puts the selection on
    /// a level's first budget row on every load, which completed "pick up the NOT gate" before the
    /// player touched anything; the fix was to stock a decoy first. And `ProgressTracker` restores a
    /// saved board, which would have satisfied all six steps the instant the level loaded; the fix
    /// was to refuse to save or restore a board for the tutorial at all.
    ///
    /// Edit Mode can check `TutorialScript.CurrentStep` against a hand-built `BoardFacts` and does.
    /// What it cannot do is check what the real board actually contains a frame after the real scene
    /// loaded it, which is where both bugs lived.
    /// </remarks>
    [TestFixture]
    public class TutorialStartPlayTests
    {
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            // Redirect first: SetReporting writes to the real machine otherwise. Mute and
            // reporting both go through Preferences, which SaveGuard redirects alongside the
            // save file, so nothing here has to be read and put back afterwards.
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        [TearDown]
        public void ClearPlantedSave() => SaveGuard.Clear();

        /// <summary>
        /// Takes the game back off the screen before the next fixture runs.
        /// </summary>
        /// <remarks>
        /// Not optional. Leaving the scene loaded turned two of `PointerArbitrationPlayTests`'
        /// assertions red -- it builds its own canvas and assumes nothing else is on screen, and
        /// the main menu is a full-screen panel, so the pointer was over UI everywhere.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static IEnumerator LoadScene() => TestScene.Load();

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>
        /// Closes the menu the game boots into, so the director's own Update runs.
        /// </summary>
        /// <remarks>
        /// It stands down while anything full-screen is open rather than talking over it, so a
        /// START or SKIP press is recorded and never consumed until the menu is gone.
        /// </remarks>
        private static IEnumerator CloseTheMainMenu()
        {
            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;
        }

        /// <summary>Starts the tutorial and lets the director settle on its first step.</summary>
        private static IEnumerator BeginTutorial(TutorialDirector director)
        {
            director.Begin();

            // Adopt rebuilds the graph, and the director only reads the board once the runner says
            // it is ready again.
            yield return null;
            yield return null;
        }

        // -----------------------------------------------------------------
        // The board is the tutorial's until the player says otherwise
        // -----------------------------------------------------------------

        /// <summary>
        /// The board does not take edits while the tutorial is still asking whether to start.
        /// </summary>
        /// <remarks>
        /// Reported from play: a player spent the parts before being asked for them, and the
        /// instructions then asked for a gate that was no longer there. The tutorial stocks one
        /// NOT and one AND decoy, and the step that wants the NOT wants it on one particular
        /// cell -- so a NOT put down anywhere else, or the decoy dropped on that cell, leaves a
        /// step that cannot be satisfied and nothing in hand to satisfy it with. Recovering means
        /// right-clicking the part back, which the step text mentions only once the player has
        /// already got there.
        ///
        /// So the intro holds the board: START or SKIP, and after either the board is the
        /// player's again. Refused rather than ignored, because a click that does nothing and
        /// says nothing teaches that the board is broken.
        /// </remarks>
        [UnityTest]
        public IEnumerator WhileTheIntroIsUp_TheBoardRefusesEdits()
        {
            yield return LoadScene();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();

            yield return BeginTutorial(director);

            Assert.IsFalse(session.TryPlaceGate(TutorialLevel.Decoy, TutorialLevel.GateCell),
                "the decoy went onto the gate's own cell before the tutorial had started, which " +
                "spends the only one and blocks the cell the NOT has to go on");

            Assert.IsFalse(session.TryPlaceGate(TutorialLevel.Part, new Vector2Int(1, 1)),
                "the NOT was spent on the wrong cell before the tutorial had started, and there " +
                "is only one of it");

            Assert.IsTrue(session.Blueprint.IsEmpty, "the board took an edit it should have refused");
        }

        /// <summary>
        /// Pressing START hands the board back, so the tutorial can be followed.
        /// </summary>
        /// <remarks>
        /// The pair to the test above, and the more important half: a hold that is never released
        /// is a game that cannot be played. Both ways out are exercised -- START here, SKIP below
        /// -- because the whole risk of holding the board is holding it forever.
        /// </remarks>
        [UnityTest]
        public IEnumerator OnceStarted_TheBoardIsThePlayersAgain()
        {
            yield return LoadScene();
            yield return CloseTheMainMenu();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();

            yield return BeginTutorial(director);
            yield return PressStart();

            Assert.IsTrue(session.TryPlaceGate(TutorialLevel.Part, TutorialLevel.GateCell),
                "the tutorial kept the board after the player pressed START");
        }

        /// <summary>Skipping hands the board back too, which is what skipping means.</summary>
        [UnityTest]
        public IEnumerator OnceSkipped_TheBoardIsThePlayersAgain()
        {
            yield return LoadScene();
            yield return CloseTheMainMenu();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();

            yield return BeginTutorial(director);

            Find<TutorialPanel>().Skip();
            yield return null;
            yield return null;

            Assert.IsTrue(session.TryPlaceGate(TutorialLevel.Part, TutorialLevel.GateCell),
                "the tutorial kept the board after the player skipped it");
        }

        /// <summary>Presses the intro's START and lets the director move on to the steps.</summary>
        private static IEnumerator PressStart()
        {
            Find<TutorialPanel>().Next();
            yield return null;
            yield return null;
        }

        // -----------------------------------------------------------------
        // Leaving the tutorial
        // -----------------------------------------------------------------

        /// <summary>
        /// Finishing the tutorial leads from the solved card to its ending card, and from there
        /// into the first level -- counted as finished, and never started again.
        /// </summary>
        /// <remarks>
        /// Reported from play. The solved card offered PLAY THE FIRST LEVEL, which loaded the first
        /// level -- and the director, seeing its board left, stopped without recording the
        /// milestone. On the next frame the save still had no tutorial milestone and the board was
        /// the first level's, empty, which is exactly a first-time player, so the tutorial started
        /// again. The only way into the first level was SKIP.
        ///
        /// The solved card now says CONTINUE and leads to the ending card, which the old button
        /// skipped; the ending card's own button is the way into the first level.
        /// </remarks>
        [UnityTest]
        public IEnumerator FinishingTheTutorial_LeadsThroughItsEndingCard_IntoTheFirstLevel()
        {
            yield return LoadScene();
            yield return CloseTheMainMenu();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();

            yield return BeginTutorial(director);
            yield return PressStart();
            yield return SolveTheTutorial(session, Find<SimulationRunner>());

            Assert.IsFalse(IsShowingButton("KEEP TINKERING"),
                "the tutorial's solved card offers KEEP TINKERING, which leads to the same card as CONTINUE");
            PressShowing("CONTINUE");

            TutorialCard card = Find<TutorialCard>();
            for (int frame = 0; frame < 60 && !card.IsShowing; frame++)
                yield return null;

            Assert.IsTrue(card.IsShowing, "the solved card's CONTINUE did not lead to the tutorial's ending card");

            PressShowing("PLAY THE FIRST LEVEL");

            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(session.AvailableLevels[0], session.LevelName,
                "the solved card's button did not leave the player in the first level");
            Assert.IsFalse(director.IsRunning, "the tutorial started again straight after it was finished");
            Assert.IsTrue(Find<ProgressTracker>().Store.HasMilestone(TutorialLevel.Key),
                "finishing the tutorial from the solved card did not count as finishing it");
        }

        /// <summary>
        /// A player who skipped the tutorial and solved its board anyway is offered the first level.
        /// </summary>
        /// <remarks>
        /// Skipping stops the tutorial, so there is no ending card to lead to: the solved card's
        /// way on has to be the first level itself, or the only way forward is a keyboard shortcut.
        /// </remarks>
        [UnityTest]
        public IEnumerator SkippedThenSolved_TheSolvedCardLeadsIntoTheFirstLevel()
        {
            yield return LoadScene();
            yield return CloseTheMainMenu();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();

            yield return BeginTutorial(director);
            Find<TutorialPanel>().Skip();
            yield return null;
            yield return null;

            Assert.IsFalse(director.IsRunning, "sanity: SKIP should have stopped the tutorial");

            yield return SolveTheTutorial(session, Find<SimulationRunner>());

            PressShowing("PLAY THE FIRST LEVEL");

            yield return null;
            yield return null;

            Assert.AreEqual(session.AvailableLevels[0], session.LevelName,
                "a skipped-then-solved tutorial's solved card did not lead into the first level");
        }

        /// <summary>
        /// Walking away from the tutorial to the first level does not start it again.
        /// </summary>
        /// <remarks>
        /// The same loop from the other side: open the level list mid-tutorial, choose the first
        /// level, and the empty first-level board on a save with no milestone looked like somebody
        /// arriving for the first time. The tutorial is offered by itself once, not every time the
        /// player comes back to where it is offered.
        /// </remarks>
        [UnityTest]
        public IEnumerator LeavingTheTutorialForTheFirstLevel_DoesNotStartItAgain()
        {
            yield return LoadScene();
            yield return CloseTheMainMenu();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();

            yield return BeginTutorial(director);
            yield return PressStart();

            string first = session.AvailableLevels[0];
            Assert.IsTrue(session.LoadLevel(first), "sanity: the first level did not load");

            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(first, session.LevelName, "the player was taken away from the level they chose");
            Assert.IsFalse(director.IsRunning, "walking away from the tutorial to the first level started it again");
        }

        /// <summary>Builds the tutorial's circuit and runs it until the solved card is up.</summary>
        private static IEnumerator SolveTheTutorial(LevelSession session, SimulationRunner runner)
        {
            Assert.IsTrue(session.TryPlaceGate(TutorialLevel.Part, TutorialLevel.GateCell), "could not place the NOT");

            int gate = -1;
            for (int id = 0; id < runner.View.NodeCount; id++)
            {
                if (runner.TryCellOf(id, out Vector2Int cell) && cell == TutorialLevel.GateCell)
                    gate = id;
            }

            Assert.AreNotEqual(-1, gate, "sanity: the NOT is not on its cell");

            Assert.IsTrue(session.TryConnect(
                    new PortAddress(runner.FixtureNodeIds[TutorialLevel.SourceId], false, 0),
                    new PortAddress(gate, true, 0)), "could not wire the source to the NOT");
            Assert.IsTrue(session.TryConnect(
                    new PortAddress(gate, false, 0),
                    new PortAddress(runner.FixtureNodeIds[TutorialLevel.SinkId], true, 0)),
                "could not wire the NOT to the bin");

            yield return null;
            session.Run();

            // Driven, not waited for.
            for (int tick = 0; tick < 60 && session.State != RunState.Passed; tick++)
            {
                runner.StepOneTick();
                yield return null;
            }

            Assert.AreEqual(RunState.Passed, session.State, "sanity: the tutorial's circuit did not pass");

            WinPanel win = Find<WinPanel>();
            for (int frame = 0; frame < 60 && !win.IsShowing; frame++)
                yield return null;

            Assert.IsTrue(win.IsShowing, "sanity: the solved card never came up");
        }

        private static bool IsShowingButton(string caption)
        {
            foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                TMPro.TextMeshProUGUI label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>();

                if (button.isActiveAndEnabled && label != null && label.text == caption)
                    return true;
            }

            return false;
        }

        /// <summary>Presses the button on screen whose caption reads <paramref name="caption"/>.</summary>
        private static void PressShowing(string caption)
        {
            foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                TMPro.TextMeshProUGUI label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>();

                if (button.isActiveAndEnabled && label != null && label.text == caption)
                {
                    button.onClick.Invoke();
                    return;
                }
            }

            Assert.Fail($"sanity: there is no '{caption}' button on screen");
        }

        // -----------------------------------------------------------------
        // What the tutorial points at stays readable
        // -----------------------------------------------------------------

        /// <summary>
        /// A ring round a parts-list row or a button goes round it, and leaves the thing itself
        /// showing.
        /// </summary>
        /// <remarks>
        /// Found in the browser build. The ring on the step that asks for RUN was the AND gate's
        /// filled squircle, stretched over the button at up to 95% opacity and drawn after it, so on
        /// top of it: a pale slab with the caption lost underneath, on the one button the step was
        /// asking the player to press. Board rings were always hollow; only the interface's were
        /// not.
        ///
        /// The first step after START rings the NOT in the parts list, so it is the first moment a
        /// ring is on the interface.
        /// </remarks>
        [UnityTest]
        public IEnumerator ARingOnTheInterface_LeavesWhatItPointsAtShowing()
        {
            yield return LoadScene();
            yield return CloseTheMainMenu();
            yield return BeginTutorial(Find<TutorialDirector>());
            yield return PressStart();

            int rings = 0;

            foreach (Image ring in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
            {
                if (ring.name != "Tutorial ring" || !ring.gameObject.activeInHierarchy)
                    continue;

                rings++;

                Rect area = ring.sprite.textureRect;
                Color middle = ring.sprite.texture.GetPixel(
                    (int)(area.x + area.width * 0.5f), (int)(area.y + area.height * 0.5f));

                Assert.Less(middle.a, 0.05f,
                    "a tutorial ring is filled in, and covers the part or button it points at");
            }

            Assert.Greater(rings, 0, "sanity: the first step should ring the NOT in the parts list");
        }

        // -----------------------------------------------------------------
        // Nothing is free
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator OnAFreshSave_TheFirstStepIsNotAlreadyComplete()
        {
            yield return LoadScene();

            TutorialDirector director = Find<TutorialDirector>();
            Assert.IsNotNull(director, "no tutorial director in the scene");

            yield return BeginTutorial(director);

            Assert.AreEqual(0, director.CurrentStep,
                "the tutorial opened with step one already satisfied -- the player is being asked " +
                "to do something the board has done for them");
        }

        /// <summary>
        /// The selection must not start on the part the first step asks for.
        /// </summary>
        /// <remarks>
        /// The cause, asserted separately from the symptom above. `PlacementController` selects a
        /// level's first budget row on load and that behaviour is correct for every other level, so
        /// the fix lives in what the tutorial stocks rather than in the controller. A future edit
        /// that reorders `TutorialLevel`'s budget would put the bug back, and this is the assertion
        /// that would say which change did it.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheTutorialDoesNotOpenWithItsOwnAnswerSelected()
        {
            yield return LoadScene();

            TutorialDirector director = Find<TutorialDirector>();
            PlacementController placement = Find<PlacementController>();

            Assert.IsNotNull(placement, "no placement controller in the scene");

            yield return BeginTutorial(director);

            Assert.AreNotEqual(TutorialLevel.Part, placement.Selected,
                "the tutorial stocks a decoy first precisely so the opening selection is the wrong " +
                "part; something has reordered its budget");
        }

        /// <summary>
        /// A board saved under the tutorial's key, as an older build could have written.
        /// </summary>
        /// <remarks>
        /// The kind is written as <see cref="GatePalette.Label"/> gives it -- "NOT" -- because that
        /// is what <see cref="BoardSerializer.ToSaved"/> writes and therefore what a real save
        /// contains. It used to be written as <c>(int)TutorialLevel.Part</c>, an integer into a
        /// string field, which no amount of JsonUtility leniency turns into a gate kind:
        /// GatePalette.TryParse refused it and the restore dropped the placement every time.
        ///
        /// That made the test that used this vacuous. It asserted the board was *not* restored, and
        /// the board could never have been restored, so it passed just as happily with the guard it
        /// exists to protect deleted. Hence the positive control below.
        /// </remarks>
        private static string ASavedTutorialBoardJson() =>
            "{\"completed\":[]," +
            "\"boards\":[{\"level\":\"" + TutorialLevel.Key + "\"," +
            "\"placements\":[{\"kind\":\"" + GatePalette.Label(TutorialLevel.Part) + "\"," +
            "\"x\":" + TutorialLevel.GateCell.x + ",\"y\":" + TutorialLevel.GateCell.y + "}]," +
            "\"wires\":[],\"bestGates\":0,\"bestLatency\":0}]," +
            "\"hintsSeen\":[],\"milestones\":[]}";

        [UnityTest]
        public IEnumerator ASavedTutorialBoard_IsNeverRestored()
        {
            // Planted before the scene loads, because ProgressTracker reads the file in Awake and
            // never looks again.
            SaveGuard.Plant(ASavedTutorialBoardJson());

            yield return LoadScene();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            ProgressTracker progress = Find<ProgressTracker>();

            yield return BeginTutorial(director);

            Assert.AreEqual(TutorialLevel.Key, session.LevelName, "the tutorial did not load");

            // The positive control, and the whole reason the assertion below means anything. The
            // same saved board, restored by hand into a throwaway blueprint against the tutorial's
            // own level: it has to come back with the gate in it. If it does not, the planted save
            // is malformed and an empty board proves nothing about the guard.
            SavedBoard saved = progress.Store.BoardFor(TutorialLevel.Key);
            Assert.IsNotNull(saved, "the planted save did not even parse into the store");

            var scratch = new CircuitBlueprint();
            int dropped = BoardSerializer.Restore(
                saved, TutorialLevel.Build(runner.HalfExtents), scratch, runner.HalfExtents);

            Assert.AreEqual(0, dropped,
                "the planted board was dropped by the restore, so it could never have landed on the " +
                "tutorial and the assertion below is vacuous");

            Assert.AreEqual(1, scratch.Placements.Count,
                "the planted board has to be restorable for its absence below to mean anything");

            // And now the thing actually under test: the real board stayed empty.
            Assert.IsEmpty(session.Blueprint.Placements,
                "a saved board was restored onto the tutorial, which satisfies its steps before the " +
                "player arrives");

            Assert.AreEqual(0, director.CurrentStep,
                "the tutorial opened part way through because a board was restored under it");
        }

        // -----------------------------------------------------------------
        // Running it twice in one session
        // -----------------------------------------------------------------

        /// <summary>
        /// The tutorial can be left part way through and started again.
        /// </summary>
        /// <remarks>
        /// TutorialHighlighter pools its rings, and a canvas ring used to be parented to whatever it
        /// pointed at. Step one rings a palette row -- and GatePaletteView destroys every row when
        /// the level changes, taking the ring with it. The pool then held a destroyed Image, and the
        /// next run threw MissingReferenceException out of Update on the frame it tried to point at
        /// anything, every frame, until the player skipped.
        ///
        /// A reachable path: start the tutorial, press Escape, pick a level from the list, then come
        /// back to the tutorial from the head of that same list.
        ///
        /// No explicit assertion is needed for the exception itself -- an unhandled one in Update is
        /// logged as an error and the framework fails the test on it. The assertions are that the
        /// tutorial actually got going both times, so the test cannot pass by never reaching the
        /// ring code.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheTutorialCanBeRunTwiceInOneSession()
        {
            yield return LoadScene();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();
            TutorialPanel panel = Find<TutorialPanel>();

            Assert.IsNotNull(panel, "no tutorial panel in the scene");
            Assert.Greater(session.AvailableLevels.Count, 0, "no levels to leave the tutorial for");

            string elsewhere = session.AvailableLevels[0];

            // First run, far enough in to ring the palette row.
            yield return BeginTutorial(director);
            yield return PastTheIntro(panel);

            Assert.IsTrue(director.IsRunning, "the tutorial did not start the first time");
            Assert.AreEqual(0, director.CurrentStep,
                "step one is the one that rings a palette row, and is where this has to be left");

            // Leaving the board destroys the palette rows, and with them anything parented to one.
            session.LoadLevel(elsewhere);
            yield return null;
            yield return null;

            Assert.IsFalse(director.IsRunning, "leaving the tutorial's board should end it");

            // Second run. The pooled ring from the first has to still be usable.
            yield return BeginTutorial(director);
            yield return PastTheIntro(panel);

            Assert.IsTrue(director.IsRunning, "the tutorial did not start the second time");
            Assert.AreEqual(TutorialLevel.Key, session.LevelName, "the tutorial's board did not load");

            Assert.AreEqual(0, director.CurrentStep,
                "the tutorial came back on the wrong step, so the board it adopted was not fresh");

            // A few more frames, because the failure was one exception per frame rather than one.
            for (int frame = 0; frame < 5; frame++)
                yield return null;

            Assert.IsTrue(director.IsRunning, "the tutorial stopped by itself during its second run");
        }

        /// <summary>
        /// Presses the opening card's START, so the director leaves Intro for the steps.
        /// </summary>
        /// <remarks>
        /// The intro phase highlights nothing, so a test that stops there never reaches the ring
        /// pool at all -- which is how this defect would have gone on hiding from a test that
        /// merely called Begin.
        /// </remarks>
        private static IEnumerator PastTheIntro(TutorialPanel panel)
        {
            panel.Next();

            yield return null;   // the director consumes the press and enters Steps
            yield return null;   // and runs a step, which is what points at a palette row
        }

        // -----------------------------------------------------------------
        // It does not start itself at the wrong moment
        // -----------------------------------------------------------------

        /// <summary>
        /// Auto-launch waits for the menu, rather than firing on startup.
        /// </summary>
        /// <remarks>
        /// The main menu holds `UiModal` at boot, so "is anything covering the board" is true on the
        /// very first frame. A director that offered the tutorial whenever the board was clear would
        /// therefore fire underneath the menu, and the player's first sight of the game would be a
        /// tutorial they had not chosen, behind a menu they had not dismissed.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheTutorialDoesNotLaunchItselfWhileTheMenuIsUp()
        {
            yield return LoadScene();

            TutorialDirector director = Find<TutorialDirector>();

            Assert.IsTrue(UiModal.AnyOpen,
                "sanity: the game is expected to boot into the main menu, which holds UiModal");

            // Long enough that a director which was going to fire would have.
            for (int frame = 0; frame < 10; frame++)
                yield return null;

            Assert.IsFalse(director.IsRunning,
                "the tutorial started underneath the main menu");
        }

        // -----------------------------------------------------------------
        // It is still not a level
        // -----------------------------------------------------------------

        /// <summary>
        /// Starting the tutorial records no progress of any kind.
        /// </summary>
        /// <remarks>
        /// The off-catalogue rule, checked against the real store rather than against
        /// `LevelCatalog.IsOffCatalogue` in isolation. The tutorial is graded, so it reaches all the
        /// code that records a solve -- and once marked itself complete, took a personal best and
        /// reported itself to analytics. Edit Mode pins the rule; this pins that the rule is actually
        /// reached on the path a player takes.
        /// </remarks>
        [UnityTest]
        public IEnumerator StartingTheTutorial_RecordsNothingAgainstTheRun()
        {
            yield return LoadScene();

            TutorialDirector director = Find<TutorialDirector>();
            ProgressTracker progress = Find<ProgressTracker>();

            int before = progress.Store.CompletedCount;

            yield return BeginTutorial(director);

            for (int frame = 0; frame < 5; frame++)
                yield return null;

            Assert.IsFalse(progress.Store.IsComplete(TutorialLevel.Key),
                "the tutorial wrote itself into the completed list");

            Assert.AreEqual(before, progress.Store.CompletedCount,
                "starting the tutorial changed how many levels the run thinks are solved");
        }

        [UnityTest]
        public IEnumerator TheTutorialIsNeverInTheLevelRotation()
        {
            // Q and E walk AvailableLevels. The tutorial appearing there would make it a tenth level
            // and put the banner's count out by one.
            yield return LoadScene();

            LevelSession session = Find<LevelSession>();

            CollectionAssert.DoesNotContain(session.AvailableLevels, TutorialLevel.Key,
                "the tutorial has leaked into the level rotation");

            CollectionAssert.DoesNotContain(session.AvailableLevels, SandboxLevel.Key,
                "free play has leaked into the level rotation");
        }
    }
}
