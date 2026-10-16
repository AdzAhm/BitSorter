using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Each value's text, formatted once: a signal's change, in a radix, is written by
    /// <see cref="ValueText.Of"/> the first time it is asked for and handed back as the same string
    /// every time after, so a frame of playback allocates nothing once its window has been formatted.
    /// </summary>
    /// <remarks>
    /// **Lazily, a change at a time, per radix.** <see cref="SignalTrack"/> formats every settled change
    /// as it grows, in the one radix there was; with a radix per row that would be six strings for
    /// every change of a run that may have a hundred thousand of them, nearly all never scrolled into
    /// view. Here a text is made the first time a frame draws its change, in the radix its row is read
    /// in then. Switching a row's radix formats what is on screen once, and switching it back costs
    /// nothing at all: the old radix's strings are still here.
    ///
    /// **A key is one signal of one run.** What is kept is keyed by (key, change index, radix), and
    /// the cache trusts that the index-th change of a key always holds the same value, with the same
    /// enum -- which is true of a run's history, where a settled change is never rewritten
    /// (<see cref="SignalTrack"/>). It does not hold the values to check: they are the history's, and a
    /// second copy of a run's every value would cost as much as the history keeps. **A run made again**
    /// -- every edit is a replay from time 0 (ADR-6) -- **is a new set of values under the same
    /// indexes**, so whatever owns the cache calls <see cref="Clear"/> then, or makes a new one with
    /// the new run.
    ///
    /// Storage grows by doubling, per key and radix, as indexes are first asked for: the only
    /// allocations once a window is formatted are the ones a wider window makes. Any int is a key --
    /// a signal's index, a row's -- since keys go through a dictionary rather than an array.
    /// </remarks>
    public sealed class TextCache
    {
        /// <summary>How many radixes there are, so a key's texts are one array per radix.</summary>
        private const int Radixes = (int)WaveRadix.Symbolic + 1;

        private readonly Dictionary<int, string[][]> _byKey = new Dictionary<int, string[][]>();

        /// <summary>How many texts it has formatted since it was made or cleared: one per (key, index, radix) asked for.</summary>
        public int Made { get; private set; }

        /// <summary>
        /// The text of change <paramref name="index"/> of <paramref name="key"/>, in
        /// <paramref name="radix"/>: formatted from <paramref name="value"/> the first time, the same
        /// string after. <paramref name="value"/> and <paramref name="enumInfo"/> must be what that change
        /// holds; see the remarks.
        /// </summary>
        public string Get(int key, int index, LogicVec value, WaveRadix radix, EnumInfo enumInfo = null)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), index, "A change's index is 0 or more.");

            int r = (int)radix;
            if (r < 0 || r >= Radixes)
                throw new ArgumentOutOfRangeException(nameof(radix), radix, "Not a radix a waveform reads in.");

            if (!_byKey.TryGetValue(key, out string[][] radixes))
            {
                radixes = new string[Radixes][];
                _byKey.Add(key, radixes);
            }

            string[] texts = radixes[r];
            if (texts == null || index >= texts.Length)
            {
                // Doubled, or as far as the index if that is further: never a loop that could overflow.
                int doubled = texts == null ? 8 : texts.Length * 2;
                Array.Resize(ref texts, Math.Max(doubled, index + 1));
                radixes[r] = texts;
            }

            string text = texts[index];
            if (text == null)
            {
                text = ValueText.Of(value, radix, enumInfo);
                texts[index] = text;
                Made++;
            }

            return text;
        }

        /// <summary>Forgets every text: for a run made again, whose changes are new values under the same indexes.</summary>
        public void Clear()
        {
            _byKey.Clear();
            Made = 0;
        }
    }
}
