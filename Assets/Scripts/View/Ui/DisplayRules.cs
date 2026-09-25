using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The fullscreen switch's decisions, apart from the screen they act on so they can be tested.
    /// </summary>
    public static class DisplayRules
    {
        /// <summary>
        /// Whether this build offers the switch: a desktop player does, a browser does not.
        /// </summary>
        /// <remarks>
        /// In a browser the page around the game has its own fullscreen button, and fullscreen there
        /// is the browser's to grant -- it refuses a request that does not come straight from a
        /// click on the page, and Escape leaves it whatever the game says.
        /// </remarks>
        public static bool Offered =>
#if UNITY_WEBGL && !UNITY_EDITOR
            false;
#else
            true;
#endif

        /// <summary>
        /// The window a desktop build opens at: the player settings' default size, which
        /// <c>DisplayRulesTests</c> holds this to.
        /// </summary>
        public static readonly Vector2Int PreferredWindow = new Vector2Int(1600, 900);

        /// <summary>The most of the screen a window may take, so its title bar stays in view.</summary>
        public const float MostOfTheScreen = 0.9f;

        /// <summary>
        /// The window to come back to from fullscreen, on a display of the given size.
        /// </summary>
        /// <remarks>
        /// Leaving fullscreen by changing only the mode keeps the fullscreen resolution, which is a
        /// window the size of the whole display with its title bar off the top. This is the size
        /// the game opens at, shrunk to fit a smaller display with its shape kept.
        /// </remarks>
        public static Vector2Int WindowedSize(int displayWidth, int displayHeight)
        {
            if (displayWidth <= 0 || displayHeight <= 0)
                return PreferredWindow;

            float scale = Mathf.Min(1f,
                MostOfTheScreen * displayWidth / PreferredWindow.x,
                MostOfTheScreen * displayHeight / PreferredWindow.y);

            return new Vector2Int(
                Mathf.RoundToInt(PreferredWindow.x * scale),
                Mathf.RoundToInt(PreferredWindow.y * scale));
        }
    }
}
