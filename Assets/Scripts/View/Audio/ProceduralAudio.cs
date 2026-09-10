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
            // Music is not one clip but a set of them, with its own cache. Clip keeps working for
            // any caller that just wants "the music" and hands back the first track.
            if (cue == Cue.Music)
                return MusicClip(0);

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

                // Music never reaches here: Clip routes it to MusicClip before Build is called.
                default:
                    return Make("silence", 0.01f, (t, d) => 0f);
            }
        }

        // -----------------------------------------------------------------
        // The background tracks
        // -----------------------------------------------------------------

        /// <summary>How many background tracks exist. <see cref="MusicRules"/> decides the order.</summary>
        public static int MusicTracks => Tracks.Length;

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
        /// One background track, built on first use and kept.
        /// </summary>
        /// <remarks>
        /// Lazily, and per track: a session that never leaves the first level pays for one clip
        /// rather than three. An out-of-range index is clamped rather than thrown on, because the
        /// failure mode of a throw here is silence with a stack trace behind it.
        /// </remarks>
        public static AudioClip MusicClip(int index)
        {
            index = Mathf.Clamp(index, 0, Tracks.Length - 1);

            if (MusicCache.TryGetValue(index, out AudioClip cached) && cached != null)
                return cached;

            Track track = Tracks[index];
            AudioClip clip = Make("music" + index, track.Seconds,
                                  (t, d) => Sample(track, t, d), MusicSampleRate);

            MusicCache[index] = clip;
            return clip;
        }

        private static readonly Dictionary<int, AudioClip> MusicCache = new Dictionary<int, AudioClip>();

        /// <summary>Seconds per bar: one chord, and one pass of the figure.</summary>
        private const float BarSeconds = 8f;

        /// <summary>Seconds per step of the figure.</summary>
        private const float StepSeconds = 0.5f;

        /// <summary>
        /// Music is rendered at half the rate the cues are.
        /// </summary>
        /// <remarks>
        /// Nothing in these tracks comes near the Nyquist limit this leaves. The highest note any
        /// figure reaches is about 2 kHz, and its one harmonic sits at 4 kHz against a ceiling of
        /// 11 kHz. What it buys is memory: these clips are held as uncompressed floats and they are
        /// long, so at the cue rate six of them would cost 34 MB of heap in a game whose entire
        /// browser build is 16 MB. At this rate the whole set costs half that, and a session only
        /// pays for the tracks it actually reaches.
        /// </remarks>
        private const int MusicSampleRate = 22050;

        /// <summary>
        /// One background track. Everything that differs between them is data; the rendering is
        /// shared, so three tracks cannot drift into three different instruments.
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

            public Track(int[] figure, float[] roots, float ring, int lift)
            {
                Figure = figure;
                Roots = roots;
                Ring = ring;
                Lift = lift;
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
        /// The six tracks, in the order <see cref="MusicRules"/> cycles them.
        /// </summary>
        /// <remarks>
        /// All three are A minor pentatonic -- A, C, D, E, G -- and stay there. The scale has no
        /// semitone clashes, so any note lands consonantly on any chord in any of these
        /// progressions and nothing ever demands resolution, which is the whole requirement for
        /// something that repeats while somebody stares at a K-map.
        ///
        /// They are the same shape on purpose: four bars, sixteen steps, one instrument, one tempo,
        /// one key. What differs is density -- four notes to eight, out of sixteen possible -- how
        /// long a note rings, which way the figure moves on its alternate pass, which register it
        /// sits in, and where the chords go. Switching between them should read as the same music
        /// continuing rather than as the game changing its mind.
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
        };

        /// <summary>
        /// One sample of a track: the plucked figure, a bass note under it, and a trace of air.
        /// </summary>
        /// <remarks>
        /// Notes ring for well over a step, so several sound at once. Walking back a few steps and
        /// summing is what lets them overlap instead of being cut off by the next one -- five is far
        /// enough back that the oldest is inaudible at every ring rate here.
        /// </remarks>
        private static float Sample(Track track, float t, float d)
        {
            int steps = track.Figure.Length;
            int now = Mathf.FloorToInt(t / StepSeconds);
            int bar = Mathf.FloorToInt(t / BarSeconds) % track.Roots.Length;

            float voice = 0f;

            for (int back = 0; back < 5; back++)
            {
                int s = now - back;

                if (s < 0)
                    continue;

                int semi = track.Figure[s % steps];

                if (semi == Rest)
                    continue;

                int lift = (s / steps) % 2 == 0 ? 0 : track.Lift;
                float age = t - s * StepSeconds;

                // Phase measured from the note's own start, so every pluck begins at a zero crossing
                // and no note starts with a click.
                float hz = 440f * Mathf.Pow(2f, (semi + lift) / 12f);
                float ring = Mathf.Exp(-track.Ring * age);

                voice += (Sine(age, hz) + Sine(age, hz * 2f) * 0.22f) * ring;
            }

            // One bass note a bar, struck and left to fall away. Felt more than heard.
            float barAge = t % BarSeconds;
            float bass = Sine(t, track.Roots[bar] * 0.5f) * Mathf.Exp(-0.45f * barAge);

            // A trace of hiss, so the quiet parts are not digitally dead.
            float air = Noise(t) * 0.010f;

            return (voice * 0.15f + bass * 0.16f + air) * Fade(t, d, 2f);
        }

        /// <summary>
        /// Renders a waveform. <paramref name="shape"/> is given the time in seconds and the clip's
        /// duration, and returns a sample which is clamped before it is stored.
        /// </summary>
        private static AudioClip Make(string name, float seconds, Func<float, float, float> shape,
                                      int rate = SampleRate)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * rate));
            var samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / rate;

                // Clamped rather than normalised: a cue that clipped would be a bug in its own
                // numbers, and silently rescaling it would hide that while changing the mix.
                samples[i] = Mathf.Clamp(shape(t, seconds), -1f, 1f);
            }

            // A short fade at the very end. Cutting a waveform mid-cycle produces an audible pop that
            // is easy to mistake for a sound the game meant to make.
            int fade = Mathf.Min(220, count / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                samples[count - 1 - i] *= k;
            }

            AudioClip clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
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
