using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The undo and redo stacks: what one step is, and when a run of edits collapses into one.
    /// </summary>
    /// <remarks>
    /// Snapshots rather than inverse operations, so none of these tests describe an edit being
    /// reversed -- they describe a board being put back. That is the whole argument for the design: an
    /// undo of "remove a gate" restores the wires that went with it without anything having to know
    /// that removal takes wires.
    /// </remarks>
    public class BoardHistoryTests
    {
        private static readonly Vector2Int A = new Vector2Int(0, 0);
        private static readonly Vector2Int B = new Vector2Int(1, 0);
        private static readonly Vector2Int C = new Vector2Int(2, 0);

        private static CircuitBlueprint Board(params Vector2Int[] cells)
        {
            var blueprint = new CircuitBlueprint();

            foreach (Vector2Int cell in cells)
                blueprint.Place(cell, GateKind.And);

            return blueprint;
        }

        private static void Wire(CircuitBlueprint blueprint, Vector2Int from, Vector2Int to, int delay = 1)
        {
            blueprint.AddWire(new BlueprintWire(
                new CellPort(from, false, 0), new CellPort(to, true, 0), delay));
        }

        // -----------------------------------------------------------------
        // One step
        // -----------------------------------------------------------------

        [Test]
        public void AFreshHistoryHasNothingToStepThrough()
        {
            var history = new BoardHistory();

            Assert.IsFalse(history.CanUndo);
            Assert.IsFalse(history.CanRedo);

            Assert.IsFalse(history.TryUndo(Board().Snapshot(), out BlueprintSnapshot back));
            Assert.IsNull(back);

            Assert.IsFalse(history.TryRedo(Board().Snapshot(), out BlueprintSnapshot forward));
            Assert.IsNull(forward);
        }

        [Test]
        public void AnEditCanBeSteppedBack()
        {
            var history = new BoardHistory();

            CircuitBlueprint board = Board(A);
            history.Push(board.Snapshot(), BoardEdit.Structural);

            board.Place(B, GateKind.Not);
            Assert.AreEqual(2, board.Placements.Count);

            Assert.IsTrue(history.TryUndo(board.Snapshot(), out BlueprintSnapshot restored));
            board.Restore(restored);

            Assert.AreEqual(1, board.Placements.Count);
            Assert.AreEqual(A, board.Placements[0].Cell);
        }

        [Test]
        public void UndoingAGateRemovalBringsItsWiresBack()
        {
            // The reason this is snapshots and not inverses. Nothing here knows that removing a gate
            // takes its wires with it; the board that had them is simply put back.
            var history = new BoardHistory();

            CircuitBlueprint board = Board(A, B);
            Wire(board, A, B);
            Wire(board, B, C);

            history.Push(board.Snapshot(), BoardEdit.Structural);

            board.RemoveAt(B);
            Assert.AreEqual(0, board.Wires.Count, "removing B should have taken both wires");

            Assert.IsTrue(history.TryUndo(board.Snapshot(), out BlueprintSnapshot restored));
            board.Restore(restored);

            Assert.AreEqual(2, board.Placements.Count);
            Assert.AreEqual(2, board.Wires.Count);
        }

        [Test]
        public void UndoThenRedoReturnsTheEditedBoard()
        {
            var history = new BoardHistory();

            CircuitBlueprint board = Board(A);
            history.Push(board.Snapshot(), BoardEdit.Structural);
            board.Place(B, GateKind.Not);

            history.TryUndo(board.Snapshot(), out BlueprintSnapshot back);
            board.Restore(back);
            Assert.AreEqual(1, board.Placements.Count);

            Assert.IsTrue(history.TryRedo(board.Snapshot(), out BlueprintSnapshot forward));
            board.Restore(forward);

            Assert.AreEqual(2, board.Placements.Count);
            Assert.AreEqual(B, board.Placements[1].Cell);
        }

        [Test]
        public void ANewEditDiscardsTheRedoBranch()
        {
            // Once the board takes a different direction the undone branch no longer follows from it,
            // and a redo into it would jump to a board that never existed on this path.
            var history = new BoardHistory();

            CircuitBlueprint board = Board(A);
            history.Push(board.Snapshot(), BoardEdit.Structural);
            board.Place(B, GateKind.Not);

            history.TryUndo(board.Snapshot(), out BlueprintSnapshot back);
            board.Restore(back);
            Assert.IsTrue(history.CanRedo);

            history.Push(board.Snapshot(), BoardEdit.Structural);
            board.Place(C, GateKind.Xor);

            Assert.IsFalse(history.CanRedo, "the abandoned branch should be gone");
        }

        // -----------------------------------------------------------------
        // Coalescing a run of delay changes
        // -----------------------------------------------------------------

        [Test]
        public void ConsecutiveDelayChangesOnOneWireAreOneStep()
        {
            // Scrolling a wire from 1 to 4 is three edits and one undo. A player who presses Ctrl+Z
            // after that expects to land on 1, not on 3.
            var history = new BoardHistory();

            CircuitBlueprint board = Board(A, B);
            Wire(board, A, B);

            for (int delay = 2; delay <= 4; delay++)
            {
                history.Push(board.Snapshot(), BoardEdit.WireDelay(0));
                board.SetDelayAt(0, delay);
            }

            Assert.AreEqual(1, history.UndoDepth, "the run should have collapsed into one step");

            history.TryUndo(board.Snapshot(), out BlueprintSnapshot restored);
            board.Restore(restored);

            Assert.AreEqual(1, board.Wires[0].Delay, "one undo should return the whole run");
        }

        [Test]
        public void DelayChangesOnDifferentWiresAreSeparateSteps()
        {
            var history = new BoardHistory();

            CircuitBlueprint board = Board(A, B, C);
            Wire(board, A, B);
            Wire(board, B, C);

            history.Push(board.Snapshot(), BoardEdit.WireDelay(0));
            board.SetDelayAt(0, 2);

            history.Push(board.Snapshot(), BoardEdit.WireDelay(1));
            board.SetDelayAt(1, 2);

            Assert.AreEqual(2, history.UndoDepth, "two wires means two steps");
        }

        [Test]
        public void AStructuralEditBetweenDelayChangesEndsTheRun()
        {
            // "Consecutive" means with nothing in between. Placing a gate mid-scroll breaks the run,
            // so the second batch of notches is its own step.
            var history = new BoardHistory();

            CircuitBlueprint board = Board(A, B);
            Wire(board, A, B);

            history.Push(board.Snapshot(), BoardEdit.WireDelay(0));
            board.SetDelayAt(0, 2);

            history.Push(board.Snapshot(), BoardEdit.Structural);
            board.Place(C, GateKind.Not);

            history.Push(board.Snapshot(), BoardEdit.WireDelay(0));
            board.SetDelayAt(0, 3);

            Assert.AreEqual(3, history.UndoDepth);
        }

        [Test]
        public void AnUndoEndsTheRunOfCoalescing()
        {
            // Without this, undoing a delay change and then scrolling the same wire again would merge
            // the new edit into an older entry that happened to name the same wire, and a single
            // Ctrl+Z would swallow two separate adjustments.
            var history = new BoardHistory();

            CircuitBlueprint board = Board(A, B);
            Wire(board, A, B);

            history.Push(board.Snapshot(), BoardEdit.WireDelay(0));
            board.SetDelayAt(0, 2);

            history.Push(board.Snapshot(), BoardEdit.WireDelay(0));
            board.SetDelayAt(0, 3);
            Assert.AreEqual(1, history.UndoDepth);

            history.TryUndo(board.Snapshot(), out BlueprintSnapshot restored);
            board.Restore(restored);

            history.Push(board.Snapshot(), BoardEdit.WireDelay(0));
            board.SetDelayAt(0, 2);

            Assert.AreEqual(1, history.UndoDepth,
                "the new edit must be its own step rather than joining the one already there");
        }

        [Test]
        public void StructuralEditsNeverCoalesce()
        {
            var history = new BoardHistory();
            CircuitBlueprint board = Board();

            history.Push(board.Snapshot(), BoardEdit.Structural);
            board.Place(A, GateKind.And);

            history.Push(board.Snapshot(), BoardEdit.Structural);
            board.Place(B, GateKind.And);

            Assert.AreEqual(2, history.UndoDepth, "two placements are two undo steps");
        }

        // -----------------------------------------------------------------
        // Housekeeping
        // -----------------------------------------------------------------

        [Test]
        public void TheHistoryIsCappedAndDropsTheOldestStep()
        {
            var history = new BoardHistory(limit: 3);
            var board = new CircuitBlueprint();

            for (int i = 0; i < 10; i++)
            {
                history.Push(board.Snapshot(), BoardEdit.Structural);
                board.Place(new Vector2Int(i, 0), GateKind.And);
            }

            Assert.AreEqual(3, history.UndoDepth);

            // The three most recent steps survived, so the oldest reachable board still has the seven
            // placements that were there when the cap started dropping entries.
            history.TryUndo(board.Snapshot(), out BlueprintSnapshot third);
            history.TryUndo(board.Snapshot(), out BlueprintSnapshot second);
            history.TryUndo(board.Snapshot(), out BlueprintSnapshot oldest);

            Assert.AreEqual(9, third.PlacementCount);
            Assert.AreEqual(8, second.PlacementCount);

            Assert.AreEqual(7, oldest.PlacementCount);
            Assert.IsFalse(history.CanUndo);
        }

        [Test]
        public void ClearForgetsBothDirections()
        {
            var history = new BoardHistory();
            CircuitBlueprint board = Board(A);

            history.Push(board.Snapshot(), BoardEdit.Structural);
            board.Place(B, GateKind.Not);

            history.TryUndo(board.Snapshot(), out BlueprintSnapshot back);
            Assert.IsNotNull(back);
            Assert.IsTrue(history.CanRedo);

            history.Clear();

            Assert.IsFalse(history.CanUndo);
            Assert.IsFalse(history.CanRedo);
        }

        [Test]
        public void ANullSnapshotIsNotRecorded()
        {
            var history = new BoardHistory();

            history.Push(null, BoardEdit.Structural);

            Assert.IsFalse(history.CanUndo);
        }

        // -----------------------------------------------------------------
        // The snapshot itself
        // -----------------------------------------------------------------

        [Test]
        public void ASnapshotIsIndependentOfTheBoardItCameFrom()
        {
            // The property that makes the whole design safe. A blueprint is two lists of readonly
            // structs, so a snapshot shares nothing with the live board and cannot be edited from
            // underneath by later play.
            CircuitBlueprint board = Board(A);
            BlueprintSnapshot taken = board.Snapshot();

            board.Place(B, GateKind.Not);
            Wire(board, A, B);

            Assert.AreEqual(1, taken.PlacementCount);
            Assert.AreEqual(0, taken.WireCount);
        }

        [Test]
        public void RestoringPutsTheBoardBackExactly()
        {
            CircuitBlueprint board = Board(A, B);
            Wire(board, A, B, delay: 3);

            BlueprintSnapshot taken = board.Snapshot();

            board.Clear();
            Assert.IsTrue(board.IsEmpty);

            board.Restore(taken);

            Assert.AreEqual(2, board.Placements.Count);
            Assert.AreEqual(1, board.Wires.Count);
            Assert.AreEqual(3, board.Wires[0].Delay, "the wire's delay is part of the board");
        }

        [Test]
        public void RestoringNullLeavesTheBoardAlone()
        {
            CircuitBlueprint board = Board(A);

            board.Restore(null);

            Assert.AreEqual(1, board.Placements.Count, "an empty history must not empty the board");
        }

        [Test]
        public void MatchingComparesOrderAsWellAsContents()
        {
            // Order is part of the contract: a rebuild assigns node ids by list position, so the same
            // gates in a different order are not the same board.
            BlueprintSnapshot forward = Board(A, B).Snapshot();
            BlueprintSnapshot backward = Board(B, A).Snapshot();

            Assert.IsTrue(forward.Matches(Board(A, B).Snapshot()));
            Assert.IsFalse(forward.Matches(backward));
            Assert.IsFalse(forward.Matches(null));
        }

        [Test]
        public void MatchingNoticesADelayChange()
        {
            CircuitBlueprint one = Board(A, B);
            Wire(one, A, B, delay: 1);

            CircuitBlueprint two = Board(A, B);
            Wire(two, A, B, delay: 2);

            Assert.IsFalse(one.Snapshot().Matches(two.Snapshot()));
        }
    }
}
