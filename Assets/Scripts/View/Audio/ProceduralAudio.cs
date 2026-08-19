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

                // The previous take was a continuous pad, and the honest reading of why it was
                // disliked is that a drone is the wrong answer rather than a slightly wrong drone.
                // A drone has no shape: there is no phrase to follow, so it becomes pressure on the
                // ear within a minute and there is nothing to do about it but switch it off.
                //
                // This is plucked and sparse instead. Notes are struck and allowed to ring out, and
                // roughly half the grid of steps is silence, so the ear gets somewhere to rest. It
                // has to survive being heard for twenty minutes while someone stares at a K-map,
                // which means the loudest thing in it should still be quieter than thinking.
                //
                // A minor pentatonic throughout. It has no semitone clashes, so a note landing on
                // any chord in the progression is consonant and nothing ever demands resolution --
                // exactly the quality wanted for something that repeats forever.
                //
                // Thirty-two seconds: four eight-second bars, and the figure lifts an octave on
                // alternate passes so the second half of the loop is not the first half again.
                case Cue.Music:
                    return Make("music", 32f, (t, d) =>
                    {
                        const float Step = 0.5f;

                        // Semitones above A, or -1 for a rest. Ten of sixteen steps are silent.
                        int[] figure = { 0, -1, 3, -1, 7, -1, 5, -1, -1, 10, -1, 7, -1, 3, -1, -1 };

                        // Am - F - C - G. The bass moves, the scale does not.
                        float[] roots = { 110.00f, 87.31f, 130.81f, 98.00f };

                        int bar = Mathf.FloorToInt(t / 8f) % roots.Length;
                        int now = Mathf.FloorToInt(t / Step);

                        // Notes ring for well over a step, so several are sounding at once. Walking
                        // back a few steps and summing is what lets them overlap instead of being
                        // cut off by the next one.
                        float voice = 0f;

                        for (int back = 0; back < 5; back++)
                        {
                            int s = now - back;

                            if (s < 0)
                                continue;

                            int semi = figure[s % 16];

                            if (semi < 0)
                                continue;

                            int lift = (s / 16) % 2 == 0 ? 0 : 12;
                            float age = t - s * Step;

                            // Phase measured from the note's own start, so every pluck begins at
                            // zero crossing and no note starts with a click.
                            float hz = 440f * Mathf.Pow(2f, (semi + lift) / 12f);
                            float ring = Mathf.Exp(-2.6f * age);

                            voice += (Sine(age, hz) + Sine(age, hz * 2f) * 0.22f) * ring;
                        }

                        // One bass note a bar, struck and left to fall away. Felt more than heard.
                        float barAge = t % 8f;
                        float bass = Sine(t, roots[bar] * 0.5f) * Mathf.Exp(-0.45f * barAge);

                        // A trace of hiss, so the quiet parts are not digitally dead.
                        float air = Noise(t) * 0.010f;

                        return (voice * 0.15f + bass * 0.16f + air) * Fade(t, d, 2f);
                    });

                default:
                    return Make("silence", 0.01f, (t, d) => 0f);
            }
        }

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

            // A short fade at the very end. Cutting a waveform mid-cycle produces an audible pop that
            // is easy to mistake for a sound the game meant to make.
            int fade = Mathf.Min(220, count / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                samples[count - 1 - i] *= k;
            }

            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
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
