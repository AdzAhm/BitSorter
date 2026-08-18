using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The generated cues: that each one exists, is audible, does not clip, and is not silence.
    /// </summary>
    /// <remarks>
    /// Nobody can assert that a sound is *good* in a test. What can be asserted is the set of ways a
    /// synthesised clip fails without anyone noticing until they hear it: samples out of range,
    /// a waveform that came out silent, a clip that ends mid-cycle and pops, or two cues that are
    /// accidentally the same sound.
    /// </remarks>
    public class ProceduralAudioTests
    {
        private static readonly Cue[] EveryCue =
        {
            Cue.Tick, Cue.Gate, Cue.Land, Cue.Collide, Cue.Win,
        };

        private static float[] SamplesOf(Cue cue)
        {
            AudioClip clip = ProceduralAudio.Clip(cue);
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            return samples;
        }

        [Test]
        public void EveryCue_ProducesAClip()
        {
            foreach (Cue cue in EveryCue)
            {
                AudioClip clip = ProceduralAudio.Clip(cue);

                Assert.IsNotNull(clip, cue.ToString());
                Assert.Greater(clip.samples, 0, $"{cue} has no samples");
                Assert.AreEqual(1, clip.channels, $"{cue} should be mono");
                Assert.AreEqual(44100, clip.frequency, $"{cue} sample rate");
            }
        }

        [Test]
        public void EveryCue_IsCachedRatherThanRebuilt()
        {
            // Rebuilding a waveform on every play would allocate an AudioClip several times a second
            // during a run, which is exactly when the game can least afford it.
            foreach (Cue cue in EveryCue)
                Assert.AreSame(ProceduralAudio.Clip(cue), ProceduralAudio.Clip(cue), cue.ToString());
        }

        [Test]
        public void NoCueClips()
        {
            // Samples are clamped rather than normalised on purpose, so anything reaching the rail is
            // a cue whose own numbers are wrong -- and it would be heard as distortion.
            foreach (Cue cue in EveryCue)
            {
                float[] samples = SamplesOf(cue);
                int railed = 0;

                foreach (float sample in samples)
                {
                    Assert.LessOrEqual(Mathf.Abs(sample), 1f, $"{cue} sample out of range");

                    if (Mathf.Abs(sample) >= 0.999f)
                        railed++;
                }

                Assert.Less(railed, samples.Length / 50,
                    $"{cue} spends too long at full scale to be anything but distorted");
            }
        }

        [Test]
        public void NoCueIsSilence()
        {
            foreach (Cue cue in EveryCue)
            {
                float peak = 0f;
                foreach (float sample in SamplesOf(cue))
                    peak = Mathf.Max(peak, Mathf.Abs(sample));

                Assert.Greater(peak, 0.05f, $"{cue} came out inaudible");
            }
        }

        [Test]
        public void EveryCue_FadesOutRatherThanBeingCutOff()
        {
            // A waveform chopped mid-cycle pops, and a pop is easy to mistake for a sound the game
            // meant to make -- particularly bad here, where one of the cues is a collision.
            foreach (Cue cue in EveryCue)
            {
                float[] samples = SamplesOf(cue);

                Assert.Less(Mathf.Abs(samples[samples.Length - 1]), 0.02f,
                    $"{cue} ends abruptly and will click");
            }
        }

        [Test]
        public void TheFrequentCuesAreTheQuietOnes()
        {
            // Tick and Gate fire many times a second; they are texture. Collide and Win are the two
            // events a player must never miss. If that ordering ever inverts, the game becomes
            // exhausting to listen to and the important cues vanish underneath the clock.
            Assert.Less(ProceduralAudio.VolumeOf(Cue.Tick), ProceduralAudio.VolumeOf(Cue.Land));
            Assert.Less(ProceduralAudio.VolumeOf(Cue.Gate), ProceduralAudio.VolumeOf(Cue.Land));

            Assert.Greater(ProceduralAudio.VolumeOf(Cue.Collide), ProceduralAudio.VolumeOf(Cue.Land));
            Assert.Greater(ProceduralAudio.VolumeOf(Cue.Win), ProceduralAudio.VolumeOf(Cue.Gate));
        }

        [Test]
        public void TheClockCueIsShortEnoughNotToOverlapItself()
        {
            // The runner's default is two ticks a second. A tick cue longer than that would layer on
            // itself into a drone rather than reading as a clock.
            AudioClip tick = ProceduralAudio.Clip(Cue.Tick);

            Assert.Less(tick.length, 0.4f, "the tick cue would overlap itself at the default speed");
        }

        [Test]
        public void CollisionSoundsNothingLikeTheOthers()
        {
            // The one cue that must never be mistaken for part of the rhythm. Noise has a far higher
            // rate of sign changes than any tone, which is a crude but reliable way to say "this is
            // not a note" without asserting anything about how it sounds.
            Assert.Greater(ZeroCrossingRate(Cue.Collide), ZeroCrossingRate(Cue.Land) * 2f,
                "the collision cue should be noisy, not tonal");
        }

        private static float ZeroCrossingRate(Cue cue)
        {
            float[] samples = SamplesOf(cue);
            int crossings = 0;

            for (int i = 1; i < samples.Length; i++)
            {
                if ((samples[i - 1] < 0f) != (samples[i] < 0f))
                    crossings++;
            }

            return (float)crossings / samples.Length;
        }
    }
}
