using System;
using System.Collections.Generic;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;

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
                await UnityServices.InitializeAsync();
                AnalyticsService.Instance.StartDataCollection();
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
