using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Watches for the first time a player meets a mechanic, and asks <see cref="HintBanner"/> to
    /// explain it. Once ever, per save.
    /// </summary>
    /// <remarks>
    /// Fired by what happens rather than by which level is loaded. The levels already teach through
    /// their goal and hint, written before the player has seen the thing they describe; this is for
    /// the moment of surprise, where a stalled gate or a vanished bit otherwise reads as the game
    /// being broken.
    ///
    /// Everything is polled, like every other readout in this project. The three triggers live in
    /// three different places -- the graph, the corruption count and the level's budget -- and a
    /// component that subscribed would need all three to announce themselves.
    /// </remarks>
    public sealed class FirstTimeHints : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private ProgressTracker _progress;
        [SerializeField] private HintBanner _banner;

        [Tooltip("Consecutive ticks a gate must sit stalled mid-run. See HintRules.StallTicks.")]
        [SerializeField] private int _stallTicks = HintRules.StallTicks;

        private readonly StallClock _stalls = new StallClock();

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_banner == null) _banner = FindFirstObjectByType<HintBanner>();
        }

        private void OnEnable()
        {
            if (_session != null)
                _session.LevelLoaded += OnLevelLoaded;
        }

        private void OnDisable()
        {
            if (_session != null)
                _session.LevelLoaded -= OnLevelLoaded;
        }

        /// <summary>
        /// A new level is a new board, so no stall carries across. The banner comes down too: a hint
        /// about a gate that is no longer there has nothing to point at.
        /// </summary>
        private void OnLevelLoaded(LevelDefinition level)
        {
            _stalls.Clear();

            if (_banner != null)
                _banner.Hide();
        }

        private void Update()
        {
            if (_session == null || _runner == null || _banner == null || !_runner.IsReady)
                return;

            ProgressStore store = _progress != null ? _progress.Store : null;

            if (store == null || !_session.IsLoaded)
                return;

            // Kept up to date even while a hint is on screen, or a stall that began during one would
            // restart its clock the moment the hint came down.
            int longestStall = _stalls.Observe(_runner.View, _runner.View.CurrentTick);

            // One at a time. A second hint arriving mid-sentence would cut the first one off, and
            // whichever lost the race would be marked seen without having been read.
            if (_banner.IsShowing || UiModal.AnyOpen)
                return;

            // Collision first: it is the most alarming of the three, and the only one where the
            // player has already lost something.
            if (TryShow(store, HintRules.Collision, _runner.View.CorruptedCount > 0))
                return;

            if (TryShow(store, HintRules.Stalled, HintRules.StallEarnsAHint(
                    _runner.IsIdle(), _stalls.AnyStalled, longestStall, _stallTicks)))
            {
                return;
            }

            // Not an event but a verb the player has no way to discover: nothing on screen says a
            // wire can be scrolled. Offered on the first board that budgets delay, while there is
            // still a board to try it on.
            //
            // Waits for a wire to exist. Firing on an empty board told the player to scroll
            // something that was not there yet -- an instruction they could not follow and would
            // have forgotten by the time they could.
            TryShow(store, HintRules.WireDelay,
                _session.Level.HasDelayBudget
                && _session.State == RunState.Editing
                && _session.Blueprint.Wires.Count > 0);
        }

        /// <summary>
        /// Shows a hint if it is due and has never been shown. Returns whether it went up.
        /// </summary>
        /// <remarks>
        /// The store is asked to record it *before* it is shown, and its answer is the gate: marking
        /// is idempotent and returns false for an id already recorded, so two frames in a row cannot
        /// both decide they were first.
        /// </remarks>
        private bool TryShow(ProgressStore store, string id, bool due)
        {
            if (!due || store.HasSeenHint(id) || !store.MarkHintSeen(id))
                return false;

            _banner.Show(HintRules.TextFor(id));
            return true;
        }
    }
}
