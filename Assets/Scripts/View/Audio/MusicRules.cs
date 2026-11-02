using System;

namespace BitSorter.View
{
    /// <summary>
    /// Which recording Settings opens on, apart from Unity so it can be tested without a scene.
    /// </summary>
    /// <remarks>
    /// Which generated track a session plays is <see cref="MusicBag"/>'s answer: a shuffle, but a fair one. The 4.x game
    /// moved the music on as its level changed; with the levels gone (Part 16a) a session keeps its first track.
    /// </remarks>
    public static class MusicRules
    {
        /// <summary>
        /// Which of the recordings a session opens on: either, by chance.
        /// </summary>
        /// <remarks>
        /// From the session's own seed, so that seed stays the only random thing about the music.
        /// The menu always used to open on its first track, and the second came on only once the
        /// first had played to its end -- two and a half minutes -- so a player could restart any
        /// number of times and never hear it. After the first, the tracks take turns;
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
