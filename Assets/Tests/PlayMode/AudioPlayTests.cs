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

            // The second level, not the first, and in the same frame the menu closes. The test save
            // is always fresh, and on a fresh save the guided tutorial starts itself as soon as the
            // menu is gone and the first level is showing -- and the tutorial is not a level file,
            // so "reload this level" could not find it.
            LevelSession session = Find<LevelSession>();

            Find<MainMenu>().Show(false);
            session.LoadLevel(session.AvailableLevels[1]);

            yield return WaitForTheFade();
        }

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
            Assert.AreEqual("jkjkke-dream", music.clip.name, "the menu is not playing its first track");
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

            StringAssert.StartsWith("music", level.name, "leaving the menu did not play a level track");

            menu.Show(true);
            yield return WaitForTheFade();

            Assert.AreEqual("jkjkke-dream", music.clip.name, "opening the menu did not bring its music back");

            menu.Show(false);
            yield return WaitForTheFade();

            Assert.AreSame(level, music.clip, "the menu's visit changed the level's track");
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
        private static IEnumerator WaitForTheFade()
        {
            float until = Time.unscaledTime + 5f;

            while (Time.unscaledTime < until)
                yield return null;
        }
    }
}
