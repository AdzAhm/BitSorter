using System.Collections;
using System.Linq;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The audio layer's two live questions: does mute actually silence the game, and does the
    /// background track change only when it should.
    /// </summary>
    /// <remarks>
    /// Both are structurally invisible to Edit Mode. `MusicRules` is pure and already tested there,
    /// but it only says what *should* happen on a level change -- whether `GameAudio` is subscribed
    /// at the right moment, and whether the clip actually swaps, needs Awake, Start, a frame loop and
    /// a real AudioSource.
    ///
    /// The subscription order is the interesting one. `GameAudio` subscribes to `LevelLoaded` in
    /// OnEnable, and `LevelSession` loads the first level in its Start -- so the first event can and
    /// does arrive before `GameAudio.Start` has built the music source at all. That is the same
    /// initialisation-order shape that made the tutorial's buttons do nothing, and the first test
    /// here is simply that it survives it.
    /// </remarks>
    [TestFixture]
    public class AudioPlayTests
    {
        /// <summary>
        /// Redirect first, then change anything.
        /// </summary>
        /// <remarks>
        /// The order is the whole point. These tests flip the mute, which used to be read, saved
        /// and restored around them -- so an interrupted run left the developer's game muted. Mute
        /// and reporting both go through <see cref="Preferences"/> now, and SaveGuard redirects it
        /// alongside the save file, so there is nothing to restore: the real settings are never
        /// read and never written.
        ///
        /// SetReporting has to come *after* Redirect, or it writes to the real machine on its way
        /// past. It used to come before.
        /// </remarks>
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        /// <summary>
        /// Takes the game back off the screen before the next fixture runs.
        /// </summary>
        /// <remarks>
        /// Not optional. Leaving the scene loaded turned two of `PointerArbitrationPlayTests`'
        /// assertions red -- it builds its own canvas and assumes nothing else is on screen, and
        /// the main menu is a full-screen panel, so the pointer was over UI everywhere.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static IEnumerator LoadScene() => TestScene.Load();

        /// <summary>
        /// Loads the game and goes past the main menu, which has music of its own, to a level's.
        /// </summary>
        private static IEnumerator LoadSceneAtALevel()
        {
            yield return TestScene.Load();
            yield return LeaveTheMenuForALevel();
        }

        /// <summary>Closes the main menu onto a level, and waits for the level's music.</summary>
        private static IEnumerator LeaveTheMenuForALevel()
        {
            // The second level, not the first, and in the same frame the menu closes. The test save
            // is always fresh, and on a fresh save the guided tutorial starts itself as soon as the
            // menu is gone and the first level is showing -- and the tutorial is not a level file,
            // so "reload this level" could not find it.
            LevelSession session = Find<LevelSession>();

            Find<MainMenu>().Show(false);
            session.LoadLevel(session.AvailableLevels[1]);

            yield return WaitForTheFade();
        }

        /// <summary>
        /// Whether a clip is one of the menu's recordings rather than a generated level track.
        /// </summary>
        /// <remarks>
        /// Generated tracks are all named "music" and their number, by ProceduralAudio. Which of the
        /// menu's tracks is playing is chance, so the tests below ask which kind it is.
        /// </remarks>
        private static bool IsMenuTrack(AudioClip clip) => clip != null && !clip.name.StartsWith("music");

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>The source carrying the loop, as opposed to the one firing cues.</summary>
        private static AudioSource MusicSource(GameAudio audio) => audio.MusicSource;

        // -----------------------------------------------------------------
        // Initialisation order
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheMusicSurvivesTheFirstLevelLoadingBeforeItStarts()
        {
            // LevelSession.Start loads a level, which fires LevelLoaded into a GameAudio that has
            // subscribed in OnEnable but not yet reached its own Start. Nothing may throw, and the
            // track that session opened on must still be the one playing.
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();
            Assert.IsNotNull(audio, "no GameAudio in the scene");

            AudioSource music = MusicSource(audio);

            Assert.IsNotNull(music, "the music source was never built");
            Assert.IsNotNull(music.clip, "the music source has no clip");

            // The menu's first track loads in the background, so it may take a moment to start.
            float until = Time.unscaledTime + 2f;

            while (!music.isPlaying && Time.unscaledTime < until)
                yield return null;

            Assert.IsTrue(music.isPlaying, "the music is not playing");
        }

        // -----------------------------------------------------------------
        // The menu's own music
        // -----------------------------------------------------------------

        /// <summary>The game opens on the main menu, and the menu plays its own music.</summary>
        [UnityTest]
        public IEnumerator TheMainMenu_PlaysItsOwnMusic()
        {
            yield return LoadScene();
            yield return null;

            AudioSource music = MusicSource(Find<GameAudio>());

            Assert.IsTrue(Find<MainMenu>().IsOpen, "sanity: the game should open on the menu");
            Assert.IsTrue(IsMenuTrack(music.clip), "the menu is playing " + music.clip?.name + ", not its own music");
        }

        /// <summary>
        /// The menu opens on either of its tracks, by chance -- not on the same one every launch.
        /// </summary>
        /// <remarks>
        /// It always opened on the first, and the second only came on if the menu stayed up for all
        /// two and a half minutes of it, so a player could restart any number of times and never
        /// hear it. Each launch here seeds Unity's random numbers, which is all GameAudio draws its
        /// session seed from, so every run of this test sees the same launches.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheMenu_DoesNotAlwaysOpenOnTheSameTrack()
        {
            var opened = new System.Collections.Generic.HashSet<string>();
            Random.State saved = Random.state;

            try
            {
                for (int launch = 0; launch < 12 && opened.Count < 2; launch++)
                {
                    Random.InitState(launch);

                    yield return LoadScene();
                    yield return null;

                    AudioClip clip = MusicSource(Find<GameAudio>()).clip;
                    Assert.IsTrue(IsMenuTrack(clip), "sanity: launch " + launch + " should open on the menu's music");

                    opened.Add(clip.name);

                    yield return TestScene.Clear();
                }
            }
            finally
            {
                Random.state = saved;
            }

            Assert.Greater(opened.Count, 1,
                "twelve launches all opened the menu on " + string.Join(", ", opened));
        }

        /// <summary>
        /// Coming back to the menu plays the track it did not play last time, not the same one again
        /// from the top.
        /// </summary>
        [UnityTest]
        public IEnumerator ComingBackToTheMenu_PlaysItsOtherTrack()
        {
            yield return LoadScene();
            yield return null;

            AudioSource music = MusicSource(Find<GameAudio>());
            string first = music.clip.name;

            Assert.IsTrue(IsMenuTrack(music.clip), "sanity: the game should open on the menu's music");

            yield return LeaveTheMenuForALevel();

            Assert.IsFalse(IsMenuTrack(music.clip), "sanity: leaving the menu should play a level track");

            Find<MainMenu>().Show(true);
            yield return WaitForTheFade();

            Assert.IsTrue(IsMenuTrack(music.clip), "opening the menu again did not bring its music back");
            Assert.AreNotEqual(first, music.clip.name,
                "the menu came back to the track it had just played, from the top");
        }

        /// <summary>
        /// Leaving the menu plays the level's track, and going back and forth resumes the same one.
        /// </summary>
        /// <remarks>
        /// The track changes only when the level does. Opening the menu over a level is not a level
        /// change, so coming back must find the same track -- the same clip, not a new build of it.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheMenuAndTheLevel_EachKeepTheirOwnMusic()
        {
            yield return LoadSceneAtALevel();

            MainMenu menu = Find<MainMenu>();
            AudioSource music = MusicSource(Find<GameAudio>());
            AudioClip level = music.clip;

            Assert.IsFalse(IsMenuTrack(level), "leaving the menu did not play a level track");

            menu.Show(true);
            yield return WaitForTheFade();

            Assert.IsTrue(IsMenuTrack(music.clip), "opening the menu did not bring its music back");

            menu.Show(false);
            yield return WaitForTheFade();

            Assert.AreSame(level, music.clip, "the menu's visit changed the level's track");
        }

        /// <summary>
        /// The menu's recordings are heard at the loudness of the level music, both of them.
        /// </summary>
        /// <remarks>
        /// They are commercial-style masters, and they played at the same volume as the generated
        /// tracks: "Dream" came out eleven decibels louder than the level music, about twice as loud
        /// to the ear, so the music dropped away every time a level started.
        ///
        /// Measured rather than trusted. The recordings are decoded from the files themselves and
        /// put through what their import settings do to them; the level track is rendered; each is
        /// scaled by the volume its source actually plays at. Three decibels is the spread the
        /// generated tracks already have between themselves.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheMenuMusic_IsAsLoudAsTheLevelMusic()
        {
            yield return LoadScene();
            yield return null;

            AudioSource music = MusicSource(Find<GameAudio>());
            var menu = new System.Collections.Generic.List<(string Name, double Db)>();

            // Whichever track the menu opens on, then -- the level between -- the other.
            AudioClip first = music.clip;
            float firstVolume = music.volume;
            Assert.IsTrue(IsMenuTrack(first), "sanity: the game should open on the menu's music");

            yield return LeaveTheMenuForALevel();

            AudioClip level = music.clip;
            Assert.IsFalse(IsMenuTrack(level), "sanity: leaving the menu should play a level track");
            double levelDb = RmsDb(ProceduralAudio.MusicSamples(int.Parse(level.name.Substring("music".Length))))
                             + VolumeDb(music.volume);

            Find<MainMenu>().Show(true);
            yield return WaitForTheFade();

            AudioClip second = music.clip;
            Assert.IsTrue(IsMenuTrack(second), "sanity: the menu should play its music again");
            Assert.AreNotEqual(first.name, second.name, "sanity: coming back should play the other track");

            double[] recorded = new double[1];

            yield return RecordedLoudness(first.name, recorded);
            menu.Add((first.name, recorded[0] + VolumeDb(firstVolume)));

            yield return RecordedLoudness(second.name, recorded);
            menu.Add((second.name, recorded[0] + VolumeDb(music.volume)));

            string report = $"the level's music is heard at {levelDb:0.0} dB; " +
                            string.Join(", ", menu.Select(m => $"{m.Name} at {m.Db:0.0} dB"));

            Assert.IsTrue(menu.All(m => System.Math.Abs(m.Db - levelDb) <= 3.0), report);
        }

        private static double VolumeDb(float volume) => 20.0 * System.Math.Log10(volume);

        private static double RmsDb(float[] samples)
        {
            double sum = 0;

            foreach (float sample in samples)
                sum += sample * sample;

            return 10.0 * System.Math.Log10(sum / samples.Length);
        }

        /// <summary>
        /// How loud a menu recording is once imported: decoded from its file, mixed down to mono and
        /// normalised to its peak, the way its import settings say.
        /// </summary>
        /// <remarks>
        /// The game's own copy is compressed and cannot be read back, so this decodes the file the
        /// import was made from. If the import stops mixing to mono or normalising, this model is
        /// wrong, and it says so instead of measuring something the game no longer plays.
        /// </remarks>
        private static IEnumerator RecordedLoudness(string name, double[] db)
        {
            string path = System.IO.Path.Combine(Application.dataPath, "Audio/Music", name + ".mp3");
            string meta = System.IO.File.ReadAllText(path + ".meta");

            Assert.IsTrue(meta.Contains("forceToMono: 1") && meta.Contains("normalize: 1"),
                name + " is no longer imported as normalised mono; update how this test measures it");

            using (UnityEngine.Networking.UnityWebRequest request =
                   UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(
                       new System.Uri(path).AbsoluteUri, AudioType.MPEG))
            {
                var handler = (UnityEngine.Networking.DownloadHandlerAudioClip)request.downloadHandler;
                handler.streamAudio = false;
                handler.compressed = false;

                yield return request.SendWebRequest();

                Assert.AreEqual(UnityEngine.Networking.UnityWebRequest.Result.Success, request.result,
                    "could not decode " + name + ": " + request.error);

                AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(request);

                while (clip.loadState == AudioDataLoadState.Loading)
                    yield return null;

                // A second at a time, twice over: the peak first, then the loudness under it.
                int channels = clip.channels;
                var chunk = new float[clip.frequency * channels];
                double peak = 0, sum = 0;

                for (int pass = 0; pass < 2; pass++)
                {
                    for (int start = 0; start < clip.samples; start += clip.frequency)
                    {
                        int frames = Mathf.Min(clip.frequency, clip.samples - start);
                        float[] data = frames == clip.frequency ? chunk : new float[frames * channels];

                        Assert.IsTrue(clip.GetData(data, start), "could not read " + name + " back");

                        for (int i = 0; i < frames; i++)
                        {
                            double mono = 0;

                            for (int c = 0; c < channels; c++)
                                mono += data[i * channels + c];

                            mono /= channels;

                            if (pass == 0)
                                peak = System.Math.Max(peak, System.Math.Abs(mono));
                            else
                                sum += (mono / peak) * (mono / peak);
                        }
                    }
                }

                db[0] = 10.0 * System.Math.Log10(sum / clip.samples);
                Object.Destroy(clip);
            }
        }

        /// <summary>The menu credits its music, as the CC BY track's licence requires.</summary>
        [UnityTest]
        public IEnumerator TheMenu_CreditsItsMusic()
        {
            yield return LoadScene();

            GameObject credit = GameObject.Find("music credit");
            Assert.IsNotNull(credit, "the menu has no music credit");

            string text = credit.GetComponent<TMPro.TextMeshProUGUI>().text;

            StringAssert.Contains("Woodland Fantasy", text);
            StringAssert.Contains("Matthew Pablo", text);
            StringAssert.Contains("CC BY 3.0", text);
            StringAssert.Contains("jkjkke", text);

            // CC BY 3.0 asks for the licence's address with every copy of the work. The README gives
            // it, but a browser build is a copy that ships without the README.
            StringAssert.Contains("creativecommons.org/licenses/by/3.0", text,
                "the credit does not say where the CC BY licence can be read");
        }

        // -----------------------------------------------------------------
        // Mute
        // -----------------------------------------------------------------

        /// <summary>
        /// Advances the clock by hand and reports how many cues that produced.
        /// </summary>
        /// <remarks>
        /// `StepOneTick` rather than waiting for wall-clock frames to contain a tick. The runner
        /// ticks twice a second, so ninety frames should have held three -- but a test that is
        /// merely probably right is worse here than one that is slow, and the first version of
        /// this produced zero cues and no explanation. Stepping the clock is exact, and a
        /// hundred times faster.
        ///
        /// The first frame is spent letting `GameAudio` baseline the tick it already sees; only a
        /// tick it watches *advance* is a sound.
        /// </remarks>
        private static IEnumerator CuesFrom(GameAudio audio, LevelSession session,
                                            SimulationRunner runner, System.Action<int> report)
        {
            session.Run();
            yield return null;

            int before = audio.CuesPlayed;

            for (int tick = 0; tick < 3; tick++)
            {
                runner.StepOneTick();
                yield return null;
            }

            report(audio.CuesPlayed - before);
        }

        /// <summary>
        /// Mute silences the cues, not only the loop.
        /// </summary>
        /// <remarks>
        /// It used to silence only the music, while the clock carried on ticking twice a second --
        /// which is the sound somebody reaching for mute most wants gone. The music source is easy
        /// to check because it holds state; the cues are fired and forgotten, which is why
        /// `GameAudio` counts them.
        ///
        /// Paired with the test below on purpose. On its own this one passes just as happily when
        /// nothing would have made a sound anyway, which is exactly how the first version of it
        /// passed while being unable to prove anything.
        /// </remarks>
        [UnityTest]
        public IEnumerator MutingSilencesTheCuesAndNotOnlyTheMusic()
        {
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            audio.SetMuted(true);
            yield return null;

            Assert.IsTrue(MusicSource(audio).mute, "the loop is not muted");

            int cues = -1;
            yield return CuesFrom(audio, session, runner, n => cues = n);

            Assert.AreEqual(0, cues, "cues were played while the game was muted");
        }

        [UnityTest]
        public IEnumerator UnmutingLetsTheCuesThroughAgain()
        {
            // The other half, and the one that gives the test above its meaning: mute must not
            // simply break the audio layer permanently, and the board must actually be capable of
            // making a sound under the same conditions.
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            audio.SetMuted(true);
            yield return null;

            audio.SetMuted(false);
            yield return null;

            Assert.IsFalse(MusicSource(audio).mute, "the loop stayed muted after unmuting");

            int cues = -1;
            yield return CuesFrom(audio, session, runner, n => cues = n);

            Assert.Greater(cues, 0, "three ticks of a running board produced no cue at all");
        }

        /// <summary>
        /// Music off silences the music and leaves the effects: the board still clicks and chimes.
        /// </summary>
        /// <remarks>
        /// Asked for in a playtest, 2026-09-26: the music and the effects each switched on their own,
        /// under the switch for the whole game. Each test here proves the other half still sounds,
        /// so neither can pass by silencing everything.
        /// </remarks>
        [UnityTest]
        public IEnumerator MusicOff_SilencesTheMusic_AndLeavesTheEffects()
        {
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            audio.SetMusic(false);
            yield return null;

            try
            {
                Assert.IsTrue(MusicSource(audio).mute, "the music is still playing with the music off");

                int cues = -1;
                yield return CuesFrom(audio, session, runner, n => cues = n);

                Assert.Greater(cues, 0, "switching the music off silenced the effects as well");
            }
            finally
            {
                audio.SetMusic(true);
            }
        }

        /// <summary>Effects off silences the effects and leaves the music playing.</summary>
        [UnityTest]
        public IEnumerator EffectsOff_SilencesTheEffects_AndLeavesTheMusic()
        {
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            audio.SetEffects(false);
            yield return null;

            try
            {
                Assert.IsFalse(MusicSource(audio).mute, "switching the effects off silenced the music as well");

                int cues = -1;
                yield return CuesFrom(audio, session, runner, n => cues = n);

                Assert.AreEqual(0, cues, "effects were played with the effects off");
            }
            finally
            {
                audio.SetEffects(true);
            }
        }

        [UnityTest]
        public IEnumerator TheMuteSettingIsRemembered()
        {
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();

            audio.SetMuted(true);
            Assert.IsTrue(GameAudio.Muted);

            audio.ToggleMute();
            Assert.IsFalse(GameAudio.Muted);

            yield return null;
        }

        // -----------------------------------------------------------------
        // When the track changes
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator ADifferentLevel_ChangesTheTrack()
        {
            yield return LoadSceneAtALevel();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();

            AudioSource music = MusicSource(audio);
            AudioClip before = music.clip;

            string elsewhere = session.AvailableLevels.First(name => name != session.LevelName);
            session.LoadLevel(elsewhere);

            yield return WaitForTheFade();

            Assert.AreNotSame(before, music.clip,
                "moving to a different level left the same track playing");
            Assert.IsTrue(music.isPlaying, "the new track is not playing");
        }

        /// <summary>
        /// The track a level change leaves behind is freed, not kept.
        /// </summary>
        /// <remarks>
        /// Every track built used to stay for the session. At two dozen tracks that is more browser
        /// heap than the rest of the game uses, so GameAudio holds two and destroys the one it left.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheTrackALevelChangeLeaves_IsFreed()
        {
            yield return LoadSceneAtALevel();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();

            AudioSource music = MusicSource(audio);
            AudioClip before = music.clip;

            Assert.IsTrue(before != null, "sanity: a track should be playing");

            string elsewhere = session.AvailableLevels.First(name => name != session.LevelName);
            session.LoadLevel(elsewhere);

            yield return WaitForTheFade();

            Assert.AreNotSame(before, music.clip, "sanity: the track should have changed");
            Assert.IsTrue(before == null, "the track the level change left behind is still held");
        }

        [UnityTest]
        public IEnumerator ReloadingTheSameLevel_LeavesTheTrackAlone()
        {
            // Re-entering the level you are already on re-fires LevelLoaded. Cycling there would
            // change the music every time somebody retried the level they were stuck on.
            yield return LoadSceneAtALevel();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();

            AudioSource music = MusicSource(audio);
            AudioClip before = music.clip;

            session.LoadLevel(session.LevelName);

            yield return WaitForTheFade();

            Assert.AreSame(before, music.clip,
                "reloading the same level changed the track");
        }

        [UnityTest]
        public IEnumerator ResettingTheBoard_LeavesTheTrackAlone()
        {
            // R resets the simulation without reloading the level, so it should not even reach the
            // rule. Worth pinning: if reset ever starts firing LevelLoaded, the music changing is the
            // symptom nobody would connect back to it.
            yield return LoadSceneAtALevel();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();

            AudioSource music = MusicSource(audio);
            AudioClip before = music.clip;

            session.ResetBoard();

            yield return WaitForTheFade();

            Assert.AreSame(before, music.clip, "resetting the board changed the track");
        }

        /// <summary>
        /// Long enough for a fade down, a swap and a fade back up -- and for the new track to finish
        /// building first, which GameAudio waits for rather than freezing a frame on.
        /// </summary>
        /// <summary>
        /// Waits until the music has stopped reacting to whatever the test just did.
        /// </summary>
        /// <remarks>
        /// This waited five seconds of wall clock, and a track is built at two milliseconds a
        /// *frame*. An editor in the background renders few enough frames that five seconds never
        /// finished one, so all seven tests below failed -- reproducibly, which made it read as a
        /// bug in the music rather than as a test that measured the wrong thing. Waiting on the
        /// condition is what CLAUDE.md already says about the cue tests: drive the clock, do not
        /// wait for it.
        ///
        /// Two frames first, so a change made on this frame has been seen before the condition is
        /// asked about; otherwise "settled" is still true from before it.
        ///
        /// The cap is wall clock and generous. It is a deadlock guard, not a timing assumption --
        /// reaching it is a failure with a sentence saying so, where a silent return would leave
        /// whichever assertion came next to report something misleading.
        /// </remarks>
        private static IEnumerator WaitForTheFade()
        {
            yield return null;
            yield return null;

            GameAudio audio = Find<GameAudio>();

            if (audio == null)
                yield break;

            float until = Time.unscaledTime + 60f;

            while (!audio.IsSettled)
            {
                if (Time.unscaledTime > until)
                    Assert.Fail("the music never settled: it is still mid-change after a minute");

                yield return null;
            }
        }
    }
}
