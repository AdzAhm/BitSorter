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

        /// <summary>The store, loaded. Null only before Awake has run.</summary>
        public ProgressStore Store => _store;

        /// <summary>
        /// Whether the last solve beat the gate record, for the win panel to read.
        /// </summary>
        /// <remarks>
        /// Set inside the call that settles the run, so it is already current on whichever frame
        /// anything first sees the pass.
        /// </remarks>
        public bool BeatGateRecord { get; private set; }

        /// <inheritdoc cref="BeatGateRecord"/>
        public bool BeatLatencyRecord { get; private set; }

        /// <summary>
        /// Raised with the level's file name each time a level is solved.
        /// </summary>
        /// <remarks>
        /// Exists so anything else that cares about a solve does not have to re-derive it, including
        /// the rule that the tutorial and free play are not levels. Fires on every solve, including
        /// re-solves of a level already recorded.
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
            _session.RunEnded += OnRunEnded;
        }

        private void OnDisable()
        {
            if (_session == null)
                return;

            _session.LevelUnloading -= SaveBoard;
            _session.LevelLoaded -= RestoreBoard;
            _session.RunEnded -= OnRunEnded;
        }

        /// <summary>Quitting is the other way a board goes missing.</summary>
        private void OnApplicationQuit()
        {
            if (_session != null && _session.IsLoaded)
                SaveBoard(_session.LevelName);
        }

        /// <summary>
        /// Records a solve in the same call that settles the run.
        /// </summary>
        /// <remarks>
        /// This used to be an Update watching for the state to become Passed, and the win panel
        /// watched for the same thing in an Update of its own. Unity does not define which of the two
        /// runs first, so the panel could present before the solve was recorded and show the previous
        /// record. Recording here means the record is current before anything can see the pass.
        /// </remarks>
        private void OnRunEnded(RunState state)
        {
            if (state == RunState.Passed && _store != null && _session.IsLoaded)
                RecordSolve();
        }

        // -----------------------------------------------------------------
        // Boards
        // -----------------------------------------------------------------

        private void SaveBoard(string levelName)
        {
            if (_store == null || string.IsNullOrEmpty(levelName))
                return;

            // The tutorial always starts empty. Its steps are predicates over the board, so a
            // restored circuit would satisfy all six the instant it loaded and the whole thing would
            // jump to its ending. Free play is the opposite and deliberately does keep its board,
            // which is why this names the tutorial rather than asking IsOffCatalogue.
            if (levelName == TutorialLevel.Key)
                return;

            _store.SaveBoard(levelName, BoardSerializer.ToSaved(levelName, _session.Blueprint));
        }

        private void RestoreBoard(LevelDefinition level)
        {
            if (_store == null || level == null)
                return;

            // The tutorial always starts from an empty board -- see SaveBoard. Guarded on the way in
            // as well as on the way out, because a save written before that guard existed still
            // carries a finished tutorial circuit, and restoring one would satisfy all six steps the
            // instant it loaded.
            if (_session.LevelName == TutorialLevel.Key)
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

            // Not a level in the run, so it is not a level that can be completed. The tutorial is
            // graded -- it ends on a real pass, which is the point -- and without this it would land
            // in the completed list, inflate CompletedCount, and take a personal best beside levels
            // it is not one of.
            if (LevelCatalog.IsOffCatalogue(level))
                return;

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
