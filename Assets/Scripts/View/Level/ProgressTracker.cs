using System;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Remembers what the player did: which levels are solved, what is on each board, and the best
    /// each has been solved.
    /// </summary>
    /// <remarks>
    /// A thin shell around <see cref="ProgressStore"/>, which is a plain class so it can be tested
    /// against a scratch file without a scene. Everything interesting lives there; this decides
    /// *when* to record.
    ///
    /// Boards are saved on the way out of a level and on quit, not on every edit. A file write per
    /// click would be a lot of writing to solve a problem nobody has, and the two moments a board
    /// can actually be lost are exactly those.
    /// </remarks>
    public sealed class ProgressTracker : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Leave empty for the real save. Set to test against a scratch file.")]
        [SerializeField] private string _pathOverride;

        private ProgressStore _store;
        private RunState _state = RunState.Editing;

        /// <summary>The store, loaded. Null only before Awake has run.</summary>
        public ProgressStore Store => _store;

        /// <summary>Set on the frame a personal best is beaten, for the win panel to read.</summary>
        public bool BeatGateRecord { get; private set; }

        /// <inheritdoc cref="BeatGateRecord"/>
        public bool BeatLatencyRecord { get; private set; }

        /// <summary>
        /// Raised with the level's file name each time a level is solved.
        /// </summary>
        /// <remarks>
        /// Exists so anything else that cares about a solve does not have to re-derive it. Detecting
        /// a solve means watching for the frame <see cref="LevelSession.State"/> becomes
        /// <see cref="RunState.Passed"/>, and a second copy of that check is a second thing to drift.
        /// Fires on every solve, including re-solves of a level already recorded.
        /// </remarks>
        public event Action<string> LevelSolved;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();

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

        private void OnEnable()
        {
            if (_session == null)
                return;

            _session.LevelUnloading += SaveBoard;
            _session.LevelLoaded += RestoreBoard;
        }

        private void OnDisable()
        {
            if (_session == null)
                return;

            _session.LevelUnloading -= SaveBoard;
            _session.LevelLoaded -= RestoreBoard;
        }

        /// <summary>Quitting is the other way a board goes missing.</summary>
        private void OnApplicationQuit()
        {
            if (_session != null && _session.IsLoaded)
                SaveBoard(_session.LevelName);
        }

        private void Update()
        {
            if (_session == null || _store == null || !_session.IsLoaded)
                return;

            RunState now = _session.State;

            if (now != _state && now == RunState.Passed)
                RecordSolve();

            _state = now;
        }

        // -----------------------------------------------------------------
        // Boards
        // -----------------------------------------------------------------

        private void SaveBoard(string levelName)
        {
            if (_store == null || string.IsNullOrEmpty(levelName))
                return;

            _store.SaveBoard(levelName, BoardSerializer.ToSaved(levelName, _session.Blueprint));
        }

        private void RestoreBoard(LevelDefinition level)
        {
            if (_store == null || level == null)
                return;

            SavedBoard saved = _store.BoardFor(_session.LevelName);

            if (saved == null)
                return;

            Vector2Int extents = _runner != null ? _runner.HalfExtents : new Vector2Int(4, 2);
            int dropped = BoardSerializer.Restore(saved, level, _session.Blueprint, extents);

            if (dropped > 0)
            {
                // Loud, because the player is about to see a board that is not the one they left.
                // Silently restoring a partial circuit would read as the game having eaten their work.
                Debug.LogWarning(
                    $"BitSorter: {dropped} saved item(s) on '{_session.LevelName}' no longer fit the " +
                    "level and were dropped.");
            }

            // The session already rebuilt from an empty blueprint before announcing the level, so it
            // has to rebuild again now that there is something in it.
            _session.ResetBoard();
        }

        // -----------------------------------------------------------------
        // Records
        // -----------------------------------------------------------------

        private void RecordSolve()
        {
            string level = _session.LevelName;

            _store.MarkComplete(level);

            int gates = 0;
            foreach (LevelBudgetEntry entry in _session.Level.Budget)
                gates += _session.PlacedCountOf(entry.Kind);

            int latency = MeasuredLatency();

            _store.RecordBest(level, gates, latency,
                out bool gatesBeaten, out bool latencyBeaten);

            BeatGateRecord = gatesBeaten;
            BeatLatencyRecord = latencyBeaten;

            // Saved immediately rather than at the next level switch. A player who solves something
            // and then closes the game has done the one thing most worth remembering.
            SaveBoard(level);

            // Last, so a throwing subscriber cannot cost the player their record.
            LevelSolved?.Invoke(level);
        }

        /// <summary>
        /// The worst source-to-sink latency the winning run showed, in ticks.
        /// </summary>
        /// <remarks>
        /// The same figure LevelGrader measures against maxLatency, worked out the same way: sources
        /// emit vector v on tick v, so a bit's latency is the tick it landed minus the vector it
        /// belongs to. Read off the run that just passed, so it describes the circuit the player
        /// actually built.
        /// </remarks>
        private int MeasuredLatency()
        {
            if (_runner == null || !_runner.IsReady)
                return 0;

            SimulationView view = _runner.View;
            int worst = 0;

            foreach (LevelExpectation expectation in _session.Level.Expectations)
            {
                if (!_runner.FixtureNodeIds.TryGetValue(expectation.SinkId, out int nodeId))
                    continue;

                if (nodeId < 0 || nodeId >= view.NodeCount || !(view.GetNode(nodeId) is SinkNode sink))
                    continue;

                for (int k = 0; k < expectation.Expected.Count && k < sink.Received.Count; k++)
                    worst = Mathf.Max(worst, sink.Received[k].Tick - expectation.Expected[k].Vector);
            }

            return worst;
        }

        /// <summary>Whether a level has ever been solved. False before Awake.</summary>
        public bool IsComplete(string levelName) => _store != null && _store.IsComplete(levelName);

        public int BestGates(string levelName) => _store != null ? _store.BestGates(levelName) : 0;

        public int BestLatency(string levelName) => _store != null ? _store.BestLatency(levelName) : 0;
    }
}
