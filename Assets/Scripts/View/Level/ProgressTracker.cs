using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The save, loaded once as the scene starts, and starting the exercises over.
    /// </summary>
    /// <remarks>
    /// A thin shell around <see cref="ProgressStore"/>, which is a plain class so it can be tested against a scratch file
    /// without a scene. The workbench keeps its documents, solves and hints through the store itself; this owns the store
    /// and says when the exercises have been started over. It saved and restored the 4.x game's boards and recorded its
    /// solves until Part 16a deleted the game.
    /// </remarks>
    public sealed class ProgressTracker : MonoBehaviour
    {
        [Tooltip("Leave empty for the real save. Set to test against a scratch file.")]
        [SerializeField] private string _pathOverride;

        private ProgressStore _store;

        /// <summary>The store, loaded. Null only before Awake has run.</summary>
        public ProgressStore Store => _store;

        /// <summary>
        /// Raised once <see cref="ResetProgress"/> has forgotten the exercises, for anything that keeps its own copy of
        /// something the save said: the workbench lets an exercise open go.
        /// </summary>
        public event Action ProgressReset;

        private void Awake()
        {
            _store = new ProgressStore(
                string.IsNullOrWhiteSpace(_pathOverride) ? ProgressStore.DefaultPath : _pathOverride);

            _store.Load();

            if (_store.LastError != null)
            {
                // A warning rather than an error: the simulator is fine, the record is not.
                Debug.LogWarning($"BitSorter: could not read progress -- {_store.LastError}");
            }
        }

        /// <summary>
        /// Starts the exercises over (Part 15g): every solved exercise, its saved answer and best, the milestones and the
        /// first-time hints are forgotten, and the student's circuits are kept -- MY CIRCUIT and every named circuit
        /// (<see cref="ProgressStore.ForgetTheExercises"/>). Sound and data settings are not progress and live in
        /// PlayerPrefs, so they stay as they are.
        /// </summary>
        /// <returns>False if there is no save to reset, which only happens before Awake.</returns>
        public bool ResetProgress()
        {
            if (_store == null)
                return false;

            var exercises = new HashSet<string>(StringComparer.Ordinal);
            foreach (BitSorter.Board.Exercise exercise in ExerciseCatalog.Run)
                exercises.Add(exercise.Id);
            _store.ForgetTheExercises(exercises);

            ProgressReset?.Invoke();
            return true;
        }
    }
}
