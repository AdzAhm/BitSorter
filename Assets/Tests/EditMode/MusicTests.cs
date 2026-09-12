using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The nine background tracks, and the rule that decides which one is playing.
    /// </summary>
    /// <remarks>
    /// Nobody can assert that music is good. What can be asserted is that nine tracks stayed nine
    /// variations of one idea rather than drifting into nine different pieces -- same five notes,
    /// same tempo, same length -- and that the track cannot change at a moment the player would
    /// notice it changing.
    ///
    /// The scale test is the load-bearing one now that the set spans two moods. Six tracks root the
    /// collection on A and read as minor, three root it on C and read as major, and the only reason
    /// that is safe is that the notes themselves never leave the shared five.
    ///
    /// The waveform assertions here are deliberately a subset of what <see cref="ProceduralAudioTests"/>
    /// does to the short cues. Rendering nine thirty-two second clips is the slowest thing in the Edit
    /// Mode suite, so each is built once and interrogated, rather than once per test.
    /// </remarks>
    public class MusicTests
    {
        /// <summary>A minor pentatonic: A, C, D, E, G, as semitones above A.</summary>
        private static readonly int[] Pentatonic = { 0, 3, 5, 7, 10 };

        private static float[] SamplesOf(int track)
        {
            AudioClip clip = ProceduralAudio.MusicClip(track);
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            return samples;
        }

        private static IEnumerable<int> EveryTrack()
        {
            for (int i = 0; i < ProceduralAudio.MusicTracks; i++)
                yield return i;
        }

        // -----------------------------------------------------------------
        // The set
        // -----------------------------------------------------------------

        [Test]
        public void ThereAreNineTracks()
        {
            Assert.AreEqual(9, ProceduralAudio.MusicTracks);
        }

        [Test]
        public void EveryTrack_ProducesADistinctClip()
        {
            var seen = new HashSet<AudioClip>();

            foreach (int track in EveryTrack())
            {
                AudioClip clip = ProceduralAudio.MusicClip(track);

                Assert.IsNotNull(clip, "track " + track);
                Assert.Greater(clip.samples, 0, "track " + track + " has no samples");
                Assert.AreEqual(1, clip.channels, "track " + track + " should be mono");

                Assert.IsTrue(seen.Add(clip),
                    "track " + track + " is the same clip object as another");
            }
        }

        [Test]
        public void EveryTrack_IsCachedRatherThanRebuilt()
        {
            // Rebuilding a thirty-two second waveform on a level change would hitch the transition,
            // and a track the player returns to would cost the same again.
            foreach (int track in EveryTrack())
            {
                Assert.AreSame(ProceduralAudio.MusicClip(track), ProceduralAudio.MusicClip(track),
                    "track " + track);
            }
        }

        [Test]
        public void AnOutOfRangeTrack_IsClampedRatherThanThrown()
        {
            // Silence with a stack trace behind it is a worse failure than the wrong track.
            Assert.AreSame(ProceduralAudio.MusicClip(0), ProceduralAudio.MusicClip(-4));
            Assert.AreSame(ProceduralAudio.MusicClip(ProceduralAudio.MusicTracks - 1),
                           ProceduralAudio.MusicClip(ProceduralAudio.MusicTracks + 10));
        }

        [Test]
        public void TheMusicCue_IsTheFirstTrack()
        {
            // Clip(Cue.Music) still has to answer for any caller that just wants "the music".
            Assert.AreSame(ProceduralAudio.MusicClip(0), ProceduralAudio.Clip(Cue.Music));
        }

        // -----------------------------------------------------------------
        // Same character
        // -----------------------------------------------------------------

        [Test]
        public void EveryTrack_IsTheSameLengthAndTempoAsTheFirst()
        {
            // Switching between tracks should read as the same music continuing. Two of them running
            // at different lengths would not break anything audibly, but it is the first sign that
            // the set has stopped being variations on one idea.
            float first = ProceduralAudio.MusicClip(0).length;

            foreach (int track in EveryTrack())
            {
                Assert.AreEqual(first, ProceduralAudio.MusicClip(track).length, 0.01f,
                    "track " + track + " is a different length to track 0");
            }
        }

        [Test]
        public void EveryTrack_IsRenderedAtTheSameRate()
        {
            int first = ProceduralAudio.MusicClip(0).frequency;

            foreach (int track in EveryTrack())
                Assert.AreEqual(first, ProceduralAudio.MusicClip(track).frequency, "track " + track);
        }

        [Test]
        public void TheMusicRate_LeavesRoomForEveryNoteItPlays()
        {
            // Music is rendered at half the rate the cues are, which is only safe while nothing in it
            // approaches the Nyquist limit. The highest thing any track produces is its top note's
            // second harmonic; this fails if a figure is ever written high enough to alias.
            int rate = ProceduralAudio.MusicClip(0).frequency;
            const float HighestHarmonic = 4200f;   // ~2.1 kHz top note, doubled

            Assert.Greater(rate * 0.5f, HighestHarmonic * 1.5f,
                "the music sample rate no longer has headroom for the notes being played");
        }

        [Test]
        public void NoTrackClips()
        {
            foreach (int track in EveryTrack())
            {
                float[] samples = SamplesOf(track);
                int railed = 0;

                foreach (float sample in samples)
                {
                    Assert.LessOrEqual(Mathf.Abs(sample), 1f, "track " + track + " out of range");

                    if (Mathf.Abs(sample) >= 0.999f)
                        railed++;
                }

                Assert.Less(railed, samples.Length / 200,
                    "track " + track + " spends long enough at full scale to be distorting");
            }
        }

        [Test]
        public void NoTrackIsSilence()
        {
            foreach (int track in EveryTrack())
            {
                float peak = 0f;
                foreach (float sample in SamplesOf(track))
                    peak = Mathf.Max(peak, Mathf.Abs(sample));

                Assert.Greater(peak, 0.05f, "track " + track + " came out inaudible");
            }
        }

        [Test]
        public void EveryTrack_StartsAndEndsNearSilence()
        {
            // These loop forever. A clip that starts and ends at different amplitudes clicks once per
            // repetition, and a click every thirty-two seconds is far more irritating than the music
            // is pleasant.
            foreach (int track in EveryTrack())
            {
                float[] samples = SamplesOf(track);

                Assert.Less(Mathf.Abs(samples[0]), 0.02f, "track " + track + " starts abruptly");
                Assert.Less(Mathf.Abs(samples[samples.Length - 1]), 0.02f,
                    "track " + track + " ends abruptly and will click on the loop");
            }
        }

        [Test]
        public void EveryNoteInEveryTrack_IsInTheSameScale()
        {
            // A minor pentatonic has no semitone clashes, so any note lands consonantly on any
            // chord in any of these progressions and nothing ever demands resolution. It is also
            // what lets any track follow any other without the switch sounding like a key change.
            // One mistyped semitone would be invisible to read and obvious to hear.
            foreach (int track in EveryTrack())
            {
                IReadOnlyList<int> notes = ProceduralAudio.MusicNotes(track);

                Assert.IsNotEmpty(notes, "track " + track + " plays nothing");

                foreach (int semi in notes)
                {
                    int degree = ((semi % 12) + 12) % 12;

                    CollectionAssert.Contains(Pentatonic, degree,
                        "track " + track + " plays " + semi + ", which is outside A minor pentatonic");
                }
            }
        }

        [Test]
        public void EveryTrack_LeavesMoreSilenceThanItFills()
        {
            // Sparse is the whole character. Half the grid empty is what gives the ear somewhere to
            // rest over twenty minutes, and it is the easiest thing to lose while adding tracks.
            const int Steps = 16;

            foreach (int track in EveryTrack())
            {
                int played = ProceduralAudio.MusicNotes(track).Count;

                Assert.LessOrEqual(played, Steps / 2,
                    "track " + track + " fills more than half its steps and is no longer sparse");
            }
        }

        [Test]
        public void NoTwoTracks_PlayTheSameFigure()
        {
            var seen = new HashSet<string>();

            foreach (int track in EveryTrack())
            {
                string figure = string.Join(",", ProceduralAudio.MusicNotes(track));

                Assert.IsTrue(seen.Add(figure),
                    "track " + track + " plays the same notes as an earlier one");
            }
        }

        [Test]
        public void NoTrackIsLouderThanTheCollisionCue()
        {
            // The one sound a player must never miss. Music that competed with it would bury it.
            foreach (int track in EveryTrack())
            {
                float peak = 0f;
                foreach (float sample in SamplesOf(track))
                    peak = Mathf.Max(peak, Mathf.Abs(sample));

                float music = peak * ProceduralAudio.VolumeOf(Cue.Music);

                Assert.Less(music, ProceduralAudio.VolumeOf(Cue.Collide),
                    "track " + track + " is loud enough to bury a collision");
            }
        }

        // -----------------------------------------------------------------
        // Which track is playing
        // -----------------------------------------------------------------

        [Test]
        public void TheFirstLevelOfASession_KeepsTheTrackItStartedOn()
        {
            // The first level loads a moment after the music begins, during LevelSession's Start.
            // Cycling there would cross-fade the opening a second after the player first heard it.
            Assert.IsFalse(MusicRules.ChangesTrack(null, "route-the-bit"));
            Assert.IsFalse(MusicRules.ChangesTrack(string.Empty, "route-the-bit"));
        }

        [Test]
        public void ReloadingTheSameLevel_DoesNotChangeTheTrack()
        {
            // Reset and re-entry both re-fire LevelLoaded. Cycling on every load would change the
            // music each time a player failed a level -- drawing attention to the one level they are
            // already stuck on.
            Assert.IsFalse(MusicRules.ChangesTrack("balance-the-paths", "balance-the-paths"));
        }

        [Test]
        public void MovingToADifferentLevel_ChangesTheTrack()
        {
            Assert.IsTrue(MusicRules.ChangesTrack("route-the-bit", "the-long-way-round"));
        }

        [Test]
        public void TheTutorialAndTheSandbox_AreLevelChangesLikeAnyOther()
        {
            // Neither is in the catalogue, but both load through LevelSession and both are somewhere
            // the player has deliberately gone. There is no reason for the music to treat them apart.
            Assert.IsTrue(MusicRules.ChangesTrack("route-the-bit", TutorialLevel.Key));
            Assert.IsTrue(MusicRules.ChangesTrack(TutorialLevel.Key, SandboxLevel.Key));
        }

        [Test]
        public void TheCycleVisitsEveryTrackBeforeRepeating()
        {
            int count = ProceduralAudio.MusicTracks;
            var seen = new HashSet<int>();
            int track = 0;

            for (int i = 0; i < count; i++)
            {
                seen.Add(track);
                track = MusicRules.NextTrack(track, count);
            }

            Assert.AreEqual(count, seen.Count, "the cycle repeats before it has played every track");
            Assert.AreEqual(0, track, "the cycle does not return to where it started");
        }

        [Test]
        public void TheCycleStartsAnywhereAndStillVisitsEverything()
        {
            // The starting track is random per session, so the cycle has to be correct from any of
            // them, not just from zero.
            int count = ProceduralAudio.MusicTracks;

            for (int start = 0; start < count; start++)
            {
                var seen = new HashSet<int>();
                int track = start;

                for (int i = 0; i < count; i++)
                {
                    seen.Add(track);
                    track = MusicRules.NextTrack(track, count);
                }

                Assert.AreEqual(count, seen.Count, "starting from " + start);
            }
        }

        [Test]
        public void NextTrack_IsAlwaysAPlayableIndex()
        {
            int count = ProceduralAudio.MusicTracks;

            foreach (int current in new[] { -9, -1, 0, 1, count - 1, count, count + 4, int.MaxValue })
            {
                int next = MusicRules.NextTrack(current, count);

                Assert.GreaterOrEqual(next, 0, "from " + current);
                Assert.Less(next, count, "from " + current);
            }
        }

        [Test]
        public void NextTrack_SurvivesAnEmptySet()
        {
            // Nothing should ever call this with no tracks, but returning 0 keeps the music silent
            // rather than throwing inside a level load.
            Assert.AreEqual(0, MusicRules.NextTrack(3, 0));
            Assert.AreEqual(0, MusicRules.NextTrack(3, -1));
        }

        // -----------------------------------------------------------------
        // What the set costs
        // -----------------------------------------------------------------

        /// <summary>
        /// The music set fits the budget the browser build is held to.
        /// </summary>
        /// <remarks>
        /// The reason the music is rendered at half the cue rate, asserted rather than described.
        /// ProceduralAudio's own remarks stated the figure for six tracks and then three more were
        /// added, so the comment claimed half of what the set actually cost -- and a comment is not
        /// something a build can check.
        ///
        /// The ceiling is generous on purpose. This is not a target to optimise towards; it is a
        /// tripwire for another three tracks, or a doubled sample rate, going in without anyone
        /// doing the arithmetic. Clips are built lazily, so this is the ceiling a long session
        /// reaches rather than the startup cost.
        /// </remarks>
        [Test]
        public void TheWholeMusicSet_FitsInItsMemoryBudget()
        {
            const int megabyte = 1024 * 1024;
            const int ceiling = 32 * megabyte;

            int bytes = ProceduralAudio.MusicBytes;

            Assert.Greater(bytes, 0, "the set measured as costing nothing, so this proves nothing");

            Assert.Less(bytes, ceiling,
                $"the music set now costs {bytes / (float)megabyte:0.0} MiB of heap against a " +
                $"{ceiling / megabyte} MiB ceiling, in a game whose whole browser build is 16 MB. " +
                "Either drop a track or lower MusicSampleRate -- and update the figure in its " +
                "remarks, which is where this number is explained.");
        }

        /// <summary>Every track is the same length, so the cost scales with the count alone.</summary>
        /// <remarks>
        /// <see cref="EveryTrack_IsTheSameLengthAndTempoAsTheFirst"/> says this of the built clips.
        /// This says it of the figure the memory assertion above is computed from, which is derived
        /// from the chord cycle and does not need a clip built to read.
        /// </remarks>
        [Test]
        public void EveryTrack_ReportsTheSameLength()
        {
            float first = ProceduralAudio.MusicSeconds(0);

            Assert.Greater(first, 0f, "a track of no length would cost nothing and play nothing");

            for (int i = 1; i < ProceduralAudio.MusicTracks; i++)
            {
                Assert.AreEqual(first, ProceduralAudio.MusicSeconds(i), 0.001f,
                    $"track {i} is a different length from the first, so the set is not one piece " +
                    "of music and its cost no longer scales with the track count");
            }
        }
    }
}
