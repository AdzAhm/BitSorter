using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>The five things the game has to say out loud.</summary>
    public enum Cue
    {
        /// <summary>The clock advanced. Plays constantly, so it has to be almost nothing.</summary>
        Tick,

        /// <summary>A gate consumed its inputs and emitted.</summary>
        Gate,

        /// <summary>A bit reached a bin.</summary>
        Land,

        /// <summary>Bits were destroyed at an input port.</summary>
        Collide,

        /// <summary>The level was solved.</summary>
        Win,

        /// <summary>A long, quiet loop under everything else.</summary>
        Music,
    }

    /// <summary>
    /// Every sound in the game, generated at runtime. No audio files.
    /// </summary>
    /// <remarks>
    /// The direct counterpart of <see cref="ProceduralSprites"/>, and chosen for the same reasons:
    /// nothing to license, nothing to keep in the repository, and every cue tunable as numbers next
    /// to the code that plays it. The character is synthetic, which suits a game about logic gates.
    ///
    /// Clips are cached by cue, so each waveform is built once however many times it is asked for.
    ///
    /// The mix is deliberately lopsided. Tick and Gate fire many times a second and are barely
    /// audible on purpose -- they are texture, not information. Collide and Win are the two events a
    /// player must never miss, so they are loud, and Collide is the only harsh sound in the set.
    /// </remarks>
    public static class ProceduralAudio
    {
        private const int SampleRate = 44100;

        private static readonly Dictionary<Cue, AudioClip> Cache = new Dictionary<Cue, AudioClip>();

        /// <summary>Suggested volume per cue, so callers do not each invent their own balance.</summary>
        public static float VolumeOf(Cue cue)
        {
            switch (cue)
            {
                case Cue.Tick: return 0.10f;
                case Cue.Gate: return 0.14f;
                case Cue.Land: return 0.45f;
                case Cue.Collide: return 0.75f;
                case Cue.Win: return 0.65f;

                // Under everything. Music that competes with the collision cue would bury the one
                // sound a player must never miss.
                case Cue.Music: return 0.16f;

                default: return 0.5f;
            }
        }

        public static AudioClip Clip(Cue cue)
        {
            // Music is not one clip but a set of tracks, and a track's clip belongs to whoever built
            // it -- GameAudio keeps two at a time and frees the rest. A cached "the music" here would
            // be a track held for the whole session by nobody in particular, so asking is a mistake
            // worth hearing about rather than a clip worth handing back.
            if (cue == Cue.Music)
            {
                throw new ArgumentException(
                    "Music is a set of tracks: build one with MusicClip or BakeMusic.", nameof(cue));
            }

            if (Cache.TryGetValue(cue, out AudioClip cached) && cached != null)
                return cached;

            AudioClip clip = Build(cue);
            Cache[cue] = clip;
            return clip;
        }

        private static AudioClip Build(Cue cue)
        {
            switch (cue)
            {
                // A soft, high click. Short enough that two ticks never overlap at any sane speed.
                case Cue.Tick:
                    return Make("tick", 0.035f, (t, d) => Sine(t, 1180f) * Decay(t, d, 26f) * 0.6f);

                // A tick's quieter cousin, a fifth below, so a firing gate reads as related to the
                // clock rather than as a separate kind of event.
                case Cue.Gate:
                    return Make("gate", 0.045f, (t, d) => Triangle(t, 786f) * Decay(t, d, 22f) * 0.5f);

                // A falling thunk: something arriving and staying put.
                case Cue.Land:
                    return Make("land", 0.16f, (t, d) =>
                    {
                        float pitch = Mathf.Lerp(420f, 190f, t / d);
                        return Sine(t, pitch) * Decay(t, d, 9f);
                    });

                // The only harsh sound in the game. Noise, because a collision is not a note -- it is
                // the one thing that must never be mistaken for part of the rhythm.
                case Cue.Collide:
                    return Make("collide", 0.22f, (t, d) =>
                    {
                        float envelope = Decay(t, d, 7f);
                        float grit = Noise(t) * 0.75f;
                        float body = Sine(t, Mathf.Lerp(220f, 70f, t / d)) * 0.6f;
                        return (grit + body) * envelope;
                    });

                // Four notes up a major triad, each with its own decay so they ring into each other.
                case Cue.Win:
                    return Make("win", 0.85f, (t, d) =>
                    {
                        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.50f };
                        const float spacing = 0.13f;

                        float total = 0f;
                        for (int i = 0; i < notes.Length; i++)
                        {
                            float start = i * spacing;
                            if (t < start)
                                continue;

                            float age = t - start;
                            total += Sine(age, notes[i]) * Decay(age, d - start, 4.5f) * 0.4f;
                        }

                        return total;
                    });

                // Music never reaches here: Clip refuses it before Build is called.
                default:
                    return Make("silence", 0.01f, (t, d) => 0f);
            }
        }

        // -----------------------------------------------------------------
        // The background tracks
        // -----------------------------------------------------------------

        /// <summary>How many background tracks exist. <see cref="MusicRules"/> decides the order.</summary>
        public static int MusicTracks => Tracks.Length;

        /// <summary>How long one track runs, in seconds.</summary>
        /// <remarks>
        /// Derived from the chord cycle, exactly as the clip is. Exposed so the set's memory cost
        /// can be asserted rather than worked out by hand -- see <see cref="MusicResidentBytes"/>.
        /// </remarks>
        public static float MusicSeconds(int index) =>
            Tracks[Mathf.Clamp(index, 0, Tracks.Length - 1)].Seconds;

        /// <summary>
        /// Heap one built track costs. Every track is the same length, which MusicTests asserts.
        /// </summary>
        /// <remarks>
        /// Mono, uncompressed, four bytes a sample: clips are created with a stream flag of false, so
        /// a built track is a float array for as long as its clip lives.
        /// </remarks>
        public static int MusicTrackBytes =>
            Mathf.RoundToInt(Tracks[0].Seconds * MusicSampleRate) * sizeof(float);

        /// <summary>
        /// The most music heap the game ever holds: the track playing, the next one ready, and the
        /// buffer the one after that is being built in.
        /// </summary>
        /// <remarks>
        /// The figure the <see cref="MusicSampleRate"/> remarks argue from, computed rather than
        /// written down. It used to be the whole set, because every track built stayed for the
        /// session -- and a comment stating that figure for six tracks still said six after three
        /// more were added. Tracks are freed as they are left now, so the count of tracks no longer
        /// moves this at all; only their length and the rate do.
        /// </remarks>
        public static int MusicResidentBytes => 3 * MusicTrackBytes;

        /// <summary>
        /// The notes a track plays, as semitones above A4, with its rests removed.
        /// </summary>
        /// <remarks>
        /// The one property that has to hold across the whole set: every track is A minor pentatonic
        /// and stays there, which is what lets any of them follow any other without the switch
        /// sounding like a key change. Exposed so that is checkable rather than a comment -- the
        /// figures themselves are just numbers, and a wrong one would be inaudible to read and
        /// obvious to hear.
        /// </remarks>
        public static IReadOnlyList<int> MusicNotes(int index)
        {
            index = Mathf.Clamp(index, 0, Tracks.Length - 1);

            var notes = new List<int>();

            foreach (int semi in Tracks[index].Figure)
            {
                if (semi != Rest)
                    notes.Add(semi);
            }

            return notes;
        }

        /// <summary>A step with no note on it. Negative, and no note is ever written this low.</summary>
        private const int Rest = -1;


        /// <summary>
        /// One background track, built now, in full. The caller owns the clip and frees it.
        /// </summary>
        /// <remarks>
        /// Nothing is cached here any more. Every track built used to be kept for the session, which
        /// was affordable at nine tracks and is not at twice that: what is resident is now
        /// <see cref="GameAudio"/>'s decision, and it keeps two.
        ///
        /// An out-of-range index is clamped rather than thrown on, because the failure mode of a
        /// throw here is silence with a stack trace behind it.
        /// </remarks>
        public static AudioClip MusicClip(int index)
        {
            MusicBake bake = BakeMusic(index);
            bake.Step(int.MaxValue);
            return bake.ToClip();
        }

        /// <summary>A track's samples, rendered in full. For tests, and anything that wants to measure.</summary>
        public static float[] MusicSamples(int index)
        {
            MusicBake bake = BakeMusic(index);
            bake.Step(int.MaxValue);
            return bake.Samples;
        }

        /// <summary>
        /// Starts rendering a track, for the caller to advance a slice at a time.
        /// </summary>
        /// <remarks>
        /// Rendering a track costs a few hundred milliseconds, and doing it in one go on the frame it
        /// is needed was a visible stall at a level change. Advanced a little each frame while the
        /// previous track plays, it is ready before it is wanted and costs nothing anyone can see.
        /// </remarks>
        public static MusicBake BakeMusic(int index) =>
            new MusicBake(Mathf.Clamp(index, 0, Tracks.Length - 1));

        /// <summary>
        /// One background track being rendered, a slice at a time.
        /// </summary>
        /// <remarks>
        /// Every step of the one-shot render survives, in the same order, so the result is the same
        /// to the last bit however it is sliced: each sample is rendered and clamped, the reverb adds
        /// what three earlier samples left, and the end is faded once the last sample exists. The
        /// reverb only ever reads samples already finished, which is what lets it run interleaved
        /// with the render instead of as a second pass over a finished buffer.
        /// </remarks>
        public sealed class MusicBake
        {
            private readonly Track _track;
            private readonly float _seconds;
            private readonly float[] _samples;
            private readonly int[] _taps;
            private int _next;

            internal MusicBake(int index)
            {
                Index = index;
                _track = Tracks[index];
                _seconds = _track.Seconds;
                _samples = new float[Mathf.Max(1, Mathf.RoundToInt(_seconds * MusicSampleRate))];

                // Three delay taps fed back into the signal as it is written, so each repeat is
                // itself repeated and the tail decays smoothly instead of arriving as three distinct
                // echoes. The delays are deliberately not multiples of each other -- taps that line up
                // read as a rhythm, which is the one thing this must not add. Total loop gain is
                // three times the track's tail, so that has to stay well under a third or the tail
                // grows instead of fading.
                _taps = new[]
                {
                    Mathf.RoundToInt(0.0371f * MusicSampleRate),
                    Mathf.RoundToInt(0.0533f * MusicSampleRate),
                    Mathf.RoundToInt(0.0719f * MusicSampleRate),
                };
            }

            /// <summary>Which track this is.</summary>
            public int Index { get; }

            /// <summary>Whether every sample has been rendered.</summary>
            public bool IsDone => _next >= _samples.Length;

            /// <summary>How far through, 0 to 1.</summary>
            public float Progress => (float)_next / _samples.Length;

            /// <summary>The finished samples, or null while it is still rendering.</summary>
            public float[] Samples => IsDone ? _samples : null;

            /// <summary>Renders up to <paramref name="count"/> more samples. True once finished.</summary>
            public bool Step(int count)
            {
                if (IsDone)
                    return true;

                int end = count >= _samples.Length - _next ? _samples.Length : _next + count;
                float feedback = _track.Tail;

                for (int i = _next; i < end; i++)
                {
                    float t = (float)i / MusicSampleRate;

                    // Clamped rather than normalised: a track that clipped would be a bug in its own
                    // numbers, and silently rescaling it would hide that while changing the mix.
                    float sample = Mathf.Clamp(Sample(_track, t, _seconds), -1f, 1f);

                    if (feedback > 0f)
                    {
                        float wet = 0f;

                        for (int k = 0; k < _taps.Length; k++)
                        {
                            if (i >= _taps[k])
                                wet += _samples[i - _taps[k]];
                        }

                        sample = Mathf.Clamp(sample + wet * feedback, -1f, 1f);
                    }

                    _samples[i] = sample;
                }

                _next = end;

                if (IsDone)
                    FadeEnd(_samples);

                return IsDone;
            }

            /// <summary>The finished track as a clip. The caller owns it.</summary>
            public AudioClip ToClip()
            {
                if (!IsDone)
                    throw new InvalidOperationException($"Track {Index} is still rendering.");

                AudioClip clip = AudioClip.Create("music" + Index, _samples.Length, 1, MusicSampleRate, false);
                clip.SetData(_samples, 0);
                return clip;
            }
        }

        /// <summary>Seconds per bar: one chord, and one pass of the figure.</summary>
        private const float BarSeconds = 8f;

        /// <summary>Seconds per step of the figure.</summary>
        private const float StepSeconds = 0.5f;

        /// <summary>
        /// Music is rendered at half the rate the cues are.
        /// </summary>
        /// <remarks>
        /// Nothing in these tracks comes near the Nyquist limit this leaves. The highest note any
        /// figure reaches is about 2 kHz, and nothing any voice adds to a note goes past 4.2 kHz,
        /// against a ceiling of 11 kHz.
        ///
        /// What it buys is memory: these clips are held as uncompressed floats and they are long.
        /// Ten tracks of 32 seconds cost about 27 MiB here, against about 54 MiB at the cue rate,
        /// in a game whose entire browser build is 16 MB. Tracks are built on first use, so a
        /// session only pays for the ones it reaches.
        ///
        /// Those numbers are <see cref="MusicResidentBytes"/>, and MusicTests asserts against it rather
        /// than against this paragraph. The paragraph said six tracks and 34 MB for a while after
        /// there were nine, because nothing was checking.
        ///
        /// The cost of the low rate is that anything sudden sits right at its top edge, where
        /// playback resampling turns it into a sizzle. That is one more reason nothing in a track
        /// may change value in a single sample -- see <see cref="Sample"/>.
        /// </remarks>
        public const int MusicSampleRate = 22050;

        /// <summary>
        /// How a track's notes are struck. The figures say which notes; this says what plays them.
        /// </summary>
        private enum Voice
        {
            /// <summary>
            /// A sine and its octave, at full amplitude a millisecond after the note starts.
            /// </summary>
            /// <remarks>
            /// The original six. An attack too fast to hear as a rise is what makes it read as
            /// plucked rather than played -- to the ear there is only a fall. Fast, but not a single
            /// sample: that was a click on every note.
            /// </remarks>
            Plucked,

            /// <summary>
            /// Struck and held: a soft rise, two oscillators beating against each other, a bell
            /// partial over the top, and a long tail.
            /// </summary>
            /// <remarks>
            /// Four differences, and each one is doing a specific job. The rise turns a pluck into a
            /// key being pressed. The second oscillator sits a few cents sharp so the two drift in
            /// and out of phase, which is the whole of that warm electric-piano shimmer. The partial
            /// three octaves-and-a-fifth up decays much faster than the fundamental, which is the
            /// metallic knock of a tine being hit. And the tail is reverb, applied to the finished
            /// buffer rather than per note.
            ///
            /// It is quieter at source than Plucked on purpose, because the reverb adds it back.
            /// </remarks>
            Keys,

            /// <summary>
            /// A struck wooden bar: the note, and a knock two octaves up that is gone in a fraction
            /// of a second.
            /// </summary>
            /// <remarks>
            /// A marimba's first overtone sits at four times the note rather than two, and it is what
            /// makes a struck bar sound like wood instead of like a plucked string. Short, so it
            /// bounces where the others ring, and no reverb, so it stays dry and close.
            ///
            /// The knock is the highest thing any track produces relative to its note, so the track
            /// that uses this is written in the lower register and drops an octave on its alternate
            /// pass rather than climbing. That keeps the knock under 3.2 kHz.
            /// </remarks>
            Mallet,
        }

        /// <summary>
        /// One background track. Everything that differs between them is data; the rendering is
        /// shared, so ten tracks cannot drift into ten different instruments.
        /// </summary>
        private readonly struct Track
        {
            /// <summary>Semitones above A4 per step, or -1 for a rest.</summary>
            public readonly int[] Figure;

            /// <summary>One chord root per bar, in Hz. The bass moves; the scale does not.</summary>
            public readonly float[] Roots;

            /// <summary>How fast a pluck dies away. Higher is shorter.</summary>
            public readonly float Ring;

            /// <summary>Semitones the figure moves on alternate passes. Signed.</summary>
            public readonly int Lift;

            /// <summary>What plays the notes.</summary>
            public readonly Voice Voice;

            /// <summary>How much of the finished buffer feeds back as reverb. Zero is none.</summary>
            public readonly float Tail;

            /// <summary>
            /// How many steps back a note is still summed: until it has fallen below -60 dB.
            /// </summary>
            /// <remarks>
            /// Derived from the ring, because a fixed number of steps cannot suit both kinds of
            /// track. It was five everywhere, which dropped the warm tracks' long notes 1.4 to 5 %
            /// of the way down, mid-cycle: a tick after the notes that the click test hears.
            /// </remarks>
            public readonly int Lookback;

            /// <summary>
            /// Each step's pitch, first pass then alternate pass, so a sample does not work out a
            /// power of two for every note it sums.
            /// </summary>
            public readonly float[] Hz;

            public Track(int[] figure, float[] roots, float ring, int lift,
                         Voice voice = Voice.Plucked, float tail = 0f)
            {
                Figure = figure;
                Roots = roots;
                Ring = ring;
                Lift = lift;
                Voice = voice;
                Tail = tail;

                // Capped at one figure. A ring slow enough to reach the cap would be cut above
                // -60 dB, and the click test would say so.
                Lookback = Mathf.Clamp(
                    Mathf.CeilToInt(Mathf.Log(1000f) / ring / StepSeconds), 1, figure.Length);

                Hz = new float[figure.Length * 2];

                for (int pass = 0; pass < 2; pass++)
                {
                    for (int s = 0; s < figure.Length; s++)
                    {
                        int semi = figure[s] + (pass == 0 ? 0 : lift);
                        Hz[pass * figure.Length + s] =
                            figure[s] == Rest ? 0f : 440f * Mathf.Pow(2f, semi / 12f);
                    }
                }
            }

            /// <summary>
            /// Clip length: one pass of the chord cycle.
            /// </summary>
            /// <remarks>
            /// Derived rather than stated, so a track cannot claim a length its chords do not fill
            /// and end half way through a bar. It also keeps the octave lift lined up: the figure is
            /// one bar long, so bars are passes, and an even number of bars leaves the lift back
            /// where it started when the clip wraps.
            /// </remarks>
            public float Seconds => Roots.Length * BarSeconds;
        }

        /// <summary>
        /// The ten tracks, in the order <see cref="MusicRules"/> cycles them.
        /// </summary>
        /// <remarks>
        /// All ten use the same five notes -- A, C, D, E, G -- and stay there. The scale has no
        /// semitone clashes, so any note lands consonantly on any chord in any of these
        /// progressions and nothing ever demands resolution, which is the whole requirement for
        /// something that repeats while somebody stares at a K-map.
        ///
        /// Seven of them root that collection on A and read as minor; three root it on C and read
        /// as major. Identical notes, different home. It is the cheapest way to put two moods in
        /// one set without the scale rule that holds the set together having to bend.
        ///
        /// They are the same shape on purpose: four bars, sixteen steps, one tempo, one key. What
        /// differs is density -- four notes to eight, out of sixteen possible -- how long a note
        /// rings, which way the figure moves on its alternate pass, which register it sits in,
        /// where the chords go, and which of three <see cref="Voice"/>s plays it. Switching between
        /// them should read as the same music continuing rather than as the game changing its mind.
        /// </remarks>
        private static readonly Track[] Tracks =
        {
            // 0. The original, unchanged. Even, mid-register, a note roughly every other step, and
            //    the figure lifts an octave on alternate passes so the second half of the loop is
            //    not the first half again.
            new Track(
                figure: new[] { 0, -1, 3, -1, 7, -1, 5, -1, -1, 10, -1, 7, -1, 3, -1, -1 },
                roots: new[] { 110.00f, 87.31f, 130.81f, 98.00f },   // Am - F - C - G
                ring: 2.6f,
                lift: 12),

            // 1. The sparse one. Five notes in sixteen steps and a longer ring, so it is mostly the
            //    sound of something decaying. It climbs, then drops an octave on the alternate pass
            //    instead of rising -- the opposite gesture to track 0 out of the same mechanism.
            //    The chords hang rather than travel: Am twice, and never further than a step away.
            new Track(
                figure: new[] { 7, -1, -1, 10, -1, -1, 12, -1, -1, -1, 15, -1, -1, 10, -1, -1 },
                roots: new[] { 110.00f, 82.41f, 110.00f, 98.00f },   // Am - Em - Am - G
                ring: 2.0f,
                lift: -12),

            // 2. The plucked one. A shorter ring, so notes are struck rather than rung, and the
            //    figure falls before it turns back -- the one shape neither of the others has. Its
            //    progression starts away from the tonic and never quite arrives.
            new Track(
                figure: new[] { 12, -1, 10, -1, 7, -1, -1, 5, -1, 3, -1, -1, 5, -1, 7, -1 },
                roots: new[] { 130.81f, 98.00f, 110.00f, 87.31f },   // C - G - Am - F
                ring: 3.0f,
                lift: 12),

            // 3. The off-beat one. Its notes fall between track 0's rather than on them, so the two
            //    sit against the bar differently despite sharing a tempo. The progression arrives at
            //    Am only at the very end, and then the loop takes it away again.
            new Track(
                figure: new[] { -1, 3, -1, -1, 5, -1, 7, -1, -1, -1, 3, -1, 0, -1, -1, -1 },
                roots: new[] { 87.31f, 130.81f, 98.00f, 110.00f },   // F - C - G - Am
                ring: 2.3f,
                lift: -12),

            // 4. The low one. Four notes, the longest ring of any of them, and written an octave
            //    below the rest, so it reads as the quietest track in the set without being mixed
            //    any quieter. The chords barely move: Am, then a step away and back, twice.
            new Track(
                figure: new[] { -12, -1, -1, -1, -5, -1, -1, -1, -2, -1, -1, -1, -5, -1, -1, -1 },
                roots: new[] { 110.00f, 98.00f, 87.31f, 98.00f },   // Am - G - F - G
                ring: 1.7f,
                lift: 12),

            // 5. The busiest, which still means eight notes in eight seconds. They come in pairs,
            //    and the ring is the shortest in the set so a pair reads as two notes rather than
            //    as a chord.
            new Track(
                figure: new[] { 0, 3, -1, -1, 7, 5, -1, -1, 10, 7, -1, -1, 3, 0, -1, -1 },
                roots: new[] { 110.00f, 130.81f, 87.31f, 98.00f },   // Am - C - F - G
                ring: 3.2f,
                lift: 12),

            // -------------------------------------------------------------------------
            // 6 to 8: the warm ones.
            //
            // Same five notes as everything above -- A, C, D, E, G -- and that is the trick.
            // A minor pentatonic and C major pentatonic are the same pitch collection; only
            // which note the bass calls home decides whether it sounds melancholy or open.
            // Rooting these on C rather than A makes them read bright without moving a single
            // note out of the scale the whole set shares, so a warm track can still follow a
            // sad one without the switch sounding like a key change.
            //
            // Their figures lean on C, E and G where the first six lean on A and D, they are the
            // sparsest in the set, and they drop an octave on the alternate pass rather than
            // climbing, so they wander downwards and never arrive anywhere.
            // -------------------------------------------------------------------------

            // 6. A C major triad, one note at a time, with nothing else in the bar. Four notes in
            //    eight seconds is the sparsest thing here by some way.
            new Track(
                figure: new[] { 3, -1, -1, -1, 7, -1, -1, -1, 10, -1, -1, -1, 7, -1, -1, -1 },
                roots: new[] { 130.81f, 87.31f, 130.81f, 98.00f },   // C - F - C - G
                ring: 1.4f,
                lift: -12,
                voice: Voice.Keys,
                tail: 0.16f),

            // 7. Rises to the octave and comes back down the same way. The chords move under a
            //    figure that mostly does not, which is what stops it reading as an exercise.
            new Track(
                figure: new[] { 7, -1, -1, 10, -1, -1, 12, -1, -1, -1, 10, -1, -1, 7, -1, -1 },
                roots: new[] { 87.31f, 130.81f, 98.00f, 130.81f },   // F - C - G - C
                ring: 1.2f,
                lift: -12,
                voice: Voice.Keys,
                tail: 0.17f),

            // 8. Falls from the octave to the root and lifts one step at the end, so the loop
            //    point is the one moment it sounds like it is going somewhere. The longest ring
            //    in the set: by the fourth note the first is still sounding.
            new Track(
                figure: new[] { 12, -1, -1, -1, 10, -1, 7, -1, -1, -1, 3, -1, -1, -1, 5, -1 },
                roots: new[] { 130.81f, 110.00f, 87.31f, 130.81f },   // C - Am - F - C
                ring: 1.6f,
                lift: -12,
                voice: Voice.Keys,
                tail: 0.15f),

            // -------------------------------------------------------------------------
            // 9: the mallets.
            //
            // The only track that differs in what plays it rather than in what is played. Same five
            // notes, same tempo, same four bars, so it crossfades with any of the others -- but a
            // struck wooden bar, short and dry, where everything else is plucked or held.
            // -------------------------------------------------------------------------

            // 9. Eight notes, two of them pairs, climbing to G and stepping back down, with a short
            //    ring so the pairs bounce rather than blur. Rooted on A, so it reads minor like the
            //    first six. Under it, D: a bass root no other track uses, which colours the same five
            //    notes without adding a sixth. Written low and dropping an octave on the alternate
            //    pass, which keeps the mallet's knock under 3.2 kHz.
            new Track(
                figure: new[] { 0, -1, 3, -1, 5, 7, -1, -1, 3, -1, 10, -1, 7, 5, -1, -1 },
                roots: new[] { 110.00f, 146.83f, 98.00f, 82.41f },   // Am - D - G - Em
                ring: 3.0f,
                lift: -12,
                voice: Voice.Mallet),
        };

        /// <summary>
        /// One sample of a track: the figure, and a bass note under it.
        /// </summary>
        /// <remarks>
        /// Notes ring for well over a step, so several sound at once. Walking back and summing is
        /// what lets them overlap instead of being cut off by the next one, and how far back is
        /// <see cref="Track.Lookback"/>: far enough that a note has decayed below -60 dB before it
        /// is dropped.
        ///
        /// **Nothing here may change value in a single sample.** A waveform that jumps puts energy
        /// at every frequency the render holds, and above the notes that is a click -- right at the
        /// edge of a 22 kHz render, where playback resampling turns it into a sizzle that comes and
        /// goes with the music. Notes start at a zero crossing and rise over about a millisecond,
        /// the bass is its own note per bar rather than one sine restarted on every bar line, and a
        /// note is only dropped once there is nothing left of it to cut.
        ///
        /// There is deliberately no noise floor. There was one -- white noise at a hundredth of full
        /// scale, "so the quiet parts are not digitally dead" -- and it was reported as a hiss.
        /// Being baked into the clip it rose and fell with the music, and in the sparse tracks,
        /// where a note decays to nothing between strikes, it was the only thing left playing.
        /// Silence between notes is what these tracks are for.
        /// </remarks>
        private static float Sample(Track track, float t, float d)
        {
            int steps = track.Figure.Length;
            int now = Mathf.FloorToInt(t / StepSeconds);

            float voice = 0f;

            for (int back = 0; back < track.Lookback; back++)
            {
                int s = now - back;

                if (s < 0)
                    break;

                int step = s % steps;

                if (track.Figure[step] == Rest)
                    continue;

                float hz = track.Hz[((s / steps) % 2) * steps + step];

                // Phase measured from the note's own start, so every note begins at a zero crossing.
                float age = t - s * StepSeconds;
                float ring = Mathf.Exp(-track.Ring * age);

                switch (track.Voice)
                {
                    case Voice.Keys:
                        voice += Keys(age, hz, ring);
                        break;

                    case Voice.Mallet:
                        voice += Mallet(age, hz, ring);
                        break;

                    default:
                        voice += Plucked(age, hz, ring);
                        break;
                }
            }

            // One bass note a bar, struck and left to fall away. Felt more than heard. The previous
            // bar's is still falling away under it: it used to be one sine whose envelope restarted
            // on every bar line, jumping from 3 % to full at whatever phase it had reached -- the
            // loudest click in the set, exactly every eight seconds. Kept one bar longer, it is
            // dropped at 0.07 %, with nothing left to cut.
            int bar = Mathf.FloorToInt(t / BarSeconds);
            float barAge = t - bar * BarSeconds;
            int roots = track.Roots.Length;

            float bass = Bass(track.Roots[bar % roots], barAge);

            if (bar > 0)
                bass += Bass(track.Roots[(bar - 1) % roots], barAge + BarSeconds);

            return (voice * 0.15f + bass * 0.16f) * Fade(t, d, 2f);
        }

        /// <summary>A bar's bass note, <paramref name="age"/> seconds after it was struck.</summary>
        /// <remarks>
        /// Its phase runs from its own start, so it begins at a zero crossing, and it rises over a
        /// few milliseconds -- well under one cycle of a note this low -- rather than in one sample.
        /// </remarks>
        private static float Bass(float root, float age) =>
            Sine(age, root * 0.5f) * Mathf.Exp(-0.45f * age) * Strike(age, 0.004f);

        /// <summary>
        /// How a struck note arrives: at full volume in about a millisecond, not in one sample.
        /// </summary>
        /// <remarks>
        /// Still an instant to the ear -- a real pluck takes longer -- but a note that jumps to full
        /// volume between two samples is a click on every note, sitting right at the top of what a
        /// 22 kHz render holds.
        ///
        /// Twenty time constants in, what is left of the rise is below 10^-8 and it stops being
        /// worked out. Every note in a track's lookback passes through here on every sample, and
        /// nearly all of them are long past their first millisecond.
        /// </remarks>
        private static float Strike(float age, float seconds = 0.0005f) =>
            age >= seconds * 20f ? 1f : 1f - Mathf.Exp(-age / seconds);

        /// <summary>
        /// Renders a waveform. <paramref name="shape"/> is given the time in seconds and the clip's
        /// duration, and returns a sample which is clamped before it is stored.
        /// </summary>
        private static AudioClip Make(string name, float seconds, Func<float, float, float> shape)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;

                // Clamped rather than normalised: a cue that clipped would be a bug in its own
                // numbers, and silently rescaling it would hide that while changing the mix.
                samples[i] = Mathf.Clamp(shape(t, seconds), -1f, 1f);
            }

            FadeEnd(samples);

            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// A short fade at the very end. Cutting a waveform mid-cycle produces an audible pop that is
        /// easy to mistake for a sound the game meant to make.
        /// </summary>
        private static void FadeEnd(float[] samples)
        {
            int count = samples.Length;
            int fade = Mathf.Min(220, count / 4);

            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                samples[count - 1 - i] *= k;
            }
        }

        /// <summary>Sine plus octave, in at full amplitude within a millisecond.</summary>
        private static float Plucked(float age, float hz, float ring) =>
            (Sine(age, hz) + Sine(age, hz * 2f) * 0.22f) * ring * Strike(age);

        /// <summary>
        /// A struck bar: the note, and a knock at four times it that is gone within half a second.
        /// </summary>
        /// <remarks>
        /// A slightly softer strike than a pluck -- a mallet is felt, not a fingernail. Past 0.7 s the
        /// knock is under 10^-4 of where it began and is left out rather than worked out.
        /// </remarks>
        private static float Mallet(float age, float hz, float ring)
        {
            float knock = age < 0.7f ? Sine(age, hz * 4f) * 0.3f * Mathf.Exp(-14f * age) : 0f;

            return (Sine(age, hz) + knock) * ring * Strike(age, 0.001f);
        }

        /// <summary>
        /// A struck key: soft rise, detuned pair, and a tine that fades faster than the note.
        /// </summary>
        /// <remarks>
        /// The detune is four cents, which is a beat every few seconds rather than a wobble -- far
        /// enough to stop the pair sounding like one oscillator, close enough not to sound out of
        /// tune. The tine is a twelfth up and dies in a fraction of the time the note does, so it
        /// is heard as the moment of the strike rather than as a note of its own.
        ///
        /// Deliberately quieter than <see cref="Plucked"/>: these tracks are reverberated
        /// afterwards, which puts the level back.
        /// </remarks>
        private static float Keys(float age, float hz, float ring)
        {
            // The rise is complete, and the tine gone, long before the note is: past those points
            // each is under 10^-5 of its start and is left out rather than worked out per sample.
            float attack = age < 0.5f ? 1f - Mathf.Exp(-26f * age) : 1f;

            float body = Sine(age, hz) + Sine(age, hz * 1.004f) * 0.75f;
            float tine = age < 1.3f ? Sine(age, hz * 3f) * 0.11f * Mathf.Exp(-9f * age) : 0f;

            return (body + tine) * attack * ring * 0.62f;
        }

        private static float Sine(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);

        private static float Triangle(float t, float hz)
        {
            float phase = (t * hz) % 1f;
            return 4f * Mathf.Abs(phase - 0.5f) - 1f;
        }

        /// <summary>Deterministic hash noise, so a clip sounds identical every run.</summary>
        private static float Noise(float t)
        {
            int seed = Mathf.RoundToInt(t * SampleRate);
            seed = (seed << 13) ^ seed;
            int n = seed * (seed * seed * 15731 + 789221) + 1376312589;
            return 1f - (n & 0x7fffffff) / 1073741824f;
        }

        /// <summary>
        /// Eases in at the start and out at the end, so a looping clip has no seam.
        /// </summary>
        /// <remarks>
        /// A loop that starts and ends at different amplitudes clicks once per repetition, and a
        /// click every sixteen seconds is far more irritating than the music is pleasant.
        /// </remarks>
        private static float Fade(float t, float duration, float seconds)
        {
            if (seconds <= 0f || duration <= 0f)
                return 1f;

            float rise = Mathf.Clamp01(t / seconds);
            float fall = Mathf.Clamp01((duration - t) / seconds);

            return Mathf.Min(rise, fall);
        }

        private static float Decay(float t, float duration, float rate)
        {
            if (duration <= 0f)
                return 0f;

            return Mathf.Exp(-rate * (t / duration));
        }
    }
}
