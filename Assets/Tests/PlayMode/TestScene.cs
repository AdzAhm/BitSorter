using System.Collections;
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
    /// </remarks>
    internal static class TestScene
    {
        internal const string GameScene = "HalfAdderDemo";

        private static int _cleared;

        /// <summary>Loads the game and lets it finish waking up.</summary>
        internal static IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync(GameScene, LoadSceneMode.Single);

            // Two frames past the load. Awake and Start have both run by the first, but components
            // that find each other in Start only agree by the second.
            yield return null;
            yield return null;
        }

        /// <summary>Leaves an empty scene active and the game gone.</summary>
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
        }
    }
}
