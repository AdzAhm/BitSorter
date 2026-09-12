using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
