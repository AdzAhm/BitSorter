using System;

namespace BitSorter.View
{
    /// <summary>
    /// When the background track changes, and which one comes next.
    /// </summary>
    /// <remarks>
    /// Pure, so the answer can be tested without a scene, an AudioSource or a frame loop -- the same
    /// reason <see cref="HintRules"/> and <see cref="MenuRules"/> exist. <see cref="GameAudio"/> is
    /// left with the part that genuinely needs Unity: the fade and the clip swap.
    ///
    /// **The track changes only when the level does.** Three things follow from that, and each one
    /// is a way this could have gone wrong:
    ///
    /// It never switches mid-level, so a track cannot change under a player halfway through working
    /// something out. There is no timer here at all -- a level load is the only event that moves it.
    ///
    /// It never switches on a retry. Reset and re-entering the same level both re-fire
    /// <c>LevelLoaded</c>, so cycling on every load would change the music each time a player failed
    /// a level -- drawing attention to exactly the level they are already stuck on.
    ///
    /// It never switches immediately after boot. The first level loads during <c>Start</c>, a moment
    /// after the music began, and cycling there would cross-fade the opening a second after the
    /// player first heard it.
    ///
    /// Which track comes next is <see cref="MusicBag"/>'s answer: a shuffle, but a fair one.
    /// </remarks>
    public static class MusicRules
    {
        /// <summary>
        /// Whether a level load should move the music on.
        /// </summary>
        /// <param name="playingUnder">Level the current track was chosen for, or null at boot.</param>
        /// <param name="loading">Level being loaded now.</param>
        public static bool ChangesTrack(string playingUnder, string loading)
        {
            // Nothing has played under a level yet. The track picked at startup belongs to the first
            // level rather than to the menu, so the first load adopts it instead of replacing it.
            if (string.IsNullOrEmpty(playingUnder))
                return false;

            // A retry, a reset, or stepping back into the level already on screen.
            return !string.Equals(playingUnder, loading, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether the menu's own music should be playing, given which panels are up.
        /// </summary>
        /// <remarks>
        /// The main menu and the level list are both full-screen panels for choosing what to play
        /// next, and the menu's music belongs to that, not to the main menu specifically -- opening
        /// the level list over a level used to drop the player into silence-but-for-the-level-track
        /// while they browsed seventeen rows.
        ///
        /// This does not bend "the track changes only when the level does". Neither panel is a
        /// level change: the level's own clip is kept underneath and resumes when the panel closes,
        /// which is exactly what the main menu already did. It only widens what counts as being in
        /// a menu.
        /// </remarks>
        public static bool WantsMenuMusic(bool mainMenuOpen, bool levelListOpen) =>
            mainMenuOpen || levelListOpen;

        /// <summary>
        /// Which of the main menu's tracks a session opens on: either, by chance.
        /// </summary>
        /// <remarks>
        /// From the session's own seed, so that seed stays the only random thing about the music.
        /// The menu always used to open on its first track, and the second came on only once the
        /// first had played to its end -- two and a half minutes -- so a player could restart any
        /// number of times and never hear it. After the first, the menu's tracks take turns;
        /// <see cref="GameAudio"/> keeps the turn.
        /// </remarks>
        public static int FirstMenuTrack(int seed, int count)
        {
            if (count <= 1)
                return 0;

            // A stream of its own. The shuffle's first draw comes from new Random(seed), so reusing
            // that would tie the menu's opening track to the first level's. Knuth's multiplicative
            // hash spreads neighbouring seeds apart; the high bits are the well-mixed ones.
            uint mixed = unchecked((uint)seed * 2654435761u);

            return (int)((mixed >> 16) % (uint)count);
        }
    }

    /// <summary>
    /// The order the background tracks play in: all of them once, in a random order, then all of
    /// them again in a fresh one -- and never the same track twice running.
    /// </summary>
    /// <remarks>
    /// A shuffle rather than a cycle, because with two dozen tracks a fixed order is one a player
    /// would learn. A bag rather than a fresh random pick each time, because a pure random pick
    /// repeats: it plays the same track twice running about once in every two dozen changes, and
    /// leaves some track unheard for an hour. Drawing every track once per bag fixes both, and the
    /// one seam a bag has -- its last track being the next bag's first -- is closed by hand.
    ///
    /// Seeded, and the seed is the only random thing about it. <see cref="GameAudio"/> picks one
    /// per session, so two evenings on the same levels are not the same evening, while a test can
    /// build the same bag and say exactly which track should be playing.
    ///
    /// The next bag is drawn one bag early, so <see cref="PeekNext"/> can see across the seam.
    /// GameAudio bakes the next track while this one plays, and it has to know which.
    /// </remarks>
    public sealed class MusicBag
    {
        private readonly int _count;
        private readonly Random _random;
        private int[] _order;
        private int[] _following;
        private int _position;

        public MusicBag(int count, int seed)
        {
            _count = count < 0 ? 0 : count;
            _random = new Random(seed);

            _order = Shuffled(avoidFirst: -1);
            _following = Shuffled(avoidFirst: Last(_order));
        }

        /// <summary>The track playing now. Zero for an empty set, rather than a throw.</summary>
        public int Current => _count == 0 ? 0 : _order[_position];

        /// <summary>What <see cref="Advance"/> will return, without moving.</summary>
        public int PeekNext()
        {
            if (_count == 0)
                return 0;

            return _position + 1 < _count ? _order[_position + 1] : _following[0];
        }

        /// <summary>Moves on to the next track, and returns it.</summary>
        public int Advance()
        {
            if (_count == 0)
                return 0;

            _position++;

            if (_position >= _count)
            {
                _order = _following;
                _following = Shuffled(avoidFirst: Last(_order));
                _position = 0;
            }

            return Current;
        }

        private int Last(int[] order) => order.Length > 0 ? order[order.Length - 1] : -1;

        /// <summary>Every track once, in a random order, not starting with <paramref name="avoidFirst"/>.</summary>
        private int[] Shuffled(int avoidFirst)
        {
            var order = new int[_count];

            for (int i = 0; i < _count; i++)
                order[i] = i;

            // Fisher-Yates: every order equally likely.
            for (int i = _count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            // The seam between two bags. Starting this one with the track that ended the last would
            // play it twice running, so it trades places with some other track in the bag.
            if (_count > 1 && order[0] == avoidFirst)
            {
                int j = 1 + _random.Next(_count - 1);
                (order[0], order[j]) = (order[j], order[0]);
            }

            return order;
        }
    }
}
