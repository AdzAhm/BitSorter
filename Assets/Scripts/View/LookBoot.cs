using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Puts the player's chosen look on before the first scene.
    /// </summary>
    /// <remarks>
    /// Everything a look decides is read by whatever draws it, as it builds, so the look only has to be the current
    /// one before the scene loads. Hooked before the first scene loads, the way <c>GameAnalytics.Boot</c> is, so no
    /// component in the scene has to remember to do it.
    ///
    /// **The chosen look is put on in a build, never in the editor**, as the frame cap is: every Play
    /// Mode test and reference shot would otherwise be drawn in whatever look the developer last
    /// picked. In the editor a choice made in Settings lasts the session (<see cref="LookSwitch"/>).
    ///
    /// It put the current look's bloom on every scene as it loaded as well, until Part 16b took bloom out: the overlay
    /// canvas and the shell panel are drawn after post-processing, so it never showed.
    /// </remarks>
    internal static class LookBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            if (!Application.isEditor)
                Look.Use(Look.Chosen);
        }
    }
}
