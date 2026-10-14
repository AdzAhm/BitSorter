using UnityEngine;
using UnityEngine.SceneManagement;

namespace BitSorter.View
{
    /// <summary>
    /// Puts the player's chosen look on before the first scene, and the current look's bloom on
    /// every scene as it loads.
    /// </summary>
    /// <remarks>
    /// Everything else a look decides is read by whatever draws it, as it builds. Bloom is the one
    /// part that lives in an asset -- the scene's volume profile, which holds Classic's values -- so
    /// something has to set it, and it has to happen for every scene, not only the first: the tests
    /// load the scene again and again, and a look's bloom that only arrived once would be a look
    /// half-applied on every reload after.
    ///
    /// Hooked before the first scene loads, the way <c>GameAnalytics.Boot</c> is, so no component in
    /// the scene has to remember to do it.
    ///
    /// **The chosen look is put on in a build, never in the editor**, as the frame cap is: every Play
    /// Mode test and reference shot would otherwise be drawn in whatever look the developer last
    /// picked. In the editor a choice made in Settings lasts the session (<see cref="LookSwitch"/>).
    /// </remarks>
    internal static class LookBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            if (!Application.isEditor)
                Look.Use(Look.Chosen);

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Look.ApplyBloom();
    }
}
