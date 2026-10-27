using System;

namespace BitSorter.View
{
    /// <summary>
    /// What BitSorter opens on (Part 15a): the simulator -- the workbench on the last thing that was open, or its start page
    /// -- where 4.x opened on its title screen. The 4.x game is still here until Part 16, one quiet row down in Settings.
    /// </summary>
    /// <remarks>
    /// <see cref="Classic"/> is the 4.x boot, kept for the Play Mode tests written against it, which every one of them was
    /// until Part 15: the title screen up as the scene loads, and the board under it. They ask for it through
    /// <c>TestScene.Load</c>; the game never does.
    /// </remarks>
    public static class AppLaunch
    {
        /// <summary>Whether the game opens on the 4.x title screen rather than the simulator. Only the tests set it.</summary>
        public static bool Classic { get; set; }

        /// <summary>
        /// Whether the 4.x game has been on screen this session: from the start in <see cref="Classic"/>, and in the
        /// simulator once Settings' 4.x GAME has opened its title screen. What analytics waits for before it says a level
        /// started, since the simulator loads one under the workbench that nobody has seen.
        /// </summary>
        public static bool GameShown => Classic || _gameShown;

        private static bool _gameShown;

        /// <summary>Raised the first time the 4.x game comes on screen in the simulator.</summary>
        public static event Action GameCameOnScreen;

        /// <summary>The 4.x game is on screen: its title screen, opened from Settings.</summary>
        public static void ShowTheGame()
        {
            if (GameShown)
                return;
            _gameShown = true;
            GameCameOnScreen?.Invoke();
        }

        /// <summary>A new session, as far as <see cref="GameShown"/> goes: for the tests, each of which is one.</summary>
        public static void Forget() => _gameShown = false;
    }
}
