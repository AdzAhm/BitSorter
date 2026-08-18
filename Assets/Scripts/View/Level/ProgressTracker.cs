using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Watches for a level being solved and writes it down.
    /// </summary>
    /// <remarks>
    /// A thin shell around <see cref="ProgressStore"/>, which is a plain class so it can be tested
    /// against a scratch file without a scene. Everything interesting lives there; this only decides
    /// *when* to record, by polling the run state the same way every other component polls.
    ///
    /// Records the moment a level passes rather than waiting for the player to move on. A run that
    /// passes and is then reset would otherwise be forgotten, which is the wrong lesson entirely --
    /// solving it happened.
    /// </remarks>
    public sealed class ProgressTracker : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;

        [Tooltip("Leave empty for the real save. Set to test against a scratch file.")]
        [SerializeField] private string _pathOverride;

        private ProgressStore _store;
        private RunState _state = RunState.Editing;

        /// <summary>The store, loaded. Null only before Awake has run.</summary>
        public ProgressStore Store => _store;

        private void Awake()
        {
            if (_session == null)
                _session = FindFirstObjectByType<LevelSession>();

            _store = new ProgressStore(
                string.IsNullOrWhiteSpace(_pathOverride) ? ProgressStore.DefaultPath : _pathOverride);

            _store.Load();

            if (_store.LastError != null)
            {
                // A warning rather than an error: the game is fine, the record is not, and the player
                // loses nothing they can see except some ticks in the level list.
                Debug.LogWarning($"BitSorter: could not read progress -- {_store.LastError}");
            }
        }

        private void Update()
        {
            if (_session == null || _store == null || !_session.IsLoaded)
                return;

            RunState now = _session.State;

            if (now != _state && now == RunState.Passed)
                _store.MarkComplete(_session.LevelName);

            _state = now;
        }

        /// <summary>Whether a level has ever been solved. False before Awake.</summary>
        public bool IsComplete(string levelName) => _store != null && _store.IsComplete(levelName);
    }
}
