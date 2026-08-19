using System;
using System.Collections.Generic;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UnityConsent;

namespace BitSorter.View
{
    /// <summary>
    /// Reports two events to Unity Analytics: a level was started, and a level was solved.
    /// </summary>
    /// <remarks>
    /// The question this exists to answer is where players give up, which the default session data
    /// cannot say. Starts against solves, per level, can.
    ///
    /// It is a static bootstrap rather than a MonoBehaviour on purpose.
    /// <see cref="Editor.HalfAdderDemoSceneBuilder"/> is the only authority on scene contents, so a
    /// component here would mean editing the builder, regenerating the scene and re-verifying its
    /// serialised references -- a lot of risk for something that needs no inspector fields and no
    /// transform. The game ships one scene, so a hook after that scene loads runs exactly once.
    ///
    /// Events are queued until data collection has actually started. Initialising Unity Services is
    /// asynchronous, but the first level loads in <see cref="LevelSession"/>'s Start, which happens
    /// first -- so without the queue the opening level's start would be dropped, and that is one of
    /// the numbers most worth having.
    ///
    /// Both events are flushed as they happen. Volume is a handful per session, and the alternative
    /// is losing precisely the interesting ones: a player who gives up closes the tab, taking any
    /// unsent buffer with them.
    /// </remarks>
    public static class GameAnalytics
    {
        private const string LevelStarted = "levelStarted";
        private const string LevelSolved = "levelSolved";
        private const string LevelNameParameter = "levelName";

        private const string ConsentKey = "bitsorter.analytics";

        /// <summary>
        /// Whether the player allows reporting. On unless they turn it off.
        /// </summary>
        /// <remarks>
        /// In PlayerPrefs for the same reason the mute setting is: it describes this machine, not
        /// the player's circuits, and copying a save to another computer should not carry it along.
        ///
        /// The consent module itself does not persist anything -- it has no save, load or clear --
        /// so the answer has to be remembered here and re-applied on every launch.
        /// </remarks>
        public static bool Reporting
        {
            get => PlayerPrefs.GetInt(ConsentKey, 1) != 0;
            private set
            {
                PlayerPrefs.SetInt(ConsentKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Turns reporting on or off and tells the SDK, for the main menu's Data item.</summary>
        /// <remarks>
        /// Takes effect immediately in both directions. Granting mid-session starts collection --
        /// the SDK begins on the grant, not only at startup -- and denying stops it.
        /// </remarks>
        public static void SetReporting(bool reporting)
        {
            Reporting = reporting;
            ApplyConsent();
        }

        private static readonly List<KeyValuePair<string, string>> Pending =
            new List<KeyValuePair<string, string>>();

        private static bool _collecting;
        private static bool _unavailable;
        private static LevelSession _session;
        private static ProgressTracker _tracker;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static async void Boot()
        {
            // Statics survive entering play mode when domain reload is switched off, so clear before
            // subscribing rather than trusting a fresh start.
            Unsubscribe();
            Pending.Clear();
            _collecting = false;
            _unavailable = false;

            Subscribe();

            try
            {
                // Consent before initialising. The SDK starts collecting during InitializeAsync when
                // consent is already granted, so setting it first means no gap at startup.
                ApplyConsent();

                await UnityServices.InitializeAsync();

                _collecting = true;
                Flush();
            }
            catch (Exception e)
            {
                // No linked project, offline, or the browser is blocking it. None of that is the
                // player's problem, so it warns and the game carries on without reporting.
                _unavailable = true;
                Pending.Clear();
                Debug.LogWarning($"BitSorter: analytics unavailable -- {e.Message}");
            }
        }

        /// <summary>
        /// Tells the consent framework what the player has chosen.
        /// </summary>
        /// <remarks>
        /// This replaced StartDataCollection, which Unity deprecated in 6.2. The two cannot be mixed:
        /// the SDK throws if the old calls are used once consent has been set this way, which is why
        /// there is no StartDataCollection left anywhere.
        ///
        /// It also means an explicit Denied rather than leaving it alone. The default state is
        /// Unspecified and collects nothing, so simply dropping the old call would have turned
        /// reporting off without anyone deciding to.
        /// </remarks>
        private static void ApplyConsent()
        {
            try
            {
                EndUserConsent.SetConsentState(new ConsentState
                {
                    AnalyticsIntent = Reporting ? ConsentStatus.Granted : ConsentStatus.Denied,
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"BitSorter: could not set analytics consent -- {e.Message}");
            }
        }

        private static void Subscribe()
        {
            if (_session == null) _session = UnityEngine.Object.FindFirstObjectByType<LevelSession>();
            if (_tracker == null) _tracker = UnityEngine.Object.FindFirstObjectByType<ProgressTracker>();

            if (_session != null)
                _session.LevelLoaded += OnLevelLoaded;

            if (_tracker != null)
                _tracker.LevelSolved += OnLevelSolved;

            Application.quitting += OnQuitting;
        }

        private static void Unsubscribe()
        {
            if (_session != null)
                _session.LevelLoaded -= OnLevelLoaded;

            if (_tracker != null)
                _tracker.LevelSolved -= OnLevelSolved;

            Application.quitting -= OnQuitting;

            _session = null;
            _tracker = null;
        }

        // LevelLoaded carries the definition; the file name is the stable identity and the key
        // progress is stored under, so that is what gets reported rather than the display title.
        private static void OnLevelLoaded(LevelDefinition level)
        {
            Record(LevelStarted, _session != null ? _session.LevelName : null);
        }

        private static void OnLevelSolved(string levelName)
        {
            Record(LevelSolved, levelName);
        }

        private static void Record(string eventName, string levelName)
        {
            if (_unavailable || string.IsNullOrEmpty(levelName))
                return;

            // Checked here as well as at the consent framework, so a player who has turned reporting
            // off does not even accumulate a queue of events waiting for an upload that must not
            // happen.
            if (!Reporting)
                return;

            // Free play is not a level and must not look like one. It can never be solved, so a
            // levelStarted from here would be a start with no solve after it -- indistinguishable
            // from someone giving up, in the one measurement these events exist to make.
            if (levelName == SandboxLevel.Key)
                return;

            if (!_collecting)
            {
                Pending.Add(new KeyValuePair<string, string>(eventName, levelName));
                return;
            }

            Send(eventName, levelName);
        }

        private static void Flush()
        {
            foreach (KeyValuePair<string, string> queued in Pending)
                Send(queued.Key, queued.Value);

            Pending.Clear();
        }

        private static void Send(string eventName, string levelName)
        {
            try
            {
                var payload = new CustomEvent(eventName) { { LevelNameParameter, levelName } };

                AnalyticsService.Instance.RecordEvent(payload);
                AnalyticsService.Instance.Flush();
            }
            catch (Exception e)
            {
                // A rejected event must never interrupt play. The usual cause is the event not being
                // registered in the Unity Cloud dashboard, which no amount of retrying will fix.
                Debug.LogWarning($"BitSorter: could not report '{eventName}' -- {e.Message}");
            }
        }

        private static void OnQuitting()
        {
            if (!_collecting)
                return;

            try
            {
                AnalyticsService.Instance.Flush();
            }
            catch (Exception)
            {
                // Shutting down; there is nowhere useful left to report a failure to.
            }
        }
    }
}
