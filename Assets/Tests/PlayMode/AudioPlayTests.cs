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

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>The source carrying the loop, as opposed to the one firing cues.</summary>
        private static AudioSource MusicSource(GameAudio audio) =>
            audio.GetComponents<AudioSource>().FirstOrDefault(s => s.loop);

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
            Assert.IsTrue(music.isPlaying, "the loop is not playing");
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
            yield return LoadScene();

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

        [UnityTest]
        public IEnumerator ReloadingTheSameLevel_LeavesTheTrackAlone()
        {
            // Re-entering the level you are already on re-fires LevelLoaded. Cycling there would
            // change the music every time somebody retried the level they were stuck on.
            yield return LoadScene();

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
            yield return LoadScene();

            GameAudio audio = Find<GameAudio>();
            LevelSession session = Find<LevelSession>();

            AudioSource music = MusicSource(audio);
            AudioClip before = music.clip;

            session.ResetBoard();

            yield return WaitForTheFade();

            Assert.AreSame(before, music.clip, "resetting the board changed the track");
        }

        /// <summary>Long enough for a fade down, a swap and a fade back up.</summary>
        private static IEnumerator WaitForTheFade()
        {
            float until = Time.unscaledTime + 2.5f;

            while (Time.unscaledTime < until)
                yield return null;
        }
    }
}
