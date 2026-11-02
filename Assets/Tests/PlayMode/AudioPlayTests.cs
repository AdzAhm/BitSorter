using System.Collections;
using System.Linq;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The music's live questions: does it start, does Settings play its own, and does mute actually silence it.
    /// </summary>
    /// <remarks>
    /// Invisible to Edit Mode: `MusicRules` and the shuffle are pure and tested there, but whether the clip actually swaps
    /// needs Awake, Start, a frame loop and a real AudioSource. Until Part 16a these tests also held the 4.x game's cues and
    /// its track changing with the level; the simulator has neither, and plays one generated track a session under the
    /// workbench and the two recordings under Settings and the credits.
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
        /// Each test starts on a fresh save, as every other fixture's does: without this, whatever
        /// one test wrote was the next test's starting point, and the fixture passed or failed by
        /// the order NUnit ran it in.
        /// </summary>
        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        /// <summary>Takes the game back off the screen before the next fixture runs.</summary>
        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        /// <summary>
        /// Loads the game with its music on: off until switched on since Part 15a, and these are tests of the music. A test
        /// that switches it off leaves it off for the next, so every load switches it on again.
        /// </summary>
        private static IEnumerator LoadScene()
        {
            Preferences.SetInt(GameAudio.MusicKey, 1);
            yield return TestScene.Load();
            yield return WaitForTheFade();
        }

        /// <summary>Opens Settings over the workbench, as SETTINGS in its header does, and waits for its music.</summary>
        private static IEnumerator OpenSettings()
        {
            Find<WorkbenchPanel>().OpenSettings();
            Assert.IsTrue(Find<SettingsPanel>().IsShowing, "sanity: Settings did not open");
            yield return WaitForTheFade();
        }

        /// <summary>Takes Settings down with its BACK, and waits for the simulator's music.</summary>
        private static IEnumerator CloseSettings()
        {
            ShellPanel.Submit(ShellPanel.Find<Button>(SettingsPanel.BackButton));
            yield return null;
            Assert.IsFalse(Find<SettingsPanel>().IsShowing, "sanity: BACK did not close Settings");
            yield return WaitForTheFade();
        }

        /// <summary>
        /// Whether a clip is one of the menu's recordings rather than a generated track.
        /// </summary>
        /// <remarks>
        /// Generated tracks are all named "music" and their number, by ProceduralAudio. Which of the
        /// menu's tracks is playing is chance, so the tests below ask which kind it is.
        /// </remarks>
        private static bool IsMenuTrack(AudioClip clip) => clip != null && !clip.name.StartsWith("music");

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>The source carrying the music.</summary>
        private static AudioSource MusicSource(GameAudio audio) => audio.MusicSource;

        // -----------------------------------------------------------------
        // Starting
        // -----------------------------------------------------------------

        /// <summary>The music is built and playing once the scene has loaded: the session's generated track, under the workbench.</summary>
        [UnityTest]
        public IEnumerator TheMusic_PlaysOnceTheSceneHasLoaded()
        {
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();
            Assert.IsNotNull(audio, "no GameAudio in the scene");

            AudioSource music = MusicSource(audio);

            Assert.IsNotNull(music, "the music source was never built");
            Assert.IsNotNull(music.clip, "the music source has no clip");
            Assert.IsFalse(IsMenuTrack(music.clip), "the workbench is playing " + music.clip.name + ", not a generated track");

            // Waited on, with WaitForTheFade's kind of cap: generous, and a deadlock guard rather than a timing assumption.
            float until = Time.unscaledTime + 60f;

            while (!music.isPlaying)
            {
                if (Time.unscaledTime > until)
                    Assert.Fail("the music is not playing a minute after the scene loaded");

                yield return null;
            }
        }

        // -----------------------------------------------------------------
        // Settings' own music
        // -----------------------------------------------------------------

        /// <summary>Settings plays the menu's recordings, where the workbench under it plays a generated track.</summary>
        [UnityTest]
        public IEnumerator Settings_PlaysTheMenusOwnMusic()
        {
            yield return LoadScene();
            AudioSource music = MusicSource(Find<GameAudio>());
            Assert.IsFalse(IsMenuTrack(music.clip), "sanity: the workbench plays a generated track");

            yield return OpenSettings();

            Assert.IsTrue(IsMenuTrack(music.clip), "Settings is playing " + music.clip?.name + ", not the menu's own music");
        }

        /// <summary>
        /// Settings opens on either of its tracks, by chance -- not on the same one every launch.
        /// </summary>
        /// <remarks>
        /// The 4.x main menu always opened on the first, and the second only came on if the menu stayed up for all two and a
        /// half minutes of it. Each launch here seeds Unity's random numbers, which is all GameAudio draws its session seed
        /// from, so every run of this test sees the same launches.
        /// </remarks>
        [UnityTest]
        public IEnumerator Settings_DoesNotAlwaysOpenOnTheSameTrack()
        {
            var opened = new System.Collections.Generic.HashSet<string>();
            Random.State saved = Random.state;

            try
            {
                for (int launch = 0; launch < 12 && opened.Count < 2; launch++)
                {
                    Random.InitState(launch);

                    yield return LoadScene();
                    yield return OpenSettings();

                    AudioClip clip = MusicSource(Find<GameAudio>()).clip;
                    Assert.IsTrue(IsMenuTrack(clip), "sanity: launch " + launch + " should play the menu's music under Settings");

                    opened.Add(clip.name);

                    yield return TestScene.Clear();
                }
            }
            finally
            {
                Random.state = saved;
            }

            Assert.Greater(opened.Count, 1,
                "twelve launches all opened Settings on " + string.Join(", ", opened));
        }

        /// <summary>
        /// Coming back to Settings plays the track it did not play last time, not the same one again from the top.
        /// </summary>
        [UnityTest]
        public IEnumerator ComingBackToSettings_PlaysItsOtherTrack()
        {
            yield return LoadScene();
            yield return OpenSettings();

            AudioSource music = MusicSource(Find<GameAudio>());
            string first = music.clip.name;

            Assert.IsTrue(IsMenuTrack(music.clip), "sanity: Settings should play the menu's music");

            yield return CloseSettings();

            Assert.IsFalse(IsMenuTrack(music.clip), "sanity: leaving Settings should play the generated track");

            yield return OpenSettings();

            Assert.IsTrue(IsMenuTrack(music.clip), "opening Settings again did not bring its music back");
            Assert.AreNotEqual(first, music.clip.name,
                "Settings came back to the track it had just played, from the top");
        }

        /// <summary>
        /// Leaving Settings plays the workbench's track, and going back and forth resumes the same one -- the same clip, not
        /// a new build of it.
        /// </summary>
        [UnityTest]
        public IEnumerator SettingsAndTheWorkbench_EachKeepTheirOwnMusic()
        {
            yield return LoadScene();

            AudioSource music = MusicSource(Find<GameAudio>());
            AudioClip workbench = music.clip;

            Assert.IsFalse(IsMenuTrack(workbench), "the workbench did not play a generated track");

            yield return OpenSettings();

            Assert.IsTrue(IsMenuTrack(music.clip), "opening Settings did not bring its music");

            yield return CloseSettings();

            Assert.AreSame(workbench, music.clip, "a visit to Settings changed the workbench's track");
        }

        /// <summary>
        /// The menu's recordings are heard at the loudness of the generated music, both of them.
        /// </summary>
        /// <remarks>
        /// They are commercial-style masters, and they played at the same volume as the generated
        /// tracks: "Dream" came out eleven decibels louder than the level music, about twice as loud
        /// to the ear, so the music dropped away every time a level started.
        ///
        /// Measured rather than trusted. The recordings are decoded from the files themselves and
        /// put through what their import settings do to them; the generated track is rendered; each is
        /// scaled by the volume its source actually plays at. Three decibels is the spread the
        /// generated tracks already have between themselves.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheMenuMusic_IsAsLoudAsTheGeneratedMusic()
        {
            yield return LoadScene();

            AudioSource music = MusicSource(Find<GameAudio>());
            var menu = new System.Collections.Generic.List<(string Name, double Db)>();

            AudioClip generated = music.clip;
            Assert.IsFalse(IsMenuTrack(generated), "sanity: the workbench should play a generated track");
            double generatedDb = RmsDb(ProceduralAudio.MusicSamples(int.Parse(generated.name.Substring("music".Length))))
                                 + VolumeDb(music.volume);

            // Whichever track Settings opens on, then -- the workbench between -- the other.
            yield return OpenSettings();

            AudioClip first = music.clip;
            float firstVolume = music.volume;
            Assert.IsTrue(IsMenuTrack(first), "sanity: Settings should play the menu's music");

            yield return CloseSettings();
            yield return OpenSettings();

            AudioClip second = music.clip;
            Assert.IsTrue(IsMenuTrack(second), "sanity: Settings should play its music again");
            Assert.AreNotEqual(first.name, second.name, "sanity: coming back should play the other track");

            double[] recorded = new double[1];

            yield return RecordedLoudness(first.name, recorded);
            menu.Add((first.name, recorded[0] + VolumeDb(firstVolume)));

            yield return RecordedLoudness(second.name, recorded);
            menu.Add((second.name, recorded[0] + VolumeDb(music.volume)));

            string report = $"the generated music is heard at {generatedDb:0.0} dB; " +
                            string.Join(", ", menu.Select(m => $"{m.Name} at {m.Db:0.0} dB"));

            Assert.IsTrue(menu.All(m => System.Math.Abs(m.Db - generatedDb) <= 3.0), report);
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

        /// <summary>
        /// The game credits the menu's music on its credits roll, as the CC BY track's licence
        /// requires -- and only there, since the line along the foot of the 4.x main menu was taken off
        /// after a playtest (2026-09-27).
        /// </summary>
        /// <remarks>
        /// Read off the roll the game actually builds, not off <see cref="Credits"/>: an Edit Mode
        /// test already holds the list to the licence's terms, and this is the half that says the
        /// list reaches the screen.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheCreditsRoll_CreditsTheMenuMusic()
        {
            yield return LoadScene();

            // The roll is a page of the workbench since Part 15g, built as it first opens: from Settings, as a player
            // reaches it.
            Find<WorkbenchPanel>().OpenSettings();
            yield return null;
            ShellPanel.Submit(ShellPanel.Find<Button>(SettingsPanel.CreditsButton));
            yield return null;
            var roll = ShellPanel.Find<UnityEngine.UIElements.VisualElement>(CreditsPanel.PageName);

            Assert.IsNotNull(roll, "the credits roll was not built");

            var lines = new System.Text.StringBuilder();

            UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.TextElement>(roll)
                .ForEach(label => lines.AppendLine(label.text));

            string text = lines.ToString();

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
        /// Mute silences the music, and unmuting brings it back.
        /// </summary>
        /// <remarks>
        /// Paired in one test on purpose: on its own, "muted" passes just as happily on a source that would have made no
        /// sound anyway. Until Part 16a this also counted the 4.x board's cues, which the simulator does not make.
        /// </remarks>
        [UnityTest]
        public IEnumerator Muting_SilencesTheMusic_AndUnmutingBringsItBack()
        {
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();

            audio.SetMuted(true);
            yield return null;

            Assert.IsTrue(MusicSource(audio).mute, "the music is not muted");

            audio.SetMuted(false);
            yield return null;

            Assert.IsFalse(MusicSource(audio).mute, "the music stayed muted after unmuting");
        }

        /// <summary>Music off silences the music under the sound switch, and on again brings it back.</summary>
        [UnityTest]
        public IEnumerator MusicOff_SilencesTheMusic()
        {
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();

            audio.SetMusic(false);
            yield return null;

            try
            {
                Assert.IsTrue(MusicSource(audio).mute, "the music is still playing with the music off");
            }
            finally
            {
                audio.SetMusic(true);
            }

            yield return null;
            Assert.IsFalse(MusicSource(audio).mute, "the music stayed off after switching it on");
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

        /// <summary>
        /// Waits until the music has stopped reacting to whatever the test just did.
        /// </summary>
        /// <remarks>
        /// This waited five seconds of wall clock, and a track is built at two milliseconds a
        /// *frame*. An editor in the background renders few enough frames that five seconds never
        /// finished one, so seven tests failed -- reproducibly, which made it read as a
        /// bug in the music rather than as a test that measured the wrong thing. Waiting on the
        /// condition is what CLAUDE.md already said about the 4.x game's cue tests: drive the clock, do
        /// not wait for it.
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
