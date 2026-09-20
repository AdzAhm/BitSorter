using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The background tracks, and the rules that decide which one is playing.
    /// </summary>
    /// <remarks>
    /// Nobody can assert that music is good. What can be asserted is that every track stayed a
    /// variation of one idea rather than drifting into a different piece -- same five notes, same
    /// tempo, same length -- that nothing is under them or between them that should not be, and
    /// that the track cannot change at a moment the player would notice it changing.
    ///
    /// The scale test is the load-bearing one now that the set spans many moods. Most tracks root
    /// the collection on A and read as minor, others on C, F or B-flat and read as major or
    /// floating, and the only reason that is safe is that the notes themselves -- chord tones
    /// included -- never leave the shared five.
    ///
    /// The hiss and click tests are the ones a listener would care about. Both look above 8 kHz,
    /// where no note reaches, so anything found there is a fault rather than a note.
    ///
    /// The waveform assertions here are deliberately a subset of what <see cref="ProceduralAudioTests"/>
    /// does to the short cues. Rendering every thirty-two second track is the slowest thing in the Edit
    /// Mode suite, so each is built once and interrogated, rather than once per test.
    /// </remarks>
    public class MusicTests
    {
        /// <summary>A minor pentatonic: A, C, D, E, G, as semitones above A.</summary>
        private static readonly int[] Pentatonic = { 0, 3, 5, 7, 10 };

        private static readonly Dictionary<int, float[]> Rendered = new Dictionary<int, float[]>();

        /// <summary>
        /// A track's samples, rendered once for the whole fixture. Callers must not write to them.
        /// </summary>
        /// <remarks>
        /// ProceduralAudio caches nothing any more -- what stays resident is GameAudio's decision --
        /// so without this every test would render every track again, and rendering them is the
        /// slowest thing in the Edit Mode suite.
        /// </remarks>
        private static float[] SamplesOf(int track)
        {
            if (!Rendered.TryGetValue(track, out float[] samples))
            {
                samples = ProceduralAudio.MusicSamples(track);
                Rendered[track] = samples;
            }

            return samples;
        }

        [OneTimeTearDown]
        public void ReleaseTheRenders() => Rendered.Clear();

        private static IEnumerable<int> EveryTrack()
        {
            for (int i = 0; i < ProceduralAudio.MusicTracks; i++)
                yield return i;
        }

        /// <summary>
        /// What is left of a track above 8 kHz.
        /// </summary>
        /// <remarks>
        /// Every note any track plays, harmonics included, sits below about 4 kHz. So nothing the
        /// music means to play gets through this, and what does get through is what should not be
        /// there: a noise floor, or the click of a waveform cut off mid-cycle. Both are exactly the
        /// kind of fault that is plain on speakers and invisible to read in the code.
        ///
        /// An eighth-order Butterworth high-pass, as four biquads, steep enough that a 4 kHz partial
        /// is down by more than 48 dB and cannot pass for noise.
        /// </remarks>
        private static float[] AboveTheNotes(int track)
        {
            // A copy: the filter works in place, and the render is shared across the fixture.
            float[] signal = (float[])SamplesOf(track).Clone();

            // The pole Qs of an eighth-order Butterworth: 1 / (2 cos((2k - 1) pi / 16)).
            foreach (double q in new[] { 0.5098, 0.6013, 0.9000, 2.5629 })
            {
                double w0 = 2.0 * System.Math.PI * 8000.0 / ProceduralAudio.MusicSampleRate;
                double cos = System.Math.Cos(w0);
                double alpha = System.Math.Sin(w0) / (2.0 * q);
                double a0 = 1.0 + alpha;

                double b0 = (1.0 + cos) / 2.0 / a0, b1 = -(1.0 + cos) / a0, b2 = b0;
                double a1 = -2.0 * cos / a0, a2 = (1.0 - alpha) / a0;

                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;

                for (int n = 0; n < signal.Length; n++)
                {
                    double x = signal[n];
                    double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;

                    x2 = x1; x1 = x;
                    y2 = y1; y1 = y;

                    signal[n] = (float)y;
                }
            }

            return signal;
        }

        // -----------------------------------------------------------------
        // The set
        // -----------------------------------------------------------------

        /// <summary>
        /// How many tracks there are, stated so a track added or removed is a deliberate act.
        /// </summary>
        /// <remarks>
        /// Was twenty-four. Four of the plucked ones went: six tracks on one voice, built before
        /// the set had any others, read as the same piece coming round again. What is left of that
        /// voice is the three with different gestures -- the original, the sparse one that drops an
        /// octave, and the busiest.
        /// </remarks>
        [Test]
        public void ThereAreTwentyTracks()
        {
            Assert.AreEqual(20, ProceduralAudio.MusicTracks);
        }

        [Test]
        public void ATrack_BuildsAMonoClipOfItsWholeLength()
        {
            // The first and last only: every track shares the builder, and the per-track tests below
            // read the samples the builder would hand to the clip.
            foreach (int track in new[] { 0, ProceduralAudio.MusicTracks - 1 })
            {
                AudioClip clip = ProceduralAudio.MusicClip(track);

                try
                {
                    Assert.IsNotNull(clip, "track " + track);
                    Assert.AreEqual(1, clip.channels, "track " + track + " should be mono");
                    Assert.AreEqual(ProceduralAudio.MusicSampleRate, clip.frequency, "track " + track);
                    Assert.AreEqual(SamplesOf(track).Length, clip.samples, "track " + track);
                }
                finally
                {
                    Object.DestroyImmediate(clip);
                }
            }
        }

        /// <summary>
        /// Every clip handed out is a new one, which its caller owns and frees.
        /// </summary>
        /// <remarks>
        /// Tracks used to be cached for the session, and at twice the number of tracks that is more
        /// heap than the browser build can spare. What stays resident is GameAudio's decision now, and
        /// it can only make one if nothing here is holding clips behind its back.
        /// </remarks>
        [Test]
        public void EveryClip_IsNewAndBelongsToItsCaller()
        {
            ProceduralAudio.MusicBake bake = ProceduralAudio.BakeMusic(0);
            bake.Step(int.MaxValue);

            AudioClip first = bake.ToClip();
            AudioClip second = bake.ToClip();

            try
            {
                Assert.AreNotSame(first, second);

                Object.DestroyImmediate(first);
                Assert.IsTrue(second != null, "freeing one clip freed another");
            }
            finally
            {
                if (first != null) Object.DestroyImmediate(first);
                if (second != null) Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void AnOutOfRangeTrack_IsClampedRatherThanThrown()
        {
            // Silence with a stack trace behind it is a worse failure than the wrong track.
            Assert.AreEqual(0, ProceduralAudio.BakeMusic(-4).Index);
            Assert.AreEqual(ProceduralAudio.MusicTracks - 1,
                            ProceduralAudio.BakeMusic(ProceduralAudio.MusicTracks + 10).Index);
        }

        [Test]
        public void AskingForTheMusicAsACue_IsRefused()
        {
            // Music is a set of tracks with owners, not one cached clip. A cue-style "the music"
            // would be a track held for the whole session by nobody.
            Assert.Throws<System.ArgumentException>(() => ProceduralAudio.Clip(Cue.Music));
        }

        // -----------------------------------------------------------------
        // Baking a slice at a time
        // -----------------------------------------------------------------

        /// <summary>
        /// A track baked a slice at a time is the same track, to the last bit.
        /// </summary>
        /// <remarks>
        /// The game bakes the next track a little each frame so a level change never stalls on it.
        /// That is only safe if where the slices fall changes nothing -- the reverb reads samples
        /// already written, and a slice boundary in the wrong place would read one not yet there.
        /// Track 7 is one of the reverberated ones; the slice sizes are deliberately awkward, one of
        /// them shorter than the reverb's shortest delay.
        /// </remarks>
        [Test]
        public void ASlicedBake_IsTheSameAsOneInOneGo()
        {
            foreach (int track in new[] { 0, 7 })
            {
                ProceduralAudio.MusicBake bake = ProceduralAudio.BakeMusic(track);
                int[] slices = { 1, 7, 811, 4093, 22050 };
                int s = 0;

                while (!bake.Step(slices[s++ % slices.Length])) { }

                float[] sliced = bake.Samples;
                float[] whole = SamplesOf(track);

                Assert.AreEqual(whole.Length, sliced.Length, "track " + track);

                for (int i = 0; i < whole.Length; i++)
                {
                    if (whole[i] != sliced[i])
                        Assert.Fail($"track {track} differs at sample {i}: {sliced[i]} sliced, {whole[i]} whole");
                }
            }
        }

        [Test]
        public void AnUnfinishedBake_HandsOutNothing()
        {
            ProceduralAudio.MusicBake bake = ProceduralAudio.BakeMusic(0);
            bake.Step(1000);

            Assert.IsFalse(bake.IsDone);
            Assert.IsNull(bake.Samples, "half a track read as a whole one");
            Assert.Throws<System.InvalidOperationException>(() => bake.ToClip());
            Assert.Greater(bake.Progress, 0f);
            Assert.Less(bake.Progress, 1f);
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
            int first = SamplesOf(0).Length;

            foreach (int track in EveryTrack())
            {
                Assert.AreEqual(first, SamplesOf(track).Length,
                    "track " + track + " is a different length to track 0");
            }
        }

        /// <summary>
        /// Nothing any track plays reaches near the top of the render or into where the hiss and
        /// click tests look.
        /// </summary>
        /// <remarks>
        /// Computed from each track's highest note and its voice's brightest partial, instead of the
        /// hand-typed 4.2 kHz this used to be -- true of the voices that existed when it was typed,
        /// and of nothing added since. A voice written too high would alias at a 22 kHz render, and
        /// its partial would read as hiss above 8 kHz; the mallets, music box and crystal tracks are
        /// all written low enough to stay clear, and this is what holds them there.
        /// </remarks>
        [Test]
        public void EveryTrack_StaysWellBelowTheTopOfTheRender()
        {
            const float Ceiling = 4400f;
            int rate = ProceduralAudio.MusicSampleRate;

            foreach (int track in EveryTrack())
            {
                float top = ProceduralAudio.HighestPartialHz(track);

                Assert.Greater(top, 0f, "track " + track + " measured as producing nothing");
                Assert.LessOrEqual(top, Ceiling,
                    $"track {track} reaches {top:0} Hz; write it lower or give its voice a darker partial");
            }

            Assert.Greater(rate * 0.5f, Ceiling * 1.5f,
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

        /// <summary>
        /// Nothing hisses under the music.
        /// </summary>
        /// <remarks>
        /// Every track had a trace of white noise mixed in on purpose, "so the quiet parts are not
        /// digitally dead". Being baked into the clip, it rose and fell with the music -- faded in and
        /// out at every loop and every change of track -- and in the sparse tracks, where a note
        /// decays to nothing between strikes, it was the only thing left playing. On speakers it was
        /// a plain hiss, and it was reported as one.
        ///
        /// -66 dBFS above the notes, against about -51 with the noise in.
        /// </remarks>
        [Test]
        public void NoTrackHasHissUnderIt()
        {
            foreach (int track in EveryTrack())
            {
                double sum = 0;
                float[] residue = AboveTheNotes(track);

                foreach (float sample in residue)
                    sum += sample * sample;

                float rms = (float)System.Math.Sqrt(sum / residue.Length);

                Assert.Less(rms, 0.0005f,
                    $"track {track} carries {rms:0.00000} RMS above 8 kHz, where none of its notes are");
            }
        }

        /// <summary>
        /// No track clicks: nothing in it jumps from one value to another between two samples.
        /// </summary>
        /// <remarks>
        /// A waveform that changes value instantly puts energy at every frequency up to the limit
        /// of the render, and above the notes that is heard as a click. Three things did it: the
        /// bass restarting at full volume and an arbitrary phase on every bar line, loudest of all
        /// and exactly every eight seconds; each plucked note starting at full volume in a single
        /// sample; and notes being dropped mid-cycle five steps after they began. The first two
        /// also sit right at the edge of what a 22 kHz render holds, which is where playback
        /// resampling turns them into a sizzle that comes and goes with the notes.
        ///
        /// -54 dBFS above the notes, against up to -30 on the bar lines before.
        /// </remarks>
        [Test]
        public void NoTrackClicks()
        {
            foreach (int track in EveryTrack())
            {
                float[] residue = AboveTheNotes(track);
                int rate = ProceduralAudio.MusicSampleRate;

                float peak = 0f;
                int at = 0;

                for (int n = 0; n < residue.Length; n++)
                {
                    if (Mathf.Abs(residue[n]) > peak)
                    {
                        peak = Mathf.Abs(residue[n]);
                        at = n;
                    }
                }

                Assert.Less(peak, 0.002f,
                    $"track {track} clicks at {at / (float)rate:0.000} s, {peak:0.0000} above 8 kHz");
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
            //
            // The chord tones too. Their colour comes from the bass under them, never from a sixth
            // note, and a chord is exactly where a stray semitone would hide best.
            foreach (int track in EveryTrack())
            {
                IReadOnlyList<int> notes = ProceduralAudio.MusicNotes(track);

                Assert.IsNotEmpty(notes, "track " + track + " plays nothing");

                foreach (int semi in notes.Concat(ProceduralAudio.MusicChordNotes(track)))
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

        /// <summary>
        /// Every track sits within 3 dB of the loudness the set is written to.
        /// </summary>
        /// <remarks>
        /// Two jobs. A track much louder or quieter than the rest would jump out of the shuffle; and
        /// the main menu's recordings are turned to <see cref="ProceduralAudio.MusicLoudnessDb"/> to
        /// match the level music, which only works if that figure is what the tracks really are.
        /// </remarks>
        [Test]
        public void EveryTrack_IsWithin3dBOfTheSetsLoudness()
        {
            foreach (int track in EveryTrack())
            {
                double sum = 0;
                float[] samples = SamplesOf(track);

                foreach (float sample in samples)
                    sum += sample * sample;

                double db = 10.0 * System.Math.Log10(sum / samples.Length);

                Assert.That(db, Is.InRange(ProceduralAudio.MusicLoudnessDb - 3.0, ProceduralAudio.MusicLoudnessDb + 3.0),
                    $"track {track} is {db:0.0} dB against the set's {ProceduralAudio.MusicLoudnessDb} dB");
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

        // -----------------------------------------------------------------
        // The shuffle
        // -----------------------------------------------------------------

        private static readonly int[] Seeds = { 0, 1, 7, 42, -3, 123456789, int.MinValue, int.MaxValue };

        /// <summary>
        /// Every bag plays every track exactly once before any plays again.
        /// </summary>
        /// <remarks>
        /// A random pick each time would leave some track unheard for an hour. The bag is what makes
        /// the shuffle fair, so this walks several bags from many seeds, bag by bag.
        /// </remarks>
        [Test]
        public void EveryBag_PlaysEveryTrackOnce()
        {
            int count = ProceduralAudio.MusicTracks;

            foreach (int seed in Seeds)
            {
                var bag = new MusicBag(count, seed);

                for (int round = 0; round < 5; round++)
                {
                    var seen = new HashSet<int>();

                    for (int i = 0; i < count; i++)
                    {
                        int track = i == 0 && round == 0 ? bag.Current : bag.Advance();

                        Assert.GreaterOrEqual(track, 0);
                        Assert.Less(track, count);
                        Assert.IsTrue(seen.Add(track),
                            $"seed {seed}, bag {round}: track {track} came round twice before the rest");
                    }
                }
            }
        }

        [Test]
        public void NoTrack_PlaysTwiceRunning()
        {
            // Including across the seam between two bags, which is the one place a bag can do it.
            foreach (int count in new[] { 2, 3, 10, ProceduralAudio.MusicTracks })
            {
                foreach (int seed in Seeds)
                {
                    var bag = new MusicBag(count, seed);
                    int previous = bag.Current;

                    for (int i = 0; i < count * 20; i++)
                    {
                        int next = bag.Advance();
                        Assert.AreNotEqual(previous, next, $"{count} tracks, seed {seed}: track {next} twice running");
                        previous = next;
                    }
                }
            }
        }

        [Test]
        public void PeekNext_IsWhatAdvanceGives()
        {
            // GameAudio bakes the peeked track while the current one plays. A peek that disagreed with
            // the advance would bake the wrong track and stall on the right one.
            var bag = new MusicBag(ProceduralAudio.MusicTracks, 42);

            for (int i = 0; i < ProceduralAudio.MusicTracks * 4; i++)
            {
                int peeked = bag.PeekNext();
                Assert.AreEqual(peeked, bag.Advance(), $"advance {i} disagreed with its peek");
            }
        }

        [Test]
        public void TheSameSeed_PlaysTheSameOrder()
        {
            var a = new MusicBag(ProceduralAudio.MusicTracks, 99);
            var b = new MusicBag(ProceduralAudio.MusicTracks, 99);

            Assert.AreEqual(a.Current, b.Current);

            for (int i = 0; i < 50; i++)
                Assert.AreEqual(a.Advance(), b.Advance(), "advance " + i);
        }

        [Test]
        public void TheShuffle_ActuallyShuffles()
        {
            // The paired test for the one above: two seeds agreeing on every one of fifty tracks
            // would mean the seed is doing nothing.
            var a = new MusicBag(ProceduralAudio.MusicTracks, 1);
            var b = new MusicBag(ProceduralAudio.MusicTracks, 2);
            int differ = a.Current != b.Current ? 1 : 0;

            for (int i = 0; i < 50; i++)
            {
                if (a.Advance() != b.Advance())
                    differ++;
            }

            Assert.Greater(differ, 0, "two seeds gave the same order");
        }

        [Test]
        public void ABagOfOneOrNone_StillAnswers()
        {
            // Nothing should ever build one, but an answer keeps the music silent rather than
            // throwing inside a level load.
            var one = new MusicBag(1, 5);
            Assert.AreEqual(0, one.Current);
            Assert.AreEqual(0, one.Advance());
            Assert.AreEqual(0, one.PeekNext());

            var none = new MusicBag(0, 5);
            Assert.AreEqual(0, none.Current);
            Assert.AreEqual(0, none.Advance());

            Assert.AreEqual(0, new MusicBag(-3, 5).Advance());
        }

        // -----------------------------------------------------------------
        // The menu's first track
        // -----------------------------------------------------------------

        [Test]
        public void TheMenusFirstTrack_IsTheSameForTheSameSeed()
        {
            // The seed is the only random thing about the music, so a test can say what a session
            // will do -- which only holds if the same seed always gives the same answer.
            for (int seed = -500; seed < 500; seed++)
                Assert.AreEqual(MusicRules.FirstMenuTrack(seed, 2), MusicRules.FirstMenuTrack(seed, 2), "seed " + seed);
        }

        /// <summary>
        /// Either track opens the menu about as often as the other.
        /// </summary>
        /// <remarks>
        /// The menu always opened on its first track. A pick that leaned hard one way would be most
        /// of that bug back, and neighbouring seeds are where a weak mix leans: they are the ones
        /// tested here.
        /// </remarks>
        [Test]
        public void EitherMenuTrack_OpensTheMenuAboutAsOftenAsTheOther()
        {
            var opened = new int[2];

            for (int seed = 0; seed < 1000; seed++)
                opened[MusicRules.FirstMenuTrack(seed, 2)]++;

            Assert.That(opened[0], Is.InRange(400, 600), $"first {opened[0]}, second {opened[1]} in 1000");
        }

        [Test]
        public void TheMenusFirstTrack_IsAlwaysOneItHas()
        {
            foreach (int seed in Seeds)
            {
                for (int count = 1; count <= 5; count++)
                    Assert.That(MusicRules.FirstMenuTrack(seed, count), Is.InRange(0, count - 1), $"seed {seed}, {count} tracks");
            }

            // A menu with no music of its own never asks, but the answer should not be a throw.
            Assert.AreEqual(0, MusicRules.FirstMenuTrack(5, 0));
            Assert.AreEqual(0, MusicRules.FirstMenuTrack(5, -1));
        }

        // -----------------------------------------------------------------
        // What the set costs
        // -----------------------------------------------------------------

        /// <summary>
        /// The music held at once fits the budget the browser build is held to.
        /// </summary>
        /// <remarks>
        /// The reason the music is rendered at half the cue rate, asserted rather than described.
        /// This used to be the whole set, because every track built stayed for the session; the
        /// figure in ProceduralAudio's remarks was written for six tracks and still said six after
        /// three more were added. Tracks are freed as they are left now, so this is the playing
        /// track, the ready one and one build buffer -- however many tracks there are.
        ///
        /// The ceiling is generous on purpose. It is not a target; it is a tripwire for longer tracks
        /// or a raised sample rate going in without anyone doing the arithmetic.
        /// </remarks>
        [Test]
        public void TheMusicHeldAtOnce_FitsItsMemoryBudget()
        {
            const int megabyte = 1024 * 1024;
            const int ceiling = 12 * megabyte;

            int bytes = ProceduralAudio.MusicResidentBytes;

            Assert.Greater(bytes, 0, "the music measured as costing nothing, so this proves nothing");
            Assert.AreEqual(3 * ProceduralAudio.MusicTrackBytes, bytes,
                "playing, ready, and one being built: three tracks at most");

            Assert.Less(bytes, ceiling,
                $"the music held at once now costs {bytes / (float)megabyte:0.0} MiB of heap against " +
                $"a {ceiling / megabyte} MiB ceiling, in a game whose whole browser build is 16 MB. " +
                "Shorten the tracks or lower MusicSampleRate -- and update the figure in its " +
                "remarks, which is where this number is explained.");
        }

        [Test]
        public void ATrackBuilt_IsTheSizeTheBudgetSaysItIs()
        {
            Assert.AreEqual(SamplesOf(0).Length * sizeof(float), ProceduralAudio.MusicTrackBytes);
        }

        /// <summary>Every track is the same length, so one track's size stands for all of them.</summary>
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
