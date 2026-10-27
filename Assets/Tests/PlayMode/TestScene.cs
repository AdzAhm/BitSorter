using System.Collections;
using UnityEngine;
using BitSorter.View;
using UnityEngine.SceneManagement;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Loads the real scene for a test, and takes it away again afterwards.
    /// </summary>
    /// <remarks>
    /// The second half is the part that matters, and it was learned the hard way. A fixture that
    /// loads the game and simply finishes leaves it loaded for whatever runs next -- and NUnit orders
    /// fixtures alphabetically, so `AudioPlayTests` ran first and handed a fully built game to
    /// `PointerArbitrationPlayTests`, which builds its own canvas and assumes nothing else is on
    /// screen. Two of its four tests went red without a line of its code changing: the main menu is a
    /// full-screen panel, so the pointer was over UI at every coordinate, and `PointerGate` correctly
    /// reported that the interface owned the mouse.
    ///
    /// That is a test leaking into another test, which is worse than either test failing -- the
    /// failure appears in the fixture that is still correct, and points nowhere near the cause.
    ///
    /// Only the game scene is unloaded by name. The test framework runs from a scene of its own and
    /// unloading everything that is not ours would take that with it.
    ///
    /// **The game runs on a driven clock**: from the load to the clear, every frame is exactly
    /// <see cref="FrameSeconds"/> of game time. A wait capped in frames used to mean seconds only at
    /// the editor's frame rate. Headless, frames are drawn unthrottled, and on 2026-10-12 a 1200-frame
    /// cap meant to cover 1.5 seconds of a hint covered as little as 0.57; a fixed 240-frame wait for
    /// bits to travel did not last one tick of the simulation, so its assertions about bits held of
    /// none. Driven, a frame is the same game time on every machine, and a cap in frames is a cap in
    /// seconds. A fixture that wants a step of its own sets it before the load, and keeps it; one that
    /// changes the step in the middle of a test puts <see cref="FrameSeconds"/> back, never zero.
    ///
    /// **The unscaled clock is not driven**: it stays real time (measured on the day, a step of a
    /// sixtieth with frames really 0.0067 seconds apart). So a wait on something counted in unscaled
    /// time -- the save's delay, the frame-rate counter, the music's cross-fade -- caps in unscaled
    /// seconds, as the audio tests do, never in frames.
    /// </remarks>
    internal static class TestScene
    {
        internal const string GameScene = "HalfAdderDemo";

        /// <summary>The game time each frame takes while a test has the game loaded: a sixtieth of a second.</summary>
        internal const float FrameSeconds = 1f / 60f;

        private static int _cleared;

        /// <summary>
        /// Loads the game as 4.x booted, its title screen up over the board, on a driven clock, and lets it finish waking up:
        /// what every fixture written before Part 15a expects (<see cref="AppLaunch.Classic"/>).
        /// </summary>
        internal static IEnumerator Load() => Load(classic: true);

        /// <summary>Loads the game as it opens since Part 15a: the simulator, on the last thing open or the start page.</summary>
        internal static IEnumerator LoadSimulator() => Load(classic: false);

        private static IEnumerator Load(bool classic)
        {
            AppLaunch.Classic = classic;
            AppLaunch.Forget();

            // Before the load, so the frames the scene takes to wake are driven too. The reference
            // shots choose a 120th before they load, and that choice stands.
            if (Time.captureDeltaTime <= 0f)
                Time.captureDeltaTime = FrameSeconds;

            yield return SceneManager.LoadSceneAsync(GameScene, LoadSceneMode.Single);

            // Two frames past the load. Awake and Start have both run by the first, but components
            // that find each other in Start only agree by the second.
            yield return null;
            yield return null;
        }

        /// <summary>Leaves an empty scene active, the game gone, and no save behind it.</summary>
        /// <remarks>
        /// The save is deleted here, after the unload, because the game writes it until it is gone.
        /// Every fixture also deletes it in its [TearDown], and Unity Test Framework runs that before
        /// the [UnityTearDown] that calls this -- with the game still loaded, so a write in the frames
        /// the unload took was there for the next test's load. <c>ProgressTracker</c> writes the
        /// board half a second after it last changed, by real time, so that leak would have come and
        /// gone with how long frames took rather than with anything in the tests.
        /// </remarks>
        internal static IEnumerator Clear()
        {
            // An empty scene has to exist and be active before the game can be unloaded: Unity
            // refuses to unload the last loaded scene, and the active scene is where anything built
            // after this lands.
            Scene empty = SceneManager.CreateScene("Cleared" + _cleared++);
            SceneManager.SetActiveScene(empty);

            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (scene.isLoaded && scene.name == GameScene)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            yield return null;

            // Nothing is left to write it now.
            SaveGuard.Clear();

            // Real time back, whatever step the test left: the next fixture may not load the game.
            Time.captureDeltaTime = 0f;
            AppLaunch.Classic = false;
            AppLaunch.Forget();
        }
    }
}
