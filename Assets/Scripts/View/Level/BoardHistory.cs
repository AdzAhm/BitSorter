using System.Collections.Generic;

namespace BitSorter.View
{
    /// <summary>What kind of edit produced a history entry.</summary>
    /// <remarks>
    /// Exists only so consecutive re-timings of one wire can be recognised and merged. Everything else
    /// is <see cref="BoardEditKind.Structural"/>, because everything else is already one edit per
    /// player action.
    /// </remarks>
    public enum BoardEditKind
    {
        /// <summary>A gate placed or removed, a wire drawn or deleted, or the board cleared.</summary>
        Structural,

        /// <summary>A wire's delay changed by one scroll notch.</summary>
        WireDelay,
    }

    /// <summary>Which edit an entry came from, and which wire when that matters.</summary>
    public readonly struct BoardEdit
    {
        public readonly BoardEditKind Kind;

        /// <summary>Position in the blueprint's wire list. Meaningful only for a delay change.</summary>
        public readonly int WireIndex;

        private BoardEdit(BoardEditKind kind, int wireIndex)
        {
            Kind = kind;
            WireIndex = wireIndex;
        }

        public static BoardEdit Structural => new BoardEdit(BoardEditKind.Structural, -1);

        public static BoardEdit WireDelay(int wireIndex) =>
            new BoardEdit(BoardEditKind.WireDelay, wireIndex);

        /// <summary>
        /// Whether a new edit should be absorbed into this one rather than pushed on top of it.
        /// </summary>
        /// <remarks>
        /// Only ever true for two delay changes on the same wire. Scrolling a wire from 1 to 4 fires
        /// three separate edits, and a player who then presses Ctrl+Z expects to land on 1 rather than
        /// on 3 -- so the run of them is one undo step.
        ///
        /// "Consecutive" means consecutive in the history, with no other edit between, rather than
        /// within some number of seconds. A clock would make this class non-deterministic and awkward to
        /// test for no gain: an edit in between already ends the run, and a player who re-times the same
        /// wire twice with nothing at all in between meant it as one adjustment.
        /// </remarks>
        public bool Absorbs(BoardEdit next) =>
            Kind == BoardEditKind.WireDelay &&
            next.Kind == BoardEditKind.WireDelay &&
            WireIndex == next.WireIndex;
    }

    /// <summary>
    /// The undo and redo stacks for one level's board.
    /// </summary>
    /// <remarks>
    /// Holds whole-board snapshots rather than inverse operations. See <see cref="BlueprintSnapshot"/>
    /// for why: a blueprint is two lists of value types, so copying one is cheap and correct by
    /// construction, whereas the inverses of "remove a gate" and "clear the board" are themselves
    /// snapshots of everything they took with them.
    ///
    /// Pure C# with no reference to the session, the runner or the scene, so the whole state machine --
    /// including the coalescing and the depth cap -- is reachable from Edit Mode.
    ///
    /// Not persisted, and cleared whenever a level is loaded. A snapshot restored into a different
    /// level's graph would be a board of gates on cells that level may not have.
    /// </remarks>
    public sealed class BoardHistory
    {
        /// <summary>
        /// How many steps back the player can go.
        /// </summary>
        /// <remarks>
        /// A cap rather than unbounded growth, because free play has no level switch to clear the
        /// history and a long session would otherwise keep every board it ever held. Fifty snapshots of
        /// a handful of structs each is nothing; the cap is about not growing forever, not about size.
        /// </remarks>
        public const int DefaultLimit = 50;

        private readonly struct Entry
        {
            public readonly BlueprintSnapshot Board;
            public readonly BoardEdit Edit;

            public Entry(BlueprintSnapshot board, BoardEdit edit)
            {
                Board = board;
                Edit = edit;
            }
        }

        private readonly List<Entry> _undo = new List<Entry>();
        private readonly List<Entry> _redo = new List<Entry>();
        private readonly int _limit;

        /// <summary>
        /// Whether the newest entry may still absorb a matching edit.
        /// </summary>
        /// <remarks>
        /// Cleared by an undo or a redo. Without this, undoing a delay change and then scrolling the
        /// same wire again could merge the new edit into an older entry that happened to name the same
        /// wire, so one Ctrl+Z would undo two separate adjustments.
        /// </remarks>
        private bool _openForAbsorption;

        public BoardHistory(int limit = DefaultLimit)
        {
            _limit = limit < 1 ? 1 : limit;
        }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        public int UndoDepth => _undo.Count;

        public int RedoDepth => _redo.Count;

        /// <summary>
        /// Records the board as it was <em>before</em> an edit that is about to happen.
        /// </summary>
        /// <remarks>
        /// Called only once an edit is known to be going ahead. Every mutating path on
        /// <see cref="LevelSession"/> validates first and returns early when it refuses, so a rejected
        /// placement never becomes a step the player has to press Ctrl+Z through.
        ///
        /// Pushing clears the redo stack: once the board takes a new direction, the branch that was
        /// undone is no longer reachable and keeping it would let redo jump to a board that never
        /// followed from this one.
        /// </remarks>
        public void Push(BlueprintSnapshot before, BoardEdit edit)
        {
            if (before == null)
                return;

            _redo.Clear();

            if (_openForAbsorption && _undo.Count > 0 && _undo[_undo.Count - 1].Edit.Absorbs(edit))
                return;

            _undo.Add(new Entry(before, edit));
            _openForAbsorption = true;

            // Oldest first, so the cap drops the least useful step rather than the most recent one.
            if (_undo.Count > _limit)
                _undo.RemoveAt(0);
        }

        /// <summary>
        /// Steps back one edit, handing back the board to restore.
        /// </summary>
        /// <param name="current">The board as it stands, which becomes the redo step.</param>
        public bool TryUndo(BlueprintSnapshot current, out BlueprintSnapshot restored)
        {
            restored = null;

            if (_undo.Count == 0 || current == null)
                return false;

            int top = _undo.Count - 1;
            Entry entry = _undo[top];
            _undo.RemoveAt(top);

            _redo.Add(new Entry(current, entry.Edit));
            _openForAbsorption = false;

            restored = entry.Board;
            return true;
        }

        /// <summary>Steps forward again, undoing an undo.</summary>
        /// <param name="current">The board as it stands, which becomes the undo step.</param>
        public bool TryRedo(BlueprintSnapshot current, out BlueprintSnapshot restored)
        {
            restored = null;

            if (_redo.Count == 0 || current == null)
                return false;

            int top = _redo.Count - 1;
            Entry entry = _redo[top];
            _redo.RemoveAt(top);

            _undo.Add(new Entry(current, entry.Edit));
            _openForAbsorption = false;

            restored = entry.Board;
            return true;
        }

        /// <summary>Forgets everything. Called whenever a level is loaded or adopted.</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _openForAbsorption = false;
        }
    }
}
