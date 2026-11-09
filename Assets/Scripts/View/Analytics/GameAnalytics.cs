using System;
using System.Collections.Generic;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UnityConsent;

namespace BitSorter.View
{
    /// <summary>
    /// Reports two events to Unity Analytics: an exercise was started, and an exercise was solved -- named <c>levelStarted</c>
    /// and <c>levelSolved</c> as the 4.x game's levels were, whose names the thirty exercises keep.
    /// </summary>
    /// <remarks>
    /// The question this exists to answer is where students give up, which the default session data
    /// cannot say. Starts against solves, per exercise, can.
    ///
    /// It is a static bootstrap rather than a MonoBehaviour on purpose.
    /// <see cref="Editor.HalfAdderDemoSceneBuilder"/> is the only authority on scene contents, so a
    /// component here would mean editing the builder, regenerating the scene and re-verifying its
    /// serialised references -- a lot of risk for something that needs no inspector fields and no
    /// transform. The game ships one scene, so a hook after that scene loads runs exactly once.
    ///
    /// Events are queued until data collection has actually started. Initialising Unity Services is
    /// asynchronous, and the simulator can open on an exercise before it has -- so without the queue
    /// that exercise's start would be dropped, and that is one of the numbers most worth having.
    ///
    /// Both events are flushed as they happen. Volume is a handful per session, and the alternative
    /// is losing precisely the interesting ones: a player who gives up closes the tab, taking any
    /// unsent buffer with them.
    /// </remarks>
    public static class GameAnalytics
    {
        private const string ConsentKey = "bitsorter.analytics";

        /// <summary>
        /// Whether the player allows reporting. On unless they turn it off.
        /// </summary>
        /// <remarks>
        /// In <see cref="Preferences"/> for the same reason the frame cap is: it describes this
        /// machine, not the player's circuits, and copying a save to another computer should not
        /// carry it along.
        ///
        /// The consent module itself does not persist anything -- it has no save, load or clear --
        /// so the answer has to be remembered here and re-applied on every launch.
        /// </remarks>
        public static bool Reporting
        {
            get => Preferences.GetInt(ConsentKey, 1) != 0;
            private set => Preferences.SetInt(ConsentKey, value ? 1 : 0);
        }

        /// <summary>Turns reporting on or off and tells the SDK, for the DATA switch in Settings.</summary>
        /// <remarks>
        /// Takes effect immediately in both directions. Granting mid-session starts collection --
        /// the SDK begins on the grant, not only at startup -- and denying stops it.
        ///
        /// Denying also drops anything still queued for the services to start. Left there, it was
        /// sent the moment they came up: a promise not to collect, kept for everything but what had
        /// already been collected.
        /// </remarks>
        public static void SetReporting(bool reporting)
        {
            Reporting = reporting;

            if (!reporting)
                Pending.Clear();

            ApplyConsent();
        }

        private static readonly List<KeyValuePair<string, string>> Pending =
            new List<KeyValuePair<string, string>>();

        /// <summary>Which event-and-level pairs have been sent this session.</summary>
        /// <remarks>
        /// A session, not an install. Nothing is persisted: somebody coming back tomorrow is a new
        /// visit to the level and worth counting again, and remembering it would mean writing
        /// something about their progress to disk for the sake of a metric.
        ///
        /// Cleared in <see cref="Boot"/> beside the pending queue, for the same reason it is:
        /// statics survive entering play mode when domain reload is switched off.
        /// </remarks>
        private static readonly HashSet<string> Sent = new HashSet<string>(StringComparer.Ordinal);

        private static bool _collecting;
        private static bool _unavailable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static async void Boot()
        {
            // Statics survive entering play mode when domain reload is switched off, so clear before
            // subscribing rather than trusting a fresh start.
            Unsubscribe();
            Pending.Clear();
            Sent.Clear();
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

        // The workbench reports an exercise itself (ExerciseStarted, ExerciseSolved). The 4.x game's level session and
        // tracker were listened to here, and the scenes a change of look loads with them, until Part 16a deleted the game.
        private static void Subscribe() => Application.quitting += OnQuitting;

        private static void Unsubscribe() => Application.quitting -= OnQuitting;

        /// <summary>
        /// An exercise opened on the workbench (Part 15d), reported as a level started under its id, once a session: the same
        /// event and the same parameter the 4.x game's levels were reported with, so the data reads on from theirs.
        /// </summary>
        public static void ExerciseStarted(string id) => Record(AnalyticsRules.LevelStarted, id);

        /// <summary>An exercise solved by CHECK (Part 15d), reported as a level solved under its id, once a session.</summary>
        public static void ExerciseSolved(string id) => Record(AnalyticsRules.LevelSolved, id);

        /// <summary>
        /// Every report the workbench asks for -- the event's name and the exercise's id -- as it asks, before any rule decides
        /// whether it is sent: for the tests to hold the workbench to asking only for what happened, with reporting off as
        /// every fixture has it. It sends nothing, and changes nothing of what is sent or when.
        /// </summary>
        public static event Action<string, string> Asked;

        private static void Record(string eventName, string levelName)
        {
            Asked?.Invoke(eventName, levelName);

            // Every reason to stay quiet lives in AnalyticsRules, where it can be tested without a
            // scene. This method is left with the part that needs one: the queue and the SDK.
            string key = AnalyticsRules.SessionKey(eventName, levelName);

            if (!AnalyticsRules.ShouldReport(levelName, Reporting, _unavailable, Sent.Contains(key)))
                return;

            // Recorded before the queue rather than after the send: an event queued while services
            // are still starting must not be counted again by a level load that arrives first.
            Sent.Add(key);

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
                var payload = new CustomEvent(eventName) { { AnalyticsRules.LevelNameParameter, levelName } };

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
