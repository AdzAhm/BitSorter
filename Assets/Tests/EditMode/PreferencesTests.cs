using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="Preferences"/>: the seam that keeps a test run out of the machine's own settings.
    /// </summary>
    /// <remarks>
    /// The mute and the reporting flag live in PlayerPrefs rather than in the save file, because
    /// they describe this machine and should not travel with a copied save. That put them outside
    /// <see cref="ProgressStore.Redirected"/>'s protection, so the Play Mode fixtures that needed
    /// reporting off read the real value, overwrote it, and put it back in OneTimeTearDown -- which
    /// is the failure SaveGuard exists to prevent, applied to a different store. An interrupted run
    /// left the developer's game muted with analytics off.
    ///
    /// These tests redirect first and assert the real keys were never touched. They deliberately use
    /// key names nothing in the game owns, so even a broken redirect could not corrupt a setting
    /// that matters.
    /// </remarks>
    public class PreferencesTests
    {
        /// <summary>Keys nothing in the game uses, so a failure here cannot damage a real setting.</summary>
        private const string ScratchKey = "bitsorter.tests.preferences.scratch";

        private const string OtherKey = "bitsorter.tests.preferences.other";

        [SetUp]
        public void Redirect() => Preferences.Redirected = new Dictionary<string, int>();

        [TearDown]
        public void Release()
        {
            Preferences.Redirected = null;

            // Nothing above should have written these. Cleaned up anyway, so a regression in the
            // redirect cannot leave debris in the editor's own preferences for the next run.
            PlayerPrefs.DeleteKey(ScratchKey);
            PlayerPrefs.DeleteKey(OtherKey);
        }

        [Test]
        public void ARedirectedWrite_NeverReachesPlayerPrefs()
        {
            Preferences.SetInt(ScratchKey, 1);

            Assert.AreEqual(1, Preferences.GetInt(ScratchKey, 0), "the value should be readable back");

            Assert.IsFalse(PlayerPrefs.HasKey(ScratchKey),
                "a redirected write reached the machine's real settings, which is the whole thing " +
                "this seam exists to prevent");
        }

        [Test]
        public void ARedirectedRead_FallsBackWithoutConsultingPlayerPrefs()
        {
            // The fallback is what makes a redirected run look like a fresh machine, which is what
            // a test wants: GameAnalytics.Reporting defaults on, GameAudio.Muted defaults off.
            Assert.AreEqual(7, Preferences.GetInt(ScratchKey, 7), "an unset key gives the fallback");

            Assert.IsFalse(PlayerPrefs.HasKey(ScratchKey), "reading must not create the key either");
        }

        [Test]
        public void KeysAreIndependent()
        {
            Preferences.SetInt(ScratchKey, 1);
            Preferences.SetInt(OtherKey, 0);

            Assert.AreEqual(1, Preferences.GetInt(ScratchKey, -1));
            Assert.AreEqual(0, Preferences.GetInt(OtherKey, -1));
        }

        [Test]
        public void AWriteCanBeOverwritten()
        {
            Preferences.SetInt(ScratchKey, 1);
            Preferences.SetInt(ScratchKey, 0);

            Assert.AreEqual(0, Preferences.GetInt(ScratchKey, -1));
        }

        /// <summary>
        /// Releasing the redirect hands the real settings back.
        /// </summary>
        /// <remarks>
        /// The other half: a seam that stayed redirected would silently stop the game remembering
        /// anything, which in a player build is a mute toggle that does nothing.
        /// </remarks>
        [Test]
        public void ReleasingTheRedirect_GoesBackToPlayerPrefs()
        {
            Preferences.SetInt(ScratchKey, 1);
            Preferences.Redirected = null;

            Assert.AreEqual(4, Preferences.GetInt(ScratchKey, 4),
                "once released, the scratch value must be gone and the real store consulted");

            Assert.IsFalse(PlayerPrefs.HasKey(ScratchKey),
                "and the scratch write must not have leaked into it");
        }

        /// <summary>
        /// The two settings the game actually keeps, read through the seam.
        /// </summary>
        /// <remarks>
        /// Their defaults are load-bearing and opposite: reporting is on unless the player turns it
        /// off, which the README promises, and the game starts unmuted. A redirected run therefore
        /// begins with analytics on, which is exactly why the Play Mode fixtures turn it off *after*
        /// redirecting rather than before.
        /// </remarks>
        [Test]
        public void TheGamesOwnSettings_HaveTheirDocumentedDefaults()
        {
            Assert.IsTrue(GameAnalytics.Reporting, "reporting is on unless the player turns it off");
            Assert.IsFalse(GameAudio.Muted, "the game starts unmuted");
            Assert.IsTrue(GameAudio.MusicOn, "the music starts on");
            Assert.IsTrue(GameAudio.EffectsOn, "the effects start on");
        }

        /// <summary>
        /// Flipping the mute in a test leaves the machine's own setting exactly as it was.
        /// </summary>
        /// <remarks>
        /// Compared before and against after rather than asserting the real key is absent -- whoever
        /// is running this may well have muted the game for real, and a test that failed because of
        /// that would be reporting on the developer rather than on the code.
        /// </remarks>
        [Test]
        public void FlippingMute_LeavesTheRealSettingAlone()
        {
            const string realKey = "bitsorter.music.muted";

            int before = PlayerPrefs.GetInt(realKey, -1);   // -1 means never set on this machine

            GameAudio.Muted = true;
            Assert.IsTrue(GameAudio.Muted, "the scratch store should hold the new value");

            GameAudio.Muted = false;
            Assert.IsFalse(GameAudio.Muted);

            Assert.AreEqual(before, PlayerPrefs.GetInt(realKey, -1),
                "flipping the mute in a test changed the machine's real setting");
        }

        /// <summary>
        /// A staged value is live at once, read back like any other, and never reaches PlayerPrefs
        /// while redirected -- nor does the save that follows it.
        /// </summary>
        [Test]
        public void AStagedWrite_IsReadBack_AndNeverReachesPlayerPrefs()
        {
            Preferences.Stage(ScratchKey, 7);

            Assert.AreEqual(7, Preferences.GetInt(ScratchKey, 0), "a staged value should read back at once");
            Assert.DoesNotThrow(Preferences.Save);
            Assert.IsFalse(PlayerPrefs.HasKey(ScratchKey), "a redirected stage reached the machine's real settings");
        }

        /// <summary>
        /// The volume starts at all of the game's own mix, and never goes below nothing or above it.
        /// </summary>
        /// <remarks>
        /// 100 is how the game sounded before there was a slider, so a machine that has never
        /// touched it hears no change. Kept or staged, an out-of-range value lands at the nearer end.
        /// </remarks>
        [Test]
        public void TheVolume_StartsFull_AndStaysBetweenSilenceAndFull()
        {
            Assert.AreEqual(100, GameAudio.DefaultVolume, "full volume is how the game sounded before the slider");
            Assert.AreEqual(GameAudio.DefaultVolume, GameAudio.Volume, "a fresh machine should start at full volume");

            var host = new GameObject("volume test");

            try
            {
                GameAudio audio = host.AddComponent<GameAudio>();

                audio.SetVolume(250, keep: true);
                Assert.AreEqual(100, GameAudio.Volume, "the volume went above full");

                audio.SetVolume(-5, keep: false);
                Assert.AreEqual(0, GameAudio.Volume, "the volume went below silence");

                audio.SetVolume(40, keep: false);
                Assert.AreEqual(40, GameAudio.Volume, "a staged volume did not read back");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
